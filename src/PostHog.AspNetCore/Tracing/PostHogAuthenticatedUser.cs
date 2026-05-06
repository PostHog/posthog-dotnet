using System.Security.Claims;

namespace PostHog;

internal static class PostHogAuthenticatedUser
{
    public static string? GetDistinctId(ClaimsPrincipal? user)
    {
        try
        {
            var identity = user?.Identity;
            if (identity?.IsAuthenticated != true)
            {
                return null;
            }

            var userId = user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? identity.Name;
            return PostHogTracingHeaders.SanitizeValue(userId);
        }
#pragma warning disable CA1031 // Authenticated user extraction must not crash the host app.
        catch
#pragma warning restore CA1031
        {
            return null;
        }
    }
}
