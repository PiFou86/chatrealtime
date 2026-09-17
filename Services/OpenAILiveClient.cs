using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using chatrealtime.Configuration;
using chatrealtime.Models;
using Microsoft.Extensions.Options;

namespace chatrealtime.Services;

public sealed class OpenAILiveClient : IOpenAILiveClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly OpenAISettings _settings;
    private readonly ILoggerFactory _loggerFactory;

    public OpenAILiveClient(
        HttpClient httpClient,
        IOptions<OpenAISettings> settings,
        ILoggerFactory loggerFactory)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _loggerFactory = loggerFactory;
    }

    public async Task<LiveCreateResponse> CreateSessionAsync(
        LiveCreateRequest request,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _settings.LiveSessionsUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new LiveApiException((int)response.StatusCode, body);
        }

        return JsonSerializer.Deserialize<LiveCreateResponse>(body, JsonOptions)
            ?? throw new LiveApiException(502, "OpenAI returned an empty Live session response.");
    }

    public async Task<ILiveSidebandConnection> AttachSidebandAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {_settings.ApiKey}");
        var baseUrl = _settings.LiveSidebandUrl.TrimEnd('/');
        var uri = new Uri($"{baseUrl}/sessions/{Uri.EscapeDataString(sessionId)}/attach");
        await socket.ConnectAsync(uri, cancellationToken);
        return new LiveSidebandConnection(
            socket,
            _loggerFactory.CreateLogger<LiveSidebandConnection>());
    }
}

internal sealed class LiveSidebandConnection : ILiveSidebandConnection
{
    private readonly ClientWebSocket _socket;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public LiveSidebandConnection(ClientWebSocket socket, ILogger logger)
    {
        _socket = socket;
        _logger = logger;
    }

    public async Task SendAsync(JsonElement message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message.GetRawText());
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task RunAsync(
        Func<JsonElement, Task> onEvent,
        Func<Task> onClosed,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                using var document = JsonDocument.Parse(stream.ToArray());
                var message = document.RootElement.Clone();
                await onEvent(message);
                if (message.TryGetProperty("type", out var type) && type.GetString() == "session.closed")
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException ex) when (
            cancellationToken.IsCancellationRequested || IsCanceledTransportRead(ex))
        {
            _logger.LogDebug("Live sideband connection closed during shutdown");
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "Live sideband connection ended unexpectedly");
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            await onClosed();
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_socket.State == WebSocketState.Open)
        {
            var close = JsonSerializer.SerializeToElement(new
            {
                type = "session.close",
                event_id = $"close_{Guid.NewGuid():N}"
            });
            await SendAsync(close, cancellationToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        _sendLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private static bool IsCanceledTransportRead(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException ||
                current is SocketException { SocketErrorCode: SocketError.OperationAborted or SocketError.Interrupted })
            {
                return true;
            }
        }

        return false;
    }
}
