using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PostHog;
using PostHog.Emails;

namespace EmailsTests;

public class TheBatchAsyncMethod
{
    const string TemplateIdText = "0190f6d2-7c1e-7b3a-9f5e-1c2d3e4f5a6b";
    const string BatchUrl = "https://us.posthog.com/api/messaging/send/email_batch/";
    const string ResendWithTheSameKey = "Send it again with the same IdempotencyKey within 24 hours of the key's first use, so it goes out once.";
    const int FourMiB = 4 * 1024 * 1024;
    const string SendSinglyFix =
        "send each email the batch didn't queue with SendAsync and its recipient's IdempotencyKey.";

    const string M0 = "0190f6d2-0000-4000-8000-000000000000";
    const string M1 = "0190f6d2-0000-4000-8000-000000000001";
    const string M2 = "0190f6d2-0000-4000-8000-000000000002";

    static readonly Guid TemplateId = Guid.Parse(TemplateIdText);

    [Fact]
    public async Task PostsATemplateBatchAndReturnsAResultPerRecipientInOrder()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.OK, Ok(Queued(M0), Queued(M1, replayed: true), Refused(M2, "recipient_suppressed")));

        var result = await harness.Client.Emails.BatchAsync(new SendTemplateBatchOptions
        {
            TemplateId = TemplateId,
            TemplateData = new Dictionary<string, object> { ["name"] = "friend", ["plan"] = "pro" },
            Recipients =
            [
                new BatchRecipient { To = "a@example.com", IdempotencyKey = "welcome:a", DistinctId = "a" },
                new BatchRecipient { To = "b@example.com", TemplateData = new Dictionary<string, object> { ["name"] = "Bea" } },
                new BatchRecipient { To = "c@example.com", IdempotencyKey = "welcome:c" },
            ],
        });

        var request = Assert.Single(harness.Requests);
        Assert.Equal((HttpMethod.Post, BatchUrl), (request.Method, request.Url));
        Assert.Equal("Bearer phs_secret", request.Header("Authorization"));
        Assert.Null(request.Header("Idempotency-Key"));
        var generated = Keys(request)[1];
        Assert.True(Guid.TryParseExact(generated, "D", out var uuid));
        Assert.Equal('4', uuid.ToString("D")[14]);
        AssertSameFields(
            $$$"""{"template_id":"{{{TemplateIdText}}}","message_data":{"name":"friend","plan":"pro"},"recipients":[{"to":"a@example.com","idempotency_key":"welcome:a","distinct_id":"a"},{"to":"b@example.com","idempotency_key":"{{{generated}}}","message_data":{"name":"Bea"}},{"to":"c@example.com","idempotency_key":"welcome:c"}]}""",
            request.Body);
        Assert.Equal([0, 1, 2], result.Results.Select(r => r.Index));
        Assert.Equal(["a@example.com", "b@example.com", "c@example.com"], result.Results.Select(r => r.To));
        Assert.Equal(["welcome:a", generated, "welcome:c"], result.Results.Select(r => r.IdempotencyKey));
        Assert.Equal(
            [("queued", M0, false), ("queued", M1, true), ("failed", M2, false)],
            result.Results.Select(r => (r.Status, r.MessageId, r.Replayed)));
        Assert.Null(result.Results[0].Error);
        var refused = result.Results[2].Error!;
        Assert.Equal(
            ("recipient_suppressed", "validation_error", (HttpStatusCode?)(HttpStatusCode)422, M2, "Refused: recipient_suppressed."),
            (refused.Code, refused.Name, refused.StatusCode, refused.MessageId, refused.Message));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task SendsTemplateDataTheWayItsOwnConverterWritesIt()
    {
        await using var harness = new EmailHarness();
        AllQueued(harness);

        await harness.Client.Emails.BatchAsync(new SendTemplateBatchOptions
        {
            TemplateId = TemplateId,
            TemplateData = new PrefixedData { ["plan"] = "pro" },
            Recipients = [new BatchRecipient { To = "a@example.com", IdempotencyKey = "k", TemplateData = new PrefixedData { ["name"] = "Ada" } }],
        });

        AssertSameFields(
            $$$"""{"template_id":"{{{TemplateIdText}}}","message_data":{"x_plan":"pro"},"recipients":[{"to":"a@example.com","idempotency_key":"k","message_data":{"x_name":"Ada"}}]}""",
            Assert.Single(harness.Requests).Body);
    }

    [JsonConverter(typeof(PrefixedDataConverter))]
    public sealed class PrefixedData : Dictionary<string, object>;

    public sealed class PrefixedDataConverter : JsonConverter<PrefixedData>
    {
        public override PrefixedData Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, PrefixedData value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (key, item) in value)
            {
                writer.WriteString("x_" + key, (string)item);
            }

            writer.WriteEndObject();
        }
    }

    [Fact]
    public async Task SendsTheSameCodeContentToEveryRecipient()
    {
        await using var harness = new EmailHarness();
        AllQueued(harness);

        var result = await harness.Client.Emails.BatchAsync(new SendCodeBatchOptions
        {
            TemplateKey = "launch-news",
            Subject = "We launched",
            Html = "<p>Hi</p>",
            From = "Acme <news@example.com>",
            Tracking = false,
            Recipients = Recipients(2),
        });

        AssertSameFields(
            """{"template_key":"launch-news","subject":"We launched","html":"<p>Hi</p>","from":"Acme <news@example.com>","tracking":false,"recipients":[{"to":"user0@example.com","idempotency_key":"key-0"},{"to":"user1@example.com","idempotency_key":"key-1"}]}""",
            Assert.Single(harness.Requests).Body);
        Assert.Equal(["queued", "queued"], result.Results.Select(r => r.Status));
    }

    public static TheoryData<string, SendBatchOptions> InvalidBatches => new()
    {
        { "Recipients must hold at least one recipient", Batch([]) },
        { "Recipients holds 101 recipients, and a batch takes at most 100", Batch(Recipients(101)) },
        { "Recipients must hold at least one recipient", new SendTemplateBatchOptions { TemplateId = TemplateId, Recipients = null! } },
        { "Recipients[0] must not be null", Batch([null!]) },
        { "Recipients[1]: To", Batch([.. Recipients(1), new BatchRecipient { To = " " }]) },
        { "Recipients[0]: DistinctId", Batch([new BatchRecipient { To = "a@example.com", DistinctId = "" }]) },
        { "Recipients[0]: IdempotencyKey", Batch([new BatchRecipient { To = "a@example.com", IdempotencyKey = "has space" }]) },
        { "Recipients[2] has the same IdempotencyKey as Recipients[0]", Batch([.. Recipients(2), new BatchRecipient { To = "c@example.com", IdempotencyKey = "key-0" }]) },
        { "TemplateId", new SendTemplateBatchOptions { TemplateId = Guid.Empty, Recipients = Recipients(1) } },
        { "MaxRetries", Batch(Recipients(1), maxRetries: -1) },
        { "TemplateKey", new SendCodeBatchOptions { TemplateKey = "has space", Subject = "s", Html = "h", Recipients = Recipients(1) } },
        { "A batch with TemplateKey needs Html, Text or both", new SendCodeBatchOptions { TemplateKey = "k", Subject = "s", Recipients = Recipients(1) } },
        {
            "Recipients[0] has TemplateData, but a batch with TemplateKey sends the same content to every recipient",
            new SendCodeBatchOptions
            {
                TemplateKey = "k",
                Subject = "s",
                Html = "h",
                Recipients = Recipients(1, new Dictionary<string, object> { ["a"] = 1 }),
            }
        },
    };

    [Theory]
    [MemberData(nameof(InvalidBatches))]
    public async Task RefusesWhatABatchDoesNotTakeBeforeAnyRequest(string message, SendBatchOptions batch)
    {
        await using var harness = new EmailHarness();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => harness.Client.Emails.BatchAsync(batch));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
        Assert.Equal("batch", error.ParamName);
        Assert.Empty(harness.Requests);
    }

    [Fact]
    public async Task RefusesMoreThan100RecipientsBeforeReadingThemOrTheirTemplateData()
    {
        await using var harness = new EmailHarness();
        var batch = new SendTemplateBatchOptions
        {
            TemplateId = TemplateId,
            Recipients = new UnreadableRecipients(101),
            TemplateData = Cyclic(),
        };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => harness.Client.Emails.BatchAsync(batch));

        Assert.Contains("Recipients holds 101 recipients", error.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Requests);
    }

    [Fact]
    public async Task SendsTheRecipientsItCountedWhenTemplateDataAddsOne()
    {
        await using var harness = new EmailHarness();
        AllQueued(harness);
        var recipients = Recipients(100).ToList();
        var batch = new SendTemplateBatchOptions
        {
            TemplateId = TemplateId,
            Recipients = recipients,
            TemplateData = new AddsARecipientWhenRead(recipients),
        };

        var result = await harness.Client.Emails.BatchAsync(batch);

        Assert.Equal(100, Keys(Assert.Single(harness.Requests)).Length);
        Assert.Equal(100, result.Results.Count);
    }

    [Fact]
    public async Task SendsARecipientsTemplateDataAsDeepAsASingleSendTakes()
    {
        var templateData = Nested(62);
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, $$"""{"message_id": "{{M0}}", "replayed": false}""");
        await harness.Client.Emails.SendAsync(new SendTemplateEmailOptions
        {
            To = "a@example.com",
            TemplateId = TemplateId,
            TemplateData = templateData,
        });
        harness.Respond(HttpStatusCode.OK, Ok(Queued(M1)));

        var result = await harness.Client.Emails.BatchAsync(
            Batch([new BatchRecipient { To = "a@example.com", TemplateData = templateData }]));

        Assert.Equal(2, harness.Requests.Count);
        Assert.Equal("queued", Assert.Single(result.Results).Status);
    }

    [Fact]
    public async Task SendsUpTo100RecipientsInOneRequestWhateverItsSize()
    {
        await using var harness = new EmailHarness();
        AllQueued(harness);

        var result = await harness.Client.Emails.BatchAsync(
            Batch(Recipients(100, new Dictionary<string, object> { ["note"] = new string('x', FourMiB / 90) })));

        Assert.Equal(100, Keys(Assert.Single(harness.Requests)).Length);
        Assert.Equal(Enumerable.Range(0, 100), result.Results.Select(r => r.Index));
        Assert.All(result.Results, r => Assert.Equal("queued", r.Status));
    }

    [Theory]
    [InlineData(413, 2, ",\"fix\":\"Split the recipients into smaller batches.\"", "Split the recipients into smaller batches.")]
    [InlineData(413, 2, "", "Split the recipients into smaller batches and send each with BatchAsync.")]
    [InlineData(413, 1, "", "Make the email's content or TemplateData smaller, so the request fits PostHog's size limit.")]
    [InlineData(400, 2, "", null)]
    public async Task ReturnsATooLargeRefusalAsTheErrorWithPostHogsFix(int status, int recipients, string fixField, string? fix)
    {
        await using var harness = new EmailHarness();
        harness.Respond(
            (HttpStatusCode)status,
            $$"""{"type":"invalid_request","code":"content_too_large","detail":"Too large."{{fixField}}}""");

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(recipients))));

        Assert.Single(harness.Requests);
        Assert.Equal(
            ("content_too_large", (HttpStatusCode?)status, "Too large.", fix),
            (error.Code, error.StatusCode, error.Message, error.Fix));
        Assert.Equal(Enumerable.Repeat("failed", recipients), error.Results.Select(r => r.Status));
    }

    [Fact]
    public async Task ReturnsARefusalWithoutABodyAsTheErrorWithoutRetrying()
    {
        await using var harness = new EmailHarness();
        harness.RespondTo(_ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = null });

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(1), maxRetries: 2)));

        Assert.Single(harness.Requests);
        Assert.Equal(("api_error", (HttpStatusCode?)HttpStatusCode.Forbidden), (error.Code, error.StatusCode));
        Assert.Equal("failed", Assert.Single(error.Results).Status);
    }

    [Fact]
    public async Task WaitsLongerThanTheClientRequestTimeoutForTheBatchAnswer()
    {
        await using var harness = new EmailHarness(options => options.EmailRequestTimeout = TimeSpan.FromMilliseconds(50));
        var answer = harness.Hold(AllQueuedAnswer);

        var send = harness.Client.Emails.BatchAsync(Batch(Recipients(1), maxRetries: 0));
        await answer.Arrived;
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.False(send.IsCompleted);
        answer.Release();
        Assert.Equal("queued", Assert.Single((await send).Results).Status);
    }

    [Theory]
    [InlineData("its timeout fires")]
    [InlineData("the app cancels its pending requests")]
    [InlineData("a handler times out under an infinite timeout")]
    public async Task NamesTheAppsHttpClientTimeoutOnlyWhenItEndsAnAttempt(string how)
    {
        const string cancelled = "The app's HttpClient cancelled the request to PostHog before PostHog answered.";
        await using var harness = new EmailHarness();
        HeldAnswer? held = null;
        var expected = cancelled;
        switch (how)
        {
            case "its timeout fires":
                harness.HttpClient.Timeout = TimeSpan.FromMilliseconds(50);
                harness.Hang();
                if (Environment.Version.Major >= 5)
                {
                    expected = "The request to PostHog timed out after 0.05s, the Timeout of the app's HttpClient.";
                }
                break;
            case "the app cancels its pending requests":
                held = harness.Hold(AllQueuedAnswer);
                break;
            default:
                harness.HttpClient.Timeout = Timeout.InfiniteTimeSpan;
                harness.Fail(new TaskCanceledException("The handler timed out.", new TimeoutException()));
                break;
        }

        var send = harness.Client.Emails.BatchAsync(Batch(Recipients(1), maxRetries: 0));
        if (held is not null)
        {
            await held.Arrived;
            harness.HttpClient.CancelPendingRequests();
        }

        var error = await Assert.ThrowsAsync<EmailBatchException>(() => send);

        Assert.Equal(("network_error", expected), (error.Code, error.Message));
    }

    [Theory]
    [InlineData("network")]
    [InlineData("503")]
    public async Task TakesQueuedFromTheRetryButLeavesARefusedRecipientUnconfirmedAfterALostAnswer(string lost)
    {
        await using var harness = new EmailHarness();
        if (lost == "network")
        {
            harness.Fail(new HttpRequestException("connection reset"));
        }
        else
        {
            harness.Respond(HttpStatusCode.ServiceUnavailable, WholeCall("delivery_unavailable"));
        }

        harness.Respond(HttpStatusCode.OK, Ok(Queued(M0, replayed: true), Refused(M1, "recipient_suppressed")));

        var result = await harness.Client.Emails.BatchAsync(Batch(
        [
            new BatchRecipient { To = "a@example.com", IdempotencyKey = "key-0" },
            new BatchRecipient { To = "b@example.com" },
        ]));

        Assert.Equal(2, harness.Requests.Count);
        Assert.Equal(["key-0", result.Results[1].IdempotencyKey], Keys(harness.Requests[0]));
        Assert.Equal(Keys(harness.Requests[0]), Keys(harness.Requests[1]));
        var (first, second) = (result.Results[0], result.Results[1]);
        Assert.Equal(("queued", M0, true), (first.Status, first.MessageId, first.Replayed));
        Assert.Equal(
            ("unconfirmed", M1, "recipient_suppressed", "Send it again with the same IdempotencyKey within 24 hours of the key's first use, so it goes out once."),
            (second.Status, second.MessageId, second.Error!.Code, second.Error.Fix));
        Assert.Equal(
            "An earlier attempt may have queued this email, so it may still go out. The retry got: Refused: recipient_suppressed.",
            second.Error.Message);
        Assert.Equal(Keys(harness.Requests[1])[1], second.IdempotencyKey);
    }

    public static TheoryData<string, string, string?, string> Slots => new()
    {
        { Refused(M0, "sending_paused"), "failed", M0, "sending_paused" },
        { Refused(M0, "delivery_unavailable", status: 503), "unconfirmed", M0, "delivery_unavailable" },
        { Refused(M0, "idempotency_key_in_flight", status: 409), "unconfirmed", M0, "idempotency_key_in_flight" },
        { $$"""{"message_id":"{{M0}}"}""", "unconfirmed", M0, "api_error" },
        { """{"status":"queued"}""", "unconfirmed", null, "api_error" },
        { "7", "unconfirmed", null, "api_error" },
        { Queued("m0"), "unconfirmed", null, "api_error" },
    };

    [Theory]
    [MemberData(nameof(Slots))]
    public async Task ReadsEachRecipientsOutcomeFromItsSlot(string slot, string status, string? messageId, string code)
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.OK, Ok(slot));

        var result = Assert.Single((await harness.Client.Emails.BatchAsync(Batch(Recipients(1)))).Results);

        Assert.Equal((status, messageId, code), (result.Status, result.MessageId, result.Error!.Code));
        Assert.Equal(status == "unconfirmed" ? ResendWithTheSameKey : null, result.Error.Fix);
        Assert.Equal([BatchUrl], harness.Requests.Select(r => r.Url));
    }

    [Fact]
    public async Task ThrowsARequestRefusedAsAWholeWithEveryRecipientFailed()
    {
        await using var harness = new EmailHarness();
        harness.Respond((HttpStatusCode)429, WholeCall("quota_limited"));

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(3), maxRetries: 0)));

        Assert.Equal(("quota_limited", (HttpStatusCode?)(HttpStatusCode)429, "Nope."), (error.Code, error.StatusCode, error.Message));
        Assert.Equal("quota_limited", Assert.IsType<EmailException>(error.InnerException).Code);
        Assert.Single(harness.Requests);
        Assert.Equal(["failed", "failed", "failed"], error.Results.Select(r => r.Status));
        Assert.Equal(("quota_limited", "key-2"), (error.Results[2].Error!.Code, error.Results[2].IdempotencyKey));
    }

    [Fact]
    public async Task LeavesEachEmailOfALostAnswerUnconfirmedWithItsKey()
    {
        await using var harness = new EmailHarness();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            harness.Fail(new HttpRequestException("connection reset"));
        }

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(2))));

        Assert.Equal("network_error", error.Code);
        Assert.Equal([BatchUrl, BatchUrl, BatchUrl], harness.Requests.Select(r => r.Url));
        Assert.Equal(
            [("unconfirmed", "key-0", null), ("unconfirmed", "key-1", null)],
            error.Results.Select(r => (r.Status, r.IdempotencyKey, r.MessageId)));
        Assert.All(error.Results, r => Assert.Equal(("delivery_unavailable", ResendWithTheSameKey), (r.Error!.Code, r.Error.Fix)));
    }

    [Fact]
    public async Task LeavesAnEmailPostHogHoldsInFlightUnconfirmedAfterABatchThatSucceeds()
    {
        await using var harness = new EmailHarness();
        harness.Respond(
            HttpStatusCode.OK,
            Ok(Queued(M0), """{"error":{"code":"idempotency_key_in_flight","detail":"In flight.","status":409}}"""));

        var result = await harness.Client.Emails.BatchAsync(Batch(Recipients(2)));

        Assert.Equal([BatchUrl], harness.Requests.Select(r => r.Url));
        Assert.Equal([("queued", M0), ("unconfirmed", null)], result.Results.Select(r => (r.Status, r.MessageId)));
        Assert.Equal(("idempotency_key_in_flight", ResendWithTheSameKey), (result.Results[1].Error!.Code, result.Results[1].Error!.Fix));
    }

    [Theory]
    [InlineData("a disposed HttpClient")]
    [InlineData("a deadline too long to set")]
    public async Task FailsEveryRecipientWithoutRetryingWhenTheRequestCantStart(string how)
    {
        await using var harness = new EmailHarness(options =>
        {
            if (how == "a deadline too long to set")
            {
                options.EmailRequestTimeout = TimeSpan.FromDays(60);
            }
        });
        if (how == "a disposed HttpClient")
        {
            harness.HttpClient.Dispose();
        }

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(2))));

        Assert.Empty(harness.Requests);
        Assert.Equal("unexpected_error", error.Code);
        Assert.Equal(["failed", "failed"], error.Results.Select(r => r.Status));
    }

    [Fact]
    public async Task LeavesEveryRecipientUnconfirmedWhenTheTransportFailsAfterTheRequestLeft()
    {
        var posthog = new RecordingHandler();
        await using var harness = new EmailHarness(posthog: posthog);
        posthog.Enqueue(_ => throw new ObjectDisposedException("response stream"));

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(2), maxRetries: 0)));

        Assert.Single(posthog.Requests);
        Assert.Equal("network_error", error.Code);
        Assert.Equal(["unconfirmed", "unconfirmed"], error.Results.Select(r => r.Status));
    }

    [Theory]
    [InlineData(SocketError.ConnectionRefused)]
    [InlineData(SocketError.HostNotFound)]
    [InlineData(SocketError.ConnectionReset)]
    public async Task LeavesEveryRecipientUnconfirmedWhenAConnectionFailsAfterTheRequestLeft(SocketError firstAttempt)
    {
        var posthog = new RecordingHandler();
        await using var harness = new EmailHarness(posthog: posthog);
        posthog.Enqueue(_ => throw new HttpRequestException("first attempt", new SocketException((int)firstAttempt)));
        posthog.Enqueue(_ => throw new HttpRequestException("retry", new SocketException((int)SocketError.ConnectionRefused)));

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(2), maxRetries: 1)));

        Assert.Equal(2, posthog.Requests.Count);
        Assert.Equal("network_error", error.Code);
        Assert.Equal(["unconfirmed", "unconfirmed"], error.Results.Select(r => r.Status));
    }

    [Fact]
    public async Task SendsNothingMoreAfterTheLastRetryOfAnUnavailableRequest()
    {
        await using var harness = new EmailHarness();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            harness.Respond(HttpStatusCode.ServiceUnavailable, WholeCall("delivery_unavailable"));
        }

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(3), maxRetries: 2)));

        Assert.Equal(3, harness.Requests.Count);
        Assert.All(error.Results, r => Assert.Equal(("unconfirmed", ResendWithTheSameKey), (r.Status, r.Error!.Fix)));
    }

    [Fact]
    public async Task ReadsEachSlotWhoseOtherFieldNameIsUnreadable()
    {
        await using var harness = new EmailHarness();
        harness.Respond(
            HttpStatusCode.OK,
            Ok(
                $$"""{"message_id":"{{M0}}","status":"queued","err\ud800r":1,"replay\ud800ed":true}""",
                $$$"""{"message_id":"{{{M1}}}","error":{"code":"sending_paused","detail":"Paused.","stat\ud800us":422}}"""));

        var result = await harness.Client.Emails.BatchAsync(Batch(Recipients(2)));

        var (first, second) = (result.Results[0], result.Results[1]);
        Assert.Equal(("queued", M0, false), (first.Status, first.MessageId, first.Replayed));
        Assert.Equal(("failed", M1, "sending_paused", null), (second.Status, second.MessageId, second.Error!.Code, second.Error.StatusCode));
    }

    [Fact]
    public async Task ReadsTheResultsNextToAnUnreadableFieldName()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.OK, $$"""{"results":[{{Queued(M0)}}],"resu\ud800lts":1}""");

        var result = Assert.Single((await harness.Client.Emails.BatchAsync(Batch(Recipients(1)))).Results);

        Assert.Equal(("queued", M0), (result.Status, result.MessageId));
        Assert.Equal([BatchUrl], harness.Requests.Select(r => r.Url));
    }

    [Fact]
    public async Task LeavesARequestUnconfirmedWhenItsAnswerLacksAResultPerRecipient()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.OK, Ok(Queued(M0)));

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(2), maxRetries: 0)));

        Assert.Equal("api_error", error.Code);
        Assert.Equal(["unconfirmed", "unconfirmed"], error.Results.Select(r => r.Status));
        Assert.All(error.Results, r => Assert.Equal("api_error", r.Error!.Code));
        Assert.Equal([BatchUrl], harness.Requests.Select(r => r.Url));
    }

    [Theory]
    [InlineData(200, null, "unconfirmed")]
    [InlineData(429, 7, "failed")]
    public async Task KeepsTheStatusOfAnAnswerItCantDecode(int status, int? retryAfterSeconds, string outcome)
    {
        await using var harness = new EmailHarness();
        harness.RespondTo(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(Ok(Queued(M0)))),
            };
            response.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json; charset=not-a-charset");
            if (retryAfterSeconds is { } seconds)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            }

            return response;
        });

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(1), maxRetries: 0)));

        Assert.Equal(
            ("api_error", (HttpStatusCode?)(HttpStatusCode)status, retryAfterSeconds),
            (error.Code, error.StatusCode, (int?)error.RetryAfter?.TotalSeconds));
        Assert.Equal(("key-0", outcome), (Assert.Single(error.Results).IdempotencyKey, error.Results[0].Status));
    }

    [Fact]
    public async Task LeavesEachEmailOfARequestPostHogStillHoldsInFlightUnconfirmed()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Conflict, WholeCall("idempotency_key_in_flight"));
        harness.Respond(HttpStatusCode.Conflict, WholeCall("idempotency_key_in_flight"));

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(2), maxRetries: 1)));

        Assert.Equal([BatchUrl, BatchUrl], harness.Requests.Select(r => r.Url));
        Assert.All(
            error.Results,
            r => Assert.Equal(
                ("unconfirmed", (string?)null, "idempotency_key_in_flight", ResendWithTheSameKey),
                (r.Status, r.MessageId, r.Error!.Code, r.Error.Fix)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>Conflict</html>")]
    [InlineData("""{"detail":"Conflict."}""")]
    public async Task LeavesEachEmailOfARequestRefusedWithA409ItCantReadUnconfirmed(string body)
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Conflict, body);

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(2), maxRetries: 1)));

        Assert.Equal([BatchUrl], harness.Requests.Select(r => r.Url));
        Assert.All(
            error.Results,
            r => Assert.Equal(
                ("unconfirmed", (string?)null, "api_error", ResendWithTheSameKey),
                (r.Status, r.MessageId, r.Error!.Code, r.Error.Fix)));
    }

    [Theory]
    [InlineData("in flight", (HttpStatusCode)422, "sending_paused")]
    [InlineData("lost", (HttpStatusCode)429, "quota_limited")]
    [InlineData("lost", (HttpStatusCode)403, null)]
    [InlineData("lost", (HttpStatusCode)409, null)]
    public async Task LeavesEveryRecipientUnconfirmedWhenTheRetryIsRefusedAfterAnAttemptThatMayHaveQueued(
        string first,
        HttpStatusCode refusal,
        string? answeredCode)
    {
        var code = answeredCode ?? "api_error";
        await using var harness = new EmailHarness();
        if (first == "lost")
        {
            harness.Fail(new HttpRequestException("connection reset"));
        }
        else
        {
            harness.Respond(HttpStatusCode.Conflict, WholeCall("idempotency_key_in_flight"));
        }

        harness.Respond(refusal, answeredCode is null ? """{"detail":"Nope."}""" : WholeCall(answeredCode));

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(2), maxRetries: 1)));

        Assert.Equal(code, error.Code);
        Assert.All(error.Results, r =>
        {
            Assert.Equal(
                ("unconfirmed", code, "An earlier attempt may have queued this email, so it may still go out. The retry got: Nope."),
                (r.Status, r.Error!.Code, r.Error.Message));
            Assert.Contains("same IdempotencyKey", r.Error.Fix, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task SendsEachRecipientAsItWasWhenBatchAsyncWasCalled()
    {
        await using var harness = new EmailHarness();
        var data = new Dictionary<string, object> { ["step"] = "before" };
        harness.RespondTo(_ =>
        {
            data["step"] = "after";
            return Json(HttpStatusCode.ServiceUnavailable, WholeCall("delivery_unavailable"));
        });
        AllQueued(harness);

        await harness.Client.Emails.BatchAsync(Batch(Recipients(2, data)));

        Assert.Equal(2, harness.Requests.Count);
        Assert.All(harness.Requests, request =>
        {
            var recipients = JsonDocument.Parse(request.Body!).RootElement.GetProperty("recipients");
            Assert.All(
                recipients.EnumerateArray(),
                recipient => Assert.Equal(
                    [("step", "before")],
                    recipient.GetProperty("message_data").EnumerateObject().Select(p => (p.Name, p.Value.GetString()))));
        });
    }

    [Fact]
    public async Task ReadsEachTemplateDataOnlyOnce()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.ServiceUnavailable, WholeCall("delivery_unavailable"));
        AllQueued(harness);
        var batch = new SendTemplateBatchOptions
        {
            TemplateId = TemplateId,
            TemplateData = new Dictionary<string, object> { ["shared"] = new ReadableOnce("all") },
            Recipients = Enumerable.Range(0, 100)
                .Select(i => new BatchRecipient
                {
                    To = $"user{i}@example.com",
                    IdempotencyKey = $"key-{i}",
                    TemplateData = new Dictionary<string, object> { ["own"] = new ReadableOnce($"r{i}") },
                })
                .ToList(),
        };

        await harness.Client.Emails.BatchAsync(batch);

        var bodies = harness.Requests.Select(r => JsonDocument.Parse(r.Body!).RootElement).ToList();
        Assert.Equal(2, bodies.Count);
        Assert.All(bodies, body =>
        {
            Assert.Equal("all", body.GetProperty("message_data").GetProperty("shared").GetProperty("value").GetString());
            Assert.Equal(
                Enumerable.Range(0, 100).Select(i => $"r{i}"),
                body.GetProperty("recipients").EnumerateArray()
                    .Select(recipient => recipient.GetProperty("message_data").GetProperty("own").GetProperty("value").GetString()));
        });
    }

    sealed class ReadableOnce(string value)
    {
        int _reads;

        public string Value => Interlocked.Increment(ref _reads) == 1
            ? value
            : throw new InvalidOperationException("Read a second time.");
    }

    [Fact]
    public async Task DoesNotRetryBatchUnavailableAndSaysToSendSingly()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Conflict, WholeCall("batch_unavailable"));

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(2))));

        Assert.Single(harness.Requests);
        Assert.Equal("batch_unavailable", error.Code);
        Assert.Equal("Until batch sends are on for the project, " + SendSinglyFix, error.Fix);
        Assert.Equal(["failed", "failed"], error.Results.Select(r => r.Status));
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"type":"invalid_request","code":"not_found","detail":"Not found."}""")]
    public async Task SaysA404MeansNoTemplateOrNoBatchSends(string body)
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.NotFound, body);

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(1))));

        Assert.Equal(("not_found", "not_found"), (error.Code, error.Name));
        Assert.Contains("batch sends", error.Why, StringComparison.Ordinal);
        Assert.Equal("Check TemplateId, or " + SendSinglyFix, error.Fix);
    }

    [Theory]
    [InlineData("recipients__37__idempotency_key", "recipients__37__idempotency_key")]
    [InlineData("recipients__²__idempotency_key", "recipients__²__idempotency_key")]
    [InlineData("recipients__99999__idempotency_key", "recipients")]
    [InlineData("recipients__100__idempotency_key", "recipients")]
    [InlineData("recipients__2147483648__to", "recipients")]
    [InlineData("recipients__1\\u0000__to", "recipients__1\u0000__to")]
    [InlineData("subject", "subject")]
    public async Task NamesARefusedRecipientFieldByItsPositionInRecipients(string attr, string field)
    {
        await using var harness = new EmailHarness();
        harness.Respond(
            HttpStatusCode.BadRequest,
            $$"""{"type":"validation_error","code":"max_length","detail":"Too long.","attr":"{{attr}}"}""");

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(100))));

        Assert.Equal(field, error.Field);
        Assert.All(error.Results, r => Assert.Equal("failed", r.Status));
    }

    [Fact]
    public async Task ReturnsAndLogsEachWarningOnce()
    {
        await using var harness = new EmailHarness();
        const string warning = """{"code":"sandbox_sender","detail":"Sent from the test sender."}""";
        harness.RespondTo(request => Json(HttpStatusCode.OK, OkWithWarnings(warning + "," + warning, QueuedSlots(request))));

        var warnings = (await harness.Client.Emails.BatchAsync(Batch(Recipients(2)))).Warnings;

        Assert.Equal(["sandbox_sender"], warnings.Select(w => w.Code));
        Assert.Single(
            harness.Logs.GetAllEvents(minimumLevel: LogLevel.Warning),
            e => e.Message?.Contains("sandbox_sender", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task SendsNoRetryAfterTheClientShutsDown()
    {
        await using var harness = new EmailHarness();
        Task? shutdown = null;
        harness.RespondTo(_ =>
        {
            shutdown = harness.Client.DisposeAsync().AsTask();
            return Json((HttpStatusCode)429, WholeCall("throttled"));
        });

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(2))));
        await shutdown!;

        Assert.Equal("client_closed", error.Code);
        Assert.Single(harness.Requests);
        Assert.Equal(["failed", "failed"], error.Results.Select(r => r.Status));
        Assert.Equal("client_closed", error.Results[0].Error!.Code);
    }

    public static TheoryData<Action<PostHogOptions>, string> ClientsThatSendNothing => new()
    {
        { options => options.Disabled = true, "client_disabled" },
        { options => options.HostUrl = new Uri("https://ph.example.com"), "missing_app_host" },
    };

    [Theory]
    [MemberData(nameof(ClientsThatSendNothing))]
    public async Task SendsNothingFromAClientThatCantSend(Action<PostHogOptions> configure, string code)
    {
        await using var harness = new EmailHarness(configure);

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(1))));

        Assert.Equal(code, error.Code);
        Assert.Empty(harness.Requests);
    }

    static SendTemplateBatchOptions Batch(IReadOnlyList<BatchRecipient> recipients, int? maxRetries = null) => new()
    {
        TemplateId = TemplateId,
        Recipients = recipients,
        MaxRetries = maxRetries,
    };

    static List<BatchRecipient> Recipients(int count, IReadOnlyDictionary<string, object>? templateData = null)
        => Enumerable.Range(0, count)
            .Select(i => new BatchRecipient
            {
                To = $"user{i}@example.com",
                IdempotencyKey = $"key-{i}",
                TemplateData = templateData,
            })
            .ToList();

    static Dictionary<string, object> Nested(int objects)
    {
        var data = new Dictionary<string, object> { ["leaf"] = "x" };
        for (var level = 1; level < objects; level++)
        {
            data = new Dictionary<string, object> { ["next"] = data };
        }

        return data;
    }

    static string Queued(string messageId, bool replayed = false)
        => $$"""{"message_id":"{{messageId}}","status":"queued","replayed":{{(replayed ? "true" : "false")}}}""";

    static string Refused(string messageId, string code, int status = 422)
        => $$$"""{"message_id":"{{{messageId}}}","error":{"code":"{{{code}}}","detail":"Refused: {{{code}}}.","status":{{{status}}},"retryable":false}}""";

    static string Ok(params string[] slots) => OkWithWarnings(warnings: null, slots);

    static string OkWithWarnings(string? warnings, params string[] slots)
        => "{\"results\":[" + string.Join(",", slots) + "]" + (warnings is null ? "" : ",\"warnings\":[" + warnings + "]") + "}";

    sealed class AddsARecipientWhenRead(List<BatchRecipient> recipients) : IReadOnlyDictionary<string, object>
    {
        readonly Dictionary<string, object> _data = new() { ["a"] = 1 };

        public object this[string key] => _data[key];

        public IEnumerable<string> Keys => _data.Keys;

        public IEnumerable<object> Values => _data.Values;

        public int Count => _data.Count;

        public bool ContainsKey(string key) => _data.ContainsKey(key);

        public bool TryGetValue(string key, out object value) => _data.TryGetValue(key, out value!);

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            recipients.Add(new BatchRecipient { To = "late@example.com" });
            return _data.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    sealed class UnreadableRecipients(int count) : IReadOnlyList<BatchRecipient>
    {
        public int Count => count;

        public BatchRecipient this[int index] => throw new InvalidOperationException("The batch read a recipient.");

        public IEnumerator<BatchRecipient> GetEnumerator() => throw new InvalidOperationException("The batch read the recipients.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    static Dictionary<string, object> Cyclic()
    {
        var data = new Dictionary<string, object>();
        data["self"] = data;
        return data;
    }

    static string WholeCall(string code) => $$"""{"type":"invalid_request","code":"{{code}}","detail":"Nope."}""";


    static string[] Keys(RecordedRequest request)
    {
        using var body = JsonDocument.Parse(request.Body!);
        return body.RootElement.GetProperty("recipients").EnumerateArray()
            .Select(recipient => recipient.GetProperty("idempotency_key").GetString()!)
            .ToArray();
    }

    static string[] QueuedSlots(RecordedRequest request) => Keys(request).Select(key => Queued(MessageIdFor(key))).ToArray();

    static string MessageIdFor(string key)
    {
        var folded = new byte[16];
        var text = Encoding.UTF8.GetBytes(key);
        for (var i = 0; i < text.Length; i++)
        {
            folded[i % folded.Length] ^= text[i];
        }

        return new Guid(folded).ToString("D");
    }

    static HttpResponseMessage AllQueuedAnswer(RecordedRequest request) => Json(HttpStatusCode.OK, Ok(QueuedSlots(request)));

    static void AllQueued(EmailHarness harness, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            harness.RespondTo(AllQueuedAnswer);
        }
    }

    static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    static void AssertSameFields(string expected, string? actual)
    {
        using var want = JsonDocument.Parse(expected);
        using var got = JsonDocument.Parse(actual!);
        Assert.Equal(Fields(want.RootElement), Fields(got.RootElement));
    }

    static string Fields(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(
            ",",
            element.EnumerateObject()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => JsonSerializer.Serialize(property.Name) + ":" + Fields(property.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", element.EnumerateArray().Select(Fields)) + "]",
        JsonValueKind.String => JsonSerializer.Serialize(element.GetString()),
        _ => element.GetRawText(),
    };
}
