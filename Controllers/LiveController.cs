using chatrealtime.Models;
using chatrealtime.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net.WebSockets;

namespace chatrealtime.Controllers;

[ApiController]
[Route("api/live")]
public sealed class LiveController : ControllerBase
{
    private readonly ILiveSessionManager _sessions;
    private readonly ILogger<LiveController> _logger;

    public LiveController(ILiveSessionManager sessions, ILogger<LiveController> logger)
    {
        _sessions = sessions;
        _logger = logger;
    }

    [HttpPost("session")]
    [ProducesResponseType<BrowserLiveSessionResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateSession(
        [FromBody] BrowserLiveSessionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var session = await _sessions.CreateAsync(request.Sdp, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, session);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (LiveApiException ex)
        {
            _logger.LogWarning("OpenAI Live session creation failed with HTTP {Status}: {Message}", ex.StatusCode, ex.Message);
            return Problem(
                "OpenAI rejected the Live session request.",
                statusCode: ex.StatusCode is >= 400 and < 600 ? ex.StatusCode : StatusCodes.Status502BadGateway);
        }
        catch (Exception ex) when (ex is HttpRequestException or WebSocketException)
        {
            _logger.LogWarning(ex, "Could not establish the OpenAI Live session or sideband");
            return Problem("Could not establish the OpenAI Live session.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    [HttpDelete("session/{sessionId}")]
    public async Task<IActionResult> CloseSession(string sessionId, CancellationToken cancellationToken)
    {
        await _sessions.CloseAsync(sessionId, cancellationToken);
        return NoContent();
    }
}
