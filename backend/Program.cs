using JobTracker.Api.Auth;
using JobTracker.Api.Data;
using JobTracker.Api.Endpoints;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
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

builder.Services.AddHttpClient("Google");

builder.Services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(
    new ConfigurationManager<OpenIdConnectConfiguration>(
    "https://accounts.google.com/.well-known/openid-configuration",
    new OpenIdConnectConfigurationRetriever(),
    new HttpDocumentRetriever()));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
   options.Cookie.Name = "session";
   options.Cookie.HttpOnly= true;
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapJobApplicationEndpoints();
app.MapApplicationDocumentEndpoints();
app.MapInterviewStageEndpoints();
app.MapAuthEndpoints();

app.MapGet("/health/db", async (AppDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return canConnect ? Results.Ok(new { status = "connected" }) : Results.Problem("Cannot reach the database");
});

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
