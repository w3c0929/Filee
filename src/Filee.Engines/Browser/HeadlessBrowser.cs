// Launches a Chromium in headless mode, keeps its stderr for diagnostics, and finds the debugging
// port it picked. The process tree is killed on dispose, also when the conversion is cancelled.

using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Filee.Engines.Browser;

internal sealed class HeadlessBrowser : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _profile;
    private readonly StringBuilder _log = new();
    private readonly object _logGate = new();

    private HeadlessBrowser(Process process, string profile)
    {
        _process = process;
        _profile = profile;
    }

    public static async Task<HeadlessBrowser> StartAsync(string executable, string workDirectory, CancellationToken cancellationToken)
    {
        var profile = Path.Combine(workDirectory, "profile");
        Directory.CreateDirectory(profile);

        var arguments = new List<string>();
        if (!IsHeadlessShell(executable))
            arguments.Add("--headless=new");
        arguments.AddRange(
        [
            "--remote-debugging-port=0",
            "--remote-allow-origins=*",
            "--user-data-dir=" + profile,
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-background-networking",
            "--disable-component-update",
            "--disable-default-apps",
            "--disable-extensions",
            "--disable-sync",
            "--disable-gpu",
            "--disable-dev-shm-usage",
            "--hide-scrollbars",
            "--mute-audio",
            "--metrics-recording-only",
            "--disable-features=Translate,MediaRouter",
            "about:blank",
        ]);

        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            WorkingDirectory = workDirectory,
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        var process = new Process { StartInfo = info };
        var browser = new HeadlessBrowser(process, profile);
        process.ErrorDataReceived += (_, e) => browser.Append(e.Data);
        process.OutputDataReceived += (_, e) => browser.Append(e.Data);
        if (!process.Start())
            throw new InvalidOperationException("The Chromium engine did not start.");

        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        try { process.StandardInput.Close(); } catch { }

        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        return browser;
    }

    private static bool IsHeadlessShell(string executable) =>
        Path.GetFileNameWithoutExtension(executable).Contains("headless-shell", StringComparison.OrdinalIgnoreCase);

    private void Append(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        lock (_logGate)
        {
            _log.AppendLine(line);
            if (_log.Length > 8000)
                _log.Remove(0, _log.Length - 4000);
        }
    }

    public string Tail()
    {
        lock (_logGate)
            return _log.ToString().Trim();
    }

    /// <summary>Waits for the port Chromium writes into <c>DevToolsActivePort</c>, then asks for a page target.</summary>
    public async Task<Uri> WaitForPageAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var port = await WaitForPortAsync(timeout, cancellationToken).ConfigureAwait(false);

        // A fresh headless browser opens one about:blank tab; /json/list names its WebSocket.
        using var handler = new HttpClientHandler { UseProxy = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_process.HasExited)
                throw new InvalidOperationException($"The Chromium engine exited early ({_process.ExitCode}). {Tail()}");
            try
            {
                var json = await http.GetStringAsync($"http://127.0.0.1:{port}/json/list", cancellationToken).ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                foreach (var target in document.RootElement.EnumerateArray())
                {
                    if (target.TryGetProperty("type", out var type) && type.GetString() == "page"
                        && target.TryGetProperty("webSocketDebuggerUrl", out var url) && url.GetString() is { Length: > 0 } address)
                        return new Uri(address);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                last = ex;
            }
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"The Chromium engine opened no page to render. {last?.Message} {Tail()}".Trim());
    }

    private async Task<int> WaitForPortAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var file = Path.Combine(_profile, "DevToolsActivePort");
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_process.HasExited)
                throw new InvalidOperationException($"The Chromium engine exited early ({_process.ExitCode}). {Tail()}");
            try
            {
                if (File.Exists(file))
                {
                    var lines = await File.ReadAllLinesAsync(file, cancellationToken).ConfigureAwait(false);
                    if (lines.Length >= 1 && int.TryParse(lines[0].Trim(), out var port) && port > 0)
                        return port;
                }
            }
            catch (IOException)
            {
                // Chrome is still writing the file; try again.
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"The Chromium engine opened no debugging port. {Tail()}".Trim());
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch { }

        try
        {
            if (_process.HasExited)
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch { }

        _process.Dispose();

        // The profile folder is often still locked for a moment after the process tree dies.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(_profile))
                    Directory.Delete(_profile, recursive: true);
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(200).ConfigureAwait(false);
            }
        }
    }
}
