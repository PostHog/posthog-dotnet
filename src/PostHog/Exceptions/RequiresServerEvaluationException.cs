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
        public RequiresServerEvaluationException()
        {
        }

        public RequiresServerEvaluationException(string message) : base(message)
        {
        }

        public RequiresServerEvaluationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
