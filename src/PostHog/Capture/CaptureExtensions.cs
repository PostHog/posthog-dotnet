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
    public static bool CaptureEvent(
        this IPostHogClient client,
        string distinctId,
        string eventName)
        => NotNull(client).CaptureEvent(
            distinctId,
            eventName,
            properties: null,
            groups: null,
            sendFeatureFlags: false);

    /// <summary>
    /// Captures an event with additional properties to add to the event.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="eventName">Human friendly name of the event. Recommended format [object] [verb] such as "Project created" or "User signed up".</param>
    /// <param name="properties">Optional: The properties to send along with the event.</param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureEvent(
        this IPostHogClient client,
        string distinctId,
        string eventName,
        Dictionary<string, object>? properties)
        => NotNull(client).CaptureEvent(
            distinctId,
            eventName,
            properties,
            groups: null,
            sendFeatureFlags: false);

    /// <summary>
    /// Captures an event.
    /// </summary>
    /// <param name="client">The <see cref="IPostHogClient"/>.</param>
    /// <param name="distinctId">The identifier you use for the user.</param>
    /// <param name="eventName">Human friendly name of the event. Recommended format [object] [verb] such as "Project created" or "User signed up".</param>
    /// <param name="groups">A set of groups to send with the event. The groups are identified by their group_type and group_key.</param>
    /// <returns><c>true</c> if the event was successfully enqueued. Otherwise <c>false</c>.</returns>
    public static bool CaptureEvent(
        this IPostHogClient client,
        string distinctId,
        string eventName,
        GroupCollection groups)
        => NotNull(client).CaptureEvent(
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
        => client.CaptureSpecialEvent(
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
    public static bool CapturePageView(
        this IPostHogClient client,
        string distinctId,
        string pagePath) => client.CapturePageView(distinctId, pagePath, properties: null);

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
        => client.CaptureSpecialEvent(
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

        return NotNull(client).CaptureEvent(distinctId, "survey sent", properties, groups: null, sendFeatureFlags: false);
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
        => client.CaptureSpecialEvent(
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
        => client.CaptureSpecialEvent(
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
        Dictionary<string, object>? properties)
    {
        properties ??= new Dictionary<string, object>();
        properties[eventPropertyName] = eventPropertyValue;
        return client.CaptureEvent(distinctId, eventName, properties);
    }
}
