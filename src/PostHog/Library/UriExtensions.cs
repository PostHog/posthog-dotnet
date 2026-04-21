namespace PostHog.Library;

internal static class UriExtensions
{
    static readonly Uri DefaultHostUrl = new("https://us.i.posthog.com");

    public static Uri NormalizeHostUrl(this Uri? value)
    {
        if (value is null)
        {
            return DefaultHostUrl;
        }

        var rawValue = value.OriginalString.Trim();
        if (!Uri.TryCreate(rawValue, UriKind.Absolute, out var normalized)
            || (normalized.Scheme != Uri.UriSchemeHttp && normalized.Scheme != Uri.UriSchemeHttps))
        {
            return DefaultHostUrl;
        }

        return normalized;
    }
}
