// Drives a headless Chromium over CDP: navigate, let fonts and lazy content settle, measure the
// page, then take one full-page PNG. The picture is returned as a file in the work directory.

using System.Text;
using System.Text.Json;
using Filee.Core.Conversion;
using Filee.Core.Presets;
using Filee.Engines.Email;

namespace Filee.Engines.Browser;

internal static class WebPageRenderer
{
    /// <summary>Upper bound on the picture, so one absurd page can't exhaust memory or disk.</summary>
    private const double MaxPagePixels = 60_000_000;

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ScreenshotTimeout = TimeSpan.FromSeconds(90);

    /// <summary>Renders <paramref name="inputPath"/> and returns the path of the full-page PNG.</summary>
    public static async Task<string> RenderAsync(string browserPath, string inputPath, string sourceFormat,
        string workDirectory, WebOptions options, CancellationToken cancellationToken)
    {
        var pagePath = inputPath;
        if (sourceFormat == "mhtml")
        {
            // The same sanitizer that reads MHTML for DOCX/HWPX: scripts are dropped, pictures become data URIs.
            var message = MhtmlArchive.Load(inputPath, cancellationToken);
            pagePath = Path.Combine(workDirectory, "page.html");
            await File.WriteAllTextAsync(pagePath, MhtmlArchive.Html(message), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }

        var url = new Uri(Path.GetFullPath(pagePath)).AbsoluteUri;
        await using var browser = await HeadlessBrowser.StartAsync(browserPath, workDirectory, cancellationToken).ConfigureAwait(false);
        var pageUri = await browser.WaitForPageAsync(TimeSpan.FromSeconds(25), cancellationToken).ConfigureAwait(false);

        await using var cdp = new CdpClient();
        await cdp.ConnectAsync(pageUri, cancellationToken).ConfigureAwait(false);
        await cdp.SendAsync("Page.enable", null, CommandTimeout, cancellationToken).ConfigureAwait(false);

        var width = options.EffectiveWidth;
        var scale = options.EffectiveScale;
        await cdp.SendAsync("Emulation.setDeviceMetricsOverride", new
        {
            width,
            height = 900,
            deviceScaleFactor = scale,
            mobile = false,
            screenWidth = width,
            screenHeight = 900,
        }, CommandTimeout, cancellationToken).ConfigureAwait(false);

        // Register before navigating: some pages fire the event before a late waiter could see it.
        var loaded = cdp.WaitForEventAsync("Page.loadEventFired", LoadTimeout, cancellationToken);
        var navigation = await cdp.SendAsync("Page.navigate", new { url }, CommandTimeout, cancellationToken).ConfigureAwait(false);
        if (navigation.TryGetProperty("errorText", out var navigationError) && navigationError.GetString() is { Length: > 0 } error)
            throw new ConversionRefusedException("error.web_load_failed", error);
        try
        {
            await loaded.ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // A page that never fires load can still be rendered; measure whatever is there.
        }

        await SettleAsync(cdp, options.EffectiveWaitMs, cancellationToken).ConfigureAwait(false);

        var height = options.FullPage
            ? await MeasureHeightAsync(cdp, cancellationToken).ConfigureAwait(false)
            : 900;

        if (width * height * scale * scale > MaxPagePixels)
        {
            throw new ConversionRefusedException("error.web_page_too_large",
                $"{width}×{height} @{scale:0.##}×");
        }

        var screenshot = await cdp.SendAsync("Page.captureScreenshot", new
        {
            format = "png",
            fromSurface = true,
            captureBeyondViewport = true,
            clip = new { x = 0, y = 0, width, height, scale = 1 },
        }, ScreenshotTimeout, cancellationToken).ConfigureAwait(false);

        var data = screenshot.GetProperty("data").GetString()
            ?? throw new InvalidOperationException("The browser returned an empty picture.");
        var output = Path.Combine(workDirectory, "render.png");
        await File.WriteAllBytesAsync(output, Convert.FromBase64String(data), cancellationToken).ConfigureAwait(false);
        return output;
    }

    /// <summary>Waits for fonts, scrolls through the page to trigger lazy images, then returns to the top.</summary>
    private static async Task SettleAsync(CdpClient cdp, int waitMilliseconds, CancellationToken cancellationToken)
    {
        var expression = $$"""
            (async () => {
              try { await document.fonts.ready; } catch (e) {}
              const sleep = ms => new Promise(r => setTimeout(r, ms));
              const root = document.documentElement;
              const step = Math.max(200, innerHeight * 0.9);
              const limit = Math.min(root.scrollHeight, 200000);
              for (let y = 0; y < limit; y += step) { scrollTo(0, y); await sleep(60); }
              scrollTo(0, 0);
              await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
              if ({{waitMilliseconds}} > 0) await sleep({{waitMilliseconds}});
            })()
            """;

        await cdp.SendAsync("Runtime.evaluate", new
        {
            expression,
            awaitPromise = true,
            returnByValue = true,
        }, SettleTimeout, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> MeasureHeightAsync(CdpClient cdp, CancellationToken cancellationToken)
    {
        const string expression = """
            (() => {
              const root = document.documentElement;
              const body = document.body;
              return Math.max(root.scrollHeight, body ? body.scrollHeight : 0, root.clientHeight, innerHeight);
            })()
            """;

        var evaluated = await cdp.SendAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true,
        }, CommandTimeout, cancellationToken).ConfigureAwait(false);

        var measured = evaluated.GetProperty("result").GetProperty("value").GetDouble();
        return Math.Max(1, (int)Math.Ceiling(measured));
    }
}
