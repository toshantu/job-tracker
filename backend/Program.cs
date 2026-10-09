using JobTracker.Api.Ai;
using JobTracker.Api.Auth;
using JobTracker.Api.Data;
using JobTracker.Api.Endpoints;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://*:{port}");
}

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddOptions<AppOptions>()
    .Bind(builder.Configuration)
    .Validate(o => !string.IsNullOrWhiteSpace(o.PublicOrigin), "PublicOrigin is missing")
    .Validate(o => Uri.TryCreate(o.PublicOrigin, UriKind.Absolute, out _), "PublicOrigin must be an absolute URL")
    .ValidateOnStart();

builder.Services.AddDataProtection().PersistKeysToDbContext<AppDbContext>();

builder.Services.AddOptions<GoogleAuthOptions>()
    .Bind(builder.Configuration.GetSection("Authentication:Google"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "Authentication:Google:ClientId is missing")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ClientSecret), "Authentication:Google:ClientSecret is missing")
    .ValidateOnStart();

builder.Services.AddOptions<GitHubAuthOptions>()
    .Bind(builder.Configuration.GetSection("Authentication:GitHub"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "Authentication:GitHub:ClientId is missing")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ClientSecret), "Authentication:GitHub:ClientSecret is missing")
    .ValidateOnStart();

builder.Services.AddOptions<AdminOptions>()
    .Bind(builder.Configuration.GetSection("Authorization"))
    .Validate(o => o.AdminUserIds.All(id => id > 0), "Authorization:AdminUserIds must contain only positive user ids")
    .ValidateOnStart();

builder.Services.AddOptions<GroqOptions>()
    .Bind(builder.Configuration.GetSection("Ai:Groq"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Ai:Groq:ApiKey is missing")
    .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out var baseUri) && baseUri.Scheme == Uri.UriSchemeHttps, "Ai:Groq:BaseUrl must be an absolute https URL")
    .Validate(o => o.BaseUrl.EndsWith("/"), "Ai:Groq:BaseUrl must end with a trailing slash")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Model), "Ai:Groq:Model is missing")
    .Validate(o => o.ReasoningEffort is "none" or "low" or "medium" or "high", "Ai:Groq:ReasoningEffort must be one of 'none', 'low', 'medium', or 'high'")
    .Validate(o => o.MaxCompletionTokens is >= 256 and <= 4000, "Ai:Groq:MaxCompletionTokens must be between 256 and 4000")
    .Validate(o => o.TimeoutSeconds is >= 5 and <= 60, "Ai:Groq:TimeoutSeconds must be between 5 and 60")
    .ValidateOnStart();

builder.Services.AddHttpClient("Google");

builder.Services.AddHttpClient("GitHub", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("JobTracker");
});

builder.Services.AddHttpClient("Groq", (serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<GroqOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds + 10);
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
});

builder.Services.AddKeywordGap(builder.Configuration);

builder.Services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(
    new ConfigurationManager<OpenIdConnectConfiguration>(
        "https://accounts.google.com/.well-known/openid-configuration",
        new OpenIdConnectConfigurationRetriever(),
        new HttpDocumentRetriever()));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
    options.Events = new CookieAuthenticationEvents
    {
        OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        },
        OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();
builder.Services.AddTransient<IClaimsTransformation, AdminClaimsTransformation>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapJobApplicationEndpoints();
app.MapApplicationDocumentEndpoints();
app.MapInterviewStageEndpoints();
app.MapAuthEndpoints();
app.MapAdminEndpoints();
app.MapKeywordGapEndpoints();

app.MapGet("/health/db", async (AppDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return canConnect ? Results.Ok(new { status = "connected" }) : Results.Problem("Cannot reach the database");
});

app.Run();