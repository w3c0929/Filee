using Filee.Core.Presets;

namespace Filee.Core.Tests;

public class SliceRatioTests
{
    [Theory]
    [InlineData("16:9", 16, 9)]
    [InlineData("9:16", 9, 16)]
    [InlineData("4:3", 4, 3)]
    [InlineData("3:4", 3, 4)]
    [InlineData("1:1", 1, 1)]
    [InlineData("2.35:1", 2.35, 1)]
    [InlineData("2,35:1", 2.35, 1)]
    public void Parses_decimals_and_integers(string text, double width, double height)
    {
        Assert.True(SliceRatio.TryParse(text, out var parsedWidth, out var parsedHeight));
        Assert.Equal((decimal)width, parsedWidth);
        Assert.Equal((decimal)height, parsedHeight);
    }

    [Theory]
    [InlineData("")]
    [InlineData("auto")]
    [InlineData("0:1")]
    [InlineData("16:")]
    [InlineData("a:b")]
    public void Rejects_invalid_ratios(string text) => Assert.False(SliceRatio.TryParse(text, out _, out _));

    [Theory]
    [InlineData(1440, 16, 9, 810)]
    [InlineData(1440, 9, 16, 2560)]
    [InlineData(1440, 4, 3, 1080)]
    [InlineData(1440, 3, 4, 1920)]
    [InlineData(1440, 1, 1, 1440)]
    [InlineData(1440, 47, 20, 613)] // 2.35:1, the nearest whole pixel
    public void Block_height_is_the_nearest_pixel(int width, int ratioWidth, int ratioHeight, int expected) =>
        Assert.Equal(expected, SliceRatio.BlockHeight(width, ratioWidth, ratioHeight));

    [Theory]
    [InlineData(2000, 810, 3)]
    [InlineData(810, 810, 1)]
    [InlineData(0, 810, 0)]
    public void Tile_count_rounds_up(int height, int blockHeight, int expected) =>
        Assert.Equal(expected, SliceRatio.TileCount(height, blockHeight));
}
