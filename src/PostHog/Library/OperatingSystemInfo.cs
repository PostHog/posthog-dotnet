using System.Runtime.InteropServices;

namespace PostHog.Library;

/// <summary>
/// Provides human friendly version information about the current operating system.
/// </summary>
/// <remarks>
/// Determines the operating system platform and version at runtime, maps out,
/// and assigns the corresponding values to the <c>Name</c> and <c>Version</c> properties.
/// Currently, we detect between different Windows / Windows Server versions (after Windows 2000), and simple Linux or macOS.
/// For unsupported platforms, the <c>Name</c> is set to "Unknown" and the <c>Version</c> is set to the value of <see cref="Environment.OSVersion.VersionString"/>.
/// </remarks>
public static class OperatingSystemInfo
{
    private static readonly Lazy<OSVERSIONINFOEXW> _winInfo = new(GetTrueWindowsVersion, isThreadSafe: true);
    private static readonly string _name = GetOSName();
    private static readonly string _version = GetOSVersion();

    public static string Name => _name;
    public static string Version => _version;

    private static string GetOSName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return GetWindowsName(_winInfo.Value);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "Linux";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "macOS";
        }

        return Environment.OSVersion.Platform.ToString();
    }

    private static string GetOSVersion()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return GetWindowsVersion(_winInfo.Value);
        }

        return Environment.OSVersion.VersionString;
    }

    private static string GetWindowsName(in OSVERSIONINFOEXW versionInfo)
    {
        bool isServer = versionInfo.wProductType != 1;

        return isServer ? "Windows Server" : "Windows";
    }

    private static string GetWindowsVersion(in OSVERSIONINFOEXW versionInfo)
    {
        bool isServer = versionInfo.wProductType != 1;
        int major = versionInfo.dwMajorVersion;
        int minor = versionInfo.dwMinorVersion;
        int build = versionInfo.dwBuildNumber;

        if (major == 10 && minor == 0)
        {
            if (isServer)
            {
                return build switch
                {
                    >= 26100 => "2025",
                    >= 20348 => "2022",
                    >= 17763 => "2019",
                    >= 14393 => "2016",
                    _ => "10 (Server)"
                };
            }

            return build >= 22000 ? "11" : "10";
        }

        if (major == 6)
        {
            return minor switch
            {
                3 => isServer ? "2012 R2" : "8.1",
                2 => isServer ? "2012" : "8",
                1 => isServer ? "2008 R2" : "7",
                0 => isServer ? "2008" : "Vista",
                _ => $"{major}.{minor} (Unknown {(isServer ? "Server" : "Client")})"
            };
        }

        if (major == 5)
        {
            // v5 is very old, but keep for completeness
            return minor switch
            {
                2 => isServer ? "2003" : "XP x64",
                1 => "XP",
                0 => "2000",
                _ => $"{major}.{minor}"
            };
        }

        return $"{major}.{minor}.{build}";
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
