using System.Text.Json;
using chatrealtime.Models;
using chatrealtime.Services;

namespace chatrealtime.Tests;

public sealed class LiveContractsTests
{
    [Fact]
    public void Session_configuration_serializes_live_voice_delegation_tools_and_permissions()
    {
        var config = TestDoubles.CreateConfiguration();
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(config));
        var root = document.RootElement;

        Assert.Equal("gpt-live-1", root.GetProperty("model").GetString());
        Assert.Equal("echo", root.GetProperty("audio").GetProperty("output").GetProperty("voice").GetString());
        Assert.Equal("Conversation instructions", root.GetProperty("instructions").GetString());
        Assert.Equal("responses", root.GetProperty("delegation").GetProperty("type").GetString());
        Assert.Equal("gpt-5.6-luna", root.GetProperty("delegation").GetProperty("responses").GetProperty("model").GetString());
        Assert.Equal("auto", root.GetProperty("delegation").GetProperty("responses").GetProperty("tool_choice").GetString());
        Assert.Equal("weather", root.GetProperty("delegation").GetProperty("responses").GetProperty("tools")[0].GetProperty("name").GetString());

        var dataChannel = root.GetProperty("client").GetProperty("data_channel");
        var clientEvents = dataChannel.GetProperty("allowed_client_events").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Equal(LiveConfigurationFactory.AllowedClientEvents, clientEvents);
        Assert.DoesNotContain("session.update", clientEvents);

        var serverEvents = dataChannel.GetProperty("allowed_server_events");
        Assert.Contains(serverEvents.EnumerateArray(), item => item.GetProperty("type").GetString() == "session.input_transcript.delta");
        Assert.Contains(serverEvents.EnumerateArray(), item => item.GetProperty("type").GetString() == "session.output_transcript.delta");
        Assert.DoesNotContain(serverEvents.EnumerateArray(), item => item.GetProperty("type").GetString() == "response.event");
        Assert.DoesNotContain(serverEvents.EnumerateArray(), item => item.GetProperty("type").GetString() == "session.delegation.created");
    }
}
