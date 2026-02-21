using System.Diagnostics;

namespace CloudSOA.Broker.Middleware;

/// <summary>
/// Structured audit logging for all mutating API operations.
/// Logs: who (identity), what (method + path), when, result (status code), duration.
/// Output goes to console/stdout and is captured by Azure Monitor / Log Analytics.
/// </summary>
public class AuditLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLoggingMiddleware> _logger;

    // HTTP methods that represent mutating operations
    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH", "DELETE"
    };

    public AuditLoggingMiddleware(RequestDelegate next, ILogger<AuditLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? "/";

        // Skip non-API and non-mutating requests (GET /healthz, /metrics, /)
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var isMutating = MutatingMethods.Contains(method);
        var sw = Stopwatch.StartNew();

        await _next(context);

        sw.Stop();
        var statusCode = context.Response.StatusCode;
        var identity = GetIdentity(context);

        if (isMutating)
        {
            // Always log mutating operations (create, update, delete)
            _logger.LogInformation(
                "[AUDIT] {Method} {Path} by {Identity} → {StatusCode} ({Duration}ms)",
                method, path, identity, statusCode, sw.ElapsedMilliseconds);
        }
        else if (statusCode >= 400)
        {
            // Log failed read attempts (unauthorized, forbidden, etc.)
            _logger.LogWarning(
                "[AUDIT] {Method} {Path} by {Identity} → {StatusCode} ({Duration}ms)",
                method, path, identity, statusCode, sw.ElapsedMilliseconds);
        }
    }

    private static string GetIdentity(HttpContext context)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var name = user.Identity.Name
                ?? user.FindFirst("sub")?.Value
                ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var roles = user.Claims
                .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role || c.Type == "roles")
                .Select(c => c.Value);
            var roleStr = string.Join(",", roles);
            return string.IsNullOrEmpty(roleStr) ? (name ?? "authenticated") : $"{name ?? "user"}[{roleStr}]";
        }

        // Check for API key (identity set by auth middleware)
        if (context.Items.ContainsKey("AuthMethod"))
            return $"apikey[{context.Items["AuthMethod"]}]";

        return "anonymous";
    }
}
