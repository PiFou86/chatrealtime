using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using chatrealtime.Configuration;
using chatrealtime.Models;
using Microsoft.Extensions.Options;

namespace chatrealtime.Services;

public class OpenAIRealtimeService : IDisposable
{
    private readonly OpenAISettings _settings;
    private readonly ILogger<OpenAIRealtimeService> _logger;
    private ClientWebSocket? _openAIWebSocket;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _receiveCts;

    public event Func<string, Task>? OnAudioReceived;
    public event Func<string, string, Task>? OnTranscriptReceived; // role, transcript
    public event Func<string, Task>? OnError;
    public event Func<string, Task>? OnStatusChanged;

    public OpenAIRealtimeService(
        IOptions<OpenAISettings> settings,
        ILogger<OpenAIRealtimeService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(_settings.ApiKey) || _settings.ApiKey == "YOUR_OPENAI_API_KEY_HERE")
            {
                _logger.LogError("OpenAI API Key is not configured");
                await NotifyError("OpenAI API Key is not configured in appsettings.json");
                return false;
            }

            _openAIWebSocket = new ClientWebSocket();
            _openAIWebSocket.Options.SetRequestHeader("Authorization", $"Bearer {_settings.ApiKey}");
            _openAIWebSocket.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");

            var uri = new Uri($"{_settings.RealtimeUrl}?model={_settings.Model}");
            _logger.LogInformation("Connecting to OpenAI Realtime API: {Uri}", uri);

            await _openAIWebSocket.ConnectAsync(uri, cancellationToken);
            _logger.LogInformation("Connected to OpenAI Realtime API");

            await NotifyStatus("Connected to OpenAI");

            // Configure session
            await ConfigureSessionAsync(cancellationToken);

            // Start receiving messages
            _receiveCts = new CancellationTokenSource();
            _ = Task.Run(() => ReceiveMessagesAsync(_receiveCts.Token), _receiveCts.Token);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to OpenAI");
            await NotifyError($"Connection error: {ex.Message}");
            return false;
        }
    }

    private async Task ConfigureSessionAsync(CancellationToken cancellationToken)
    {
        var sessionUpdate = new SessionUpdateEvent
        {
            Session = new SessionConfig
            {
                Modalities = new[] { "text", "audio" },
                Instructions = _settings.Instructions,
                Voice = _settings.Voice,
                InputAudioFormat = "pcm16",
                OutputAudioFormat = "pcm16",
                // Use gpt-4o-transcribe instead of whisper-1 for realtime transcription
                // Reference: https://community.openai.com/t/cant-get-the-user-transcription-in-realtime-api/1076308/5
                InputAudioTranscription = new TranscriptionConfig
                {
                    Model = "gpt-4o-transcribe"
                },
                TurnDetection = new TurnDetectionConfig
                {
                    Type = _settings.TurnDetection.Type,
                    Threshold = _settings.TurnDetection.Threshold,
                    PrefixPaddingMs = _settings.TurnDetection.PrefixPaddingMs,
                    SilenceDurationMs = _settings.TurnDetection.SilenceDurationMs
                },
                Temperature = _settings.Temperature,
                MaxResponseOutputTokens = _settings.MaxResponseOutputTokens
            }
        };

        await SendToOpenAIAsync(sessionUpdate, cancellationToken);
        _logger.LogInformation("Session configured");
    }

    public async Task SendAudioAsync(string base64Audio, CancellationToken cancellationToken = default)
    {
        if (_openAIWebSocket?.State != WebSocketState.Open)
        {
            _logger.LogWarning("WebSocket is not open, cannot send audio");
            return;
        }

        var appendEvent = new InputAudioBufferAppendEvent
        {
            Audio = base64Audio
        };

        await SendToOpenAIAsync(appendEvent, cancellationToken);
    }

    public async Task CommitAudioAsync(CancellationToken cancellationToken = default)
    {
        if (_openAIWebSocket?.State != WebSocketState.Open)
        {
            return;
        }

        var commitEvent = new InputAudioBufferCommitEvent();
        await SendToOpenAIAsync(commitEvent, cancellationToken);
    }

    public async Task CreateResponseAsync(CancellationToken cancellationToken = default)
    {
        if (_openAIWebSocket?.State != WebSocketState.Open)
        {
            return;
        }

        var responseEvent = new ResponseCreateEvent();
        await SendToOpenAIAsync(responseEvent, cancellationToken);
    }

    public async Task CancelResponseAsync(CancellationToken cancellationToken = default)
    {
        if (_openAIWebSocket?.State != WebSocketState.Open)
        {
            return;
        }

        var cancelEvent = new ResponseCancelEvent();
        await SendToOpenAIAsync(cancelEvent, cancellationToken);
    }

    private async Task SendToOpenAIAsync<T>(T eventData, CancellationToken cancellationToken) where T : class
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_openAIWebSocket?.State != WebSocketState.Open)
            {
                return;
            }

            var json = JsonSerializer.Serialize(eventData, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            _logger.LogDebug("Sending to OpenAI: {Json}", json.Length > 200 ? json.Substring(0, 200) + "..." : json);

            var bytes = Encoding.UTF8.GetBytes(json);
            await _openAIWebSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 64]; // 64KB buffer
        var messageBuilder = new StringBuilder();

        try
        {
            while (_openAIWebSocket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await _openAIWebSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("OpenAI WebSocket closed");
                    await NotifyStatus("Disconnected from OpenAI");
                    break;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                messageBuilder.Append(message);

                if (result.EndOfMessage)
                {
                    var completeMessage = messageBuilder.ToString();
                    messageBuilder.Clear();

                    await ProcessOpenAIMessageAsync(completeMessage);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Receive operation cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error receiving messages from OpenAI");
            await NotifyError($"Receive error: {ex.Message}");
        }
    }

    private async Task ProcessOpenAIMessageAsync(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            var eventType = typeElement.GetString();
            _logger.LogInformation("[OpenAI Event] {EventType}", eventType);
            
            // Log full message for debugging transcription issues
            if (eventType?.Contains("transcript") == true || eventType?.Contains("item.created") == true)
            {
                _logger.LogInformation("[OpenAI Event Full] {Message}", message);
            }

            switch (eventType)
            {
                case "session.created":
                case "session.updated":
                    _logger.LogInformation("Session event: {Type}", eventType);
                    await NotifyStatus("Session ready");
                    break;

                case "input_audio_buffer.speech_started":
                    _logger.LogInformation("User started speaking");
                    await NotifyStatus("User speaking...");
                    break;

                case "input_audio_buffer.speech_stopped":
                    _logger.LogInformation("User stopped speaking");
                    await NotifyStatus("Processing...");
                    break;

                case "input_audio_buffer.committed":
                    _logger.LogInformation("Audio committed");
                    break;

                case "conversation.item.created":
                    // Check if this item contains a transcript (for user input)
                    if (root.TryGetProperty("item", out var item))
                    {
                        var itemType = item.TryGetProperty("type", out var typeVal) ? typeVal.GetString() : null;
                        var itemRole = item.TryGetProperty("role", out var roleVal) ? roleVal.GetString() : null;
                        
                        _logger.LogInformation("[Item Created] Type: {Type}, Role: {Role}", itemType, itemRole);
                        
                        // For user messages, check if there's a transcript
                        if (itemRole == "user" && item.TryGetProperty("content", out var content))
                        {
                            foreach (var contentItem in content.EnumerateArray())
                            {
                                if (contentItem.TryGetProperty("transcript", out var transcript))
                                {
                                    var transcriptText = transcript.GetString();
                                    if (!string.IsNullOrEmpty(transcriptText))
                                    {
                                        _logger.LogInformation("[User Transcript] {Transcript}", transcriptText);
                                        await NotifyTranscript("user", transcriptText);
                                    }
                                }
                            }
                        }
                    }
                    break;

                case "conversation.item.input_audio_transcription.completed":
                    _logger.LogInformation("[Transcription Event] Processing input_audio_transcription.completed");
                    if (root.TryGetProperty("transcript", out var userTranscript))
                    {
                        var transcriptText = userTranscript.GetString();
                        _logger.LogInformation("[User Transcript Raw] '{Transcript}' (Empty: {IsEmpty})", 
                            transcriptText ?? "null", 
                            string.IsNullOrEmpty(transcriptText));
                        
                        if (!string.IsNullOrEmpty(transcriptText))
                        {
                            _logger.LogInformation("[User Transcript Complete] Sending to client: {Transcript}", transcriptText);
                            await NotifyTranscript("user", transcriptText);
                        }
                        else
                        {
                            _logger.LogWarning("[User Transcript] Transcript is empty or null");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("[User Transcript] No 'transcript' property found in event");
                    }
                    break;

                case "conversation.item.input_audio_transcription.failed":
                    _logger.LogError("[Transcription Failed] User audio transcription failed");
                    if (root.TryGetProperty("error", out var transcriptError))
                    {
                        _logger.LogError("[Transcription Error] {Error}", transcriptError);
                    }
                    break;

                case "response.audio.delta":
                    if (root.TryGetProperty("delta", out var audioDelta))
                    {
                        var audioData = audioDelta.GetString();
                        if (!string.IsNullOrEmpty(audioData))
                        {
                            _logger.LogDebug("[Audio Delta] Size: {Size} bytes", audioData.Length);
                            await NotifyAudio(audioData);
                        }
                    }
                    break;

                case "response.audio_transcript.delta":
                    if (root.TryGetProperty("delta", out var transcriptDelta))
                    {
                        var delta = transcriptDelta.GetString();
                        if (!string.IsNullOrEmpty(delta))
                        {
                            _logger.LogDebug("[Assistant Transcript Delta] {Delta}", delta);
                            await NotifyTranscript("assistant", delta);
                        }
                    }
                    break;

                case "response.audio_transcript.done":
                    if (root.TryGetProperty("transcript", out var fullTranscript))
                    {
                        _logger.LogInformation("Full transcript: {Transcript}", fullTranscript.GetString());
                    }
                    break;

                case "response.done":
                    _logger.LogInformation("Response completed");
                    await NotifyStatus("Ready");
                    // Notify client that response is complete
                    await NotifyResponseComplete();
                    break;

                case "error":
                    if (root.TryGetProperty("error", out var error))
                    {
                        var errorMessage = error.GetProperty("message").GetString() ?? "Unknown error";
                        _logger.LogError("OpenAI error: {Error}", errorMessage);
                        await NotifyError(errorMessage);
                    }
                    break;

                default:
                    _logger.LogWarning("[OpenAI Event] Unhandled event type: {Type} - Full message: {Message}", 
                        eventType, 
                        message.Length > 500 ? message.Substring(0, 500) + "..." : message);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing OpenAI message: {Message}", message);
        }
    }

    private async Task NotifyAudio(string base64Audio)
    {
        if (OnAudioReceived != null)
        {
            await OnAudioReceived(base64Audio);
        }
    }

    private async Task NotifyTranscript(string role, string transcript)
    {
        if (OnTranscriptReceived != null)
        {
            await OnTranscriptReceived(role, transcript);
        }
    }

    private async Task NotifyError(string error)
    {
        if (OnError != null)
        {
            await OnError(error);
        }
    }

    private async Task NotifyStatus(string status)
    {
        if (OnStatusChanged != null)
        {
            await OnStatusChanged(status);
        }
    }

    private async Task NotifyResponseComplete()
    {
        if (OnTranscriptReceived != null)
        {
            await OnTranscriptReceived("system", "__RESPONSE_DONE__");
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            _receiveCts?.Cancel();

            if (_openAIWebSocket?.State == WebSocketState.Open)
            {
                await _openAIWebSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Client disconnecting",
                    CancellationToken.None);
            }

            _logger.LogInformation("Disconnected from OpenAI");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from OpenAI");
        }
    }

    public void Dispose()
    {
        _receiveCts?.Cancel();
        _receiveCts?.Dispose();
        _openAIWebSocket?.Dispose();
        _sendLock?.Dispose();
    }
}
