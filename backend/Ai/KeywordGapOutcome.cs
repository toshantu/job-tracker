using JobTracker.Api.Dtos;

namespace JobTracker.Api.Ai;

public enum KeywordGapFailure
{
    RateLimited,
    Timeout,
    ProviderUnavailable,
    ProviderRejected,
    BadModelOutput
}

public sealed record TokenUsage(int? PromptTokens, int? CompletionTokens, int? TotalTokens);

// Numbers and short machine-readable tokens only. Nothing here may ever hold provider text,
// model output or user text.
public sealed record KeywordGapDiagnostics(
    int? ProviderStatusCode,
    string? ProviderErrorType,
    string? ProviderErrorCode,
    TokenUsage? Usage,
    int? RemainingTokensPerMinute,
    long ElapsedMilliseconds,
    int EmailsRemoved,
    int PhonesRemoved,
    int LinksRemoved);

public sealed record KeywordGapOutcome(
    KeywordGapResponse? Result,
    KeywordGapFailure? Failure,
    int? RetryAfterSeconds,
    KeywordGapDiagnostics Diagnostics)
{
    public bool Succeeded => Result is not null;
}
