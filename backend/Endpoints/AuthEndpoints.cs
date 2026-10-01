using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using JobTracker.Api.Auth;
using JobTracker.Api.Data;
using JobTracker.Api.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace JobTracker.Api.Endpoints;

public static class AuthEndpoints
{
    private const string GoogleCorrelationCookieName = "google_oauth_correlation";
    private const string GoogleDataProtectionPurpose = "GoogleOAuthCorrelation";

    private record GoogleTokenResponse(
        [property: JsonPropertyName("id_token")] string? IdToken
    );

    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/auth/google/login", async (
            HttpContext httpContext,
            IOptions<AppOptions> appOptions,
            IOptions<GoogleAuthOptions> googleOptions,
            IConfigurationManager<OpenIdConnectConfiguration> googleConfigManager,
            IDataProtectionProvider dataProtectionProvider) =>
        {
            var codeVerifier = PkceHelper.GenerateCodeVerifier();
            var codeChallenge = PkceHelper.ComputeCodeChallenge(codeVerifier);
            var state = PkceHelper.GenerateRandomToken();
            var nonce = PkceHelper.GenerateRandomToken();

            var correlationData = new OAuthCorrelationData(codeVerifier, state, nonce);
            var protector = dataProtectionProvider.CreateProtector(GoogleDataProtectionPurpose);
            var protectedPayload = protector.Protect(JsonSerializer.Serialize(correlationData));

            httpContext.Response.Cookies.Append(GoogleCorrelationCookieName, protectedPayload, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddMinutes(10),
                IsEssential = true
            });

            var googleConfig = await googleConfigManager.GetConfigurationAsync(CancellationToken.None);
            var redirectUri = $"{appOptions.Value.PublicOrigin}/api/auth/google/callback";

            var authorizeUrl = QueryHelpers.AddQueryString(googleConfig.AuthorizationEndpoint, new Dictionary<string, string?>
            {
                ["client_id"] = googleOptions.Value.ClientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = "openid email profile",
                ["state"] = state,
                ["nonce"] = nonce,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256",
                ["prompt"] = "select_account",
            });

            return Results.Redirect(authorizeUrl);
        });

        app.MapGet("/auth/google/callback", async (
            HttpContext httpContext,
            HttpRequest request,
            IOptions<AppOptions> appOptions,
            IOptions<GoogleAuthOptions> googleOptions,
            IConfigurationManager<OpenIdConnectConfiguration> googleConfigManager,
            IDataProtectionProvider dataProtectionProvider,
            IHttpClientFactory httpClientFactory,
            AppDbContext db) =>
        {
            // 1. Correlation cookie must exist and must unprotect cleanly. Single-use: delete immediately.
            if (!request.Cookies.TryGetValue(GoogleCorrelationCookieName, out var protectedCorrelation))
            {
                return Results.Problem("Missing login session. Start the flow again at /auth/google/login.", statusCode: StatusCodes.Status400BadRequest);
            }
            httpContext.Response.Cookies.Delete(GoogleCorrelationCookieName);

            OAuthCorrelationData correlation;
            try
            {
                var protector = dataProtectionProvider.CreateProtector(GoogleDataProtectionPurpose);
                var json = protector.Unprotect(protectedCorrelation);
                correlation = JsonSerializer.Deserialize<OAuthCorrelationData>(json)
                    ?? throw new InvalidOperationException("Empty correlation payload.");
            }
            catch
            {
                return Results.Problem("Login session could not be verified. Start the flow again.", statusCode: StatusCodes.Status400BadRequest);
            }

            // 2. state must match before anything else in the query string is trusted.
            var returnedState = request.Query["state"].ToString();
            if (string.IsNullOrEmpty(returnedState) || returnedState != correlation.State)
            {
                return Results.Problem("State mismatch.", statusCode: StatusCodes.Status400BadRequest);
            }

            // 3. Now safe to check whether Google reported an error (e.g. consent denied).
            if (request.Query.TryGetValue("error", out var errorValue))
            {
                return Results.Problem($"Google returned an error: {errorValue}", statusCode: StatusCodes.Status400BadRequest);
            }

            var code = request.Query["code"].ToString();
            if (string.IsNullOrEmpty(code))
            {
                return Results.Problem("Missing authorization code.", statusCode: StatusCodes.Status400BadRequest);
            }

            var googleConfig = await googleConfigManager.GetConfigurationAsync(CancellationToken.None);
            var redirectUri = $"{appOptions.Value.PublicOrigin}/api/auth/google/callback";

            // 4. Exchange the code for tokens, proving possession via the PKCE verifier.
            var httpClient = httpClientFactory.CreateClient("Google");
            var tokenResponse = await httpClient.PostAsync(googleConfig.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = googleOptions.Value.ClientId,
                ["client_secret"] = googleOptions.Value.ClientSecret,
                ["code_verifier"] = correlation.CodeVerifier,
            }));

            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorBody = await tokenResponse.Content.ReadAsStringAsync();
                return Results.Problem($"Token exchange failed: {(int)tokenResponse.StatusCode} {errorBody}", statusCode: StatusCodes.Status400BadRequest);
            }

            var tokenData = await tokenResponse.Content.ReadFromJsonAsync<GoogleTokenResponse>();
            if (string.IsNullOrEmpty(tokenData?.IdToken))
            {
                return Results.Problem("Token response did not include an id_token.", statusCode: StatusCodes.Status400BadRequest);
            }

            // 5. Validate the id_token: signature, issuer, audience, expiry — then nonce by hand.
            var tokenHandler = new JsonWebTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidIssuer = googleConfig.Issuer,
                ValidAudience = googleOptions.Value.ClientId,
                IssuerSigningKeys = googleConfig.SigningKeys,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
            };

            var validationResult = await tokenHandler.ValidateTokenAsync(tokenData.IdToken, validationParameters);
            if (!validationResult.IsValid)
            {
                return Results.Problem($"id_token validation failed: {validationResult.Exception?.Message}", statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!validationResult.Claims.TryGetValue("nonce", out var nonceClaim) || nonceClaim?.ToString() != correlation.Nonce)
            {
                return Results.Problem("Nonce mismatch.", statusCode: StatusCodes.Status401Unauthorized);
            }

            var providerSubject = validationResult.Claims["sub"]?.ToString();
            var email = validationResult.Claims.TryGetValue("email", out var emailClaim) ? emailClaim?.ToString() : null;
            var displayName = validationResult.Claims.TryGetValue("name", out var nameClaim) ? nameClaim?.ToString() : null;

            // 6. Upsert the User, keyed on (Provider, ProviderSubject) — never on email.
            var user = await db.Users.FirstOrDefaultAsync(u => u.Provider == AuthProvider.Google && u.ProviderSubject == providerSubject);
            if (user is null)
            {
                user = new User { Provider = AuthProvider.Google, ProviderSubject = providerSubject! };
                db.Users.Add(user);
            }
            user.Email = email;
            user.DisplayName = displayName;
            await db.SaveChangesAsync();

            // 7. Issue the session. Google's own tokens are discarded here — never stored.
            var claims = new List<Claim> { new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, user.Id.ToString()) };
            if (email is not null) claims.Add(new System.Security.Claims.Claim(ClaimTypes.Email, email));
            if (displayName is not null) claims.Add(new System.Security.Claims.Claim(ClaimTypes.Name, displayName));

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            return Results.Redirect(appOptions.Value.PublicOrigin);
        });

        app.MapGet("/auth/me", (ClaimsPrincipal user) =>
        {
            return Results.Ok(new
            {
                userId = user.FindFirstValue(ClaimTypes.NameIdentifier),
                email = user.FindFirstValue(ClaimTypes.Email),
                displayName = user.FindFirstValue(ClaimTypes.Name),
            });
        })
        .RequireAuthorization();

        app.MapPost("/auth/logout", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });
    }
}