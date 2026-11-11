// Options for "web page slices": render an HTML/MHTML page with a real browser, then cut the
// full-page picture into tiles of one aspect ratio.

using System.Text.Json.Serialization;

namespace Filee.Core.Presets;

/// <summary>How a web page is rendered and cut up (see <see cref="Preset.Web"/>).</summary>
public sealed class WebOptions
{
    /// <summary>"16:9", "2.35:1", … Empty: this preset doesn't slice a web page.</summary>
    public string SliceRatio { get; set; } = "";

    /// <summary>Width of the page in CSS pixels (the browser's viewport width).</summary>
    public int ViewportWidth { get; set; } = 1440;

    /// <summary>Device pixel ratio: 1 writes one output pixel per CSS pixel, 2 doubles the resolution.</summary>
    public double DeviceScale { get; set; } = 1;

    /// <summary>
    /// Page zoom (1 = 100%): the browser lays the page out at <see cref="ViewportWidth"/> / this scale, so 80% fits
    /// more of the page and 125% makes text larger. Applied before measuring and slicing; the output width stays
    /// <see cref="ViewportWidth"/> × <see cref="DeviceScale"/>.
    /// </summary>
    public double PageZoom { get; set; } = 1;

    /// <summary>Colour ("#RRGGBB") of the strip that pads the last tile to the exact ratio.</summary>
    public string Background { get; set; } = "#FFFFFF";

    /// <summary>Most pictures one page may be cut into; 0 means the default.</summary>
    public int MaxTiles { get; set; } = 40;

    /// <summary>Extra wait after the page loaded and was scrolled through, in milliseconds.</summary>
    public int ScriptWaitMs { get; set; } = 300;

    /// <summary>True: the whole page. False: one viewport height only.</summary>
    public bool FullPage { get; set; } = true;

    public const int DefaultMaxTiles = 40;
    public const int MaxTilesLimit = 200;
    public const int MinWidth = 320;
    public const int MaxWidth = 4096;
    public const double MinScale = 0.5;
    public const double MaxScale = 4;
    public const double MinZoom = 0.25;
    public const double MaxZoom = 3;
    public const int MaxWaitMs = 15000;

    [JsonIgnore]
    public int EffectiveMaxTiles => MaxTiles <= 0 ? DefaultMaxTiles : Math.Clamp(MaxTiles, 1, MaxTilesLimit);

    [JsonIgnore]
    public int EffectiveWidth => Math.Clamp(ViewportWidth, MinWidth, MaxWidth);

    [JsonIgnore]
    public double EffectiveScale =>
        double.IsFinite(DeviceScale) ? Math.Clamp(DeviceScale, MinScale, MaxScale) : 1;

    [JsonIgnore]
    public double EffectiveZoom =>
        double.IsFinite(PageZoom) ? Math.Clamp(PageZoom, MinZoom, MaxZoom) : 1;

    [JsonIgnore]
    public int EffectiveWaitMs => Math.Clamp(ScriptWaitMs, 0, MaxWaitMs);

    public WebOptions Clone() => (WebOptions)MemberwiseClone();
}
