using Godot;
using MotoGymkhanaTrainer;
using MotoGymkhanaTrainer.ExerciseEditor;
using MotoGymkhanaTrainer.Tracks;

const string ProjectDirectory = "E:\\Projects\\Games\\MotoGymkhanaTrainer";
string samplePath = Path.Combine(ProjectDirectory, "examples", "courses", "basic.json");
string json = File.ReadAllText(samplePath);
string alternatePath = Path.Combine(ProjectDirectory, "tests", "fixtures", "alternate-track.json");
string invalidFixturePath = Path.Combine(ProjectDirectory, "tests", "fixtures", "invalid-track.json");

TrackSnapshotDto snapshot = TrackLoader.LoadFromJson(json, samplePath);
AssertEqual(3, snapshot.FormatVersion, "sample uses formatVersion 3");
AssertEqual("basic-demo", snapshot.Track.Id, "valid JSON loads track metadata");
AssertEqual(4, snapshot.Cones.Length, "valid JSON loads every sample cone");
AssertEqual(4, snapshot.Markings.Length, "valid JSON loads solid, dashed, dotted and hidden markings");
AssertEqual(2, snapshot.Trajectory.Segments.Length, "valid JSON loads both trajectory segments");

TrackSnapshotDto alternate = TrackLoader.LoadFromJson(
    File.ReadAllText(alternatePath),
    alternatePath);
AssertEqual("alternate-test", alternate.Track.Id, "a second exported track loads independently");
AssertEqual(20.0f, alternate.Area.Width, "the second track supplies a different area width");
AssertEqual(30.0f, alternate.Area.Length, "the second track supplies a different area length");
AssertEqual(2, alternate.Cones.Length, "the second track supplies its own runtime cone set");
AssertEqual(3, alternate.FormatVersion, "Track version 2 migrates to version 3 in memory");
AssertEqual("solid", alternate.Markings[0].Style, "Track v2 marking receives the solid default");
AssertTrue(alternate.Markings[0].VisibleInViewer, "Track v2 marking receives visibleInViewer=true");

TrajectorySegmentDto polyline = snapshot.Trajectory.Segments[0];
AssertEqual("trajectory-segment-001", polyline.Id, "polyline segment id loads");
AssertEqual("polyline", polyline.Type, "polyline segment type loads");
AssertEqual(5, polyline.Points!.Length, "polyline points load in order");

TrajectorySegmentDto cubicBezier = snapshot.Trajectory.Segments[1];
AssertEqual("trajectory-segment-002", cubicBezier.Id, "cubicBezier segment id loads");
AssertEqual("cubicBezier", cubicBezier.Type, "cubicBezier segment type loads");
AssertTrue(cubicBezier.Points is null, "cubicBezier does not create legacy points in the DTO");
AssertEqual(18.0f, cubicBezier.Start!.X, "cubicBezier start loads");
AssertEqual(28.0f, cubicBezier.Control1!.Y, "cubicBezier control1 loads");
AssertEqual(15.0f, cubicBezier.Control2!.X, "cubicBezier control2 loads");
AssertEqual(35.0f, cubicBezier.End!.Y, "cubicBezier end loads");

Vector3 mapped = DomainCoordinateMapper.ToGodot(new Point2Dto { X = 12.0f, Y = 10.0f });
AssertEqual(new Vector3(12.0f, 0.0f, 10.0f), mapped, "domain X/Y maps to Godot X/Z");

Vector3[] expectedPositions =
[
    new(12.0f, 0.0f, 10.0f),
    new(18.0f, 0.0f, 15.0f),
    new(12.0f, 0.0f, 20.0f),
    new(18.0f, 0.0f, 25.0f),
];

for (int index = 0; index < snapshot.Cones.Length; index++)
{
    AssertEqual(
        expectedPositions[index],
        DomainCoordinateMapper.ToGodot(snapshot.Cones[index].Position),
        $"sample cone {index + 1} has its deterministic world position");
}

AssertThrows<InvalidDataException>(
    () => TrackLoader.LoadFromJson("{ invalid", "invalid.json"),
    "invalid JSON produces a clear error");

AssertThrows<InvalidDataException>(
    () => TrackLoader.LoadFromJson(File.ReadAllText(invalidFixturePath), invalidFixturePath),
    "damaged selected JSON produces a clear error");

AssertThrows<InvalidDataException>(
    () => TrackLoader.LoadFromJson(
        """{"formatVersion":2,"track":{},"area":{"width":40,"length":100},"cones":[],"markings":null,"trajectory":{"segments":[]}}""",
        "null-markings.json"),
    "null markings produce a clear contract error");

AssertThrows<InvalidDataException>(
    () => TrackLoader.LoadFromJson(
        """{"formatVersion":2,"track":{},"area":{"width":40,"length":100},"cones":[],"markings":[{"type":"line","points":[{"x":0,"y":0}],"widthMeters":0.08}],"trajectory":{"segments":[]}}""",
        "short-marking.json"),
    "a marking with fewer than two points produces a clear contract error");

AssertThrows<InvalidDataException>(
    () => TrackLoader.LoadFromJson(
        """{"formatVersion":2,"track":{},"area":{"width":0,"length":100},"cones":[],"markings":[],"trajectory":{"segments":[]}}""",
        "invalid-area.json"),
    "invalid area dimensions produce a clear contract error");

AssertThrows<InvalidDataException>(
    () => TrackLoader.LoadFromJson(
        """{"formatVersion":1,"track":{},"area":{"width":40,"length":100},"cones":[],"markings":[],"trajectory":{"points":[]}}""",
        "legacy-version.json"),
    "formatVersion 1 is rejected without a legacy compatibility branch");

AssertThrows<InvalidDataException>(
    () => TrackLoader.LoadFromJson(
        """{"formatVersion":2,"track":{},"area":{"width":40,"length":100},"cones":[],"markings":[],"trajectory":{"points":[]}}""",
        "legacy-points.json"),
    "legacy trajectory.points is rejected");

AssertThrows<InvalidDataException>(
    () => TrackLoader.LoadFromJson(
        """{"formatVersion":2,"track":{},"area":{"width":40,"length":100},"cones":[],"markings":[],"trajectory":[]}""",
        "legacy-array.json"),
    "legacy trajectory array is rejected");

AssertThrows<InvalidDataException>(
    () => TrackLoader.LoadFromJson(
        """{"formatVersion":2,"track":{},"area":{"width":40,"length":100},"cones":[],"markings":[],"trajectory":{"segments":[{"id":"bad-polyline","type":"polyline","points":[{"x":0,"y":0}]}]}}""",
        "invalid-polyline.json"),
    "polyline with fewer than two points is rejected");

AssertThrows<InvalidDataException>(
    () => TrackLoader.LoadFromJson(
        """{"formatVersion":2,"track":{},"area":{"width":40,"length":100},"cones":[],"markings":[],"trajectory":{"segments":[{"id":"bad-bezier","type":"cubicBezier","start":{"x":0,"y":0},"control1":{"x":1,"y":0},"end":{"x":2,"y":0}}]}}""",
        "invalid-bezier.json"),
    "cubicBezier with a missing control point is rejected");

TrackSnapshotDto futureTrack = TrackLoader.LoadFromJson(
    """{"formatVersion":2,"track":{},"area":{"width":40,"length":100},"cones":[],"markings":[],"trajectory":{"segments":[{"id":"future-segment","type":"arc"}]}}""",
    "future-segment.json");
AssertEqual(1, futureTrack.Trajectory.Segments.Length, "unknown segment type does not reject the track");
AssertEqual("arc", futureTrack.Trajectory.Segments[0].Type, "unknown segment type remains diagnostic data");

AssertThrows<FileNotFoundException>(
    () => File.ReadAllText(Path.Combine(ProjectDirectory, "missing.json")),
    "missing file produces a clear error");

ExerciseDocument exercise = ExerciseDocument.CreateNew(
    "iteration-3-test",
    "Iteration 3 Test",
    10.0f,
    12.0f);
string firstConeId = exercise.AddCone(new Point2Dto { X = 1.25f, Y = -2.5f });
string secondConeId = exercise.AddCone(new Point2Dto { X = -1.0f, Y = 3.0f });
AssertEqual("cone-001", firstConeId, "Exercise Editor generates the first deterministic cone id");
AssertEqual("cone-002", secondConeId, "Exercise Editor generates a second unique cone id");

exercise.MoveCone(firstConeId, new Point2Dto { X = 2.0f, Y = -1.5f });
exercise.SetConeColor(firstConeId, "blue");
exercise.StartTrajectoryAt(new Point2Dto { X = 0.0f, Y = -5.0f });
exercise.MoveTrajectoryPoint(1, new Point2Dto { X = -2.0f, Y = -2.5f });
exercise.AppendTrajectoryPoint(new Point2Dto { X = 2.0f, Y = 0.0f });
exercise.AppendTrajectoryPoint(new Point2Dto { X = -2.0f, Y = 2.5f });
exercise.AppendTrajectoryPoint(new Point2Dto { X = 0.0f, Y = 5.0f });
exercise.Definition.Bounds.Width = 4.0f;
exercise.Definition.Bounds.Length = 6.0f;

AssertEqual(2.0f, exercise.FindCone(firstConeId)!.Position.X, "a cone can be moved in local X/Y metres");
AssertEqual("blue", exercise.FindCone(firstConeId)!.Color, "a cone color can be edited");
AssertEqual(-1.0f, exercise.FindCone(secondConeId)!.Position.X, "bounds changes do not scale existing cones");
AssertEqual(1, exercise.Definition.Trajectory.Segments.Length, "a newly constructed trajectory starts as one segment");
AssertEqual("polyline", exercise.Definition.Trajectory.Segments[0].Type, "editable trajectory is a polyline");
AssertEqual("trajectory-segment-001", exercise.Definition.Trajectory.Segments[0].Id,
    "new trajectory has a stable segment id");
AssertEqual(5, exercise.TrajectoryPointCount, "a five-point polyline can be constructed");
AssertEqual(-5.0f, exercise.Definition.EntryPoint.Y, "first trajectory point is the EntryPoint source");
AssertEqual(5.0f, exercise.Definition.ExitPoint.Y, "last trajectory point is the ExitPoint source");

int insertedPointIndex = exercise.InsertTrajectoryPointAfter(0);
AssertEqual(1, insertedPointIndex, "a point is inserted after the selected trajectory anchor");
AssertEqual(6, exercise.TrajectoryPointCount, "inserting into a polyline adds one anchor");
AssertEqual(-3.75f, exercise.GetTrajectoryPoint(insertedPointIndex).Y, "inserted point is the adjacent midpoint");
AssertTrue(
    exercise.DeleteTrajectoryPoint(insertedPointIndex) == TrajectoryAnchorDeleteResult.Deleted,
    "an internal line anchor can be deleted");
AssertEqual(5, exercise.TrajectoryPointCount, "deleting removes exactly one trajectory anchor");
AssertEqual("trajectory-segment-001", exercise.Definition.Trajectory.Segments[0].Id,
    "non-structural polyline edits preserve its segment id");

TrajectorySectionLocation? converted =
    exercise.ConvertSectionToCubic(new TrajectorySectionLocation(0, 1));
AssertTrue(converted is not null, "a selected middle polyline section converts to cubicBezier");
AssertEqual(3, exercise.TrajectorySegmentCount, "middle conversion splits the polyline into three segments");
AssertEqual("polyline", exercise.GetTrajectorySegment(0).Type, "split keeps a left polyline");
AssertEqual("cubicBezier", exercise.GetTrajectorySegment(1).Type, "split creates a middle cubicBezier");
AssertEqual("polyline", exercise.GetTrajectorySegment(2).Type, "split keeps a right polyline");
AssertEqual(2, exercise.GetTrajectorySegment(0).Points!.Length, "left split polyline remains valid");
AssertEqual(3, exercise.GetTrajectorySegment(2).Points!.Length, "right split polyline remains valid");
AssertEqual("trajectory-segment-002", exercise.GetTrajectorySegment(0).Id, "split ids are predictable");
AssertEqual("trajectory-segment-003", exercise.GetTrajectorySegment(1).Id, "Bezier id is predictable");
AssertEqual("trajectory-segment-004", exercise.GetTrajectorySegment(2).Id, "right split id is predictable");

TrajectorySegmentDto cubic = exercise.GetTrajectorySegment(1);
AssertTrue(
    MathF.Abs(
        (cubic.Start!.X + cubic.End!.X) / 2.0f -
        TrajectoryGeometry.EvaluateCubicBezier(
        cubic.Start,
        cubic.Control1!,
        cubic.Control2!,
        cubic.End,
        0.5f).X) < 0.0001f,
    "initial Bezier remains on the converted straight section");
exercise.MoveBezierControl(1, BezierControlKind.Control1, new Point2Dto { X = 1.0f, Y = -1.0f });
exercise.MoveBezierControl(1, BezierControlKind.Control2, new Point2Dto { X = 3.0f, Y = 1.0f });
AssertEqual(1.0f, cubic.Control1!.X, "control1 can be edited in local metres");
AssertEqual(3.0f, cubic.Control2!.X, "control2 can be edited in local metres");

Point2Dto control1BeforeAnchorMove = new() { X = cubic.Control1.X, Y = cubic.Control1.Y };
Point2Dto leftJoinBefore = exercise.GetTrajectoryPoint(1);
exercise.MoveTrajectoryPoint(
    1,
    new Point2Dto { X = leftJoinBefore.X + 0.75f, Y = leftJoinBefore.Y + 0.5f });
AssertEqual(exercise.GetTrajectorySegment(0).Points![^1].X, cubic.Start!.X,
    "moving a shared anchor keeps polyline end and Bezier start synchronized");
AssertEqual(control1BeforeAnchorMove.X + 0.75f, cubic.Control1.X,
    "moving Bezier start translates control1 by the same delta");
AssertEqual(control1BeforeAnchorMove.Y + 0.5f, cubic.Control1.Y,
    "control1 preserves its local offset after anchor movement");

Point2Dto control2BeforeAnchorMove = new() { X = cubic.Control2.X, Y = cubic.Control2.Y };
Point2Dto rightJoinBefore = exercise.GetTrajectoryPoint(2);
exercise.MoveTrajectoryPoint(
    2,
    new Point2Dto { X = rightJoinBefore.X - 0.5f, Y = rightJoinBefore.Y + 0.25f });
AssertEqual(cubic.End!.X, exercise.GetTrajectorySegment(2).Points![0].X,
    "moving the other shared anchor keeps Bezier end and right polyline synchronized");
AssertEqual(control2BeforeAnchorMove.X - 0.5f, cubic.Control2.X,
    "moving Bezier end translates control2 by the same delta");
AssertTrue(
    exercise.DeleteTrajectoryPoint(1) == TrajectoryAnchorDeleteResult.CubicAdjacentBlocked,
    "deleting an anchor adjacent to cubicBezier is safely blocked");
AssertEqual(-2, exercise.InsertTrajectoryPointAfter(1),
    "inserting directly into cubicBezier is blocked with a distinct result");

TrajectorySectionLocation? convertedBack = exercise.ConvertCubicToLine(1);
AssertTrue(convertedBack is not null, "cubicBezier converts back to a line");
AssertEqual(1, exercise.TrajectorySegmentCount, "adjacent polylines normalize into one segment");
AssertEqual("polyline", exercise.GetTrajectorySegment(0).Type, "normalized trajectory is a polyline");
AssertEqual("trajectory-segment-002", exercise.GetTrajectorySegment(0).Id,
    "normalization preserves the first compatible polyline id");

insertedPointIndex = exercise.InsertTrajectoryPointAfter(1);
AssertEqual(2, insertedPointIndex, "a point is inserted after the selected trajectory index");
AssertEqual(6, exercise.TrajectoryPointCount, "inserting adds exactly one trajectory point");
AssertTrue(
    exercise.DeleteTrajectoryPoint(insertedPointIndex) == TrajectoryAnchorDeleteResult.Deleted,
    "polyline deletion continues working after Bezier round-trip");
AssertEqual(5, exercise.TrajectoryPointCount, "deleting removes exactly one trajectory point");

ExerciseDocument minimumPolyline = ExerciseDocument.CreateNew("minimum", "Minimum", 10.0f, 10.0f);
AssertTrue(
    minimumPolyline.DeleteTrajectoryPoint(0) == TrajectoryAnchorDeleteResult.MinimumBlocked,
    "deletion is blocked when only two trajectory points remain");
AssertEqual(2, minimumPolyline.TrajectoryPointCount, "blocked deletion preserves the valid two-point trajectory");

string lineMarkingId = exercise.AddMarking(
    "line",
    [new Point2Dto { X = -2.0f, Y = -2.0f }, new Point2Dto { X = 2.0f, Y = -2.0f }]);
string polylineMarkingId = exercise.AddMarking(
    "polyline",
    [new Point2Dto { X = -2.0f, Y = 0.0f }, new Point2Dto { X = 0.0f, Y = 1.0f }]);
exercise.AppendMarkingPoint(polylineMarkingId, new Point2Dto { X = 2.0f, Y = 0.0f });
exercise.MoveMarkingPoint(polylineMarkingId, 1, new Point2Dto { X = 0.5f, Y = 1.5f });
AssertTrue(
    exercise.SetMarkingProperties(lineMarkingId, "#12ABEF", 0.15f, "solid", visibleInViewer: false),
    "a line marking accepts arbitrary canonical RGB, width and visibility");
AssertTrue(
    exercise.SetMarkingProperties(polylineMarkingId, "#EE44AA", 0.1f, "dashed", visibleInViewer: true),
    "a polyline marking accepts dashed style");
AssertEqual(0.5f, exercise.FindMarking(polylineMarkingId)!.Points[1].X,
    "a marking point moves in local exercise metres");
int insertedMarkingPoint = exercise.InsertMarkingPointAfter(polylineMarkingId, 1);
AssertEqual(2, insertedMarkingPoint, "an internal polyline marking point can be inserted");
AssertTrue(exercise.DeleteMarkingPoint(polylineMarkingId, insertedMarkingPoint),
    "an internal polyline marking point can be deleted without dropping below two points");
AssertTrue(!exercise.FindMarking(lineMarkingId)!.VisibleInViewer,
    "visibleInViewer=false remains persisted editor data");

IReadOnlyList<MarkingStroke> solidStrokes = MarkingGeometry.CreateStrokes(
    exercise.FindMarking(lineMarkingId)!.Points,
    "solid");
IReadOnlyList<MarkingStroke> dashedStrokes = MarkingGeometry.CreateStrokes(
    exercise.FindMarking(polylineMarkingId)!.Points,
    "dashed");
IReadOnlyList<MarkingStroke> dottedStrokes = MarkingGeometry.CreateStrokes(
    exercise.FindMarking(polylineMarkingId)!.Points,
    "dotted");
AssertEqual(1, solidStrokes.Count, "solid rendering keeps one stroke per line section");
AssertTrue(dashedStrokes.Count > 1, "dashed rendering produces temporary separated strokes");
AssertTrue(dottedStrokes.Count > dashedStrokes.Count, "dotted rendering produces a denser temporary pattern");

string exerciseJson = ExerciseDefinitionStore.Serialize(exercise.Definition);
AssertTrue(exerciseJson.Contains("\"formatVersion\": 2"), "Exercise Definition saves formatVersion 2 as indented JSON");
AssertTrue(!exerciseJson.Contains("zoom", StringComparison.OrdinalIgnoreCase), "serialized document excludes zoom UI state");
AssertTrue(!exerciseJson.Contains("selected", StringComparison.OrdinalIgnoreCase), "serialized document excludes selection UI state");
AssertTrue(!exerciseJson.Contains("pan", StringComparison.OrdinalIgnoreCase), "serialized document excludes pan UI state");
AssertTrue(!exerciseJson.Contains("\"control1\"", StringComparison.Ordinal),
    "polyline JSON omits inapplicable nullable Bezier fields");

string temporaryExercisePath = Path.Combine(Path.GetTempPath(), $"motogymkhana-exercise-{Guid.NewGuid():N}.json");
try
{
    ExerciseDefinitionStore.SaveToFile(exercise.Definition, temporaryExercisePath);
    byte[] savedBytes = File.ReadAllBytes(temporaryExercisePath);
    AssertTrue(savedBytes.Length >= 3 && !(savedBytes[0] == 0xEF && savedBytes[1] == 0xBB && savedBytes[2] == 0xBF),
        "Exercise Definition is UTF-8 without a BOM");

    ExerciseDefinitionDto reopened = ExerciseDefinitionStore.LoadFromFile(temporaryExercisePath);
    AssertEqual("iteration-3-test", reopened.Exercise.Id, "saved Exercise Definition reopens successfully");
    AssertEqual(2, reopened.Cones.Length, "reopened Exercise Definition retains all cones");
    AssertEqual("blue", reopened.Cones[0].Color, "reopened Exercise Definition retains cone edits");
    AssertEqual(5, reopened.Trajectory.Segments[0].Points!.Length,
        "reopened Exercise Definition retains the full polyline");
}
finally
{
    File.Delete(temporaryExercisePath);
}

ExerciseDefinitionDto preservedCandidate = exercise.Definition;
AssertThrows<InvalidDataException>(
    () => ExerciseDefinitionStore.LoadFromJson("{ damaged", "damaged-exercise.json"),
    "damaged Exercise Definition JSON produces a clear error");
AssertTrue(ReferenceEquals(preservedCandidate, exercise.Definition),
    "a failed candidate load leaves the current Exercise document intact");
AssertThrows<InvalidDataException>(
    () => ExerciseDefinitionStore.LoadFromJson(json, samplePath),
    "exported Track JSON is rejected as an incompatible Exercise Definition root");

string iteration1ExercisePath = Path.Combine(ProjectDirectory, "tests", "fixtures", "iteration1-exercise.json");
ExerciseDefinitionDto iteration1Definition = ExerciseDefinitionStore.LoadFromFile(iteration1ExercisePath);
var iteration1Document = new ExerciseDocument(iteration1Definition);
AssertEqual(2, iteration1Document.TrajectoryPointCount,
    "an Iteration 1 two-point temporary trajectory still opens");
AssertEqual("entry-exit-preview", iteration1Document.Definition.Trajectory.Segments[0].Id,
    "an Iteration 1 segment keeps its existing stable id");
iteration1Document.AppendTrajectoryPoint(new Point2Dto { X = 1.0f, Y = 4.0f });
AssertEqual(3, iteration1Document.TrajectoryPointCount,
    "an Iteration 1 trajectory can be extended with new points");

string mismatchPath = Path.Combine(ProjectDirectory, "tests", "fixtures", "mismatched-exercise.json");
string mismatchSource = File.ReadAllText(mismatchPath);
ExerciseDefinitionLoadResult mismatchResult =
    ExerciseDefinitionStore.LoadFromJsonWithDiagnostics(mismatchSource, mismatchPath);
AssertEqual(3, mismatchResult.Warnings.Count,
    "version migration plus entry and exit mismatches produce diagnostic warnings");
AssertEqual(-2.0f, mismatchResult.Definition.EntryPoint.X,
    "trajectory start replaces mismatched EntryPoint in memory");
AssertEqual(6.0f, mismatchResult.Definition.ExitPoint.Y,
    "trajectory end replaces mismatched ExitPoint in memory");
AssertTrue(mismatchSource.Contains("\"x\": 99.0"),
    "loading a mismatch does not modify the source file automatically");

AssertThrows<InvalidDataException>(
    () => ExerciseDefinitionStore.LoadFromJson(
        """{"formatVersion":1,"exercise":{"id":"one-point","name":"One Point","version":1},"bounds":{"width":10,"length":10},"cones":[],"markings":[],"entryPoint":{"x":0,"y":0},"exitPoint":{"x":0,"y":0},"trajectory":{"segments":[{"id":"trajectory-segment-001","type":"polyline","points":[{"x":0,"y":0}]}]},"checkpoints":[]}""",
        "one-point-exercise.json"),
    "a one-point Exercise polyline is rejected");

string multiSegmentPath = Path.Combine(ProjectDirectory, "tests", "fixtures", "multi-segment-exercise.json");
ExerciseDefinitionDto multiSegment = ExerciseDefinitionStore.LoadFromFile(multiSegmentPath);
AssertEqual(3, multiSegment.Trajectory.Segments.Length,
    "a prepared Exercise with multiple segments loads");
AssertEqual("cubicBezier", multiSegment.Trajectory.Segments[1].Type,
    "prepared Exercise preserves cubicBezier geometry");
AssertEqual(2.0f, multiSegment.Trajectory.Segments[1].Control1!.X,
    "prepared Exercise retains control points");
var multiDocument = new ExerciseDocument(multiSegment);
Point2Dto preparedControl = new()
{
    X = multiDocument.GetTrajectorySegment(1).Control1!.X,
    Y = multiDocument.GetTrajectorySegment(1).Control1!.Y,
};
multiDocument.MoveTrajectoryPoint(1, new Point2Dto { X = -1.5f, Y = -1.5f });
AssertEqual(-1.5f, multiDocument.GetTrajectorySegment(0).Points![^1].X,
    "loaded shared anchor updates the preceding segment");
AssertEqual(-1.5f, multiDocument.GetTrajectorySegment(1).Start!.X,
    "loaded shared anchor updates the following segment");
AssertEqual(preparedControl.X + 0.5f, multiDocument.GetTrajectorySegment(1).Control1!.X,
    "loaded Bezier handle follows its moved anchor");

string multiJson = ExerciseDefinitionStore.Serialize(multiDocument.Definition);
ExerciseDefinitionDto multiRoundTrip =
    ExerciseDefinitionStore.LoadFromJson(multiJson, "multi-segment-round-trip.json");
AssertEqual(3, multiRoundTrip.Trajectory.Segments.Length,
    "multiple segments survive save and reload");
AssertTrue(multiJson.Contains("\"control1\"", StringComparison.Ordinal),
    "Bezier control points remain persisted rather than sampled");
AssertTrue(!multiJson.Contains("samples", StringComparison.OrdinalIgnoreCase),
    "rendering samples are not serialized");

string discontinuousPath = Path.Combine(
    ProjectDirectory,
    "tests",
    "fixtures",
    "discontinuous-exercise.json");
AssertThrows<InvalidDataException>(
    () => ExerciseDefinitionStore.LoadFromFile(discontinuousPath),
    "a trajectory discontinuity produces a clear validation error");

AssertThrows<InvalidDataException>(
    () => ExerciseDefinitionStore.LoadFromJson(
        """{"formatVersion":1,"exercise":{"id":"bad-bezier","name":"Bad Bezier","version":1},"bounds":{"width":10,"length":10},"cones":[],"markings":[],"entryPoint":{"x":0,"y":0},"exitPoint":{"x":1,"y":1},"trajectory":{"segments":[{"id":"trajectory-segment-001","type":"cubicBezier","start":{"x":0,"y":0},"control1":{"x":0,"y":1},"end":{"x":1,"y":1}}]},"checkpoints":[]}""",
        "missing-control-exercise.json"),
    "Exercise cubicBezier with a missing control point is rejected");

ExerciseDefinitionLoadResult migratedV1 = ExerciseDefinitionStore.LoadFromJsonWithDiagnostics(
    """{"formatVersion":1,"exercise":{"id":"legacy-marking","name":"Legacy Marking","version":1},"bounds":{"width":10,"length":10},"cones":[],"markings":[{"id":"marking-001","type":"line","points":[{"x":0,"y":0},{"x":1,"y":0}],"color":"yellow","widthMeters":0.08}],"entryPoint":{"x":0,"y":-1},"exitPoint":{"x":0,"y":1},"trajectory":{"segments":[{"id":"trajectory-segment-001","type":"polyline","points":[{"x":0,"y":-1},{"x":0,"y":1}]}]},"checkpoints":[]}""",
    "legacy-marking.json");
AssertEqual(2, migratedV1.Definition.FormatVersion, "Exercise version 1 migrates to version 2 in memory");
AssertEqual("solid", migratedV1.Definition.Markings[0].Style, "Exercise v1 marking receives solid style");
AssertTrue(migratedV1.Definition.Markings[0].VisibleInViewer,
    "Exercise v1 marking receives visibleInViewer=true");
AssertEqual("#FFD10D", migratedV1.Definition.Markings[0].Color,
    "legacy named marking color becomes canonical RGB in memory");
AssertTrue(migratedV1.Warnings.Count > 0, "Exercise migration is reported without rewriting its source");

ExerciseDefinitionLoadResult normalizedMarking = ExerciseDefinitionStore.LoadFromJsonWithDiagnostics(
    """{"formatVersion":2,"exercise":{"id":"fallback-marking","name":"Fallback Marking","version":1},"bounds":{"width":10,"length":10},"cones":[],"markings":[{"id":"marking-001","type":"line","points":[{"x":0,"y":0},{"x":1,"y":0}],"color":"#aabbcc","widthMeters":0.08,"style":"futureStyle","visibleInViewer":true}],"entryPoint":{"x":0,"y":-1},"exitPoint":{"x":0,"y":1},"trajectory":{"segments":[{"id":"trajectory-segment-001","type":"polyline","points":[{"x":0,"y":-1},{"x":0,"y":1}]}]},"checkpoints":[]}""",
    "fallback-marking.json");
AssertEqual("solid", normalizedMarking.Definition.Markings[0].Style,
    "unknown Exercise marking style uses the documented solid fallback");
AssertEqual("#AABBCC", normalizedMarking.Definition.Markings[0].Color,
    "valid lowercase RGB is normalized to canonical #RRGGBB in memory");
AssertEqual(2, normalizedMarking.Warnings.Count,
    "style fallback and color normalization both produce diagnostics");

TrackSnapshotDto unknownStyleTrack = TrackLoader.LoadFromJson(
    """{"formatVersion":3,"track":{"id":"unknown-style","name":"Unknown Style"},"area":{"width":10,"length":10},"elements":[],"cones":[],"markings":[{"id":"marking-001","type":"line","points":[{"x":0,"y":0},{"x":1,"y":0}],"color":"#FFFFFF","widthMeters":0.1,"style":"futureStyle","visibleInViewer":true}],"trajectory":{"segments":[]},"checkpoints":[]}""",
    "unknown-style-track.json");
AssertEqual("futureStyle", unknownStyleTrack.Markings[0].Style,
    "unknown Track marking style remains available for Viewer warning and solid fallback");

string temporaryLibraryRoot = Path.Combine(Path.GetTempPath(), $"motogymkhana-library-{Guid.NewGuid():N}");
try
{
    var library = new ExerciseLibrary(temporaryLibraryRoot);
    AssertEqual(0, library.EnumerateEntries().Count, "an empty Exercise library initializes cleanly");
    string drills = library.CreateFolder(string.Empty, "drills");
    string slaloms = library.CreateFolder(drills, "slaloms");
    AssertTrue(Directory.Exists(library.ResolveFolder(slaloms)), "nested Exercise library folders are created");

    string firstLibraryFile = library.ResolveSaveJson(drills, ExerciseLibrary.SuggestFileName(exercise.Definition.Exercise.Id));
    ExerciseDefinitionStore.SaveToFile(exercise.Definition, firstLibraryFile);
    ExerciseDefinitionDto libraryReload = ExerciseDefinitionStore.LoadFromFile(library.ResolveExistingJson(firstLibraryFile));
    AssertEqual(exercise.Definition.Exercise.Id, libraryReload.Exercise.Id,
        "an Exercise saved in a selected library folder reopens");

    string saveAsFile = library.ResolveSaveJson(slaloms, "saved-as.json");
    ExerciseDefinitionStore.SaveToFile(exercise.Definition, saveAsFile);
    AssertTrue(File.Exists(saveAsFile), "Save As can target a different nested library folder");
    AssertThrows<InvalidDataException>(
        () => library.CreateFolder(string.Empty, "../escape"),
        "an invalid folder name is rejected");
    AssertThrows<IOException>(
        () => library.CreateFolder(string.Empty, "drills"),
        "an existing Exercise library folder is not silently accepted or overwritten");
    AssertThrows<InvalidDataException>(
        () => library.ResolveUserPath(Path.Combine(temporaryLibraryRoot, "..", "escape.json")),
        "path traversal cannot leave the Exercise library root");
}
finally
{
    if (Directory.Exists(temporaryLibraryRoot))
    {
        Directory.Delete(temporaryLibraryRoot, recursive: true);
    }
}

Console.WriteLine("All Viewer and Exercise Editor Iteration 4 checks passed.");

static void AssertEqual<T>(T expected, T actual, string description)
    where T : IEquatable<T>
{
    if (!expected.Equals(actual))
    {
        throw new InvalidOperationException(
            $"Check failed: {description}. Expected '{expected}', got '{actual}'.");
    }

    Console.WriteLine($"PASS: {description}");
}

static void AssertThrows<TException>(Action action, string description)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception)
    {
        Console.WriteLine($"PASS: {description} ({exception.Message})");
        return;
    }

    throw new InvalidOperationException($"Check failed: {description}. Expected {typeof(TException).Name}.");
}

static void AssertTrue(bool condition, string description)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Check failed: {description}.");
    }

    Console.WriteLine($"PASS: {description}");
}
