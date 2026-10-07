using JobTracker.Api.Ai;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    private readonly IReadOnlyDictionary<string, string?>? _extraSettings;

    // useRealGroq leaves the Groq client untouched so calls go to the real API. Only the live
    // tests use it, and they pass the real key in through overrideKey and overrideValue.
    // extraSettings overrides any other configuration value, for example the limits.
    public ApiFactory(
        string? overrideKey = null,
        string? overrideValue = null,
        bool useRealGroq = false,
        IReadOnlyDictionary<string, string?>? extraSettings = null)
    {
        _overrideKey = overrideKey;
        _overrideValue = overrideValue;
        _useRealGroq = useRealGroq;
        _extraSettings = extraSettings;
    }

    public StubHttpMessageHandler GroqStub { get; } = new();

    public CapturingLoggerProvider Logs { get; } = new();

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

        if (_extraSettings is not null)
        {
            foreach (var pair in _extraSettings)
            {
                settings[pair.Key] = pair.Value;
            }
        }

        if (_overrideKey is not null)
        {
            settings[_overrideKey] = _overrideValue;
        }

        // Added last, so it wins over appsettings, environment variables and anything else.
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(Logs);
            logging.AddFilter((_, _, _) => true);
        });

        builder.ConfigureTestServices(services =>
        {
            if (!_useRealGroq)
            {
                services.AddHttpClient("Groq").ConfigurePrimaryHttpMessageHandler(() => GroqStub);
            }

            // Keep Data Protection keys out of the database.
            services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "jobtracker-tests-keys")));

            // Signed-in test requests use the test scheme instead of the session cookie.
            services.AddAuthentication(options =>
                {
                    options.DefaultScheme = TestAuthHandler.SchemeName;
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }
}
