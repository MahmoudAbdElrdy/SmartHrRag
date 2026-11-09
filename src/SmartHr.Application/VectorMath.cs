namespace SmartHr.Application;

public static class VectorMath
{
    public static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count == 0 || left.Count != right.Count)
        {
            return 0;
        }

        double dot = 0, l = 0, r = 0;
        for (var i = 0; i < left.Count; i++)
        {
            dot += left[i] * right[i];
            l += left[i] * left[i];
            r += right[i] * right[i];
        }

        return l == 0 || r == 0 ? 0 : dot / (Math.Sqrt(l) * Math.Sqrt(r));
    }
}
