namespace PostHog.Json;

/// <summary>
/// Exception thrown when a match is inconclusive. This is thrown internally and should not be thrown by any
/// public methods of the client.
/// </summary>
public class InconclusiveMatchException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InconclusiveMatchException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public InconclusiveMatchException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InconclusiveMatchException"/> class.
    /// </summary>
    public InconclusiveMatchException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InconclusiveMatchException"/> class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused the current exception.</param>
    public InconclusiveMatchException(string message, Exception innerException) : base(message, innerException)
    {
    }
}