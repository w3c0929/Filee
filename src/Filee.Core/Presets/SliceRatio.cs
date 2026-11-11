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

    /// <summary>
    /// A block height and width that make the ratio exact to the pixel, when both sides are whole numbers: the height
    /// is a multiple of <paramref name="width"/>'s height side and the width follows from it. The caller crops or pads
    /// the picture to <paramref name="exactWidth"/> (usually by a pixel or two).
    /// </summary>
    public static bool TryExactBlock(int pixelWidth, decimal width, decimal height, out int blockHeight, out int exactWidth)
    {
        blockHeight = exactWidth = 0;
        if (width != decimal.Truncate(width) || height != decimal.Truncate(height) || width <= 0 || height <= 0)
            return false;

        var w = (int)width;
        var h = (int)height;
        var block = Math.Max(1, (int)decimal.Round(pixelWidth * height / width, 0, MidpointRounding.AwayFromZero));
        // The width is exact when the height is a multiple of the ratio's height side.
        block = Math.Max(h, (int)Math.Round((double)block / h, MidpointRounding.AwayFromZero) * h);
        exactWidth = block / h * w;
        if (exactWidth <= 0)
            return false;
        blockHeight = block;
        return true;
    }
}
