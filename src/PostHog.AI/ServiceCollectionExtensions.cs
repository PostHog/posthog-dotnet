using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PostHog.AI.OpenAI;

namespace PostHog.AI;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> to register PostHog AI observability.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers PostHog AI observability for the official OpenAI client.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure PostHog AI options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPostHogOpenAI(
        this IServiceCollection services,
        Action<PostHogAIOptions>? configureOptions = null
    )
    {
        services = services ?? throw new ArgumentNullException(nameof(services));

        // Register PostHogOpenAIHandler
        services.TryAddTransient<PostHogOpenAIHandler>();

        // Register options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Register a typed HTTP client that includes our handler
        // This will be used by the OpenAI client when configured
        services.AddHttpClient("PostHogOpenAI").AddHttpMessageHandler<PostHogOpenAIHandler>();

        return services;
    }

    /// <summary>
    /// Registers PostHog AI observability for Azure OpenAI client.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure PostHog AI options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPostHogAzureOpenAI(
        this IServiceCollection services,
        Action<PostHogAIOptions>? configureOptions = null
    )
    {
        services = services ?? throw new ArgumentNullException(nameof(services));

        // Register PostHogOpenAIHandler
        services.TryAddTransient<PostHogOpenAIHandler>();

        // Register options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Register a typed HTTP client that includes our handler
        // This will be used by the Azure OpenAI client when configured
        services
            .AddHttpClient("PostHogAzureOpenAI")
            .AddHttpMessageHandler<PostHogOpenAIHandler>();

        return services;
    }
}

/// <summary>
/// Options for configuring PostHog AI observability.
/// </summary>
public class PostHogAIOptions
{
    /// <summary>
    /// Gets or sets the default distinct ID to use for AI events when not specified per request.
    /// </summary>
    public string? DefaultDistinctId { get; set; }

    /// <summary>
    /// Gets or sets whether to enable privacy mode by default.
    /// When enabled, input and output content will not be sent to PostHog.
    /// </summary>
    public bool PrivacyMode { get; set; }

    /// <summary>
    /// Gets or sets additional properties to include with all AI events.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public Dictionary<string, object>? Properties { get; set; }

    /// <summary>
    /// Gets or sets group information to associate with AI events.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    public Dictionary<string, object>? Groups { get; set; }

    /// <summary>
    /// Gets or sets whether to capture events immediately (synchronous) instead of batching.
    /// Useful for serverless environments.
    /// </summary>
    public bool CaptureImmediate { get; set; }
}
