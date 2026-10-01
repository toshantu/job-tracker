using System.Text.Json;
using JobTracker.Api.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace JobTracker.Api.Endpoints;

public static class AuthEndpoints
{
    private const string GoogleCorrelationCookieName = "google_oauth_correlation";
    private const string GoogleDataProtectionPurpose = "GoogleOAuthCorrelation";

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
                ["prompt"]= "select_account",
            });

            return Results.Redirect(authorizeUrl);
        });       
    }
}
