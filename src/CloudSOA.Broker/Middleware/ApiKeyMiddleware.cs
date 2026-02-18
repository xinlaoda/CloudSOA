using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CloudSOA.Broker.Middleware;

/// <summary>
/// API Key 认证中间件 — 简单认证，生产环境建议用 Azure AD / JWT
/// 请求 Header: X-Api-Key: {key}
/// </summary>
public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _apiKey;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    // 不需要认证的路径
    private static readonly string[] PublicPaths = { "/healthz", "/metrics", "/" };

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration config, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _apiKey = config["Authentication:ApiKey"];
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip auth for public endpoints
        var path = context.Request.Path.Value ?? "";
        if (PublicPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // Skip if no API key configured (dev mode)
        if (string.IsNullOrEmpty(_apiKey))
        {
            await _next(context);
            return;
        }

        // Check API key
        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var providedKey) ||
            providedKey != _apiKey)
        {
            _logger.LogWarning("Unauthorized request from {IP} to {Path}",
                context.Connection.RemoteIpAddress, path);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized: Invalid or missing API key.");
            return;
        }

        await _next(context);
    }
}
