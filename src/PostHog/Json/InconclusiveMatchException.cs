namespace PostHog.Json;

/// <summary>
/// Exception thrown when a match is inconclusive. This is thrown internally and should not be thrown by any
/// public methods of the client.
/// </summary>
public class InconclusiveMatchException : Exception
{
    public InconclusiveMatchException(string message) : base(message)
    {
    }

    public InconclusiveMatchException()
    {
    }

    public InconclusiveMatchException(string message, Exception innerException) : base(message, innerException)
    {
    }
}