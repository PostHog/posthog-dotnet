using Xunit.Abstractions;

namespace TestLibrary.Fakes;

public class ConsoleTestOutputHelper : ITestOutputHelper
{
    public bool Enabled { get; set; }

    public void WriteLine(string message)
    {
        if (Enabled)
        {
            Console.WriteLine(message);
        }
    }


    public void WriteLine(string format, params object[] args)
    {
        if (Enabled)
        {
            Console.WriteLine(format, args);
        }
    }
}