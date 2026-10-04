using System.Globalization;

namespace PulseDeck.Core.Model;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    public static RgbColor FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte g = byte.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte b = byte.Parse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new RgbColor(r, g, b);
    }

    /// <summary>A small fixed palette cycled for newly-added signals, chosen for visibility on a dark background.</summary>
    public static readonly RgbColor[] DefaultPalette =
    {
        FromHex("#4FC3F7"), // light blue
        FromHex("#81C784"), // green
        FromHex("#FFD54F"), // amber
        FromHex("#F06292"), // pink
        FromHex("#BA68C8"), // purple
        FromHex("#FF8A65"), // deep orange
        FromHex("#4DB6AC"), // teal
        FromHex("#DCE775"), // lime
    };

    public static RgbColor FromPalette(int index) => DefaultPalette[((index % DefaultPalette.Length) + DefaultPalette.Length) % DefaultPalette.Length];
}
