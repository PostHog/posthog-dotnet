using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PostHog;
using PostHog.Emails;

namespace EmailsTests;

public class TheBatchAsyncMethod
{
    const string TemplateIdText = "0190f6d2-7c1e-7b3a-9f5e-1c2d3e4f5a6b";
    const string BatchUrl = "https://us.posthog.com/api/messaging/send/email_batch/";
    const string StatusByKeyUrl = "https://us.posthog.com/api/messaging/emails/status/?idempotency_key=";
    const int FourMiB = 4 * 1024 * 1024;
    const string SendSinglyFix =
        "send each email the batch didn't queue with SendAsync and its recipient's IdempotencyKey.";

    static readonly Guid TemplateId = Guid.Parse(TemplateIdText);

    [Fact]
    public async Task PostsATemplateBatchAndReturnsAResultPerRecipientInOrder()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.OK, Ok(Queued("m0"), Queued("m1", replayed: true), Refused("m2", "recipient_suppressed")));

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
        Assert.Equal(
            $$$"""{"template_id":"{{{TemplateIdText}}}","message_data":{"name":"friend","plan":"pro"},"recipients":[{"to":"a@example.com","idempotency_key":"welcome:a","distinct_id":"a"},{"to":"b@example.com","idempotency_key":"{{{generated}}}","message_data":{"name":"Bea"}},{"to":"c@example.com","idempotency_key":"welcome:c"}]}""",
            request.Body);
        Assert.Equal([0, 1, 2], result.Results.Select(r => r.Index));
        Assert.Equal(["a@example.com", "b@example.com", "c@example.com"], result.Results.Select(r => r.To));
        Assert.Equal(["welcome:a", generated, "welcome:c"], result.Results.Select(r => r.IdempotencyKey));
        Assert.Equal(
            [("queued", "m0", false), ("queued", "m1", true), ("failed", "m2", false)],
            result.Results.Select(r => (r.Status, r.MessageId, r.Replayed)));
        Assert.Null(result.Results[0].Error);
        var refused = result.Results[2].Error!;
        Assert.Equal(
            ("recipient_suppressed", "validation_error", (HttpStatusCode?)(HttpStatusCode)422, "m2", "Refused: recipient_suppressed."),
            (refused.Code, refused.Name, refused.StatusCode, refused.MessageId, refused.Message));
        Assert.Empty(result.Warnings);
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

        Assert.Equal(
            """{"template_key":"launch-news","subject":"We launched","html":"\u003Cp\u003EHi\u003C/p\u003E","from":"Acme \u003Cnews@example.com\u003E","tracking":false,"recipients":[{"to":"user0@example.com","idempotency_key":"key-0"},{"to":"user1@example.com","idempotency_key":"key-1"}]}""",
            Assert.Single(harness.Requests).Body);
        Assert.Equal(["queued", "queued"], result.Results.Select(r => r.Status));
    }

    public static TheoryData<string, SendBatchOptions> InvalidBatches => new()
    {
        { "Recipients must hold at least one recipient", Batch([]) },
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
    public async Task SplitsBy100RecipientsAndSendsTheRequestsInOrder()
    {
        await using var harness = new EmailHarness();
        AllQueued(harness, times: 3);

        var result = await harness.Client.Emails.BatchAsync(Batch(Recipients(250)));

        Assert.Equal([100, 100, 50], harness.Requests.Select(r => Keys(r).Length));
        Assert.Equal(Enumerable.Range(0, 250), result.Results.Select(r => r.Index));
        Assert.Equal(Enumerable.Range(0, 250).Select(i => $"msg_key-{i}"), result.Results.Select(r => r.MessageId));
        Assert.All(result.Results, r => Assert.Equal("queued", r.Status));
    }

    [Fact]
    public async Task SplitsBy4MiBAndFailsOnlyARecipientTooLargeAlone()
    {
        await using var harness = new EmailHarness();
        AllQueued(harness, times: 2);
        var recipients = Recipients(5, new Dictionary<string, object> { ["note"] = new string('x', FourMiB / 3) });
        recipients[3] = new BatchRecipient
        {
            To = "user3@example.com",
            IdempotencyKey = "key-3",
            TemplateData = new Dictionary<string, object> { ["note"] = new string('y', FourMiB) },
        };

        var result = await harness.Client.Emails.BatchAsync(Batch(recipients));

        Assert.All(harness.Requests, r => Assert.InRange(Encoding.UTF8.GetByteCount(r.Body!), 0, FourMiB));
        Assert.Equal([["key-0", "key-1"], ["key-2", "key-4"]], harness.Requests.Select(Keys));
        Assert.Equal(["queued", "queued", "queued", "failed", "queued"], result.Results.Select(r => r.Status));
        var tooLarge = result.Results[3];
        Assert.Equal((null, "key-3"), (tooLarge.MessageId, tooLarge.IdempotencyKey));
        Assert.Equal(
            ("content_too_large", "validation_error", "recipients"),
            (tooLarge.Error!.Code, tooLarge.Error.Name, tooLarge.Error.Field));
    }

    [Theory]
    [InlineData(0, 0, new[] { 2, 1 })]
    [InlineData(1, 1, new[] { 1, 2 })]
    public async Task FillsARequestUpToExactly4MiB(int overByBytes, int firstBodyItemsShort, int[] perRequest)
    {
        await using var harness = new EmailHarness();
        AllQueued(harness, times: 2);
        var shell = Bytes($$"""{"template_id":"{{TemplateIdText}}","message_data":{},"recipients":[]}""");
        var item = Bytes("""{"to":"a@example.com","idempotency_key":"k1","message_data":{"n":""}}""");
        BatchRecipient Recipient(string key, int padding) => new()
        {
            To = "a@example.com",
            IdempotencyKey = key,
            TemplateData = new Dictionary<string, object> { ["n"] = new string('a', padding) },
        };

        await harness.Client.Emails.BatchAsync(Batch(
        [
            Recipient("k1", FourMiB - shell - item - 1 - item + overByBytes),
            Recipient("k2", 0),
            Recipient("k3", 0),
        ]));

        Assert.Equal(FourMiB - (firstBodyItemsShort * item), Bytes(harness.Requests[0].Body!));
        Assert.Equal(perRequest, harness.Requests.Select(r => Keys(r).Length));
    }

    [Fact]
    public async Task WaitsForASlowAnswerLongerThanTheClientTimeout()
    {
        await using var harness = new EmailHarness(options => options.EmailRequestTimeout = TimeSpan.FromMilliseconds(50));
        harness.RespondTo(AllQueuedAnswer, delay: TimeSpan.FromMilliseconds(300));

        var result = await harness.Client.Emails.BatchAsync(Batch(Recipients(1), maxRetries: 0));

        Assert.Equal("queued", Assert.Single(result.Results).Status);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("503")]
    public async Task KeepsARecipientUnconfirmedWhenARetryAfterALostAnswerRefusesIt(string lost)
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

        harness.Respond(HttpStatusCode.OK, Ok(Queued("m0", replayed: true), Refused("m1", "idempotency_key_reused")));

        var result = await harness.Client.Emails.BatchAsync(Batch(
        [
            new BatchRecipient { To = "a@example.com", IdempotencyKey = "key-0" },
            new BatchRecipient { To = "b@example.com" },
        ]));

        Assert.Equal(2, harness.Requests.Count);
        Assert.Equal(harness.Requests[0].Body, harness.Requests[1].Body);
        Assert.Single(harness.Time.Delays);
        var (first, second) = (result.Results[0], result.Results[1]);
        Assert.Equal(("queued", "m0", true), (first.Status, first.MessageId, first.Replayed));
        Assert.Equal(("unconfirmed", "m1"), (second.Status, second.MessageId));
        Assert.Equal(("delivery_unavailable", "service_unavailable"), (second.Error!.Code, second.Error.Name));
        Assert.Equal("idempotency_key_reused", second.LaterError!.Code);
        Assert.Equal(Keys(harness.Requests[1])[1], second.IdempotencyKey);
    }

    public static TheoryData<string, string, string?, string> Slots => new()
    {
        { Refused("m0", "sending_paused"), "failed", "m0", "sending_paused" },
        { Refused("m0", "delivery_unavailable", status: 503), "unconfirmed", "m0", "delivery_unavailable" },
        { Refused("m0", "idempotency_key_in_flight", status: 409), "unconfirmed", "m0", "idempotency_key_in_flight" },
        { """{"message_id":"m0"}""", "unconfirmed", "m0", "api_error" },
        { """{"status":"queued"}""", "unconfirmed", null, "api_error" },
        { "7", "unconfirmed", null, "api_error" },
    };

    [Theory]
    [MemberData(nameof(Slots))]
    public async Task ReadsEachRecipientsOutcomeFromItsSlot(string slot, string status, string? messageId, string code)
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.OK, Ok(slot));

        var result = Assert.Single((await harness.Client.Emails.BatchAsync(Batch(Recipients(1)))).Results);

        Assert.Equal((status, messageId, code), (result.Status, result.MessageId, result.Error!.Code));
        Assert.Null(result.LaterError);
        Assert.Single(harness.Requests);
    }

    [Fact]
    public async Task StopsAtTheFirstRequestThatFailsAsAWholeAndReturnsEveryRecipient()
    {
        await using var harness = new EmailHarness();
        AllQueued(harness);
        harness.Respond((HttpStatusCode)429, WholeCall("quota_limited"));

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(250))));

        Assert.Equal(("quota_limited", (HttpStatusCode?)(HttpStatusCode)429, "Nope."), (error.Code, error.StatusCode, error.Message));
        Assert.Equal("quota_limited", Assert.IsType<EmailException>(error.InnerException).Code);
        Assert.Equal(2, harness.Requests.Count);
        Assert.Equal(
            Enumerable.Repeat("queued", 100).Concat(Enumerable.Repeat("failed", 100)).Concat(Enumerable.Repeat("not_attempted", 50)),
            error.Results.Select(r => r.Status));
        Assert.Equal(("quota_limited", "Nope."), (error.Results[150].Error!.Code, error.Results[150].Error!.Message));
        Assert.Equal("not_attempted", error.Results[200].Error!.Code);
        Assert.Equal("key-249", error.Results[249].IdempotencyKey);
    }

    [Fact]
    public async Task ReadsEachEmailByItsKeyAfterALostAnswer()
    {
        await using var harness = new EmailHarness();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            harness.Fail(new HttpRequestException("connection reset"));
        }

        for (var read = 0; read < 2; read++)
        {
            harness.RespondTo(request => request.Url.EndsWith("key-0", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, StatusFound("m0"))
                : Json(HttpStatusCode.NotFound, WholeCall("not_found")));
        }

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(2))));

        Assert.Equal("network_error", error.Code);
        Assert.Equal(
            [StatusByKeyUrl + "key-0", StatusByKeyUrl + "key-1"],
            harness.Requests.Skip(3).Select(r => r.Url));
        Assert.All(harness.Requests.Skip(3), r => Assert.Equal(HttpMethod.Get, r.Method));
        Assert.Equal([("queued", "m0"), ("unconfirmed", null)], error.Results.Select(r => (r.Status, r.MessageId)));
        Assert.Equal("delivery_unavailable", error.Results[1].Error!.Code);
    }

    [Fact]
    public async Task StopsReadingByKeyWhenPostHogStaysUnreachable()
    {
        await using var harness = new EmailHarness();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            harness.Respond(HttpStatusCode.ServiceUnavailable, WholeCall("delivery_unavailable"));
        }

        harness.Fail(new HttpRequestException("down"));

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(3), maxRetries: 2)));

        Assert.Equal(4, harness.Requests.Count);
        Assert.All(error.Results, r => Assert.Equal("unconfirmed", r.Status));
    }

    [Fact]
    public async Task EncodesAKeyWithReservedCharactersInTheStatusRead()
    {
        await using var harness = new EmailHarness();
        harness.Fail(new HttpRequestException("connection reset"));
        harness.Respond(HttpStatusCode.NotFound, WholeCall("not_found"));

        await Assert.ThrowsAsync<EmailBatchException>(() => harness.Client.Emails.BatchAsync(Batch(
            [new BatchRecipient { To = "a@example.com", IdempotencyKey = "order#1&x=2" }],
            maxRetries: 0)));

        Assert.Equal(StatusByKeyUrl + "order%231%26x%3D2", harness.Requests[1].Url);
    }

    [Fact]
    public async Task LeavesARequestUnconfirmedWhenItsAnswerLacksAResultPerRecipient()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.OK, Ok(Queued("m0")));
        harness.Respond(HttpStatusCode.NotFound, WholeCall("not_found"));
        harness.Respond(HttpStatusCode.NotFound, WholeCall("not_found"));

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(2), maxRetries: 0)));

        Assert.Equal("api_error", error.Code);
        Assert.Equal(["unconfirmed", "unconfirmed"], error.Results.Select(r => r.Status));
        Assert.All(error.Results, r => Assert.Equal("api_error", r.LaterError!.Code));
        Assert.Equal([BatchUrl, StatusByKeyUrl + "key-0", StatusByKeyUrl + "key-1"], harness.Requests.Select(r => r.Url));
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
    [InlineData("recipients__37__idempotency_key", "recipients__137__idempotency_key")]
    [InlineData("recipients__²__idempotency_key", "recipients__²__idempotency_key")]
    [InlineData("recipients__99999__idempotency_key", "recipients")]
    [InlineData("recipients__50__idempotency_key", "recipients")]
    [InlineData("subject", "subject")]
    public async Task NamesARefusedRecipientFieldByItsPositionInRecipients(string attr, string field)
    {
        await using var harness = new EmailHarness();
        AllQueued(harness);
        harness.Respond(
            HttpStatusCode.BadRequest,
            $$"""{"type":"validation_error","code":"max_length","detail":"Too long.","attr":"{{attr}}"}""");

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(150))));

        Assert.Equal(field, error.Field);
        Assert.Equal(
            Enumerable.Repeat("queued", 100).Concat(Enumerable.Repeat("failed", 50)),
            error.Results.Select(r => r.Status));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReturnsAndLogsEachWarningOnce(bool laterRequestFails)
    {
        await using var harness = new EmailHarness();
        const string warning = """{"code":"sandbox_sender","detail":"Sent from the test sender."}""";
        harness.RespondTo(request => Json(HttpStatusCode.OK, OkWithWarnings(warning, QueuedSlots(request))));
        if (laterRequestFails)
        {
            harness.Respond((HttpStatusCode)429, WholeCall("quota_limited"));
        }
        else
        {
            harness.RespondTo(request => Json(HttpStatusCode.OK, OkWithWarnings(warning, QueuedSlots(request))));
        }

        IReadOnlyList<EmailWarning> warnings;
        if (laterRequestFails)
        {
            warnings = (await Assert.ThrowsAsync<EmailBatchException>(
                () => harness.Client.Emails.BatchAsync(Batch(Recipients(150))))).Warnings;
        }
        else
        {
            warnings = (await harness.Client.Emails.BatchAsync(Batch(Recipients(150)))).Warnings;
        }

        Assert.Equal(["sandbox_sender"], warnings.Select(w => w.Code));
        Assert.Single(
            harness.Logs.GetAllEvents(minimumLevel: LogLevel.Warning),
            e => e.Message?.Contains("sandbox_sender", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task StopsBeforeTheNextRequestWhenTheClientShutsDown()
    {
        await using var harness = new EmailHarness();
        Task? shutdown = null;
        harness.RespondTo(request =>
        {
            shutdown = harness.Client.DisposeAsync().AsTask();
            return AllQueuedAnswer(request);
        });
        AllQueued(harness);

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(150))));
        await shutdown!;

        Assert.Equal("client_closed", error.Code);
        Assert.Single(harness.Requests);
        Assert.Equal(
            Enumerable.Repeat("queued", 100).Concat(Enumerable.Repeat("not_attempted", 50)),
            error.Results.Select(r => r.Status));
        Assert.Equal("client_closed", error.Results[100].Error!.Code);
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
        AllQueued(harness);

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(150))));
        await shutdown!;

        Assert.Equal("client_closed", error.Code);
        Assert.Single(harness.Requests);
        Assert.Equal(
            Enumerable.Repeat("failed", 100).Concat(Enumerable.Repeat("not_attempted", 50)),
            error.Results.Select(r => r.Status));
        Assert.Equal("client_closed", error.Results[0].Error!.Code);
    }

    [Fact]
    public async Task ReadsNoEmailByItsKeyAfterTheClientShutsDown()
    {
        await using var harness = new EmailHarness();
        Task? shutdown = null;
        harness.RespondTo(_ =>
        {
            shutdown = harness.Client.DisposeAsync().AsTask();
            return Json(HttpStatusCode.ServiceUnavailable, WholeCall("delivery_unavailable"));
        });
        harness.Respond(HttpStatusCode.OK, StatusFound("m0"));

        var error = await Assert.ThrowsAsync<EmailBatchException>(
            () => harness.Client.Emails.BatchAsync(Batch(Recipients(2), maxRetries: 0)));
        await shutdown!;

        Assert.Equal("delivery_unavailable", error.Code);
        Assert.Single(harness.Requests);
        Assert.Equal(["unconfirmed", "unconfirmed"], error.Results.Select(r => r.Status));
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

    static string Queued(string messageId, bool replayed = false)
        => $$"""{"message_id":"{{messageId}}","status":"queued","replayed":{{(replayed ? "true" : "false")}}}""";

    static string Refused(string messageId, string code, int status = 422)
        => $$$"""{"message_id":"{{{messageId}}}","error":{"code":"{{{code}}}","detail":"Refused: {{{code}}}.","status":{{{status}}},"retryable":false}}""";

    static string Ok(params string[] slots) => OkWithWarnings(warnings: null, slots);

    static string OkWithWarnings(string? warnings, params string[] slots)
        => "{\"results\":[" + string.Join(",", slots) + "]" + (warnings is null ? "" : ",\"warnings\":[" + warnings + "]") + "}";

    static string WholeCall(string code) => $$"""{"type":"invalid_request","code":"{{code}}","detail":"Nope."}""";

    static string StatusFound(string messageId)
        => $$"""{"id":"{{messageId}}","status":"sent","created_at":"2026-11-22T10:00:00Z","status_at":"2026-11-22T10:00:01Z"}""";

    static string[] Keys(RecordedRequest request)
    {
        using var body = JsonDocument.Parse(request.Body!);
        return body.RootElement.GetProperty("recipients").EnumerateArray()
            .Select(recipient => recipient.GetProperty("idempotency_key").GetString()!)
            .ToArray();
    }

    static string[] QueuedSlots(RecordedRequest request) => Keys(request).Select(key => Queued("msg_" + key)).ToArray();

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

    static int Bytes(string text) => Encoding.UTF8.GetByteCount(text);
}
