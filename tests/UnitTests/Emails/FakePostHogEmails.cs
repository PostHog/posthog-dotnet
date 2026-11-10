using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EmailsTests;

/// <summary>
/// PostHog's upload and send routes and its file storage, as one HTTP peer that keeps state. The upload route
/// answers <c>present</c> for a stored file and presigns an upload for any other. The storage takes a PUT only as
/// its URL was signed, with no other credential, and stores the bytes. The send route delivers an email only when
/// PostHog stores every file it names. Every route refuses a request outside the real server's schema.
/// </summary>
sealed class FakePostHogEmails : HttpMessageHandler
{
    public const string SecretKey = "phs_secret";
    public const string MessageId = "01a18356-c56c-0001-e494-7666bc915188";
    const string UploadsPath = "/api/projects/@current/messaging/attachments/uploads/";
    const string SendPath = "/api/projects/@current/messaging/send/email/";

    static readonly string[] OlderSendFields =
    [
        "template_key", "to", "subject", "html", "text", "from", "reply_to", "capture_engagement", "distinct_id",
    ];

    static readonly string[] SendFields = [.. OlderSendFields, "cc", "bcc", "attachments"];
    static readonly string[] AttachmentFields = ["sha256", "filename", "content_type", "content_id"];

    readonly ConcurrentDictionary<string, byte[]> _stored = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, SignedUpload> _signed = new(StringComparer.Ordinal);
    readonly ConcurrentQueue<Func<HttpResponseMessage>> _uploadRouteFaults = new();
    readonly ConcurrentQueue<Func<HttpResponseMessage>> _storageFaults = new();
    readonly List<DeliveredEmail> _delivered = [];
    bool _olderVersion;
    int _storageWrites;

    /// <summary>Whether this project may send files: off for a project PostHog hasn't opened them to yet.</summary>
    public bool FilesAllowed { get; set; } = true;

    /// <summary>Whether every upload URL expired before the client used it.</summary>
    public bool UploadUrlsExpired { get; set; }

    /// <summary>The scheme, host and port of the file storage that PostHog's upload URLs point to.</summary>
    public string StorageOrigin { get; set; } = "https://files.example.com";

    public IReadOnlyList<DeliveredEmail> Delivered
    {
        get
        {
            lock (_delivered)
            {
                return _delivered.ToList();
            }
        }
    }

    /// <summary>How many files the storage wrote.</summary>
    public int StorageWrites => Volatile.Read(ref _storageWrites);

    /// <summary>Stores a file, as an earlier send would have uploaded it.</summary>
    public void Store(byte[] content) => _stored[Sha256Hex(content)] = content;

    /// <summary>Plays a PostHog version from before copies and files: no upload route, no <c>cc</c>, <c>bcc</c> or
    /// <c>attachments</c> on sends.</summary>
    public void RunAnOlderVersion() => _olderVersion = true;

    /// <summary>Answers the next call to the upload route with a fault the real route can't be made to give.</summary>
    public void FailUploadRoute(HttpStatusCode status, string body, string contentType = "application/json")
        => _uploadRouteFaults.Enqueue(() => Answer(status, body, contentType));

    /// <summary>Answers the next signed PUT with a fault the real storage can't be made to give.</summary>
    public void FailStorage(HttpStatusCode status, string body, string contentType = "application/xml")
        => _storageFaults.Enqueue(() => Answer(status, body, contentType));

    /// <summary>Answers the next signed PUT with a <c>307</c> to another URL, as a hijacked storage host would.</summary>
    public void RedirectStorage(Uri location)
        => _storageFaults.Enqueue(() =>
        {
            var answer = Answer(HttpStatusCode.TemporaryRedirect, $"Temporary Redirect. Redirecting to {location.AbsoluteUri}", "text/plain");
            answer.Headers.Location = location;
            return answer;
        });

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
#if NET5_0_OR_GREATER
        var body = request.Content is null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(cancellationToken);
#else
        var body = request.Content is null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync();
#endif
        if (string.Equals(uri.GetLeftPart(UriPartial.Authority), StorageOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return Store(request, uri, body);
        }

        if (request.Headers.Authorization is not { Scheme: "Bearer", Parameter: SecretKey })
        {
            return Refusal(HttpStatusCode.Unauthorized, "authentication_failed", null, "Invalid secret key.");
        }

        return (request.Method.Method, uri.AbsolutePath) switch
        {
            ("POST", UploadsPath) when !_olderVersion => Uploads(body),
            ("POST", SendPath) => Send(body),
            _ => Refusal(HttpStatusCode.NotFound, "not_found", null, "Endpoint not found."),
        };
    }

    HttpResponseMessage Uploads(byte[] body)
    {
        if (_uploadRouteFaults.TryDequeue(out var fault))
        {
            return fault();
        }

        if (!FilesAllowed)
        {
            return Refusal(
                HttpStatusCode.BadRequest,
                "field_not_accepted",
                "files",
                "This project can't send files yet, so PostHog didn't prepare the uploads.");
        }

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        if (UnknownField(root, ["files"]) is not null
            || !root.TryGetProperty("files", out var files)
            || files.ValueKind != JsonValueKind.Array
            || files.GetArrayLength() is < 1 or > 10)
        {
            return Refusal(HttpStatusCode.BadRequest, "invalid_request", "files", "Send 1 to 10 files.");
        }

        var answers = new List<Dictionary<string, object>>();
        foreach (var file in files.EnumerateArray())
        {
            if (UnknownField(file, ["sha256", "size"]) is not null
                || Text(file, "sha256") is not { } sha256
                || !IsSha256Hex(sha256)
                || !file.TryGetProperty("size", out var sizeField)
                || !sizeField.TryGetInt64(out var size)
                || size < 1)
            {
                return Refusal(HttpStatusCode.BadRequest, "invalid_request", "files", "A file takes only sha256 and size.");
            }

            answers.Add(_stored.ContainsKey(sha256) ? Present(sha256) : SignUpload(sha256, size));
        }

        return Json(HttpStatusCode.OK, new Dictionary<string, object> { ["files"] = answers });
    }

    static Dictionary<string, object> Present(string sha256) => new()
    {
        ["sha256"] = sha256,
        ["status"] = "present",
        ["expires_at"] = "2026-12-16T12:00:00Z",
    };

    Dictionary<string, object> SignUpload(string sha256, long size)
    {
        var signed = new SignedUpload(size, Convert.ToBase64String(HexBytes(sha256)));
        _signed[sha256] = signed;
        return new Dictionary<string, object>
        {
            ["sha256"] = sha256,
            ["status"] = "upload",
            ["url"] = $"{StorageOrigin}/team/1/{sha256}?X-Amz-Expires=900&X-Amz-Signature=signed",
            ["method"] = "PUT",
            ["headers"] = new Dictionary<string, string>
            {
                ["Content-Length"] = size.ToString(CultureInfo.InvariantCulture),
                ["x-amz-checksum-sha256"] = signed.Checksum,
            },
            ["url_expires_at"] = "2026-11-10T12:15:00Z",
        };
    }

    HttpResponseMessage Store(HttpRequestMessage request, Uri uri, byte[] body)
    {
        var sha256 = uri.AbsolutePath.Substring(uri.AbsolutePath.LastIndexOf('/') + 1);
        if (request.Method != HttpMethod.Put || !_signed.TryGetValue(sha256, out var signed))
        {
            return StorageError(HttpStatusCode.Forbidden, "SignatureDoesNotMatch", "The request signature we calculated does not match the signature you provided.");
        }

        if (request.Headers.Authorization is not null)
        {
            return StorageError(HttpStatusCode.BadRequest, "InvalidArgument", "Only one auth mechanism allowed.");
        }

        if (UploadUrlsExpired)
        {
            return StorageError(HttpStatusCode.Forbidden, "AccessDenied", "Request has expired");
        }

        if (_storageFaults.TryDequeue(out var fault))
        {
            return fault();
        }

        if (request.Content?.Headers.ContentLength != signed.Size || Header(request, "x-amz-checksum-sha256") != signed.Checksum)
        {
            return StorageError(HttpStatusCode.Forbidden, "SignatureDoesNotMatch", "The request signature we calculated does not match the signature you provided.");
        }

        if (Convert.ToBase64String(Sha256(body)) != signed.Checksum)
        {
            return StorageError(HttpStatusCode.BadRequest, "BadDigest", "The SHA256 you specified did not match the calculated checksum.");
        }

        _stored[sha256] = body;
        Interlocked.Increment(ref _storageWrites);
        return Answer(HttpStatusCode.OK, string.Empty, "application/xml");
    }

    HttpResponseMessage Send(byte[] body)
    {
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        if (UnknownField(root, _olderVersion ? OlderSendFields : SendFields) is { } unknown)
        {
            return Refusal(HttpStatusCode.BadRequest, "invalid_request", unknown, "PostHog doesn't take this field, so it doesn't send the email without it.");
        }

        var files = new List<DeliveredFile>();
        if (root.TryGetProperty("attachments", out var references))
        {
            if (references.ValueKind != JsonValueKind.Array || references.GetArrayLength() is < 1 or > 10)
            {
                return Refusal(HttpStatusCode.BadRequest, "invalid_request", "attachments", "Send 1 to 10 attachments.");
            }

            foreach (var (reference, index) in references.EnumerateArray().Select((reference, index) => (reference, index)))
            {
                if (UnknownField(reference, AttachmentFields) is not null
                    || Text(reference, "sha256") is not { } sha256
                    || !IsSha256Hex(sha256)
                    || Text(reference, "filename") is not { } filename)
                {
                    return Refusal(HttpStatusCode.BadRequest, "invalid_request", $"attachments__{index}", "An attachment takes sha256, filename, content_type and content_id.");
                }

                if (!_stored.TryGetValue(sha256, out var content))
                {
                    return Refusal(HttpStatusCode.BadRequest, "attachment_not_uploaded", $"attachments__{index}__sha256", "A file this email names isn't uploaded, so PostHog didn't send the email.");
                }

                files.Add(new DeliveredFile(filename, Text(reference, "content_type"), Text(reference, "content_id"), content));
            }
        }

        lock (_delivered)
        {
            _delivered.Add(new DeliveredEmail(
                Text(root, "to")!,
                Texts(root, "cc"),
                Texts(root, "bcc"),
                Text(root, "subject"),
                Text(root, "html"),
                Text(root, "text"),
                files));
        }

        return Json(HttpStatusCode.Accepted, new Dictionary<string, object> { ["message_id"] = MessageId });
    }

    static string? UnknownField(JsonElement element, string[] known)
        => element.ValueKind != JsonValueKind.Object
            ? "(not an object)"
            : element.EnumerateObject().Select(field => field.Name).FirstOrDefault(name => !known.Contains(name));

    static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static List<string> Texts(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.GetString()!).ToList()
            : [];

    static string? Header(HttpRequestMessage request, string name)
    {
        var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>());
        return headers
            .Where(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
            .Select(header => string.Join(",", header.Value))
            .FirstOrDefault();
    }

    static bool IsSha256Hex(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    static byte[] HexBytes(string hex)
    {
#if NET5_0_OR_GREATER
        return Convert.FromHexString(hex);
#else
        return Enumerable.Range(0, hex.Length / 2)
            .Select(i => byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToArray();
#endif
    }

    static byte[] Sha256(byte[] content)
    {
#if NET5_0_OR_GREATER
        return SHA256.HashData(content);
#else
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(content);
#endif
    }

    static string Sha256Hex(byte[] content)
        => string.Concat(Sha256(content).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));

    static HttpResponseMessage Refusal(HttpStatusCode status, string code, string? attr, string detail)
        => Json(status, new Dictionary<string, object?>
        {
            ["type"] = "validation_error",
            ["code"] = code,
            ["detail"] = detail,
            ["attr"] = attr,
        });

    static HttpResponseMessage StorageError(HttpStatusCode status, string code, string message)
        => Answer(status, $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Error><Code>{code}</Code><Message>{message}</Message></Error>", "application/xml");

    static HttpResponseMessage Json(HttpStatusCode status, object body)
        => Answer(status, JsonSerializer.Serialize(body), "application/json");

    static HttpResponseMessage Answer(HttpStatusCode status, string body, string contentType)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };

    sealed record SignedUpload(long Size, string Checksum);
}

/// <summary>An email PostHog accepted, with the files it attached.</summary>
sealed record DeliveredEmail(
    string To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string? Subject,
    string? Html,
    string? Text,
    IReadOnlyList<DeliveredFile> Attachments);

/// <summary>A file attached to a delivered email, with the bytes PostHog stored for it.</summary>
sealed record DeliveredFile(string Filename, string? ContentType, string? ContentId, byte[] Content);
