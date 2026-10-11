using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maf.ServiceDefaults;

/// <summary>
/// Rejects requests whose <c>X-Api-Key</c> header doesn't match the configured key.
/// Uses a constant-time comparison to avoid timing side channels.
/// </summary>
public sealed class ApiKeyEndpointFilter(IOptionsMonitor<ApiKeyOptions> options) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!IsAuthorized(context.HttpContext, options.CurrentValue.ApiKey))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Missing or invalid API key.");
        }

        return await next(context);
    }

    internal static bool IsAuthorized(HttpContext httpContext, string? expectedKey)
    {
        if (string.IsNullOrEmpty(expectedKey))
        {
            return true;
        }

        if (!httpContext.Request.Headers.TryGetValue(ApiKeyOptions.HeaderName, out var provided))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided.ToString()),
            Encoding.UTF8.GetBytes(expectedKey));
    }
}

public static class ApiKeyExtensions
{
    public static TBuilder RequireApiKey<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder.AddEndpointFilter<TBuilder, ApiKeyEndpointFilter>();

    /// <summary>
    /// Middleware variant for endpoints that aren't minimal-API handlers (e.g. the MCP endpoint).
    /// </summary>
    public static IApplicationBuilder UseApiKeyFor(this IApplicationBuilder app, PathString pathPrefix)
        => app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments(pathPrefix))
            {
                var key = ctx.RequestServices.GetRequiredService<IOptionsMonitor<ApiKeyOptions>>().CurrentValue.ApiKey;
                if (!ApiKeyEndpointFilter.IsAuthorized(ctx, key))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
            }

            await next();
        });
}
