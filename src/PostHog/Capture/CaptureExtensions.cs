using PostHog.Library;

namespace PostHog;
using static Ensure;

/// <summary>
/// Extensions of <see cref="IPostHogClient"/> related to capturing events.
/// </summary>
public static class CaptureExtensions
{
    /// <summary>
    /// Captures an event.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="eventName">Human friendly name of the event. Recommended format [object] [verb] such as "Project created" or "User signed up".</param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool Capture(
        this IPostHogClient client,
        string distinctId,
        string eventName)
        => NotNull(client).Capture(
            distinctId,
            eventName,
            properties: null,
            groups: null,
            sendFeatureFlags: false);

    /// <summary>
    /// Captures an event.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="eventName">Human friendly name of the event. Recommended format [object] [verb] such as "Project created" or "User signed up".</param>
    /// <param name="sendFeatureFlags">Default: <c>false</c>. If <c>true</c>, feature flags are sent with the captured event.</param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool Capture(
        this IPostHogClient client,
        string distinctId,
        string eventName,
        bool sendFeatureFlags)
        => NotNull(client).Capture(
            distinctId,
            eventName,
            properties: null,
            groups: null,
            sendFeatureFlags: sendFeatureFlags);

    /// <summary>
    /// Captures an event with additional properties to add to the event.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="eventName">Human friendly name of the event. Recommended format [object] [verb] such as "Project created" or "User signed up".</param>
    /// <param name="properties">Optional: The properties to send along with the event.</param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool Capture(
        this IPostHogClient client,
        string distinctId,
        string eventName,
        Dictionary<string, object>? properties)
        => NotNull(client).Capture(
            distinctId,
            eventName,
            properties,
            groups: null,
            sendFeatureFlags: false);

    /// <summary>
    /// Captures an event with properties to set on the user.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="eventName">Human friendly name of the event. Recommended format [object] [verb] such as "Project created" or "User signed up".</param>
    /// <param name="personPropertiesToSet">
    /// Key value pairs to store as a property of the user. Any key value pairs in this dictionary that match
    /// existing property keys will overwrite those properties.
    /// </param>
    /// <param name="personPropertiesToSetOnce">User properties to set only once (ex: Sign up date). If a property already exists, then the
    /// value in this dictionary is ignored.
    /// </param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool Capture(
        this IPostHogClient client,
        string distinctId,
        string eventName,
        Dictionary<string, object> personPropertiesToSet,
        Dictionary<string, object> personPropertiesToSetOnce)
        => client.Capture(distinctId, eventName, properties: null, personPropertiesToSet, personPropertiesToSetOnce);

    /// <summary>
    /// Captures an event with properties to set on the user.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="eventName">Human friendly name of the event. Recommended format [object] [verb] such as "Project created" or "User signed up".</param>
    /// <param name="properties">Optional: The properties to send along with the event.</param>
    /// <param name="personPropertiesToSet">
    /// Key value pairs to store as a property of the user. Any key value pairs in this dictionary that match
    /// existing property keys will overwrite those properties.
    /// </param>
    /// <param name="personPropertiesToSetOnce">User properties to set only once (ex: Sign up date). If a property already exists, then the
    /// value in this dictionary is ignored.
    /// </param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool Capture(
        this IPostHogClient client,
        string distinctId,
        string eventName,
        Dictionary<string, object>? properties,
        Dictionary<string, object> personPropertiesToSet,
        Dictionary<string, object> personPropertiesToSetOnce)
    {
        properties ??= new Dictionary<string, object>();
        properties["$set"] = personPropertiesToSet;
        properties["$set_once"] = personPropertiesToSetOnce;

        return NotNull(client).Capture(
            distinctId,
            eventName,
            properties,
            groups: null,
            sendFeatureFlags: false);
    }

    /// <summary>
    /// Captures an event.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="eventName">Human friendly name of the event. Recommended format [object] [verb] such as "Project created" or "User signed up".</param>
    /// <param name="groups">A set of groups to send with the event. The groups are identified by their group_type and group_key.</param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool Capture(
        this IPostHogClient client,
        string distinctId,
        string eventName,
        GroupCollection groups)
        => NotNull(client).Capture(
            distinctId,
            eventName,
            properties: null,
            groups: groups,
            sendFeatureFlags: false);

    /// <summary>
    /// Captures a Page View ($pageview) event.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="pagePath">The URL or path of the page to capture.</param>
    /// <param name="properties">Additional context to save with the event.</param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CapturePageView(
        this IPostHogClient client,
        string distinctId,
        string pagePath,
        Dictionary<string, object>? properties)
        => NotNull(client).CaptureSpecialEvent(
            distinctId,
            eventName: "$pageview",
            eventPropertyName: "$current_url",
            eventPropertyValue: pagePath,
            properties);

    /// <summary>
    /// Captures a Page View ($pageview) event.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="pagePath">The URL or path of the page to capture.</param>
    /// <param name="properties">Additional context to save with the event.</param>
    /// <param name="sendFeatureFlags">Default: <c>false</c>. If <c>true</c>, feature flags are sent with the captured event.</param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CapturePageView(
        this IPostHogClient client,
        string distinctId,
        string pagePath,
        Dictionary<string, object>? properties,
        bool sendFeatureFlags)
        => NotNull(client).CaptureSpecialEvent(
            distinctId,
            eventName: "$pageview",
            eventPropertyName: "$current_url",
            eventPropertyValue: pagePath,
            properties,
            sendFeatureFlags);

    /// <summary>
    /// Captures a Page View ($pageview) event.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="pagePath">The URL or path of the page to capture.</param>
    public static bool CapturePageView(
        this IPostHogClient client,
        string distinctId,
        string pagePath) => client.CapturePageView(distinctId, pagePath, properties: null);

    /// <summary>
    /// Captures a Page View ($pageview) event.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="pagePath">The URL or path of the page to capture.</param>
    /// <param name="sendFeatureFlags">Default: <c>false</c>. If <c>true</c>, feature flags are sent with the captured event.</param>
    public static bool CapturePageView(
        this IPostHogClient client,
        string distinctId,
        string pagePath,
        bool sendFeatureFlags) => NotNull(client).CapturePageView(distinctId, pagePath, properties: null, sendFeatureFlags);


    /// <summary>
    /// Captures a Screen View ($screen) event.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="screenName">The URL or path of the page to capture.</param>
    /// <param name="properties">Additional context to save with the event.</param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureScreenView(
        this IPostHogClient client,
        string distinctId,
        string screenName,
        Dictionary<string, object>? properties)
        => NotNull(client).CaptureSpecialEvent(
            distinctId,
            eventName: "$screen",
            eventPropertyName: "$screen_name",
            eventPropertyValue: screenName,
            properties);

    /// <summary>
    /// Captures a Screen View ($screen) event.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="screenName">The URL or path of the page to capture.</param>
    /// <param name="properties">Additional context to save with the event.</param>
    /// <param name="sendFeatureFlags">Default: <c>false</c>. If <c>true</c>, feature flags are sent with the captured event.</param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureScreenView(
        this IPostHogClient client,
        string distinctId,
        string screenName,
        Dictionary<string, object>? properties,
        bool sendFeatureFlags)
        => NotNull(client).CaptureSpecialEvent(
            distinctId,
            eventName: "$screen",
            eventPropertyName: "$screen_name",
            eventPropertyValue: screenName,
            properties,
            sendFeatureFlags);

    /// <summary>
    /// Captures a Screen View ($screen) event.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="screenName">The URL or path of the page to capture.</param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureScreenView(
        this IPostHogClient client,
        string distinctId,
        string screenName) => client.CaptureScreenView(distinctId, screenName, properties: null);

    /// <summary>
    /// Captures a survey response.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="surveyId">The id of the survey.</param>
    /// <param name="surveyResponse">The survey response.</param>
    /// <param name="properties">Additional properties to capture.</param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureSurveyResponse(
        this IPostHogClient client,
        string distinctId,
        string surveyId,
        string surveyResponse,
        Dictionary<string, object>? properties)
        => client.CaptureSurveyResponses(
            distinctId,
            surveyId,
            surveyResponses: [surveyResponse],
            properties);

    /// <summary>
    /// Captures a survey response.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="surveyId">The id of the survey.</param>
    /// <param name="surveyResponses">The survey responses.</param>
    /// <param name="properties">Additional properties to capture.</param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureSurveyResponses(
        this IPostHogClient client,
        string distinctId,
        string surveyId,
        IReadOnlyList<string> surveyResponses,
        Dictionary<string, object>? properties)
    {
        properties ??= new Dictionary<string, object>();
        properties["$survey_id"] = surveyId;

        if (NotNull(surveyResponses).Count > 0)
        {
            properties["$survey_response"] = surveyResponses[0];
        }

        for (var i = 1; i < surveyResponses.Count; i++)
        {
            properties[$"survey_response_{i}"] = surveyResponses[i];
        }

        return NotNull(client).Capture(distinctId, "survey sent", properties, groups: null, sendFeatureFlags: false);
    }

    /// <summary>
    /// Captures that a survey was shown.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="surveyId">The id of the survey.</param>
    /// <param name="properties">Additional properties to capture.</param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureSurveyShown(
        this IPostHogClient client,
        string distinctId,
        string surveyId,
        Dictionary<string, object>? properties)
        => NotNull(client).CaptureSpecialEvent(
            distinctId,
            eventName: "survey shown",
            eventPropertyName: "$survey_id",
            surveyId,
            properties);

    /// <summary>
    /// Captures that a survey was dismissed.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="surveyId">The id of the survey.</param>
    /// <param name="properties">Additional properties to capture.</param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureSurveyDismissed(
        this IPostHogClient client,
        string distinctId,
        string surveyId,
        Dictionary<string, object>? properties)
        => NotNull(client).CaptureSpecialEvent(
            distinctId,
            eventName: "survey dismissed",
            eventPropertyName: "$survey_id",
            eventPropertyValue: surveyId,
            properties);

    static bool CaptureSpecialEvent(
        this IPostHogClient client,
        string distinctId,
        string eventName,
        string eventPropertyName,
        string eventPropertyValue,
        Dictionary<string, object>? properties,
        bool sendFeatureFlags = false)
    {
        properties ??= new Dictionary<string, object>();
        properties[eventPropertyName] = eventPropertyValue;
        return NotNull(client).Capture(distinctId, eventName, properties, null, sendFeatureFlags);
    }
}
