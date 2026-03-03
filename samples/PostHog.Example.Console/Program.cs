// PostHog .NET SDK Console Example
//
// This demonstrates basic PostHog SDK usage including:
// - Event capture
// - User identification
// - Feature flags (local and remote evaluation)
//
// Setup:
// 1. Copy .env.example to .env and fill in your PostHog credentials
// 2. Run: dotnet run

using System.Text.Json;
using dotenv.net;
using Microsoft.Extensions.Logging;
using PostHog;

// Load .env file if it exists
// Try multiple locations: current directory, and project directory (for dotnet run --project)
var envPaths = new[]
{
    ".env",
    Path.Combine(AppContext.BaseDirectory, ".env"),
    "samples/PostHog.Example.Console/.env"
};
DotEnv.Load(options: new DotEnvOptions(envFilePaths: envPaths, ignoreExceptions: true));

// Get configuration from environment variables
var projectApiKey = Environment.GetEnvironmentVariable("POSTHOG_PROJECT_API_KEY");
var personalApiKey = Environment.GetEnvironmentVariable("POSTHOG_PERSONAL_API_KEY");
var endpoint = Environment.GetEnvironmentVariable("POSTHOG_HOST") ?? "https://us.i.posthog.com";

// Check credentials
if (string.IsNullOrEmpty(projectApiKey))
{
    Console.WriteLine("❌ Missing POSTHOG_PROJECT_API_KEY!");
    Console.WriteLine("   Please set the environment variable or copy .env.example to .env");
    Console.WriteLine();
    Console.Write("Enter your PostHog project API key (starts with phc_): ");
    projectApiKey = Console.ReadLine()?.Trim();
}

if (string.IsNullOrEmpty(projectApiKey))
{
    Console.WriteLine("❌ Project API key is required. Exiting.");
    return 1;
}

// Validate HTTPS requirement (except for localhost development)
var endpointUri = new Uri(endpoint);
if (endpointUri.Scheme != "https" && !endpointUri.IsLoopback)
{
    Console.WriteLine($"❌ ERROR: PostHog endpoint must use HTTPS: {endpoint}");
    Console.WriteLine("   HTTP is only allowed for localhost development.");
    return 1;
}

Console.WriteLine("✅ PostHog credentials loaded successfully!");
Console.WriteLine($"   Project API Key: ✅ Configured");
Console.WriteLine($"   Personal API Key: {(string.IsNullOrEmpty(personalApiKey) ? "❌ Not set (local evaluation disabled)" : "✅ Configured")}");
Console.WriteLine($"   Endpoint: {endpoint}");
Console.WriteLine();

// Create PostHog options
var options = new PostHogOptions
{
    ProjectApiKey = projectApiKey,
    PersonalApiKey = personalApiKey,
    HostUrl = new Uri(endpoint),
    FlushAt = 1, // Flush immediately for demo purposes
    FlushInterval = TimeSpan.FromSeconds(1)
};

// Create a logger factory for visibility
// Use Debug level for PostHog to see ETag behavior (304 Not Modified responses)
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
    builder.AddFilter("PostHog", LogLevel.Debug);
});

// Create the PostHog client
await using var posthog = new PostHogClient(options, loggerFactory: loggerFactory);

// Display menu
while (true)
{
    ShowMenu(hasPersonalApiKey: !string.IsNullOrEmpty(personalApiKey));
    var choice = Prompt("\nEnter your choice (1-6): ");

    switch (choice)
    {
        case "1":
            await RunCaptureExamples(posthog);
            break;
        case "2":
            await RunIdentifyExamples(posthog);
            break;
        case "3":
            await RunFeatureFlagExamples(posthog);
            break;
        case "4":
            await RunLocalEvaluationExample(posthog, hasPersonalApiKey: !string.IsNullOrEmpty(personalApiKey));
            break;
        case "5":
            await RunAllExamples(posthog, hasPersonalApiKey: !string.IsNullOrEmpty(personalApiKey));
            break;
        case "6":
            Console.WriteLine("👋 Goodbye!");
            await posthog.FlushAsync();
            return 0;
        default:
            Console.WriteLine("❌ Invalid choice. Please select 1-6.");
            continue;
    }

    Console.WriteLine("\n" + new string('=', 60));
    Console.WriteLine("✅ Example completed!");
    Console.WriteLine(new string('=', 60));

    var again = Prompt("\nWould you like to run another example? (y/N): ");
    if (!again.Equals("y", StringComparison.OrdinalIgnoreCase) &&
        !again.Equals("yes", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("👋 Goodbye!");
        await posthog.FlushAsync();
        return 0;
    }
    Console.WriteLine();
}

static void ShowMenu(bool hasPersonalApiKey)
{
    Console.WriteLine("🦔 PostHog .NET SDK Demo - Choose an example to run:");
    Console.WriteLine();
    Console.WriteLine("1. Capture events");
    Console.WriteLine("2. Identify users");
    Console.WriteLine("3. Feature flags (remote evaluation)");
    Console.WriteLine($"4. Local evaluation with ETag polling{(hasPersonalApiKey ? "" : " (requires Personal API Key)")}");
    Console.WriteLine("5. Run all examples");
    Console.WriteLine("6. Exit");
}

static string Prompt(string message)
{
    Console.Write(message);
    return Console.ReadLine()?.Trim() ?? string.Empty;
}

static async Task RunCaptureExamples(PostHogClient posthog)
{
    Console.WriteLine("\n" + new string('=', 60));
    Console.WriteLine("CAPTURE EVENTS");
    Console.WriteLine(new string('=', 60));

    var distinctId = $"user_{Guid.NewGuid():N}";
    Console.WriteLine($"\nUsing distinct ID: {distinctId}");

    // Simple capture
    Console.WriteLine("\n📤 Capturing 'page_view' event…");
    posthog.Capture(
        distinctId: distinctId,
        eventName: "page_view",
        properties: new Dictionary<string, object>
        {
            ["page"] = "/home",
            ["referrer"] = "https://google.com"
        });
    Console.WriteLine("   ✓ Event queued");

    // Capture with more properties
    Console.WriteLine("\n📤 Capturing 'button_clicked' event with properties…");
    posthog.Capture(
        distinctId: distinctId,
        eventName: "button_clicked",
        properties: new Dictionary<string, object>
        {
            ["button_id"] = "signup_cta",
            ["button_text"] = "Get Started",
            ["page"] = "/pricing"
        });
    Console.WriteLine("   ✓ Event queued");

    // Flush to ensure events are sent
    Console.WriteLine("\n⏳ Flushing events…");
    await posthog.FlushAsync();
    Console.WriteLine("   ✓ Events sent to PostHog");
}

static async Task RunIdentifyExamples(PostHogClient posthog)
{
    Console.WriteLine("\n" + new string('=', 60));
    Console.WriteLine("IDENTIFY USERS");
    Console.WriteLine(new string('=', 60));

    var distinctId = $"user_{Guid.NewGuid():N}";
    Console.WriteLine($"\nUsing distinct ID: {distinctId}");

    // Identify with properties
    Console.WriteLine("\n👤 Identifying user with properties…");
    await posthog.IdentifyAsync(
        distinctId: distinctId,
        personPropertiesToSet: new Dictionary<string, object>
        {
            ["email"] = "test@example.com",
            ["name"] = "Test User",
            ["plan"] = "pro"
        },
        personPropertiesToSetOnce: new Dictionary<string, object>
        {
            ["signed_up_at"] = DateTimeOffset.UtcNow.ToString("o")
        },
        cancellationToken: CancellationToken.None);
    Console.WriteLine("   ✓ User identified");

    // Capture an event for this user
    Console.WriteLine("\n📤 Capturing 'subscription_upgraded' event for identified user…");
    posthog.Capture(
        distinctId: distinctId,
        eventName: "subscription_upgraded",
        properties: new Dictionary<string, object>
        {
            ["old_plan"] = "free",
            ["new_plan"] = "pro"
        });

    await posthog.FlushAsync();
    Console.WriteLine("   ✓ Events sent to PostHog");
}

static async Task RunFeatureFlagExamples(PostHogClient posthog)
{
    Console.WriteLine("\n" + new string('=', 60));
    Console.WriteLine("FEATURE FLAGS");
    Console.WriteLine(new string('=', 60));

    var distinctId = $"user_{Guid.NewGuid():N}";
    Console.WriteLine($"\nUsing distinct ID: {distinctId}");

    // Check a simple boolean flag
    Console.WriteLine("\n🚩 Checking feature flag 'new-dashboard'…");
    var isEnabled = await posthog.IsFeatureEnabledAsync(
        featureKey: "new-dashboard",
        distinctId: distinctId,
        options: null,
        cancellationToken: CancellationToken.None);
    Console.WriteLine($"   Result: {isEnabled}");

    // Get flag with payload
    Console.WriteLine("\n🚩 Getting feature flag with payload 'pricing-experiment'…");
    var flag = await posthog.GetFeatureFlagAsync(
        featureKey: "pricing-experiment",
        distinctId: distinctId,
        options: null,
        cancellationToken: CancellationToken.None);
    if (flag is not null)
    {
        Console.WriteLine($"   Key: {flag.Key}");
        Console.WriteLine($"   IsEnabled: {flag.IsEnabled}");
        Console.WriteLine($"   VariantKey: {flag.VariantKey}");
        if (flag.Payload is not null)
        {
            Console.WriteLine($"   Payload: {flag.Payload.RootElement.GetRawText()}");
        }
    }
    else
    {
        Console.WriteLine("   Flag not found or not enabled");
    }

    // Get all flags
    Console.WriteLine("\n🚩 Getting all feature flags…");
    var allFlags = await posthog.GetAllFeatureFlagsAsync(
        distinctId: distinctId,
        options: null,
        cancellationToken: CancellationToken.None);
    Console.WriteLine($"   Found {allFlags.Count} flags:");
    foreach (var (key, value) in allFlags.Take(5))
    {
        Console.WriteLine($"   - {key}: {(value.IsEnabled ? value.VariantKey ?? "true" : "false")}");
    }
    if (allFlags.Count > 5)
    {
        Console.WriteLine($"   … and {allFlags.Count - 5} more");
    }
}

static async Task RunLocalEvaluationExample(PostHogClient posthog, bool hasPersonalApiKey)
{
    Console.WriteLine("\n" + new string('=', 60));
    Console.WriteLine("LOCAL EVALUATION WITH ETAG POLLING");
    Console.WriteLine(new string('=', 60));

    if (!hasPersonalApiKey)
    {
        Console.WriteLine("\n⚠️  This example requires a Personal API Key to be set.");
        Console.WriteLine("   Set POSTHOG_PERSONAL_API_KEY in your .env file.");
        Console.WriteLine("   Personal API keys can be created at:");
        Console.WriteLine("   https://us.posthog.com/settings/user-api-keys");
        return;
    }

    Console.WriteLine("\nℹ️  Local evaluation allows feature flag decisions to be made");
    Console.WriteLine("   locally without network calls. The SDK polls the server for");
    Console.WriteLine("   flag definitions and uses ETags to minimize bandwidth when");
    Console.WriteLine("   flags haven't changed (HTTP 304 Not Modified).");
    Console.WriteLine();
    Console.WriteLine("🔄 This example will poll every 5 seconds. Press CTRL+C to stop.");
    Console.WriteLine();

    var distinctId = $"user_{Guid.NewGuid():N}";
    Console.WriteLine($"Using distinct ID: {distinctId}");

    // Set up cancellation on CTRL+C
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true; // Prevent immediate termination
        cts.Cancel();
        Console.WriteLine("\n\n🛑 Stopping…");
    };

    var pollCount = 0;
    var pollingInterval = TimeSpan.FromSeconds(5);

    try
    {
        while (!cts.Token.IsCancellationRequested)
        {
            pollCount++;
            var timestamp = DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

            Console.WriteLine($"\n[{timestamp}] 📥 Poll #{pollCount}…");

            // Load/reload flags - ETag will be used after first load
            await posthog.LoadFeatureFlagsAsync(cts.Token);

            // Get all flags
            var allFlags = await posthog.GetAllFeatureFlagsAsync(
                distinctId: distinctId,
                options: null,
                cancellationToken: cts.Token);

            Console.WriteLine($"   ✓ {allFlags.Count} flags loaded");

            // Show a sample flag evaluation
            if (allFlags.Count > 0)
            {
                var sampleFlag = allFlags.First();
                var startTime = DateTime.UtcNow;
                var isEnabled = await posthog.IsFeatureEnabledAsync(
                    featureKey: sampleFlag.Key,
                    distinctId: distinctId,
                    options: null,
                    cancellationToken: cts.Token);
                var elapsed = DateTime.UtcNow - startTime;
                Console.WriteLine($"   🚩 '{sampleFlag.Key}': {(isEnabled == true ? "enabled" : "disabled")} ({elapsed.TotalMilliseconds:F2}ms)");
            }

            // Wait for next poll
            Console.WriteLine($"   ⏳ Next poll in {pollingInterval.TotalSeconds:F0}s…");
            await Task.Delay(pollingInterval, cts.Token);
        }
    }
    catch (OperationCanceledException)
    {
        // Expected when CTRL+C is pressed
    }

    Console.WriteLine("   ✓ Polling stopped");
}

static async Task RunAllExamples(PostHogClient posthog, bool hasPersonalApiKey)
{
    Console.WriteLine("\n🔄 Running all examples…");

    await RunCaptureExamples(posthog);
    await RunIdentifyExamples(posthog);
    await RunFeatureFlagExamples(posthog);
    await RunLocalEvaluationExample(posthog, hasPersonalApiKey);
}
