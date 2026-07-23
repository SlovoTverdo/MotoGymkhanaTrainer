using Godot;
using MotoGymkhanaTrainer.Tracks;

const string ProjectDirectory = "E:\\Projects\\Games\\MotoGymkhanaTrainer";
string samplePath = Path.Combine(ProjectDirectory, "examples", "courses", "basic.json");
string json = File.ReadAllText(samplePath);
string alternatePath = Path.Combine(ProjectDirectory, "tests", "fixtures", "alternate-track.json");
string invalidFixturePath = Path.Combine(ProjectDirectory, "tests", "fixtures", "invalid-track.json");

TrackSnapshotDto snapshot = TrackLoader.LoadFromJson(json, samplePath);
AssertEqual(2, snapshot.FormatVersion, "sample uses formatVersion 2");
AssertEqual("basic-demo", snapshot.Track.Id, "valid JSON loads track metadata");
AssertEqual(4, snapshot.Cones.Length, "valid JSON loads every sample cone");
AssertEqual(1, snapshot.Markings.Length, "valid JSON loads sample markings");
AssertEqual(2, snapshot.Trajectory.Segments.Length, "valid JSON loads both trajectory segments");

TrackSnapshotDto alternate = TrackLoader.LoadFromJson(
    File.ReadAllText(alternatePath),
    alternatePath);
AssertEqual("alternate-test", alternate.Track.Id, "a second exported track loads independently");
AssertEqual(20.0f, alternate.Area.Width, "the second track supplies a different area width");
AssertEqual(30.0f, alternate.Area.Length, "the second track supplies a different area length");
AssertEqual(2, alternate.Cones.Length, "the second track supplies its own runtime cone set");

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

Console.WriteLine("All Viewer checks through Iteration 3 passed.");

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
