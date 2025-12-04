using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using PostHog.AI.OpenAI;

namespace PostHog.AI;

public static class PostHogAIConstants
{
    public const string OpenAINamedClient = "PostHogOpenAI";
    public const string AzureOpenAINamedClient = "PostHogAzureOpenAI";
}

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
        services.AddHttpClient(PostHogAIConstants.OpenAINamedClient).AddHttpMessageHandler<PostHogOpenAIHandler>();

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
            .AddHttpClient(PostHogAIConstants.AzureOpenAINamedClient)
            .AddHttpMessageHandler<PostHogOpenAIHandler>();

        return services;
    }

    /// <summary>
    /// Registers a typed HTTP client for OpenAI with PostHog AI observability.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure PostHog AI options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPostHogOpenAIHttpClient(
        this IServiceCollection services,
        Action<PostHogAIOptions>? configureOptions = null
    )
    {
        services = services ?? throw new ArgumentNullException(nameof(services));

        // Register handler and options (shared with named client)
        AddPostHogOpenAI(services, configureOptions);

        // Register typed client
        services.AddHttpClient<PostHogOpenAIHttpClient>()
                .AddHttpMessageHandler<PostHogOpenAIHandler>();

        return services;
    }

    /// <summary>
    /// Registers a typed HTTP client for Azure OpenAI with PostHog AI observability.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure PostHog AI options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPostHogAzureOpenAIHttpClient(
        this IServiceCollection services,
        Action<PostHogAIOptions>? configureOptions = null
    )
    {
        services = services ?? throw new ArgumentNullException(nameof(services));

        // Register handler and options (shared with named client)
        AddPostHogAzureOpenAI(services, configureOptions);

        // Register typed client
        services.AddHttpClient<PostHogAzureOpenAIHttpClient>()
                .AddHttpMessageHandler<PostHogOpenAIHandler>();

        return services;
    }
}

public class PostHogOpenAIHttpClient
{
    public HttpClient HttpClient { get; }
    public PostHogOpenAIHttpClient(HttpClient httpClient) => HttpClient = httpClient;
}

public class PostHogAzureOpenAIHttpClient
{
    public HttpClient HttpClient { get; }
    public PostHogAzureOpenAIHttpClient(HttpClient httpClient) => HttpClient = httpClient;
}

/// <summary>
/// Extension methods for <see cref="IHttpClientFactory"/> to retrieve PostHog AI HTTP clients.
/// </summary>
public static class HttpClientFactoryExtensions
{
    /// <summary>
    /// Gets an HTTP client configured for OpenAI with PostHog AI observability.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <returns>An HTTP client with PostHog AI handler attached.</returns>
    public static HttpClient GetPostHogOpenAIHttpClient(this IHttpClientFactory httpClientFactory)
    {
#if NETSTANDARD2_1
        if (httpClientFactory == null)
            throw new ArgumentNullException(nameof(httpClientFactory));
#else
        ArgumentNullException.ThrowIfNull(httpClientFactory);
#endif
        return httpClientFactory.CreateClient(PostHogAIConstants.OpenAINamedClient);
    }

    /// <summary>
    /// Gets an HTTP client configured for Azure OpenAI with PostHog AI observability.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <returns>An HTTP client with PostHog AI handler attached.</returns>
    public static HttpClient GetPostHogAzureOpenAIHttpClient(this IHttpClientFactory httpClientFactory)
    {
#if NETSTANDARD2_1
        if (httpClientFactory == null)
            throw new ArgumentNullException(nameof(httpClientFactory));
#else
        ArgumentNullException.ThrowIfNull(httpClientFactory);
#endif
        return httpClientFactory.CreateClient(PostHogAIConstants.AzureOpenAINamedClient);
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
