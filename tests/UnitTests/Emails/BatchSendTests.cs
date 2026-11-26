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
    const string Unconfirmed = "PostHog didn't confirm this email, so it may still go out.";
    const string ResendWithTheSameKey = "Send it again with the same IdempotencyKey within 24 hours of the key's first use, so it goes out once.";
    const string ReadEachResult = "Read each recipient's result in Results, and send each unconfirmed one again with "
        + "the IdempotencyKey its result holds, within 24 hours of the key's first use, so each email goes out once.";
    const int FourMiB = 4 * 1024 * 1024;
    const string NoTemplateOrNoBatchSends =
        "PostHog found no template with this TemplateId, or batch sends aren't available for this project, so PostHog sent no email of this request.";
    const string NoBatchSendsFromCode =
        "Batch sends, or sending from code, aren't available for this project, so PostHog sent no email of this request.";
    const string SendEachOnItsOwn = "If this project can send email from code, " +
        "send each recipient on its own with SendAsync and its IdempotencyKey until batch sends are available.";
    const string CheckTheTemplateFix = "Check TemplateId. If it is right, " +
        "send each recipient on its own with SendAsync and its IdempotencyKey until batch sends are available.";
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
        Assert.Contains(
            "\"message_data\":" + string.Concat(Enumerable.Repeat("{\"next\":", 61)) + "{\"leaf\":\"x\"" + new string('}', 62),
            harness.Requests[^1].Body,
            StringComparison.Ordinal);
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
        Assert.Equal(Unconfirmed, second.Error.Message);
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
        if (how == "a disposed HttpClient" && Environment.Version.Major < 5)
        {
            Assert.Equal("network_error", error.Code);
            Assert.DoesNotContain("no email was sent", error.Message, StringComparison.Ordinal);
            Assert.All(error.Results, r => Assert.Equal(("unconfirmed", Unconfirmed), (r.Status, r.Error!.Message)));
        }
        else
        {
            Assert.Equal("unexpected_error", error.Code);
            Assert.Equal(["failed", "failed"], error.Results.Select(r => r.Status));
        }
    }

    [Fact]
    public async Task KeepsPostHogsOwnErrorWhenTheOnlyAttemptIsUnconfirmed()
    {
        await using var harness = new EmailHarness();
        harness.Respond(
            HttpStatusCode.ServiceUnavailable,
            """{"type":"server_error","code":"delivery_unavailable","detail":"Nope.","fix":"Send them again later with the same keys."}""");

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(2), maxRetries: 0)));

        Assert.Single(harness.Requests);
        Assert.Equal(["unconfirmed", "unconfirmed"], error.Results.Select(r => r.Status));
        Assert.Equal(("Nope.", "Send them again later with the same keys."), (error.Message, error.Fix));
    }

    [Theory]
    [InlineData("a template", HttpStatusCode.BadRequest, """{"type":"invalid_request","code":"content_too_large","detail":"Too large.","fix":"Make the template data smaller."}""")]
    [InlineData("a template", HttpStatusCode.NotFound, "")]
    [InlineData("code", HttpStatusCode.NotFound, "")]
    [InlineData("a template", HttpStatusCode.Conflict, """{"type":"invalid_request","code":"batch_unavailable","detail":"No email was sent."}""")]
    [InlineData("a template", HttpStatusCode.RequestEntityTooLarge, """{"type":"invalid_request","code":"content_too_large","detail":"Nothing was sent.","fix":"Split the recipients into smaller batches and send each with BatchAsync."}""")]
    [InlineData("a template", HttpStatusCode.RequestEntityTooLarge, "")]
    public async Task SaysAnEarlierAttemptMayHaveQueuedWhenPostHogRefusesTheRetry(string from, HttpStatusCode retry, string body)
    {
        var posthog = new RecordingHandler();
        await using var harness = new EmailHarness(posthog: posthog);
        posthog.Enqueue(_ => throw new HttpRequestException("lost", new SocketException((int)SocketError.ConnectionReset)));
        posthog.Enqueue(_ => Task.FromResult(new HttpResponseMessage(retry)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));
        SendBatchOptions batch = from == "code"
            ? new SendCodeBatchOptions { TemplateKey = "launch-news", Subject = "s", Html = "h", Recipients = Recipients(2), MaxRetries = 1 }
            : Batch(Recipients(2), maxRetries: 1);

        var error = await Assert.ThrowsAsync<EmailBatchException>(() => harness.Client.Emails.BatchAsync(batch));

        Assert.Equal(2, posthog.Requests.Count);
        Assert.Equal(
            [("unconfirmed", "key-0", Unconfirmed, ResendWithTheSameKey), ("unconfirmed", "key-1", Unconfirmed, ResendWithTheSameKey)],
            error.Results.Select(r => (r.Status, r.IdempotencyKey, r.Error!.Message, r.Error.Fix)));
        Assert.Contains("may still go out", error.Message, StringComparison.Ordinal);
        Assert.DoesNotMatch("(?i)sent no|no email|nothing|split|smaller|new key|another key", error.Message + " " + error.Fix);
        Assert.Equal(ReadEachResult, error.Fix);
        Assert.Equal(retry, Assert.IsType<EmailException>(error.InnerException!.InnerException).StatusCode);
    }

    [Theory]
    [InlineData("""{"type":"invalid_request","code":"conflict","detail":"Conflict."}""")]
    [InlineData("""{"type":"invalid_request","code":"api_error","detail":"Not sent."}""")]
    public async Task LeavesEachRecipientUnconfirmedAfterACoded409OtherThanBatchUnavailable(string body)
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Conflict, body);

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(2), maxRetries: 1)));

        Assert.Single(harness.Requests);
        Assert.Equal(
            [("unconfirmed", "key-0", Unconfirmed, ResendWithTheSameKey), ("unconfirmed", "key-1", Unconfirmed, ResendWithTheSameKey)],
            error.Results.Select(r => (r.Status, r.IdempotencyKey, r.Error!.Message, r.Error.Fix)));
    }

    [Theory]
    [InlineData("a slot PostHog left without a detail")]
    [InlineData("a slot whose detail says it wasn't sent")]
    [InlineData("a whole request still in flight")]
    [InlineData("a whole request PostHog can't deliver yet")]
    public async Task TellsAnUnconfirmedRecipientOnlyThatPostHogDidNotConfirmIt(string answer)
    {
        await using var harness = new EmailHarness();
        if (answer == "a slot PostHog left without a detail")
        {
            harness.Respond(HttpStatusCode.OK, Ok("""{"error":{"code":"delivery_unavailable","status":503}}"""));
        }
        else if (answer == "a slot whose detail says it wasn't sent")
        {
            harness.Respond(HttpStatusCode.OK, Ok("""{"error":{"code":"idempotency_key_in_flight","detail":"Not queued: failed.","status":409}}"""));
        }
        else
        {
            harness.Respond(
                answer == "a whole request still in flight" ? HttpStatusCode.Conflict : HttpStatusCode.UnprocessableEntity,
                WholeCall(answer == "a whole request still in flight" ? "idempotency_key_in_flight" : "delivery_unavailable"));
        }

        var send = harness.Client.Emails.BatchAsync(Batch(Recipients(1), maxRetries: 0));
        var result = answer.StartsWith("a slot", StringComparison.Ordinal)
            ? Assert.Single((await send).Results)
            : Assert.Single((await Assert.ThrowsAsync<EmailBatchException>(() => send)).Results);

        Assert.Equal(("unconfirmed", "key-0", Unconfirmed, ResendWithTheSameKey), (result.Status, result.IdempotencyKey, result.Error!.Message, result.Error.Fix));
    }

    [Theory]
    [InlineData("while PostHog holds the answer")]
    [InlineData("while it waits to retry a lost answer")]
    [InlineData("while it waits to retry a rate limit after a lost answer")]
    public async Task CarriesEveryRecipientsKeyWhenTheCallerCancelsAfterTheRequestLeft(string when)
    {
        await using var harness = new EmailHarness();
        using var cancellation = new CancellationTokenSource();
        Task requestOut;
        if (when == "while PostHog holds the answer")
        {
            requestOut = harness.Hold(AllQueuedAnswer).Arrived;
        }
        else if (when == "while it waits to retry a lost answer")
        {
            harness.Time.HoldDelays();
            harness.Fail(new HttpRequestException("connection reset"));
            requestOut = harness.Time.WaitForHeldDelayAsync();
        }
        else
        {
            harness.Fail(new HttpRequestException("connection reset"));
            harness.RespondTo(_ =>
            {
                harness.Time.HoldDelays();
                return Json((HttpStatusCode)429, WholeCall("rate_limited"));
            });
            requestOut = harness.Time.WaitForHeldDelayAsync();
        }

        var send = harness.Client.Emails.BatchAsync(Batch(UnkeyedRecipients(2)), cancellation.Token);
        await requestOut;
        await cancellation.CancelAsync();

        Assert.Same(send, await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(5))));
        var error = await Assert.ThrowsAsync<EmailBatchCanceledException>(() => send);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(
            Keys(harness.Requests[^1]).Select(key => ("unconfirmed", key, Unconfirmed)),
            error.Results.Select(r => (r.Status, r.IdempotencyKey, r.Error!.Message)));
    }

    [Theory]
    [InlineData("before the call")]
    [InlineData("while it waits to retry a rate limit")]
    public async Task KeepsAPlainCancelWhenNoRequestCouldHaveQueued(string when)
    {
        await using var harness = new EmailHarness();
        using var cancellation = new CancellationTokenSource();
        Task<SendBatchResult> send;
        if (when == "before the call")
        {
            await cancellation.CancelAsync();
            send = harness.Client.Emails.BatchAsync(Batch(Recipients(2)), cancellation.Token);
        }
        else
        {
            harness.Time.HoldDelays();
            harness.Respond((HttpStatusCode)429, WholeCall("rate_limited"));
            send = harness.Client.Emails.BatchAsync(Batch(Recipients(2)), cancellation.Token);
            await harness.Time.WaitForHeldDelayAsync();
            await cancellation.CancelAsync();
        }

        Assert.Same(send, await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(5))));
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        Assert.IsNotType<EmailBatchCanceledException>(error);
        Assert.Equal(when == "before the call" ? 0 : 1, harness.Requests.Count);
    }

    [Fact]
    public async Task SaysAnEarlierAttemptMayHaveQueuedWhenTheRetryCantStart()
    {
        var posthog = new RecordingHandler();
        await using var harness = new EmailHarness(posthog: posthog);
        posthog.Enqueue(_ =>
        {
            harness.HttpClient.Dispose();
            throw new HttpRequestException("lost", new SocketException((int)SocketError.ConnectionReset));
        });

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(2), maxRetries: 1)));

        Assert.Single(posthog.Requests);
        Assert.Equal(["unconfirmed", "unconfirmed"], error.Results.Select(r => r.Status));
        Assert.DoesNotContain("no email was sent", error.Message, StringComparison.Ordinal);
        if (Environment.Version.Major < 5)
        {
            Assert.Equal("network_error", error.Code);
            Assert.All(error.Results, r => Assert.Equal(Unconfirmed, r.Error!.Message));
        }
        else
        {
            Assert.Contains("may have queued", error.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("its handler")]
    [InlineData("its handler, before returning")]
    [InlineData("its handler, through a disposed HttpClient")]
    [InlineData("its handler, with an EmailException of its own")]
    [InlineData("an HttpClient of the app's own")]
    public async Task LeavesEveryRecipientUnconfirmedWhenTheTransportFailsAfterTheRequestLeft(string fails)
    {
        var posthog = new RecordingHandler();
        await using var harness = fails switch
        {
            "its handler" => new EmailHarness(posthog: posthog),
            "its handler, before returning" => new EmailHarness(posthog: new ThrowsAfterSendingHandler(posthog)),
            "its handler, through a disposed HttpClient" => new EmailHarness(posthog: new SendsThroughADisposedClientAfterSendingHandler(posthog)),
            "its handler, with an EmailException of its own" => new EmailHarness(posthog: new ThrowsAnEmailExceptionAfterSendingHandler(posthog)),
            _ => new EmailHarness(posthog: posthog, httpClient: transport => new ThrowsAfterSendingHttpClient(transport)),
        };
        if (fails == "its handler")
        {
            posthog.Enqueue(_ => throw new ObjectDisposedException("response stream"));
        }
        else
        {
            posthog.Enqueue(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        }

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

    sealed class ThrowsAfterSendingHandler(HttpMessageHandler posthog) : DelegatingHandler(posthog)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            base.SendAsync(request, cancellationToken).GetAwaiter().GetResult().Dispose();
            throw new InvalidOperationException("reply");
        }
    }

    sealed class ThrowsAnEmailExceptionAfterSendingHandler(HttpMessageHandler posthog) : DelegatingHandler(posthog)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            (await base.SendAsync(request, cancellationToken)).Dispose();
            throw new EmailException("The app's own handler failed.");
        }
    }

    sealed class SendsThroughADisposedClientAfterSendingHandler(HttpMessageHandler posthog) : DelegatingHandler(posthog)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            (await base.SendAsync(request, cancellationToken)).Dispose();
            var disposed = new HttpClient();
            disposed.Dispose();
            return await disposed.GetAsync(new Uri("https://example.com/"), cancellationToken);
        }
    }

    sealed class ThrowsAfterSendingHttpClient(HttpMessageHandler transport) : HttpClient(transport, disposeHandler: false)
    {
        public override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            base.SendAsync(request, cancellationToken).GetAwaiter().GetResult().Dispose();
            throw new ObjectDisposedException("response stream");
        }
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
    [InlineData(409, null, "unconfirmed")]
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
    [InlineData("""{"code":" ","detail":"Conflict."}""")]
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
                ("unconfirmed", code, Unconfirmed),
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
    [InlineData("a template", "")]
    [InlineData("a template", """{"type":"invalid_request","code":"not_found","detail":"Not found.","attr":null}""")]
    [InlineData("a template", """{"type":"invalid_request","code":"not_found","detail":"Endpoint not found."}""")]
    [InlineData("a template", """{"type":"invalid_request","code":"api_error","detail":"Nope."}""")]
    [InlineData("a template", """{"type":"invalid_request","code":"","detail":"Nope."}""")]
    [InlineData("a template", """{"type":"invalid_request","code":" ","detail":"Nope."}""")]
    [InlineData("code", """{"type":"invalid_request","code":"not_found","detail":"Not found.","attr":null}""")]
    [InlineData("code", """{"type":"invalid_request","code":"not_found","detail":"Endpoint not found."}""")]
    public async Task SaysA404WithoutAFieldMeansNoTemplateOrNoBatchSends(string from, string body)
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.NotFound, body);
        SendBatchOptions batch = from == "code"
            ? new SendCodeBatchOptions { TemplateKey = "launch-news", Subject = "s", Html = "h", Recipients = Recipients(2) }
            : Batch(Recipients(2));

        var error = await Assert.ThrowsAsync<EmailBatchException>(() => harness.Client.Emails.BatchAsync(batch));

        Assert.Single(harness.Requests);
        Assert.Equal(("not_found", (HttpStatusCode?)HttpStatusCode.NotFound), (error.Code, error.StatusCode));
        Assert.Equal(
            from == "code" ? (NoBatchSendsFromCode, SendEachOnItsOwn) : (NoTemplateOrNoBatchSends, CheckTheTemplateFix),
            (error.Message, error.Fix));
        Assert.Equal(["failed", "failed"], error.Results.Select(r => r.Status));
        Assert.All(
            new[] { error.Message, error.Why ?? "", error.Fix! },
            text => Assert.DoesNotMatch("(?i)update|upgrade|server|version", text));
    }

    [Fact]
    public async Task KeepsPostHogsOwnAnswerForA404ThatNamesAField()
    {
        await using var harness = new EmailHarness();
        harness.Respond(
            HttpStatusCode.NotFound,
            """{"type":"invalid_request","code":"not_found","detail":"No such sender.","attr":"from"}""");

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(1))));

        Assert.Equal(("not_found", "No such sender.", "from"), (error.Code, error.Message, error.Field));
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

    static List<BatchRecipient> UnkeyedRecipients(int count)
        => Enumerable.Range(0, count).Select(i => new BatchRecipient { To = $"user{i}@example.com" }).ToList();

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
