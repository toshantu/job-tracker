using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace JobTracker.Api.Auth;

public class AdminClaimsTransformation : IClaimsTransformation
{
    private readonly AdminOptions _options;

    public AdminClaimsTransformation(IOptions<AdminOptions> options)
    {
        _options = options.Value;
    }

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // Runs on every authenticated request and may run more than once per request,
        // so it must be idempotent and must never mutate the incoming principal.
        if (principal.Identity?.IsAuthenticated != true || principal.IsInRole(AppRoles.Admin))
        {
            return Task.FromResult(principal);
        }

        var idValue = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(idValue, out var userId) || !_options.AdminUserIds.Contains(userId))
        {
            return Task.FromResult(principal);
        }

        var clone = principal.Clone();
        if (clone.Identity is ClaimsIdentity identity)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, AppRoles.Admin));
        }

        return Task.FromResult(clone);
    }
}