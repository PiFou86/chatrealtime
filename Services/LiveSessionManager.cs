using System.Collections.Concurrent;
using System.Text.Json;
using chatrealtime.Configuration;
using chatrealtime.Models;
using chatrealtime.Services.Tools;
using Microsoft.Extensions.Options;

namespace chatrealtime.Services;

public interface ILiveSessionManager
{
    Task<BrowserLiveSessionResponse> CreateAsync(string offerSdp, CancellationToken cancellationToken);
    Task CloseAsync(string sessionId, CancellationToken cancellationToken);
}

public sealed class LiveSessionManager : ILiveSessionManager, IHostedService, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();
    private readonly IOpenAILiveClient _client;
    private readonly ILiveConfigurationFactory _configurationFactory;
    private readonly IToolExecutor _toolExecutor;
    private readonly OpenAISettings _settings;
    private readonly ILogger<LiveSessionManager> _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _lifetimeLock = new();
    private Task? _stopTask;
    private bool _disposed;

    public LiveSessionManager(
        IOpenAILiveClient client,
        ILiveConfigurationFactory configurationFactory,
        IToolExecutor toolExecutor,
        IOptions<OpenAISettings> settings,
        ILogger<LiveSessionManager> logger)
    {
        _client = client;
        _configurationFactory = configurationFactory;
        _toolExecutor = toolExecutor;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<BrowserLiveSessionResponse> CreateAsync(
        string offerSdp,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _stopTask) is not null)
        {
            throw new InvalidOperationException("The Live session manager is stopping.");
        }

        if (string.IsNullOrWhiteSpace(_settings.ApiKey) ||
            _settings.ApiKey == "YOUR_OPENAI_API_KEY_HERE")
        {
            throw new InvalidOperationException("OpenAI API key is not configured.");
        }

        if (string.IsNullOrWhiteSpace(offerSdp))
        {
            throw new ArgumentException("An SDP offer is required.", nameof(offerSdp));
        }

        var sessionConfig = await _configurationFactory.CreateAsync(cancellationToken);
        var created = await _client.CreateSessionAsync(new LiveCreateRequest
        {
            Session = sessionConfig,
            Transport = new LiveTransport { Sdp = offerSdp }
        }, cancellationToken);

        // The trusted sideband must be attached before the browser receives the answer.
        var sideband = await _client.AttachSidebandAsync(created.Session.Id, cancellationToken);
        var entry = new SessionEntry(sideband, _shutdown.Token);
        if (!_sessions.TryAdd(created.Session.Id, entry))
        {
            await sideband.DisposeAsync();
            throw new InvalidOperationException($"Duplicate Live session id: {created.Session.Id}");
        }

        entry.ReceiveTask = Task.Run(() => sideband.RunAsync(
            message => HandleSidebandEventAsync(created.Session.Id, message, entry.Lifetime.Token),
            () => RemoveSessionAsync(created.Session.Id),
            entry.Lifetime.Token), CancellationToken.None);

        return new BrowserLiveSessionResponse(
            created.Session.Id,
            created.Transport.Sdp,
            created.Transport.Type);
    }

    internal async Task HandleSidebandEventAsync(
        string sessionId,
        JsonElement message,
        CancellationToken cancellationToken)
    {
        if (!message.TryGetProperty("type", out var typeElement))
        {
            return;
        }

        var type = typeElement.GetString();
        if (type == "session.closed")
        {
            return;
        }

        if (type == "error")
        {
            _logger.LogWarning("Live sideband error for {SessionId}: {Event}", sessionId, message.GetRawText());
            return;
        }

        if (type != "response.event" ||
            !message.TryGetProperty("event", out var nested) ||
            !nested.TryGetProperty("type", out var nestedType) ||
            nestedType.GetString() != "response.output_item.done" ||
            !nested.TryGetProperty("item", out var item) ||
            !item.TryGetProperty("type", out var itemType) ||
            itemType.GetString() != "function_call")
        {
            // Live and Responses event sets are extensible; unknown events are intentionally ignored.
            return;
        }

        await ExecuteToolCallAsync(sessionId, item, cancellationToken);
    }

    private async Task ExecuteToolCallAsync(
        string sessionId,
        JsonElement item,
        CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(sessionId, out var entry))
        {
            return;
        }

        var callId = item.TryGetProperty("call_id", out var callIdElement)
            ? callIdElement.GetString()
            : null;
        var toolName = item.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString()
            : null;

        object output;
        if (string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(toolName))
        {
            output = new { error = "Invalid function call: call_id and name are required." };
        }
        else
        {
            try
            {
                var argumentsText = item.TryGetProperty("arguments", out var argumentsElement)
                    ? argumentsElement.GetString()
                    : "{}";
                using var argumentsDocument = JsonDocument.Parse(argumentsText ?? "{}");
                if (argumentsDocument.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException("Tool arguments must be a JSON object.");
                }

                output = await _toolExecutor.ExecuteAsync(toolName, argumentsDocument.RootElement.Clone());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Tool {ToolName} failed in Live session {SessionId}", toolName, sessionId);
                output = new { error = ex.Message };
            }
        }

        var outputEvent = JsonSerializer.SerializeToElement(new
        {
            type = "response.item.create",
            event_id = $"tool_{Guid.NewGuid():N}",
            item = new
            {
                type = "function_call_output",
                call_id = callId,
                output = JsonSerializer.Serialize(output)
            }
        });
        await entry.Connection.SendAsync(outputEvent, cancellationToken);

        var continueEvent = JsonSerializer.SerializeToElement(new
        {
            type = "response.create",
            event_id = $"continue_{Guid.NewGuid():N}"
        });
        await entry.Connection.SendAsync(continueEvent, cancellationToken);
    }

    public async Task CloseAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (_sessions.TryRemove(sessionId, out var entry))
        {
            try
            {
                await entry.Connection.CloseAsync(cancellationToken);

                if (entry.ReceiveTask is not null)
                {
                    try
                    {
                        await entry.ReceiveTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
                    }
                    catch (TimeoutException)
                    {
                        entry.Lifetime.Cancel();
                        await entry.ReceiveTask;
                    }
                }
            }
            finally
            {
                await entry.DisposeAsync();
            }
        }
    }

    private async Task RemoveSessionAsync(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var entry))
        {
            await entry.DisposeAsync();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_lifetimeLock)
        {
            return _stopTask ??= StopCoreAsync(cancellationToken);
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var closeTasks = _sessions.Keys.Select(async sessionId =>
            {
                try
                {
                    await CloseAsync(sessionId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error closing Live session {SessionId} during shutdown", sessionId);
                }
            });
            await Task.WhenAll(closeTasks);
        }
        finally
        {
            _shutdown.Cancel();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        lock (_lifetimeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _shutdown.Dispose();
        }
    }

    private sealed class SessionEntry : IAsyncDisposable
    {
        private int _disposed;

        public SessionEntry(ILiveSidebandConnection connection, CancellationToken shutdownToken)
        {
            Connection = connection;
            Lifetime = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        }

        public ILiveSidebandConnection Connection { get; }
        public CancellationTokenSource Lifetime { get; }
        public Task? ReceiveTask { get; set; }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            await Lifetime.CancelAsync();
            await Connection.DisposeAsync();
            Lifetime.Dispose();
        }
    }
}
