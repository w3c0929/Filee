// HTML / MHTML → PNG with a real browser (headless Chromium), then cut into tiles of one aspect
// ratio. The engine is an optional download (Settings → Engines); never a browser installed on the system.

using Filee.Core.Conversion;
using Filee.Core.Presets;
using Filee.Engines.Infrastructure;
using Filee.Engines.Magick;
using ImageMagick;

namespace Filee.Engines.Browser;

public sealed class BrowserHtmlConverter : IConverter
{
    private string? _program;

    public string Id => "browser-html";

    public string DisplayName => "Chromium";

    /// <summary>Browsers are memory hungry: one page at a time.</summary>
    public int MaxParallelism => 1;

    public IReadOnlyList<ConversionEdge> Edges { get; } =
    [
        // Only a web page slice job (Preset.SlicesWebPage) takes these; ordinary routes keep the reflow path.
        new("html", "png", Cost: 5, DirectOnly: true, NotForImageSources: true, WebPage: true),
        new("mhtml", "png", Cost: 5, DirectOnly: true, NotForImageSources: true, WebPage: true),
    ];

    public EngineStatus GetStatus()
    {
        _program = Locate();
        return _program is null
            ? EngineStatus.Unavailable("engine.reason.not_installed")
            : EngineStatus.Available(_program, EngineVersions.Component("chromium"));
    }

    public async Task<IReadOnlyList<string>> ConvertAsync(ConversionStep step, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var browser = _program ?? Locate() ?? throw new InvalidOperationException("The Chromium engine is not installed.");
        var options = step.Preset.Web;
        if (!SliceRatio.TryParse(options.SliceRatio, out _, out _))
            throw new ConversionRefusedException("error.web_bad_ratio", options.SliceRatio);

        var work = Path.Combine(step.WorkDirectory, "browser-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);

        progress?.Report(0.02);
        string rendered;
        using (ProgressEstimate.Start(progress, TimeSpan.FromSeconds(12), 0.02, 0.6))
        {
            rendered = await WebPageRenderer.RenderAsync(browser, step.InputPath, step.From, work, options, cancellationToken)
                .ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(0.65);

        using var page = new MagickImage(rendered);
        ImageEncoder.ApplyOptions(page, step.Preset.Image, "png");

        return WebSlicer.Slice(page, options, step.Output, progress, cancellationToken);
    }

    /// <summary>Filee's own Chromium: the user's chosen copy, or one Filee downloaded / bundled. Never the system's.</summary>
    private static string? Locate()
    {
        if (EngineEnvironment.OwnCopies.TryGetValue("chromium", out var chosen) && !string.IsNullOrWhiteSpace(chosen))
        {
            var folder = File.Exists(chosen) ? Path.GetDirectoryName(chosen) : chosen;
            if (folder is not null && ProgramIn(folder) is { } own)
                return own;
        }

        if (EngineEnvironment.FindBundled("chromium") is { } root)
        {
            foreach (var folder in new[] { root, Path.Combine(root, "bin"), Path.Combine(root, "program") })
                if (ProgramIn(folder) is { } bundled)
                    return bundled;
        }

        // Last resort, like the other optional engines: a Chromium already on this PC. Filee's own copy is preferred.
        return EngineEnvironment.SystemProgram("chromium", "chrome.exe");
    }

    private static string? ProgramIn(string folder) =>
        Names.Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists);

    private static readonly string[] Names = OperatingSystem.IsWindows()
        ? ["chrome.exe", "chrome-headless-shell.exe"]
        : ["chrome", "chrome-headless-shell"];
}
