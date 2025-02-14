namespace PostHog;

/// <summary>
/// This is here just so I can test examples in the docs in isolation.
/// </summary>
public static class Examples
{
    internal static readonly PostHogClient PostHog = new(new PostHogOptions {
        ProjectApiKey = "<ph_project_api_key>",
        HostUrl = new Uri("<ph_client_api_host>"),
        PersonalApiKey = Environment.GetEnvironmentVariable("PostHog__PersonalApiKey"),
    });

}