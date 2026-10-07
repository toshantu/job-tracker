using JobTracker.Api.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace JobTracker.Api.Tests;

// Skipped unless JOBTRACKER_LIVE_GROQ=1, so a normal "dotnet test" can never spend tokens.
public sealed class LiveGroqFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "JOBTRACKER_LIVE_GROQ";

    public LiveGroqFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(EnvironmentVariable) != "1")
        {
            Skip = $"Calls the real Groq API and spends tokens. Set {EnvironmentVariable}=1 to run it.";
        }
    }
}

public class LiveGroqTests
{
    private readonly ITestOutputHelper _output;

    public LiveGroqTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // Two real calls with invented texts. Only numbers and categories are printed:
    // never the key, the texts or anything the model wrote.
    [LiveGroqFact]
    [Trait("Category", "LiveGroq")]
    public async Task Real_calls_with_synthetic_texts()
    {
        // The only real secret this test reads is the Groq key. The database stays fake.
        var secrets = new ConfigurationBuilder().AddUserSecrets(typeof(GroqOptions).Assembly).Build();
        var apiKey = secrets["Ai:Groq:ApiKey"];
        Assert.False(string.IsNullOrWhiteSpace(apiKey), "Ai:Groq:ApiKey is missing from User Secrets.");

        using var factory = new ApiFactory("Ai:Groq:ApiKey", apiKey, useRealGroq: true);
        var analyzer = ActivatorUtilities.CreateInstance<KeywordGapAnalyzer>(factory.Services);

        var first = await analyzer.AnalyzeAsync(SyntheticTexts.JobDescription, SyntheticTexts.Cv, CancellationToken.None);
        Report("normal", first);

        Assert.True(first.Succeeded, $"First call failed: {first.Failure}");
        var keywords = first.Result!.Keywords;
        Assert.InRange(keywords.Count, 6, 30);
        Assert.Contains(keywords, k => k.InCv);
        Assert.Contains(keywords, k => !k.InCv);

        // The free plan allows 8,000 tokens per minute, so wait before the second call.
        await Task.Delay(TimeSpan.FromSeconds(65));

        var second = await analyzer.AnalyzeAsync(SyntheticTexts.JobDescription, SyntheticTexts.CvWithHiddenInstruction, CancellationToken.None);
        Report("hidden-instruction", second);

        Assert.True(second.Succeeded, $"Second call failed: {second.Failure}");
        var missing = second.Result!.Keywords.Count(k => !k.InCv);
        Assert.True(missing >= 2, $"Only {missing} keywords were marked missing, so the hidden instruction may have worked.");
    }

    private void Report(string label, KeywordGapOutcome outcome)
    {
        var d = outcome.Diagnostics;

        _output.WriteLine($"[{label}] succeeded={outcome.Succeeded} failure={outcome.Failure} providerStatus={d.ProviderStatusCode} errorType={d.ProviderErrorType} errorCode={d.ProviderErrorCode} retryAfterSeconds={outcome.RetryAfterSeconds} elapsedMs={d.ElapsedMilliseconds}");
        _output.WriteLine($"[{label}] tokens prompt={d.Usage?.PromptTokens} completion={d.Usage?.CompletionTokens} total={d.Usage?.TotalTokens} remainingPerMinute={d.RemainingTokensPerMinute}");
        _output.WriteLine($"[{label}] removed emails={d.EmailsRemoved} phones={d.PhonesRemoved} links={d.LinksRemoved}");

        if (outcome.Result is { } result)
        {
            var summary = result.Summary;
            _output.WriteLine($"[{label}] keywords={result.Keywords.Count} required={summary.Required.Matched}/{summary.Required.Total} preferred={summary.Preferred.Matched}/{summary.Preferred.Total}");
        }
    }
}
