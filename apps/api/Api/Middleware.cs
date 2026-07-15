using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace GoldenHour.Api.Api;

public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var requested = context.Request.Headers[Header].FirstOrDefault();
        var correlationId = requested is not null && SafeCorrelationId().IsMatch(requested)
            ? requested
            : Guid.NewGuid().ToString("N");
        context.TraceIdentifier = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);
        context.Response.Headers[Header] = correlationId;
        await next(context);
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeCorrelationId();
}

public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            InvalidDataException => (StatusCodes.Status400BadRequest, "Invalid request data"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            UnauthorizedAccessException or SecurityTokenException => (StatusCodes.Status403Forbidden, "Access denied"),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrent update conflict"),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Request cannot be completed"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };
        if (status >= 500) logger.LogError(exception, "Unhandled API failure with correlation ID {CorrelationId}.", context.TraceIdentifier);
        else logger.LogWarning("API request failed safely with {ExceptionType} and correlation ID {CorrelationId}.", exception.GetType().Name, context.TraceIdentifier);
        context.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status >= 500 ? "The request could not be completed safely." : exception.Message,
                Type = $"https://httpstatuses.io/{status}",
                Extensions = { ["correlationId"] = context.TraceIdentifier }
            }
        });
    }
}

public sealed class SameOriginMutationMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var unsafeMethod = context.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE";
        var webhook = context.Request.Path.StartsWithSegments("/api/v1/webhooks");
        if (!environment.IsDevelopment() && unsafeMethod && !webhook && !HasTrustedBrowserSource(context))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = 403, Title = "Cross-site request rejected" });
            return;
        }
        await next(context);
    }

    private static bool HasTrustedBrowserSource(HttpContext context)
    {
        var expected = $"{context.Request.Scheme}://{context.Request.Host}";
        if (context.Request.Headers.TryGetValue("Origin", out var origin))
            return string.Equals(origin, expected, StringComparison.OrdinalIgnoreCase);
        if (context.Request.Headers.TryGetValue("Referer", out var referer)
            && Uri.TryCreate(referer.FirstOrDefault(), UriKind.Absolute, out var uri))
            return string.Equals(uri.GetLeftPart(UriPartial.Authority), expected, StringComparison.OrdinalIgnoreCase);
        return context.Request.Headers.TryGetValue("Sec-Fetch-Site", out var fetchSite)
            && fetchSite.Any(value => value is "same-origin" or "none");
    }
}

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self' ws: wss:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Permissions-Policy"] = "camera=(), geolocation=(self), microphone=(self), payment=(), usb=()";
            context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            return Task.CompletedTask;
        });
        await next(context);
    }
}
