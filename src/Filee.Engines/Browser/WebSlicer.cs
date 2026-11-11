// Cuts a rendered full page into tiles of one aspect ratio, padding the last tile with the
// background colour so every output has exactly that ratio.

using Filee.Core.Conversion;
using Filee.Core.Presets;
using ImageMagick;

namespace Filee.Engines.Browser;

internal static class WebSlicer
{
    public static IReadOnlyList<string> Slice(IMagickImage<byte> page, WebOptions options, IOutputAllocator output,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!SliceRatio.TryParse(options.SliceRatio, out var ratioWidth, out var ratioHeight))
            throw new ConversionRefusedException("error.web_bad_ratio", options.SliceRatio);

        var width = page.Width;
        var height = (int)page.Height;
        if (width == 0 || height == 0)
            throw new ConversionRefusedException("error.web_render_failed", "empty picture");

        var blockHeight = SliceRatio.BlockHeight((int)width, ratioWidth, ratioHeight);
        var count = SliceRatio.TileCount(height, blockHeight);
        if (count > options.EffectiveMaxTiles)
            throw new ConversionRefusedException("error.web_too_many_tiles", $"{count} / {options.EffectiveMaxTiles}");
        if (count <= 0)
            return [];

        var background = BackgroundOf(options.Background);
        var written = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var top = index * blockHeight;
            var sliceHeight = Math.Min(blockHeight, height - top);
            // Allocate even for a skipped tile (?), so the numbering of the others stays predictable.
            var path = output.Allocate("png", count > 1 ? $"_p{index + 1}" : null);
            if (path is null)
                continue;

            using var tile = page.CloneArea(new MagickGeometry(0, top, width, (uint)sliceHeight));
            tile.ResetPage();
            if (sliceHeight < blockHeight)
            {
                // The last tile is short: pad it below, so the picture is exactly the requested ratio.
                tile.BackgroundColor = background;
                tile.Extent(width, (uint)blockHeight, Gravity.North);
            }

            tile.Write(path, MagickFormat.Png);
            written.Add(path);
            progress?.Report(0.7 + 0.3 * (index + 1) / count);
        }
        return written;
    }

    private static MagickColor BackgroundOf(string? text)
    {
        try
        {
            return new MagickColor(string.IsNullOrWhiteSpace(text) ? "#FFFFFF" : text.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or MagickException)
        {
            return MagickColors.White;
        }
    }
}
