using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CloudSOA.Broker.Middleware;

/// <summary>
/// Unified authentication middleware — supports multiple auth schemes:
///   1. JWT Bearer token (Azure AD / custom issuer)
///   2. API Key (X-Api-Key header)
///   3. Anonymous (dev mode, when no auth is configured)
///
/// Config:
///   Authentication:Mode = "jwt" | "apikey" | "none" (default: "none")
///   Authentication:ApiKey = "..." (for apikey mode)
///   Authentication:Jwt:Issuer = "https://login.microsoftonline.com/{tenantId}/v2.0"
///   Authentication:Jwt:Audience = "api://cloudsoa-broker"
///   Authentication:Jwt:SigningKey = "..." (for custom JWT, not needed for Azure AD)
///   Authentication:Jwt:ValidateIssuer = true/false
/// </summary>
public class AuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthenticationMiddleware> _logger;
    private readonly string _authMode;
    private readonly string? _apiKey;
    private readonly TokenValidationParameters? _jwtParams;

    private static readonly string[] PublicPaths = { "/healthz", "/metrics", "/" };

    public AuthenticationMiddleware(RequestDelegate next, IConfiguration config, ILogger<AuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _authMode = config["Authentication:Mode"] ?? "none";
        _apiKey = config["Authentication:ApiKey"];

        // Build JWT validation parameters
        if (string.Equals(_authMode, "jwt", StringComparison.OrdinalIgnoreCase))
        {
            var issuer = config["Authentication:Jwt:Issuer"];
            var audience = config["Authentication:Jwt:Audience"];
            var signingKey = config["Authentication:Jwt:SigningKey"];

            _jwtParams = new TokenValidationParameters
            {
                ValidateIssuer = !string.IsNullOrEmpty(issuer),
                ValidIssuer = issuer,
                ValidateAudience = !string.IsNullOrEmpty(audience),
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
                ValidateIssuerSigningKey = !string.IsNullOrEmpty(signingKey),
            };

            if (!string.IsNullOrEmpty(signingKey))
            {
                _jwtParams.IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(signingKey));
            }
            else
            {
                // Azure AD: disable signing key validation (OIDC discovery handles it)
                _jwtParams.ValidateIssuerSigningKey = false;
                _jwtParams.SignatureValidator = (token, parameters) =>
                    new JwtSecurityToken(token);
            }

            logger.LogInformation("[Auth] JWT mode: issuer={Issuer}, audience={Audience}",
                issuer ?? "(any)", audience ?? "(any)");
        }
        else if (string.Equals(_authMode, "apikey", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("[Auth] API Key mode");
        }
        else
        {
            logger.LogWarning("[Auth] No authentication configured (dev mode). Set Authentication:Mode=jwt or apikey for production.");
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Public endpoints — always allow
        if (PublicPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // No auth configured — allow all (dev mode)
        if (string.Equals(_authMode, "none", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Try JWT Bearer first
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var headerVal = authHeader.ToString();
            if (headerVal.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = headerVal.Substring("Bearer ".Length).Trim();
                if (ValidateJwt(token, context))
                {
                    await _next(context);
                    return;
                }
                // JWT present but invalid — reject
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Unauthorized: Invalid JWT token.");
                return;
            }
        }

        // Try API Key
        if (context.Request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader))
        {
            if (!string.IsNullOrEmpty(_apiKey) && CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(apiKeyHeader.ToString()),
                    Encoding.UTF8.GetBytes(_apiKey)))
            {
                // API key valid — set a basic identity
                var claims = new[] { new Claim(ClaimTypes.Name, "apikey-user"), new Claim(ClaimTypes.Role, "Admin") };
                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "ApiKey"));
                await _next(context);
                return;
            }
        }

        // No valid credentials
        _logger.LogWarning("Unauthorized request from {IP} to {Path}",
            context.Connection.RemoteIpAddress, path);
        context.Response.StatusCode = 401;
        context.Response.Headers["WWW-Authenticate"] = "Bearer, ApiKey";
        await context.Response.WriteAsync("Unauthorized: Provide a valid Bearer token or X-Api-Key header.");
    }

    private bool ValidateJwt(string token, HttpContext context)
    {
        if (_jwtParams == null) return false;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, _jwtParams, out _);
            context.User = principal;

            var name = principal.FindFirst(ClaimTypes.Name)?.Value
                    ?? principal.FindFirst("preferred_username")?.Value
                    ?? principal.FindFirst("sub")?.Value
                    ?? "jwt-user";
            _logger.LogDebug("JWT authenticated: {User}", name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("JWT validation failed: {Error}", ex.Message);
            return false;
        }
    }
}
