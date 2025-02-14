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

    public static async Task Main()
    {
        await using var posthog = new PostHogClient(new PostHogOptions());
        posthog.Capture("distinct_id_of_the_user", "user_signed_up");
        posthog.Capture(
            "distinct_id",
            "event_name",
            personPropertiesToSet: new() { ["name"] = "Max Hedgehog" },
            personPropertiesToSetOnce: new() { ["initial_url"] = "/blog" }
        );

        await posthog.AliasAsync("current_distinct_id", "new_distinct_id");

        posthog.Capture(
            "user_distinct_id",
            "some_event",
            groups: [new Group("company", "company_id_in_your_db")]);

        await posthog.GroupIdentifyAsync(
            type: "company",
            key: "company_id_in_your_db",
            name: "Awesome Inc.",
            properties: new()
            {
                ["employees"] = 11
            }
        );

        if (await posthog.GetFeatureFlagAsync(
                "experiment-feature-flag-key",
                "user_distinct_id")
            is { VariantKey: "variant-name" })
        {
            // Do something
        }

        if (await posthog.IsFeatureEnabledAsync("flag-key", "distinct_id_of_your_user"))
        {
            // Feature is enabled
        }
        else
        {
            // Feature is disabled
        }

        var flag = await posthog.GetFeatureFlagAsync(
            "flag-key",
            "distinct_id_of_your_user"
        );

        // replace "variant-key" with the key of your variant
        if (flag is { VariantKey: "variant-key" })
        {
            // Do something differently for this user
            // Optional: fetch the payload
            var matchedPayload = flag.Payload;
        }

        if (await posthog.GetFeatureFlagAsync(
                "flag-key",
                "distinct_id_of_your_user")
           )
        {
            // Do something differently for this user
        }

        posthog.Capture(
            "distinct_id_of_your_user",
            "event_name",
            properties: new()
            {
                // replace feature-flag-key with your flag key.
                // Replace "variant-key" with the key of your variant
                ["$feature/feature-flag-key"] = "variant-key"
            }
        );

        posthog.Capture(
            "distinct_id_of_your_user",
            "event_name",
            properties: null,
            groups: null,
            sendFeatureFlags: true
        );

        var flags = await posthog.GetAllFeatureFlagsAsync(
            "distinct_id_of_your_user"
        );

        var personFlag = await posthog.GetFeatureFlagAsync(
            "flag-key",
            "distinct_id_of_the_user",
            personProperties: new() { ["plan"] = "premium" });

        var groupFlag = await posthog.GetFeatureFlagAsync(
            "flag-key",
            "distinct_id_of_the_user",
            options: new FeatureFlagOptions
            {
                PersonProperties = new() { ["property_name"] = "value" },
                Groups =
                [
                    new Group("your_group_type", "your_group_id")
                    {
                        ["group_property_name"] = "your group value"
                    },
                    new Group(
                        "another_group_type",
                        "another_group_id")
                    {
                        ["group_property_name"] = "another group value"
                    }
                ]
            });
        var bothFlag = await posthog.GetFeatureFlagAsync(
            "flag-key",
            "distinct_id_of_the_user",
            options: new FeatureFlagOptions
            {
                PersonProperties = new() { ["property_name"] = "value" },
                Groups =
                [
                    new Group("your_group_type", "your_group_id")
                    {
                        ["group_property_name"] = "your group value"
                    },
                    new Group(
                        "another_group_type",
                        "another_group_id")
                    {
                        ["group_property_name"] = "another group value"
                    }
                ]
            });
    }
}