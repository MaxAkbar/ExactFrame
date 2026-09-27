using System.Globalization;

namespace ExactFrame.Core.Geometry;

public static class AspectRatio
{
    public static string Describe(int width, int height)
    {
        if (width <= 0 || height <= 0) return "—";

        double ratio = (double)width / height;
        if (Near(ratio, 16d / 9)) return "16:9";
        if (Near(ratio, 9d / 16)) return "9:16";
        if (Near(ratio, 1)) return "1:1";
        if (Near(ratio, 4d / 3)) return "4:3";
        if (Near(ratio, 3d / 4)) return "3:4";
        if (Math.Abs(ratio - 21d / 9) < 0.06) return "21:9"; // 2560 × 1080 and 3440 × 1440 are sold as 21:9

        int divisor = Gcd(width, height);
        int a = width / divisor, b = height / divisor;
        return a <= 64 && b <= 64
            ? $"{a}:{b}"
            : ratio.ToString("0.00", CultureInfo.CurrentCulture) + ":1";
    }

    public static string Orientation(int width, int height) =>
        width > height ? "Landscape" : width < height ? "Portrait" : "Square";

    private static bool Near(double value, double target) => Math.Abs(value - target) < 0.005;

    private static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return Math.Max(1, a);
    }
}
