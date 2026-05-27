using System;

namespace PostHog.Exceptions
{
    /// <summary>
    /// Exception thrown when feature flag evaluation requires server-side data
    /// that is not available locally (e.g., static cohorts, experience continuity).
    ///
    /// This exception should propagate immediately to trigger API fallback, unlike
    /// InconclusiveMatchException which allows trying other conditions.
    /// </summary>
    public class RequiresServerEvaluationException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RequiresServerEvaluationException"/> class.
        /// </summary>
        public RequiresServerEvaluationException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RequiresServerEvaluationException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public RequiresServerEvaluationException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RequiresServerEvaluationException"/> class with a specified error message and inner exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that caused the current exception.</param>
        public RequiresServerEvaluationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
