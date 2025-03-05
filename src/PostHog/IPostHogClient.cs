using System.Text.Json;
using PostHog.Api;
using PostHog.Features;
using PostHog.Json;

namespace PostHog;

/// <summary>
/// Interface for the PostHog client. This is the main interface for interacting with PostHog.
/// Use this to identify users and capture events.
/// </summary>
public interface IPostHogClient : IPostHogBaseClient, IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Retrieves the local evaluator for evaluating feature flags locally.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    internal Task<LocalEvaluator?> GetLocalEvaluatorAsync(CancellationToken cancellationToken);
}