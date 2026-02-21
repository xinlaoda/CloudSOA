using System.Security.Claims;

namespace CloudSOA.Broker.Middleware;

/// <summary>
/// Role-based authorization middleware.
/// Checks ClaimTypes.Role from the authenticated user against endpoint requirements.
///
/// Roles:
///   Admin  — full access (service register/deploy/delete, session manage, metrics)
///   User   — create/close own sessions, submit requests, get responses
///   Reader — read-only (list sessions, get status, metrics)
///
/// When auth mode is "none", all requests are allowed (dev mode).
/// Role mapping:
///   - JWT: roles claim from token (Azure AD app roles or custom)
///   - API Key: always Admin role
/// </summary>
public class AuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthorizationMiddleware> _logger;
    private readonly string _authMode;

    private static readonly string[] PublicPaths = { "/healthz", "/metrics", "/" };

    // Endpoint → minimum role (Admin > User > Reader)
    private static readonly Dictionary<string, (string Method, string MinRole)[]> EndpointRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Session management
        ["/api/v1/sessions"] = new[]
        {
            ("GET", "Reader"),
            ("POST", "User"),      // create session
        },
        // Service management (ServiceManager — proxied or direct)
        ["/api/v1/services"] = new[]
        {
            ("GET", "Reader"),
            ("POST", "Admin"),     // register service
        },
    };

    // Path patterns that require Admin role (service lifecycle operations)
    private static readonly string[] AdminWritePrefixes =
    {
        "/api/v1/services/*/deploy",
        "/api/v1/services/*/stop",
        "/api/v1/services/*/scale",
    };

    public AuthorizationMiddleware(RequestDelegate next, IConfiguration config, ILogger<AuthorizationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _authMode = config["Authentication:Mode"] ?? "none";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var method = context.Request.Method;

        // Public paths — always allow
        if (PublicPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // No auth → no authorization check (dev mode)
        if (string.Equals(_authMode, "none", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Must be authenticated at this point
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        var userRoles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Azure AD uses "roles" claim directly
        userRoles.UnionWith(context.User.FindAll("roles").Select(c => c.Value));

        var requiredRole = GetRequiredRole(path, method);
        if (requiredRole != null && !HasSufficientRole(userRoles, requiredRole))
        {
            var user = context.User.FindFirst(ClaimTypes.Name)?.Value
                    ?? context.User.FindFirst("preferred_username")?.Value ?? "unknown";
            _logger.LogWarning("Forbidden: user={User} roles=[{Roles}] required={Required} path={Path}",
                user, string.Join(",", userRoles), requiredRole, path);
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync($"Forbidden: requires {requiredRole} role.");
            return;
        }

        await _next(context);
    }

    private static string? GetRequiredRole(string path, string method)
    {
        // Check exact path matches
        foreach (var kv in EndpointRoles)
        {
            if (path.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))
            {
                var match = kv.Value.FirstOrDefault(r =>
                    r.Method.Equals(method, StringComparison.OrdinalIgnoreCase));
                if (match != default) return match.MinRole;
            }
        }

        // Admin write operations on services
        if (method is "POST" or "PUT" or "DELETE")
        {
            if (path.StartsWith("/api/v1/services/", StringComparison.OrdinalIgnoreCase))
            {
                // deploy/stop/scale/delete → Admin
                var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 4) // api/v1/services/{name}/{action}
                    return "Admin";
                // DELETE /api/v1/services/{name} → Admin
                if (method == "DELETE")
                    return "Admin";
                // PUT /api/v1/services/{name} → Admin
                if (method == "PUT")
                    return "Admin";
            }
        }

        // Session sub-paths
        if (path.StartsWith("/api/v1/sessions/", StringComparison.OrdinalIgnoreCase))
        {
            // DELETE session → User (own session)
            if (method == "DELETE") return "User";
            // POST requests/flush/attach → User
            if (method == "POST") return "User";
            // GET responses/status → Reader
            if (method == "GET") return "Reader";
        }

        // GET cluster metrics → Reader
        if (path.StartsWith("/api/v1/metrics", StringComparison.OrdinalIgnoreCase) && method == "GET")
            return "Reader";

        // Default: require User role for any unmatched API path
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            return "User";

        return null;
    }

    /// <summary>Admin > User > Reader hierarchy</summary>
    private static bool HasSufficientRole(HashSet<string> userRoles, string requiredRole)
    {
        if (userRoles.Contains("Admin")) return true;
        if (requiredRole == "User" && userRoles.Contains("User")) return true;
        if (requiredRole == "Reader" && (userRoles.Contains("Reader") || userRoles.Contains("User"))) return true;
        return false;
    }
}
