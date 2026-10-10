using System.Text;
using JobTracker.Api.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobTracker.Api.Tests;

public class GroqWiringTests
{
    [Fact]
    public void Test_host_never_sees_real_configuration()
    {
        using var factory = new ApiFactory();

        var environment = factory.Services.GetRequiredService<IHostEnvironment>();
        var configuration = factory.Services.GetRequiredService<IConfiguration>();

        Assert.Equal("Testing", environment.EnvironmentName);
        Assert.Equal(ApiFactory.FakeConnectionString, configuration["ConnectionStrings:DefaultConnection"]);
        Assert.Equal(ApiFactory.FakeGroqKey, configuration["Ai:Groq:ApiKey"]);
    }

    [Theory]
    [InlineData("Ai:Groq:ApiKey", " ", "Ai:Groq:ApiKey is missing")]
    [InlineData("Ai:Groq:BaseUrl", "http://api.groq.com/openai/v1/", "Ai:Groq:BaseUrl must be an absolute https URL")]
    [InlineData("Ai:Groq:BaseUrl", "https://api.groq.com/openai/v1", "Ai:Groq:BaseUrl must end with a trailing slash")]
    [InlineData("Ai:Groq:Model", " ", "Ai:Groq:Model is missing")]
    [InlineData("Ai:Groq:ReasoningEffort", "extreme", "Ai:Groq:ReasoningEffort must be one of")]
    [InlineData("Ai:Groq:MaxCompletionTokens", "10", "Ai:Groq:MaxCompletionTokens must be between 256 and 4000")]
    [InlineData("Ai:Groq:TimeoutSeconds", "999", "Ai:Groq:TimeoutSeconds must be between 5 and 60")]
    public void Invalid_groq_setting_stops_the_host_from_starting(string key, string value, string expectedMessage)
    {
        var validation = StartupFailure.Capture(key, value);

        Assert.Contains(expectedMessage, validation.Message);
    }

    [Fact]
    public void Groq_client_is_configured_from_options()
    {
        using var factory = new ApiFactory();

        var options = factory.Services.GetRequiredService<IOptions<GroqOptions>>().Value;
        var client = factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient("Groq");

        Assert.Equal(new Uri(options.BaseUrl), client.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(options.TimeoutSeconds + 10), client.Timeout);
        Assert.Equal("Bearer", client.DefaultRequestHeaders.Authorization?.Scheme);
        Assert.Equal(ApiFactory.FakeGroqKey, client.DefaultRequestHeaders.Authorization?.Parameter);
    }

    [Fact]
    public async Task Groq_requests_carry_the_key_in_a_header_and_never_in_the_url()
    {
        using var factory = new ApiFactory();

        var options = factory.Services.GetRequiredService<IOptions<GroqOptions>>().Value;
        var client = factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient("Groq");

        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("chat/completions", content);

        var call = Assert.Single(factory.GroqStub.Calls);
        Assert.NotNull(call.Uri);
        Assert.Equal(new Uri(options.BaseUrl + "chat/completions"), call.Uri);
        Assert.Equal(string.Empty, call.Uri.Query);
        Assert.DoesNotContain(ApiFactory.FakeGroqKey, call.Uri.ToString());
        Assert.Equal("Bearer", call.AuthorizationScheme);
        Assert.Equal(ApiFactory.FakeGroqKey, call.AuthorizationParameter);
    }
}
