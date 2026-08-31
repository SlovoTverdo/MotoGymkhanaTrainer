using MotoGymkhanaTrainer.ExerciseEditor;
using MotoGymkhanaTrainer.Tracks;
using MotoGymkhanaTrainer.VenueEditor;

namespace MotoGymkhanaTrainer.TrackEditor;

/// <summary>Severity-tagged compilation message shown before Viewer export.</summary>
public sealed record TrackCompilationDiagnostic(string Message);

/// <summary>Origin of the control points in a derived transition preview.</summary>
public enum TransitionSourceMode
{
    Automatic,
    Override,
}

/// <summary>
/// Runtime-only transition geometry. It is rebuilt from current instance endpoints
/// plus an optional persisted override and is never serialized as Track Project data.
/// </summary>
public sealed class CompiledTransition
{
    public required string TransitionId { get; init; }
    public required string FromInstanceId { get; init; }
    public required string ToInstanceId { get; init; }
    public required Point2Dto Start { get; init; }
    public required Point2Dto Control1 { get; init; }
    public required Point2Dto Control2 { get; init; }
    public required Point2Dto End { get; init; }
    public required TransitionSourceMode SourceMode { get; init; }

    /// <summary>Creates the ordinary cubicBezier segment consumed by Viewer.</summary>
    public TrajectorySegmentDto ToTrajectorySegment() => new()
    {
        Id = TransitionId,
        Type = "cubicBezier",
        Start = Copy(Start),
        Control1 = Copy(Control1),
        Control2 = Copy(Control2),
        End = Copy(End),
    };

    private static Point2Dto Copy(Point2Dto point) => new() { X = point.X, Y = point.Y };
}

/// <summary>
/// Derived compilation output. Snapshot and transition preview are never written
/// into Track Project v3; they can always be rebuilt from project + dependencies.
/// </summary>
public sealed class TrackCompilationResult
{
    public TrackSnapshotDto? Snapshot { get; init; }
    public IReadOnlyList<CompiledTransition> Transitions { get; init; } = [];
    public IReadOnlyList<TrackCompilationDiagnostic> Errors { get; init; } = [];
    public IReadOnlyList<TrackCompilationDiagnostic> Warnings { get; init; } = [];
    public bool CanExport => Snapshot is not null && Errors.Count == 0;
}

/// <summary>Compiles Track Project v3 plus resolved Venue and Exercises into Track v5.</summary>
public static class TrackCompiler
{
    public const float MinTransitionHandleLength = 0.5f;
    public const float MaxTransitionHandleLength = 15.0f;
    private const float PointTolerance = 0.001f;
    private const float TangentTolerance = 0.00001f;
    private const float ShortTransitionMeters = 1.0f;
    private const float LongTransitionMeters = 45.0f;
    private const float LongManualHandleMeters = 30.0f;
    private const int TransitionSamples = 32;

    private sealed class CompiledInstance
    {
        public required TrackProjectInstanceDto Instance { get; init; }
        public required ExerciseDefinitionDto Definition { get; init; }
        public required Point2Dto[] Bounds { get; init; }
        public required ConeDto[] Cones { get; init; }
        public required MarkingDto[] Markings { get; init; }
        public required TrajectorySegmentDto[] Segments { get; init; }
        public required Point2Dto Entry { get; init; }
        public required Point2Dto Exit { get; init; }
        public required Point2Dto EntryTangent { get; init; }
        public required Point2Dto ExitTangent { get; init; }
    }

    /// <summary>
    /// Builds a new world snapshot without mutating either persisted source. A
    /// snapshot is withheld when any blocking diagnostic exists, while valid
    /// transitions remain available for editor preview.
    /// </summary>
    public static TrackCompilationResult Compile(TrackProjectDocument document)
    {
        var errors = new List<TrackCompilationDiagnostic>();
        var warnings = new List<TrackCompilationDiagnostic>();
        var compiled = new List<CompiledInstance>();
        var cones = new List<ConeDto>();
        var markings = new List<MarkingDto>();
        var elements = new List<ElementDto>();
        var globalSegments = new List<TrajectorySegmentDto>();
        var transitions = new List<CompiledTransition>();
        VenueDefinitionDto venue = document.Venue.Definition;
        VenueAreaDto area = venue.Area;
        var venueObjects = new List<VenueObjectSnapshotDto>();

        if (string.IsNullOrWhiteSpace(document.Project.Track.Id) ||
            string.IsNullOrWhiteSpace(document.Project.Track.Name))
            errors.Add(Error("Track id and name must be non-empty."));
        if (!float.IsFinite(area.Width) || area.Width <= 0 ||
            !float.IsFinite(area.Length) || area.Length <= 0)
            errors.Add(Error("Track area width and length must be finite positive numbers."));
        if (document.Project.Instances.Length == 0)
        {
            errors.Add(Error("Export requires at least one ExerciseInstance."));
        }

        CompileVenue(document.Venue, venueObjects, cones, markings, errors, warnings);

        IReadOnlyDictionary<(string From, string To), TransitionOverrideDto> applicableOverrides =
            ValidateTransitionOverrides(document, errors, warnings);

        foreach (TrackProjectInstanceDto instance in document.Project.Instances)
        {
            CompileInstance(document, instance, compiled, errors, warnings);
        }

        foreach (CompiledInstance item in compiled)
        {
            cones.AddRange(item.Cones);
            markings.AddRange(item.Markings);
            elements.Add(new ElementDto
            {
                InstanceId = item.Instance.InstanceId,
                DefinitionId = item.Definition.Exercise.Id,
                ExercisePath = item.Instance.ExercisePath,
                Position = Copy(item.Instance.Position),
                RotationDeg = item.Instance.RotationDeg,
                Scale = Copy(item.Instance.Scale),
            });
        }

        var compiledById = new Dictionary<string, CompiledInstance>(StringComparer.Ordinal);
        foreach (CompiledInstance item in compiled)
        {
            if (!compiledById.TryAdd(item.Instance.InstanceId, item))
                errors.Add(Error($"Duplicate instanceId '{item.Instance.InstanceId}'."));
        }

        // Walk the original Route Order rather than the resolved subset. This
        // prevents a misleading A->C preview when unresolved instance B is between
        // them; transitions only exist for truly adjacent project entries.
        for (int index = 0; index < document.Project.Instances.Length; index++)
        {
            TrackProjectInstanceDto source = document.Project.Instances[index];
            if (!compiledById.TryGetValue(source.InstanceId, out CompiledInstance? current))
                continue;
            globalSegments.AddRange(current.Segments);
            if (index + 1 >= document.Project.Instances.Length ||
                !compiledById.TryGetValue(
                    document.Project.Instances[index + 1].InstanceId,
                    out CompiledInstance? next))
            {
                continue;
            }

            applicableOverrides.TryGetValue(
                (current.Instance.InstanceId, next.Instance.InstanceId),
                out TransitionOverrideDto? transitionOverride);
            CompiledTransition? transition = BuildTransition(
                current, next, transitionOverride, errors, warnings);
            if (transition is not null)
            {
                transitions.Add(transition);
                TrajectorySegmentDto segment = transition.ToTrajectorySegment();
                globalSegments.Add(segment);
                WarnForTransitionArea(segment, area, warnings);
            }
        }

        ValidateBounds(compiled, venue.Objects, area, warnings);
        ValidateUniqueIds(venueObjects, elements, cones, markings, globalSegments, errors);
        ValidateGlobalContinuity(globalSegments, errors);

        TrackSnapshotDto candidate = new()
        {
            FormatVersion = 5,
            Track = new TrackMetadataDto
            {
                Id = document.Project.Track.Id,
                Name = document.Project.Track.Name,
            },
            Venue = new VenueMetadataSnapshotDto
            {
                Id = venue.Venue.Id,
                Name = venue.Venue.Name,
            },
            Area = new AreaDto
            {
                Width = area.Width,
                Length = area.Length,
            },
            Panorama = new PanoramaSnapshotDto
            {
                Enabled = venue.Panorama.Enabled,
                TexturePath = venue.Panorama.TexturePath,
                RotationDeg = venue.Panorama.RotationDeg,
                EnergyMultiplier = venue.Panorama.EnergyMultiplier,
            },
            VenueObjects = [.. venueObjects],
            Elements = [.. elements],
            Cones = [.. cones],
            Markings = [.. markings],
            Trajectory = new TrajectoryDto { Segments = [.. globalSegments] },
            Checkpoints = [],
        };

        if (errors.Count == 0)
        {
            try
            {
                _ = TrackExportStore.Serialize(candidate);
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
            {
                errors.Add(Error($"Compiled snapshot cannot be serialized for Viewer: {exception.Message}"));
            }
        }

        return new TrackCompilationResult
        {
            Snapshot = errors.Count == 0 ? candidate : null,
            Transitions = transitions,
            Errors = errors,
            Warnings = warnings,
        };
    }

    /// <summary>Copies Venue world data without applying an Exercise transform.</summary>
    private static void CompileVenue(
        ResolvedVenue resolved,
        ICollection<VenueObjectSnapshotDto> objects,
        ICollection<ConeDto> cones,
        ICollection<MarkingDto> markings,
        ICollection<TrackCompilationDiagnostic> errors,
        ICollection<TrackCompilationDiagnostic> warnings)
    {
        VenueDefinitionDto venue = resolved.Definition;
        if (venue.Panorama.Enabled &&
            (string.IsNullOrWhiteSpace(venue.Panorama.TexturePath) || !resolved.PanoramaTextureResolved))
        {
            errors.Add(Error(
                $"Venue '{venue.Venue.Id}' panorama is enabled but texturePath " +
                $"'{venue.Panorama.TexturePath}' is not loadable as Texture2D."));
        }

        foreach (VenueObjectInstanceDto item in venue.Objects)
        {
            bool assetResolved = resolved.IsObjectResolved(item.ObjectId);
            string context = $"Venue object '{item.ObjectId}' assetPath '{item.AssetPath}'";
            if (!assetResolved && item.VisibleInViewer)
            {
                errors.Add(Error($"{context} is visibleInViewer but is not loadable as PackedScene."));
            }
            else if (!assetResolved)
            {
                warnings.Add(Warning($"{context} is hidden and unresolved; it remains in the exported snapshot."));
            }

            objects.Add(new VenueObjectSnapshotDto
            {
                Id = $"venue--object--{item.ObjectId}",
                Name = item.Name,
                AssetPath = item.AssetPath,
                ObjectType = item.ObjectType,
                AssetId = item.AssetId,
                Position = Copy(item.Position),
                Elevation = item.Elevation,
                RotationDeg = item.RotationDeg,
                Scale = new Point3Dto { X = item.Scale.X, Y = item.Scale.Y, Z = item.Scale.Z },
                Footprint = new AreaDto
                {
                    Width = item.Footprint.Width,
                    Length = item.Footprint.Length,
                    CenterX = item.Footprint.CenterX,
                    CenterY = item.Footprint.CenterY,
                },
                CollisionEnabled = item.CollisionEnabled,
                CollisionMode = item.CollisionMode,
                VisibleInViewer = item.VisibleInViewer,
            });
        }

        var footprints = venue.Objects.Select(item =>
            (Item: item, Polygon: VenueGeometry.TransformFootprint(item))).ToArray();
        for (int index = 0; index < footprints.Length; index++)
        {
            if (footprints[index].Polygon.Any(point => VenueGeometry.IsOutsideArea(point, venue.Area)))
            {
                warnings.Add(Warning(
                    $"Venue object '{footprints[index].Item.ObjectId}' assetPath " +
                    $"'{footprints[index].Item.AssetPath}' footprint extends outside Venue area."));
            }

            for (int other = index + 1; other < footprints.Length; other++)
            {
                if (VenueGeometry.Overlaps(footprints[index].Polygon, footprints[other].Polygon))
                {
                    warnings.Add(Warning(
                        $"Venue object footprints '{footprints[index].Item.ObjectId}' and " +
                        $"'{footprints[other].Item.ObjectId}' overlap."));
                }
            }
        }

        foreach (ConeDto cone in venue.Cones)
        {
            if (VenueGeometry.IsOutsideArea(cone.Position, venue.Area))
                warnings.Add(Warning($"Venue cone '{cone.Id}' is outside Venue area."));
            cones.Add(new ConeDto
            {
                Id = $"venue--cone--{cone.Id}",
                Position = Copy(cone.Position),
                Color = cone.Color,
                Type = cone.Type,
            });
        }

        foreach (MarkingDto marking in venue.Markings)
        {
            if (PathBoundsCalculator.Calculate(marking.Path, marking.WidthMeters)
                .IsOutside(venue.Area.Width, venue.Area.Length))
                warnings.Add(Warning($"Venue marking '{marking.Id}' extends outside Venue area."));
            markings.Add(new MarkingDto
            {
                Id = $"venue--marking--{marking.Id}",
                Path = PathEditing.CopyPath(marking.Path),
                Color = marking.Color,
                WidthMeters = marking.WidthMeters,
                Style = marking.Style,
                VisibleInViewer = marking.VisibleInViewer,
            });
        }
    }

    private static void CompileInstance(
        TrackProjectDocument document,
        TrackProjectInstanceDto instance,
        ICollection<CompiledInstance> output,
        ICollection<TrackCompilationDiagnostic> errors,
        ICollection<TrackCompilationDiagnostic> warnings)
    {
        ExerciseDefinitionDto? definition = document.FindDefinition(instance.InstanceId);
        if (definition is null)
        {
            errors.Add(Error($"Instance '{instance.InstanceId}' is unresolved ({instance.ExercisePath})."));
            return;
        }

        if (definition.FormatVersion != 3 || definition.Exercise is null ||
            string.IsNullOrWhiteSpace(definition.Exercise.Id))
        {
            errors.Add(Error($"Instance '{instance.InstanceId}' has an unsupported or incomplete Exercise Definition."));
            return;
        }

        if (!IsFinite(instance.Position) || !float.IsFinite(instance.RotationDeg) ||
            !IsFinite(instance.Scale) || MathF.Abs(instance.Scale.X) < TangentTolerance ||
            MathF.Abs(instance.Scale.Y) < TangentTolerance)
        {
            errors.Add(Error($"Instance '{instance.InstanceId}' has an invalid transform or zero scale."));
            return;
        }

        if (!TryValidateTrajectory(instance.InstanceId, definition, errors,
                out Point2Dto localEntryTangent, out Point2Dto localExitTangent))
        {
            return;
        }


        if (!ValidateObjects(instance.InstanceId, definition, errors))
            return;

        Point2Dto entryTangent = ExerciseInstanceGeometry.TransformDirection(
            localEntryTangent, instance.RotationDeg, instance.Scale);
        Point2Dto exitTangent = ExerciseInstanceGeometry.TransformDirection(
            localExitTangent, instance.RotationDeg, instance.Scale);
        if (!TryNormalize(entryTangent, out entryTangent) || !TryNormalize(exitTangent, out exitTangent))
        {
            errors.Add(Error($"Instance '{instance.InstanceId}' has a tangent that becomes undefined after transform."));
            return;
        }

        Point2Dto Transform(Point2Dto point) => ExerciseInstanceGeometry.TransformPoint(
            point, instance.Position, instance.RotationDeg, instance.Scale);

        ConeDto[] cones = definition.Cones.Select(cone => new ConeDto
        {
            Id = ExportId(instance.InstanceId, cone.Id),
            Position = Transform(cone.Position),
            Color = cone.Color,
            Type = cone.Type,
        }).ToArray();
        MarkingDto[] markings = definition.Markings.Select(marking => new MarkingDto
        {
            Id = ExportId(instance.InstanceId, marking.Id),
            Path = PathTransformService.Transform(marking.Path, Transform),
            Color = marking.Color,
            WidthMeters = marking.WidthMeters,
            Style = marking.Style,
            VisibleInViewer = marking.VisibleInViewer,
        }).ToArray();
        TrajectorySegmentDto[] segments = definition.Trajectory.Segments
            .Select(segment => TransformSegment(instance.InstanceId, segment, Transform)).ToArray();
        Point2Dto[] bounds = ExerciseInstanceGeometry.TransformBounds(
            definition.Bounds.Width, definition.Bounds.Length,
            instance.Position, instance.RotationDeg, instance.Scale);

        if (cones.Any(cone => !IsFinite(cone.Position)) ||
            markings.Any(marking => !PathProducesFiniteGeometry(marking.Path)) ||
            segments.Any(segment => EnumerateGeometry(segment).Any(point => !IsFinite(point))) ||
            bounds.Any(point => !IsFinite(point)))
        {
            errors.Add(Error($"Instance '{instance.InstanceId}' produces NaN or Infinity world geometry."));
            return;
        }

        if (GeometryOutsideArea(cones, markings, segments, document.Venue.Definition.Area))
        {
            warnings.Add(Warning($"Instance '{instance.InstanceId}' has geometry outside the track area."));
        }

        output.Add(new CompiledInstance
        {
            Instance = instance,
            Definition = definition,
            Bounds = bounds,
            Cones = cones,
            Markings = markings,
            Segments = segments,
            Entry = Transform(definition.EntryPoint),
            Exit = Transform(definition.ExitPoint),
            EntryTangent = entryTangent,
            ExitTangent = exitTangent,
        });
    }

    private static bool ValidateObjects(
        string instanceId,
        ExerciseDefinitionDto definition,
        ICollection<TrackCompilationDiagnostic> errors)
    {
        bool valid = true;
        if (definition.Cones is null || definition.Markings is null)
        {
            errors.Add(Error($"Instance '{instanceId}' cones and markings must be arrays."));
            return false;
        }

        foreach (ConeDto cone in definition.Cones)
        {
            if (string.IsNullOrWhiteSpace(cone.Id) || !IsFinite(cone.Position) ||
                cone.Type != "standard" || string.IsNullOrWhiteSpace(cone.Color))
            {
                errors.Add(Error($"Instance '{instanceId}' has invalid cone '{cone?.Id}'."));
                valid = false;
            }
        }

        foreach (MarkingDto marking in definition.Markings)
        {
            bool colorValid = MarkingGeometry.TryNormalizeColor(
                marking.Color, allowLegacyNames: false, out string canonical) && canonical == marking.Color;
            bool pathValid;
            try
            {
                PathValidator.ValidateOrThrow(marking.Path, $"marking '{marking.Id}'.path");
                pathValid = true;
            }
            catch (InvalidDataException)
            {
                pathValid = false;
            }
            if (string.IsNullOrWhiteSpace(marking.Id) || !pathValid || !colorValid || !float.IsFinite(marking.WidthMeters) ||
                marking.WidthMeters <= 0 || !MarkingGeometry.IsSupportedStyle(marking.Style))
            {
                errors.Add(Error($"Instance '{instanceId}' has invalid marking '{marking?.Id}'."));
                valid = false;
            }
        }

        return valid;
    }

    private static bool TryValidateTrajectory(
        string instanceId,
        ExerciseDefinitionDto definition,
        ICollection<TrackCompilationDiagnostic> errors,
        out Point2Dto entryTangent,
        out Point2Dto exitTangent)
    {
        entryTangent = new Point2Dto();
        exitTangent = new Point2Dto();
        TrajectorySegmentDto[]? segments = definition.Trajectory?.Segments;
        if (segments is null || segments.Length == 0)
        {
            errors.Add(Error($"Instance '{instanceId}' has no trajectory."));
            return false;
        }

        Point2Dto? previousEnd = null;
        for (int index = 0; index < segments.Length; index++)
        {
            if (!TrySegmentEndpointsAndTangents(segments[index], out Point2Dto start, out Point2Dto end,
                    out Point2Dto segmentEntry, out Point2Dto segmentExit))
            {
                errors.Add(Error($"Instance '{instanceId}' has invalid trajectory segment '{segments[index]?.Id ?? index.ToString()}'."));
                return false;
            }

            if (previousEnd is not null && Distance(previousEnd, start) > PointTolerance)
            {
                errors.Add(Error($"Instance '{instanceId}' trajectory is discontinuous before segment '{segments[index].Id}'."));
                return false;
            }

            if (index == 0) entryTangent = segmentEntry;
            if (index == segments.Length - 1) exitTangent = segmentExit;
            previousEnd = end;
        }

        Point2Dto first = SegmentStart(segments[0]);
        Point2Dto last = SegmentEnd(segments[^1]);
        if (Distance(definition.EntryPoint, first) > PointTolerance ||
            Distance(definition.ExitPoint, last) > PointTolerance)
        {
            errors.Add(Error($"Instance '{instanceId}' entryPoint/exitPoint do not match trajectory endpoints."));
            return false;
        }

        if (!TryNormalize(entryTangent, out _) || !TryNormalize(exitTangent, out _))
        {
            errors.Add(Error($"Instance '{instanceId}' trajectory has an undefined entry or exit tangent."));
            return false;
        }

        return true;
    }

    private static bool TrySegmentEndpointsAndTangents(
        TrajectorySegmentDto? segment,
        out Point2Dto start,
        out Point2Dto end,
        out Point2Dto entryTangent,
        out Point2Dto exitTangent)
    {
        start = end = entryTangent = exitTangent = new Point2Dto();
        if (segment is null || string.IsNullOrWhiteSpace(segment.Id)) return false;
        if (segment.Type == "polyline")
        {
            if (segment.Points is null || segment.Points.Length < 2 || segment.Points.Any(point => !IsFinite(point)))
                return false;
            start = segment.Points[0];
            end = segment.Points[^1];
            if (!TryFirstDifference(segment.Points, fromStart: true, out entryTangent) ||
                !TryFirstDifference(segment.Points, fromStart: false, out exitTangent)) return false;
            return true;
        }

        if (segment.Type != "cubicBezier" || segment.Start is null || segment.Control1 is null ||
            segment.Control2 is null || segment.End is null ||
            EnumerateGeometry(segment).Any(point => !IsFinite(point))) return false;
        start = segment.Start;
        end = segment.End;
        entryTangent = FirstNonZeroDifference(start, segment.Control1, segment.Control2, end);
        exitTangent = FirstNonZeroDifference(end, segment.Control2, segment.Control1, start, reverse: true);
        return TryNormalize(entryTangent, out _) && TryNormalize(exitTangent, out _);
    }

    private static CompiledTransition? BuildTransition(
        CompiledInstance from,
        CompiledInstance to,
        TransitionOverrideDto? transitionOverride,
        ICollection<TrackCompilationDiagnostic> errors,
        ICollection<TrackCompilationDiagnostic> warnings)
    {
        float distance = Distance(from.Exit, to.Entry);
        string pair = $"'{from.Instance.InstanceId}' -> '{to.Instance.InstanceId}'";
        if (!float.IsFinite(distance))
        {
            errors.Add(Error($"Transition {pair} has non-finite endpoints."));
            return null;
        }

        if (!TryNormalize(from.ExitTangent, out Point2Dto exitTangent) ||
            !TryNormalize(to.EntryTangent, out Point2Dto entryTangent))
        {
            errors.Add(Error($"Transition {pair} cannot determine a valid tangent."));
            return null;
        }

        if (distance <= PointTolerance && transitionOverride is null)
        {
            if (Dot(exitTangent, entryTangent) < 0.5f)
                warnings.Add(Warning($"Very short transition {pair} has conflicting directions."));
            return null;
        }

        if (distance < ShortTransitionMeters)
            warnings.Add(Warning($"Transition {pair} is very short ({distance:F2} m)."));
        if (distance > LongTransitionMeters)
            warnings.Add(Warning($"Transition {pair} is unusually long ({distance:F1} m)."));
        if (Dot(exitTangent, entryTangent) < -0.25f)
            warnings.Add(Warning($"Transition {pair} has a sharp direction change."));

        string transitionId = $"transition--{from.Instance.InstanceId}--{to.Instance.InstanceId}";
        Point2Dto control1;
        Point2Dto control2;
        TransitionSourceMode sourceMode;
        if (transitionOverride is not null)
        {
            /*
             * Endpoints always follow the latest compiled exercises. Persisted
             * offsets remain unchanged through move/rotate/scale and translate the
             * manual handles together with their respective endpoint.
             */
            control1 = Add(from.Exit, transitionOverride.Control1Offset);
            control2 = Add(to.Entry, transitionOverride.Control2Offset);
            sourceMode = TransitionSourceMode.Override;
            if (!IsFinite(control1) || !IsFinite(control2))
            {
                errors.Add(Error($"Transition {pair} override produces non-finite geometry."));
                return null;
            }

            WarnForManualHandles(
                transitionId, from.Exit, control1, control2, to.Entry,
                exitTangent, entryTangent, warnings);
        }
        else
        {
            float handle = Math.Clamp(distance / 3.0f, MinTransitionHandleLength, MaxTransitionHandleLength);
            control1 = Add(from.Exit, Multiply(exitTangent, handle));
            control2 = Add(to.Entry, Multiply(entryTangent, -handle));
            sourceMode = TransitionSourceMode.Automatic;
        }

        return new CompiledTransition
        {
            TransitionId = transitionId,
            FromInstanceId = from.Instance.InstanceId,
            ToInstanceId = to.Instance.InstanceId,
            Start = Copy(from.Exit),
            Control1 = control1,
            Control2 = control2,
            End = Copy(to.Entry),
            SourceMode = sourceMode,
        };
    }

    private static IReadOnlyDictionary<(string From, string To), TransitionOverrideDto>
        ValidateTransitionOverrides(
            TrackProjectDocument document,
            ICollection<TrackCompilationDiagnostic> errors,
            ICollection<TrackCompilationDiagnostic> warnings)
    {
        var adjacentPairs = new HashSet<(string From, string To)>();
        for (int index = 0; index + 1 < document.Project.Instances.Length; index++)
        {
            adjacentPairs.Add((document.Project.Instances[index].InstanceId,
                document.Project.Instances[index + 1].InstanceId));
        }

        var applicable = new Dictionary<(string From, string To), TransitionOverrideDto>();
        var transitionIds = new HashSet<string>(StringComparer.Ordinal);
        var seenPairs = new HashSet<(string From, string To)>();
        foreach (TransitionOverrideDto item in document.Project.TransitionOverrides ?? [])
        {
            bool valid = true;
            if (string.IsNullOrWhiteSpace(item.TransitionId) || !transitionIds.Add(item.TransitionId))
            {
                errors.Add(Error($"Duplicate or empty TransitionOverride transitionId '{item.TransitionId}'."));
                valid = false;
            }

            var pair = (item.FromInstanceId, item.ToInstanceId);
            if (string.IsNullOrWhiteSpace(pair.FromInstanceId) ||
                string.IsNullOrWhiteSpace(pair.ToInstanceId) ||
                !seenPairs.Add(pair))
            {
                errors.Add(Error(
                    $"Duplicate or empty TransitionOverride pair '{pair.FromInstanceId}' -> '{pair.ToInstanceId}'."));
                valid = false;
            }

            if (!IsFinite(item.Control1Offset) || !IsFinite(item.Control2Offset))
            {
                errors.Add(Error($"TransitionOverride '{item.TransitionId}' contains a non-finite offset."));
                valid = false;
            }

            if (!adjacentPairs.Contains(pair))
            {
                warnings.Add(Warning(
                    $"TransitionOverride '{item.TransitionId}' is orphaned: " +
                    $"'{item.FromInstanceId}' does not directly precede '{item.ToInstanceId}'."));
                continue;
            }

            if (valid)
            {
                applicable.Add(pair, item);
            }
        }

        return applicable;
    }

    private static void WarnForManualHandles(
        string transitionId,
        Point2Dto start,
        Point2Dto control1,
        Point2Dto control2,
        Point2Dto end,
        Point2Dto exitTangent,
        Point2Dto entryTangent,
        ICollection<TrackCompilationDiagnostic> warnings)
    {
        float firstLength = Distance(start, control1);
        float secondLength = Distance(end, control2);
        if (firstLength > LongManualHandleMeters || secondLength > LongManualHandleMeters)
        {
            warnings.Add(Warning(
                $"Manual transition '{transitionId}' has unusually long handles " +
                $"({firstLength:F1} m, {secondLength:F1} m)."));
        }

        if ((TryNormalize(Subtract(control1, start), out Point2Dto firstDirection) &&
             Dot(firstDirection, exitTangent) < -0.25f) ||
            (TryNormalize(Subtract(end, control2), out Point2Dto secondDirection) &&
             Dot(secondDirection, entryTangent) < -0.25f))
        {
            warnings.Add(Warning($"Manual transition '{transitionId}' has an unusually sharp handle direction."));
        }
    }

    private static TrajectorySegmentDto TransformSegment(
        string instanceId,
        TrajectorySegmentDto source,
        Func<Point2Dto, Point2Dto> transform)
    {
        if (source.Type == "polyline")
        {
            return new TrajectorySegmentDto
            {
                Id = ExportId(instanceId, source.Id),
                Type = source.Type,
                Points = source.Points!.Select(transform).ToArray(),
            };
        }

        return new TrajectorySegmentDto
        {
            Id = ExportId(instanceId, source.Id),
            Type = source.Type,
            Start = transform(source.Start!),
            Control1 = transform(source.Control1!),
            Control2 = transform(source.Control2!),
            End = transform(source.End!),
        };
    }

    private static void ValidateBounds(
        IReadOnlyList<CompiledInstance> instances,
        IReadOnlyList<VenueObjectInstanceDto> venueObjects,
        VenueAreaDto area,
        ICollection<TrackCompilationDiagnostic> warnings)
    {
        var footprints = venueObjects.Select(item =>
            (Item: item, Polygon: VenueGeometry.TransformFootprint(item))).ToArray();
        for (int index = 0; index < instances.Count; index++)
        {
            if (ExerciseInstanceGeometry.IsOutsideArea(instances[index].Bounds, area.Width, area.Length))
                warnings.Add(Warning($"Instance '{instances[index].Instance.InstanceId}' bounds extend outside Venue area."));
            foreach ((VenueObjectInstanceDto item, Point2Dto[] polygon) in footprints)
            {
                if (VenueGeometry.Overlaps(instances[index].Bounds, polygon))
                {
                    warnings.Add(Warning(
                        $"Instance '{instances[index].Instance.InstanceId}' bounds intersect Venue object " +
                        $"'{item.ObjectId}' assetPath '{item.AssetPath}'."));
                }
            }
            for (int other = index + 1; other < instances.Count; other++)
            {
                if (BoundsOverlap(instances[index].Bounds, instances[other].Bounds))
                    warnings.Add(Warning($"Bounds of '{instances[index].Instance.InstanceId}' and '{instances[other].Instance.InstanceId}' overlap."));
            }
        }
    }

    private static void WarnForTransitionArea(
        TrajectorySegmentDto transition,
        VenueAreaDto area,
        ICollection<TrackCompilationDiagnostic> warnings)
    {
        if (TrajectoryGeometry.SampleCubicBezier(transition, TransitionSamples)
            .Any(point => IsOutsideArea(point, area)))
        {
            warnings.Add(Warning($"Transition '{transition.Id}' extends outside the track area."));
        }
    }

    private static void ValidateUniqueIds(
        IReadOnlyList<VenueObjectSnapshotDto> objects,
        IReadOnlyList<ElementDto> elements,
        IReadOnlyList<ConeDto> cones,
        IReadOnlyList<MarkingDto> markings,
        IReadOnlyList<TrajectorySegmentDto> segments,
        ICollection<TrackCompilationDiagnostic> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        AddDuplicateErrors(objects.Select(item => item.Id), "Venue object", seen, errors);
        AddDuplicateErrors(elements.Select(item => item.InstanceId), "element", seen, errors);
        AddDuplicateErrors(cones.Select(item => item.Id), "cone", seen, errors);
        AddDuplicateErrors(markings.Select(item => item.Id), "marking", seen, errors);
        AddDuplicateErrors(segments.Select(item => item.Id), "trajectory segment", seen, errors);
    }

    private static void AddDuplicateErrors(
        IEnumerable<string> ids,
        string kind,
        ISet<string> seen,
        ICollection<TrackCompilationDiagnostic> errors)
    {
        foreach (string id in ids)
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                errors.Add(Error($"Duplicate or empty exported {kind} id '{id}'."));
    }

    private static void ValidateGlobalContinuity(
        IReadOnlyList<TrajectorySegmentDto> segments,
        ICollection<TrackCompilationDiagnostic> errors)
    {
        for (int index = 1; index < segments.Count; index++)
        {
            float gap = Distance(SegmentEnd(segments[index - 1]), SegmentStart(segments[index]));
            if (!float.IsFinite(gap) || gap > PointTolerance)
                errors.Add(Error($"Global trajectory is discontinuous between '{segments[index - 1].Id}' and '{segments[index].Id}' (gap {gap:F3} m)."));
        }
    }

    private static bool GeometryOutsideArea(
        IEnumerable<ConeDto> cones,
        IEnumerable<MarkingDto> markings,
        IEnumerable<TrajectorySegmentDto> segments,
        VenueAreaDto area) =>
        cones.Any(cone => IsOutsideArea(cone.Position, area)) ||
        markings.Any(marking => PathBoundsCalculator.Calculate(marking.Path, marking.WidthMeters)
            .IsOutside(area.Width, area.Length)) ||
        segments.SelectMany(EnumerateRenderedGeometry).Any(point => IsOutsideArea(point, area));

    private static bool PathProducesFiniteGeometry(PathDefinition path)
    {
        try
        {
            PathValidator.ValidateOrThrow(path, "path");
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool BoundsOverlap(IReadOnlyList<Point2Dto> a, IReadOnlyList<Point2Dto> b)
    {
        // The separating-axis test uses normals from both transformed rectangles.
        // Unlike an AABB warning it does not report two rotated, separated bounds
        // merely because their enclosing axis-aligned boxes overlap.
        foreach (IReadOnlyList<Point2Dto> polygon in new[] { a, b })
        {
            for (int index = 0; index < polygon.Count; index++)
            {
                Point2Dto edge = Subtract(polygon[(index + 1) % polygon.Count], polygon[index]);
                Point2Dto axis = new() { X = -edge.Y, Y = edge.X };
                float aMin = a.Min(point => Dot(point, axis));
                float aMax = a.Max(point => Dot(point, axis));
                float bMin = b.Min(point => Dot(point, axis));
                float bMax = b.Max(point => Dot(point, axis));
                if (aMax < bMin || bMax < aMin) return false;
            }
        }
        return true;
    }

    private static bool TryFirstDifference(Point2Dto[] points, bool fromStart, out Point2Dto result)
    {
        if (fromStart)
        {
            for (int index = 1; index < points.Length; index++)
                if (TryNormalize(Subtract(points[index], points[0]), out _))
                { result = Subtract(points[index], points[0]); return true; }
        }
        else
        {
            for (int index = points.Length - 2; index >= 0; index--)
                if (TryNormalize(Subtract(points[^1], points[index]), out _))
                { result = Subtract(points[^1], points[index]); return true; }
        }
        result = new Point2Dto();
        return false;
    }

    private static Point2Dto FirstNonZeroDifference(
        Point2Dto origin, Point2Dto first, Point2Dto second, Point2Dto third, bool reverse = false)
    {
        foreach (Point2Dto candidate in new[] { first, second, third })
        {
            Point2Dto difference = reverse ? Subtract(origin, candidate) : Subtract(candidate, origin);
            if (TryNormalize(difference, out _)) return difference;
        }
        return new Point2Dto();
    }

    private static IEnumerable<Point2Dto> EnumerateGeometry(TrajectorySegmentDto segment) =>
        segment.Type == "polyline" ? segment.Points ?? [] :
        new[] { segment.Start!, segment.Control1!, segment.Control2!, segment.End! };
    private static IEnumerable<Point2Dto> EnumerateRenderedGeometry(TrajectorySegmentDto segment) =>
        segment.Type == "polyline"
            ? segment.Points ?? []
            : TrajectoryGeometry.SampleCubicBezier(segment, TransitionSamples);
    private static Point2Dto SegmentStart(TrajectorySegmentDto segment) =>
        segment.Type == "polyline" ? segment.Points![0] : segment.Start!;
    private static Point2Dto SegmentEnd(TrajectorySegmentDto segment) =>
        segment.Type == "polyline" ? segment.Points![^1] : segment.End!;
    private static string ExportId(string instanceId, string localId) => $"{instanceId}--{localId}";
    private static bool IsFinite(Point2Dto? point) => point is not null && float.IsFinite(point.X) && float.IsFinite(point.Y);
    private static bool IsOutsideArea(Point2Dto point, VenueAreaDto area) =>
        MathF.Abs(point.X) > area.Width * 0.5f || MathF.Abs(point.Y) > area.Length * 0.5f;
    private static float Distance(Point2Dto a, Point2Dto b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    private static float Dot(Point2Dto a, Point2Dto b) => a.X * b.X + a.Y * b.Y;
    private static Point2Dto Add(Point2Dto a, Point2Dto b) => new() { X = a.X + b.X, Y = a.Y + b.Y };
    private static Point2Dto Subtract(Point2Dto a, Point2Dto b) => new() { X = a.X - b.X, Y = a.Y - b.Y };
    private static Point2Dto Multiply(Point2Dto point, float factor) => new() { X = point.X * factor, Y = point.Y * factor };
    private static Point2Dto Copy(Point2Dto point) => new() { X = point.X, Y = point.Y };
    private static bool TryNormalize(Point2Dto value, out Point2Dto normalized)
    {
        float length = MathF.Sqrt(value.X * value.X + value.Y * value.Y);
        if (!float.IsFinite(length) || length <= TangentTolerance)
        { normalized = new Point2Dto(); return false; }
        normalized = new Point2Dto { X = value.X / length, Y = value.Y / length };
        return true;
    }
    private static TrackCompilationDiagnostic Error(string message) => new(message);
    private static TrackCompilationDiagnostic Warning(string message) => new(message);
}
