using Godot;
using System.Text.Json;
using MotoGymkhanaTrainer;
using MotoGymkhanaTrainer.ExerciseEditor;
using MotoGymkhanaTrainer.TrackEditor;
using MotoGymkhanaTrainer.Tracks;
using MotoGymkhanaTrainer.VenueEditor;
using MotoGymkhanaTrainer.Viewer;

const string ProjectDirectory = "E:\\Projects\\Games\\MotoGymkhanaTrainer";
string samplePath = Path.Combine(ProjectDirectory, "examples", "courses", "basic.json");
string json = File.ReadAllText(samplePath);
string alternatePath = Path.Combine(ProjectDirectory, "tests", "fixtures", "alternate-track.json");
string invalidFixturePath = Path.Combine(ProjectDirectory, "tests", "fixtures", "invalid-track.json");
string noColorFixturePath = Path.Combine(ProjectDirectory, "tests", "fixtures", "no-color-track.json");

TrackSnapshotDto snapshot = TrackLoader.LoadFromJson(json, samplePath);
AssertEqual(5, snapshot.FormatVersion, "sample uses formatVersion 5");
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
AssertEqual(5, alternate.FormatVersion, "a second exported Track uses formatVersion 5");
AssertEqual("solid", alternate.Markings[0].Style, "Track v5 keeps the marking style");
AssertTrue(alternate.Markings[0].VisibleInViewer, "Track v5 keeps visibleInViewer=true");

TrackSnapshotDto noColorTrack = TrackLoader.LoadFromJson(
    File.ReadAllText(noColorFixturePath), noColorFixturePath);
AssertEqual("none", noColorTrack.Cones[0].Color,
    "exported Track preserves the no-topper cone color sentinel");

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

AssertTrue(TrajectoryGeometry.TryGetEntryPose(
        snapshot.Trajectory,
        out Point2Dto viewerEntry,
        out Point2Dto viewerDirection),
    "Viewer resolves a camera pose from the first trajectory segment");
AssertTrue(viewerEntry.X == polyline.Points![0].X && viewerEntry.Y == polyline.Points[0].Y,
    "Viewer camera entry is the first trajectory point");
Point2Dto expectedViewerDirection = new()
{
    X = polyline.Points[1].X - polyline.Points[0].X,
    Y = polyline.Points[1].Y - polyline.Points[0].Y,
};
float expectedViewerDirectionLength = MathF.Sqrt(
    expectedViewerDirection.X * expectedViewerDirection.X +
    expectedViewerDirection.Y * expectedViewerDirection.Y);
AssertEqual(expectedViewerDirection.X / expectedViewerDirectionLength, viewerDirection.X,
    "Viewer camera follows the first segment X tangent");
AssertEqual(expectedViewerDirection.Y / expectedViewerDirectionLength, viewerDirection.Y,
    "Viewer camera follows the first segment Y tangent");

Vector3 mapped = DomainCoordinateMapper.ToGodot(new Point2Dto { X = 12.0f, Y = 10.0f });
AssertEqual(new Vector3(12.0f, 0.0f, -10.0f), mapped, "domain X/Y maps to Godot X/-Z");

Vector3[] expectedPositions =
[
    new(12.0f, 0.0f, -10.0f),
    new(18.0f, 0.0f, -15.0f),
    new(12.0f, 0.0f, -20.0f),
    new(18.0f, 0.0f, -25.0f),
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
    """{"formatVersion":5,"track":{"id":"future","name":"Future"},"venue":{"id":"venue","name":"Venue"},"area":{"width":40,"length":100},"panorama":{"enabled":false,"texturePath":"","rotationDeg":0,"energyMultiplier":1},"venueObjects":[],"elements":[],"cones":[],"markings":[],"trajectory":{"segments":[{"id":"future-segment","type":"arc"}]},"checkpoints":[]}""",
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
exercise.SetConeColor(secondConeId, "none");
exercise.StartTrajectoryAt(new Point2Dto { X = 0.0f, Y = -5.0f });
exercise.MoveTrajectoryPoint(1, new Point2Dto { X = -2.0f, Y = -2.5f });
exercise.AppendTrajectoryPoint(new Point2Dto { X = 2.0f, Y = 0.0f });
exercise.AppendTrajectoryPoint(new Point2Dto { X = -2.0f, Y = 2.5f });
exercise.AppendTrajectoryPoint(new Point2Dto { X = 0.0f, Y = 5.0f });
exercise.Definition.Bounds.Width = 4.0f;
exercise.Definition.Bounds.Length = 6.0f;

AssertEqual(2.0f, exercise.FindCone(firstConeId)!.Position.X, "a cone can be moved in local X/Y metres");
AssertEqual("blue", exercise.FindCone(firstConeId)!.Color, "a cone color can be edited");
AssertEqual("none", exercise.FindCone(secondConeId)!.Color, "a cone can explicitly omit its color topper");
AssertEqual(secondConeId, exercise.FindConeAt(new Point2Dto { X = -1.0f, Y = 3.0f })!.Id,
    "snapped duplicate placement resolves the existing cone instead of requiring a new id");
AssertEqual(-1.0f, exercise.FindCone(secondConeId)!.Position.X, "bounds changes do not scale existing cones");
AssertEqual(1, exercise.Definition.Trajectory.Segments.Length, "a newly constructed trajectory starts as one segment");
AssertEqual("polyline", exercise.Definition.Trajectory.Segments[0].Type, "editable trajectory is a polyline");
AssertEqual("trajectory-segment-001", exercise.Definition.Trajectory.Segments[0].Id,
    "new trajectory has a stable segment id");

ExerciseDocument splineBuild = ExerciseDocument.CreateNew("spline-build", "Spline Build");
splineBuild.StartSplineTrajectoryAt(new Point2Dto { X = 0.0f, Y = 0.0f });
splineBuild.SetInitialTrajectorySplineEnd(new Point2Dto { X = 3.0f, Y = 0.0f });
int appendedSplineAnchor = splineBuild.AppendTrajectorySpline(new Point2Dto { X = 6.0f, Y = 3.0f });
AssertEqual(2, splineBuild.Definition.Trajectory.Segments.Length,
    "trajectory click construction creates one ordered segment per section");
AssertTrue(splineBuild.Definition.Trajectory.Segments.All(segment => segment.Type == "cubicBezier"),
    "new trajectory sections default directly to persisted cubicBezier geometry");
TrajectorySegmentDto firstBuiltSpline = splineBuild.Definition.Trajectory.Segments[0];
TrajectorySegmentDto secondBuiltSpline = splineBuild.Definition.Trajectory.Segments[1];
Point2Dto firstSplineControl1 = new() { X = firstBuiltSpline.Control1!.X, Y = firstBuiltSpline.Control1.Y };
Point2Dto firstSplineControl2 = new() { X = firstBuiltSpline.Control2!.X, Y = firstBuiltSpline.Control2.Y };
AssertEqual(2, appendedSplineAnchor, "appended spline returns its final conceptual anchor");
AssertTrue(MathF.Abs((firstBuiltSpline.End!.X - firstBuiltSpline.Control2!.X) -
                     (secondBuiltSpline.Control1!.X - secondBuiltSpline.Start!.X)) < 0.0001f &&
           MathF.Abs((firstBuiltSpline.End.Y - firstBuiltSpline.Control2.Y) -
                     (secondBuiltSpline.Control1.Y - secondBuiltSpline.Start.Y)) < 0.0001f,
    "adjacent construction splines have matching derivatives at their shared anchor");
int insertedSplineAnchor = splineBuild.InsertTrajectoryPointAfter(0);
AssertEqual(1, insertedSplineAnchor, "a point can be inserted into a newly constructed cubic trajectory");
AssertEqual(3, splineBuild.Definition.Trajectory.Segments.Length,
    "splitting a cubic adds one persisted segment");
AssertEqual(4, splineBuild.TrajectoryPointCount, "splitting a cubic adds one conceptual anchor");
AssertEqual(splineBuild.GetTrajectorySegment(0).End!.X, splineBuild.GetTrajectorySegment(1).Start!.X,
    "split cubic sections share the inserted anchor");
AssertTrue(
    splineBuild.DeleteTrajectoryPoint(insertedSplineAnchor) == TrajectoryAnchorDeleteResult.Deleted,
    "an anchor between cubic sections can be deleted");
AssertEqual(2, splineBuild.Definition.Trajectory.Segments.Length,
    "deleting the inserted cubic anchor restores the segment count");
AssertEqual(firstSplineControl1.X, splineBuild.GetTrajectorySegment(0).Control1!.X,
    "midpoint split followed by delete restores cubic control1");
AssertEqual(firstSplineControl1.Y, splineBuild.GetTrajectorySegment(0).Control1!.Y,
    "midpoint split followed by delete restores cubic control1 Y");
AssertEqual(firstSplineControl2.X, splineBuild.GetTrajectorySegment(0).Control2!.X,
    "midpoint split followed by delete restores cubic control2 X");
AssertEqual(firstSplineControl2.Y, splineBuild.GetTrajectorySegment(0).Control2!.Y,
    "midpoint split followed by delete restores cubic control2");
AssertTrue(splineBuild.ConvertCubicToLine(1) is not null,
    "a newly constructed spline can still be converted back to a line");

ExerciseDocument endpointSpline = ExerciseDocument.CreateNew("endpoint-spline", "Endpoint Spline");
endpointSpline.StartSplineTrajectoryAt(new Point2Dto { X = 0.0f, Y = 0.0f });
endpointSpline.SetInitialTrajectorySplineEnd(new Point2Dto { X = 2.0f, Y = 0.0f });
endpointSpline.AppendTrajectorySpline(new Point2Dto { X = 4.0f, Y = 1.0f });
endpointSpline.AppendTrajectorySpline(new Point2Dto { X = 6.0f, Y = 0.0f });
AssertTrue(endpointSpline.DeleteTrajectoryPoint(0) == TrajectoryAnchorDeleteResult.Deleted,
    "the first anchor of a multi-section cubic trajectory can be deleted");
AssertEqual(2.0f, endpointSpline.Definition.EntryPoint.X,
    "deleting the first cubic anchor promotes the next anchor to EntryPoint");
AssertTrue(
    endpointSpline.DeleteTrajectoryPoint(endpointSpline.TrajectoryPointCount - 1) ==
    TrajectoryAnchorDeleteResult.Deleted,
    "the last anchor of a multi-section cubic trajectory can be deleted");
AssertEqual(4.0f, endpointSpline.Definition.ExitPoint.X,
    "deleting the last cubic anchor promotes the preceding anchor to ExitPoint");

ExerciseDocument mixedTrajectory = ExerciseDocument.CreateNew("mixed-delete", "Mixed Delete");
mixedTrajectory.StartTrajectoryAt(new Point2Dto { X = -4.0f, Y = 0.0f });
mixedTrajectory.MoveTrajectoryPoint(1, new Point2Dto { X = -2.0f, Y = 0.0f });
mixedTrajectory.AppendTrajectoryPoint(new Point2Dto { X = 0.0f, Y = 1.0f });
mixedTrajectory.AppendTrajectoryPoint(new Point2Dto { X = 2.0f, Y = 0.0f });
mixedTrajectory.AppendTrajectoryPoint(new Point2Dto { X = 4.0f, Y = 0.0f });
AssertTrue(
    mixedTrajectory.ConvertSectionToCubic(new TrajectorySectionLocation(0, 1)) is not null,
    "mixed deletion fixture contains polyline-cubic-polyline sections");
AssertTrue(mixedTrajectory.DeleteTrajectoryPoint(1) == TrajectoryAnchorDeleteResult.Deleted,
    "an anchor at a polyline-to-cubic join can be deleted");
AssertEqual(
    mixedTrajectory.GetTrajectorySegment(0).End!.X,
    mixedTrajectory.GetTrajectorySegment(1).Points![0].X,
    "polyline-to-cubic deletion keeps the following polyline connected");
AssertTrue(mixedTrajectory.DeleteTrajectoryPoint(1) == TrajectoryAnchorDeleteResult.Deleted,
    "an anchor at a cubic-to-multi-point-polyline join can be deleted");
AssertEqual(
    mixedTrajectory.GetTrajectorySegment(0).End!.X,
    mixedTrajectory.GetTrajectorySegment(1).Points![0].X,
    "cubic-to-polyline deletion keeps the retained polyline suffix connected");
AssertEqual(
    mixedTrajectory.TrajectorySegmentCount,
    mixedTrajectory.Definition.Trajectory.Segments.Select(segment => segment.Id).Distinct().Count(),
    "mixed cubic deletion keeps trajectory segment ids unique");
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
AssertEqual(0.5f, PathEditing.GetVertices(exercise.FindMarking(polylineMarkingId)!.Path)[1].X,
    "a marking point moves in local exercise metres");
int insertedMarkingPoint = exercise.InsertMarkingPointAfter(polylineMarkingId, 1);
AssertEqual(2, insertedMarkingPoint, "an internal polyline marking point can be inserted");
AssertTrue(exercise.DeleteMarkingPoint(polylineMarkingId, insertedMarkingPoint),
    "an internal polyline marking point can be deleted without dropping below two points");
AssertTrue(!exercise.FindMarking(lineMarkingId)!.VisibleInViewer,
    "visibleInViewer=false remains persisted editor data");

IReadOnlyList<MarkingStroke> solidStrokes = MarkingGeometry.CreateStyleGeometry(
    PathSampler.Sample(exercise.FindMarking(lineMarkingId)!.Path), "solid").Strokes;
IReadOnlyList<MarkingStroke> dashedStrokes = MarkingGeometry.CreateStyleGeometry(
    PathSampler.Sample(exercise.FindMarking(polylineMarkingId)!.Path), "dashed").Strokes;
IReadOnlyList<Point2Dto> dottedPoints = MarkingGeometry.CreateStyleGeometry(
    PathSampler.Sample(exercise.FindMarking(polylineMarkingId)!.Path), "dotted").Dots;
AssertEqual(1, solidStrokes.Count, "solid rendering keeps one stroke per line section");
AssertTrue(dashedStrokes.Count > 1, "dashed rendering produces temporary separated strokes");
AssertTrue(dottedPoints.Count > dashedStrokes.Count, "dotted rendering produces a denser batched pattern");

string exerciseJson = ExerciseDefinitionStore.Serialize(exercise.Definition);
AssertTrue(exerciseJson.Contains("\"formatVersion\": 3"), "Exercise Definition saves formatVersion 3 as indented JSON");
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
    AssertEqual("none", reopened.Cones[1].Color, "Exercise JSON round-trip retains color none");
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
AssertEqual(2, mismatchResult.Warnings.Count,
    "entry and exit mismatches produce diagnostic warnings without runtime migration");
AssertEqual(-2.0f, mismatchResult.Definition.EntryPoint.X,
    "trajectory start replaces mismatched EntryPoint in memory");
AssertEqual(6.0f, mismatchResult.Definition.ExitPoint.Y,
    "trajectory end replaces mismatched ExitPoint in memory");
AssertTrue(mismatchSource.Contains("\"x\"") && mismatchSource.Contains("99"),
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
        """{"formatVersion":3,"exercise":{"id":"bad-bezier","name":"Bad Bezier","version":1},"bounds":{"width":10,"length":10},"cones":[],"markings":[],"entryPoint":{"x":0,"y":0},"exitPoint":{"x":1,"y":1},"trajectory":{"segments":[{"id":"trajectory-segment-001","type":"cubicBezier","start":{"x":0,"y":0},"control1":{"x":0,"y":1},"end":{"x":1,"y":1}}]},"checkpoints":[]}""",
        "missing-control-exercise.json"),
    "Exercise cubicBezier with a missing control point is rejected");

AssertThrows<InvalidDataException>(() => ExerciseDefinitionStore.LoadFromJson(
    """{"formatVersion":1,"exercise":{"id":"legacy","name":"Legacy","version":1},"bounds":{"width":10,"length":10},"cones":[],"markings":[],"entryPoint":{"x":0,"y":-1},"exitPoint":{"x":0,"y":1},"trajectory":{"segments":[{"id":"trajectory-segment-001","type":"polyline","points":[{"x":0,"y":-1},{"x":0,"y":1}]}]},"checkpoints":[]}""",
    "legacy.json"), "production Exercise loader rejects legacy marking formats");

TrackSnapshotDto isolatedInvalidMarking = TrackLoader.LoadFromJson(
    """{"formatVersion":5,"track":{"id":"unknown-style","name":"Unknown Style"},"venue":{"id":"venue","name":"Venue"},"area":{"width":10,"length":10},"panorama":{"enabled":false,"texturePath":"","rotationDeg":0,"energyMultiplier":1},"venueObjects":[],"elements":[],"cones":[],"markings":[{"id":"marking-001","color":"#FFFFFF","widthMeters":0.1,"style":"futureStyle","visibleInViewer":true,"path":{"start":{"x":0,"y":0},"segments":[{"type":"line","end":{"x":1,"y":0}}]}}],"trajectory":{"segments":[]},"checkpoints":[]}""",
    "unknown-style-track.json");
AssertTrue(!string.IsNullOrEmpty(isolatedInvalidMarking.Markings[0].LoadError),
    "invalid exported marking is rejected individually without breaking Viewer Track loading");

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

string temporaryTrackTestRoot = Path.Combine(Path.GetTempPath(), $"motogymkhana-track-editor-{Guid.NewGuid():N}");
try
{
    string exerciseRoot = Path.Combine(temporaryTrackTestRoot, "exercises");
    string trackRoot = Path.Combine(temporaryTrackTestRoot, "tracks");
    string venueRoot = Path.Combine(temporaryTrackTestRoot, "venues");
    var exerciseLibrary = new SandboxedJsonLibrary(exerciseRoot, "Exercise library", "res://exercises/");
    var trackLibrary = new SandboxedJsonLibrary(trackRoot, "Track Project library", "res://tracks/");
    var venueLibrary = new SandboxedJsonLibrary(venueRoot, "Venue library", "res://venues/");
    string venueFolder = venueLibrary.CreateFolder(string.Empty, "empty-ground");
    string venueFile = venueLibrary.ResolveSaveJson(venueFolder, "venue.json");
    VenueDefinitionDto emptyVenueDefinition = VenueDocument.CreateNew("empty-ground", "Empty Ground").Definition;
    emptyVenueDefinition.Area.Width = 80;
    emptyVenueDefinition.Area.Length = 120;
    VenueStore.SaveToFile(emptyVenueDefinition, venueFile);
    const string VenuePath = "empty-ground/venue.json";
    Func<string, VenueResourceKind, bool> fileProbe =
        ResolvedVenueLoader.CreateFilesystemProbe(temporaryTrackTestRoot);
    ResolvedVenue resolvedVenue = ResolvedVenueLoader.Load(
        VenuePath, venueLibrary, temporaryTrackTestRoot, fileProbe);
    TrackProjectDocument NewTrack(string id = "new-track", string name = "New Track", ResolvedVenue? venue = null) =>
        TrackProjectDocument.CreateNew(id, name, (venue ?? resolvedVenue).VenuePath, venue ?? resolvedVenue);
    TrackProjectLoadResult LoadProject(string json, string sourceName) =>
        TrackProjectStore.LoadFromJson(
            json, sourceName, exerciseLibrary, venueLibrary, temporaryTrackTestRoot, fileProbe);
    string firstExercisePath = Path.Combine(exerciseRoot, "polyline.json");
    string secondExercisePath = Path.Combine(exerciseRoot, "multi.json");
    File.Copy(Path.Combine(ProjectDirectory, "tests", "fixtures", "polyline-exercise.json"), firstExercisePath);
    File.Copy(Path.Combine(ProjectDirectory, "tests", "fixtures", "multi-segment-exercise.json"), secondExercisePath);
    ExerciseDefinitionDto firstDefinition = ExerciseDefinitionStore.LoadFromFile(firstExercisePath);
    ExerciseDefinitionDto secondDefinition = ExerciseDefinitionStore.LoadFromFile(secondExercisePath);

    TrackProjectDocument trackDocument = NewTrack();
    TrackCompilationResult emptyCompilation = TrackCompiler.Compile(trackDocument);
    AssertTrue(!emptyCompilation.CanExport && emptyCompilation.Errors.Count > 0,
        "empty Track Project is editable but blocked from Viewer export");
    AssertEqual(80.0f, trackDocument.Venue.Definition.Area.Width, "new Track Project derives width from Venue");
    AssertEqual(120.0f, trackDocument.Venue.Definition.Area.Length, "new Track Project derives length from Venue");
    AssertEqual(5.8f,
        EditorCanvasMath.FitPixelsPerMeter(100, 40, new Vector2(636, 420), 28, 3, 120),
        "Track canvas fit uses the central panel size after side-panel layout");
    AssertEqual(0, trackDocument.Project.Instances.Length, "new Track Project starts with no instances");
    AssertEqual(3, trackDocument.Project.FormatVersion, "new Track Project uses formatVersion 3");
    AssertEqual(VenuePath, trackDocument.Project.VenuePath, "new Track Project persists the selected venuePath");
    AssertEqual(0, trackDocument.Project.TransitionOverrides.Length,
        "new Track Project starts with an explicit empty transitionOverrides array");
    string emptyProjectJson = TrackProjectStore.Serialize(
        trackDocument.Project, exerciseLibrary, venueLibrary);
    TrackProjectLoadResult emptyProjectReload = LoadProject(emptyProjectJson, "empty.track.json");
    AssertEqual(0, emptyProjectReload.Project.Instances.Length,
        "an empty Track Project saves and reopens without special handling");
    AssertTrue(!emptyProjectJson.Contains("\"area\"", StringComparison.Ordinal),
        "Track Project v3 JSON contains no second area source");

    string firstInstance = trackDocument.AddInstance("polyline.json", firstDefinition);
    string secondInstance = trackDocument.AddInstance("polyline.json", firstDefinition);
    string thirdInstance = trackDocument.AddInstance("multi.json", secondDefinition);
    AssertEqual("exercise-instance-001", firstInstance, "first Track instance id is deterministic");
    AssertEqual("exercise-instance-002", secondInstance, "the same Exercise can be instantiated more than once");
    AssertEqual(3, trackDocument.Project.Instances.Length, "a different Exercise can join Route Order");

    AssertTrue(trackDocument.SetTransform(firstInstance,
        new Point2Dto { X = 10.0f, Y = -5.0f }, 90.0f,
        new Point2Dto { X = 2.0f, Y = 0.5f }), "instance position, rotation and independent scale are editable");
    Point2Dto transformed = ExerciseInstanceGeometry.TransformPoint(
        new Point2Dto { X = 2.0f, Y = 4.0f },
        trackDocument.FindInstance(firstInstance)!.Position,
        trackDocument.FindInstance(firstInstance)!.RotationDeg,
        trackDocument.FindInstance(firstInstance)!.Scale);
    AssertTrue(MathF.Abs(transformed.X - 8.0f) < 0.0001f && MathF.Abs(transformed.Y - -1.0f) < 0.0001f,
        "point transform applies scale then CCW rotation then translation");
    Point2Dto inverse = ExerciseInstanceGeometry.InverseTransformPoint(
        transformed,
        trackDocument.FindInstance(firstInstance)!.Position,
        trackDocument.FindInstance(firstInstance)!.RotationDeg,
        trackDocument.FindInstance(firstInstance)!.Scale);
    AssertTrue(MathF.Abs(inverse.X - 2.0f) < 0.0001f && MathF.Abs(inverse.Y - 4.0f) < 0.0001f,
        "inverse transform supports transformed-bounds selection");
    Point2Dto rotationOnlyInverse = ExerciseInstanceGeometry.InverseRotationTranslation(
        transformed,
        trackDocument.FindInstance(firstInstance)!.Position,
        trackDocument.FindInstance(firstInstance)!.RotationDeg);
    AssertTrue(MathF.Abs(rotationOnlyInverse.X - 4.0f) < 0.0001f &&
        MathF.Abs(rotationOnlyInverse.Y - 2.0f) < 0.0001f,
        "mouse resize inverse keeps scaled local coordinates");

    AssertTrue(trackDocument.ToggleHorizontalMirror(firstInstance),
        "horizontal mirror toggles the persisted X scale sign");
    AssertEqual(-2.0f, trackDocument.FindInstance(firstInstance)!.Scale.X,
        "horizontal reflection preserves the X size magnitude");
    Point2Dto mirrored = ExerciseInstanceGeometry.TransformPoint(
        new Point2Dto { X = 2.0f, Y = 4.0f },
        trackDocument.FindInstance(firstInstance)!.Position,
        trackDocument.FindInstance(firstInstance)!.RotationDeg,
        trackDocument.FindInstance(firstInstance)!.Scale);
    AssertTrue(MathF.Abs(mirrored.X - 8.0f) < 0.0001f && MathF.Abs(mirrored.Y - -9.0f) < 0.0001f,
        "the shared point transform reflects all exercise geometry before rotation");
    AssertTrue(trackDocument.ToggleVerticalMirror(firstInstance),
        "vertical mirror toggles the persisted Y scale sign");
    AssertEqual(-0.5f, trackDocument.FindInstance(firstInstance)!.Scale.Y,
        "vertical reflection preserves the Y size magnitude");
    AssertEqual(0.25f,
        EditorCanvasMath.Snap(new Point2Dto { X = 0.31f, Y = -0.37f }, 0.25f).X,
        "Track drag reuses the common 0.25 m snap utility");
    Point2Dto snappedDrag = EditorCanvasMath.ResolveDragPosition(
        new Point2Dto { X = 0.31f, Y = -0.37f }, 0.25f, bypassSnap: false);
    AssertEqual(0.25f, snappedDrag.X, "Exercise drag snaps X when Ctrl is not held");
    AssertEqual(-0.25f, snappedDrag.Y, "Exercise drag snaps Y when Ctrl is not held");
    Point2Dto preciseDrag = EditorCanvasMath.ResolveDragPosition(
        new Point2Dto { X = 0.31f, Y = -0.37f }, 0.25f, bypassSnap: true);
    AssertEqual(0.31f, preciseDrag.X, "Exercise drag preserves exact X while Ctrl is held");
    AssertEqual(-0.37f, preciseDrag.Y, "Exercise drag preserves exact Y while Ctrl is held");

    AssertTrue(trackDocument.MoveUp(thirdInstance), "Route Order Move Up changes instances[] order");
    AssertEqual(thirdInstance, trackDocument.Project.Instances[1].InstanceId,
        "Route Order array is the persisted traversal order");
    AssertTrue(trackDocument.MoveDown(thirdInstance), "Route Order Move Down restores the item");
    AssertTrue(trackDocument.DeleteInstance(secondInstance), "Delete removes only one Track instance");
    AssertTrue(File.Exists(firstExercisePath), "deleting an instance does not delete its Exercise Definition");

    string projectsFolder = trackLibrary.CreateFolder(string.Empty, "training");
    string projectPath = trackLibrary.ResolveSaveJson(projectsFolder, "iteration-1.json");
    TrackProjectStore.SaveToFile(trackDocument.Project, projectPath, exerciseLibrary, venueLibrary);
    TrackProjectLoadResult reopened = TrackProjectStore.LoadFromFile(
        projectPath, exerciseLibrary, venueLibrary, temporaryTrackTestRoot, fileProbe);
    AssertEqual(2, reopened.Project.Instances.Length, "saved Track Project reopens with every remaining instance");
    AssertEqual(firstInstance, reopened.Project.Instances[0].InstanceId, "instance order survives save and reload");
    AssertEqual(90.0f, reopened.Project.Instances[0].RotationDeg, "rotation survives save and reload");
    AssertEqual(-2.0f, reopened.Project.Instances[0].Scale.X, "mirrored X scale survives save and reload");
    AssertEqual(-0.5f, reopened.Project.Instances[0].Scale.Y, "mirrored Y scale survives save and reload");
    string serializedTrackProject = File.ReadAllText(projectPath);
    AssertTrue(!serializedTrackProject.Contains("selectedInstance", StringComparison.OrdinalIgnoreCase) &&
        !serializedTrackProject.Contains("zoom", StringComparison.OrdinalIgnoreCase) &&
        !serializedTrackProject.Contains("trajectory", StringComparison.OrdinalIgnoreCase),
        "Track Project excludes UI state, cached definitions and transformed geometry");

    string damagedProject = "{ damaged";
    AssertThrows<InvalidDataException>(
        () => LoadProject(damagedProject, "damaged.track.json"),
        "damaged Track Project is rejected before replacing a live document");
    AssertEqual(firstInstance, trackDocument.Project.Instances[0].InstanceId,
        "failed candidate loading leaves the current Track document intact");

    File.WriteAllText(Path.Combine(exerciseRoot, "broken.json"), "{ broken");
    string unresolvedJson =
        """{"formatVersion":3,"track":{"id":"unresolved","name":"Unresolved"},"venuePath":"empty-ground/venue.json","instances":[{"instanceId":"exercise-instance-001","exercisePath":"missing.json","position":{"x":0,"y":0},"rotationDeg":0,"scale":{"x":1,"y":1}},{"instanceId":"exercise-instance-002","exercisePath":"broken.json","position":{"x":3,"y":4},"rotationDeg":15,"scale":{"x":1,"y":1}}],"transitionOverrides":[]}""";
    TrackProjectLoadResult unresolved = LoadProject(unresolvedJson, "unresolved.track.json");
    AssertEqual(2, unresolved.Project.Instances.Length, "missing and damaged Exercise files do not remove instances");
    AssertEqual(0, unresolved.Definitions.Count, "bad Exercise dependencies remain unresolved caches");
    AssertEqual(3, unresolved.Project.FormatVersion, "Track Project remains formatVersion 3");
    AssertEqual(2, unresolved.Warnings.Count,
        "each unresolved Exercise instance produces a diagnostic warning");
    string unresolvedSaved = TrackProjectStore.Serialize(unresolved.Project, exerciseLibrary, venueLibrary);
    AssertTrue(unresolvedSaved.Contains("missing.json") &&
        unresolvedSaved.Contains("\"formatVersion\": 3") &&
        unresolvedSaved.Contains("\"venuePath\": \"empty-ground/venue.json\"") &&
        unresolvedSaved.Contains("\"transitionOverrides\": []"),
        "explicit Save preserves unresolved instances in canonical v3");

    AssertThrows<InvalidDataException>(
        () => LoadProject(
            """{"formatVersion":2,"track":{"id":"old","name":"Old"},"area":{"width":60,"length":100},"instances":[],"transitionOverrides":[]}""",
            "v2.track.json"),
        "Track Project v2 is rejected with no migration branch");
    foreach (string unsafeVenuePath in new[]
    {
        "res://venues/empty-ground/venue.json",
        "../venue.json",
        "C:/venue.json",
        "empty-ground\\venue.json",
    })
    {
        AssertThrows<InvalidDataException>(
            () => LoadProject(
                $$"""{"formatVersion":3,"track":{"id":"unsafe","name":"Unsafe"},"venuePath":"{{unsafeVenuePath.Replace("\\", "\\\\")}}","instances":[],"transitionOverrides":[]}""",
                "unsafe-venue.track.json"),
            $"unsafe venuePath '{unsafeVenuePath}' is rejected");
    }
    AssertThrows<InvalidDataException>(
        () => LoadProject(
            """{"formatVersion":3,"track":{"id":"missing","name":"Missing"},"venuePath":"missing/venue.json","instances":[],"transitionOverrides":[]}""",
            "missing-venue.track.json"),
        "missing Venue is a blocking Track Project open error");
    string corruptVenueFolder = venueLibrary.CreateFolder(string.Empty, "corrupt-ground");
    File.WriteAllText(venueLibrary.ResolveSaveJson(corruptVenueFolder, "venue.json"), "{ corrupt");
    AssertThrows<InvalidDataException>(
        () => LoadProject(
            """{"formatVersion":3,"track":{"id":"corrupt","name":"Corrupt"},"venuePath":"corrupt-ground/venue.json","instances":[],"transitionOverrides":[]}""",
            "corrupt-venue.track.json"),
        "corrupt Venue is a blocking Track Project open error");

    AssertThrows<InvalidDataException>(
        () => TrackProjectStore.LoadFromJson(
            """{"formatVersion":3,"track":{"id":"escape","name":"Escape"},"venuePath":"empty-ground/venue.json","instances":[{"instanceId":"exercise-instance-001","exercisePath":"../escape.json","position":{"x":0,"y":0},"rotationDeg":0,"scale":{"x":1,"y":1}}],"transitionOverrides":[]}""",
            "escape.track.json", exerciseLibrary, venueLibrary, temporaryTrackTestRoot, fileProbe),
        "Track Project exercisePath cannot traverse outside res://exercises/");
    TrackProjectLoadResult reflected = LoadProject(
        """{"formatVersion":3,"track":{"id":"scale","name":"Scale"},"venuePath":"empty-ground/venue.json","instances":[{"instanceId":"exercise-instance-001","exercisePath":"polyline.json","position":{"x":0,"y":0},"rotationDeg":0,"scale":{"x":-1,"y":1}}],"transitionOverrides":[]}""",
        "negative-scale.track.json");
    AssertEqual(-1.0f, reflected.Project.Instances[0].Scale.X,
        "negative non-zero scale is accepted as explicit mirror state");
    AssertThrows<InvalidDataException>(
        () => TrackProjectStore.LoadFromJson(
            """{"formatVersion":3,"track":{"id":"scale","name":"Scale"},"venuePath":"empty-ground/venue.json","instances":[{"instanceId":"exercise-instance-001","exercisePath":"polyline.json","position":{"x":0,"y":0},"rotationDeg":0,"scale":{"x":0,"y":1}}],"transitionOverrides":[]}""",
            "zero-scale.track.json", exerciseLibrary, venueLibrary, temporaryTrackTestRoot, fileProbe),
        "zero instance scale is rejected");
    AssertThrows<InvalidDataException>(
        () => trackLibrary.CreateFolder(string.Empty, "../escape"),
        "invalid Track library folder names are rejected");
    AssertThrows<InvalidDataException>(
        () => trackLibrary.ResolveUserPath(Path.Combine(trackRoot, "..", "escape.json")),
        "Track library path traversal cannot leave res://tracks/");

    ExerciseDefinitionDto compileFirst = ExerciseDefinitionStore.LoadFromFile(firstExercisePath);
    compileFirst.Markings =
    [
        new MarkingDto
        {
            Id = "hidden-dash",
            Path = PathEditing.FromPolyline([new Point2Dto { X = -1, Y = 0 }, new Point2Dto { X = 1, Y = 0 }]),
            Color = "#12ABEF",
            WidthMeters = 0.22f,
            Style = "dashed",
            VisibleInViewer = false,
        },
        new MarkingDto
        {
            Id = "visible-dot",
            Path = PathEditing.FromPolyline([new Point2Dto { X = -1, Y = 1 }, new Point2Dto { X = 0, Y = 2 }, new Point2Dto { X = 1, Y = 1 }]),
            Color = "#FF00AA",
            WidthMeters = 0.11f,
            Style = "dotted",
            VisibleInViewer = true,
        },
    ];
    ExerciseDefinitionDto compileSecond = ExerciseDefinitionStore.LoadFromFile(secondExercisePath);
    var compileDocument = NewTrack("iteration-2", "Iteration 2");
    string compileA = compileDocument.AddInstance("polyline.json", compileFirst);
    compileDocument.SetTransform(compileA, new Point2Dto { X = 3, Y = -8 }, 90,
        new Point2Dto { X = 2, Y = 0.5f });
    TrackCompilationResult oneInstanceCompilation = TrackCompiler.Compile(compileDocument);
    AssertTrue(oneInstanceCompilation.CanExport, "one resolved instance compiles without a transition");
    AssertEqual(0, oneInstanceCompilation.Transitions.Count, "single-instance export has no transition segments");
    AssertEqual(1, oneInstanceCompilation.Snapshot!.Trajectory.Segments.Length,
        "single-instance global trajectory contains only its transformed trajectory");
    AssertTrue(MathF.Abs(oneInstanceCompilation.Snapshot.Cones[0].Position.X - 3.0f) < 0.0001f &&
        MathF.Abs(oneInstanceCompilation.Snapshot.Cones[0].Position.Y - -4.0f) < 0.0001f,
        "compiler transforms cone coordinates with scale, rotation and translation");
    AssertEqual(0.22f, oneInstanceCompilation.Snapshot.Markings[0].WidthMeters,
        "instance scale does not change marking widthMeters");
    AssertTrue(!oneInstanceCompilation.Snapshot.Markings[0].VisibleInViewer &&
        oneInstanceCompilation.Snapshot.Markings[0].Style == "dashed" &&
        oneInstanceCompilation.Snapshot.Markings[1].Style == "dotted",
        "hidden state and dashed/dotted marking styles survive compilation");

    string testAssetFolder = Path.Combine(temporaryTrackTestRoot, "Assets");
    Directory.CreateDirectory(testAssetFolder);
    File.WriteAllText(Path.Combine(testAssetFolder, "shed.tscn"), "[gd_scene format=3]");
    VenueDefinitionDto richDefinition = VenueDocument.CreateNew("rich-ground", "Rich Ground").Definition;
    richDefinition.Area.Width = 40;
    richDefinition.Area.Length = 50;
    richDefinition.Objects =
    [
        new VenueObjectInstanceDto
        {
            ObjectId = "shed",
            Name = "Shed",
            AssetPath = "res://Assets/shed.tscn",
            Position = new Point2Dto { X = 3, Y = -8 },
            Elevation = 1.5f,
            RotationDeg = 20,
            Scale = new Scale3Dto { X = 2, Y = 3, Z = 4 },
            Footprint = new FootprintDto { Width = 6, Length = 8 },
            CollisionEnabled = false,
            VisibleInViewer = true,
        },
    ];
    richDefinition.Cones =
    [
        new ConeDto { Id = "venue-cone", Position = new Point2Dto { X = 1, Y = 2 }, Color = "blue", Type = "standard" },
    ];
    richDefinition.Markings =
    [
        new MarkingDto
        {
            Id = "venue-line",
            Path = PathEditing.FromPolyline([new Point2Dto { X = -2, Y = -2 }, new Point2Dto { X = 2, Y = -2 }]),
            Color = "#AABBCC", WidthMeters = 0.2f, Style = "dashed", VisibleInViewer = false,
        },
    ];
    string richFolder = venueLibrary.CreateFolder(string.Empty, "rich-ground");
    VenueStore.SaveToFile(richDefinition, venueLibrary.ResolveSaveJson(richFolder, "venue.json"));
    ResolvedVenue richVenue = ResolvedVenueLoader.Load(
        "rich-ground/venue.json", venueLibrary, temporaryTrackTestRoot, fileProbe);
    var richDocument = NewTrack("rich-track", "Rich Track", richVenue);
    richDocument.AddInstance("polyline.json", compileFirst);
    richDocument.SetTransform("exercise-instance-001", new Point2Dto { X = 3, Y = -8 }, 0,
        new Point2Dto { X = 1, Y = 1 });
    TrackCompilationResult richCompilation = TrackCompiler.Compile(richDocument);
    AssertTrue(richCompilation.CanExport, "resolved Venue object allows Track v5 export");
    AssertEqual("rich-ground", richCompilation.Snapshot!.Venue.Id, "Venue metadata is copied into export");
    AssertEqual(40.0f, richCompilation.Snapshot.Area.Width, "exported area comes from Venue");
    AssertEqual("venue--object--shed", richCompilation.Snapshot.VenueObjects[0].Id,
        "Venue object receives a scoped exported id");
    AssertEqual(1.5f, richCompilation.Snapshot.VenueObjects[0].Elevation,
        "Venue object elevation is preserved without Track transform");
    AssertEqual(3.0f, richCompilation.Snapshot.VenueObjects[0].Scale.Y,
        "Venue object Y scale is preserved separately from its 2D footprint");
    AssertEqual("venue--cone--venue-cone", richCompilation.Snapshot.Cones[0].Id,
        "Venue cones precede transformed Exercise cones in the common array");
    AssertEqual("venue--marking--venue-line", richCompilation.Snapshot.Markings[0].Id,
        "Venue markings precede transformed Exercise markings in the common array");
    AssertTrue(!richCompilation.Snapshot.Markings[0].VisibleInViewer &&
        richCompilation.Snapshot.Markings[0].Style == "dashed",
        "Venue marking style and Viewer visibility survive compilation");
    AssertEqual("polyline.json", richCompilation.Snapshot.Elements[0].ExercisePath,
        "elements keep diagnostic exercisePath metadata");
    AssertTrue(richCompilation.Warnings.Any(item => item.Message.Contains("intersect Venue object 'shed'")),
        "Exercise bounds intersecting a transformed Venue footprint produces a non-blocking warning");

    richDefinition.Objects[0].AssetPath = "res://Assets/missing.tscn";
    ResolvedVenue missingVisibleVenue = new()
    {
        VenuePath = richVenue.VenuePath,
        SourcePath = richVenue.SourcePath,
        Definition = richDefinition,
        ResolvedObjectIds = new HashSet<string>(StringComparer.Ordinal),
        PanoramaTextureResolved = false,
        Warnings = [],
    };
    var missingVisibleDocument = NewTrack("missing-visible", "Missing Visible", missingVisibleVenue);
    missingVisibleDocument.AddInstance("polyline.json", compileFirst);
    TrackCompilationResult missingVisibleCompilation = TrackCompiler.Compile(missingVisibleDocument);
    AssertTrue(!missingVisibleCompilation.CanExport &&
        missingVisibleCompilation.Errors.Any(item => item.Message.Contains("shed") && item.Message.Contains("missing.tscn")),
        "visible unresolved Venue object blocks export with object and asset context");
    richDefinition.Objects[0].VisibleInViewer = false;
    var hiddenMissingDocument = NewTrack("hidden-missing", "Hidden Missing", missingVisibleVenue);
    hiddenMissingDocument.AddInstance("polyline.json", compileFirst);
    TrackCompilationResult hiddenMissingCompilation = TrackCompiler.Compile(hiddenMissingDocument);
    AssertTrue(hiddenMissingCompilation.CanExport && hiddenMissingCompilation.Warnings.Any(item =>
        item.Message.Contains("hidden and unresolved")),
        "hidden unresolved Venue object is exported with a warning");
    richDefinition.Panorama.Enabled = true;
    richDefinition.Panorama.TexturePath = "res://Assets/missing-panorama.png";
    TrackCompilationResult missingPanoramaCompilation = TrackCompiler.Compile(hiddenMissingDocument);
    AssertTrue(!missingPanoramaCompilation.CanExport && missingPanoramaCompilation.Errors.Any(item =>
        item.Message.Contains("panorama") && item.Message.Contains("Texture2D")),
        "enabled unresolved panorama blocks export");

    string beforeReloadSnapshot = TrackProjectStore.SerializeHistorySnapshot(richDocument.Project);
    richDocument.ReplaceVenue(richVenue);
    AssertEqual(beforeReloadSnapshot, TrackProjectStore.SerializeHistorySnapshot(richDocument.Project),
        "replacing a resolved Venue dependency does not mutate Track Project or dirty history data");

    string compileB = compileDocument.AddInstance("multi.json", compileSecond);
    compileDocument.SetTransform(compileB, new Point2Dto { X = 18, Y = 20 }, -20,
        new Point2Dto { X = 0.75f, Y = 1.5f });
    TrackCompilationResult twoInstanceCompilation = TrackCompiler.Compile(compileDocument);
    AssertTrue(twoInstanceCompilation.CanExport, "two resolved instances compile with an automatic transition");
    AssertEqual(1, twoInstanceCompilation.Transitions.Count, "one transition is generated for two instances");
    AssertEqual(5, twoInstanceCompilation.Snapshot!.Trajectory.Segments.Length,
        "global trajectory keeps polyline, transition, then all cubicBezier/polyline segments");
    AssertEqual("exercise-instance-001--fixture-polyline",
        twoInstanceCompilation.Snapshot.Trajectory.Segments[0].Id,
        "internal segment id is scoped by instanceId");
    AssertEqual("transition--exercise-instance-001--exercise-instance-002",
        twoInstanceCompilation.Snapshot.Trajectory.Segments[1].Id,
        "transition id is deterministic from the adjacent instance pair");
    AssertEqual("cubicBezier", twoInstanceCompilation.Snapshot.Trajectory.Segments[1].Type,
        "automatic transition remains cubicBezier in exported JSON");
    AssertTrue(twoInstanceCompilation.Snapshot.Trajectory.Segments[1].Points is null,
        "Bezier rendering samples are not persisted in exported geometry");

    CompiledTransition automaticTransition = twoInstanceCompilation.Transitions[0];
    Point2Dto editedControl1 = new()
    {
        X = automaticTransition.Control1.X + 2.25f,
        Y = automaticTransition.Control1.Y - 1.5f,
    };
    AssertTrue(compileDocument.SetTransitionControlPoint(automaticTransition, 1, editedControl1),
        "first manual handle edit creates a TransitionOverride");
    TrackCompilationResult firstManualCompilation = TrackCompiler.Compile(compileDocument);
    AssertEqual(1, compileDocument.Project.TransitionOverrides.Length,
        "first edit creates exactly one override");
    AssertTrue(firstManualCompilation.Transitions[0].SourceMode == TransitionSourceMode.Override,
        "compiled transition reports Override mode after first edit");
    AssertEqual(editedControl1.X, firstManualCompilation.Transitions[0].Control1.X,
        "manual control1 is applied to derived transition geometry");

    Point2Dto editedControl2 = new()
    {
        X = firstManualCompilation.Transitions[0].Control2.X - 1.75f,
        Y = firstManualCompilation.Transitions[0].Control2.Y + 2.0f,
    };
    AssertTrue(compileDocument.SetTransitionControlPoint(
        firstManualCompilation.Transitions[0], 2, editedControl2),
        "control2 can be edited after the override exists");
    AssertEqual(1, compileDocument.Project.TransitionOverrides.Length,
        "repeated handle edits do not create duplicate overrides");
    TransitionOverrideDto savedOverride = compileDocument.Project.TransitionOverrides[0];
    Point2Dto savedOffset1 = new() { X = savedOverride.Control1Offset.X, Y = savedOverride.Control1Offset.Y };
    Point2Dto savedOffset2 = new() { X = savedOverride.Control2Offset.X, Y = savedOverride.Control2Offset.Y };

    string manualProjectJson = TrackProjectStore.Serialize(
        compileDocument.Project, exerciseLibrary, venueLibrary);
    TrackProjectLoadResult manualReload = LoadProject(manualProjectJson, "manual-v3.track.json");
    var manualReloadDocument = new TrackProjectDocument(
        manualReload.Project, manualReload.Venue, manualReload.Definitions);
    TrackCompilationResult manualReloadCompilation = TrackCompiler.Compile(manualReloadDocument);
    AssertTrue(manualReloadCompilation.Transitions[0].SourceMode == TransitionSourceMode.Override,
        "saved and reopened Track Project restores manual transition mode");
    AssertEqual(editedControl2.Y, manualReloadCompilation.Transitions[0].Control2.Y,
        "saved and reopened Track Project restores the manual curve shape");

    TrackProjectInstanceDto movedFrom = compileDocument.FindInstance(compileA)!;
    Point2Dto oldFromPosition = new() { X = movedFrom.Position.X, Y = movedFrom.Position.Y };
    compileDocument.MoveInstance(compileA,
        new Point2Dto { X = oldFromPosition.X + 3.0f, Y = oldFromPosition.Y + 1.0f });
    TrackCompilationResult movedManual = TrackCompiler.Compile(compileDocument);
    AssertTrue(MathF.Abs(movedManual.Transitions[0].Start.X - automaticTransition.Start.X) > 0.001f,
        "manual transition endpoint follows a moved instance");
    AssertEqual(savedOffset1.X,
        movedManual.Transitions[0].Control1.X - movedManual.Transitions[0].Start.X,
        "control1 offset remains unchanged when its endpoint moves");
    AssertEqual(savedOffset2.Y,
        movedManual.Transitions[0].Control2.Y - movedManual.Transitions[0].End.Y,
        "control2 offset remains unchanged when the other endpoint is unchanged");
    compileDocument.MoveInstance(compileA, oldFromPosition);

    TrackProjectInstanceDto transformedManualFrom = compileDocument.FindInstance(compileA)!;
    float oldManualRotation = transformedManualFrom.RotationDeg;
    Point2Dto oldManualScale = new()
    {
        X = transformedManualFrom.Scale.X,
        Y = transformedManualFrom.Scale.Y,
    };
    compileDocument.SetTransform(compileA, transformedManualFrom.Position,
        oldManualRotation + 17.0f, new Point2Dto { X = 1.7f, Y = 0.8f });
    TrackCompilationResult rotatedScaledManual = TrackCompiler.Compile(compileDocument);
    AssertEqual(savedOffset1.Y,
        rotatedScaledManual.Transitions[0].Control1.Y - rotatedScaledManual.Transitions[0].Start.Y,
        "manual offsets remain track-space constants after rotation and non-uniform scale");
    compileDocument.SetTransform(compileA, transformedManualFrom.Position,
        oldManualRotation, oldManualScale);

    string manualExportRoot = Path.Combine(temporaryTrackTestRoot, "exports", "tracks");
    var manualExportLibrary = new SandboxedJsonLibrary(
        manualExportRoot, "Track export library", "res://exports/tracks/");
    string manualExportPath = manualExportLibrary.ResolveSaveJson(string.Empty, "manual-transition.json");
    TrackCompilationResult manualForExport = TrackCompiler.Compile(compileDocument);
    TrackExportStore.SaveToFile(manualForExport.Snapshot!, manualExportPath);
    TrackSnapshotDto manualViewerTrack = TrackLoader.LoadFromJson(
        File.ReadAllText(manualExportPath), manualExportPath);
    AssertEqual(savedOverride.TransitionId, manualViewerTrack.Trajectory.Segments[1].Id,
        "manual transition keeps the same exported segment id");
    AssertEqual(manualForExport.Transitions[0].Control1.X,
        manualViewerTrack.Trajectory.Segments[1].Control1!.X,
        "Viewer export contains manual control points and no override metadata");

    float finiteOffset = manualReloadDocument.Project.TransitionOverrides[0].Control1Offset.X;
    float finiteOffsetY = manualReloadDocument.Project.TransitionOverrides[0].Control1Offset.Y;
    manualReloadDocument.Project.TransitionOverrides[0].Control1Offset =
        new Point2Dto { X = float.NaN, Y = finiteOffsetY };
    TrackCompilationResult nonFiniteOverride = TrackCompiler.Compile(manualReloadDocument);
    AssertTrue(!nonFiniteOverride.CanExport &&
        nonFiniteOverride.Errors.Any(item => item.Message.Contains("non-finite offset")),
        "non-finite TransitionOverride offset blocks export without throwing");
    manualReloadDocument.Project.TransitionOverrides[0].Control1Offset =
        new Point2Dto { X = finiteOffset, Y = finiteOffsetY };

    TrackProjectLoadResult deletionReload = LoadProject(manualProjectJson, "manual-delete.track.json");
    var deletionDocument = new TrackProjectDocument(
        deletionReload.Project, deletionReload.Venue, deletionReload.Definitions);
    AssertTrue(deletionDocument.DeleteInstance(compileB),
        "an instance related to a manual transition can be deleted");
    AssertEqual(1, deletionDocument.Project.TransitionOverrides.Length,
        "deleting an instance preserves its related override as orphaned");
    AssertTrue(TrackCompiler.Compile(deletionDocument).Warnings.Any(item => item.Message.Contains("orphaned")),
        "deleting a related instance produces an orphaned override warning");

    AssertTrue(manualReloadDocument.ResetTransition(compileA, compileB),
        "Reset to Automatic removes the applicable override");
    AssertEqual(0, manualReloadDocument.Project.TransitionOverrides.Length,
        "reset removes persisted manual data");
    AssertTrue(TrackCompiler.Compile(manualReloadDocument).Transitions[0].SourceMode ==
        TransitionSourceMode.Automatic,
        "reset recompiles the automatic transition");

    AssertThrows<InvalidDataException>(
        () => TrackProjectStore.LoadFromJson(
            manualProjectJson.Replace("\"transitionOverrides\": [", "\"transitionOverrides\": [" +
                "{\"transitionId\":\"duplicate\",\"fromInstanceId\":\"exercise-instance-001\",\"toInstanceId\":\"exercise-instance-002\",\"control1Offset\":{\"x\":1,\"y\":1},\"control2Offset\":{\"x\":-1,\"y\":-1}},"),
            "duplicate-override.track.json", exerciseLibrary, venueLibrary,
            temporaryTrackTestRoot, fileProbe),
        "duplicate override pair is a blocking Track Project validation error");

    Point2Dto transformedDirection = ExerciseInstanceGeometry.TransformDirection(
        new Point2Dto { X = 2, Y = 3 }, 90, new Point2Dto { X = 2, Y = 0.5f });
    AssertTrue(MathF.Abs(transformedDirection.X - -1.5f) < 0.0001f &&
        MathF.Abs(transformedDirection.Y - 4.0f) < 0.0001f,
        "tangent transform applies non-uniform scale before rotation and omits translation");

    string compileC = compileDocument.AddInstance("polyline.json", compileFirst);
    compileDocument.SetTransform(compileC, new Point2Dto { X = -18, Y = 30 }, 180,
        new Point2Dto { X = 1, Y = 1 });
    TrackCompilationResult multiCompilation = TrackCompiler.Compile(compileDocument);
    AssertEqual(2, multiCompilation.Transitions.Count, "three instances generate two ordered transitions");
    AssertEqual(multiCompilation.Snapshot!.Cones.Length,
        multiCompilation.Snapshot.Cones.Select(cone => cone.Id).Distinct().Count(),
        "multiple instances of one definition produce unique exported cone ids");

    compileDocument.MoveUp(compileC);
    TrackCompilationResult reordered = TrackCompiler.Compile(compileDocument);
    AssertTrue(reordered.Warnings.Any(item => item.Message.Contains("orphaned")),
        "reorder preserves but warns about a now-orphaned override");
    AssertEqual(1, compileDocument.Project.TransitionOverrides.Length,
        "reorder never deletes persisted manual transition data");
    AssertEqual("exercise-instance-003--fixture-polyline", reordered.Snapshot!.Trajectory.Segments[2].Id,
        "Route Order changes the global segment sequence without changing stable ids");
    compileDocument.MoveDown(compileC);
    AssertTrue(TrackCompiler.Compile(compileDocument).Transitions[0].SourceMode ==
        TransitionSourceMode.Override,
        "restoring adjacency reapplies the preserved override");
    compileDocument.MoveUp(compileC);
    AssertEqual(1, compileDocument.RemoveOrphanedTransitionOverrides(),
        "confirmed orphan cleanup removes the orphaned override explicitly");
    AssertEqual(0, compileDocument.Project.TransitionOverrides.Length,
        "orphan cleanup leaves no hidden override records");
    Point2Dto transitionStartBeforeMove = reordered.Transitions[0].Start!;
    compileDocument.MoveInstance(compileA, new Point2Dto { X = 6, Y = -6 });
    TrackCompilationResult movedCompilation = TrackCompiler.Compile(compileDocument);
    AssertTrue(MathF.Abs(movedCompilation.Transitions[0].Start!.X - transitionStartBeforeMove.X) > 0.001f ||
        MathF.Abs(movedCompilation.Transitions[0].Start!.Y - transitionStartBeforeMove.Y) > 0.001f,
        "moving an instance recomputes derived transition endpoints");
    Point2Dto controlBeforeTransform = movedCompilation.Transitions[0].Control1!;
    TrackProjectInstanceDto transformedA = compileDocument.FindInstance(compileA)!;
    compileDocument.SetTransform(compileA, transformedA.Position, transformedA.RotationDeg + 25.0f,
        new Point2Dto { X = 1.4f, Y = 0.65f });
    TrackCompilationResult transformedCompilation = TrackCompiler.Compile(compileDocument);
    AssertTrue(MathF.Abs(transformedCompilation.Transitions[0].Control1!.X - controlBeforeTransform.X) > 0.001f ||
        MathF.Abs(transformedCompilation.Transitions[0].Control1!.Y - controlBeforeTransform.Y) > 0.001f,
        "rotation and non-uniform scale recompute transition tangent handles");

    string exportRoot = Path.Combine(temporaryTrackTestRoot, "exports", "tracks");
    var exportLibrary = new SandboxedJsonLibrary(exportRoot, "Track export library", "res://exports/tracks/");
    string exportedPath = exportLibrary.ResolveSaveJson(string.Empty, "iteration-2.json");
    TrackExportStore.SaveToFile(movedCompilation.Snapshot!, exportedPath);
    TrackSnapshotDto viewerRoundTrip = TrackLoader.LoadFromJson(File.ReadAllText(exportedPath), exportedPath);
    AssertEqual(5, viewerRoundTrip.FormatVersion, "Viewer loader accepts compiled formatVersion 5 export");
    AssertEqual(movedCompilation.Snapshot!.Trajectory.Segments.Length,
        viewerRoundTrip.Trajectory.Segments.Length,
        "Viewer receives complete global trajectory including transitions");
    AssertTrue(File.ReadAllText(exportedPath).Contains("\"checkpoints\": []"),
        "canonical Viewer export contains an explicit empty checkpoints array");
    string exportedJson = File.ReadAllText(exportedPath);
    AssertTrue(!exportedJson.Contains("\"area\": {\n    \"name\"") &&
        !exportedJson.Contains("\"area\": {\r\n    \"name\""),
        "canonical Viewer export does not add undocumented area UI metadata");
    AssertThrows<InvalidDataException>(
        () => exportLibrary.ResolveUserPath(Path.Combine(exportRoot, "..", "escape.json")),
        "Viewer export path cannot traverse outside res://exports/tracks/");

    var unresolvedDocument = new TrackProjectDocument(
        unresolved.Project, unresolved.Venue, unresolved.Definitions);
    TrackCompilationResult unresolvedCompilation = TrackCompiler.Compile(unresolvedDocument);
    AssertTrue(!unresolvedCompilation.CanExport && unresolvedCompilation.Errors.Count >= 2,
        "unresolved Exercise Definitions block export without throwing");

    ExerciseDefinitionDto zeroTangent = ExerciseDefinitionStore.LoadFromFile(firstExercisePath);
    zeroTangent.Trajectory.Segments[0].Points =
    [new Point2Dto { X = 0, Y = 0 }, new Point2Dto { X = 0, Y = 0 }];
    zeroTangent.EntryPoint = new Point2Dto { X = 0, Y = 0 };
    zeroTangent.ExitPoint = new Point2Dto { X = 0, Y = 0 };
    var zeroDocument = NewTrack("zero", "Zero tangent");
    zeroDocument.AddInstance("polyline.json", zeroTangent);
    TrackCompilationResult zeroCompilation = TrackCompiler.Compile(zeroDocument);
    AssertTrue(!zeroCompilation.CanExport &&
        zeroCompilation.Errors.Any(error => error.Message.Contains("invalid trajectory segment")),
        "zero tangent is reported as a blocking validation error");

    ExerciseDefinitionDto damagedTrajectory = ExerciseDefinitionStore.LoadFromFile(firstExercisePath);
    damagedTrajectory.Trajectory.Segments[0].Points = [new Point2Dto { X = 0, Y = 0 }];
    var damagedDocument = NewTrack("damaged", "Damaged trajectory");
    damagedDocument.AddInstance("polyline.json", damagedTrajectory);
    AssertTrue(!TrackCompiler.Compile(damagedDocument).CanExport,
        "damaged trajectory blocks export without crashing the compiler");

    VenueDefinitionDto smallDefinition = VenueDocument.CreateNew("small-ground", "Small Ground").Definition;
    smallDefinition.Area.Width = 10;
    smallDefinition.Area.Length = 10;
    string smallFolder = venueLibrary.CreateFolder(string.Empty, "small-ground");
    VenueStore.SaveToFile(smallDefinition, venueLibrary.ResolveSaveJson(smallFolder, "venue.json"));
    ResolvedVenue smallVenue = ResolvedVenueLoader.Load(
        "small-ground/venue.json", venueLibrary, temporaryTrackTestRoot, fileProbe);
    var outsideDocument = NewTrack("outside", "Outside", smallVenue);
    string outsideId = outsideDocument.AddInstance("polyline.json", firstDefinition);
    outsideDocument.MoveInstance(outsideId, new Point2Dto { X = 50, Y = 50 });
    TrackCompilationResult outsideCompilation = TrackCompiler.Compile(outsideDocument);
    AssertTrue(outsideCompilation.CanExport && outsideCompilation.Warnings.Count > 0,
        "geometry outside area is a non-blocking warning");

    var duplicateDocument = NewTrack("duplicate", "Duplicate");
    string duplicateSource = duplicateDocument.AddInstance("polyline.json", firstDefinition);
    duplicateDocument.SetTransform(duplicateSource, new Point2Dto { X = 4, Y = -2 }, 35,
        new Point2Dto { X = -1.25f, Y = 0.75f });
    string duplicateCopy = duplicateDocument.DuplicateInstance(
        duplicateSource, new Point2Dto { X = 1, Y = 1 })!;
    AssertEqual("exercise-instance-002", duplicateCopy,
        "Duplicate creates a predictable unique instance id");
    AssertEqual(duplicateCopy, duplicateDocument.Project.Instances[1].InstanceId,
        "Duplicate is inserted immediately after its source in Route Order");
    AssertEqual(5.0f, duplicateDocument.FindInstance(duplicateCopy)!.Position.X,
        "Duplicate applies the documented +1 m X offset");
    AssertEqual(-1.0f, duplicateDocument.FindInstance(duplicateCopy)!.Position.Y,
        "Duplicate applies the documented +1 m Y offset");
    AssertEqual(-1.25f, duplicateDocument.FindInstance(duplicateCopy)!.Scale.X,
        "Duplicate preserves reflection, scale and rotation");
    AssertEqual(0, duplicateDocument.Project.TransitionOverrides.Length,
        "Duplicate does not synthesize or copy TransitionOverride data");

    string historyInitial = TrackProjectStore.SerializeHistorySnapshot(duplicateDocument.Project);
    var history = new TrackProjectHistory(100);
    history.Reset(historyInitial, saved: true);
    duplicateDocument.MoveInstance(duplicateCopy, new Point2Dto { X = 8, Y = 9 });
    string historyMoved = TrackProjectStore.SerializeHistorySnapshot(duplicateDocument.Project);
    AssertTrue(history.Commit(historyMoved, "Move instance"),
        "one completed transform creates one history entry");
    AssertTrue(history.IsDirty && history.CanUndo, "an edit after Save is dirty and undoable");
    TrackProjectLoadResult undoneHistory = TrackProjectStore.RestoreHistorySnapshot(
        history.Undo()!, exerciseLibrary, resolvedVenue);
    AssertEqual(-1.0f, undoneHistory.Project.Instances[1].Position.Y,
        "Undo restores the persisted project snapshot");
    AssertTrue(!history.IsDirty, "Undo back to the saved revision returns the document to clean");
    AssertEqual(9.0f, TrackProjectStore.RestoreHistorySnapshot(
        history.Redo()!, exerciseLibrary, resolvedVenue).Project.Instances[1].Position.Y,
        "Redo restores the later persisted state");
    history.Undo();
    duplicateDocument = new TrackProjectDocument(
        undoneHistory.Project, undoneHistory.Venue, undoneHistory.Definitions);
    duplicateDocument.Project.Track.Name = "Branched edit";
    history.Commit(TrackProjectStore.SerializeHistorySnapshot(duplicateDocument.Project), "Rename track");
    AssertTrue(!history.CanRedo, "a new edit after Undo clears Redo history");
    history.MarkSaved();
    AssertTrue(!history.IsDirty, "successful Save marks the current revision clean");
    AssertTrue(!TrackProjectStore.SerializeHistorySnapshot(duplicateDocument.Project)
            .Contains("locked", StringComparison.OrdinalIgnoreCase),
        "history snapshots contain no editor-only lock state");

    var unresolvedDuplicateDocument = new TrackProjectDocument(
        unresolved.Project, unresolved.Venue, unresolved.Definitions);
    string unresolvedDuplicate = unresolvedDuplicateDocument.DuplicateInstance(
        "exercise-instance-001", new Point2Dto { X = 1, Y = 1 })!;
    AssertEqual(3, unresolvedDuplicateDocument.Project.Instances.Length,
        "an unresolved instance can be duplicated without a resolved definition cache");
    AssertEqual("missing.json", unresolvedDuplicateDocument.FindInstance(unresolvedDuplicate)!.ExercisePath,
        "an unresolved duplicate preserves its safe exercisePath");

    ExerciseDefinitionDto routingOnly = ExerciseDefinitionStore.LoadFromFile(firstExercisePath);
    routingOnly.Exercise = new ExerciseMetadataDto
    {
        Id = "routing-only",
        Name = "Routing Only",
        Version = 1,
    };
    routingOnly.Cones = [];
    string routingJson = ExerciseDefinitionStore.Serialize(routingOnly);
    ExerciseDefinitionDto routingReload = ExerciseDefinitionStore.LoadFromJson(
        routingJson, "routing-only.json");
    AssertEqual(0, routingReload.Cones.Length,
        "Exercise Definition with zero cones remains valid when trajectory is valid");
    var routingDocument = NewTrack("routing-track", "Routing Track");
    routingDocument.AddInstance("polyline.json", routingReload);
    routingDocument.AddInstance("polyline.json", routingReload);
    routingDocument.MoveInstance("exercise-instance-002", new Point2Dto { X = 8, Y = 4 });
    TrackCompilationResult routingCompilation = TrackCompiler.Compile(routingDocument);
    AssertTrue(routingCompilation.CanExport && routingCompilation.Transitions.Count == 1,
        "routing-only instances participate in route transitions and export");
    AssertEqual(0, routingCompilation.Snapshot!.Cones.Length,
        "routing-only export emits no artificial cones");

    string[] previewArguments = ViewerPreviewLauncher.BuildArguments(
        ProjectDirectory, Path.Combine(exportRoot, "_preview", "current-track-preview.json"));
    AssertEqual("--track", previewArguments[^2],
        "Viewer preview launch uses a path-only exported Track argument");
    AssertEqual(previewArguments[^1],
        MotoGymkhanaTrainer.Viewer.TrackViewer.FindStartupTrackPath(previewArguments)!,
        "Viewer startup extracts exactly the exported Track path");
}
finally
{
    if (Directory.Exists(temporaryTrackTestRoot))
    {
        Directory.Delete(temporaryTrackTestRoot, recursive: true);
    }
}

{
    var projectExerciseLibrary = new SandboxedJsonLibrary(
        Path.Combine(ProjectDirectory, "exercises"), "Exercise library", "res://exercises/");
    var projectVenueLibrary = new SandboxedJsonLibrary(
        Path.Combine(ProjectDirectory, "venues"), "Venue library", "res://venues/");
    Func<string, VenueResourceKind, bool> projectFileProbe =
        ResolvedVenueLoader.CreateFilesystemProbe(ProjectDirectory);
    foreach (string projectPath in Directory.GetFiles(
        Path.Combine(ProjectDirectory, "tracks", "Test"), "*.json", SearchOption.TopDirectoryOnly))
    {
        TrackProjectLoadResult loadedProject = TrackProjectStore.LoadFromFile(
            projectPath, projectExerciseLibrary, projectVenueLibrary, ProjectDirectory, projectFileProbe);
        AssertEqual(3, loadedProject.Project.FormatVersion,
            $"repository Track Project '{Path.GetFileName(projectPath)}' opens as strict v3");
        AssertTrue(!File.ReadAllText(projectPath).Contains("\"area\"", StringComparison.Ordinal),
            $"repository Track Project '{Path.GetFileName(projectPath)}' has no persisted area");
        TrackCompilationResult compiledProject = TrackCompiler.Compile(new TrackProjectDocument(
            loadedProject.Project, loadedProject.Venue, loadedProject.Definitions));
        AssertTrue(compiledProject.CanExport && compiledProject.Snapshot!.FormatVersion == 5,
            $"repository Track Project '{Path.GetFileName(projectPath)}' compiles to exported Track v5");
    }
}

string temporaryVenueRoot = Path.Combine(Path.GetTempPath(), $"venue-editor-tests-{Guid.NewGuid():N}");
try
{
    string venueLibraryRoot = Path.Combine(temporaryVenueRoot, "venues");
    string assetsRoot = Path.Combine(temporaryVenueRoot, "Assets");
    Directory.CreateDirectory(assetsRoot);
    File.WriteAllText(Path.Combine(assetsRoot, "barrier.tscn"), "[gd_scene format=3]");
    var venueLibrary = new SandboxedJsonLibrary(venueLibraryRoot, "Venue library", "res://venues/");
    AssertEqual(0, venueLibrary.EnumerateEntries().Count, "empty Venue library is created and enumerated");
    string folder = venueLibrary.CreateFolder(string.Empty, "training");
    string nested = venueLibrary.CreateFolder(folder, "indoor");
    AssertEqual(Path.Combine("training", "indoor"), nested, "Venue library supports nested folders");
    AssertThrows<InvalidDataException>(() => venueLibrary.ResolveFolder(".."),
        "Venue library rejects path traversal");
    AssertThrows<InvalidDataException>(() => venueLibrary.CreateFolder(string.Empty, "../escape"),
        "Venue library rejects invalid folder names");

    VenueDocument venue = VenueDocument.CreateNew("training-hall", "Training Hall");
    AssertEqual(60.0f, venue.Definition.Area.Width, "new Venue width defaults to 60 metres");
    AssertEqual(100.0f, venue.Definition.Area.Length, "new Venue length defaults to 100 metres");
    AssertTrue(!venue.Definition.Panorama.Enabled && venue.Definition.Panorama.EnergyMultiplier == 1.0f,
        "new Venue panorama uses documented defaults");
    var measuredVisuals = new VenueAssetVisualBounds[]
    {
        new(
            new Aabb(new Vector3(-1, -0.5f, -2), new Vector3(2, 1, 4)),
            Transform3D.Identity),
        new(
            new Aabb(Vector3.Zero, new Vector3(1, 2, 3)),
            new Transform3D(Basis.Identity, new Vector3(4, 0, -1))),
    };
    FootprintDto measuredFootprint = VenueAssetFootprint.Calculate(
        measuredVisuals, "res://Assets/barrier.tscn");
    AssertEqual(6.0f, measuredFootprint.Width,
        "Venue asset footprint combines transformed visual AABBs on local X");
    AssertEqual(4.0f, measuredFootprint.Length,
        "Venue asset footprint combines transformed visual AABBs on local Z");
    VenueObjectInstanceDto barrier = venue.AddObject(
        "res://Assets/barrier.tscn", measuredFootprint);
    AssertEqual(6.0f, barrier.Footprint.Width,
        "new Venue object persists the measured asset width instead of 1 metre");
    AssertEqual(4.0f, barrier.Footprint.Length,
        "new Venue object persists the measured asset length instead of 1 metre");
    barrier.Position = new Point2Dto { X = 3, Y = -2 };
    barrier.RotationDeg = 90;
    barrier.Scale = new Scale3Dto { X = 2, Y = 3, Z = 4 };
    barrier.Footprint = new FootprintDto { Width = 2, Length = 1 };
    Point2Dto[] footprint = VenueGeometry.TransformFootprint(barrier);
    AssertTrue(footprint.All(point => float.IsFinite(point.X) && float.IsFinite(point.Y)),
        "Venue footprint applies finite scale/rotation/translation geometry");
    VenueObjectInstanceDto barrierCopy = venue.DuplicateObject(barrier.ObjectId);
    AssertTrue(barrierCopy.ObjectId != barrier.ObjectId, "duplicated Venue object receives a unique id");
    AssertEqual(4.0f, barrierCopy.Position.X, "duplicated Venue object receives the +1 m X offset");
    AssertEqual(-1.0f, barrierCopy.Position.Y, "duplicated Venue object receives the +1 m Y offset");

    ConeDto venueCone = venue.AddCone(new Point2Dto { X = 1, Y = 2 });
    venue.SetConeColor(venueCone.Id, "none");
    MarkingDto venueLine = venue.AddMarking("line",
    [
        new Point2Dto { X = -2, Y = 0 },
        new Point2Dto { X = 2, Y = 0 },
    ]);
    venueLine.Color = "#12ABEF";
    venueLine.WidthMeters = 0.2f;
    venueLine.Style = "dashed";
    venueLine.VisibleInViewer = false;
    MarkingDto venuePolyline = venue.AddMarking("polyline",
    [
        new Point2Dto { X = 0, Y = 0 }, new Point2Dto { X = 1, Y = 1 }, new Point2Dto { X = 2, Y = 0 },
    ]);
    venue.InsertMarkingPointAfter(venuePolyline.Id, 1);
    AssertEqual(4, PathEditing.GetVertices(venuePolyline.Path).Length, "Venue polyline supports internal point insertion");
    venue.DeleteMarkingPoint(venuePolyline.Id, 2);
    AssertEqual(3, PathEditing.GetVertices(venuePolyline.Path).Length, "Venue polyline supports safe internal point deletion");

    string savePath = venueLibrary.ResolveSaveJson(nested, "training-hall.json");
    VenueStore.SaveToFile(venue.Definition, savePath);
    VenueLoadResult venueReload = VenueStore.LoadFromFile(savePath, temporaryVenueRoot);
    AssertEqual(2, venueReload.Definition.FormatVersion, "Venue Definition saves formatVersion 2");
    AssertEqual(2, venueReload.Definition.Objects.Length, "Venue object instances survive Save/Open");
    AssertEqual("none", venueReload.Definition.Cones[0].Color, "Venue cone color survives Save/Open");
    AssertEqual("dashed", venueReload.Definition.Markings[0].Style, "Venue marking style survives Save/Open");
    AssertTrue(!venueReload.Definition.Markings[0].VisibleInViewer,
        "Venue hidden marking remains persisted rather than removed");
    AssertTrue(venueReload.Warnings.Any(value => value.Contains("overlap", StringComparison.OrdinalIgnoreCase)),
        "overlapping Venue footprints produce a non-blocking warning");

    var venueHistory = new EditorSnapshotHistory(100);
    venueHistory.Reset(VenueStore.Serialize(venue.Definition), saved: true);
    venue.Definition.Venue.Name = "Changed Hall";
    venueHistory.Commit(VenueStore.Serialize(venue.Definition), "Rename Venue");
    AssertTrue(venueHistory.IsDirty && venueHistory.CanUndo, "Venue persisted edit enters snapshot history");
    venueHistory.Undo();
    AssertTrue(!venueHistory.IsDirty, "Venue Undo to saved revision restores clean state");
    venueHistory.Redo();
    AssertTrue(venueHistory.IsDirty, "Venue Redo restores dirty revision");

    AssertThrows<InvalidDataException>(() => VenueStore.LoadFromJson("{broken", "broken.json", temporaryVenueRoot),
        "corrupt Venue JSON is rejected before document replacement");
    string unsupported = VenueStore.Serialize(venue.Definition).Replace("\"formatVersion\": 2", "\"formatVersion\": 3");
    AssertThrows<InvalidDataException>(() => VenueStore.LoadFromJson(unsupported, "future.json", temporaryVenueRoot),
        "unsupported Venue version is rejected without migration");
    string unresolved = VenueStore.Serialize(venue.Definition).Replace("res://Assets/barrier.tscn", "res://Assets/missing.tscn");
    VenueLoadResult unresolvedVenue = VenueStore.LoadFromJson(unresolved, "unresolved.json", temporaryVenueRoot);
    AssertTrue(unresolvedVenue.Warnings.Any(value => value.Contains("unresolved", StringComparison.OrdinalIgnoreCase)),
        "missing .tscn is retained as an unresolved non-blocking Venue object");
    string absoluteAsset = VenueStore.Serialize(venue.Definition).Replace("res://Assets/barrier.tscn", "C:/outside.tscn");
    AssertThrows<InvalidDataException>(() => VenueStore.LoadFromJson(absoluteAsset, "absolute.json", temporaryVenueRoot),
        "Venue object rejects absolute asset paths");
}
finally
{
    if (Directory.Exists(temporaryVenueRoot)) Directory.Delete(temporaryVenueRoot, recursive: true);
}

// Viewer/Venue Physics Iteration 1 keeps the layer policy and projection sampling
// executable as small regression checks, rather than relying on scene defaults.
AssertEqual(1u, ViewerPhysicsLayers.WalkableSurface, "walkable surfaces use the semantic layer 1");
AssertEqual(2u, ViewerPhysicsLayers.WorldObstacle, "world obstacles use the semantic layer 2");
AssertEqual(3u, ViewerPhysicsLayers.CharacterMask,
    "Viewer character sees walkable surfaces and world obstacles only");
AssertEqual(1u, ViewerPhysicsLayers.ProjectionMask,
    "surface projection sees walkable surfaces only");
AssertTrue(typeof(CharacterBody3D).IsAssignableFrom(typeof(FirstPersonCamera)),
    "Viewer walk controller is a CharacterBody3D");

Point2Dto[] subdividedProjectionLine = SurfaceProjectionService.SubdividePolyline(
[
    new Point2Dto { X = 0, Y = 0 },
    new Point2Dto { X = 1, Y = 0 },
], 0.35f);
AssertEqual(4, subdividedProjectionLine.Length,
    "one metre projection interval is subdivided into three intervals");
AssertTrue(subdividedProjectionLine.Zip(subdividedProjectionLine.Skip(1))
        .All(pair => MathF.Sqrt(
            MathF.Pow(pair.Second.X - pair.First.X, 2) +
            MathF.Pow(pair.Second.Y - pair.First.Y, 2)) <= 0.3501f),
    "projection subdivision respects the maximum sample spacing");
AssertTrue(subdividedProjectionLine[0].X == 0 && subdividedProjectionLine[0].Y == 0,
    "projection subdivision preserves the first source point");
AssertTrue(subdividedProjectionLine[^1].X == 1 && subdividedProjectionLine[^1].Y == 0,
    "projection subdivision preserves the last source point");

string viewerPhysicsFixturePath = Path.Combine(
    ProjectDirectory, "tests", "fixtures", "viewer-venue-physics.json");
TrackSnapshotDto viewerPhysicsFixture = TrackLoader.LoadFromJson(
    File.ReadAllText(viewerPhysicsFixturePath), viewerPhysicsFixturePath);
AssertEqual(5, viewerPhysicsFixture.FormatVersion, "Viewer physics fixture uses exported Track v5");
AssertEqual("dashed", viewerPhysicsFixture.Markings[0].Style,
    "Viewer physics fixture covers projected dashed Venue markings");
AssertEqual("dotted", viewerPhysicsFixture.Markings[1].Style,
    "Viewer physics fixture covers projected dotted Exercise markings");
AssertEqual(3, viewerPhysicsFixture.Cones.Length,
    "Viewer physics fixture covers cones on the ramp, platform and exit ramp");
string collisionDisabledFixturePath = Path.Combine(
    ProjectDirectory, "tests", "fixtures", "viewer-venue-collision-disabled.json");
TrackSnapshotDto collisionDisabledFixture = TrackLoader.LoadFromJson(
    File.ReadAllText(collisionDisabledFixturePath), collisionDisabledFixturePath);
AssertTrue(collisionDisabledFixture.VenueObjects[0].VisibleInViewer &&
           !collisionDisabledFixture.VenueObjects[0].CollisionEnabled,
    "collision-disabled fixture keeps the Venue object visible");

// Curved Markings Iteration 1: shared contract, mathematics, world transforms,
// bounds and style phase are executable independently of Godot scene nodes.
var linePath = new PathDefinition
{
    Start = new Point2Dto { X = 0, Y = 0 },
    Segments = [new LinePathSegmentDefinition { End = new Point2Dto { X = 3, Y = 4 } }],
};
var cubicPath = new PathDefinition
{
    Start = new Point2Dto { X = 0, Y = 0 },
    Segments = [new CubicBezierPathSegmentDefinition
    {
        Control1 = new Point2Dto { X = 1, Y = 2 },
        Control2 = new Point2Dto { X = 2, Y = -2 },
        End = new Point2Dto { X = 3, Y = 0 },
    }],
};
var mixedPath = new PathDefinition
{
    Start = new Point2Dto { X = -1, Y = 0 },
    Segments =
    [
        new LinePathSegmentDefinition { End = new Point2Dto { X = 0, Y = 0 } },
        new CubicBezierPathSegmentDefinition
        {
            Control1 = new Point2Dto { X = 1, Y = 2 },
            Control2 = new Point2Dto { X = 2, Y = 2 },
            End = new Point2Dto { X = 3, Y = 0 },
        },
    ],
};
string linePathJson = JsonSerializer.Serialize(linePath);
PathDefinition lineRoundTrip = JsonSerializer.Deserialize<PathDefinition>(linePathJson)!;
AssertTrue(lineRoundTrip.Segments[0] is LinePathSegmentDefinition && !linePathJson.Contains("control1"),
    "marking line Path round-trips without cubic fields");
string cubicPathJson = JsonSerializer.Serialize(cubicPath);
PathDefinition cubicRoundTrip = JsonSerializer.Deserialize<PathDefinition>(cubicPathJson)!;
AssertTrue(cubicRoundTrip.Segments[0] is CubicBezierPathSegmentDefinition && cubicPathJson.Contains("control2"),
    "marking cubic Path round-trips with both controls");
PathDefinition mixedRoundTrip = JsonSerializer.Deserialize<PathDefinition>(JsonSerializer.Serialize(mixedPath))!;
AssertTrue(mixedRoundTrip.Segments[0] is LinePathSegmentDefinition &&
           mixedRoundTrip.Segments[1] is CubicBezierPathSegmentDefinition,
    "mixed marking Path preserves ordered segment types");
AssertThrows<JsonException>(() => JsonSerializer.Deserialize<PathDefinition>(
    """{"start":{"x":0,"y":0},"segments":[{"type":"arc","end":{"x":1,"y":0}}]}"""),
    "unknown marking Path discriminator is rejected");
AssertThrows<JsonException>(() => JsonSerializer.Deserialize<PathDefinition>(
    """{"start":{"x":0,"y":0},"segments":[{"type":"cubicBezier","control1":{"x":1,"y":0},"end":{"x":2,"y":0}}]}"""),
    "missing cubic control point is rejected during deserialization");
AssertThrows<JsonException>(() => JsonSerializer.Deserialize<PathDefinition>(
    """{"segments":[{"type":"line","end":{"x":1,"y":0}}]}"""),
    "missing Path start is rejected instead of defaulting to the origin");
var nonFinitePath = new PathDefinition
{
    Start = new Point2Dto { X = float.NaN, Y = 0 },
    Segments = [new LinePathSegmentDefinition { End = new Point2Dto { X = 1, Y = 0 } }],
};
AssertThrows<InvalidDataException>(() => PathValidator.ValidateOrThrow(nonFinitePath, "markings[7].path"),
    "non-finite marking coordinate is rejected with a marking path");

SampledPath sampledLine = PathSampler.Sample(linePath);
AssertEqual(2, sampledLine.Points.Length, "line sampling returns exact start and end");
var twoLines = PathEditing.FromPolyline([
    new Point2Dto { X = 0, Y = 0 }, new Point2Dto { X = 1, Y = 0 }, new Point2Dto { X = 2, Y = 0 }]);
SampledPath sampledTwoLines = PathSampler.Sample(twoLines);
AssertEqual(3, sampledTwoLines.Points.Length, "two line segments sample without duplicate join points");
SampledPath sampledCubic = PathSampler.Sample(cubicPath);
AssertTrue(sampledCubic.Points[0].X == 0 && sampledCubic.Points[^1].X == 3,
    "adaptive cubic sampling preserves exact endpoints");
var nearlyStraight = new PathDefinition
{
    Start = new Point2Dto { X = 0, Y = 0 },
    Segments = [new CubicBezierPathSegmentDefinition
    {
        Control1 = new Point2Dto { X = 1, Y = 0.001f }, Control2 = new Point2Dto { X = 2, Y = -0.001f },
        End = new Point2Dto { X = 3, Y = 0 },
    }],
};
AssertTrue(MathF.Abs(PathSampler.Sample(nearlyStraight).TotalLength - 3.0f) < 0.01f,
    "nearly straight cubic has stable approximate length");
AssertTrue(sampledCubic.Points.Any(point => point.Y > 0.1f) && sampledCubic.Points.Any(point => point.Y < -0.1f),
    "adaptive sampler resolves an S-curve on both sides of its chord");
var tightCurve = new PathDefinition
{
    Start = new Point2Dto { X = 0, Y = 0 },
    Segments = [new CubicBezierPathSegmentDefinition
    {
        Control1 = new Point2Dto { X = 0, Y = 5 }, Control2 = new Point2Dto { X = 0.1f, Y = -5 },
        End = new Point2Dto { X = 0.1f, Y = 0 },
    }],
};
AssertTrue(PathSampler.Sample(tightCurve).Points.Length > 10,
    "tight cubic receives additional adaptive subdivisions");
var pathological = new PathDefinition
{
    Start = new Point2Dto { X = 0, Y = 0 },
    Segments = [new CubicBezierPathSegmentDefinition
    {
        Control1 = new Point2Dto { X = 100000, Y = 100000 }, Control2 = new Point2Dto { X = -100000, Y = -100000 },
        End = new Point2Dto { X = 0, Y = 0 },
    }],
};
AssertTrue(PathSampler.Sample(pathological).Points.Length <= 4097,
    "adaptive sampler maximum recursion bounds pathological curves");
var collinearOvershoot = new PathDefinition
{
    Start = new Point2Dto { X = 0, Y = 0 },
    Segments = [new CubicBezierPathSegmentDefinition
    {
        Control1 = new Point2Dto { X = 10, Y = 0 },
        Control2 = new Point2Dto { X = -10, Y = 0 },
        End = new Point2Dto { X = 0.1f, Y = 0 },
    }],
};
SampledPath overshootSample = PathSampler.Sample(collinearOvershoot);
AssertTrue(overshootSample.Points.Length > 2 && overshootSample.TotalLength > 1,
    "collinear cubic overshoot is not collapsed to its short endpoint chord");
var duplicateControls = new PathDefinition
{
    Start = new Point2Dto { X = 0, Y = 0 },
    Segments = [new CubicBezierPathSegmentDefinition
    {
        Control1 = new Point2Dto { X = 0, Y = 0 }, Control2 = new Point2Dto { X = 1, Y = 0 },
        End = new Point2Dto { X = 1, Y = 0 },
    }],
};
AssertTrue(PathSampler.Sample(duplicateControls).Points.All(point => float.IsFinite(point.X) && float.IsFinite(point.Y)),
    "duplicate cubic controls still produce finite output");
AssertEqual(5.0f, sampledLine.TotalLength, "straight sampled Path has known length");
AssertTrue(sampledCubic.TotalLength > 3.0f && sampledCubic.TotalLength < 6.0f,
    "cubic sampled length stays in a deterministic expected range");
AssertTrue(sampledCubic.CumulativeDistances.Zip(sampledCubic.CumulativeDistances.Skip(1))
        .All(pair => pair.Second > pair.First),
    "sampled cumulative distances are strictly monotonic");
AssertEqual(sampledCubic.TotalLength, sampledCubic.CumulativeDistances[^1],
    "sampled total length equals the last cumulative value");

PathBounds lineBounds = PathBoundsCalculator.Calculate(linePath, 0.0f);
AssertTrue(lineBounds.MinX == 0 && lineBounds.MaxX == 3 && lineBounds.MinY == 0 && lineBounds.MaxY == 4,
    "line Path bounds include both endpoints");
var xExtremum = new PathDefinition
{
    Start = new Point2Dto { X = 0, Y = 0 }, Segments = [new CubicBezierPathSegmentDefinition
    { Control1 = new Point2Dto { X = 2, Y = 0 }, Control2 = new Point2Dto { X = 2, Y = 0 }, End = new Point2Dto { X = 0, Y = 0 } }],
};
AssertTrue(MathF.Abs(PathBoundsCalculator.Calculate(xExtremum, 0).MaxX - 1.5f) < 0.0001f,
    "cubic bounds include an interior X extremum");
var yExtremum = new PathDefinition
{
    Start = new Point2Dto { X = 0, Y = 0 }, Segments = [new CubicBezierPathSegmentDefinition
    { Control1 = new Point2Dto { X = 0, Y = -2 }, Control2 = new Point2Dto { X = 0, Y = -2 }, End = new Point2Dto { X = 0, Y = 0 } }],
};
AssertTrue(MathF.Abs(PathBoundsCalculator.Calculate(yExtremum, 0).MinY + 1.5f) < 0.0001f,
    "cubic bounds include an interior Y extremum");
PathBounds expandedBounds = PathBoundsCalculator.Calculate(linePath, 0.4f);
AssertTrue(MathF.Abs(expandedBounds.MinX + 0.2f) < 0.0001f && MathF.Abs(expandedBounds.MaxY - 4.2f) < 0.0001f,
    "marking bounds expand by half the world thickness");

PathDefinition translated = PathTransformService.Transform(mixedPath,
    point => new Point2Dto { X = point.X + 5, Y = point.Y - 2 });
AssertTrue(translated.Start.X == 4 && ((CubicBezierPathSegmentDefinition)translated.Segments[1]).Control1.X == 6,
    "Path transform applies translation to start and cubic controls");
PathDefinition rotated = PathTransformService.Transform(linePath,
    point => ExerciseInstanceGeometry.TransformPoint(point, new Point2Dto(), 90, new Point2Dto { X = 1, Y = 1 }));
AssertTrue(MathF.Abs(rotated.Segments[0].EndPoint.X + 4) < 0.0001f && MathF.Abs(rotated.Segments[0].EndPoint.Y - 3) < 0.0001f,
    "Path transform applies rotation in project coordinates");
PathDefinition uniformScaled = PathTransformService.Transform(linePath,
    point => ExerciseInstanceGeometry.TransformPoint(point, new Point2Dto(), 0, new Point2Dto { X = 2, Y = 2 }));
AssertEqual(10.0f, PathSampler.Sample(uniformScaled).TotalLength, "uniform scale changes world Path length");
PathDefinition nonUniformScaled = PathTransformService.Transform(cubicPath,
    point => ExerciseInstanceGeometry.TransformPoint(point, new Point2Dto(), 0, new Point2Dto { X = 2, Y = 0.5f }));
var scaledCubic = (CubicBezierPathSegmentDefinition)nonUniformScaled.Segments[0];
AssertTrue(scaledCubic.Control1.X == 2 && scaledCubic.Control1.Y == 1,
    "non-uniform scale transforms cubic control coordinates before sampling");
AssertTrue(nonUniformScaled.Start.X == 0 && scaledCubic.End.X == 6 && scaledCubic.Control2.X == 4,
    "transformed cubic preserves endpoints and both controls as cubic geometry");

var joinedDashPath = PathEditing.FromPolyline([
    new Point2Dto { X = 0, Y = 0 }, new Point2Dto { X = 0.5f, Y = 0 }, new Point2Dto { X = 1, Y = 0 }]);
MarkingStyleGeometry joinedDash = MarkingGeometry.CreateStyleGeometry(PathSampler.Sample(joinedDashPath), "dashed");
AssertTrue(joinedDash.Strokes.Count == 2 && MathF.Abs(joinedDash.Strokes[1].End.X - 0.75f) < 0.0001f,
    "dashed phase continues across a Path segment boundary");
MarkingStyleGeometry dottedGeometry = MarkingGeometry.CreateStyleGeometry(sampledLine, "dotted");
AssertTrue(dottedGeometry.Dots.Count > 2 && MathF.Abs(dottedGeometry.Dots[1].X - 0.192f) < 0.001f,
    "dotted centers use constant cumulative world-distance spacing");
PathDefinition scaledPatternPath = PathTransformService.Transform(joinedDashPath,
    point => ExerciseInstanceGeometry.TransformPoint(point, new Point2Dto(), 0, new Point2Dto { X = 3, Y = 0.5f }));
AssertTrue(MarkingGeometry.CreateStyleGeometry(PathSampler.Sample(scaledPatternPath), "dashed").Strokes.Count > joinedDash.Strokes.Count,
    "non-uniform scale generates marking style pattern from transformed world length");

// Curved Markings Exercise Editor Iteration 2: pure editing operations are
// verified without constructing native Godot canvas nodes.
var editablePath = new PathDefinition
{
    Start = new Point2Dto { X = 0, Y = 0 },
    Segments = [new LinePathSegmentDefinition { End = new Point2Dto { X = 3, Y = 0 } }],
};
AssertTrue(PathEditing.ConvertLineToCubic(editablePath, 0), "line converts to cubic");
var convertedCurve = (CubicBezierPathSegmentDefinition)editablePath.Segments[0];
AssertTrue(convertedCurve.Control1.X == 1 && convertedCurve.Control2.X == 2,
    "line to cubic uses one-third and two-thirds controls");
AssertTrue(Enumerable.Range(0, 11).All(step =>
    MathF.Abs(PathEditing.Evaluate(editablePath, 0, step / 10.0f).Y) < 0.000001f &&
    MathF.Abs(PathEditing.Evaluate(editablePath, 0, step / 10.0f).X - 3 * step / 10.0f) < 0.00001f),
    "converted cubic exactly matches its line");
AssertTrue(PathEditing.ConvertCubicToLine(editablePath, 0) && editablePath.Segments[0].EndPoint.X == 3,
    "cubic to line retains endpoint");

var conversionDocument = ExerciseDocument.CreateNew("conversion-history", "Conversion History", 10, 10);
string conversionId = conversionDocument.AddMarking(PathEditing.CopyPath(editablePath));
var conversionHistory = new EditorSnapshotHistory();
string conversionBefore = ExerciseDefinitionStore.Serialize(conversionDocument.Definition);
conversionHistory.Reset(conversionBefore, saved: true);
conversionDocument.ConvertMarkingSegment(conversionId, 0, toCubic: true);
string conversionAfter = ExerciseDefinitionStore.Serialize(conversionDocument.Definition);
conversionHistory.Commit(conversionAfter, "Convert marking segment");
AssertTrue(ExerciseDefinitionStore.LoadFromJson(conversionHistory.Undo()!, "conversion undo")
        .Markings[0].Path.Segments[0] is LinePathSegmentDefinition,
    "conversion Undo restores line");
AssertTrue(ExerciseDefinitionStore.LoadFromJson(conversionHistory.Redo()!, "conversion redo")
        .Markings[0].Path.Segments[0] is CubicBezierPathSegmentDefinition,
    "conversion Redo restores cubic controls");

var splitLinePath = PathEditing.FromPolyline([new Point2Dto { X = 0, Y = 0 }, new Point2Dto { X = 4, Y = 0 }]);
AssertTrue(PathEditing.SplitSegment(splitLinePath, 0, 0.25f) && splitLinePath.Segments.Length == 2 &&
           splitLinePath.Segments[0].EndPoint.X == 1,
    "line split preserves order and requested parameter");
foreach (float splitAt in new[] { 0.5f, 0.01f, 0.99f })
{
    PathDefinition original = PathEditing.CopyPath(cubicPath);
    PathDefinition split = PathEditing.CopyPath(cubicPath);
    AssertTrue(PathEditing.SplitSegment(split, 0, splitAt), $"cubic splits at {splitAt:0.##}");
    for (int step = 0; step <= 40; step++)
    {
        float t = step / 40.0f;
        Point2Dto expected = PathEditing.Evaluate(original, 0, t);
        Point2Dto actual = t <= splitAt
            ? PathEditing.Evaluate(split, 0, t / splitAt)
            : PathEditing.Evaluate(split, 1, (t - splitAt) / (1 - splitAt));
        AssertTrue(MathF.Abs(expected.X - actual.X) < 0.0001f && MathF.Abs(expected.Y - actual.Y) < 0.0001f,
            $"split cubic sampled geometry matches original at t={t:0.###}, split={splitAt:0.##}");
    }
}

var splitHistory = new EditorSnapshotHistory();
var splitDocument = ExerciseDocument.CreateNew("split-history", "Split History", 10, 10);
string splitId = splitDocument.AddMarking(cubicPath);
string splitBefore = ExerciseDefinitionStore.Serialize(splitDocument.Definition);
splitHistory.Reset(splitBefore, saved: true);
splitDocument.SplitMarkingSegment(splitId, 0, 0.5f);
string splitAfter = ExerciseDefinitionStore.Serialize(splitDocument.Definition);
splitHistory.Commit(splitAfter, "Split marking segment");
AssertEqual(1, ExerciseDefinitionStore.LoadFromJson(splitHistory.Undo()!, "split undo").Markings[0].Path.Segments.Length,
    "split Undo restores one segment");
AssertEqual(2, ExerciseDefinitionStore.LoadFromJson(splitHistory.Redo()!, "split redo").Markings[0].Path.Segments.Length,
    "split Redo restores both segments");

var dragPath = new PathDefinition
{
    Start = new Point2Dto { X = 0, Y = 0 },
    Segments =
    [
        new CubicBezierPathSegmentDefinition
        {
            Control1 = new Point2Dto { X = 1, Y = 1 }, Control2 = new Point2Dto { X = 2, Y = 1 },
            End = new Point2Dto { X = 3, Y = 0 },
        },
        new LinePathSegmentDefinition { End = new Point2Dto { X = 5, Y = 0 } },
    ],
};
PathEditing.MoveCoordinate(dragPath, -1, MarkingPathCoordinateKind.PathStart, new Point2Dto { X = -1, Y = 0 });
AssertEqual(-1.0f, dragPath.Start.X, "Path start drag changes only Path.start");
PathEditing.MoveCoordinate(dragPath, 0, MarkingPathCoordinateKind.SegmentEnd, new Point2Dto { X = 3, Y = 2 });
AssertEqual(3.0f, PathEditing.GetSegmentStart(dragPath, 1).X, "endpoint drag changes the next implicit start");
AssertEqual(2.0f, PathEditing.GetSegmentStart(dragPath, 1).Y, "shared endpoint remains continuous");
PathEditing.MoveCoordinate(dragPath, 0, MarkingPathCoordinateKind.Control1, new Point2Dto { X = 0, Y = 4 });
AssertEqual(4.0f, ((CubicBezierPathSegmentDefinition)dragPath.Segments[0]).Control1.Y, "control1 drag changes control1");
PathEditing.MoveCoordinate(dragPath, 0, MarkingPathCoordinateKind.Control2, new Point2Dto { X = 4, Y = -2 });
AssertEqual(-2.0f, ((CubicBezierPathSegmentDefinition)dragPath.Segments[0]).Control2.Y, "control2 drag changes control2");
PathDefinition movedWhole = PathEditing.Translate(dragPath, 10, -3);
AssertTrue(movedWhole.Start.X == 9 && movedWhole.Segments[1].EndPoint.X == 15 &&
           ((CubicBezierPathSegmentDefinition)movedWhole.Segments[0]).Control1.Y == 1,
    "whole marking drag translates start, endpoints and controls");

var dragHistory = new EditorSnapshotHistory();
var dragDocument = ExerciseDocument.CreateNew("drag-history", "Drag History", 10, 10);
string dragId = dragDocument.AddMarking(dragPath);
string dragBefore = ExerciseDefinitionStore.Serialize(dragDocument.Definition);
dragHistory.Reset(dragBefore, saved: true);
for (int motion = 1; motion <= 5; motion++) dragDocument.MoveMarking(dragId, 0.1f, 0);
string dragAfter = ExerciseDefinitionStore.Serialize(dragDocument.Definition);
dragHistory.Commit(dragAfter, "Move marking");
AssertEqual(dragBefore, dragHistory.Undo()!, "one history command restores a complete drag");
AssertTrue(dragHistory.Undo() is null, "one drag creates only one history command");
PathDefinition cancelBefore = PathEditing.CopyPath(dragDocument.FindMarking(dragId)!.Path);
dragDocument.MoveMarking(dragId, 4, 4);
dragDocument.ReplaceMarkingPath(dragId, cancelBefore);
AssertEqual(JsonSerializer.Serialize(cancelBefore), JsonSerializer.Serialize(dragDocument.FindMarking(dragId)!.Path),
    "cancel drag restores immutable before Path");

var structureDocument = ExerciseDocument.CreateNew("structure", "Structure", 10, 10);
string structureId = structureDocument.AddMarking(PathEditing.FromPolyline([
    new Point2Dto { X = 0, Y = 0 }, new Point2Dto { X = 1, Y = 0 }]));
AssertEqual(1, structureDocument.AppendMarkingSegment(structureId, new Point2Dto { X = 2, Y = 0 }, cubic: false),
    "append line adds one ordered segment");
AssertEqual(2, structureDocument.AppendMarkingSegment(structureId, new Point2Dto { X = 3, Y = 1 }, cubic: true),
    "append cubic selects its ordered segment index");
AssertTrue(structureDocument.FindMarking(structureId)!.Path.Segments[2] is CubicBezierPathSegmentDefinition,
    "appended cubic retains cubic domain type");
structureDocument.DeleteMarkingSegment(structureId, 0, out bool deletedFirstOwner);
AssertTrue(!deletedFirstOwner && structureDocument.FindMarking(structureId)!.Path.Segments.Length == 2,
    "delete first promotes its endpoint to Path.start");
structureDocument.SplitMarkingSegment(structureId, 0, 0.5f);
structureDocument.DeleteMarkingSegment(structureId, 1, out bool deletedInternalOwner);
AssertTrue(!deletedInternalOwner && structureDocument.FindMarking(structureId)!.Path.Segments.Length == 2,
    "delete internal retains neighboring segments");
structureDocument.DeleteMarkingSegment(structureId, 1, out bool deletedLastOwner);
AssertTrue(!deletedLastOwner && structureDocument.FindMarking(structureId)!.Path.Segments.Length == 1,
    "delete last retains the preceding segment");
structureDocument.DeleteMarkingSegment(structureId, 0, out bool deletedOnlyOwner);
AssertTrue(deletedOnlyOwner && structureDocument.FindMarking(structureId) is null,
    "delete only segment removes its marking");
var recoveredSelection = new MarkingSelection("marking-001", 4, MarkingHandleKind.Control1);
MarkingSelection sanitizedSelection = recoveredSelection.Sanitize(new PathDefinition
{
    Start = new Point2Dto(),
    Segments = [new LinePathSegmentDefinition { End = new Point2Dto { X = 1 } },
        new CubicBezierPathSegmentDefinition
        {
            Control1 = new Point2Dto { X = 1.2f }, Control2 = new Point2Dto { X = 1.8f },
            End = new Point2Dto { X = 2 },
        }],
});
AssertEqual(1, sanitizedSelection.SegmentIndex, "selection after mutation clamps to nearest remaining segment");

var editedRoundTripDocument = ExerciseDocument.CreateNew("edited-roundtrip", "Edited Roundtrip", 10, 10);
string editedId = editedRoundTripDocument.AddMarking(mixedPath);
editedRoundTripDocument.MoveMarkingCoordinate(editedId, 1, MarkingPathCoordinateKind.Control1,
    new Point2Dto { X = 1.25f, Y = 2.75f });
editedRoundTripDocument.SetMarkingProperties(editedId, "#12AB34", 0.12f, "dotted", false);
string editedJson = ExerciseDefinitionStore.Serialize(editedRoundTripDocument.Definition);
ExerciseDefinitionDto editedReloaded = ExerciseDefinitionStore.LoadFromJson(editedJson, "edited-roundtrip");
var editedCubic = (CubicBezierPathSegmentDefinition)editedReloaded.Markings[0].Path.Segments[1];
AssertTrue(editedReloaded.Markings[0].Path.Start.X == -1 && editedCubic.Control1.X == 1.25f &&
           editedCubic.Control1.Y == 2.75f && editedReloaded.Markings[0].Style == "dotted" &&
           !editedReloaded.Markings[0].VisibleInViewer,
    "save/reload after edits preserves Path order, controls and style");
AssertTrue(!editedJson.Contains("selection", StringComparison.OrdinalIgnoreCase) &&
           !editedJson.Contains("handleKind", StringComparison.OrdinalIgnoreCase) &&
           !editedJson.Contains("segmentIndex", StringComparison.OrdinalIgnoreCase),
    "editor selection and handles are absent from Exercise JSON");

string mainSceneText = File.ReadAllText(Path.Combine(ProjectDirectory, "scenes", "Main.tscn"));
AssertTrue(mainSceneText.Contains("type=\"CharacterBody3D\"", StringComparison.Ordinal),
    "Viewer scene persists a CharacterBody3D root");
AssertTrue(mainSceneText.Contains("type=\"CapsuleShape3D\"", StringComparison.Ordinal),
    "Viewer scene persists a capsule collision shape");
string viewerSourceText = File.ReadAllText(Path.Combine(ProjectDirectory, "scripts", "TrackViewer.cs"));
AssertTrue(viewerSourceText.Contains("node is CollisionShape3D", StringComparison.Ordinal) &&
           viewerSourceText.Contains("node is CollisionPolygon3D", StringComparison.Ordinal),
    "collision disabling covers both supported collision descendant types");
string overpassSceneText = File.ReadAllText(Path.Combine(
    ProjectDirectory, "venues", "shared_assets", "scenes", "overpass_model.tscn"));
AssertTrue(overpassSceneText.Contains("ConvexPolygonShape3D_left_ramp", StringComparison.Ordinal) &&
           overpassSceneText.Contains("ConvexPolygonShape3D_right_ramp", StringComparison.Ordinal),
    "overpass uses separate continuous ramp collision profiles");
AssertTrue(!overpassSceneText.Contains("BoxShape3D_ramp", StringComparison.Ordinal),
    "overpass has no single box collision blocking ramp entry");

Console.WriteLine("All Viewer, Exercise Editor Iteration 2, Track Editor and Venue Editor checks passed.");

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
