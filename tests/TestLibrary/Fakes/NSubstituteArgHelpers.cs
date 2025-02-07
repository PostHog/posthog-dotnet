using NSubstitute;
using PostHog;

public static class Any
{
    public static string Strings => Arg.Any<string>();

    public static CancellationToken CancellationToken => Arg.Any<CancellationToken>();

    public static Dictionary<string, object> Properties => Arg.Any<Dictionary<string, object>>();

    public static GroupCollection Groups => Arg.Any<GroupCollection>();
}