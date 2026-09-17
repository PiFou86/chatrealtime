using chatrealtime.Controllers;
using chatrealtime.Models;
using chatrealtime.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace chatrealtime.Tests;

public sealed class LiveControllerTests
{
    [Fact]
    public async Task Create_returns_sdp_answer()
    {
        var manager = new FakeSessionManager
        {
            Result = new BrowserLiveSessionResponse("live_123", "answer")
        };
        var controller = new LiveController(manager, NullLogger<LiveController>.Instance);

        var result = await controller.CreateSession(new BrowserLiveSessionRequest { Sdp = "offer" }, default);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, objectResult.StatusCode);
        Assert.Equal("offer", manager.Offer);
        Assert.Equal("answer", Assert.IsType<BrowserLiveSessionResponse>(objectResult.Value).Sdp);
    }

    [Fact]
    public async Task Create_maps_missing_key_to_service_unavailable()
    {
        var controller = new LiveController(
            new FakeSessionManager { Exception = new InvalidOperationException("missing key") },
            NullLogger<LiveController>.Instance);

        var result = await controller.CreateSession(new BrowserLiveSessionRequest { Sdp = "offer" }, default);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Create_preserves_openai_http_error_status()
    {
        var controller = new LiveController(
            new FakeSessionManager { Exception = new LiveApiException(429, "rate limited") },
            NullLogger<LiveController>.Instance);

        var result = await controller.CreateSession(new BrowserLiveSessionRequest { Sdp = "offer" }, default);

        Assert.Equal(StatusCodes.Status429TooManyRequests, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    private sealed class FakeSessionManager : ILiveSessionManager
    {
        public BrowserLiveSessionResponse? Result { get; init; }
        public Exception? Exception { get; init; }
        public string? Offer { get; private set; }

        public Task<BrowserLiveSessionResponse> CreateAsync(string offerSdp, CancellationToken cancellationToken)
        {
            Offer = offerSdp;
            return Exception is null
                ? Task.FromResult(Result!)
                : Task.FromException<BrowserLiveSessionResponse>(Exception);
        }

        public Task CloseAsync(string sessionId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
