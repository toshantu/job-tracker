using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobTracker.Api.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobTracker.Api.Ai;

public interface IKeywordGapAnalyzer
{
    Task<KeywordGapOutcome> AnalyzeAsync(string jobDescription, string cvText, CancellationToken cancellationToken);
}

// Sends one non-streaming request to Groq and turns the answer into a validated report.
// Logs and outcomes carry categories, status codes and counts only: never the key, the texts,
// the model output or any provider error text.
public sealed class KeywordGapAnalyzer : IKeywordGapAnalyzer
{
    private const int MaxResponseBytes = 262_144;

    // Error types and codes are kept only if they look like short machine-readable tokens.
    private static readonly Regex SafeToken = new(
        "^[a-z0-9_.\\-]{1,64}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<GroqOptions> _options;
    private readonly ILogger<KeywordGapAnalyzer> _logger;

    public KeywordGapAnalyzer(
        IHttpClientFactory httpClientFactory,
        IOptions<GroqOptions> options,
        ILogger<KeywordGapAnalyzer> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<KeywordGapOutcome> AnalyzeAsync(string jobDescription, string cvText, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = _options.Value;
        var redaction = CvRedactor.Redact(cvText);
        var requestJson = KeywordGapPrompt.BuildRequestJson(options, jobDescription.Trim(), redaction.Text);
        var stopwatch = Stopwatch.StartNew();

        int? providerStatus = null;
        ProviderError providerError = new(null, null);
        int? remainingTokens = null;
        TokenUsage? usage = null;

        KeywordGapDiagnostics Diagnostics() => new(
            providerStatus,
            providerError.Type,
            providerError.Code,
            usage,
            remainingTokens,
            stopwatch.ElapsedMilliseconds,
            redaction.EmailsRemoved,
            redaction.PhonesRemoved,
            redaction.LinksRemoved);

        try
        {
            var client = _httpClientFactory.CreateClient("Groq");
            client.MaxResponseContentBufferSize = MaxResponseBytes;

            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

            using var response = await client.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);

            providerStatus = (int)response.StatusCode;
            remainingTokens = ReadNumberHeader(response, "x-ratelimit-remaining-tokens");

            if (!response.IsSuccessStatusCode)
            {
                providerError = ReadError(body);
                var failure = MapFailure(response.StatusCode, providerError);
                var retryAfter = failure == KeywordGapFailure.RateLimited
                    ? ReadNumberHeader(response, "retry-after")
                    : null;

                return Failed(failure, retryAfter, Diagnostics());
            }

            var completion = ReadCompletion(body);
            usage = completion.Usage;

            if (completion.Content is null || !ModelOutputParser.TryParse(completion.Content, out var keywords))
            {
                return Failed(KeywordGapFailure.BadModelOutput, null, Diagnostics());
            }

            var result = KeywordGapResponse.FromKeywords(keywords);
            var diagnostics = Diagnostics();

            _logger.LogInformation(
                "Keyword gap analysis succeeded in {ElapsedMs} ms: {KeywordCount} keywords, {PromptTokens} prompt and {CompletionTokens} completion tokens, {Emails} emails, {Phones} phones and {Links} links removed",
                diagnostics.ElapsedMilliseconds,
                result.Keywords.Count,
                usage?.PromptTokens,
                usage?.CompletionTokens,
                diagnostics.EmailsRemoved,
                diagnostics.PhonesRemoved,
                diagnostics.LinksRemoved);

            return new KeywordGapOutcome(result, null, null, diagnostics);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed(KeywordGapFailure.Timeout, null, Diagnostics());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Only the exception type is logged: its message could carry provider or user text.
            _logger.LogWarning(
                "Keyword gap analysis raised {ExceptionType} after {ElapsedMs} ms",
                exception.GetType().Name,
                stopwatch.ElapsedMilliseconds);

            return new KeywordGapOutcome(null, KeywordGapFailure.ProviderUnavailable, null, Diagnostics());
        }
    }

    private KeywordGapOutcome Failed(KeywordGapFailure failure, int? retryAfterSeconds, KeywordGapDiagnostics diagnostics)
    {
        _logger.LogWarning(
            "Keyword gap analysis failed: {Failure}, provider status {ProviderStatus}, error {ErrorType}/{ErrorCode}, {ElapsedMs} ms",
            failure,
            diagnostics.ProviderStatusCode,
            diagnostics.ProviderErrorType,
            diagnostics.ProviderErrorCode,
            diagnostics.ElapsedMilliseconds);

        return new KeywordGapOutcome(null, failure, retryAfterSeconds, diagnostics);
    }

    private static KeywordGapFailure MapFailure(HttpStatusCode status, ProviderError error)
    {
        var code = (int)status;

        if (code == 429)
        {
            return KeywordGapFailure.RateLimited;
        }

        if (code is 498 or 499 or >= 500)
        {
            return KeywordGapFailure.ProviderUnavailable;
        }

        // Groq answers 400 with this error code when the model's output broke the schema.
        // That is a bad answer rather than a bad request, so it is reported like any unusable output.
        if (code == 400 && error.Code == "json_validate_failed")
        {
            return KeywordGapFailure.BadModelOutput;
        }

        return KeywordGapFailure.ProviderRejected;
    }

    private sealed record ProviderError(string? Type, string? Code);

    // Reads only the short machine-readable type and code. The message and any generated text are never kept.
    private static ProviderError ReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("error", out var error) ||
                error.ValueKind != JsonValueKind.Object)
            {
                return new ProviderError(null, null);
            }

            return new ProviderError(ReadToken(error, "type"), ReadToken(error, "code"));
        }
        catch (JsonException)
        {
            return new ProviderError(null, null);
        }
    }

    private static string? ReadToken(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return text is not null && SafeToken.IsMatch(text) ? text : null;
    }

    private sealed record Completion(string? Content, TokenUsage? Usage);

    private static Completion ReadCompletion(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return new Completion(null, null);
            }

            var usage = ReadUsage(root);

            if (!root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                return new Completion(null, usage);
            }

            var first = choices[0];

            if (first.ValueKind != JsonValueKind.Object ||
                !first.TryGetProperty("finish_reason", out var finishReason) ||
                finishReason.ValueKind != JsonValueKind.String ||
                finishReason.GetString() != "stop" ||
                !first.TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.Object ||
                !message.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.String)
            {
                return new Completion(null, usage);
            }

            return new Completion(content.GetString(), usage);
        }
        catch (JsonException)
        {
            return new Completion(null, null);
        }
    }

    private static TokenUsage? ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new TokenUsage(
            ReadInt(usage, "prompt_tokens"),
            ReadInt(usage, "completion_tokens"),
            ReadInt(usage, "total_tokens"));
    }

    private static int? ReadInt(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetInt32(out var number)
            ? number
            : null;
    }

    private static int? ReadNumberHeader(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out var values))
        {
            return null;
        }

        return double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
               number >= 0 &&
               number < int.MaxValue
            ? (int)Math.Ceiling(number)
            : null;
    }
}
