using System.Runtime.InteropServices;

namespace PostHog.Library;

/// <summary>
/// Provides human friendly version information about the current operating system.
/// </summary>
public static class OperatingSystemInfo
{
    public static string Name { get; private set; }
    public static string Version { get; private set; }

    /// <summary>
    /// Initializes static properties of <see cref="OperatingSystemInfo"/> for the current operating system.
    /// </summary>
    /// <remarks>
    /// Determines the operating system platform and version at runtime, attemps to map out the sometimes overlapping
    /// values that .NET provides and assigns the corresponding values to the <c>Name</c> and <c>Version</c> properties.
    /// Currently, we detect between different Windows / Windows Server versions (after Windows 2000), and simple Linux or macOS.
    /// For unsupported platforms, the <c>Name</c> is set to "Unknown" and the <c>Version</c> is set to the value
    /// of <see cref="Environment.OSVersion.VersionString"/>.
    /// </remarks>
    static OperatingSystemInfo()
    {
        Name = Environment.OSVersion.Platform.ToString();
        Version = Environment.OSVersion.VersionString;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetWindowsVersionInfo();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Name = "Linux";
            Version = Environment.OSVersion.VersionString;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Name = "macOS";
            Version = Environment.OSVersion.VersionString;
        }
    }

    private static void SetWindowsVersionInfo()
    {
        var versionInfo = GetTrueWindowsVersion();
        bool isServer = versionInfo.wProductType != 1;

        Name = isServer ? "Windows Server" : "Windows";

        int major = versionInfo.dwMajorVersion;
        int minor = versionInfo.dwMinorVersion;
        int build = versionInfo.dwBuildNumber;

        if (major == 10 && minor == 0)
        {
            if (isServer)
            {
                if (build >= 26100) Version = "2025";
                else if (build >= 20348) Version = "2022";
                else if (build >= 17763) Version = "2019";
                else if (build >= 14393) Version = "2016";
                else Version = "10 (Server)";
            }
            else
            {
                Version = build >= 22000 ? "11" : "10";
            }
        }
        else if (major == 6)
        {
            switch (minor)
            {
                case 3: Version = isServer ? "2012 R2" : "8.1"; break;
                case 2: Version = isServer ? "2012" : "8"; break;
                case 1: Version = isServer ? "2008 R2" : "7"; break;
                case 0: Version = isServer ? "2008" : "Vista"; break;
                default: Version = $"{major}.{minor} (Unknown {(isServer ? "Server" : "Client")})"; break;
            }
        }
        else if (major == 5)
        {
            // V5 should technically be too old for .netstandard2.1 anyway, but just in case...
            switch (minor)
            {
                case 2: Version = isServer ? "2003" : "XP x64"; break;
                case 1: Version = "XP"; break;
                case 0: Version = "2000"; break;
                default: Version = $"{major}.{minor}"; break;
            }
        }
        else
        {
            Version = $"{major}.{minor}.{build}";
        }
    }

    // RtlGetVersion for Windows
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OSVERSIONINFOEXW
    {
        public int dwOSVersionInfoSize;
        public int dwMajorVersion;
        public int dwMinorVersion;
        public int dwBuildNumber;
        public int dwPlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szCSDVersion;
        public ushort wServicePackMajor;
        public ushort wServicePackMinor;
        public ushort wSuiteMask;
        public byte wProductType; // 1=Workstation, >1=Server
        public byte wReserved;
    }

    [DllImport("ntdll.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int RtlGetVersion(ref OSVERSIONINFOEXW versionInfo);

    private static OSVERSIONINFOEXW GetTrueWindowsVersion()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException();

        var vi = new OSVERSIONINFOEXW { dwOSVersionInfoSize = Marshal.SizeOf<OSVERSIONINFOEXW>() };
        _ = RtlGetVersion(ref vi);
        return vi;
    }
}
