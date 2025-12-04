using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using PostHog;
using PostHog.AI;
using PostHog.AI.OpenAI;
using UnitTests.Fakes;
using Xunit;

#pragma warning disable CA1707

namespace PostHog.UnitTests.AI;

public sealed class PostHogOpenAIHandlerTests : IDisposable
{
    private readonly IPostHogClient _postHogClient;
    private readonly FakeHttpMessageHandler _innerHandler;
    private readonly HttpClient _httpClient;

    public PostHogOpenAIHandlerTests()
    {
        _postHogClient = Substitute.For<IPostHogClient>();
        _innerHandler = new FakeHttpMessageHandler();
        var logger = Substitute.For<ILogger<PostHogOpenAIHandler>>();
        var options = Options.Create(new PostHogAIOptions());

        var handler = new PostHogOpenAIHandler(_postHogClient, logger, options)
        {
            InnerHandler = _innerHandler
        };

        _httpClient = new HttpClient(handler);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _innerHandler.Dispose();
        _postHogClient.Dispose();
    }

    [Fact]
    public async Task ShouldCaptureChatCompletion()
    {
        // Setup OpenAI response
        var openAIResponse = new
        {
            id = "chatcmpl-123",
            @object = "chat.completion",
            created = 1677652288,
            model = "gpt-3.5-turbo-0613",
            choices = new[]
            {
                new { index = 0, message = new { role = "assistant", content = "Hello there!" }, finish_reason = "stop" }
            },
            usage = new { prompt_tokens = 9, completion_tokens = 12, total_tokens = 21 }
        };

        _innerHandler.AddResponse(new Uri("https://api.openai.com/v1/chat/completions"), HttpMethod.Post, openAIResponse);

        // Make request
        var requestBody = new
        {
            model = "gpt-3.5-turbo",
            messages = new[] { new { role = "user", content = "Hello!" } }
        };

        var response = await _httpClient.PostAsync(
            new Uri("https://api.openai.com/v1/chat/completions"),
            new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"));

        Assert.True(response.IsSuccessStatusCode);

        // Verify PostHog capture
        _postHogClient.Received().Capture(
            Arg.Any<string>(), // distinctId (auto generated uuid)
            "$ai_generation",
            Arg.Is<Dictionary<string, object>>(props => 
                props["$ai_model"].ToString() == "gpt-3.5-turbo-0613" &&
                props["$ai_provider"].ToString() == "openai" &&
                props["$ai_input_tokens"].ToString() == "9" &&
                props["$ai_output_tokens"].ToString() == "12"
            ),
            Arg.Any<GroupCollection>(),
            false
        );
    }

    [Fact]
    public async Task ShouldExtractDistinctIdFromHeader()
    {
        // Setup OpenAI response
        _innerHandler.AddResponse(new Uri("https://api.openai.com/v1/embeddings"), HttpMethod.Post, new {});

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        request.Headers.Add("x-posthog-distinct-id", "my-custom-id");

        await _httpClient.SendAsync(request);

        _postHogClient.Received().Capture(
            "my-custom-id",
            "$ai_embedding",
            Arg.Any<Dictionary<string, object>>(),
            Arg.Any<GroupCollection>(),
            false
        );
    }
}
