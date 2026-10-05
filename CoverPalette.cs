using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland;

static class CoverPalette
{
    public static readonly Color[] Plain = [Colors.White];

    const double NeighborDegrees = 28;
    const int DecodeWidth = 192;
    const double SampleSize = 24;
    const int HueSlices = 12, PaletteSize = 3;
    const double MinShare = 0.08;
    const double MinDistance = 64;
    const double BaseWeight = 0.01;
    const double LiftedPeak = 235;
    const double Whitening = 0.18;

    public static (ImageSource Art, Color[] Palette) Decode(MemoryStream data)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = DecodeWidth;
        bitmap.StreamSource = data;
        bitmap.EndInit();
        bitmap.Freeze();
        return (bitmap, Extract(bitmap));
    }

    public static Color[] Around(Color color) => [color, RotateHue(color, NeighborDegrees), RotateHue(color, -NeighborDegrees)];

    static Color[] Extract(BitmapSource source)
    {
        try
        {
            double[,] sums = WeighHues(Pixels(source));
            Color? Mean(int row) => Lift(sums[row, 0] / sums[row, 3], sums[row, 1] / sums[row, 3], sums[row, 2] / sums[row, 3]);

            if (Mean(HueSlices) is not { } accent) return Plain;
            var palette = new List<Color> { accent };
            foreach (int slice in Enumerable.Range(0, HueSlices).OrderByDescending(s => sums[s, 3]))
            {
                if (palette.Count == PaletteSize || sums[slice, 3] < MinShare * sums[HueSlices, 3]) break;
                if (Mean(slice) is not { } color) continue;
                if (palette.Any(taken => Distance(taken, color) < MinDistance)) continue;
                palette.Add(color);
            }
            for (double turn = NeighborDegrees; palette.Count < PaletteSize; turn = -turn) palette.Add(RotateHue(accent, turn));
            return palette.ToArray();
        }
        catch
        {
            return Plain;
        }
    }

    static byte[] Pixels(BitmapSource source)
    {
        var small = new TransformedBitmap(source,
            new ScaleTransform(SampleSize / source.PixelWidth, SampleSize / source.PixelHeight));
        var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var px = new byte[w * h * 4];
        bgra.CopyPixels(px, w * 4, 0);
        return px;
    }

    static double[,] WeighHues(byte[] px)
    {
        var sums = new double[HueSlices + 1, 4];
        for (int i = 0; i < px.Length; i += 4)
        {
            double r = px[i + 2], g = px[i + 1], b = px[i];
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double saturation = max == 0 ? 0 : (max - min) / max;
            double weight = saturation * saturation * (max / 255) + BaseWeight;
            Add(sums, (int)(Hue(r, g, b) / 360 * HueSlices) % HueSlices, r, g, b, weight);
            Add(sums, HueSlices, r, g, b, weight);
        }
        return sums;
    }

    static void Add(double[,] sums, int row, double r, double g, double b, double weight)
    {
        sums[row, 0] += r * weight;
        sums[row, 1] += g * weight;
        sums[row, 2] += b * weight;
        sums[row, 3] += weight;
    }

    static double Distance(Color a, Color b) =>
        Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));

    static Color? Lift(double r, double g, double b)
    {
        double peak = Math.Max(r, Math.Max(g, b));
        if (!(peak >= 1)) return null;

        double lift = LiftedPeak / peak;
        r *= lift;
        g *= lift;
        b *= lift;
        return Color.FromRgb(
            (byte)(r + (255 - r) * Whitening),
            (byte)(g + (255 - g) * Whitening),
            (byte)(b + (255 - b) * Whitening));
    }

    static double Hue(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), span = max - Math.Min(r, Math.Min(g, b));
        if (span <= 0) return 0;
        double hue = max == r ? (g - b) / span : max == g ? 2 + (b - r) / span : 4 + (r - g) / span;
        return (hue * 60 + 360) % 360;
    }

    static Color RotateHue(Color color, double degrees)
    {
        double max = Math.Max(color.R, Math.Max(color.G, color.B)), min = Math.Min(color.R, Math.Min(color.G, color.B));
        double hue = (Hue(color.R, color.G, color.B) + degrees + 360) % 360 / 60;
        double mid = min + (max - min) * (1 - Math.Abs(hue % 2 - 1));
        (double r, double g, double b) = (int)hue switch
        {
            0 => (max, mid, min),
            1 => (mid, max, min),
            2 => (min, max, mid),
            3 => (min, mid, max),
            4 => (mid, min, max),
            _ => (max, min, mid),
        };
        return Color.FromRgb((byte)r, (byte)g, (byte)b);
    }
}
