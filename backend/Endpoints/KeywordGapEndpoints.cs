using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using JobTracker.Api.Ai;
using JobTracker.Api.Auth;
using JobTracker.Api.Dtos;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace JobTracker.Api.Endpoints;

public static class KeywordGapEndpoints
{
    public static IEndpointRouteBuilder MapKeywordGapEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ai").RequireAuthorization();

        group.MapPost("/keyword-gap", AnalyzeAsync);

        return app;
    }

    // Order matters here. Everything that costs nothing (size, shape, caps) is checked first,
    // so a bad request never uses up a rate-limit permit or reaches the provider.
    private static async Task<IResult> AnalyzeAsync(
        HttpContext http,
        ClaimsPrincipal user,
        IOptions<KeywordGapLimitsOptions> limitsOptions,
        KeywordGapRateLimiter limiter,
        IKeywordGapAnalyzer analyzer)
    {
        // The report is built from a user's CV: no shared cache may keep it.
        http.Response.Headers.CacheControl = "no-store";

        var limits = limitsOptions.Value;
        var cancellationToken = http.RequestAborted;

        if (http.Request.ContentLength is { } declaredLength && declaredLength > limits.MaxBodyBytes)
        {
            return Fail(http, StatusCodes.Status413PayloadTooLarge, "too_large", "The request is too large.");
        }

        // Kestrel enforces this while the body is read, which also covers bodies sent without a length.
        var sizeFeature = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = limits.MaxBodyBytes;
        }

        KeywordGapRequest? request;
        try
        {
            request = await http.Request.ReadFromJsonAsync<KeywordGapRequest>(cancellationToken);
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Fail(http, StatusCodes.Status413PayloadTooLarge, "too_large", "The request is too large.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or BadHttpRequestException)
        {
            // Nothing from the exception is logged or returned: its message can quote the body.
            return Fail(http, StatusCodes.Status400BadRequest, "invalid_input", "Send a JSON body with a job description and a CV.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Results.StatusCode(499);
        }

        if (request is null ||
            string.IsNullOrWhiteSpace(request.JobDescription) ||
            string.IsNullOrWhiteSpace(request.CvText))
        {
            return Fail(http, StatusCodes.Status400BadRequest, "invalid_input", "Paste both a job description and a CV.");
        }

        if (request.JobDescription.Length > limits.MaxJobDescriptionChars)
        {
            return Fail(
                http,
                StatusCodes.Status413PayloadTooLarge,
                "too_long",
                "The job description is too long.",
                field: "jobDescription",
                maxLength: limits.MaxJobDescriptionChars);
        }

        if (request.CvText.Length > limits.MaxCvChars)
        {
            return Fail(
                http,
                StatusCodes.Status413PayloadTooLarge,
                "too_long",
                "The CV is too long.",
                field: "cvText",
                maxLength: limits.MaxCvChars);
        }

        // The caller is always the signed-in user. Nothing in the body can name another one.
        using var lease = limiter.TryAcquire(user.GetUserId());
        if (!lease.IsAcquired)
        {
            return Fail(
                http,
                StatusCodes.Status429TooManyRequests,
                "rate_limited",
                "You have reached the limit for now. Please wait and try again.",
                retryAfterSeconds: RetryAfterSeconds(lease));
        }

        KeywordGapOutcome outcome;
        try
        {
            outcome = await analyzer.AnalyzeAsync(request.JobDescription, request.CvText, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Results.StatusCode(499);
        }

        if (outcome.Result is { } report)
        {
            return Results.Ok(report);
        }

        // Our own 429 means "your limit". Provider trouble uses other statuses, so the two never look alike.
        return outcome.Failure switch
        {
            KeywordGapFailure.RateLimited => Fail(
                http,
                StatusCodes.Status503ServiceUnavailable,
                "ai_busy",
                "The AI service is busy. Please try again shortly.",
                retryAfterSeconds: outcome.RetryAfterSeconds),
            KeywordGapFailure.Timeout => Fail(
                http,
                StatusCodes.Status504GatewayTimeout,
                "ai_timeout",
                "The AI service took too long. Please try again."),
            KeywordGapFailure.BadModelOutput => Fail(
                http,
                StatusCodes.Status502BadGateway,
                "ai_bad_output",
                "The AI returned an unusable answer. Please try again."),
            _ => Fail(
                http,
                StatusCodes.Status502BadGateway,
                "ai_unavailable",
                "The AI service is not available right now. Please try again later.")
        };
    }

    private static IResult Fail(
        HttpContext http,
        int status,
        string code,
        string message,
        int? retryAfterSeconds = null,
        string? field = null,
        int? maxLength = null)
    {
        if (retryAfterSeconds is { } seconds)
        {
            http.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return Results.Json(
            new KeywordGapError(code, message, retryAfterSeconds, field, maxLength),
            statusCode: status);
    }

    private static int? RetryAfterSeconds(RateLimitLease lease) =>
        lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? (int)Math.Ceiling(retryAfter.TotalSeconds)
            : null;
}
