using MotoGymkhanaTrainer.Tracks;

namespace MotoGymkhanaTrainer;

/// <summary>Pure geometry for placing an Exercise Definition inside a Track Project.</summary>
public static class ExerciseInstanceGeometry
{
    /// <summary>
    /// Applies the contract order scale X/Y, counter-clockwise rotation, then
    /// translation. Every preview geometry type calls this one function so there
    /// cannot be divergent cone, trajectory, marking or bounds transforms.
    /// </summary>
    public static Point2Dto TransformPoint(
        Point2Dto localPoint,
        Point2Dto position,
        float rotationDeg,
        Point2Dto scale)
    {
        float scaledX = localPoint.X * scale.X;
        float scaledY = localPoint.Y * scale.Y;
        float radians = rotationDeg * MathF.PI / 180.0f;
        float cosine = MathF.Cos(radians);
        float sine = MathF.Sin(radians);
        return new Point2Dto
        {
            X = scaledX * cosine - scaledY * sine + position.X,
            Y = scaledX * sine + scaledY * cosine + position.Y,
        };
    }

    /// <summary>Maps a track point back into Exercise-local coordinates for bounds hit testing.</summary>
    public static Point2Dto InverseTransformPoint(
        Point2Dto trackPoint,
        Point2Dto position,
        float rotationDeg,
        Point2Dto scale)
    {
        Point2Dto rotated = InverseRotationTranslation(trackPoint, position, rotationDeg);
        return new Point2Dto { X = rotated.X / scale.X, Y = rotated.Y / scale.Y };
    }

    /// <summary>
    /// Removes translation and rotation while intentionally retaining scale.
    /// Mouse resizing uses this intermediate coordinate to derive a new scale
    /// from the pointer distance to the instance center.
    /// </summary>
    public static Point2Dto InverseRotationTranslation(
        Point2Dto trackPoint,
        Point2Dto position,
        float rotationDeg)
    {
        float translatedX = trackPoint.X - position.X;
        float translatedY = trackPoint.Y - position.Y;
        float radians = -rotationDeg * MathF.PI / 180.0f;
        float cosine = MathF.Cos(radians);
        float sine = MathF.Sin(radians);
        return new Point2Dto
        {
            X = translatedX * cosine - translatedY * sine,
            Y = translatedX * sine + translatedY * cosine,
        };
    }

    /// <summary>Returns the four transformed corners of centered Exercise bounds.</summary>
    public static Point2Dto[] TransformBounds(
        float width,
        float length,
        Point2Dto position,
        float rotationDeg,
        Point2Dto scale)
    {
        float halfWidth = width * 0.5f;
        float halfLength = length * 0.5f;
        Point2Dto[] local =
        [
            new() { X = -halfWidth, Y = -halfLength },
            new() { X = halfWidth, Y = -halfLength },
            new() { X = halfWidth, Y = halfLength },
            new() { X = -halfWidth, Y = halfLength },
        ];
        return local.Select(point => TransformPoint(point, position, rotationDeg, scale)).ToArray();
    }

    /// <summary>Reports whether any transformed bounds corner lies outside the area.</summary>
    public static bool IsOutsideArea(IReadOnlyList<Point2Dto> bounds, float areaWidth, float areaLength)
    {
        float halfWidth = areaWidth * 0.5f;
        float halfLength = areaLength * 0.5f;
        return bounds.Any(point =>
            point.X < -halfWidth || point.X > halfWidth ||
            point.Y < -halfLength || point.Y > halfLength);
    }
}
