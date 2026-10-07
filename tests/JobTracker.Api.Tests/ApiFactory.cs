using JobTracker.Api.Ai;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobTracker.Api.Tests;

// Program is internal (top-level statements), so a public type from the API
// assembly marks the entry assembly instead.
public sealed class ApiFactory : WebApplicationFactory<GroqOptions>
{
    public const string FakeConnectionString = "Host=127.0.0.1;Port=1;Database=tests;Username=tests;Password=tests";
    public const string FakeGroqKey = "test-key-not-real";

    private readonly string? _overrideKey;
    private readonly string? _overrideValue;
    private readonly bool _useRealGroq;

    // useRealGroq leaves the Groq client untouched so calls go to the real API. Only the live
    // tests use it, and they pass the real key in through overrideKey and overrideValue.
    public ApiFactory(string? overrideKey = null, string? overrideValue = null, bool useRealGroq = false)
    {
        _overrideKey = overrideKey;
        _overrideValue = overrideValue;
        _useRealGroq = useRealGroq;
    }

    public StubHttpMessageHandler GroqStub { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = FakeConnectionString,
            ["Authentication:Google:ClientId"] = "test-google-client-id",
            ["Authentication:Google:ClientSecret"] = "test-google-client-secret",
            ["Authentication:GitHub:ClientId"] = "test-github-client-id",
            ["Authentication:GitHub:ClientSecret"] = "test-github-client-secret",
            ["Ai:Groq:ApiKey"] = FakeGroqKey,
        };

        if (_overrideKey is not null)
        {
            settings[_overrideKey] = _overrideValue;
        }

        // Added last, so it wins over appsettings, environment variables and anything else.
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));

        builder.ConfigureTestServices(services =>
        {
            if (!_useRealGroq)
            {
                services.AddHttpClient("Groq").ConfigurePrimaryHttpMessageHandler(() => GroqStub);
            }

            // Keep Data Protection keys out of the database.
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "jobtracker-tests-keys")));
        });
    }
}
