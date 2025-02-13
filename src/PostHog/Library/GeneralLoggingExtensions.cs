using Microsoft.Extensions.Logging;

namespace PostHog.Library;

public static partial class GeneralLoggingExtensions
{
    [LoggerMessage(
        EventId = 500,
        Level = LogLevel.Error,
        Message = "Unexpected exception occurred.")]
    public static partial void LogErrorUnexpectedException(this ILogger logger, Exception exception);
}