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
    public void ParsesValidVersions(string input, int expectedMajor, int expectedMinor, int expectedPatch)
    {
        var result = SemanticVersion.TryParse(input, out var version);

        Assert.True(result);
        Assert.NotNull(version);
        Assert.Equal(expectedMajor, version.Value.Major);
        Assert.Equal(expectedMinor, version.Value.Minor);
        Assert.Equal(expectedPatch, version.Value.Patch);
    }

    [Theory]
    // v-prefix handling
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("V1.2.3", 1, 2, 3)]
    [InlineData("v0.0.1", 0, 0, 1)]
    public void StripsVPrefix(string input, int expectedMajor, int expectedMinor, int expectedPatch)
    {
        var result = SemanticVersion.TryParse(input, out var version);

        Assert.True(result);
        Assert.NotNull(version);
        Assert.Equal(expectedMajor, version.Value.Major);
        Assert.Equal(expectedMinor, version.Value.Minor);
        Assert.Equal(expectedPatch, version.Value.Patch);
    }

    [Theory]
    // Whitespace handling
    [InlineData("  1.2.3  ", 1, 2, 3)]
    [InlineData("\t1.2.3\t", 1, 2, 3)]
    [InlineData("  v1.2.3  ", 1, 2, 3)]
    public void StripsWhitespace(string input, int expectedMajor, int expectedMinor, int expectedPatch)
    {
        var result = SemanticVersion.TryParse(input, out var version);

        Assert.True(result);
        Assert.NotNull(version);
        Assert.Equal(expectedMajor, version.Value.Major);
        Assert.Equal(expectedMinor, version.Value.Minor);
        Assert.Equal(expectedPatch, version.Value.Patch);
    }

    [Theory]
    // Pre-release and build metadata stripping
    [InlineData("1.2.3-alpha", 1, 2, 3)]
    [InlineData("1.2.3-alpha.1", 1, 2, 3)]
    [InlineData("1.2.3+build", 1, 2, 3)]
    [InlineData("1.2.3+build.123", 1, 2, 3)]
    [InlineData("1.2.3-alpha+build", 1, 2, 3)]
    [InlineData("1.2.3-beta.2+build.456", 1, 2, 3)]
    [InlineData("v1.2.3-rc1", 1, 2, 3)]
    public void StripsPreReleaseAndBuildMetadata(string input, int expectedMajor, int expectedMinor, int expectedPatch)
    {
        var result = SemanticVersion.TryParse(input, out var version);

        Assert.True(result);
        Assert.NotNull(version);
        Assert.Equal(expectedMajor, version.Value.Major);
        Assert.Equal(expectedMinor, version.Value.Minor);
        Assert.Equal(expectedPatch, version.Value.Patch);
    }

    [Theory]
    // Partial versions (missing components default to 0)
    [InlineData("1", 1, 0, 0)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("v1", 1, 0, 0)]
    [InlineData("v1.2", 1, 2, 0)]
    public void DefaultsMissingComponentsToZero(string input, int expectedMajor, int expectedMinor, int expectedPatch)
    {
        var result = SemanticVersion.TryParse(input, out var version);

        Assert.True(result);
        Assert.NotNull(version);
        Assert.Equal(expectedMajor, version.Value.Major);
        Assert.Equal(expectedMinor, version.Value.Minor);
        Assert.Equal(expectedPatch, version.Value.Patch);
    }

    [Theory]
    // Extra components beyond the third are ignored
    [InlineData("1.2.3.4", 1, 2, 3)]
    [InlineData("1.2.3.4.5", 1, 2, 3)]
    [InlineData("1.2.3.4.5.6", 1, 2, 3)]
    public void IgnoresExtraComponents(string input, int expectedMajor, int expectedMinor, int expectedPatch)
    {
        var result = SemanticVersion.TryParse(input, out var version);

        Assert.True(result);
        Assert.NotNull(version);
        Assert.Equal(expectedMajor, version.Value.Major);
        Assert.Equal(expectedMinor, version.Value.Minor);
        Assert.Equal(expectedPatch, version.Value.Patch);
    }

    [Theory]
    // Leading zeros are parsed as integers
    [InlineData("01.02.03", 1, 2, 3)]
    [InlineData("001.002.003", 1, 2, 3)]
    public void ParsesLeadingZeros(string input, int expectedMajor, int expectedMinor, int expectedPatch)
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
    public void ReturnsZeroForEqualVersions(string left, string right, int expected)
    {
        Assert.True(SemanticVersion.TryParse(left, out var leftVersion));
        Assert.True(SemanticVersion.TryParse(right, out var rightVersion));

        Assert.NotNull(leftVersion);
        Assert.NotNull(rightVersion);
        Assert.Equal(expected, leftVersion.Value.CompareTo(rightVersion.Value));
    }

    [Theory]
    // Greater than comparisons
    [InlineData("2.0.0", "1.0.0", 1)]
    [InlineData("1.2.0", "1.1.0", 1)]
    [InlineData("1.2.4", "1.2.3", 1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    [InlineData("1.0.0", "0.9.9", 1)]
    public void ReturnsPositiveWhenLeftIsGreater(string left, string right, int expected)
    {
        Assert.True(SemanticVersion.TryParse(left, out var leftVersion));
        Assert.True(SemanticVersion.TryParse(right, out var rightVersion));

        Assert.NotNull(leftVersion);
        Assert.NotNull(rightVersion);
        Assert.Equal(expected, Math.Sign(leftVersion.Value.CompareTo(rightVersion.Value)));
    }

    [Theory]
    // Less than comparisons
    [InlineData("1.0.0", "2.0.0", -1)]
    [InlineData("1.1.0", "1.2.0", -1)]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("1.9.9", "2.0.0", -1)]
    [InlineData("0.9.9", "1.0.0", -1)]
    public void ReturnsNegativeWhenLeftIsLess(string left, string right, int expected)
    {
        Assert.True(SemanticVersion.TryParse(left, out var leftVersion));
        Assert.True(SemanticVersion.TryParse(right, out var rightVersion));

        Assert.NotNull(leftVersion);
        Assert.NotNull(rightVersion);
        Assert.Equal(expected, Math.Sign(leftVersion.Value.CompareTo(rightVersion.Value)));
    }
}

public class TheGetTildeBoundsMethod
{
    [Theory]
    // ~X.Y.Z means >=X.Y.Z and <X.Y+1.0
    [InlineData("1.2.3", "1.2.3", "1.3.0")]
    [InlineData("1.0.0", "1.0.0", "1.1.0")]
    [InlineData("0.2.3", "0.2.3", "0.3.0")]
    [InlineData("0.0.1", "0.0.1", "0.1.0")]
    public void CalculatesCorrectTildeBounds(string input, string expectedLower, string expectedUpper)
    {
        Assert.True(SemanticVersion.TryParse(input, out var version));
        Assert.True(SemanticVersion.TryParse(expectedLower, out var expectedLowerVersion));
        Assert.True(SemanticVersion.TryParse(expectedUpper, out var expectedUpperVersion));

        Assert.NotNull(version);
        Assert.NotNull(expectedLowerVersion);
        Assert.NotNull(expectedUpperVersion);

        var (lower, upper) = version.Value.GetTildeBounds();

        Assert.Equal(expectedLowerVersion.Value, lower);
        Assert.Equal(expectedUpperVersion.Value, upper);
    }

    [Theory]
    // Test range matching for tilde
    [InlineData("1.2.3", "1.2.3", true)]  // At lower bound
    [InlineData("1.2.3", "1.2.4", true)]  // Within range
    [InlineData("1.2.3", "1.2.99", true)] // Within range
    [InlineData("1.2.3", "1.3.0", false)] // At upper bound (exclusive)
    [InlineData("1.2.3", "1.2.2", false)] // Below range
    [InlineData("1.2.3", "2.0.0", false)] // Above range
    public void TildeBoundsMatchCorrectly(string baseVersion, string testVersion, bool expectedInRange)
    {
        Assert.True(SemanticVersion.TryParse(baseVersion, out var baseVer));
        Assert.True(SemanticVersion.TryParse(testVersion, out var testVer));

        Assert.NotNull(baseVer);
        Assert.NotNull(testVer);

        var (lower, upper) = baseVer.Value.GetTildeBounds();
        var inRange = testVer.Value.IsInRange(lower, upper);

        Assert.Equal(expectedInRange, inRange);
    }
}

public class TheGetCaretBoundsMethod
{
    [Theory]
    // ^X.Y.Z where X > 0 → >=X.Y.Z <X+1.0.0
    [InlineData("1.2.3", "1.2.3", "2.0.0")]
    [InlineData("1.0.0", "1.0.0", "2.0.0")]
    [InlineData("2.5.10", "2.5.10", "3.0.0")]
    public void CalculatesCorrectCaretBoundsForMajorGreaterThanZero(string input, string expectedLower, string expectedUpper)
    {
        Assert.True(SemanticVersion.TryParse(input, out var version));
        Assert.True(SemanticVersion.TryParse(expectedLower, out var expectedLowerVersion));
        Assert.True(SemanticVersion.TryParse(expectedUpper, out var expectedUpperVersion));

        Assert.NotNull(version);
        Assert.NotNull(expectedLowerVersion);
        Assert.NotNull(expectedUpperVersion);

        var (lower, upper) = version.Value.GetCaretBounds();

        Assert.Equal(expectedLowerVersion.Value, lower);
        Assert.Equal(expectedUpperVersion.Value, upper);
    }

    [Theory]
    // ^0.Y.Z where Y > 0 → >=0.Y.Z <0.Y+1.0
    [InlineData("0.2.3", "0.2.3", "0.3.0")]
    [InlineData("0.1.0", "0.1.0", "0.2.0")]
    [InlineData("0.5.10", "0.5.10", "0.6.0")]
    public void CalculatesCorrectCaretBoundsForMajorZeroMinorGreaterThanZero(string input, string expectedLower, string expectedUpper)
    {
        Assert.True(SemanticVersion.TryParse(input, out var version));
        Assert.True(SemanticVersion.TryParse(expectedLower, out var expectedLowerVersion));
        Assert.True(SemanticVersion.TryParse(expectedUpper, out var expectedUpperVersion));

        Assert.NotNull(version);
        Assert.NotNull(expectedLowerVersion);
        Assert.NotNull(expectedUpperVersion);

        var (lower, upper) = version.Value.GetCaretBounds();

        Assert.Equal(expectedLowerVersion.Value, lower);
        Assert.Equal(expectedUpperVersion.Value, upper);
    }

    [Theory]
    // ^0.0.Z → >=0.0.Z <0.0.Z+1
    [InlineData("0.0.3", "0.0.3", "0.0.4")]
    [InlineData("0.0.0", "0.0.0", "0.0.1")]
    [InlineData("0.0.10", "0.0.10", "0.0.11")]
    public void CalculatesCorrectCaretBoundsForMajorAndMinorZero(string input, string expectedLower, string expectedUpper)
    {
        Assert.True(SemanticVersion.TryParse(input, out var version));
        Assert.True(SemanticVersion.TryParse(expectedLower, out var expectedLowerVersion));
        Assert.True(SemanticVersion.TryParse(expectedUpper, out var expectedUpperVersion));

        Assert.NotNull(version);
        Assert.NotNull(expectedLowerVersion);
        Assert.NotNull(expectedUpperVersion);

        var (lower, upper) = version.Value.GetCaretBounds();

        Assert.Equal(expectedLowerVersion.Value, lower);
        Assert.Equal(expectedUpperVersion.Value, upper);
    }

    [Theory]
    // Test range matching for caret with major > 0
    [InlineData("1.2.3", "1.2.3", true)]   // At lower bound
    [InlineData("1.2.3", "1.2.4", true)]   // Within range
    [InlineData("1.2.3", "1.9.9", true)]   // Within range
    [InlineData("1.2.3", "2.0.0", false)]  // At upper bound (exclusive)
    [InlineData("1.2.3", "1.2.2", false)]  // Below range
    [InlineData("1.2.3", "3.0.0", false)]  // Above range
    public void CaretBoundsMatchCorrectlyForMajorGreaterThanZero(string baseVersion, string testVersion, bool expectedInRange)
    {
        Assert.True(SemanticVersion.TryParse(baseVersion, out var baseVer));
        Assert.True(SemanticVersion.TryParse(testVersion, out var testVer));

        Assert.NotNull(baseVer);
        Assert.NotNull(testVer);

        var (lower, upper) = baseVer.Value.GetCaretBounds();
        var inRange = testVer.Value.IsInRange(lower, upper);

        Assert.Equal(expectedInRange, inRange);
    }

    [Theory]
    // Test range matching for caret with major = 0, minor > 0
    [InlineData("0.2.3", "0.2.3", true)]   // At lower bound
    [InlineData("0.2.3", "0.2.4", true)]   // Within range
    [InlineData("0.2.3", "0.2.99", true)]  // Within range
    [InlineData("0.2.3", "0.3.0", false)]  // At upper bound (exclusive)
    [InlineData("0.2.3", "0.2.2", false)]  // Below range
    [InlineData("0.2.3", "1.0.0", false)]  // Above range
    public void CaretBoundsMatchCorrectlyForMajorZeroMinorGreaterThanZero(string baseVersion, string testVersion, bool expectedInRange)
    {
        Assert.True(SemanticVersion.TryParse(baseVersion, out var baseVer));
        Assert.True(SemanticVersion.TryParse(testVersion, out var testVer));

        Assert.NotNull(baseVer);
        Assert.NotNull(testVer);

        var (lower, upper) = baseVer.Value.GetCaretBounds();
        var inRange = testVer.Value.IsInRange(lower, upper);

        Assert.Equal(expectedInRange, inRange);
    }

    [Theory]
    // Test range matching for caret with major = 0, minor = 0
    [InlineData("0.0.3", "0.0.3", true)]   // At lower bound
    [InlineData("0.0.3", "0.0.4", false)]  // At upper bound (exclusive)
    [InlineData("0.0.3", "0.0.2", false)]  // Below range
    [InlineData("0.0.3", "0.1.0", false)]  // Above range
    public void CaretBoundsMatchCorrectlyForMajorAndMinorZero(string baseVersion, string testVersion, bool expectedInRange)
    {
        Assert.True(SemanticVersion.TryParse(baseVersion, out var baseVer));
        Assert.True(SemanticVersion.TryParse(testVersion, out var testVer));

        Assert.NotNull(baseVer);
        Assert.NotNull(testVer);

        var (lower, upper) = baseVer.Value.GetCaretBounds();
        var inRange = testVer.Value.IsInRange(lower, upper);

        Assert.Equal(expectedInRange, inRange);
    }
}

public class TheTryParseWildcardMethod
{
    [Theory]
    // "X.*" pattern → >=X.0.0 <X+1.0.0
    [InlineData("1.*", "1.0.0", "2.0.0")]
    [InlineData("2.*", "2.0.0", "3.0.0")]
    [InlineData("0.*", "0.0.0", "1.0.0")]
    [InlineData("v1.*", "1.0.0", "2.0.0")]
    public void ParsesXWildcardPattern(string pattern, string expectedLower, string expectedUpper)
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
    // "X.Y.*" pattern → >=X.Y.0 <X.Y+1.0
    [InlineData("1.2.*", "1.2.0", "1.3.0")]
    [InlineData("1.0.*", "1.0.0", "1.1.0")]
    [InlineData("0.2.*", "0.2.0", "0.3.0")]
    [InlineData("v1.2.*", "1.2.0", "1.3.0")]
    public void ParsesXYWildcardPattern(string pattern, string expectedLower, string expectedUpper)
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
    // "X" pattern (without explicit wildcard) → >=X.0.0 <X+1.0.0
    [InlineData("1", "1.0.0", "2.0.0")]
    [InlineData("2", "2.0.0", "3.0.0")]
    public void ParsesImplicitMajorWildcard(string pattern, string expectedLower, string expectedUpper)
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
    // "X.Y" pattern (without explicit wildcard) → >=X.Y.0 <X.Y+1.0
    [InlineData("1.2", "1.2.0", "1.3.0")]
    [InlineData("0.5", "0.5.0", "0.6.0")]
    public void ParsesImplicitMinorWildcard(string pattern, string expectedLower, string expectedUpper)
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
    [InlineData("1.2.3", "1.2.4", true)]
    [InlineData("1.2.3", "1.2.3", false)]
    [InlineData("1.2.4", "1.2.3", false)]
    public void LessThanOperatorWorks(string left, string right, bool expected)
    {
        Assert.True(SemanticVersion.TryParse(left, out var leftVersion));
        Assert.True(SemanticVersion.TryParse(right, out var rightVersion));

        Assert.NotNull(leftVersion);
        Assert.NotNull(rightVersion);
        Assert.Equal(expected, leftVersion.Value < rightVersion.Value);
    }

    [Theory]
    [InlineData("1.2.3", "1.2.4", true)]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("1.2.4", "1.2.3", false)]
    public void LessThanOrEqualOperatorWorks(string left, string right, bool expected)
    {
        Assert.True(SemanticVersion.TryParse(left, out var leftVersion));
        Assert.True(SemanticVersion.TryParse(right, out var rightVersion));

        Assert.NotNull(leftVersion);
        Assert.NotNull(rightVersion);
        Assert.Equal(expected, leftVersion.Value <= rightVersion.Value);
    }

    [Theory]
    [InlineData("1.2.4", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.3", false)]
    [InlineData("1.2.3", "1.2.4", false)]
    public void GreaterThanOperatorWorks(string left, string right, bool expected)
    {
        Assert.True(SemanticVersion.TryParse(left, out var leftVersion));
        Assert.True(SemanticVersion.TryParse(right, out var rightVersion));

        Assert.NotNull(leftVersion);
        Assert.NotNull(rightVersion);
        Assert.Equal(expected, leftVersion.Value > rightVersion.Value);
    }

    [Theory]
    [InlineData("1.2.4", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.4", false)]
    public void GreaterThanOrEqualOperatorWorks(string left, string right, bool expected)
    {
        Assert.True(SemanticVersion.TryParse(left, out var leftVersion));
        Assert.True(SemanticVersion.TryParse(right, out var rightVersion));

        Assert.NotNull(leftVersion);
        Assert.NotNull(rightVersion);
        Assert.Equal(expected, leftVersion.Value >= rightVersion.Value);
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
