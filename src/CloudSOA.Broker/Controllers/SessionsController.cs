using CloudSOA.Common.Exceptions;
using CloudSOA.Common.Interfaces;
using CloudSOA.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace CloudSOA.Broker.Controllers;

[ApiController]
[Route("api/v1/sessions")]
public class SessionsController : ControllerBase
{
    private readonly ISessionManager _sessionManager;
    private readonly ISessionStore _sessionStore;
    private readonly IRequestQueue _requestQueue;
    private readonly IResponseStore _responseStore;

    public SessionsController(
        ISessionManager sessionManager,
        ISessionStore sessionStore,
        IRequestQueue requestQueue,
        IResponseStore responseStore)
    {
        _sessionManager = sessionManager;
        _sessionStore = sessionStore;
        _requestQueue = requestQueue;
        _responseStore = responseStore;
    }

    /// <summary>List all active sessions.</summary>
    [HttpGet]
    public async Task<IActionResult> ListSessions(CancellationToken ct)
    {
        var sessions = await _sessionStore.ListAsync(ct);
        var result = new List<object>();

        foreach (var s in sessions)
        {
            var pending = await _requestQueue.GetQueueDepthAsync(s.SessionId, ct);
            var responses = await _responseStore.GetCountAsync(s.SessionId, ct);
            result.Add(new
            {
                id = s.SessionId,
                serviceName = s.ServiceName,
                status = s.State.ToString(),
                createdAt = s.CreatedAt,
                lastAccessedAt = s.LastAccessedAt,
                clientId = s.ClientId,
                requestCount = pending,
                responseCount = responses
            });
        }

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<SessionInfo>> CreateSession(
        [FromBody] CreateSessionRequest request, CancellationToken ct)
    {
        var session = await _sessionManager.CreateSessionAsync(request, ct);
        return CreatedAtAction(nameof(GetSession), new { sessionId = session.SessionId }, session);
    }

    [HttpGet("{sessionId}")]
    public async Task<ActionResult<SessionInfo>> GetSession(string sessionId, CancellationToken ct)
    {
        try
        {
            var session = await _sessionManager.GetSessionAsync(sessionId, ct);
            return Ok(session);
        }
        catch (SessionNotFoundException)
        {
            return NotFound(new { message = $"Session '{sessionId}' not found." });
        }
    }

    [HttpPost("{sessionId}/attach")]
    public async Task<ActionResult<SessionInfo>> AttachSession(
        string sessionId, [FromQuery] string? clientId, CancellationToken ct)
    {
        try
        {
            var session = await _sessionManager.AttachSessionAsync(sessionId, clientId, ct);
            return Ok(session);
        }
        catch (SessionNotFoundException)
        {
            return NotFound(new { message = $"Session '{sessionId}' not found." });
        }
        catch (SessionStateException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpDelete("{sessionId}")]
    public async Task<IActionResult> CloseSession(string sessionId, CancellationToken ct)
    {
        try
        {
            await _sessionManager.CloseSessionAsync(sessionId, ct);
            return NoContent();
        }
        catch (SessionNotFoundException)
        {
            return NotFound(new { message = $"Session '{sessionId}' not found." });
        }
    }

    [HttpGet("{sessionId}/status")]
    public async Task<ActionResult<SessionStatusResponse>> GetSessionStatus(
        string sessionId, CancellationToken ct)
    {
        try
        {
            var status = await _sessionManager.GetSessionStatusAsync(sessionId, ct);
            return Ok(status);
        }
        catch (SessionNotFoundException)
        {
            return NotFound(new { message = $"Session '{sessionId}' not found." });
        }
    }
}
