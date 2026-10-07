using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using JobTracker.Api.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JobTracker.Api.Tests;

// Builds the analyzer around the stub handler. No web host, no database, no network.
internal sealed class AnalyzerHarness : IDisposable
{
    private readonly ServiceProvider _services;

    public AnalyzerHarness()
    {
        Settings = new GroqOptions
        {
            ApiKey = "test-key-not-real",
            BaseUrl = "https://api.groq.com/openai/v1/",
            Model = "test-model",
            ReasoningEffort = "low",
            MaxCompletionTokens = 1800,
            TimeoutSeconds = 20
        };

        var collection = new ServiceCollection();
        collection
            .AddHttpClient("Groq", client =>
            {
                client.BaseAddress = new Uri(Settings.BaseUrl);
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Settings.ApiKey);
            })
            .ConfigurePrimaryHttpMessageHandler(() => Stub);

        _services = collection.BuildServiceProvider();

        Analyzer = new KeywordGapAnalyzer(
            _services.GetRequiredService<IHttpClientFactory>(),
            Microsoft.Extensions.Options.Options.Create(Settings),
            Logger);
    }

    public GroqOptions Settings { get; }

    public StubHttpMessageHandler Stub { get; } = new();

    public CapturingLogger<KeywordGapAnalyzer> Logger { get; } = new();

    public KeywordGapAnalyzer Analyzer { get; }

    public void Dispose() => _services.Dispose();
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly object _gate = new();
    private readonly List<string> _messages = new();

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_gate)
            {
                return _messages.ToList();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var text = formatter(state, exception);

        if (exception is not null)
        {
            text += " | " + exception;
        }

        lock (_gate)
        {
            _messages.Add(text);
        }
    }
}

// Canned Groq responses. Provider text in errors is a marker so tests can prove it never leaks.
internal static class GroqResponses
{
    public static HttpResponseMessage Completion(string content, string finishReason = "stop")
    {
        var body = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { index = 0, message = new { role = "assistant", content }, finish_reason = finishReason }
            },
            usage = new { prompt_tokens = 1200, completion_tokens = 340, total_tokens = 1540 }
        });

        var response = Raw(HttpStatusCode.OK, body);
        response.Headers.TryAddWithoutValidation("x-ratelimit-remaining-tokens", "6460");
        return response;
    }

    public static HttpResponseMessage Raw(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Error(
        HttpStatusCode status,
        string providerText,
        string code = "some_error",
        int? retryAfterSeconds = null)
    {
        var body = JsonSerializer.Serialize(new
        {
            error = new { message = providerText, type = "invalid_request_error", code, failed_generation = providerText }
        });

        var response = Raw(status, body);

        if (retryAfterSeconds is { } seconds)
        {
            response.Headers.TryAddWithoutValidation("retry-after", seconds.ToString(CultureInfo.InvariantCulture));
        }

        return response;
    }
}
