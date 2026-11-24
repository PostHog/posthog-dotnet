using System.Net;
using System.Text.Json;
using PostHog.Emails;

namespace EmailsTests;

public class TheSendAsyncMethodWithScheduledAt
{
    const string MessageId = "01a18356-c56c-0001-e494-7666bc915188";
    const string Due = "2026-12-01T09:00:00.000Z";
    static readonly DateTimeOffset DueTime = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

    public static TheoryData<DateTimeOffset, string> TimesOnTheWire => new()
    {
        { DueTime, Due },
        { new DateTimeOffset(2026, 12, 1, 11, 0, 0, TimeSpan.FromHours(2)), Due },
        { new DateTimeOffset(2026, 12, 1, 4, 0, 0, TimeSpan.FromHours(-5)), Due },
        { DueTime.AddMilliseconds(123), "2026-12-01T09:00:00.123Z" },
        { DueTime.AddMilliseconds(123).AddTicks(1), "2026-12-01T09:00:00.124Z" },
        { new DateTimeOffset(2026, 12, 1, 11, 0, 0, TimeSpan.FromHours(2)).AddTicks(5), "2026-12-01T09:00:00.001Z" },
        { new DateTimeOffset(2026, 12, 31, 23, 59, 59, 999, TimeSpan.Zero).AddTicks(1), "2027-01-01T00:00:00.000Z" },
    };

    [Theory]
    [MemberData(nameof(TimesOnTheWire))]
    public async Task SendsTheTimeRoundedUpToTheMillisecondInUtcAndReturnsTheConfirmedSchedule(
        DateTimeOffset scheduledAt,
        string wire)
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, Scheduled(wire));

        var result = await harness.Client.Emails.SendAsync(TemplateSend(scheduledAt));

        Assert.Equal(wire, SentTime(Assert.Single(harness.Requests)));
        Assert.Equal("scheduled", result.Status);
        Assert.Equal(DateTimeOffset.Parse(wire, System.Globalization.CultureInfo.InvariantCulture), result.ScheduledAt);
        Assert.Equal(TimeSpan.Zero, result.ScheduledAt!.Value.Offset);
    }

    [Fact]
    public async Task SchedulesACodeSendToo()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, Scheduled(Due));

        var result = await harness.Client.Emails.SendAsync(new SendCodeEmailOptions
        {
            To = "user@example.com",
            TemplateKey = "reminder",
            Subject = "Your trial ends tomorrow",
            Html = "<p>Hi</p>",
            ScheduledAt = DueTime,
        });

        Assert.Equal(
            $$"""{"template_key":"reminder","to":"user@example.com","subject":"Your trial ends tomorrow","html":"\u003Cp\u003EHi\u003C/p\u003E","scheduled_at":"{{Due}}"}""",
            Assert.Single(harness.Requests).Body);
        Assert.Equal(("scheduled", DueTime), (result.Status, result.ScheduledAt));
    }

    [Fact]
    public async Task SendsTheSameRoundedTimeOnEveryRetry()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.ServiceUnavailable, """{"code": "service_unavailable"}""");
        harness.Respond(HttpStatusCode.Accepted, Scheduled("2026-12-01T09:00:00.124Z"));

        await harness.Client.Emails.SendAsync(TemplateSend(DueTime.AddMilliseconds(123).AddTicks(1)));

        Assert.Equal(2, harness.Requests.Count);
        Assert.All(harness.Requests, request => Assert.Equal("2026-12-01T09:00:00.124Z", SentTime(request)));
    }

    [Theory]
    [InlineData(-60)]
    [InlineData(0)]
    [InlineData(59)]
    [InlineData(60)]
    public async Task ReturnsAnEmailThatGoesOutNowWhenTheTimeHadPassed(int secondsAhead)
    {
        await using var harness = new EmailHarness();
        harness.Time.Now = DueTime.AddSeconds(-secondsAhead);
        harness.Respond(
            HttpStatusCode.Accepted,
            $$"""{"message_id": "{{MessageId}}", "status": "queued", "scheduled_at": null}""");

        var result = await harness.Client.Emails.SendAsync(TemplateSend(DueTime));

        Assert.Equal(MessageId, result.Id);
        Assert.Equal("queued", result.Status);
        Assert.Null(result.ScheduledAt);
    }

    [Theory]
    [InlineData($$"""{"message_id": "{{MessageId}}"}""")]
    [InlineData($$"""{"message_id": "{{MessageId}}", "status": "queued"}""")]
    [InlineData($$"""{"message_id": "{{MessageId}}", "status": "scheduled", "scheduled_at": "2026-12-01T09:00:00.001Z"}""")]
    [InlineData($$"""{"message_id": "{{MessageId}}", "status": "scheduled", "scheduled_at": "2026-12-01T09:00:00"}""")]
    [InlineData($$"""{"message_id": "{{MessageId}}", "status": "scheduled", "scheduled_at": "tomorrow"}""")]
    [InlineData($$"""{"message_id": "{{MessageId}}", "status": "scheduled", "scheduled_at": null}""")]
    [InlineData($$"""{"message_id": "{{MessageId}}", "status": "queued", "scheduled_at": "{{Due}}"}""")]
    [InlineData($$"""{"message_id": "{{MessageId}}", "scheduled_at": "{{Due}}"}""")]
    [InlineData($$"""{"message_id": "{{MessageId}}", "status": "scheduled", "scheduled_at": "\ud800"}""")]
    [InlineData($$"""{"message_id": "{{MessageId}}", "status": "\ud800", "scheduled_at": "{{Due}}"}""")]
    public async Task FailsWithTheMessageIdWhenPostHogAcceptsWithoutConfirmingTheTime(string body)
        => await FailsWithTheMessageId(DueTime, body);

    [Theory]
    [InlineData("2026-12-01Z")]
    [InlineData("2026-12-01T00:00Z")]
    [InlineData("2026-12-01 00:00:00Z")]
    [InlineData("2026-12-01T00:00:00.Z")]
    public async Task FailsWithTheMessageIdWhenPostHogConfirmsAnIncompleteTime(string answered)
        => await FailsWithTheMessageId(
            new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
            $$"""{"message_id": "{{MessageId}}", "status": "scheduled", "scheduled_at": "{{answered}}"}""");

    [Theory]
    [InlineData("2026-12-01T09:00:00Z")]
    [InlineData("2026-12-01T09:00:00.000000+00:00")]
    [InlineData("2026-12-01T10:00:00.000+01:00")]
    public async Task AcceptsTheConfirmedTimeInAnyOffset(string answered)
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, Scheduled(answered));

        var result = await harness.Client.Emails.SendAsync(TemplateSend(DueTime));

        Assert.Equal(("scheduled", DueTime), (result.Status, result.ScheduledAt));
    }

    static async Task FailsWithTheMessageId(DateTimeOffset scheduledAt, string body)
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, body);
        harness.Respond(HttpStatusCode.Accepted, Scheduled(Due));

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(TemplateSend(scheduledAt)));

        Assert.Single(harness.Requests);
        Assert.Equal("schedule_not_confirmed", error.Code);
        Assert.Equal("application_error", error.Name);
        Assert.Equal(HttpStatusCode.Accepted, error.StatusCode);
        Assert.Equal("scheduled_at", error.Field);
        Assert.Equal(MessageId, error.MessageId);
        Assert.Contains(MessageId, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(61)]
    [InlineData(6 * 24 * 60 * 60)]
    public async Task FailsWhenPostHogSendsATimeThatHasNotComeNow(int secondsAhead)
    {
        await using var harness = new EmailHarness();
        harness.Time.Now = DueTime.AddSeconds(-secondsAhead);
        harness.Respond(
            HttpStatusCode.Accepted,
            $$"""{"message_id": "{{MessageId}}", "status": "queued", "scheduled_at": null}""");

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(TemplateSend(DueTime)));

        Assert.Single(harness.Requests);
        Assert.Equal("schedule_not_confirmed", error.Code);
        Assert.Equal(MessageId, error.MessageId);
    }

    [Fact]
    public async Task ReturnsAnEmailThatGoesOutNowWithAClockAtTheEndOfTime()
    {
        await using var harness = new EmailHarness();
        harness.Time.Now = DateTimeOffset.MaxValue.AddSeconds(-30);
        harness.Respond(
            HttpStatusCode.Accepted,
            $$"""{"message_id": "{{MessageId}}", "status": "queued", "scheduled_at": null}""");

        var result = await harness.Client.Emails.SendAsync(TemplateSend(DateTimeOffset.MaxValue.AddSeconds(-60)));

        Assert.Equal(("queued", null), (result.Status, result.ScheduledAt));
    }

    [Fact]
    public async Task KeepsTheConfirmedScheduleWhenAWarningIsUnreadable()
    {
        await using var harness = new EmailHarness();
        harness.Respond(
            HttpStatusCode.Accepted,
            $$"""{"message_id": "{{MessageId}}", "status": "scheduled", "scheduled_at": "{{Due}}", "warnings": [{"code": "no_distinct_id", "detail": "\ud800"}]}""");

        var result = await harness.Client.Emails.SendAsync(TemplateSend(DueTime));

        Assert.Equal((MessageId, "scheduled", DueTime), (result.Id, result.Status, result.ScheduledAt));
    }

    [Fact]
    public async Task LogsTheWarningsOfAnAcceptanceWithoutAConfirmedTime()
    {
        await using var harness = new EmailHarness();
        harness.Respond(
            HttpStatusCode.Accepted,
            $$"""{"message_id": "{{MessageId}}", "warnings": [{"code": "no_distinct_id", "detail": "Pass distinct_id."}]}""");

        await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.SendAsync(TemplateSend(DueTime)));

        var logged = Assert.Single(harness.Logs.GetAllEvents(minimumLevel: Microsoft.Extensions.Logging.LogLevel.Warning));
        Assert.Equal("PostHog email warning no_distinct_id: Pass distinct_id.", logged.Message);
    }

    [Fact]
    public async Task SurfacesARefusedTimeWithoutSendingNowInstead()
    {
        await using var harness = new EmailHarness();
        harness.Respond(
            HttpStatusCode.BadRequest,
            """{"type": "validation_error", "code": "scheduled_too_far", "detail": "`scheduled_at` is more than 7 days away, so PostHog didn't send the email.", "attr": "scheduled_at"}""");
        harness.Respond(HttpStatusCode.Accepted, $$"""{"message_id": "{{MessageId}}"}""");

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(TemplateSend(DueTime)));

        Assert.Equal("2026-12-01T09:00:00.000Z", SentTime(Assert.Single(harness.Requests)));
        Assert.Equal("scheduled_too_far", error.Code);
        Assert.Equal("validation_error", error.Name);
        Assert.Equal("scheduled_at", error.Field);
        Assert.Null(error.MessageId);
    }

    [Theory]
    [InlineData("https://us.i.posthog.com", null, "https://us.posthog.com")]
    [InlineData("https://eu.i.posthog.com", null, "https://eu.posthog.com")]
    [InlineData("https://proxy.example.com", "https://eu.posthog.com/", "https://eu.posthog.com")]
    public async Task SchedulesThroughEitherPostHogCloudRegion(string host, string? appHost, string expected)
    {
        await using var harness = new EmailHarness(options =>
        {
            options.HostUrl = new Uri(host);
            options.AppHost = appHost is null ? null : new Uri(appHost);
        });
        harness.Respond(HttpStatusCode.Accepted, Scheduled(Due));

        var result = await harness.Client.Emails.SendAsync(TemplateSend(DueTime));

        Assert.Equal(expected + "/api/messaging/send/email/", Assert.Single(harness.Requests).Url);
        Assert.Equal("scheduled", result.Status);
    }

    [Theory]
    [InlineData("https://proxy.example.com", "https://posthog.example.com")]
    [InlineData("https://us.i.posthog.com", "https://ph.example.com")]
    [InlineData("http://localhost:8010", "http://localhost:8000")]
    public async Task RefusesToScheduleOffPostHogCloudBeforeAnyRequest(string host, string appHost)
    {
        await using var harness = new EmailHarness(options =>
        {
            options.HostUrl = new Uri(host);
            options.AppHost = new Uri(appHost);
            options.AllowInsecureAppHost = true;
        });

        var error = await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.SendAsync(new SendCodeEmailOptions
        {
            To = "user@example.com",
            TemplateKey = "invoice",
            Subject = "Your invoice",
            Html = "<p>Attached</p>",
            Attachments = [new EmailAttachment { Filename = "invoice.pdf", Content = new byte[] { 1, 2, 3 } }],
            ScheduledAt = DueTime,
        }));

        Assert.Empty(harness.Requests);
        Assert.Equal("scheduling_requires_cloud", error.Code);
        Assert.Equal("validation_error", error.Name);
        Assert.Equal("scheduled_at", error.Field);
        Assert.Contains(appHost, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NamesTheRefusedHostWithoutItsCredentials()
    {
        await using var harness = new EmailHarness(options =>
            options.AppHost = new Uri("https://proxy-user:proxy-pass@proxy.example.com/posthog/"));

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(TemplateSend(DueTime)));

        Assert.Equal("scheduling_requires_cloud", error.Code);
        Assert.Contains("https://proxy.example.com", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("proxy-pass", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("proxy-user", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(harness.Requests);
    }

    [Fact]
    public async Task RefusesATimeTooLateToRoundUpBeforeAnyRequest()
    {
        await using var harness = new EmailHarness();

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => harness.Client.Emails.SendAsync(TemplateSend(DateTimeOffset.MaxValue)));

        Assert.Contains("ScheduledAt", error.Message, StringComparison.Ordinal);
        Assert.Equal("email", error.ParamName);
        Assert.Empty(harness.Requests);
    }

    static SendTemplateEmailOptions TemplateSend(DateTimeOffset scheduledAt) => new()
    {
        To = "user@example.com",
        TemplateId = Guid.Parse("0190f6d2-7c1e-7b3a-9f5e-1c2d3e4f5a6b"),
        ScheduledAt = scheduledAt,
    };

    static string Scheduled(string scheduledAt)
        => $$"""{"message_id": "{{MessageId}}", "status": "scheduled", "scheduled_at": "{{scheduledAt}}"}""";

    static string? SentTime(RecordedRequest request)
    {
        using var body = JsonDocument.Parse(request.Body!);
        return body.RootElement.GetProperty("scheduled_at").GetString();
    }
}
