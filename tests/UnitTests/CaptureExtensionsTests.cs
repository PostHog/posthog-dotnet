using NSubstitute;
using PostHog;
using PostHog.Features;

namespace CaptureExtensionsTests;

public class TheCaptureExtensions
{
    [Fact]
    public void CaptureWithPersonPropertiesDoesNotMutateProvidedProperties()
    {
        var client = Substitute.For<IPostHogClient>();
        var properties = new Dictionary<string, object> { ["source"] = "test" };
        var personPropertiesToSet = new Dictionary<string, object> { ["name"] = "Max" };
        var personPropertiesToSetOnce = new Dictionary<string, object> { ["initial_url"] = "/blog" };

        client.Capture(
            "distinct-id",
            "event",
            properties,
            personPropertiesToSet,
            personPropertiesToSetOnce);

        Assert.Equal(new Dictionary<string, object> { ["source"] = "test" }, properties);
        client.Received(1).Capture(
            "distinct-id",
            "event",
            Arg.Is<Dictionary<string, object>>(captured => HasCopiedPersonProperties(
                captured,
                properties,
                personPropertiesToSet,
                personPropertiesToSetOnce)),
            groups: null,
            flags: (FeatureFlagEvaluations?)null,
            timestamp: null);
    }

    [Fact]
    public void CapturePageViewDoesNotMutateProvidedProperties()
    {
        var client = Substitute.For<IPostHogClient>();
        var properties = new Dictionary<string, object> { ["source"] = "test" };

        client.CapturePageView("distinct-id", "/pricing", properties);

        Assert.Equal(new Dictionary<string, object> { ["source"] = "test" }, properties);
        client.Received(1).Capture(
            "distinct-id",
            "$pageview",
            Arg.Is<Dictionary<string, object>>(captured =>
                !ReferenceEquals(captured, properties)
                && (string)captured["source"] == "test"
                && (string)captured["$current_url"] == "/pricing"),
            groups: null,
            flags: (FeatureFlagEvaluations?)null,
            timestamp: null);
    }

    [Fact]
    public void CaptureSurveyResponsesDoesNotMutateProvidedProperties()
    {
        var client = Substitute.For<IPostHogClient>();
        var properties = new Dictionary<string, object> { ["source"] = "test" };

        client.CaptureSurveyResponses(
            "distinct-id",
            "survey-id",
            ["first", "second"],
            properties);

        Assert.Equal(new Dictionary<string, object> { ["source"] = "test" }, properties);
        client.Received(1).Capture(
            "distinct-id",
            "survey sent",
            Arg.Is<Dictionary<string, object>>(captured =>
                !ReferenceEquals(captured, properties)
                && (string)captured["$survey_id"] == "survey-id"
                && (string)captured["$survey_response"] == "first"
                && (string)captured["survey_response_1"] == "second"),
            groups: null,
            flags: (FeatureFlagEvaluations?)null,
            timestamp: null);
    }

    static bool HasCopiedPersonProperties(
        Dictionary<string, object> captured,
        Dictionary<string, object> properties,
        Dictionary<string, object> personPropertiesToSet,
        Dictionary<string, object> personPropertiesToSetOnce)
        => !ReferenceEquals(captured, properties)
           && captured["$set"] is Dictionary<string, object> set
           && !ReferenceEquals(set, personPropertiesToSet)
           && (string)set["name"] == "Max"
           && captured["$set_once"] is Dictionary<string, object> setOnce
           && !ReferenceEquals(setOnce, personPropertiesToSetOnce)
           && (string)setOnce["initial_url"] == "/blog";
}
