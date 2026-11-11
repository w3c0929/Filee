// Aspect ratios for web page slices: "2.35:1", "16:9", … (decimals allowed, unlike VideoCrop's
// integer-only "16:9"), and the pixel maths that turns a page width into tile heights.

using System.Globalization;

namespace Filee.Core.Presets;

/// <summary>Reads "width:height" ratios and computes tile sizes.</summary>
public static class SliceRatio
{
    /// <summary>Upper bound for either side, so a typo like "16000:9" can't produce absurd tiles.</summary>
    public const decimal MaxSide = 1000;

    /// <summary>Reads "16:9", "2.35:1", or "2,35:1" into its two sides.</summary>
    public static bool TryParse(string? text, out decimal width, out decimal height)
    {
        width = height = 0;
        var parts = text?.Split(':');
        if (parts is not [var w, var h])
            return false;

        w = w.Trim().Replace(',', '.');
        h = h.Trim().Replace(',', '.');
        if (!decimal.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out width)
            || !decimal.TryParse(h, NumberStyles.Float, CultureInfo.InvariantCulture, out height))
            return false;

        return width > 0 && width <= MaxSide && height > 0 && height <= MaxSide;
    }

    /// <summary>Height of one tile when the picture is <paramref name="pixelWidth"/> pixels wide.</summary>
    public static int BlockHeight(int pixelWidth, decimal width, decimal height) =>
        Math.Max(1, (int)decimal.Round(pixelWidth * height / width, 0, MidpointRounding.AwayFromZero));

    /// <summary>How many tiles <paramref name="pixelHeight"/> pixels make.</summary>
    public static int TileCount(int pixelHeight, int blockHeight) =>
        blockHeight <= 0 ? 0 : (pixelHeight + blockHeight - 1) / blockHeight;
}
