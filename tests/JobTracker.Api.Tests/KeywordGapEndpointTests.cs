using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobTracker.Api.Tests;

public class KeywordGapEndpointTests
{
    private const string Path = "/ai/keyword-gap";
    private const string ProviderText = "PROVIDER-ERROR-TEXT-5C2D";
    private const string ModelMarker = "ModelOutputMarker5C2D";
    private const string JobDescriptionMarker = "JDMARKER-5C2D";
    private const string CvMarker = "CVMARKER-5C2D";

    private const string ValidModelOutput = """{"keywords":[{"keyword":"C#","importance":"required","inCv":true},{"keyword":"Kubernetes","importance":"required","inCv":false},{"keyword":"GraphQL","importance":"preferred","inCv":false}]}""";

    // Small caps and limits so the tests stay tiny. They are checked at startup like the real ones.
    private static readonly Dictionary<string, string?> SmallLimits = new()
    {
        ["Ai:KeywordGap:MaxJobDescriptionChars"] = "300",
        ["Ai:KeywordGap:MaxCvChars"] = "300",
        ["Ai:KeywordGap:MaxBodyBytes"] = "2048",
        ["Ai:KeywordGap:WindowMinutes"] = "10",
        ["Ai:KeywordGap:PerUserPermits"] = "2",
        ["Ai:KeywordGap:GlobalPermits"] = "3"
    };

    private static ApiFactory NewFactory()
    {
        var factory = new ApiFactory(extraSettings: SmallLimits);
        factory.GroqStub.Responder = _ => GroqResponses.Completion(ValidModelOutput);
        return factory;
    }

    private static string Body(
        string jobDescription = "Backend engineer who knows C# and Docker.",
        string cvText = "Engineer with C# and Docker experience.") =>
        JsonSerializer.Serialize(new { jobDescription, cvText });

    private static async Task<(HttpResponseMessage Response, JsonElement Json)> SendAsync(
        HttpClient client,
        int? userId,
        string json,
        string mediaType = "application/json")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(json, Encoding.UTF8, mediaType)
        };

        if (userId is { } id)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, id.ToString(CultureInfo.InvariantCulture));
        }

        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        if (response.Content.Headers.ContentType?.MediaType != "application/json" || text.Length == 0)
        {
            return (response, default);
        }

        using var document = JsonDocument.Parse(text);
        return (response, document.RootElement.Clone());
    }

    [Fact]
    public async Task Anonymous_callers_get_401_and_the_provider_is_never_called()
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        var (response, _) = await SendAsync(client, null, Body());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.GroqStub.Calls);
    }

    [Fact]
    public async Task A_signed_in_user_gets_a_report()
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        var (response, json) = await SendAsync(client, 1, Body());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());

        var keywords = json.GetProperty("keywords");
        Assert.Equal(3, keywords.GetArrayLength());
        Assert.Equal("C#", keywords[0].GetProperty("keyword").GetString());
        Assert.Equal("Required", keywords[0].GetProperty("importance").GetString());
        Assert.True(keywords[0].GetProperty("inCv").GetBoolean());

        var required = json.GetProperty("summary").GetProperty("required");
        Assert.Equal(2, required.GetProperty("total").GetInt32());
        Assert.Equal(1, required.GetProperty("matched").GetInt32());
        Assert.Equal(50, required.GetProperty("percent").GetInt32());

        Assert.Single(factory.GroqStub.Calls);
    }

    [Theory]
    [InlineData("""{"jobDescription":"","cvText":"Engineer"}""")]
    [InlineData("""{"jobDescription":"Engineer","cvText":"   "}""")]
    [InlineData("""{"jobDescription":"Engineer"}""")]
    [InlineData("""{}""")]
    [InlineData("""null""")]
    [InlineData("""not json""")]
    [InlineData("""[1,2,3]""")]
    public async Task Rejects_unusable_bodies_without_calling_the_provider(string json)
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        var (response, error) = await SendAsync(client, 1, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_input", error.GetProperty("code").GetString());
        Assert.Empty(factory.GroqStub.Calls);
    }

    [Fact]
    public async Task Rejects_a_body_that_is_not_declared_as_json()
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        var (response, error) = await SendAsync(client, 1, Body(), mediaType: "text/plain");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_input", error.GetProperty("code").GetString());
        Assert.Empty(factory.GroqStub.Calls);
    }

    [Theory]
    [InlineData("jobDescription")]
    [InlineData("cvText")]
    public async Task Rejects_text_over_the_cap_without_calling_the_provider(string field)
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        var tooLong = new string('x', 301);
        var json = field == "cvText" ? Body(cvText: tooLong) : Body(jobDescription: tooLong);

        var (response, error) = await SendAsync(client, 1, json);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("too_long", error.GetProperty("code").GetString());
        Assert.Equal(field, error.GetProperty("field").GetString());
        Assert.Equal(300, error.GetProperty("maxLength").GetInt32());
        Assert.Empty(factory.GroqStub.Calls);
    }

    [Fact]
    public async Task Rejects_an_oversize_body_without_calling_the_provider()
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        var (response, error) = await SendAsync(client, 1, Body(cvText: new string('x', 3000)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("too_large", error.GetProperty("code").GetString());
        Assert.Empty(factory.GroqStub.Calls);
    }

    [Fact]
    public async Task One_users_limit_does_not_block_another()
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, 1, Body())).Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, 1, Body())).Response.StatusCode);

        var (blocked, error) = await SendAsync(client, 1, Body());

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal("rate_limited", error.GetProperty("code").GetString());

        var retryAfter = error.GetProperty("retryAfterSeconds").GetInt32();
        Assert.True(retryAfter > 0);
        Assert.Equal(retryAfter.ToString(CultureInfo.InvariantCulture), Assert.Single(blocked.Headers.GetValues("Retry-After")));
        Assert.Equal(2, factory.GroqStub.Calls.Count);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, 2, Body())).Response.StatusCode);
        Assert.Equal(3, factory.GroqStub.Calls.Count);
    }

    [Fact]
    public async Task The_shared_limit_applies_to_everyone()
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, 1, Body())).Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, 1, Body())).Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, 2, Body())).Response.StatusCode);

        var (blocked, error) = await SendAsync(client, 3, Body());

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal("rate_limited", error.GetProperty("code").GetString());
        Assert.Equal(3, factory.GroqStub.Calls.Count);
    }

    [Fact]
    public async Task Rejected_requests_do_not_use_up_permits()
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var (rejected, _) = await SendAsync(client, 1, Body(cvText: new string('x', 301)));
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejected.StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, 1, Body())).Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, 1, Body())).Response.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendAsync(client, 1, Body())).Response.StatusCode);
        Assert.Equal(2, factory.GroqStub.Calls.Count);
    }

    [Theory]
    [InlineData("busy", 503, "ai_busy")]
    [InlineData("unavailable", 502, "ai_unavailable")]
    [InlineData("rejected", 502, "ai_unavailable")]
    [InlineData("bad-schema", 502, "ai_bad_output")]
    [InlineData("truncated", 502, "ai_bad_output")]
    [InlineData("timeout", 504, "ai_timeout")]
    public async Task Maps_provider_failures_to_safe_responses(string kind, int expectedStatus, string expectedCode)
    {
        using var factory = NewFactory();
        factory.GroqStub.Responder = _ => kind switch
        {
            "busy" => GroqResponses.Error(HttpStatusCode.TooManyRequests, ProviderText, retryAfterSeconds: 9),
            "unavailable" => GroqResponses.Error(HttpStatusCode.ServiceUnavailable, ProviderText),
            "rejected" => GroqResponses.Error(HttpStatusCode.Unauthorized, ProviderText),
            "bad-schema" => GroqResponses.Error(HttpStatusCode.BadRequest, ProviderText, code: "json_validate_failed"),
            "truncated" => GroqResponses.Completion(ValidModelOutput, finishReason: "length"),
            _ => throw new TaskCanceledException()
        };

        using var client = factory.CreateClient();

        var (response, error) = await SendAsync(client, 1, Body());

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Equal(expectedCode, error.GetProperty("code").GetString());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.DoesNotContain(ProviderText, error.GetRawText());

        if (kind == "busy")
        {
            Assert.Equal(9, error.GetProperty("retryAfterSeconds").GetInt32());
            Assert.Equal("9", Assert.Single(response.Headers.GetValues("Retry-After")));
        }
    }

    [Fact]
    public async Task Keeps_keys_texts_and_provider_output_out_of_logs()
    {
        using var factory = NewFactory();
        using var client = factory.CreateClient();

        var jobDescription = JobDescriptionMarker + " needs C# and Docker";
        var cvText = CvMarker + " knows C# and Docker";

        // A successful report whose keyword carries a marker.
        factory.GroqStub.Responder = _ => GroqResponses.Completion(
            $$"""{"keywords":[{"keyword":"{{ModelMarker}}","importance":"required","inCv":true}]}""");
        await SendAsync(client, 1, Body(jobDescription, cvText));

        // A provider failure whose error text carries a marker.
        factory.GroqStub.Responder = _ => GroqResponses.Error(HttpStatusCode.ServiceUnavailable, ProviderText);
        await SendAsync(client, 2, Body(jobDescription, cvText));

        // A malformed body that quotes both markers.
        await SendAsync(client, 3, "{\"jobDescription\":\"" + JobDescriptionMarker + "\",\"cvText\":" + CvMarker + "}");

        var logs = factory.Logs.Messages;

        Assert.Contains(logs, message => message.Contains("Keyword gap analysis succeeded", StringComparison.Ordinal));
        Assert.Contains(logs, message => message.Contains("Keyword gap analysis failed", StringComparison.Ordinal));

        var forbidden = new[] { ApiFactory.FakeGroqKey, ProviderText, ModelMarker, JobDescriptionMarker, CvMarker };

        foreach (var message in logs)
        {
            foreach (var text in forbidden)
            {
                Assert.DoesNotContain(text, message);
            }
        }
    }
}

public class KeywordGapLimitsConfigTests
{
    [Theory]
    [InlineData("Ai:KeywordGap:MaxCvChars", "10", "MaxCvChars must be between")]
    [InlineData("Ai:KeywordGap:MaxJobDescriptionChars", "99999", "MaxJobDescriptionChars must be between")]
    [InlineData("Ai:KeywordGap:MaxBodyBytes", "500", "MaxBodyBytes must be between")]
    [InlineData("Ai:KeywordGap:MaxBodyBytes", "1024", "MaxBodyBytes must be at least")]
    [InlineData("Ai:KeywordGap:WindowMinutes", "0", "WindowMinutes must be between")]
    [InlineData("Ai:KeywordGap:PerUserPermits", "0", "PerUserPermits must be between")]
    [InlineData("Ai:KeywordGap:GlobalPermits", "4", "GlobalPermits must be greater than PerUserPermits")]
    public void Invalid_limits_stop_the_host_from_starting(string key, string value, string expectedMessage)
    {
        using var factory = new ApiFactory(key, value);

        var exception = Record.Exception(() => { _ = factory.Services; });

        var validation = FindOptionsValidationException(exception);
        Assert.NotNull(validation);
        Assert.Contains(expectedMessage, validation.Message);
    }

    private static OptionsValidationException? FindOptionsValidationException(Exception? exception)
    {
        if (exception is null)
        {
            return null;
        }

        if (exception is OptionsValidationException validation)
        {
            return validation;
        }

        if (exception is AggregateException aggregate)
        {
            return aggregate.InnerExceptions
                .Select(FindOptionsValidationException)
                .FirstOrDefault(found => found is not null);
        }

        return FindOptionsValidationException(exception.InnerException);
    }
}
