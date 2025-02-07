#if NETSTANDARD2_0 || NETSTANDARD2_1
namespace TestLibrary.Fakes.Polyfills;

public class TaskCompletionSource : TaskCompletionSource<object>
{
    public void SetResult() => base.SetResult(new object());
}
#endif