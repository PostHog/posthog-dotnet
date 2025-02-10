using PostHog.Library;

/// <summary>
/// An implementation of <see cref="ITaskScheduler"/> that runs immediately.
/// </summary>
public class FakeTaskScheduler : ITaskScheduler
{
    /// <summary>
    /// Just run that sucker immediately.
    /// </summary>
    /// <param name="action">The action to run.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    public void Run(Action action, CancellationToken cancellationToken) => action();

    /// <summary>
    /// Just run that sucker immediately.
    /// </summary>
    /// <param name="task">The action to run.</param>
    /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
    public Task Run(Func<Task> task, CancellationToken cancellationToken = default) => task();
}