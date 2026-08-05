using System.Globalization;

namespace UnitTests.Library;

/// <summary>
/// Provides a scoped override of the current culture for tests of culture-sensitive behavior.
/// </summary>
public static class TestCulture
{
    /// <summary>
    /// Sets <see cref="CultureInfo.CurrentCulture"/> to the specified culture until the returned scope is
    /// disposed. For example, "de-DE" formats 3.14 as "3,14", which exercises culture-sensitive formatting.
    /// </summary>
    /// <param name="cultureName">The name of the culture, e.g. "de-DE".</param>
    /// <returns>A scope that restores the original culture when disposed.</returns>
    public static IDisposable Use(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(cultureName);
        return Disposable.Create(() => CultureInfo.CurrentCulture = originalCulture);
    }
}
