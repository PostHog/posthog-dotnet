using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.DependencyInjection;
#if NET8_0_OR_GREATER
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
#endif
using OpenAI;

namespace PostHog.AI;

/// <summary>
/// Extension methods for setting up PostHog AI.
/// </summary>
public static class PostHogAIExtensions
{
    /// <summary>
    /// Adds PostHog AI services to the specified <see cref="IServiceCollection" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection" /> to add services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddPostHogAI(this IServiceCollection services)
    {
        services.AddTransient<PostHogOpenAIHandler>();
        return services;
    }

    /// <summary>
    /// Adds the <see cref="PostHogOpenAIHandler"/> to the HTTP client builder.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
    /// <returns>The <see cref="IHttpClientBuilder"/>.</returns>
    public static IHttpClientBuilder AddPostHogOpenAIHandler(this IHttpClientBuilder builder)
    {
        return builder.AddHttpMessageHandler<PostHogOpenAIHandler>();
    }

    /// <summary>
    /// Adds an <see cref="OpenAIClient"/> that intercepts requests and sends events to PostHog.
    /// Note: This will override the <see cref="OpenAIClientOptions.Transport"/> property to use the PostHog handler.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <param name="apiKey">The OpenAI API key.</param>
    /// <param name="configureOptions">Optional action to configure <see cref="OpenAIClientOptions"/>.</param>
    /// <returns>The <see cref="IHttpClientBuilder"/> for the underlying HttpClient, allowing further customization (e.g. resilience).</returns>
    public static IHttpClientBuilder AddPostHogOpenAIClient(
        this IServiceCollection services,
        string apiKey,
        Action<OpenAIClientOptions>? configureOptions = null
    )
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key cannot be null or whitespace.", nameof(apiKey));
        }

        if (!services.Any(x => x.ServiceType == typeof(IPostHogClient)))
        {
            throw new InvalidOperationException(
                "PostHog services are not registered. Please call 'services.AddPostHog()' before calling 'AddPostHogOpenAIClient'."
            );
        }

        services.AddPostHogAI();

        var builder = services.AddHttpClient("PostHogOpenAIClient").AddPostHogOpenAIHandler();

        services.AddSingleton<OpenAIClient>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient("PostHogOpenAIClient");

            var options = new OpenAIClientOptions();
            configureOptions?.Invoke(options);

            if (options.Transport != null)
            {
                throw new InvalidOperationException(
                    "AddPostHogOpenAIClient cannot be used when a custom Transport is set in OpenAIClientOptions. "
                        + "To use a custom Transport with PostHog, manually configure the OpenAIClient and add the PostHogOpenAIHandler to your HttpClient."
                );
            }

            options.Transport = new HttpClientPipelineTransport(httpClient);

            return new OpenAIClient(new ApiKeyCredential(apiKey), options);
        });

        return builder;
    }

#if NET8_0_OR_GREATER
    /// <summary>
    /// Adds the <see cref="PostHogChatClient"/> to the <see cref="ChatClientBuilder"/> pipeline
    /// to capture <c>$ai_generation</c> events in PostHog.
    /// </summary>
    /// <param name="builder">The <see cref="ChatClientBuilder"/>.</param>
    /// <returns>The <see cref="ChatClientBuilder"/> so that additional calls can be chained.</returns>
    public static ChatClientBuilder UsePostHog(this ChatClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Use(
            (innerClient, services) =>
            {
                var postHogClient = services.GetService<IPostHogClient>();
                if (postHogClient is null)
                {
                    return innerClient;
                }

                var logger = services.GetRequiredService<ILogger<PostHogChatClient>>();
                return new PostHogChatClient(innerClient, postHogClient, logger);
            }
        );
    }
#endif
}
