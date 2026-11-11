// A very small Chrome DevTools Protocol client: JSON over one WebSocket, request ids matched to
// responses, and event waiters. Enough to navigate a page, measure it and take one full-page picture.

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace Filee.Engines.Browser;

internal sealed class CdpClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _events = new();
    private readonly CancellationTokenSource _closed = new();
    private int _nextId;
    private Task? _readLoop;

    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        await _socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
        _readLoop = Task.Run(() => ReadLoopAsync(_closed.Token), CancellationToken.None);
    }

    /// <summary>Sends one command and returns its <c>result</c> object.</summary>
    public async Task<JsonElement> SendAsync(string method, object? parameters, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        var payload = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters });
        await _socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var registration = timeoutSource.Token.Register(() => completion.TrySetCanceled(timeoutSource.Token));

        var response = await completion.Task.ConfigureAwait(false);
        if (response.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"{method}: {error}");
        return response.TryGetProperty("result", out var result) ? result.Clone() : default;
    }

    /// <summary>Waits for the next protocol event of <paramref name="method"/>.</summary>
    public async Task<JsonElement> WaitForEventAsync(string method, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _events[method] = completion;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var registration = timeoutSource.Token.Register(() => completion.TrySetException(new TimeoutException(method)));
        try
        {
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _events.TryRemove(method, out _);
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var received = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (received.MessageType == WebSocketMessageType.Close)
                    break;

                message.Write(buffer, 0, received.Count);
                if (!received.EndOfMessage)
                    continue;

                var json = message.ToArray();
                message.SetLength(0);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement.Clone();

                if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt32(out var id))
                {
                    if (_pending.TryRemove(id, out var pending))
                        pending.TrySetResult(root);
                }
                else if (root.TryGetProperty("method", out var methodElement) && methodElement.GetString() is { } method
                         && _events.TryRemove(method, out var waiter))
                {
                    waiter.TrySetResult(root);
                }
            }
        }
        catch
        {
            // The browser went away or the token was cancelled; every waiter is failed below.
        }
        finally
        {
            foreach (var pending in _pending.Values)
                pending.TrySetException(new WebSocketException("The browser connection closed."));
            _pending.Clear();
            foreach (var waiter in _events.Values)
                waiter.TrySetException(new WebSocketException("The browser connection closed."));
            _events.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { _closed.Cancel(); } catch { }
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).ConfigureAwait(false);
        }
        catch { }
        _socket.Dispose();
        if (_readLoop is not null)
            await Task.WhenAny(_readLoop, Task.Delay(1000)).ConfigureAwait(false);
        _closed.Dispose();
    }
}
