using System.Net;
using System.Security.Claims;
using Data.Models.Client;

namespace ServerPulse;

internal static class NativeWebSupport
{
    public static Dictionary<string, string> Query(string uri)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var query = new Uri(uri).Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(query))
            return result;

        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            result[WebUtility.UrlDecode(pieces[0])] = pieces.Length == 2
                ? WebUtility.UrlDecode(pieces[1])
                : string.Empty;
        }

        return result;
    }

    public static bool HasPermission(ClaimsPrincipal? user, EFClient.Permission minimum)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return false;

        return user.FindAll(ClaimTypes.Role).Any(claim =>
            (int.TryParse(claim.Value, out var numeric) && numeric >= (int)minimum) ||
            (Enum.TryParse<EFClient.Permission>(claim.Value, true, out var permission) && permission >= minimum));
    }
}
