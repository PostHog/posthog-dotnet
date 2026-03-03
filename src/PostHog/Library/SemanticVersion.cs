using System.Diagnostics.CodeAnalysis;

namespace PostHog.Library;

/// <summary>
/// Represents a semantic version (major.minor.patch) for comparison purposes.
/// </summary>
internal readonly record struct SemanticVersion : IComparable<SemanticVersion>
{
    /// <summary>
    /// The major version component.
    /// </summary>
    public int Major { get; }

    /// <summary>
    /// The minor version component.
    /// </summary>
    public int Minor { get; }

    /// <summary>
    /// The patch version component.
    /// </summary>
    public int Patch { get; }

    /// <summary>
    /// Creates a new <see cref="SemanticVersion"/> with the specified components.
    /// </summary>
    public SemanticVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    /// <summary>
    /// Tries to parse a version string into a <see cref="SemanticVersion"/>.
    /// </summary>
    /// <param name="value">The version string to parse.</param>
    /// <param name="version">The resulting <see cref="SemanticVersion"/> if parsing succeeds.</param>
    /// <returns><c>true</c> if parsing was successful; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Parsing rules:
    /// 1. Strip leading/trailing whitespace
    /// 2. Strip 'v' or 'V' prefix
    /// 3. Strip pre-release and build metadata (split on '-' or '+', take first part)
    /// 4. Split on '.' and parse first 3 components as integers
    /// 5. Default missing components to 0 (e.g., "1.2" → (1, 2, 0), "1" → (1, 0, 0))
    /// 6. Ignore extra components beyond the third (e.g., "1.2.3.4" → (1, 2, 3))
    /// 7. Return false for truly invalid input (empty string, non-numeric parts, leading dot)
    /// </remarks>
    public static bool TryParse(string? value, [NotNullWhen(returnValue: true)] out SemanticVersion? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // Strip leading/trailing whitespace
        // value is guaranteed non-null here since IsNullOrWhiteSpace returned false
        var trimmed = value!.Trim();

        // Strip 'v' or 'V' prefix
        if (trimmed.Length > 0 && (trimmed[0] == 'v' || trimmed[0] == 'V'))
        {
            trimmed = trimmed[1..];
        }

        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        // Strip pre-release and build metadata (split on '-' or '+', take first part)
#pragma warning disable CA1865 // Use char overload - but CA1307 requires StringComparison
        var hyphenIndex = trimmed.IndexOf("-", StringComparison.Ordinal);
        var plusIndex = trimmed.IndexOf("+", StringComparison.Ordinal);
#pragma warning restore CA1865

        var metadataIndex = -1;
        if (hyphenIndex >= 0 && plusIndex >= 0)
        {
            metadataIndex = Math.Min(hyphenIndex, plusIndex);
        }
        else if (hyphenIndex >= 0)
        {
            metadataIndex = hyphenIndex;
        }
        else if (plusIndex >= 0)
        {
            metadataIndex = plusIndex;
        }

        if (metadataIndex >= 0)
        {
            trimmed = trimmed[..metadataIndex];
        }

        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        // Check for leading dot (invalid)
        if (trimmed[0] == '.')
        {
            return false;
        }

        // Split on '.' and parse components
        var parts = trimmed.Split('.');

        // Parse major (required)
        if (!int.TryParse(parts[0], out var major))
        {
            return false;
        }

        // Parse minor (optional, defaults to 0)
        var minor = 0;
        if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]) && !int.TryParse(parts[1], out minor))
        {
            return false;
        }

        // Parse patch (optional, defaults to 0)
        var patch = 0;
        if (parts.Length > 2 && !string.IsNullOrEmpty(parts[2]) && !int.TryParse(parts[2], out patch))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch);
        return true;
    }

    /// <summary>
    /// Compares this version to another version.
    /// </summary>
    public int CompareTo(SemanticVersion other)
    {
        var majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0)
        {
            return majorComparison;
        }

        var minorComparison = Minor.CompareTo(other.Minor);
        if (minorComparison != 0)
        {
            return minorComparison;
        }

        return Patch.CompareTo(other.Patch);
    }

    /// <summary>
    /// Computes the tilde range bounds for this version.
    /// ~X.Y.Z means >=X.Y.Z and &lt;X.Y+1.0
    /// </summary>
    /// <returns>A tuple of (lower, upper) bounds where lower is inclusive and upper is exclusive.</returns>
    public (SemanticVersion Lower, SemanticVersion Upper) GetTildeBounds()
    {
        var lower = this;
        var upper = new SemanticVersion(Major, Minor + 1, 0);
        return (lower, upper);
    }

    /// <summary>
    /// Computes the caret range bounds for this version.
    /// ^X.Y.Z is compatible-with per semver spec:
    /// - ^1.2.3 means >=1.2.3 &lt;2.0.0 (major > 0)
    /// - ^0.2.3 means >=0.2.3 &lt;0.3.0 (major = 0, minor > 0)
    /// - ^0.0.3 means >=0.0.3 &lt;0.0.4 (major = 0, minor = 0)
    /// </summary>
    /// <returns>A tuple of (lower, upper) bounds where lower is inclusive and upper is exclusive.</returns>
    public (SemanticVersion Lower, SemanticVersion Upper) GetCaretBounds()
    {
        var lower = this;
        SemanticVersion upper;

        if (Major > 0)
        {
            // ^1.2.3 → >=1.2.3 <2.0.0
            upper = new SemanticVersion(Major + 1, 0, 0);
        }
        else if (Minor > 0)
        {
            // ^0.2.3 → >=0.2.3 <0.3.0
            upper = new SemanticVersion(0, Minor + 1, 0);
        }
        else
        {
            // ^0.0.3 → >=0.0.3 <0.0.4
            upper = new SemanticVersion(0, 0, Patch + 1);
        }

        return (lower, upper);
    }

    /// <summary>
    /// Tries to parse a wildcard pattern and compute its bounds.
    /// "X.*" or "X" means >=X.0.0 &lt;X+1.0.0
    /// "X.Y.*" means >=X.Y.0 &lt;X.Y+1.0
    /// </summary>
    /// <param name="pattern">The wildcard pattern to parse.</param>
    /// <param name="lower">The lower bound (inclusive).</param>
    /// <param name="upper">The upper bound (exclusive).</param>
    /// <returns><c>true</c> if the pattern was successfully parsed; otherwise <c>false</c>.</returns>
    public static bool TryParseWildcard(
        string? pattern,
        [NotNullWhen(returnValue: true)] out SemanticVersion? lower,
        [NotNullWhen(returnValue: true)] out SemanticVersion? upper)
    {
        lower = null;
        upper = null;

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        // pattern is guaranteed non-null here since IsNullOrWhiteSpace returned false
        var trimmed = pattern!.Trim();

        // Strip 'v' or 'V' prefix
        if (trimmed.Length > 0 && (trimmed[0] == 'v' || trimmed[0] == 'V'))
        {
            trimmed = trimmed[1..];
        }

        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        var parts = trimmed.Split('.');

        // Check for leading dot (invalid)
        if (trimmed[0] == '.')
        {
            return false;
        }

        // Parse based on the pattern structure
        if (parts.Length == 1)
        {
            // Could be "X" or "X.*" pattern without the dot
            // Actually "1" is valid and means "1.*"
            if (!int.TryParse(parts[0], out var major))
            {
                // Check if it's a wildcard itself
                if (parts[0] == "*")
                {
                    // "*" alone is invalid for our purposes
                    return false;
                }
                return false;
            }

            lower = new SemanticVersion(major, 0, 0);
            upper = new SemanticVersion(major + 1, 0, 0);
            return true;
        }
        else if (parts.Length == 2)
        {
            // "X.Y" or "X.*" pattern
            if (!int.TryParse(parts[0], out var major))
            {
                return false;
            }

            if (parts[1] == "*")
            {
                // "X.*" pattern
                lower = new SemanticVersion(major, 0, 0);
                upper = new SemanticVersion(major + 1, 0, 0);
                return true;
            }

            if (!int.TryParse(parts[1], out var minor))
            {
                return false;
            }

            // "X.Y" without wildcard - treat as "X.Y.*"
            lower = new SemanticVersion(major, minor, 0);
            upper = new SemanticVersion(major, minor + 1, 0);
            return true;
        }
        else if (parts.Length >= 3)
        {
            // "X.Y.Z" or "X.Y.*" pattern
            if (!int.TryParse(parts[0], out var major))
            {
                return false;
            }

            if (!int.TryParse(parts[1], out var minor))
            {
                return false;
            }

            if (parts[2] == "*")
            {
                // "X.Y.*" pattern
                lower = new SemanticVersion(major, minor, 0);
                upper = new SemanticVersion(major, minor + 1, 0);
                return true;
            }

            // "X.Y.Z" is not a wildcard pattern
            return false;
        }

        return false;
    }

    /// <summary>
    /// Checks if this version is within the specified range [lower, upper).
    /// </summary>
    /// <param name="lower">The lower bound (inclusive).</param>
    /// <param name="upper">The upper bound (exclusive).</param>
    /// <returns><c>true</c> if this version is >= lower and &lt; upper.</returns>
    public bool IsInRange(SemanticVersion lower, SemanticVersion upper)
    {
        return CompareTo(lower) >= 0 && CompareTo(upper) < 0;
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
