using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EmailsTests;

/// <summary>
/// An in-memory stand-in for PostHog's code-send endpoint that keeps the project's message categories the way the
/// send API does. It upserts a category by key, refuses a type change with <c>category_mismatch</c>, keeps a
/// category's description when the field is missing and clears it on <c>""</c>, and refuses a marketing email
/// without <c>{{ unsubscribe_url }}</c>. It refuses category fields outside the API's schema, so a misspelled field
/// fails here instead of being dropped.
/// </summary>
sealed class FakeMessagingApi
{
    static readonly HashSet<string> CategoryFields = ["key", "type", "name", "description"];
    static readonly Regex UnsubscribePlaceholder = new(@"\{\{\s*unsubscribe_url\s*\}\}");

    readonly Dictionary<string, StoredCategory> _categories = new(StringComparer.Ordinal);
    readonly List<AcceptedSend> _sends = [];

    public IReadOnlyDictionary<string, StoredCategory> Categories => _categories;

    public IReadOnlyList<AcceptedSend> Sends => _sends;

    public void Seed(StoredCategory category) => _categories[category.Key] = category;

    public Task<HttpResponseMessage> Answer(RecordedRequest request)
    {
        if (request.Method != HttpMethod.Post || !request.Url.EndsWith("/messaging/send/email/", StringComparison.Ordinal))
        {
            return Respond(HttpStatusCode.NotFound, """{"type": "invalid_request", "code": "not_found", "detail": "Not found."}""");
        }

        using var body = JsonDocument.Parse(request.Body ?? "{}");
        var email = body.RootElement;
        if (!email.TryGetProperty("category", out var category)
            || category.ValueKind == JsonValueKind.Null
            || category is { ValueKind: JsonValueKind.String } && category.GetString() == "transactional")
        {
            return Accept(categoryKey: null);
        }

        if (category.ValueKind != JsonValueKind.Object)
        {
            return Refuse("invalid_input", "Send `transactional`, or a category object with `key`, `type` and `name`.");
        }

        foreach (var field in category.EnumerateObject())
        {
            if (!CategoryFields.Contains(field.Name))
            {
                return Refuse("invalid_input", $"The category has no field `{field.Name}`.");
            }
        }

        var key = Text(category, "key");
        var name = Text(category, "name");
        var type = Text(category, "type");
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(name) || type is not ("marketing" or "transactional"))
        {
            return Refuse("invalid_input", "The category needs a `key`, a `name` and a `type` of marketing or transactional.");
        }

        string? description = null;
        if (category.TryGetProperty("description", out var given))
        {
            if (given.ValueKind != JsonValueKind.String)
            {
                return Refuse("invalid_input", "The category's `description` must be a string.");
            }

            description = given.GetString();
        }

        var stored = _categories.TryGetValue(key, out var existing) ? existing : null;
        if (stored is not null && stored.Type != type)
        {
            return Refuse("category_mismatch", $"The category `{key}` is {stored.Type}.");
        }

        if (type == "marketing" && !HoldsUnsubscribeLink(email))
        {
            return Refuse("unsubscribe_link_required", "A marketing email needs `{{ unsubscribe_url }}`.", attr: null);
        }

        _categories[key] = stored is null
            ? new StoredCategory(key, type, name, description ?? "")
            : stored with { Name = name, Description = description ?? stored.Description };
        return Accept(key);
    }

    static bool HoldsUnsubscribeLink(JsonElement email)
    {
        var content = Text(email, "html") ?? Text(email, "text");
        return content is not null && UnsubscribePlaceholder.IsMatch(content);
    }

    static string? Text(JsonElement element, string field)
        => element.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    Task<HttpResponseMessage> Accept(string? categoryKey)
    {
        _sends.Add(new AcceptedSend(categoryKey));
        return Respond(
            HttpStatusCode.Accepted,
            $$"""{"message_id": "01a18356-c56c-0001-e494-7666bc91{{_sends.Count:D4}}"}""");
    }

    static Task<HttpResponseMessage> Refuse(string code, string detail, string? attr = "category")
        => Respond(
            HttpStatusCode.BadRequest,
            JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["type"] = "validation_error",
                ["code"] = code,
                ["detail"] = detail,
                ["attr"] = attr,
            }));

    static Task<HttpResponseMessage> Respond(HttpStatusCode status, string json)
        => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
}

sealed record StoredCategory(string Key, string Type, string Name, string Description);

/// <summary>
/// A send the fake accepted, with the key of the category it went out in, or null for no category.
/// </summary>
sealed record AcceptedSend(string? CategoryKey);
