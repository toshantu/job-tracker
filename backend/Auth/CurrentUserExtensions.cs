using System.Security.Claims;

namespace JobTracker.Api.Auth;

public static class CurrentUserExtensions
{
    public static int GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return int.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("Authenticated principal has no valied user id claim.");
    }
} 