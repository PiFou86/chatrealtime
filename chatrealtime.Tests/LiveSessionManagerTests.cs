using System.Collections.Concurrent;
using System.Text.Json;
using chatrealtime.Configuration;
using chatrealtime.Models;
using chatrealtime.Services;
using chatrealtime.Services.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace chatrealtime.Tests;

public sealed class LiveSessionManagerTests
{
    [Fact]
    public async Task Create_attaches_sideband_before_returning_answer()
    {
        var events = new List<string>();
        var client = new FakeLiveClient(events);
        await using var manager = CreateManager(client, new FakeToolExecutor());

        var result = await manager.CreateAsync("offer", default);

        Assert.Equal(["create", "attach"], events);
        Assert.Equal("answer", result.Sdp);
        Assert.True(client.Connection.RunStarted.Task.IsCompleted);
    }

    [Fact]
    public async Task Create_rejects_missing_api_key_without_calling_openai()
    {
        var client = new FakeLiveClient([]);
        await using var manager = CreateManager(client, new FakeToolExecutor(), apiKey: "YOUR_OPENAI_API_KEY_HERE");

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CreateAsync("offer", default));
        Assert.Equal(0, client.CreateCalls);
    }

    [Fact]
    public async Task Valid_tool_call_sends_result_then_continues_response()
    {
        var client = new FakeLiveClient([]);
        var tools = new FakeToolExecutor { Result = new { temperature = 21 } };
        await using var manager = CreateManager(client, tools);
        await manager.CreateAsync("offer", default);
        await client.Connection.RunStarted.Task;

        await client.Connection.EmitAsync(ToolEvent("{\"city\":\"Montréal\"}"));

        Assert.Equal("weather", tools.LastToolName);
        Assert.Equal(2, client.Connection.Sent.Count);
        Assert.Equal("response.item.create", client.Connection.Sent[0].GetProperty("type").GetString());
        Assert.Contains("temperature", client.Connection.Sent[0].GetProperty("item").GetProperty("output").GetString());
        Assert.Equal("response.create", client.Connection.Sent[1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Invalid_arguments_are_returned_as_tool_error_and_response_continues()
    {
        var client = new FakeLiveClient([]);
        var tools = new FakeToolExecutor();
        await using var manager = CreateManager(client, tools);
        await manager.CreateAsync("offer", default);
        await client.Connection.RunStarted.Task;

        await client.Connection.EmitAsync(ToolEvent("not-json"));

        Assert.Null(tools.LastToolName);
        Assert.Equal(2, client.Connection.Sent.Count);
        Assert.Contains("error", client.Connection.Sent[0].GetProperty("item").GetProperty("output").GetString());
    }

    [Fact]
    public async Task Tool_failure_is_returned_and_unknown_events_are_ignored()
    {
        var client = new FakeLiveClient([]);
        var tools = new FakeToolExecutor { Exception = new InvalidOperationException("tool failed") };
        await using var manager = CreateManager(client, tools);
        await manager.CreateAsync("offer", default);
        await client.Connection.RunStarted.Task;

        await client.Connection.EmitAsync(JsonSerializer.SerializeToElement(new { type = "future.event" }));
        Assert.Empty(client.Connection.Sent);

        await client.Connection.EmitAsync(ToolEvent("{}"));
        Assert.Contains("tool failed", client.Connection.Sent[0].GetProperty("item").GetProperty("output").GetString());
        Assert.Equal("response.create", client.Connection.Sent[1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Session_closed_removes_and_disposes_sideband()
    {
        var client = new FakeLiveClient([]);
        await using var manager = CreateManager(client, new FakeToolExecutor());
        await manager.CreateAsync("offer", default);
        await client.Connection.RunStarted.Task;

        await client.Connection.EmitAsync(JsonSerializer.SerializeToElement(new { type = "session.closed" }));

        Assert.Equal(1, client.Connection.DisposeCalls);
    }

    [Fact]
    public async Task Stop_and_dispose_are_idempotent()
    {
        var client = new FakeLiveClient([]);
        var manager = CreateManager(client, new FakeToolExecutor());
        await manager.CreateAsync("offer", default);
        await client.Connection.RunStarted.Task;

        await manager.StopAsync(default);
        await manager.StopAsync(default);
        await manager.DisposeAsync();

        Assert.Equal(1, client.Connection.DisposeCalls);
    }

    [Fact]
    public async Task Create_is_rejected_after_shutdown_starts()
    {
        var client = new FakeLiveClient([]);
        await using var manager = CreateManager(client, new FakeToolExecutor());
        await manager.StopAsync(default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CreateAsync("offer", default));
        Assert.Equal(0, client.CreateCalls);
    }

    private static LiveSessionManager CreateManager(
        FakeLiveClient client,
        FakeToolExecutor executor,
        string apiKey = "test-key") => new(
            client,
            new FakeConfigurationFactory(),
            executor,
            Options.Create(new OpenAISettings { ApiKey = apiKey }),
            NullLogger<LiveSessionManager>.Instance);

    private static JsonElement ToolEvent(string arguments) => JsonSerializer.SerializeToElement(new
    {
        type = "response.event",
        delegation_id = "del_123",
        @event = new
        {
            type = "response.output_item.done",
            item = new { type = "function_call", call_id = "call_123", name = "weather", arguments }
        }
    });
}

internal static class TestDoubles
{
    public static LiveSessionConfig CreateConfiguration() => new()
    {
        Model = "gpt-live-1",
        Instructions = "Conversation instructions",
        Audio = new LiveAudioConfig { Output = new LiveAudioOutputConfig { Voice = "echo" } },
        Delegation = new LiveDelegationConfig
        {
            Responses = new LiveResponsesConfig
            {
                Model = "gpt-5.6-luna",
                Instructions = "Tool instructions",
                MaxOutputTokens = 512,
                Tools =
                [
                    new LiveFunctionTool
                    {
                        Name = "weather",
                        Parameters = JsonSerializer.SerializeToElement(new { type = "object" })
                    }
                ]
            }
        },
        Client = new LiveClientConfig
        {
            DataChannel = new LiveDataChannelConfig
            {
                AllowedClientEvents = LiveConfigurationFactory.AllowedClientEvents,
                AllowedServerEvents = LiveConfigurationFactory.AllowedServerEvents
            }
        }
    };
}

internal sealed class FakeConfigurationFactory : ILiveConfigurationFactory
{
    public Task<LiveSessionConfig> CreateAsync(CancellationToken cancellationToken) =>
        Task.FromResult(TestDoubles.CreateConfiguration());
}

internal sealed class FakeToolExecutor : IToolExecutor
{
    public object Result { get; init; } = new { ok = true };
    public Exception? Exception { get; init; }
    public string? LastToolName { get; private set; }

    public Task<object> ExecuteAsync(string toolName, JsonElement arguments)
    {
        LastToolName = toolName;
        return Exception is null ? Task.FromResult(Result) : Task.FromException<object>(Exception);
    }

    public List<string> GetAvailableTools() => ["weather"];
}

internal sealed class FakeLiveClient(List<string> events) : IOpenAILiveClient
{
    public FakeSidebandConnection Connection { get; } = new();
    public int CreateCalls { get; private set; }

    public Task<LiveCreateResponse> CreateSessionAsync(LiveCreateRequest request, CancellationToken cancellationToken)
    {
        CreateCalls++;
        events.Add("create");
        return Task.FromResult(new LiveCreateResponse
        {
            Session = new LiveSessionResource { Id = "live_123" },
            Transport = new LiveTransport { Sdp = "answer" }
        });
    }

    public Task<ILiveSidebandConnection> AttachSidebandAsync(string sessionId, CancellationToken cancellationToken)
    {
        events.Add("attach");
        return Task.FromResult<ILiveSidebandConnection>(Connection);
    }
}

internal sealed class FakeSidebandConnection : ILiveSidebandConnection
{
    private Func<JsonElement, Task>? _onEvent;
    private Func<Task>? _onClosed;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource RunStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<JsonElement> Sent { get; } = [];
    public int DisposeCalls { get; private set; }

    public Task SendAsync(JsonElement message, CancellationToken cancellationToken)
    {
        Sent.Add(message.Clone());
        return Task.CompletedTask;
    }

    public async Task RunAsync(Func<JsonElement, Task> onEvent, Func<Task> onClosed, CancellationToken cancellationToken)
    {
        _onEvent = onEvent;
        _onClosed = onClosed;
        RunStarted.TrySetResult();
        try { await _closed.Task.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { }
        await onClosed();
    }

    public async Task EmitAsync(JsonElement message)
    {
        await _onEvent!(message);
        if (message.TryGetProperty("type", out var type) && type.GetString() == "session.closed")
        {
            _closed.TrySetResult();
            await _onClosed!();
        }
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        _closed.TrySetResult();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        _closed.TrySetResult();
        return ValueTask.CompletedTask;
    }
}
