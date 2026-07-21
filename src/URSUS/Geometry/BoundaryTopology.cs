namespace URSUS.Geometry;

public readonly record struct Coordinate2D(double X, double Y);

public sealed class BoundaryRing
{
    public IReadOnlyList<Coordinate2D> Points { get; }
    public double SignedArea { get; }

    public BoundaryRing(IReadOnlyList<Coordinate2D> points)
    {
        if (points == null || points.Count < 4 || points[0] != points[^1])
            throw new ArgumentException("Ring은 닫힌 4개 이상의 좌표가 필요합니다.", nameof(points));
        double signedArea = ComputeSignedArea(points);
        if (!double.IsFinite(signedArea) || Math.Abs(signedArea) < 1e-9)
            throw new ArgumentException("Ring 면적은 0일 수 없습니다.", nameof(points));
        Points = Array.AsReadOnly(points.ToArray());
        SignedArea = signedArea;
    }

    internal BoundaryRing WithOrientation(bool counterClockwise)
    {
        if ((SignedArea > 0) == counterClockwise) return this;
        return new BoundaryRing(Points.Reverse().ToArray());
    }

    internal (double X, double Y, double SignedArea) CentroidContribution()
    {
        double crossSum = 0, xSum = 0, ySum = 0;
        for (int i = 0; i < Points.Count - 1; i++)
        {
            double cross = Points[i].X * Points[i + 1].Y - Points[i + 1].X * Points[i].Y;
            crossSum += cross;
            xSum += (Points[i].X + Points[i + 1].X) * cross;
            ySum += (Points[i].Y + Points[i + 1].Y) * cross;
        }
        return (xSum / (3 * crossSum), ySum / (3 * crossSum), crossSum / 2);
    }

    private static double ComputeSignedArea(IReadOnlyList<Coordinate2D> points)
    {
        double twiceArea = 0;
        for (int i = 0; i < points.Count - 1; i++)
            twiceArea += points[i].X * points[i + 1].Y - points[i + 1].X * points[i].Y;
        return twiceArea / 2;
    }
}

public sealed record BoundaryPart(BoundaryRing Outer, IReadOnlyList<BoundaryRing> Holes);

public sealed class BoundaryTopology
{
    public IReadOnlyList<BoundaryPart> Parts { get; }
    public IReadOnlyList<string> Warnings { get; }
    public double Area { get; }
    public Coordinate2D Centroid { get; }

    private BoundaryTopology(IReadOnlyList<BoundaryPart> parts, IReadOnlyList<string> warnings)
    {
        Parts = Array.AsReadOnly(parts.ToArray());
        Warnings = Array.AsReadOnly(warnings.ToArray());
        double areaSum = 0, xMoment = 0, yMoment = 0;
        foreach (var ring in parts.SelectMany(part => new[] { part.Outer }.Concat(part.Holes)))
        {
            var contribution = ring.CentroidContribution();
            areaSum += contribution.SignedArea;
            xMoment += contribution.X * contribution.SignedArea;
            yMoment += contribution.Y * contribution.SignedArea;
        }
        if (areaSum <= 0) throw new ArgumentException("유효 topology 면적이 없습니다.");
        Area = areaSum;
        Centroid = new Coordinate2D(xMoment / areaSum, yMoment / areaSum);
    }

    public static BoundaryTopology Create(IEnumerable<BoundaryPart> candidates)
    {
        var parts = new List<BoundaryPart>();
        var warnings = new List<string>();
        int partIndex = 0;
        foreach (var candidate in candidates)
        {
            try
            {
                var outer = candidate.Outer.WithOrientation(counterClockwise: true);
                var holes = new List<BoundaryRing>();
                int holeIndex = 0;
                foreach (var hole in candidate.Holes ?? Array.Empty<BoundaryRing>())
                {
                    try { holes.Add(hole.WithOrientation(counterClockwise: false)); }
                    catch (ArgumentException) { warnings.Add($"part {partIndex} hole {holeIndex} invalid"); }
                    holeIndex++;
                }
                parts.Add(new BoundaryPart(outer, holes.AsReadOnly()));
            }
            catch (ArgumentException)
            {
                warnings.Add($"part {partIndex} invalid");
            }
            partIndex++;
        }
        if (parts.Count == 0) throw new ArgumentException("모든 topology part가 유효하지 않습니다.");
        return new BoundaryTopology(parts, warnings);
    }
}
