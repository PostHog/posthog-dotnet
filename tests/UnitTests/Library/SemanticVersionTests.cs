using PostHog.Library;

namespace SemanticVersionTests;

public class TheTryParseMethod
{
    [Theory]
    // Basic valid versions
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("0.0.0", 0, 0, 0)]
    [InlineData("10.20.30", 10, 20, 30)]
    [InlineData("999.999.999", 999, 999, 999)]
    // v-prefix handling
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("V1.2.3", 1, 2, 3)]
    [InlineData("v0.0.1", 0, 0, 1)]
    // Whitespace handling
    [InlineData("  1.2.3  ", 1, 2, 3)]
    [InlineData("\t1.2.3\t", 1, 2, 3)]
    [InlineData("  v1.2.3  ", 1, 2, 3)]
    // Pre-release and build metadata stripping
    [InlineData("1.2.3-alpha", 1, 2, 3)]
    [InlineData("1.2.3-alpha.1", 1, 2, 3)]
    [InlineData("1.2.3+build", 1, 2, 3)]
    [InlineData("1.2.3+build.123", 1, 2, 3)]
    [InlineData("1.2.3-alpha+build", 1, 2, 3)]
    [InlineData("1.2.3-beta.2+build.456", 1, 2, 3)]
    [InlineData("v1.2.3-rc1", 1, 2, 3)]
    // Partial versions (missing components default to 0)
    [InlineData("1", 1, 0, 0)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("v1", 1, 0, 0)]
    [InlineData("v1.2", 1, 2, 0)]
    // Extra components beyond the third are ignored
    [InlineData("1.2.3.4", 1, 2, 3)]
    [InlineData("1.2.3.4.5", 1, 2, 3)]
    [InlineData("1.2.3.4.5.6", 1, 2, 3)]
    // Literal "0" components are valid per semver 2.0.0
    [InlineData("0.0.1", 0, 0, 1)]
    [InlineData("0.1.0", 0, 1, 0)]
    [InlineData("1.0.0", 1, 0, 0)]
    [InlineData("1.2.0", 1, 2, 0)]
    public void ParsesValidInputs(string input, int expectedMajor, int expectedMinor, int expectedPatch)
    {
        var result = SemanticVersion.TryParse(input, out var version);

        Assert.True(result);
        Assert.NotNull(version);
        Assert.Equal(expectedMajor, version.Value.Major);
        Assert.Equal(expectedMinor, version.Value.Minor);
        Assert.Equal(expectedPatch, version.Value.Patch);
    }

    [Theory]
    // Invalid inputs
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v")]
    [InlineData("V")]
    [InlineData(".1.2.3")]
    [InlineData("abc")]
    [InlineData("1.2.abc")]
    [InlineData("abc.2.3")]
    [InlineData("1.abc.3")]
    [InlineData("not-a-version")]
    [InlineData("..")]
    [InlineData("...")]
    [InlineData("1.-2.3")]    // Negative minor
    [InlineData("-1.2.3")]    // Negative major
    [InlineData("1.2.-3")]    // Negative patch
    [InlineData("01.02.03")]  // Leading zeros (all components)
    [InlineData("001.002.003")] // Multiple leading zeros
    [InlineData("1.07.3")]    // Leading zero in minor
    [InlineData("1.2.03")]    // Leading zero in patch
    [InlineData("01.2.3")]    // Leading zero in major
    [InlineData("00.0.0")]    // Leading zero on a zero major
    [InlineData("v01.2.3")]   // Leading zero with v-prefix
    [InlineData("01.2.3-alpha")]      // Leading zero + pre-release
    [InlineData("01.2.3+build")]      // Leading zero + build metadata
    [InlineData("  01.2.3  ")]        // Leading zero + outer whitespace
    [InlineData("1. 2.3")]            // Embedded whitespace in minor (NumberStyles.None rejects)
    [InlineData("١.2.3")]             // Arabic-Indic digit in major (InvariantCulture rejects)
    [InlineData("99999999999999.0.0")] // Overflows int.MaxValue
    public void ReturnsFalseForInvalidInput(string? input)
    {
        var result = SemanticVersion.TryParse(input, out var version);

        Assert.False(result);
        Assert.Null(version);
    }
}

public class TheCompareToMethod
{
    [Theory]
    // Equal versions
    [InlineData("1.2.3", "1.2.3", 0)]
    [InlineData("0.0.0", "0.0.0", 0)]
    [InlineData("v1.2.3", "1.2.3", 0)]
    [InlineData("1.2.3-alpha", "1.2.3", 0)] // Pre-release stripped, so equal
    // Greater than comparisons
    [InlineData("2.0.0", "1.0.0", 1)]
    [InlineData("1.2.0", "1.1.0", 1)]
    [InlineData("1.2.4", "1.2.3", 1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    [InlineData("1.0.0", "0.9.9", 1)]
    // Less than comparisons
    [InlineData("1.0.0", "2.0.0", -1)]
    [InlineData("1.1.0", "1.2.0", -1)]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("1.9.9", "2.0.0", -1)]
    [InlineData("0.9.9", "1.0.0", -1)]
    public void ComparesVersions(string left, string right, int expectedSign)
    {
        Assert.True(SemanticVersion.TryParse(left, out var leftVersion));
        Assert.True(SemanticVersion.TryParse(right, out var rightVersion));

        Assert.NotNull(leftVersion);
        Assert.NotNull(rightVersion);
        Assert.Equal(expectedSign, Math.Sign(leftVersion.Value.CompareTo(rightVersion.Value)));
    }
}

public class TheVersionRangeBoundsMethods
{
    [Theory]
    // ~X.Y.Z means >=X.Y.Z and <X.Y+1.0
    [InlineData("tilde", "1.2.3", "1.2.3", "1.3.0")]
    [InlineData("tilde", "1.0.0", "1.0.0", "1.1.0")]
    [InlineData("tilde", "0.2.3", "0.2.3", "0.3.0")]
    [InlineData("tilde", "0.0.1", "0.0.1", "0.1.0")]
    // ^X.Y.Z where X > 0 → >=X.Y.Z <X+1.0.0
    [InlineData("caret", "1.2.3", "1.2.3", "2.0.0")]
    [InlineData("caret", "1.0.0", "1.0.0", "2.0.0")]
    [InlineData("caret", "2.5.10", "2.5.10", "3.0.0")]
    // ^0.Y.Z where Y > 0 → >=0.Y.Z <0.Y+1.0
    [InlineData("caret", "0.2.3", "0.2.3", "0.3.0")]
    [InlineData("caret", "0.1.0", "0.1.0", "0.2.0")]
    [InlineData("caret", "0.5.10", "0.5.10", "0.6.0")]
    // ^0.0.Z → >=0.0.Z <0.0.Z+1
    [InlineData("caret", "0.0.3", "0.0.3", "0.0.4")]
    [InlineData("caret", "0.0.0", "0.0.0", "0.0.1")]
    [InlineData("caret", "0.0.10", "0.0.10", "0.0.11")]
    public void CalculatesCorrectBounds(string kind, string input, string expectedLower, string expectedUpper)
    {
        Assert.True(SemanticVersion.TryParse(input, out var version));
        Assert.True(SemanticVersion.TryParse(expectedLower, out var expectedLowerVersion));
        Assert.True(SemanticVersion.TryParse(expectedUpper, out var expectedUpperVersion));

        Assert.NotNull(version);
        Assert.NotNull(expectedLowerVersion);
        Assert.NotNull(expectedUpperVersion);

        var (lower, upper) = GetBounds(kind, version.Value);

        Assert.Equal(expectedLowerVersion.Value, lower);
        Assert.Equal(expectedUpperVersion.Value, upper);
    }

    [Theory]
    // Tilde range matching
    [InlineData("tilde", "1.2.3", "1.2.3", true)]
    [InlineData("tilde", "1.2.3", "1.2.4", true)]
    [InlineData("tilde", "1.2.3", "1.2.99", true)]
    [InlineData("tilde", "1.2.3", "1.3.0", false)]
    [InlineData("tilde", "1.2.3", "1.2.2", false)]
    [InlineData("tilde", "1.2.3", "2.0.0", false)]
    // Caret range matching with major > 0
    [InlineData("caret", "1.2.3", "1.2.3", true)]
    [InlineData("caret", "1.2.3", "1.2.4", true)]
    [InlineData("caret", "1.2.3", "1.9.9", true)]
    [InlineData("caret", "1.2.3", "2.0.0", false)]
    [InlineData("caret", "1.2.3", "1.2.2", false)]
    [InlineData("caret", "1.2.3", "3.0.0", false)]
    // Caret range matching with major = 0, minor > 0
    [InlineData("caret", "0.2.3", "0.2.3", true)]
    [InlineData("caret", "0.2.3", "0.2.4", true)]
    [InlineData("caret", "0.2.3", "0.2.99", true)]
    [InlineData("caret", "0.2.3", "0.3.0", false)]
    [InlineData("caret", "0.2.3", "0.2.2", false)]
    [InlineData("caret", "0.2.3", "1.0.0", false)]
    // Caret range matching with major = 0, minor = 0
    [InlineData("caret", "0.0.3", "0.0.3", true)]
    [InlineData("caret", "0.0.3", "0.0.4", false)]
    [InlineData("caret", "0.0.3", "0.0.2", false)]
    [InlineData("caret", "0.0.3", "0.1.0", false)]
    public void BoundsMatchCorrectly(string kind, string baseVersion, string testVersion, bool expectedInRange)
    {
        Assert.True(SemanticVersion.TryParse(baseVersion, out var baseVer));
        Assert.True(SemanticVersion.TryParse(testVersion, out var testVer));

        Assert.NotNull(baseVer);
        Assert.NotNull(testVer);

        var (lower, upper) = GetBounds(kind, baseVer.Value);
        var inRange = testVer.Value.IsInRange(lower, upper);

        Assert.Equal(expectedInRange, inRange);
    }

    static (SemanticVersion Lower, SemanticVersion Upper) GetBounds(string kind, SemanticVersion version)
        => kind switch
        {
            "tilde" => version.GetTildeBounds(),
            "caret" => version.GetCaretBounds(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
}

public class TheTryParseWildcardMethod
{
    [Theory]
    // "X.*" pattern → >=X.0.0 <X+1.0.0
    [InlineData("1.*", "1.0.0", "2.0.0")]
    [InlineData("2.*", "2.0.0", "3.0.0")]
    [InlineData("0.*", "0.0.0", "1.0.0")]
    [InlineData("v1.*", "1.0.0", "2.0.0")]
    // "X.Y.*" pattern → >=X.Y.0 <X.Y+1.0
    [InlineData("1.2.*", "1.2.0", "1.3.0")]
    [InlineData("1.0.*", "1.0.0", "1.1.0")]
    [InlineData("0.2.*", "0.2.0", "0.3.0")]
    [InlineData("v1.2.*", "1.2.0", "1.3.0")]
    // "X" pattern (without explicit wildcard) → >=X.0.0 <X+1.0.0
    [InlineData("1", "1.0.0", "2.0.0")]
    [InlineData("2", "2.0.0", "3.0.0")]
    // "X.Y" pattern (without explicit wildcard) → >=X.Y.0 <X.Y+1.0
    [InlineData("1.2", "1.2.0", "1.3.0")]
    [InlineData("0.5", "0.5.0", "0.6.0")]
    public void ParsesWildcardPatterns(string pattern, string expectedLower, string expectedUpper)
    {
        var result = SemanticVersion.TryParseWildcard(pattern, out var lower, out var upper);

        Assert.True(result);
        Assert.NotNull(lower);
        Assert.NotNull(upper);

        Assert.True(SemanticVersion.TryParse(expectedLower, out var expectedLowerVersion));
        Assert.True(SemanticVersion.TryParse(expectedUpper, out var expectedUpperVersion));

        Assert.Equal(expectedLowerVersion!.Value, lower.Value);
        Assert.Equal(expectedUpperVersion!.Value, upper.Value);
    }

    [Theory]
    // Invalid wildcard patterns
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("*")]        // Just wildcard alone
    [InlineData("1.2.3")]    // Full version (no wildcard)
    [InlineData("1.2.3.*")]  // Too specific
    [InlineData(".1.*")]     // Leading dot
    [InlineData("abc.*")]    // Non-numeric
    [InlineData("01.*")]     // Leading zero in major
    [InlineData("1.02.*")]   // Leading zero in minor
    [InlineData("01.2.*")]   // Leading zero in major (X.Y.*)
    [InlineData("01")]       // Leading zero in implicit major
    [InlineData("01.2")]     // Leading zero in implicit X.Y
    [InlineData("1.02")]     // Leading zero in implicit minor
    [InlineData("v01.*")]    // Leading zero with v-prefix
    [InlineData("001.*")]    // Multiple leading zeros
    public void ReturnsFalseForInvalidPatterns(string? pattern)
    {
        var result = SemanticVersion.TryParseWildcard(pattern, out var lower, out var upper);

        Assert.False(result);
        Assert.Null(lower);
        Assert.Null(upper);
    }

    [Theory]
    // Test wildcard range matching
    [InlineData("1.*", "1.0.0", true)]
    [InlineData("1.*", "1.5.3", true)]
    [InlineData("1.*", "1.99.99", true)]
    [InlineData("1.*", "2.0.0", false)]
    [InlineData("1.*", "0.9.9", false)]
    [InlineData("1.2.*", "1.2.0", true)]
    [InlineData("1.2.*", "1.2.99", true)]
    [InlineData("1.2.*", "1.3.0", false)]
    [InlineData("1.2.*", "1.1.99", false)]
    public void WildcardRangeMatchesCorrectly(string pattern, string testVersion, bool expectedInRange)
    {
        var parseResult = SemanticVersion.TryParseWildcard(pattern, out var lower, out var upper);

        Assert.True(parseResult);
        Assert.NotNull(lower);
        Assert.NotNull(upper);

        Assert.True(SemanticVersion.TryParse(testVersion, out var testVer));
        Assert.NotNull(testVer);

        var inRange = testVer.Value.IsInRange(lower.Value, upper.Value);

        Assert.Equal(expectedInRange, inRange);
    }
}

public class TheOperatorOverloads
{
    [Theory]
    [InlineData("<", "1.2.3", "1.2.4", true)]
    [InlineData("<", "1.2.3", "1.2.3", false)]
    [InlineData("<", "1.2.4", "1.2.3", false)]
    [InlineData("<=", "1.2.3", "1.2.4", true)]
    [InlineData("<=", "1.2.3", "1.2.3", true)]
    [InlineData("<=", "1.2.4", "1.2.3", false)]
    [InlineData(">", "1.2.4", "1.2.3", true)]
    [InlineData(">", "1.2.3", "1.2.3", false)]
    [InlineData(">", "1.2.3", "1.2.4", false)]
    [InlineData(">=", "1.2.4", "1.2.3", true)]
    [InlineData(">=", "1.2.3", "1.2.3", true)]
    [InlineData(">=", "1.2.3", "1.2.4", false)]
    public void ComparisonOperatorsWork(string operatorName, string left, string right, bool expected)
    {
        Assert.True(SemanticVersion.TryParse(left, out var leftVersion));
        Assert.True(SemanticVersion.TryParse(right, out var rightVersion));

        Assert.NotNull(leftVersion);
        Assert.NotNull(rightVersion);

        var actual = operatorName switch
        {
            "<" => leftVersion.Value < rightVersion.Value,
            "<=" => leftVersion.Value <= rightVersion.Value,
            ">" => leftVersion.Value > rightVersion.Value,
            ">=" => leftVersion.Value >= rightVersion.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(operatorName), operatorName, null)
        };
        Assert.Equal(expected, actual);
    }
}

public class TheToStringMethod
{
    [Theory]
    [InlineData(1, 2, 3, "1.2.3")]
    [InlineData(0, 0, 0, "0.0.0")]
    [InlineData(10, 20, 30, "10.20.30")]
    public void ReturnsCorrectFormat(int major, int minor, int patch, string expected)
    {
        var version = new SemanticVersion(major, minor, patch);

        Assert.Equal(expected, version.ToString());
    }
}
