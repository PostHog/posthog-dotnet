using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PostHog;
using PostHog.Emails;

namespace EmailsTests;

public class TheSendAsyncMethodWithATemplate
{
    static readonly Guid TemplateId = Guid.Parse("0190F6D2-7C1E-7B3A-9F5E-1C2D3E4F5A6B");

    [Fact]
    public async Task SendsTheFlatTemplateBodyToTheAppHost()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188", "replayed": false}""");

        var result = await harness.Client.Emails.SendAsync(new SendTemplateEmailOptions
        {
            To = "user@example.com",
            TemplateId = TemplateId,
            TemplateData = new Dictionary<string, object> { ["name"] = "Ada" },
            DistinctId = "user_123",
        });

        Assert.Equal("01a18356-c56c-0001-e494-7666bc915188", result.Id);
        Assert.False(result.Replayed);
        Assert.Empty(result.Warnings);
        var request = Assert.Single(harness.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://us.posthog.com/api/messaging/send/email/", request.Url);
        Assert.Equal("Bearer phs_secret", request.Header("Authorization"));
        Assert.Equal("application/json", request.Header("Accept"));
        Assert.Equal("application/json", request.Header("Content-Type"));
        Assert.StartsWith("posthog-dotnet/", request.Header("User-Agent"), StringComparison.Ordinal);
        Assert.Null(request.Header("X-PostHog-Migrated-From"));
        Assert.Equal(
            """{"template_id":"0190f6d2-7c1e-7b3a-9f5e-1c2d3e4f5a6b","to":"user@example.com","message_data":{"name":"Ada"},"distinct_id":"user_123"}""",
            request.Body);
    }

    [Fact]
    public async Task SendsReadOnlyTemplateData()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        await harness.Client.Emails.SendAsync(new SendTemplateEmailOptions
        {
            To = "user@example.com",
            TemplateId = TemplateId,
            TemplateData = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>
            {
                ["name"] = "Ada",
                ["order"] = new ReadOnlyDictionary<string, object>(new Dictionary<string, object> { ["total"] = 42 }),
            }),
        });

        Assert.Equal(
            """{"template_id":"0190f6d2-7c1e-7b3a-9f5e-1c2d3e4f5a6b","to":"user@example.com","message_data":{"name":"Ada","order":{"total":42}}}""",
            Assert.Single(harness.Requests).Body);
    }

    [Fact]
    public async Task KeepsEveryKeyOfReadOnlyTemplateDataWithNonStringKeys()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");
        var second = new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);

        await harness.Client.Emails.SendAsync(new SendTemplateEmailOptions
        {
            To = "user@example.com",
            TemplateId = TemplateId,
            TemplateData = new Dictionary<string, object>
            {
                ["visits"] = new ReadOnlyDictionary<DateTime, object>(new Dictionary<DateTime, object>
                {
                    [second] = 1,
                    [second.AddMilliseconds(1)] = 2,
                }),
            },
        });

        using var body = JsonDocument.Parse(Assert.Single(harness.Requests).Body!);
        var visits = body.RootElement.GetProperty("message_data").GetProperty("visits");
        Assert.Equal(1, visits.GetProperty("2026-11-02T10:00:00Z").GetInt32());
        Assert.Equal(2, visits.GetProperty("2026-11-02T10:00:00.001Z").GetInt32());
    }

    [Fact]
    public async Task KeepsKeysThatOnlyTheSourceComparerTellsApart()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");
        var utc = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

        await harness.Client.Emails.SendAsync(new SendTemplateEmailOptions
        {
            To = "user@example.com",
            TemplateId = TemplateId,
            TemplateData = new Dictionary<string, object>
            {
                ["visits"] = new ReadOnlyDictionary<DateTimeOffset, object>(
                    new Dictionary<DateTimeOffset, object>(new OffsetSensitiveComparer())
                    {
                        [utc] = 1,
                        [utc.ToOffset(TimeSpan.FromHours(1))] = 2,
                    }),
            },
        });

        using var body = JsonDocument.Parse(Assert.Single(harness.Requests).Body!);
        var visits = body.RootElement.GetProperty("message_data").GetProperty("visits");
        Assert.Equal(1, visits.GetProperty("2026-11-02T10:00:00+00:00").GetInt32());
        Assert.Equal(2, visits.GetProperty("2026-11-02T11:00:00+01:00").GetInt32());
    }

    sealed class OffsetSensitiveComparer : IEqualityComparer<DateTimeOffset>
    {
        public bool Equals(DateTimeOffset x, DateTimeOffset y) => x.EqualsExact(y);

        public int GetHashCode(DateTimeOffset obj) => HashCode.Combine(obj.UtcDateTime, obj.Offset);
    }

    [Fact]
    public async Task SendsEmptyTemplateDataAndNoDistinctIdWhenLeftOut()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        await harness.Client.Emails.SendAsync(new SendTemplateEmailOptions
        {
            To = "user@example.com",
            TemplateId = TemplateId,
        });

        Assert.Equal(
            """{"template_id":"0190f6d2-7c1e-7b3a-9f5e-1c2d3e4f5a6b","to":"user@example.com","message_data":{}}""",
            Assert.Single(harness.Requests).Body);
    }

    [Fact]
    public async Task ReturnsAReplayAndLogsEachWarning()
    {
        await using var harness = new EmailHarness();
        harness.Respond(
            HttpStatusCode.Accepted,
            """{"message_id": "01a18356-c56c-0001-e494-7666bc915188", "replayed": true, "warnings": [{"code": "no_distinct_id", "detail": "Pass distinct_id.", "fix": "Add it."}]}""");

        var result = await harness.Client.Emails.SendAsync(new SendTemplateEmailOptions
        {
            To = "user@example.com",
            TemplateId = TemplateId,
        });

        Assert.True(result.Replayed);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal("no_distinct_id", warning.Code);
        Assert.Equal("Pass distinct_id.", warning.Message);
        Assert.Equal("Add it.", warning.Fix);
        var logged = Assert.Single(harness.Logs.GetAllEvents(minimumLevel: LogLevel.Warning));
        Assert.Equal("PostHog email warning no_distinct_id: Pass distinct_id.", logged.Message);
    }

    [Fact]
    public async Task SendsTheMigratedFromHeader()
    {
        await using var harness = new EmailHarness(options => options.MigratedFrom = "resend");
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        await harness.Client.Emails.SendAsync(new SendTemplateEmailOptions
        {
            To = "user@example.com",
            TemplateId = TemplateId,
        });

        Assert.Equal("resend", Assert.Single(harness.Requests).Header("X-PostHog-Migrated-From"));
    }
}

public class TheSendAsyncMethodWithCodeContent
{
    [Fact]
    public async Task SendsTheRenderedContentWithoutUnsetFields()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        await harness.Client.Emails.SendAsync(new SendCodeEmailOptions
        {
            To = "user@example.com",
            TemplateKey = "password-reset",
            Subject = "Reset your password",
            Html = "<p>Hi</p>",
            From = "Acme <security@example.com>",
            ReplyTo = ["support@example.com"],
            DistinctId = "user_123",
        });

        var request = Assert.Single(harness.Requests);
        Assert.Equal("https://us.posthog.com/api/messaging/send/email/", request.Url);
        Assert.Equal(
            """{"template_key":"password-reset","to":"user@example.com","subject":"Reset your password","html":"\u003Cp\u003EHi\u003C/p\u003E","from":"Acme \u003Csecurity@example.com\u003E","reply_to":["support@example.com"],"distinct_id":"user_123"}""",
            request.Body);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("<p>Hi</p>", body.RootElement.GetProperty("html").GetString());
    }

    [Fact]
    public async Task SendsTextOnlyContentWithEngagementCaptureOff()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        await harness.Client.Emails.SendAsync(new SendCodeEmailOptions
        {
            To = "user@example.com",
            TemplateKey = "receipt_2",
            Subject = "Your receipt",
            Text = "Thanks!",
#pragma warning disable CS0618 // CaptureEngagement is deprecated but still sent when set.
            CaptureEngagement = false,
#pragma warning restore CS0618
        });

        Assert.Equal(
            """{"template_key":"receipt_2","to":"user@example.com","subject":"Your receipt","text":"Thanks!","capture_engagement":false}""",
            Assert.Single(harness.Requests).Body);
    }

    public static TheoryData<MessageCategory, string, string, string, string?> NewCategories => new()
    {
        {
            new MessageCategory
            {
                Key = "newsletter",
                Type = MessageCategoryType.Marketing,
                Name = "Newsletter",
                Description = "Monthly product news",
            },
            "newsletter", "marketing", "Newsletter", "Monthly product news"
        },
        {
            new MessageCategory { Key = "receipts", Type = MessageCategoryType.Transactional, Name = "Receipts" },
            "receipts", "transactional", "Receipts", null
        },
    };

    const string SendUrl = "https://us.posthog.com/api/messaging/send/email/";
    static readonly string[] CodeSendKeys = ["html", "subject", "template_key", "to"];
    static readonly string[] CategorizedCodeSendKeys = ["category", "html", "subject", "template_key", "to"];
    static readonly string[] CategorizedCopiesCodeSendKeys = ["bcc", "category", "cc", "html", "subject", "template_key", "to"];
    static readonly string[] CategorizedAttachmentCodeSendKeys = ["attachments", "category", "html", "subject", "template_key", "to"];
    static readonly string[] CategoryKeys = ["key", "name", "type"];
    static readonly string[] CategoryWithDescriptionKeys = ["description", "key", "name", "type"];
    static readonly string[] UploadRequestKeys = ["files"];
    static readonly string[] UploadFileKeys = ["sha256", "size"];
    static readonly string[] AttachmentRefKeys = ["content_type", "filename", "sha256"];

    static void AssertObjectKeys(JsonElement element, string[] expectedKeys)
    {
        Assert.Equal(JsonValueKind.Object, element.ValueKind);
        Assert.Equal(
            expectedKeys,
            element.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList());
    }

    static void AssertCodeSendFields(JsonElement root, SendCodeEmailOptions email)
    {
        Assert.Equal(email.TemplateKey, root.GetProperty("template_key").GetString());
        Assert.Equal(email.To, root.GetProperty("to").GetString());
        Assert.Equal(email.Subject, root.GetProperty("subject").GetString());
        Assert.Equal(email.Html, root.GetProperty("html").GetString());
    }

    static void AssertCategoryObject(
        JsonElement element,
        string key,
        string type,
        string name,
        string? description = null)
    {
        if (description is null)
        {
            AssertObjectKeys(element, CategoryKeys);
        }
        else
        {
            AssertObjectKeys(element, CategoryWithDescriptionKeys);
            Assert.Equal(description, element.GetProperty("description").GetString());
        }

        Assert.Equal(key, element.GetProperty("key").GetString());
        Assert.Equal(type, element.GetProperty("type").GetString());
        Assert.Equal(name, element.GetProperty("name").GetString());
    }

    [Theory]
    [MemberData(nameof(NewCategories))]
    public async Task SendsCategoryObjectWithOptionalDescriptionOnTheWire(
        MessageCategory category,
        string key,
        string type,
        string name,
        string? description)
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");
        var email = CodeSendIn(category);

        var result = await harness.Client.Emails.SendAsync(email);

        Assert.Equal("01a18356-c56c-0001-e494-7666bc915188", result.Id);
        var request = Assert.Single(harness.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(SendUrl, request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        AssertObjectKeys(root, CategorizedCodeSendKeys);
        AssertCodeSendFields(root, email);
        AssertCategoryObject(root.GetProperty("category"), key, type, name, description);
    }

    [Fact]
    public async Task SurfacesUnsubscribeLinkRequiredRefusalWithoutRetrying()
    {
        await using var harness = new EmailHarness();
        harness.Respond(
            HttpStatusCode.BadRequest,
            """{"type":"invalid_request","code":"unsubscribe_link_required","detail":"A marketing email needs a clickable `{{ unsubscribe_url }}` link."}""");
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");
        var email = new SendCodeEmailOptions
        {
            To = "a@example.com",
            TemplateKey = "k",
            Subject = "s",
            Html = "<p>News</p>",
            Category = new MessageCategory
            {
                Key = "newsletter",
                Type = MessageCategoryType.Marketing,
                Name = "Newsletter",
            },
        };

        var error = await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.SendAsync(email));

        Assert.Equal("unsubscribe_link_required", error.Code);
        Assert.Equal("validation_error", error.Name);
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Null(error.Field);
        var request = Assert.Single(harness.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(SendUrl, request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        AssertObjectKeys(root, CategorizedCodeSendKeys);
        AssertCodeSendFields(root, email);
        AssertCategoryObject(root.GetProperty("category"), "newsletter", "marketing", "Newsletter");
    }

    [Fact]
    public async Task KeepsAnEmptyCategoryDescriptionOnTheWireAndOmitsANullOne()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915189"}""");
        var emptyEmail = CodeSendIn(new MessageCategory
        {
            Key = "newsletter",
            Type = MessageCategoryType.Marketing,
            Name = "Newsletter",
            Description = "",
        });
        var nullEmail = CodeSendIn(new MessageCategory
        {
            Key = "receipts",
            Type = MessageCategoryType.Transactional,
            Name = "Receipts",
            Description = null,
        });

        await harness.Client.Emails.SendAsync(emptyEmail);
        await harness.Client.Emails.SendAsync(nullEmail);

        Assert.Equal(2, harness.Requests.Count);
        Assert.Equal(HttpMethod.Post, harness.Requests[0].Method);
        Assert.Equal(SendUrl, harness.Requests[0].Url);
        using var emptyBody = JsonDocument.Parse(harness.Requests[0].Body!);
        AssertObjectKeys(emptyBody.RootElement, CategorizedCodeSendKeys);
        AssertCodeSendFields(emptyBody.RootElement, emptyEmail);
        AssertCategoryObject(emptyBody.RootElement.GetProperty("category"), "newsletter", "marketing", "Newsletter", "");

        Assert.Equal(HttpMethod.Post, harness.Requests[1].Method);
        Assert.Equal(SendUrl, harness.Requests[1].Url);
        using var nullBody = JsonDocument.Parse(harness.Requests[1].Body!);
        AssertObjectKeys(nullBody.RootElement, CategorizedCodeSendKeys);
        AssertCodeSendFields(nullBody.RootElement, nullEmail);
        AssertCategoryObject(nullBody.RootElement.GetProperty("category"), "receipts", "transactional", "Receipts");
    }

    public static TheoryData<MessageCategory?> NoCategory => new() { MessageCategory.Transactional, null };

    [Theory]
    [MemberData(nameof(NoCategory))]
    public async Task SendsTransactionalWithNoCategory(MessageCategory? category)
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");
        var email = CodeSendIn(category);

        await harness.Client.Emails.SendAsync(email);

        var request = Assert.Single(harness.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(SendUrl, request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        if (category is null)
        {
            AssertObjectKeys(root, CodeSendKeys);
        }
        else
        {
            AssertObjectKeys(root, CategorizedCodeSendKeys);
            Assert.Equal("transactional", root.GetProperty("category").GetString());
        }

        AssertCodeSendFields(root, email);
    }

    public static TheoryData<MessageCategory, string, string> SeparatelyConstructedTransactionalCategories => new()
    {
        {
            new MessageCategory
            {
                Key = MessageCategory.Transactional.Key,
                Type = MessageCategory.Transactional.Type,
                Name = MessageCategory.Transactional.Name,
                Description = MessageCategory.Transactional.Description,
            },
            "transactional",
            "Transactional"
        },
        {
            new MessageCategory
            {
                Key = "transactional",
                Type = MessageCategoryType.Marketing,
                Name = "Transactional Updates",
            },
            "marketing",
            "Transactional Updates"
        },
    };

    [Theory]
    [MemberData(nameof(SeparatelyConstructedTransactionalCategories))]
    public async Task SendsASeparatelyConstructedCategoryKeyedTransactionalAsAnObject(
        MessageCategory category,
        string expectedType,
        string expectedName)
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");
        var email = CodeSendIn(category);

        await harness.Client.Emails.SendAsync(email);

        var request = Assert.Single(harness.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(SendUrl, request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        AssertObjectKeys(root, CategorizedCodeSendKeys);
        AssertCodeSendFields(root, email);
        AssertCategoryObject(root.GetProperty("category"), "transactional", expectedType, expectedName);
    }

    static readonly byte[] BinaryPdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A, 0xFF, 0x00, 0xFE];
    const string BinaryPdfSha256 = "073f87b9f33968773e842aa48a6afbcb4e79ae661452c94e2313d1dc7563072f";

    public static TheoryData<MessageCategory, HttpStatusCode, string, string?> CategoryRefusals => new()
    {
        {
            new MessageCategory { Key = "receipts", Type = MessageCategoryType.Marketing, Name = "Receipts" },
            HttpStatusCode.BadRequest, "category_mismatch", "category"
        },
        {
            MessageCategory.Transactional,
            HttpStatusCode.BadRequest, "category_mismatch", "category"
        },
        {
            new MessageCategory { Key = "newsletter", Type = MessageCategoryType.Marketing, Name = "Newsletter" },
            (HttpStatusCode)422, "recipient_opted_out", null
        },
    };

    [Theory]
    [MemberData(nameof(CategoryRefusals))]
    public async Task SurfacesCategoryRefusalsWithoutRetrying(
        MessageCategory category,
        HttpStatusCode status,
        string code,
        string? field)
    {
        await using var harness = new EmailHarness();
        var attrJson = field is null ? "" : $",\"attr\":\"{field}\"";
        harness.Respond(status, $$"""{"type":"invalid_request","code":"{{code}}","detail":"Refused."{{attrJson}}}""");
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        var email = CodeSendIn(category);

        var error = await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.SendAsync(email));

        Assert.Equal(code, error.Code);
        Assert.Equal("validation_error", error.Name);
        Assert.Equal(status, error.StatusCode);
        Assert.Equal(field, error.Field);
        var request = Assert.Single(harness.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(SendUrl, request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        AssertObjectKeys(root, CategorizedCodeSendKeys);
        AssertCodeSendFields(root, email);
        var sentCategory = root.GetProperty("category");
        if (ReferenceEquals(category, MessageCategory.Transactional))
        {
            Assert.Equal("transactional", sentCategory.GetString());
        }
        else
        {
            AssertCategoryObject(
                sentCategory,
                category.Key,
                category.Type == MessageCategoryType.Marketing ? "marketing" : "transactional",
                category.Name,
                category.Description);
        }
    }

    [Fact]
    public async Task SurfacesCopiesNotAllowedRefusalWithoutRetrying()
    {
        await using var harness = new EmailHarness();
        harness.Respond(
            HttpStatusCode.BadRequest,
            """{"type":"invalid_request","code":"copies_not_allowed","detail":"Marketing emails cannot include cc or bcc."}""");
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");
        var email = new SendCodeEmailOptions
        {
            To = "a@example.com",
            TemplateKey = "k",
            Subject = "s",
            Html = "<a href=\"{{ unsubscribe_url }}\">Unsubscribe</a>",
            Cc = ["copy@example.com"],
            Bcc = ["blind@example.com"],
            Category = new MessageCategory
            {
                Key = "newsletter",
                Type = MessageCategoryType.Marketing,
                Name = "Newsletter",
            },
        };

        var error = await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.SendAsync(email));

        Assert.Equal("copies_not_allowed", error.Code);
        Assert.Equal("validation_error", error.Name);
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Null(error.Field);
        var request = Assert.Single(harness.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(SendUrl, request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        AssertObjectKeys(root, CategorizedCopiesCodeSendKeys);
        AssertCodeSendFields(root, email);
        Assert.Equal(
            ["copy@example.com"],
            root.GetProperty("cc").EnumerateArray().Select(item => item.GetString()!).ToList());
        Assert.Equal(
            ["blind@example.com"],
            root.GetProperty("bcc").EnumerateArray().Select(item => item.GetString()!).ToList());
        AssertCategoryObject(root.GetProperty("category"), "newsletter", "marketing", "Newsletter");
    }

    [Theory]
    [InlineData("workflow_can_wait")]
    [InlineData("sandbox_fields_unsupported")]
    public async Task UploadsAttachmentsBeforeMarketingRefusal(string code)
    {
        using var posthog = new FakePostHogEmails();
        using var transport = new CategorizedSendHandler(posthog);
        transport.EnqueueSendResponse(
            HttpStatusCode.BadRequest,
            $$"""{"type":"invalid_request","code":"{{code}}","detail":"Marketing emails cannot carry attachments.","attr":"attachments"}""");
        transport.EnqueueSendResponse(
            HttpStatusCode.Accepted,
            """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");
        await using var harness = new EmailHarness(posthog: transport);

        Task<SendEmailResult> sendTask;
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            sendTask = harness.Client.Emails.SendAsync(new SendCodeEmailOptions
            {
                To = "a@example.com",
                TemplateKey = "k",
                Subject = "s",
                Html = "<a href=\"{{ unsubscribe_url }}\">Unsubscribe</a>",
                Category = new MessageCategory
                {
                    Key = "newsletter",
                    Type = MessageCategoryType.Marketing,
                    Name = "Newsletter",
                },
                Attachments =
                [
                    new EmailAttachment
                    {
                        Filename = "brochure.pdf",
                        Content = BinaryPdfBytes,
                        ContentType = "application/pdf",
                    },
                ],
            }, testTimeout.Token);

            var started = await Task.WhenAny(transport.UploadStarted, sendTask);
            Assert.Same(transport.UploadStarted, started);
            Assert.False(sendTask.IsCompleted);
            Assert.Equal(2, transport.Requests.Count);
            Assert.Equal(HttpMethod.Put, transport.Requests[1].Method);
            Assert.Equal(0, posthog.StorageWrites);
            Assert.Null(posthog.StoredContent(BinaryPdfSha256));
        }
        finally
        {
            transport.ReleaseUpload();
        }

        var error = await Assert.ThrowsAsync<EmailException>(() => sendTask);

        Assert.Equal(code, error.Code);
        Assert.Equal("validation_error", error.Name);
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal("attachments", error.Field);
        Assert.Equal(1, transport.StorageWritesAtSend);
        Assert.Equal(1, posthog.StorageWrites);
        AssertSignedPdfUploadBeforeSend(
            posthog,
            transport,
            "<a href=\"{{ unsubscribe_url }}\">Unsubscribe</a>",
            "brochure.pdf",
            "newsletter",
            "marketing",
            "Newsletter");
    }

    [Fact]
    public async Task UploadsAttachmentsAndSendsTransactionalCategoryOnTheWire()
    {
        using var posthog = new FakePostHogEmails();
        using var transport = new CategorizedSendHandler(posthog);
        transport.EnqueueSendResponse(
            HttpStatusCode.Accepted,
            """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");
        await using var harness = new EmailHarness(posthog: transport);

        Task<SendEmailResult> sendTask;
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            sendTask = harness.Client.Emails.SendAsync(new SendCodeEmailOptions
            {
                To = "a@example.com",
                TemplateKey = "k",
                Subject = "s",
                Html = "<p>Receipt</p>",
                Category = new MessageCategory
                {
                    Key = "receipts",
                    Type = MessageCategoryType.Transactional,
                    Name = "Receipts",
                },
                Attachments =
                [
                    new EmailAttachment
                    {
                        Filename = "invoice.pdf",
                        Content = BinaryPdfBytes,
                        ContentType = "application/pdf",
                    },
                ],
            }, testTimeout.Token);

            var started = await Task.WhenAny(transport.UploadStarted, sendTask);
            Assert.Same(transport.UploadStarted, started);
            Assert.False(sendTask.IsCompleted);
            Assert.Equal(2, transport.Requests.Count);
            Assert.Equal(HttpMethod.Put, transport.Requests[1].Method);
            Assert.Equal(0, posthog.StorageWrites);
            Assert.Null(posthog.StoredContent(BinaryPdfSha256));
        }
        finally
        {
            transport.ReleaseUpload();
        }

        var result = await sendTask;

        Assert.Equal("01a18356-c56c-0001-e494-7666bc915188", result.Id);
        Assert.Equal(1, transport.StorageWritesAtSend);
        Assert.Equal(1, posthog.StorageWrites);
        AssertSignedPdfUploadBeforeSend(
            posthog,
            transport,
            "<p>Receipt</p>",
            "invoice.pdf",
            "receipts",
            "transactional",
            "Receipts");
    }

    static void AssertSignedPdfUploadBeforeSend(
        FakePostHogEmails posthog,
        CategorizedSendHandler transport,
        string expectedHtml,
        string filename,
        string categoryKey,
        string categoryType,
        string categoryName)
    {
        var requests = transport.Requests;
        Assert.Equal(3, requests.Count);

        var prepare = requests[0];
        Assert.Equal(HttpMethod.Post, prepare.Method);
        Assert.Equal("https://us.posthog.com/api/messaging/attachments/uploads/", prepare.Url);
        Assert.Equal("Bearer phs_secret", prepare.Header("Authorization"));
        using (var prepareBody = JsonDocument.Parse(prepare.Body!))
        {
            AssertObjectKeys(prepareBody.RootElement, UploadRequestKeys);
            var file = Assert.Single(prepareBody.RootElement.GetProperty("files").EnumerateArray());
            AssertObjectKeys(file, UploadFileKeys);
            Assert.Equal(BinaryPdfSha256, file.GetProperty("sha256").GetString());
            Assert.Equal(BinaryPdfBytes.Length, file.GetProperty("size").GetInt32());
        }

        var grant = transport.GrantFor(BinaryPdfSha256)
            ?? throw new InvalidOperationException("Expected upload grant for PDF.");

        var upload = requests[1];
        Assert.Equal(new HttpMethod(grant.Method), upload.Method);
        Assert.Equal(grant.Url, upload.Url);
        Assert.Null(upload.Header("Authorization"));
        foreach (var header in grant.Headers)
        {
            Assert.Equal(header.Value, upload.Header(header.Key));
        }

        Assert.Equal(BinaryPdfBytes, upload.BodyBytes);

        var send = requests[2];
        Assert.Equal(HttpMethod.Post, send.Method);
        Assert.Equal(SendUrl, send.Url);
        Assert.Equal("Bearer phs_secret", send.Header("Authorization"));
        using var sendBody = JsonDocument.Parse(send.Body!);
        var root = sendBody.RootElement;
        AssertObjectKeys(root, CategorizedAttachmentCodeSendKeys);
        Assert.Equal("a@example.com", root.GetProperty("to").GetString());
        Assert.Equal("k", root.GetProperty("template_key").GetString());
        Assert.Equal("s", root.GetProperty("subject").GetString());
        Assert.Equal(expectedHtml, root.GetProperty("html").GetString());
        var attachment = Assert.Single(root.GetProperty("attachments").EnumerateArray());
        AssertObjectKeys(attachment, AttachmentRefKeys);
        var sentSha256 = attachment.GetProperty("sha256").GetString()!;
        Assert.Equal(BinaryPdfSha256, sentSha256);
        Assert.Equal(BinaryPdfBytes, posthog.StoredContent(sentSha256));
        Assert.Equal(filename, attachment.GetProperty("filename").GetString());
        Assert.Equal("application/pdf", attachment.GetProperty("content_type").GetString());
        AssertCategoryObject(root.GetProperty("category"), categoryKey, categoryType, categoryName);
    }

    sealed record GrantedUpload(string Url, string Method, IReadOnlyDictionary<string, string> Headers);

    sealed class CategorizedSendHandler(FakePostHogEmails inner, bool holdUpload = true) : DelegatingHandler(inner)
    {
        readonly ConcurrentQueue<Func<HttpResponseMessage>> _sendResponses = new();
        readonly ConcurrentDictionary<string, GrantedUpload> _grantedUploads = new(StringComparer.Ordinal);
        readonly ConcurrentDictionary<string, byte> _grantedUploadUrls = new(StringComparer.Ordinal);
        readonly List<RecordedRequest> _requests = [];
        readonly TaskCompletionSource<bool> _uploadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource<bool> _releaseUpload = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int _storageWritesAtSend;

        public List<RecordedRequest> Requests
        {
            get
            {
                lock (_requests)
                {
                    return _requests.ToList();
                }
            }
        }

        public Task UploadStarted => _uploadStarted.Task;

        public int StorageWritesAtSend => Volatile.Read(ref _storageWritesAtSend);

        public GrantedUpload? GrantFor(string sha256)
            => _grantedUploads.TryGetValue(sha256, out var grant) ? grant : null;

        public void ReleaseUpload() => _releaseUpload.TrySetResult(true);

        public void EnqueueSendResponse(HttpStatusCode status, string body)
            => _sendResponses.Enqueue(() => new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var recorded = await RecordedRequest.CaptureAsync(request, cancellationToken);
            lock (_requests)
            {
                _requests.Add(recorded);
            }

            if (request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath == "/api/messaging/attachments/uploads/")
            {
                var response = await base.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode && response.Content is not null)
                {
#if NET5_0_OR_GREATER
                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
#else
                    var responseBody = await response.Content.ReadAsStringAsync();
#endif
                    RecordGrantedUploads(responseBody);
                }

                return response;
            }

            if (request.Method == HttpMethod.Put)
            {
                if (holdUpload)
                {
                    _uploadStarted.TrySetResult(true);
                    await WaitForReleaseAsync(_releaseUpload.Task, cancellationToken);
                }

                if (!_grantedUploadUrls.ContainsKey(recorded.Url))
                {
                    return new HttpResponseMessage(HttpStatusCode.Forbidden)
                    {
                        Content = new StringContent(
                            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Error><Code>SignatureDoesNotMatch</Code>"
                            + "<Message>The request signature we calculated does not match the signature you provided."
                            + "</Message></Error>",
                            Encoding.UTF8,
                            "application/xml"),
                    };
                }

                return await base.SendAsync(request, cancellationToken);
            }

            if (request.Method == HttpMethod.Post
                && request.RequestUri!.AbsolutePath == "/api/messaging/send/email/"
                && _sendResponses.TryDequeue(out var respond))
            {
                Interlocked.Exchange(ref _storageWritesAtSend, inner.StorageWrites);
                return ValidateSendTransport(request, recorded.Body) ?? respond();
            }

            return await base.SendAsync(request, cancellationToken);
        }

        HttpResponseMessage? ValidateSendTransport(HttpRequestMessage request, string? body)
        {
            if (request.Headers.Authorization is not { Scheme: "Bearer", Parameter: FakePostHogEmails.SecretKey })
            {
                return JsonRefusal(
                    HttpStatusCode.Unauthorized,
                    "authentication_failed",
                    null,
                    "Invalid secret key.");
            }

            if (!string.Equals(
                    request.Content?.Headers.ContentType?.MediaType,
                    "application/json",
                    StringComparison.OrdinalIgnoreCase))
            {
                return JsonRefusal(
                    HttpStatusCode.UnsupportedMediaType,
                    "unsupported_media_type",
                    null,
                    "Unsupported media type in request.");
            }

            if (string.IsNullOrEmpty(body))
            {
                return JsonRefusal(HttpStatusCode.BadRequest, "parse_error", null, "Request body must be JSON.");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                return JsonRefusal(HttpStatusCode.BadRequest, "parse_error", null, "JSON parse error.");
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return JsonRefusal(HttpStatusCode.BadRequest, "invalid_request", null, "Body must be an object.");
                }

                if (root.TryGetProperty("attachments", out var attachments)
                    && attachments.ValueKind == JsonValueKind.Array)
                {
                    var index = 0;
                    foreach (var reference in attachments.EnumerateArray())
                    {
                        var sha256 = reference.ValueKind == JsonValueKind.Object
                            && reference.TryGetProperty("sha256", out var shaField)
                            && shaField.ValueKind == JsonValueKind.String
                            ? shaField.GetString()
                            : null;
                        if (string.IsNullOrEmpty(sha256) || inner.StoredContent(sha256) is null)
                        {
                            return JsonRefusal(
                                HttpStatusCode.BadRequest,
                                "attachment_not_uploaded",
                                $"attachments__{index}__sha256",
                                "A file this email names isn't uploaded, so PostHog didn't send the email.");
                        }

                        index++;
                    }
                }
            }

            return null;
        }

        static HttpResponseMessage JsonRefusal(HttpStatusCode status, string code, string? attr, string detail)
            => new(status)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new Dictionary<string, object?>
                    {
                        ["type"] = "validation_error",
                        ["code"] = code,
                        ["detail"] = detail,
                        ["attr"] = attr,
                    }),
                    Encoding.UTF8,
                    "application/json"),
            };

        static async Task WaitForReleaseAsync(Task releaseTask, CancellationToken cancellationToken)
        {
            if (releaseTask.IsCompleted)
            {
                await releaseTask;
                return;
            }

            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(
                static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(),
                cancelled);
            var completed = await Task.WhenAny(releaseTask, cancelled.Task);
            await completed;
        }

        void RecordGrantedUploads(string responseBody)
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("files", out var files)
                || files.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var file in files.EnumerateArray())
            {
                if (file.TryGetProperty("status", out var status)
                    && status.GetString() == "upload"
                    && file.TryGetProperty("sha256", out var sha256)
                    && sha256.GetString() is { Length: > 0 } fileSha256
                    && file.TryGetProperty("url", out var url)
                    && url.GetString() is { Length: > 0 } uploadUrl
                    && file.TryGetProperty("method", out var method)
                    && method.GetString() is { Length: > 0 } uploadMethod
                    && file.TryGetProperty("headers", out var headerFields)
                    && headerFields.ValueKind == JsonValueKind.Object)
                {
                    var headers = headerFields
                        .EnumerateObject()
                        .ToDictionary(
                            header => header.Name,
                            header => header.Value.GetString()!,
                            StringComparer.OrdinalIgnoreCase);
                    _grantedUploads[fileSha256] = new GrantedUpload(uploadUrl, uploadMethod, headers);
                    _grantedUploadUrls[uploadUrl] = 0;
                }
            }
        }
    }

    public static TheoryData<string, string, string, string, string?, string?> MarketingCategoryCompatibilityRefusals => new()
    {
        {
            """{"type":"invalid_request","code":"categories_not_enabled","attr":"category","detail":"Sending marketing email in a message category isn't available for this project yet, so PostHog didn't send this email. Send it from a PostHog workflow instead, or wait until category support is enabled."}""",
            "categories_not_enabled",
            "category",
            "Sending marketing email in a message category isn't available for this project yet, so PostHog didn't send this email. Send it from a PostHog workflow instead, or wait until category support is enabled.",
            null,
            null
        },
        {
            """{"type":"validation_error","code":"invalid_input","attr":"category","detail":"PostHog doesn't take this field, so it doesn't send the email without it."}""",
            "invalid_input",
            "category",
            "PostHog doesn't take this field, so it doesn't send the email without it.",
            null,
            null
        },
        {
            """{"type":"invalid_request","code":"field_not_accepted","attr":"category","detail":"PostHog doesn't take this field yet, so it didn't send the email.","why":"This PostHog version doesn't take the field yet, so it refused the email instead of sending it without it.","fix":"Wait until PostHog accepts `category`, then send the email again."}""",
            "field_not_accepted",
            "category",
            "PostHog doesn't take this field yet, so it didn't send the email.",
            "This PostHog version doesn't take the field yet, so it refused the email instead of sending it without it.",
            "Wait until PostHog accepts `category`, then send the email again."
        },
    };

    public static TheoryData<string, string, string, string, string?, string?> TransactionalCategoryCompatibilityRefusals => new()
    {
        {
            """{"type":"invalid_request","code":"categories_not_enabled","attr":"category","detail":"Sending in a message category isn't available for this project yet. Leave `category` out, or send `transactional`."}""",
            "categories_not_enabled",
            "category",
            "Sending in a message category isn't available for this project yet. Leave `category` out, or send `transactional`.",
            null,
            null
        },
        {
            """{"type":"validation_error","code":"invalid_input","attr":"category","detail":"PostHog doesn't take this field, so it doesn't send the email without it."}""",
            "invalid_input",
            "category",
            "PostHog doesn't take this field, so it doesn't send the email without it.",
            null,
            null
        },
        {
            """{"type":"invalid_request","code":"field_not_accepted","attr":"category","detail":"PostHog doesn't take this field yet, so it didn't send the email.","why":"This PostHog version doesn't take the field yet, so it refused the email instead of sending it without it.","fix":"Wait until PostHog accepts `category`, then send the email again."}""",
            "field_not_accepted",
            "category",
            "PostHog doesn't take this field yet, so it didn't send the email.",
            "This PostHog version doesn't take the field yet, so it refused the email instead of sending it without it.",
            "Wait until PostHog accepts `category`, then send the email again."
        },
    };

    [Theory]
    [MemberData(nameof(MarketingCategoryCompatibilityRefusals))]
    public async Task MapsOlderPostHogRefusalEnvelopesWithoutRetrying(
        string responseBody,
        string expectedCode,
        string expectedField,
        string expectedMessage,
        string? expectedWhy,
        string? expectedFix)
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.BadRequest, responseBody);
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        var email = CodeSendIn(
            new MessageCategory { Key = "newsletter", Type = MessageCategoryType.Marketing, Name = "Newsletter" });

        var error = await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.SendAsync(email));

        Assert.Equal(expectedCode, error.Code);
        Assert.Equal("validation_error", error.Name);
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal(expectedField, error.Field);
        Assert.Equal(expectedMessage, error.Message);
        Assert.Equal(expectedWhy, error.Why);
        Assert.Equal(expectedFix, error.Fix);
        var request = Assert.Single(harness.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(SendUrl, request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        AssertObjectKeys(root, CategorizedCodeSendKeys);
        AssertCodeSendFields(root, email);
        AssertCategoryObject(root.GetProperty("category"), "newsletter", "marketing", "Newsletter");
    }

    [Theory]
    [MemberData(nameof(TransactionalCategoryCompatibilityRefusals))]
    public async Task SurfacesCategoryCompatibilityRefusalsAfterAttachmentUploadWithoutRetrying(
        string responseBody,
        string expectedCode,
        string expectedField,
        string expectedMessage,
        string? expectedWhy,
        string? expectedFix)
    {
        using var posthog = new FakePostHogEmails();
        using var transport = new CategorizedSendHandler(posthog, holdUpload: false);
        transport.EnqueueSendResponse(HttpStatusCode.BadRequest, responseBody);
        transport.EnqueueSendResponse(
            HttpStatusCode.Accepted,
            """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");
        await using var harness = new EmailHarness(posthog: transport);

        var error = await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.SendAsync(
            new SendCodeEmailOptions
            {
                To = "a@example.com",
                TemplateKey = "k",
                Subject = "s",
                Html = "<p>Receipt</p>",
                Category = new MessageCategory
                {
                    Key = "receipts",
                    Type = MessageCategoryType.Transactional,
                    Name = "Receipts",
                },
                Attachments =
                [
                    new EmailAttachment
                    {
                        Filename = "invoice.pdf",
                        Content = BinaryPdfBytes,
                        ContentType = "application/pdf",
                    },
                ],
            }));

        Assert.Equal(expectedCode, error.Code);
        Assert.Equal("validation_error", error.Name);
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Equal(expectedField, error.Field);
        Assert.Equal(expectedMessage, error.Message);
        Assert.Equal(expectedWhy, error.Why);
        Assert.Equal(expectedFix, error.Fix);
        Assert.Equal(1, transport.StorageWritesAtSend);
        Assert.Equal(1, posthog.StorageWrites);
        Assert.Single(transport.Requests, request => request.Url == SendUrl);
        AssertSignedPdfUploadBeforeSend(
            posthog,
            transport,
            "<p>Receipt</p>",
            "invoice.pdf",
            "receipts",
            "transactional",
            "Receipts");
    }

    [Fact]
    public async Task StopsCategorizedSendWithAttachmentAtNotFoundWhenServerLacksUploadRoute()
    {
        using var posthog = new FakePostHogEmails();
        posthog.RunAnOlderVersion();
        using var transport = new CategorizedSendHandler(posthog, holdUpload: false);
        await using var harness = new EmailHarness(posthog: transport);

        var error = await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.SendAsync(
            new SendCodeEmailOptions
            {
                To = "a@example.com",
                TemplateKey = "k",
                Subject = "s",
                Html = "<p>Receipt</p>",
                Category = new MessageCategory
                {
                    Key = "receipts",
                    Type = MessageCategoryType.Transactional,
                    Name = "Receipts",
                },
                Attachments =
                [
                    new EmailAttachment
                    {
                        Filename = "invoice.pdf",
                        Content = BinaryPdfBytes,
                        ContentType = "application/pdf",
                    },
                ],
            }));

        Assert.Equal("not_found", error.Code);
        Assert.Equal("not_found", error.Name);
        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Equal("attachments", error.Field);
        Assert.Equal(0, posthog.StorageWrites);
        Assert.Null(posthog.StoredContent(BinaryPdfSha256));
        var request = Assert.Single(transport.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://us.posthog.com/api/messaging/attachments/uploads/", request.Url);
    }

    [Fact]
    public async Task SendsEmptyCopyAndAttachmentListsToAnOlderPostHog()
    {
        var posthog = new FakePostHogEmails();
        posthog.RunAnOlderVersion();
        await using var harness = new EmailHarness(posthog: posthog);

        await harness.Client.Emails.SendAsync(new SendCodeEmailOptions
        {
            To = "user@example.com",
            TemplateKey = "receipt",
            Subject = "Your receipt",
            Text = "Thanks!",
            Cc = [],
            Bcc = [],
            Attachments = [],
        });

        var email = Assert.Single(posthog.Delivered);
        Assert.Equal("user@example.com", email.To);
        Assert.Equal("Thanks!", email.Text);
    }

    public static TheoryData<EmailTracking, string> TrackingSends => new()
    {
        { true, "true" },
        { false, "false" },
        { EmailTracking.All, "true" },
        { EmailTracking.None, "false" },
        { new EmailTracking { Clicks = true }, """{"clicks":true}""" },
        { new EmailTracking { Opens = false, Engagement = false }, """{"opens":false,"engagement":false}""" },
        { new EmailTracking(), "{}" },
    };

    [Theory]
    [MemberData(nameof(TrackingSends))]
    public async Task SendsTrackingOnlyWhenSet(EmailTracking tracking, string sent)
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        await harness.Client.Emails.SendAsync(new SendCodeEmailOptions
        {
            To = "user@example.com",
            TemplateKey = "receipt_2",
            Subject = "Your receipt",
            Text = "Thanks!",
            Tracking = tracking,
        });

        Assert.Equal(
            $$"""{"template_key":"receipt_2","to":"user@example.com","subject":"Your receipt","text":"Thanks!","tracking":{{sent}}}""",
            Assert.Single(harness.Requests).Body);
    }

    [Fact]
    public async Task SendsCaptureEngagementNextToTrackingWhenBothAreSet()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        await harness.Client.Emails.SendAsync(new SendCodeEmailOptions
        {
            To = "user@example.com",
            TemplateKey = "receipt_2",
            Subject = "Your receipt",
            Text = "Thanks!",
#pragma warning disable CS0618 // CaptureEngagement is deprecated but still sent when set.
            CaptureEngagement = true,
#pragma warning restore CS0618
            Tracking = true,
        });

        Assert.Equal(
            """{"template_key":"receipt_2","to":"user@example.com","subject":"Your receipt","text":"Thanks!","capture_engagement":true,"tracking":true}""",
            Assert.Single(harness.Requests).Body);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SendsSensitiveNextToTheTrackingTheCallerSet(bool sensitive)
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        await harness.Client.Emails.SendAsync(new SendCodeEmailOptions
        {
            To = "user@example.com",
            TemplateKey = "receipt_2",
            Subject = "Your receipt",
            Text = "Thanks!",
            Sensitive = sensitive,
            Tracking = new EmailTracking { Clicks = false },
        });

        Assert.Equal(
            $$$"""{"template_key":"receipt_2","to":"user@example.com","subject":"Your receipt","text":"Thanks!","sensitive":{{{(sensitive ? "true" : "false")}}},"tracking":{"clicks":false}}""",
            Assert.Single(harness.Requests).Body);
    }

    public static TheoryData<string, SendCodeEmailOptions> InvalidCodeSends => new()
    {
        { "Cc", new SendCodeEmailOptions { To = "a@example.com", TemplateKey = "k", Subject = "s", Html = "h", Cc = [" "] } },
        { "Bcc", new SendCodeEmailOptions { To = "a@example.com", TemplateKey = "k", Subject = "s", Html = "h", Bcc = [null!] } },
        { "Attachments", new SendCodeEmailOptions { To = "a@example.com", TemplateKey = "k", Subject = "s", Html = "h", Attachments = [null!] } },
        { "Filename", new SendCodeEmailOptions { To = "a@example.com", TemplateKey = "k", Subject = "s", Html = "h", Attachments = [new EmailAttachment { Filename = " ", Content = new byte[] { 1 } }] } },
        { "Filename", new SendCodeEmailOptions { To = "a@example.com", TemplateKey = "k", Subject = "s", Html = "h", Attachments = [new EmailAttachment { Filename = null!, Content = new byte[] { 1 } }] } },
        { "Content", new SendCodeEmailOptions { To = "a@example.com", TemplateKey = "k", Subject = "s", Html = "h", Attachments = [new EmailAttachment { Filename = "empty.csv", Content = Array.Empty<byte>() }] } },
        { "TemplateKey", new SendCodeEmailOptions { To = "a@example.com", TemplateKey = "password reset", Subject = "s", Html = "h" } },
        { "TemplateKey", new SendCodeEmailOptions { To = "a@example.com", TemplateKey = "", Subject = "s", Html = "h" } },
        { "TemplateKey", new SendCodeEmailOptions { To = "a@example.com", TemplateKey = new string('k', 401), Subject = "s", Html = "h" } },
        { "Subject", new SendCodeEmailOptions { To = "a@example.com", TemplateKey = "k", Subject = null!, Html = "h" } },
        { "Html, Text or both", new SendCodeEmailOptions { To = "a@example.com", TemplateKey = "k", Subject = "s" } },
        { "ReplyTo", new SendCodeEmailOptions { To = "a@example.com", TemplateKey = "k", Subject = "s", Html = "h", ReplyTo = [" "] } },
        { "Category.Key", CodeSendIn(new MessageCategory { Key = " ", Type = MessageCategoryType.Marketing, Name = "News" }) },
        { "Category.Key", CodeSendIn(new MessageCategory { Key = null!, Type = MessageCategoryType.Marketing, Name = "News" }) },
        { "Category.Name", CodeSendIn(new MessageCategory { Key = "news", Type = MessageCategoryType.Marketing, Name = "" }) },
        { "Category.Type", CodeSendIn(new MessageCategory { Key = "news", Type = (MessageCategoryType)7, Name = "News" }) },
    };

    static SendCodeEmailOptions CodeSendIn(MessageCategory? category)
        => new() { To = "a@example.com", TemplateKey = "k", Subject = "s", Html = "<a href=\"{{ unsubscribe_url }}\">Unsubscribe</a>", Category = category };

    [Theory]
    [MemberData(nameof(InvalidCodeSends))]
    public async Task RefusesInvalidContentBeforeAnyRequest(string named, SendCodeEmailOptions email)
    {
        await using var harness = new EmailHarness();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => harness.Client.Emails.SendAsync(email));

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Requests);
    }
}

public class TheSendAsyncMethodWithAttachments
{
    const string PdfSha256 = "315d429b7714cedb6ad04ac31240145257692630457f3c88253c5beceac76027";
    const int LargestFileBesideOneSubjectWord = 29_182_821;
    const int FileBesideLongHtml = 29_170_000;
    static readonly string LongHtml = new('x', 10_000);
    static readonly byte[] Bytes = [0x00, 0x25, 0x50, 0x44, 0x46, 0xFF];

    static SendCodeEmailOptions Invoice(params EmailAttachment[] attachments) => new()
    {
        To = "user@example.com",
        TemplateKey = "invoice",
        Subject = "Your invoice",
        Html = "<p>Thanks!</p>",
        Attachments = attachments,
    };

    static EmailAttachment Pdf() => new()
    {
        Filename = "invoice.pdf",
        Content = Bytes.AsMemory(1, 4),
        ContentType = "application/pdf",
    };

    static EmailAttachment Logo() => new() { Filename = "logo.png", Content = Bytes, ContentId = "logo" };

    [Fact]
    public async Task UploadsTheFilesPostHogLacksThenSendsThemWithCopies()
    {
        var posthog = new FakePostHogEmails();
        posthog.Store(Bytes);
        await using var harness = new EmailHarness(options => options.MigratedFrom = "resend", posthog);

        var result = await harness.Client.Emails.SendAsync(new SendCodeEmailOptions
        {
            To = "user@example.com",
            TemplateKey = "invoice",
            Subject = "Your invoice",
            Html = "<img src=\"cid:logo\">",
            Cc = ["finance@example.com"],
            Bcc = ["audit@example.com", "legal@example.com"],
            Attachments = [Pdf(), Logo()],
        });

        Assert.Equal(FakePostHogEmails.MessageId, result.Id);
        var email = Assert.Single(posthog.Delivered);
        Assert.Equal(["finance@example.com"], email.Cc);
        Assert.Equal(["audit@example.com", "legal@example.com"], email.Bcc);
        Assert.Equal("<img src=\"cid:logo\">", email.Html);
        Assert.Collection(
            email.Attachments,
            pdf =>
            {
                Assert.Equal("invoice.pdf", pdf.Filename);
                Assert.Equal("application/pdf", pdf.ContentType);
                Assert.Null(pdf.ContentId);
                Assert.Equal(Bytes[1..5], pdf.Content);
            },
            logo =>
            {
                Assert.Equal("logo.png", logo.Filename);
                Assert.Null(logo.ContentType);
                Assert.Equal("logo", logo.ContentId);
                Assert.Equal(Bytes, logo.Content);
            });
        Assert.Equal(1, posthog.StorageWrites);
    }

    [Fact]
    public async Task UploadsAFileAttachedTwiceOnce()
    {
        var posthog = new FakePostHogEmails();
        await using var harness = new EmailHarness(posthog: posthog);

        await harness.Client.Emails.SendAsync(
            Invoice(Logo(), new EmailAttachment { Filename = "copy.png", Content = Bytes.ToArray() }));

        var email = Assert.Single(posthog.Delivered);
        Assert.Equal(["logo.png", "copy.png"], email.Attachments.Select(file => file.Filename));
        Assert.All(email.Attachments, file => Assert.Equal(Bytes, file.Content));
        Assert.Equal(1, posthog.StorageWrites);
    }

    [Fact]
    public async Task SendsNothingWhenAnUploadUrlExpired()
    {
        var posthog = new FakePostHogEmails { UploadUrlsExpired = true };
        posthog.Store(Bytes);
        await using var harness = new EmailHarness(posthog: posthog);

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(Invoice(Logo(), Pdf())));

        Assert.Empty(posthog.Delivered);
        Assert.Equal("attachment_upload_failed", error.Code);
        Assert.Equal("application_error", error.Name);
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Equal("attachments__1", error.Field);
        Assert.Contains("invoice.pdf", error.Message, StringComparison.Ordinal);
        Assert.Contains("no email was sent", error.Message, StringComparison.Ordinal);
        Assert.Equal("The file storage answered HTTP 403 AccessDenied: Request has expired", error.Why);
        Assert.NotNull(error.Fix);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task QuotesAPlainTextStorageRefusalAsAnApplicationError(HttpStatusCode status)
    {
        var posthog = new FakePostHogEmails();
        posthog.FailStorage(status, "Refused by proxy", contentType: "text/plain");
        await using var harness = new EmailHarness(posthog: posthog);

        var error = await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.SendAsync(Invoice(Pdf())));

        Assert.Empty(posthog.Delivered);
        Assert.Equal($"The file storage answered HTTP {(int)status}: Refused by proxy", error.Why);
        Assert.Equal(status, error.StatusCode);
        Assert.Equal("application_error", error.Name);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task RetriesAFailingStorageThenSends(HttpStatusCode status)
    {
        var posthog = new FakePostHogEmails();
        posthog.FailStorage(status, "<Error><Code>SlowDown</Code><Message>Please reduce your request rate.</Message></Error>");
        await using var harness = new EmailHarness(posthog: posthog);

        await harness.Client.Emails.SendAsync(Invoice(Pdf()));

        var file = Assert.Single(Assert.Single(posthog.Delivered).Attachments);
        Assert.Equal(Bytes[1..5], file.Content);
    }

    [Fact]
    public async Task ReportsAStorageThatKeepsFailingAsUnavailable()
    {
        var posthog = new FakePostHogEmails();
        posthog.FailStorage(HttpStatusCode.InternalServerError, "<Error><Code>InternalError</Code><Message>Try again.</Message></Error>");
        posthog.FailStorage(HttpStatusCode.InternalServerError, "<Error><Code>InternalError</Code><Message>Try again.</Message></Error>");
        await using var harness = new EmailHarness(options => options.EmailRequestMaxRetries = 1, posthog);

        var error = await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.SendAsync(Invoice(Pdf())));

        Assert.Empty(posthog.Delivered);
        Assert.Equal("attachment_upload_failed", error.Code);
        Assert.Equal("service_unavailable", error.Name);
    }

    [Fact]
    public async Task KeepsTheAppsDefaultAuthorizationOffUploads()
    {
        var posthog = new FakePostHogEmails();
        await using var harness = new EmailHarness(posthog: posthog);
        harness.HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "cHJveHk6c2VjcmV0");

        await harness.Client.Emails.SendAsync(Invoice(Pdf()));

        Assert.Equal(Bytes[1..5], Assert.Single(Assert.Single(posthog.Delivered).Attachments).Content);
        Assert.Equal(1, posthog.StorageWrites);
    }

    [Fact]
    public async Task UploadsNothingToAnHttpUrlUnlessAllowed()
    {
        var posthog = new FakePostHogEmails { StorageOrigin = "http://files.example.com" };
        await using var harness = new EmailHarness(posthog: posthog);

        var error = await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.SendAsync(Invoice(Pdf())));

        Assert.Empty(posthog.Delivered);
        Assert.Equal(0, posthog.StorageWrites);
        Assert.Equal("insecure_app_host", error.Code);
        Assert.Equal("attachments__0", error.Field);
        Assert.Contains("invoice.pdf", error.Message, StringComparison.Ordinal);
        Assert.Contains("file storage over https", error.Fix, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "files.example.com",
            string.Join("\n", error.Message, error.Why, error.Fix, error.ToString()),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadsToAnHttpUrlWithTheOptIn()
    {
        var posthog = new FakePostHogEmails { StorageOrigin = "http://files.example.com" };
        await using var harness = new EmailHarness(options => options.AllowInsecureAppHost = true, posthog);

        await harness.Client.Emails.SendAsync(Invoice(Pdf()));

        var file = Assert.Single(Assert.Single(posthog.Delivered).Attachments);
        Assert.Equal(Bytes[1..5], file.Content);
        Assert.Equal(1, posthog.StorageWrites);
    }

    [Fact]
    public async Task GivesUpOnAStalledUploadAtTheAppsHttpClientTimeout()
    {
        var posthog = new FakePostHogEmails();
        posthog.DelayStorage(StalledUpload);
        await using var harness = new EmailHarness(options => options.EmailRequestMaxRetries = 0, posthog);
        harness.HttpClient.Timeout = TimeSpan.FromMilliseconds(200);

        var send = harness.Client.Emails.SendAsync(Invoice(Pdf()));

        var error = await Assert.ThrowsAsync<EmailException>(() => send.WaitAsync(TimeSpan.FromSeconds(3), TimeProvider.System));
        Assert.Empty(posthog.Delivered);
        Assert.Equal(0, posthog.StorageWrites);
        Assert.Equal("network_error", error.Code);
        Assert.Equal("Uploading invoice.pdf timed out, so no email was sent.", error.Message);
    }

    [Fact]
    public async Task LetsTheCallerCancelAStalledUpload()
    {
        var posthog = new FakePostHogEmails();
        posthog.DelayStorage(StalledUpload);
        await using var harness = new EmailHarness(options => options.EmailRequestMaxRetries = 0, posthog);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var send = harness.Client.Emails.SendAsync(Invoice(Pdf()), cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(TimeSpan.FromSeconds(3), TimeProvider.System));
        Assert.Empty(posthog.Delivered);
        Assert.Equal(0, posthog.StorageWrites);
    }

    [Fact]
    public async Task LetsAnUploadOutlastTheEmailRequestTimeout()
    {
        var posthog = new FakePostHogEmails();
        posthog.DelayStorage(TimeSpan.FromMilliseconds(500));
        await using var harness = new EmailHarness(
            options =>
            {
                options.EmailRequestTimeout = TimeSpan.FromMilliseconds(100);
                options.EmailRequestMaxRetries = 0;
            },
            posthog);

        await harness.Client.Emails.SendAsync(Invoice(Pdf()));

        Assert.Equal(Bytes[1..5], Assert.Single(Assert.Single(posthog.Delivered).Attachments).Content);
        Assert.Equal(1, posthog.StorageWrites);
    }

    /// <summary>
    /// Longer than any test waits for a send, yet short enough that a client that never gives up still lets the
    /// harness close.
    /// </summary>
    static readonly TimeSpan StalledUpload = TimeSpan.FromSeconds(6);

    [Fact]
    public async Task RetriesAnUnavailableUploadRouteWithoutRetryingADailyCap()
    {
        var posthog = new FakePostHogEmails();
        posthog.FailUploadRoute(HttpStatusCode.ServiceUnavailable, """{"code": "delivery_unavailable"}""");
        posthog.FailUploadRoute((HttpStatusCode)429, """{"code": "upload_quota_exceeded", "detail": "This project uploaded as many file bytes as it can today."}""");
        await using var harness = new EmailHarness(posthog: posthog);

        var error = await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.SendAsync(Invoice(Pdf())));

        Assert.Empty(posthog.Delivered);
        Assert.Equal("upload_quota_exceeded", error.Code);
    }

    [Fact]
    public async Task SendsNothingWhenThisProjectCantSendFiles()
    {
        var posthog = new FakePostHogEmails { FilesAllowed = false };
        await using var harness = new EmailHarness(posthog: posthog);

        var error = await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.SendAsync(Invoice(Pdf())));

        Assert.Empty(posthog.Delivered);
        Assert.Equal("field_not_accepted", error.Code);
        Assert.Equal("validation_error", error.Name);
        Assert.Equal("files", error.Field);
    }

    [Fact]
    public async Task ReportsAnOlderPostHogsMissingUploadRouteAsNotFound()
    {
        var posthog = new FakePostHogEmails();
        posthog.RunAnOlderVersion();
        await using var harness = new EmailHarness(posthog: posthog);

        var error = await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.SendAsync(Invoice(Pdf())));

        Assert.Empty(posthog.Delivered);
        Assert.Equal("not_found", error.Code);
        Assert.Equal("not_found", error.Name);
        Assert.Equal("attachments", error.Field);
        Assert.Equal("Endpoint not found.", error.Message);
        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
    }

    [Theory]
    [InlineData("""{"type": "invalid_request", "code": "not_found", "detail": "Sending email from code isn't enabled for this project.", "attr": null}""", "Sending email from code isn't enabled for this project.")]
    [InlineData("<h1>Not Found</h1>", "PostHog answered HTTP 404.")]
    public async Task ReportsAnyUploadRoute404AsNotFound(string body, string message)
    {
        var posthog = new FakePostHogEmails();
        posthog.FailUploadRoute(HttpStatusCode.NotFound, body, contentType: body.StartsWith('<') ? "text/html" : "application/json");
        await using var harness = new EmailHarness(posthog: posthog);

        var error = await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.SendAsync(Invoice(Pdf())));

        Assert.Empty(posthog.Delivered);
        Assert.Equal("not_found", error.Code);
        Assert.Equal("attachments", error.Field);
        Assert.Equal(message, error.Message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"files": []}""")]
    [InlineData("""{"files": [{"sha256": "315d429b7714cedb6ad04ac31240145257692630457f3c88253c5beceac76027", "status": "upload"}]}""")]
    [InlineData("""{"files": [{"sha256": "315d429b7714cedb6ad04ac31240145257692630457f3c88253c5beceac76027", "status": "upload", "url": "/relative", "method": "PUT", "headers": {}}]}""")]
    [InlineData("""{"files": [{"sha256": "315d429b7714cedb6ad04ac31240145257692630457f3c88253c5beceac76027", "status": "upload", "url": "https://files.example.com/a", "method": "PUT ", "headers": {}}]}""")]
    [InlineData("""{"files": [{"sha256": "315d429b7714cedb6ad04ac31240145257692630457f3c88253c5beceac76027", "status": "upload", "url": "https://files.example.com/a", "method": " ", "headers": {}}]}""")]
    [InlineData("""{"files": [{"sha256": "315d429b7714cedb6ad04ac31240145257692630457f3c88253c5beceac76027", "status": "archived"}]}""")]
    public async Task SendsNothingWhenAFileHasNoUsableAnswer(string body)
    {
        var posthog = new FakePostHogEmails();
        posthog.FailUploadRoute(HttpStatusCode.OK, body);
        await using var harness = new EmailHarness(posthog: posthog);

        var error = await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.SendAsync(Invoice(Pdf())));

        Assert.Empty(posthog.Delivered);
        Assert.Equal(0, posthog.StorageWrites);
        Assert.Equal("api_error", error.Code);
        Assert.Equal("attachments__0", error.Field);
        Assert.Contains(PdfSha256, error.Why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesMoreThanTenFilesBeforeUploadingAny()
    {
        var posthog = new FakePostHogEmails();
        await using var harness = new EmailHarness(posthog: posthog);

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(Invoice(Enumerable.Range(0, 11).Select(_ => Logo()).ToArray())));

        Assert.Empty(posthog.Delivered);
        Assert.Equal(0, posthog.StorageWrites);
        Assert.Equal("too_many_attachments", error.Code);
        Assert.Equal("validation_error", error.Name);
        Assert.Equal("attachments", error.Field);
        Assert.Null(error.StatusCode);
        Assert.NotNull(error.Why);
        Assert.NotNull(error.Fix);
    }

    public static TheoryData<string, int, string, string, string?> SendsThatFit => new()
    {
        { "a one-word subject", LargestFileBesideOneSubjectWord, "Your archive", "", null },
        { "a subject of 42 ASCII bytes", LargestFileBesideOneSubjectWord, new string('a', 42), "", null },
        { "a subject of 21 two-byte characters", LargestFileBesideOneSubjectWord, new string('é', 21), "", null },
        { "an empty subject", 29_182_875, "", "", null },
        { "HTML with its own text", FileBesideLongHtml, "Your archive", LongHtml, "x" },
    };

    [Theory]
    [MemberData(nameof(SendsThatFit))]
    public async Task SendsTheLargestFileThatFitsTheMessageEstimate(
        string beside,
        int size,
        string subject,
        string html,
        string? text)
    {
        var posthog = new FakePostHogEmails();
        var content = new byte[size];
        posthog.Store(content);
        await using var harness = new EmailHarness(posthog: posthog);

        await harness.Client.Emails.SendAsync(Archive(content, subject, html, text));

        var file = Assert.Single(Assert.Single(posthog.Delivered).Attachments);
        Assert.True(file.Content.Length == size, $"The file beside {beside} was sent.");
    }

    public static TheoryData<string, int, string, string, string?> SendsOverTheEstimate => new()
    {
        { "one more file byte", LargestFileBesideOneSubjectWord + 1, "Your archive", "", null },
        { "a subject of 43 ASCII bytes", LargestFileBesideOneSubjectWord, new string('a', 43), "", null },
        { "a subject of 22 two-byte characters", LargestFileBesideOneSubjectWord, new string('é', 22), "", null },
        { "one HTML byte", LargestFileBesideOneSubjectWord, "Your archive", "x", null },
        { "one text byte", LargestFileBesideOneSubjectWord, "Your archive", "", "x" },
        { "one more file byte beside an empty subject", 29_182_876, "", "", null },
        { "HTML whose derived text PostHog sends too", FileBesideLongHtml, "Your archive", LongHtml, null },
        { "HTML with an empty text", FileBesideLongHtml, "Your archive", LongHtml, "" },
    };

    [Theory]
    [MemberData(nameof(SendsOverTheEstimate))]
    public async Task RefusesAMessageOverTheEstimateBeforeUploadingAny(string named, int size, string subject, string html, string? text)
    {
        var posthog = new FakePostHogEmails();
        await using var harness = new EmailHarness(posthog: posthog);

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(Archive(new byte[size], subject, html, text)));

        Assert.Empty(posthog.Delivered);
        Assert.True(posthog.StorageWrites == 0, $"Nothing was uploaded for {named}.");
        Assert.Equal("message_too_large", error.Code);
        Assert.Equal("validation_error", error.Name);
        Assert.Equal("attachments", error.Field);
        Assert.Contains("40 MB", error.Why, StringComparison.Ordinal);
        Assert.Contains("about 29 MB of files", error.Fix, StringComparison.Ordinal);
    }

    static SendCodeEmailOptions Archive(byte[] content, string subject, string html, string? text) => new()
    {
        To = "user@example.com",
        TemplateKey = "archive",
        Subject = subject,
        Html = html,
        Text = text,
        Attachments = [new EmailAttachment { Filename = "archive.zip", Content = content }],
    };
}

public class TheSendAsyncMethodArguments
{
    public static TheoryData<string, SendEmailOptions> InvalidSends => new()
    {
        { "To", new SendTemplateEmailOptions { To = " ", TemplateId = Guid.NewGuid() } },
        { "To", new SendTemplateEmailOptions { To = null!, TemplateId = Guid.NewGuid() } },
        { "TemplateId", new SendTemplateEmailOptions { To = "a@example.com", TemplateId = Guid.Empty } },
        { "DistinctId", new SendTemplateEmailOptions { To = "a@example.com", TemplateId = Guid.NewGuid(), DistinctId = "" } },
        { "IdempotencyKey", new SendTemplateEmailOptions { To = "a@example.com", TemplateId = Guid.NewGuid(), IdempotencyKey = "has space" } },
        { "IdempotencyKey", new SendTemplateEmailOptions { To = "a@example.com", TemplateId = Guid.NewGuid(), IdempotencyKey = new string('k', 256) } },
        { "IdempotencyKey", new SendTemplateEmailOptions { To = "a@example.com", TemplateId = Guid.NewGuid(), IdempotencyKey = "" } },
        { "MaxRetries", new SendTemplateEmailOptions { To = "a@example.com", TemplateId = Guid.NewGuid(), MaxRetries = -1 } },
    };

    [Theory]
    [MemberData(nameof(InvalidSends))]
    public async Task RefusesInvalidOptionsBeforeAnyRequest(string named, SendEmailOptions email)
    {
        await using var harness = new EmailHarness();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => harness.Client.Emails.SendAsync(email));

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
        Assert.Equal("email", error.ParamName);
        Assert.Empty(harness.Requests);
    }

    [Fact]
    public async Task RefusesANullSend()
    {
        await using var harness = new EmailHarness();

        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Client.Emails.SendAsync(null!));

        Assert.Empty(harness.Requests);
    }

    [Fact]
    public async Task SendsTheCallersIdempotencyKey()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        await harness.Client.Emails.SendAsync(EmailHarness.TemplateSend(idempotencyKey: "welcome:user_123"));

        Assert.Equal("welcome:user_123", Assert.Single(harness.Requests).Header("Idempotency-Key"));
    }
}

public class TheSendAsyncMethodRetries
{
    [Fact]
    public async Task RetriesWithTheSameGeneratedKey()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.ServiceUnavailable, """{"code": "service_unavailable"}""");
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        var result = await harness.Client.Emails.SendAsync(EmailHarness.TemplateSend());

        Assert.Equal("01a18356-c56c-0001-e494-7666bc915188", result.Id);
        Assert.Equal(2, harness.Requests.Count);
        var key = harness.Requests[0].Header("Idempotency-Key");
        Assert.Equal(key, harness.Requests[1].Header("Idempotency-Key"));
        Assert.True(Guid.TryParseExact(key, "D", out var uuid));
        Assert.Equal('4', uuid.ToString("D")[14]);
        var delay = Assert.Single(harness.Time.Delays);
        Assert.InRange(delay, TimeSpan.FromMilliseconds(800), TimeSpan.FromMilliseconds(1200));
    }

    [Fact]
    public async Task MakesThreeAttemptsByDefaultWithGrowingBackoff()
    {
        await using var harness = new EmailHarness();
        for (var i = 0; i < 4; i++)
        {
            harness.Respond(HttpStatusCode.ServiceUnavailable, "<html>Service Unavailable</html>", "text/html");
        }

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(EmailHarness.TemplateSend()));

        Assert.Equal(3, harness.Requests.Count);
        Assert.Equal("api_error", error.Code);
        Assert.Equal("service_unavailable", error.Name);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Equal(2, harness.Time.Delays.Count);
        Assert.InRange(harness.Time.Delays[0], TimeSpan.FromMilliseconds(800), TimeSpan.FromMilliseconds(1200));
        Assert.InRange(harness.Time.Delays[1], TimeSpan.FromMilliseconds(1600), TimeSpan.FromMilliseconds(2400));
    }

    [Fact]
    public async Task APerSendLimitOfZeroMakesOneAttempt()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.ServiceUnavailable, """{"code": "service_unavailable"}""");

        await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(EmailHarness.TemplateSend(maxRetries: 0)));

        Assert.Single(harness.Requests);
    }

    [Fact]
    public async Task TheClientLimitAppliesWhenTheSendSetsNone()
    {
        await using var harness = new EmailHarness(options => options.EmailRequestMaxRetries = 0);
        harness.Respond(HttpStatusCode.ServiceUnavailable, """{"code": "service_unavailable"}""");

        await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(EmailHarness.TemplateSend()));

        Assert.Single(harness.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "idempotency_key_in_flight")]
    [InlineData((HttpStatusCode)429, "throttled")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "service_unavailable")]
    public async Task RetriesTransientAnswers(HttpStatusCode status, string code)
    {
        await using var harness = new EmailHarness();
        harness.Respond(status, $$"""{"code": "{{code}}"}""");
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        var result = await harness.Client.Emails.SendAsync(EmailHarness.TemplateSend());

        Assert.Equal("01a18356-c56c-0001-e494-7666bc915188", result.Id);
        Assert.Equal(2, harness.Requests.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "invalid_recipient", "validation_error")]
    [InlineData(HttpStatusCode.Unauthorized, "authentication_failed", "missing_api_key")]
    [InlineData(HttpStatusCode.Forbidden, "permission_denied", "invalid_permission")]
    [InlineData(HttpStatusCode.NotFound, "not_found", "not_found")]
    [InlineData(HttpStatusCode.Conflict, "template_outdated", "validation_error")]
    [InlineData(HttpStatusCode.Conflict, "upload_conflict", "concurrent_idempotent_requests")]
    [InlineData((HttpStatusCode)422, "idempotency_key_reused", "invalid_idempotent_request")]
    [InlineData((HttpStatusCode)422, "sending_paused", "validation_error")]
    [InlineData((HttpStatusCode)429, "quota_limited", "monthly_quota_exceeded")]
    [InlineData(HttpStatusCode.InternalServerError, "server_error", "application_error")]
    public async Task DoesNotRetryOtherAnswers(HttpStatusCode status, string code, string name)
    {
        await using var harness = new EmailHarness();
        harness.Respond(status, $$"""{"code": "{{code}}"}""");
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(EmailHarness.TemplateSend()));

        Assert.Single(harness.Requests);
        Assert.Equal(code, error.Code);
        Assert.Equal(name, error.Name);
        Assert.Equal(status, error.StatusCode);
    }

    [Fact]
    public async Task WaitsForALongerRetryAfter()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.ServiceUnavailable, """{"code": "service_unavailable"}""", retryAfter: TimeSpan.FromSeconds(5));
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        await harness.Client.Emails.SendAsync(EmailHarness.TemplateSend());

        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(harness.Time.Delays));
    }

    [Fact]
    public async Task FailsAtOnceWhenRetryAfterIsOverThirtySeconds()
    {
        await using var harness = new EmailHarness();
        harness.Respond((HttpStatusCode)429, """{"code": "throttled", "detail": "Slow down."}""", retryAfter: TimeSpan.FromSeconds(60));

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(EmailHarness.TemplateSend()));

        Assert.Single(harness.Requests);
        Assert.Equal("throttled", error.Code);
        Assert.Equal("rate_limit_exceeded", error.Name);
        Assert.Equal(TimeSpan.FromSeconds(60), error.RetryAfter);
        Assert.Empty(harness.Time.Delays);
    }

    [Fact]
    public async Task RetriesANetworkErrorThenReportsIt()
    {
        await using var harness = new EmailHarness(options => options.EmailRequestMaxRetries = 1);
        harness.Fail(new HttpRequestException("connection refused"));
        harness.Fail(new HttpRequestException("connection refused"));

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(EmailHarness.TemplateSend()));

        Assert.Equal(2, harness.Requests.Count);
        Assert.Equal("network_error", error.Code);
        Assert.Equal("service_unavailable", error.Name);
        Assert.Null(error.StatusCode);
        Assert.IsType<HttpRequestException>(error.InnerException);
    }

    [Fact]
    public async Task TreatsATimedOutAttemptAsANetworkError()
    {
        await using var harness = new EmailHarness(options =>
        {
            options.EmailRequestTimeout = TimeSpan.FromMilliseconds(50);
            options.EmailRequestMaxRetries = 0;
        });
        harness.Hang();

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(EmailHarness.TemplateSend()));

        Assert.Equal("network_error", error.Code);
        Assert.Contains("timed out", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LetsTheCallerCancel()
    {
        await using var harness = new EmailHarness();
        harness.Hang();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Client.Emails.SendAsync(EmailHarness.TemplateSend(), cancellation.Token));

        Assert.Single(harness.Requests);
    }
}

public class TheSendAsyncMethodAnswers
{
    [Fact]
    public async Task KeepsPostHogsErrorFields()
    {
        await using var harness = new EmailHarness();
        harness.Respond(
            (HttpStatusCode)422,
            """{"type": "validation_error", "code": "recipient_suppressed", "detail": "Nope.", "attr": "to", "why": "It bounced.", "fix": "Remove it.", "docs_url": "https://posthog.com/docs/x"}""");

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(EmailHarness.TemplateSend()));

        Assert.Equal("recipient_suppressed", error.Code);
        Assert.Equal("validation_error", error.Name);
        Assert.Equal("Nope.", error.Message);
        Assert.Equal((HttpStatusCode)422, error.StatusCode);
        Assert.Equal("to", error.Field);
        Assert.Equal("It bounced.", error.Why);
        Assert.Equal("Remove it.", error.Fix);
        Assert.Equal(new Uri("https://posthog.com/docs/x"), error.DocsUrl);
        Assert.DoesNotContain("phs_secret", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"message_id": ""}""")]
    [InlineData("""{"message_id": 7}""")]
    [InlineData("accepted")]
    public async Task TreatsAnAcceptanceWithoutAMessageIdAsAnError(string body)
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.Accepted, body);

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(EmailHarness.TemplateSend()));

        Assert.Single(harness.Requests);
        Assert.Equal("api_error", error.Code);
        Assert.Equal(HttpStatusCode.Accepted, error.StatusCode);
    }
}

public class TheSendAsyncMethodClientChecks
{
    [Theory]
    [InlineData("https://eu.i.posthog.com", null, "https://eu.posthog.com")]
    [InlineData("https://eu.posthog.com/", null, "https://eu.posthog.com")]
    [InlineData("https://app.posthog.com", null, "https://us.posthog.com")]
    [InlineData("https://proxy.example.com", "https://posthog.example.com/", "https://posthog.example.com")]
    [InlineData("https://proxy.example.com", "https://us.i.posthog.com", "https://us.posthog.com")]
    public async Task SendsToTheAppHost(string host, string? appHost, string expected)
    {
        await using var harness = new EmailHarness(options =>
        {
            options.HostUrl = new Uri(host);
            options.AppHost = appHost is null ? null : new Uri(appHost);
        });
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        await harness.Client.Emails.SendAsync(EmailHarness.TemplateSend());

        Assert.Equal(expected + "/api/messaging/send/email/", Assert.Single(harness.Requests).Url);
    }

    [Fact]
    public async Task RefusesACustomHostWithoutAnAppHost()
    {
        await using var harness = new EmailHarness(options => options.HostUrl = new Uri("https://proxy.example.com"));

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(EmailHarness.TemplateSend()));

        Assert.Equal("missing_app_host", error.Code);
        Assert.Equal("application_error", error.Name);
        Assert.Null(error.StatusCode);
        Assert.Empty(harness.Requests);
    }

    [Theory]
    [InlineData("/relative")]
    [InlineData("ftp://posthog.example.com")]
    public async Task RefusesAnAppHostThatIsNotAnHttpUrl(string appHost)
    {
        await using var harness = new EmailHarness(options =>
        {
            options.HostUrl = new Uri("https://proxy.example.com");
            options.AppHost = new Uri(appHost, UriKind.RelativeOrAbsolute);
        });

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(EmailHarness.TemplateSend()));

        Assert.Equal("missing_app_host", error.Code);
        Assert.StartsWith("AppHost must be an absolute http or https URL", error.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Requests);
    }

    [Theory]
    [InlineData("http://posthog.example.com")]
    [InlineData("HTTP://localhost:8010")]
    public async Task RefusesAnHttpAppHostWithoutTheOptIn(string appHost)
    {
        await using var harness = new EmailHarness(options => options.AppHost = new Uri(appHost));

        var send = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(EmailHarness.TemplateSend()));
        var status = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.GetAsync("01a18356-c56c-0001-e494-7666bc915188"));

        Assert.Equal("insecure_app_host", send.Code);
        Assert.Equal("application_error", send.Name);
        Assert.Contains("no email was sent", send.Message, StringComparison.Ordinal);
        Assert.Equal("insecure_app_host", status.Code);
        Assert.Empty(harness.Requests);
    }

    [Fact]
    public async Task SendsToAnHttpAppHostWithTheOptIn()
    {
        await using var harness = new EmailHarness(options =>
        {
            options.AppHost = new Uri("http://localhost:8010");
            options.AllowInsecureAppHost = true;
        });
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        await harness.Client.Emails.SendAsync(EmailHarness.TemplateSend());

        Assert.Equal(
            "http://localhost:8010/api/messaging/send/email/",
            Assert.Single(harness.Requests).Url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task RefusesAClientWithoutASecretKey(string? secretKey)
    {
        await using var harness = new EmailHarness(options => options.SecretKey = secretKey);

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(EmailHarness.TemplateSend()));

        Assert.Equal("missing_secret_key", error.Code);
        Assert.Equal("missing_api_key", error.Name);
        Assert.Empty(harness.Requests);
    }

    [Fact]
    public async Task TrimsTheSecretKey()
    {
        await using var harness = new EmailHarness(options => options.SecretKey = " phs_secret \n");
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        await harness.Client.Emails.SendAsync(EmailHarness.TemplateSend());

        Assert.Equal("Bearer phs_secret", Assert.Single(harness.Requests).Header("Authorization"));
    }

    [Fact]
    public async Task RefusesAnInvalidMigratedFrom()
    {
        await using var harness = new EmailHarness(options => options.MigratedFrom = "re send");

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(EmailHarness.TemplateSend()));

        Assert.Equal("invalid_migrated_from", error.Code);
        Assert.Equal("validation_error", error.Name);
        Assert.Empty(harness.Requests);
    }

    [Fact]
    public async Task ADisabledClientSendsNothing()
    {
        await using var harness = new EmailHarness(options => options.Disabled = true);

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(EmailHarness.TemplateSend()));

        Assert.Equal("client_disabled", error.Code);
        Assert.Empty(harness.Requests);
    }

    [Fact]
    public async Task AShutDownClientSendsNothing()
    {
        var harness = new EmailHarness();
        await harness.DisposeAsync();

        var error = await Assert.ThrowsAsync<EmailException>(
            () => harness.Client.Emails.SendAsync(EmailHarness.TemplateSend()));

        Assert.Equal("client_closed", error.Code);
        Assert.Empty(harness.Requests);
    }

    [Fact]
    public async Task ShutdownWaitsForASendInItsRetryBackoff()
    {
        await using var harness = new EmailHarness();
        harness.Time.HoldDelays();
        harness.Respond(HttpStatusCode.ServiceUnavailable, """{"code": "service_unavailable"}""");
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        var send = harness.Client.Emails.SendAsync(EmailHarness.TemplateSend());
        await harness.Time.WaitForHeldDelayAsync();
        var shutdown = harness.Client.DisposeAsync().AsTask();
        await Task.Delay(50);
        Assert.False(shutdown.IsCompleted);

        harness.Time.ReleaseDelays();
        await Within(shutdown, TimeSpan.FromSeconds(5));

        Assert.Equal("01a18356-c56c-0001-e494-7666bc915188", (await send).Id);
        Assert.Equal(2, harness.Requests.Count);
    }

    [Fact]
    public async Task SynchronousDisposeOnABlockedContextWaitsForASendWithoutDeadlocking()
    {
        await using var harness = new EmailHarness();
        harness.Time.HoldDelays();
        harness.Respond(HttpStatusCode.ServiceUnavailable, """{"code": "service_unavailable"}""");
        harness.Respond(HttpStatusCode.Accepted, """{"message_id": "01a18356-c56c-0001-e494-7666bc915188"}""");

        var sendStarted = new TaskCompletionSource<Task<SendEmailResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var disposeNow = new ManualResetEventSlim();
        var dispose = Task.Run(() =>
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new BlockedContext());
            try
            {
                sendStarted.SetResult(harness.Client.Emails.SendAsync(EmailHarness.TemplateSend()));
                disposeNow.Wait();
                harness.Client.Dispose();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        });
        var send = await sendStarted.Task;
        await harness.Time.WaitForHeldDelayAsync();
        disposeNow.Set();
        await Task.Delay(50);
        Assert.False(dispose.IsCompleted);

        harness.Time.ReleaseDelays();
        await Within(dispose, TimeSpan.FromSeconds(5));

        Assert.Equal("01a18356-c56c-0001-e494-7666bc915188", (await send).Id);
    }

    sealed class BlockedContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
        }
    }

    static async Task Within(Task task, TimeSpan timeout)
    {
        if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
        {
            throw new TimeoutException("The task did not finish in time.");
        }

        await task;
    }
}

public class TheGetAsyncMethod
{
    const string MessageId = "01A18356-C56C-0001-E494-7666BC915188";

    [Fact]
    public async Task ReadsTheStatusRecord()
    {
        await using var harness = new EmailHarness(options => options.MigratedFrom = "resend");
        harness.Respond(
            HttpStatusCode.OK,
            """{"id": "01a18356-c56c-0001-e494-7666bc915188", "status": "bounced", "created_at": "2026-10-28T16:27:44.364000Z", "status_at": "2026-10-28T16:30:02.100000Z", "reason": "hard"}""");

        var status = await harness.Client.Emails.GetAsync(MessageId);

        Assert.Equal("01a18356-c56c-0001-e494-7666bc915188", status.Id);
        Assert.Equal("bounced", status.Status);
        Assert.Equal("hard", status.Reason);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 16, 27, 44, 364, TimeSpan.Zero), status.CreatedAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 16, 30, 2, 100, TimeSpan.Zero), status.StatusAt);
        var request = Assert.Single(harness.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://us.posthog.com/api/messaging/emails/01a18356-c56c-0001-e494-7666bc915188/",
            request.Url);
        Assert.Equal("Bearer phs_secret", request.Header("Authorization"));
        Assert.Equal("resend", request.Header("X-PostHog-Migrated-From"));
        Assert.Null(request.Header("Idempotency-Key"));
        Assert.Null(request.Body);
    }

    [Fact]
    public async Task PassesAnUnknownStatusThrough()
    {
        await using var harness = new EmailHarness();
        harness.Respond(
            HttpStatusCode.OK,
            """{"id": "01a18356-c56c-0001-e494-7666bc915188", "status": "archived", "created_at": "2026-10-28T16:27:44Z", "status_at": "2026-10-28T16:27:44Z", "reason": null}""");

        var status = await harness.Client.Emails.GetAsync(MessageId);

        Assert.Equal("archived", status.Status);
        Assert.Null(status.Reason);
    }

    [Fact]
    public async Task DoesNotRetryAnUnknownId()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.NotFound, """{"code": "not_found", "detail": "No email."}""");

        var error = await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.GetAsync(MessageId));

        Assert.Single(harness.Requests);
        Assert.Equal("not_found", error.Code);
        Assert.Equal("not_found", error.Name);
    }

    [Fact]
    public async Task RetriesAnUnavailableService()
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.ServiceUnavailable, "");
        harness.Respond(
            HttpStatusCode.OK,
            """{"id": "01a18356-c56c-0001-e494-7666bc915188", "status": "sent", "created_at": "2026-10-28T16:27:44Z", "status_at": "2026-10-28T16:27:45Z"}""");

        var status = await harness.Client.Emails.GetAsync(MessageId);

        Assert.Equal("sent", status.Status);
        Assert.Equal(2, harness.Requests.Count);
    }

    [Theory]
    [InlineData("""{"status": "sent", "created_at": "2026-10-28T16:27:44Z", "status_at": "2026-10-28T16:27:44Z"}""")]
    [InlineData("""{"id": "x", "created_at": "2026-10-28T16:27:44Z", "status_at": "2026-10-28T16:27:44Z"}""")]
    [InlineData("""{"id": "x", "status": "sent", "status_at": "2026-10-28T16:27:44Z"}""")]
    [InlineData("""{"id": "x", "status": "sent", "created_at": "yesterday", "status_at": "2026-10-28T16:27:44Z"}""")]
    public async Task TreatsAnIncompleteRecordAsAnError(string body)
    {
        await using var harness = new EmailHarness();
        harness.Respond(HttpStatusCode.OK, body);

        var error = await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.GetAsync(MessageId));

        Assert.Equal("api_error", error.Code);
        Assert.Equal(HttpStatusCode.OK, error.StatusCode);
    }

    [Theory]
    [InlineData("msg_1")]
    [InlineData("{01a18356-c56c-0001-e494-7666bc915188}")]
    [InlineData("")]
    public async Task RefusesAnIdThatIsNotAMessageId(string messageId)
    {
        await using var harness = new EmailHarness();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => harness.Client.Emails.GetAsync(messageId));

        Assert.Equal("messageId", error.ParamName);
        Assert.Empty(harness.Requests);
    }

    [Fact]
    public async Task ADisabledClientReadsNothing()
    {
        await using var harness = new EmailHarness(options => options.Disabled = true);

        var error = await Assert.ThrowsAsync<EmailException>(() => harness.Client.Emails.GetAsync(MessageId));

        Assert.Equal("client_disabled", error.Code);
        Assert.Empty(harness.Requests);
    }
}

/// <summary>
/// A real <see cref="PostHogClient"/> whose HTTP calls and retry waits are recorded.
/// </summary>
sealed class EmailHarness : IAsyncDisposable
{
    readonly RecordingHandler _handler = new();
    readonly HttpClient _httpClient;

    public EmailHarness(Action<PostHogOptions>? configure = null, HttpMessageHandler? posthog = null)
    {
        var options = new PostHogOptions
        {
            ProjectToken = "fake-project-token",
            SecretKey = "phs_secret",
        };
        configure?.Invoke(options);
        var transport = posthog ?? _handler;
        _httpClient = new HttpClient(transport, disposeHandler: false);
        Client = new PostHogClient(
            options,
            featureFlagsCache: null,
            httpClientFactory: new SingleHttpClientFactory(_httpClient),
            taskScheduler: null,
            timeProvider: Time,
            loggerFactory: Logs,
            uploadTransport: transport);
    }

    public PostHogClient Client { get; }

    /// <summary>The <see cref="HttpClient"/> the client sends through, as an app configures it.</summary>
    public HttpClient HttpClient => _httpClient;

    public RecordingTimeProvider Time { get; } = new();

    public FakeLoggerProvider Logs { get; } = new();

    public IReadOnlyList<RecordedRequest> Requests => _handler.Requests;

    public static SendTemplateEmailOptions TemplateSend(string? idempotencyKey = null, int? maxRetries = null) => new()
    {
        To = "user@example.com",
        TemplateId = Guid.Parse("0190f6d2-7c1e-7b3a-9f5e-1c2d3e4f5a6b"),
        IdempotencyKey = idempotencyKey,
        MaxRetries = maxRetries,
    };

    public void Respond(
        HttpStatusCode status,
        string body,
        string contentType = "application/json",
        TimeSpan? retryAfter = null)
        => _handler.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType),
            };
            if (retryAfter is { } wait)
            {
                response.Headers.RetryAfter = new RetryConditionHeaderValue(wait);
            }

            return Task.FromResult(response);
        });

    public void Fail(Exception exception) => _handler.Enqueue(_ => Task.FromException<HttpResponseMessage>(exception));

    public void Hang() => _handler.Enqueue(async cancellationToken =>
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("Unreachable.");
    });

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        _httpClient.Dispose();
        _handler.Dispose();
    }

    /// <summary>Hands out the app's PostHog client, as an app's IHttpClientFactory does.</summary>
    sealed class SingleHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}

sealed record RecordedRequest(
    HttpMethod Method,
    string Url,
    IReadOnlyDictionary<string, string> Headers,
    string? Body,
    byte[]? BodyBytes = null)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;

    public static async Task<RecordedRequest> CaptureAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        string? body = null;
        byte[]? bodyBytes = null;
        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }

#if NET5_0_OR_GREATER
            bodyBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
#else
            bodyBytes = await request.Content.ReadAsByteArrayAsync();
#endif
            body = Encoding.UTF8.GetString(bodyBytes);
        }

        return new RecordedRequest(request.Method, request.RequestUri!.AbsoluteUri, headers, body, bodyBytes);
    }
}

sealed class RecordingHandler : HttpMessageHandler
{
    readonly ConcurrentQueue<Func<CancellationToken, Task<HttpResponseMessage>>> _responses = new();
    readonly List<RecordedRequest> _requests = [];

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToList();
            }
        }
    }

    public void Enqueue(Func<CancellationToken, Task<HttpResponseMessage>> response) => _responses.Enqueue(response);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var recorded = await RecordedRequest.CaptureAsync(request, cancellationToken);
        lock (_requests)
        {
            _requests.Add(recorded);
        }

        if (!_responses.TryDequeue(out var respond))
        {
            throw new InvalidOperationException($"No response queued for {request.Method} {request.RequestUri}.");
        }

        return await respond(cancellationToken);
    }
}

/// <summary>
/// Records each one-shot wait, such as a retry backoff, and ends it at once, or holds it until released.
/// Periodic timers run on the system clock.
/// </summary>
sealed class RecordingTimeProvider : TimeProvider
{
    readonly List<TimeSpan> _delays = [];
    readonly List<Action> _held = [];
    readonly TaskCompletionSource<bool> _firstHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool _holding;

    public IReadOnlyList<TimeSpan> Delays
    {
        get
        {
            lock (_delays)
            {
                return _delays.ToList();
            }
        }
    }

    public void HoldDelays()
    {
        lock (_delays)
        {
            _holding = true;
        }
    }

    public async Task WaitForHeldDelayAsync()
    {
        if (await Task.WhenAny(_firstHeld.Task, Task.Delay(TimeSpan.FromSeconds(5))) != _firstHeld.Task)
        {
            throw new TimeoutException("No retry wait started.");
        }
    }

    public void ReleaseDelays()
    {
        List<Action> held;
        lock (_delays)
        {
            _holding = false;
            held = _held.ToList();
            _held.Clear();
        }

        foreach (var fire in held)
        {
            fire();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (period != Timeout.InfiniteTimeSpan || dueTime == Timeout.InfiniteTimeSpan)
        {
            return TimeProvider.System.CreateTimer(callback, state, dueTime, period);
        }

        void Fire() => ThreadPool.QueueUserWorkItem(_ => callback(state));
        lock (_delays)
        {
            _delays.Add(dueTime);
            if (_holding)
            {
                _held.Add(Fire);
                _firstHeld.TrySetResult(true);
                return new NoOpTimer();
            }
        }

        Fire();
        return new NoOpTimer();
    }

    sealed class NoOpTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => default;
    }
}
