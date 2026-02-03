using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PostHog.Api;
using PostHog.Json;

/// <summary>
/// Extensions of <see cref="FakeHttpMessageHandler"/> specific to PostHog
/// </summary>
internal static class FakeHttpMessageHandlerExtensions
{
    static readonly Uri FlagsUrl = new("https://us.i.posthog.com/flags/?v=2");

    public static FakeHttpMessageHandler.RequestHandler AddCaptureResponse(this FakeHttpMessageHandler handler) =>
        handler.AddResponse(
            new Uri("https://us.i.posthog.com/capture"),
            HttpMethod.Post,
            responseBody: new { status = 1 });

    public static FakeHttpMessageHandler.RequestHandler AddBatchResponse(this FakeHttpMessageHandler handler) =>
        handler.AddResponse(
            new Uri("https://us.i.posthog.com/batch"),
            HttpMethod.Post,
            responseBody: new { status = 1 });

    public static FakeHttpMessageHandler.RequestHandler AddDecideResponseException(
        this FakeHttpMessageHandler handler,
        Exception exception)
        => handler.AddResponseException(FlagsUrl, HttpMethod.Post, exception);

    public static FakeHttpMessageHandler.RequestHandler AddDecideResponse(
        this FakeHttpMessageHandler handler,
        Func<Dictionary<string, object>, bool> decideRequestPredicate,
        string responseBody)
        => handler.AddDecideResponse(
            decideRequestPredicate,
            responseBody: Deserialize<DecideApiResult>(responseBody));

    public static FakeHttpMessageHandler.RequestHandler AddDecideResponse(
        this FakeHttpMessageHandler handler,
        string responseBody)
        => handler.AddDecideResponse(_ => true, responseBody);

    public static FakeHttpMessageHandler.RequestHandler AddDecideResponse(
        this FakeHttpMessageHandler handler,
        DecideApiResult responseBody)
        => handler.AddDecideResponse(
            _ => true,
            responseBody: responseBody);

    public static FakeHttpMessageHandler.RequestHandler AddDecideResponse(
        this FakeHttpMessageHandler handler,
        Func<Dictionary<string, object>, bool> decideRequestPredicate,
        DecideApiResult responseBody)
        => handler.AddResponse(
            FlagsUrl,
            HttpMethod.Post,
            decideRequestPredicate,
            responseBody);

    public static void AddRepeatedDecideResponse(this FakeHttpMessageHandler handler, int count, Func<int, string> responseBodyFunc)
        => handler.AddRepeatedResponses(
            count,
            FlagsUrl,
            HttpMethod.Post,
            responseBodyFunc: responseBodyFunc);

    public static void AddRepeatedDecideResponse(this FakeHttpMessageHandler handler, int count, string responseBody)
        => handler.AddRepeatedDecideResponse(count, _ => responseBody);

    static readonly Uri LocalEvaluationUrl = new("https://us.i.posthog.com/api/feature_flag/local_evaluation?token=fake-project-api-key&send_cohorts");

    public static FakeHttpMessageHandler.RequestHandler AddLocalEvaluationResponse(
        this FakeHttpMessageHandler handler,
        string responseBody)
        => handler.AddLocalEvaluationResponse(Deserialize<LocalEvaluationApiResult>(responseBody));

    public static FakeHttpMessageHandler.RequestHandler AddLocalEvaluationResponse(
        this FakeHttpMessageHandler handler,
        LocalEvaluationApiResult responseBody) =>
        handler.AddResponse(
            LocalEvaluationUrl,
            HttpMethod.Get,
            responseBody: responseBody);

    /// <summary>
    /// Adds a local evaluation response with an ETag header.
    /// </summary>
    public static FakeHttpMessageHandler.RequestHandler AddLocalEvaluationResponseWithETag(
        this FakeHttpMessageHandler handler,
        string responseBody,
        string etag)
    {
#pragma warning disable CA2000 // HttpResponseMessage is disposed by the handler
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
        };
#pragma warning restore CA2000
        response.Headers.ETag = new EntityTagHeaderValue(etag);

        return handler.AddResponse(LocalEvaluationUrl, HttpMethod.Get, response);
    }

    /// <summary>
    /// Adds a 304 Not Modified response for local evaluation.
    /// </summary>
    public static FakeHttpMessageHandler.RequestHandler AddLocalEvaluationNotModifiedResponse(
        this FakeHttpMessageHandler handler,
        string? etag = null)
    {
#pragma warning disable CA2000 // HttpResponseMessage is disposed by the handler
        var response = new HttpResponseMessage(HttpStatusCode.NotModified);
#pragma warning restore CA2000
        if (etag is not null)
        {
            response.Headers.ETag = new EntityTagHeaderValue(etag);
        }

        return handler.AddResponse(LocalEvaluationUrl, HttpMethod.Get, response);
    }

    /// <summary>
    /// Adds a quota_limited error response for local evaluation.
    /// </summary>
    public static FakeHttpMessageHandler.RequestHandler AddLocalEvaluationQuotaLimitedResponse(
        this FakeHttpMessageHandler handler)
    {
        const string quotaLimitedBody = """
            {
                "type": "quota_limited",
                "detail": "You have exceeded your feature flag request quota",
                "code": "payment_required"
            }
            """;
#pragma warning disable CA2000 // HttpResponseMessage is disposed by the handler
        var response = new HttpResponseMessage(HttpStatusCode.PaymentRequired)
        {
            Content = new StringContent(quotaLimitedBody, System.Text.Encoding.UTF8, "application/json")
        };
#pragma warning restore CA2000

        return handler.AddResponse(LocalEvaluationUrl, HttpMethod.Get, response);
    }

    public static FakeHttpMessageHandler.RequestHandler AddRemoteConfigResponse(
        this FakeHttpMessageHandler handler,
        string key,
        string responseBody) =>
        handler.AddResponse(
            new Uri($"https://us.i.posthog.com/api/projects/@current/feature_flags/{key}/remote_config?token=fake-project-api-key"),
            HttpMethod.Get,
            responseBody: responseBody);

    public static FakeHttpMessageHandler.RequestHandler AddDecryptedPayloadResponse(
        this FakeHttpMessageHandler handler,
        string key,
        string responseBody) =>
        handler.AddResponse(
            new Uri($"https://us.i.posthog.com/api/projects/@current/feature_flags/{key}/remote_config?token=fake-project-api-key"),
            HttpMethod.Get,
            responseBody: responseBody);

    static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonSerializerHelper.Options)
        ?? throw new ArgumentException("Json is invalid and deserializes to null", nameof(json));
}