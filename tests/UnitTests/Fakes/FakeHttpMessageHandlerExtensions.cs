using System.Text.Json;
using PostHog.Api;
using PostHog.Json;

/// <summary>
/// Extensions of <see cref="FakeHttpMessageHandler"/> specific to PostHog
/// </summary>
internal static class FakeHttpMessageHandlerExtensions
{
    static readonly Uri DecideUrl = new("https://us.i.posthog.com/decide?v=3");

    public static FakeHttpMessageHandler.RequestHandler AddBatchResponse(this FakeHttpMessageHandler handler) =>
        handler.AddResponse(
            new Uri("https://us.i.posthog.com/batch"),
            HttpMethod.Post,
            responseBody: new { status = 1 });

    public static FakeHttpMessageHandler.RequestHandler AddDecideResponseException(
        this FakeHttpMessageHandler handler,
        Exception exception)
        => handler.AddResponseException(DecideUrl, HttpMethod.Post, exception);

    public static FakeHttpMessageHandler.RequestHandler AddDecideResponse(this FakeHttpMessageHandler handler, string responseBody)
        => handler.AddDecideResponse(Deserialize<DecideApiResult>(responseBody));

    public static FakeHttpMessageHandler.RequestHandler AddDecideResponse(this FakeHttpMessageHandler handler, DecideApiResult responseBody)
        => handler.AddResponse(
            DecideUrl,
            HttpMethod.Post,
            responseBody: responseBody);

    public static void AddRepeatedDecideResponse(this FakeHttpMessageHandler handler, int count, Func<int, DecideApiResult> responseBodyFunc)
        => handler.AddRepeatedResponses(
            count,
            DecideUrl,
            HttpMethod.Post,
            responseBodyFunc: responseBodyFunc);

    public static void AddRepeatedDecideResponse(this FakeHttpMessageHandler handler, int count, string responseBody)
        => handler.AddRepeatedDecideResponse(count, _ => Deserialize<DecideApiResult>(responseBody));

    public static void AddRepeatedDecideResponse(this FakeHttpMessageHandler handler, int count, DecideApiResult responseBody)
        => handler.AddRepeatedDecideResponse(count, _ => responseBody);

    public static FakeHttpMessageHandler.RequestHandler AddLocalEvaluationResponse(this FakeHttpMessageHandler handler, string responseBody)
        => handler.AddLocalEvaluationResponse(Deserialize<LocalEvaluationApiResult>(responseBody));

    public static FakeHttpMessageHandler.RequestHandler AddLocalEvaluationResponse(
        this FakeHttpMessageHandler handler,
        LocalEvaluationApiResult responseBody)
        => handler.AddLocalEvaluationResponse("fake-project-api-key", responseBody);

    public static FakeHttpMessageHandler.RequestHandler AddLocalEvaluationResponse(
        this FakeHttpMessageHandler handler,
        string projectApiKey,
        LocalEvaluationApiResult responseBody) =>
        handler.AddResponse(
            new Uri($"https://us.i.posthog.com/api/feature_flag/local_evaluation/?token={projectApiKey}&send_cohorts"),
            HttpMethod.Get,
            responseBody: responseBody);

    static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonSerializerHelper.Options)
        ?? throw new ArgumentException("Json is invalid and deserializes to null", nameof(json));
}