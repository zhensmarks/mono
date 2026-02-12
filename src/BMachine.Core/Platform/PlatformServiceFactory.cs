using System.Runtime.InteropServices;

namespace BMachine.Core.Platform;

public static class PlatformServiceFactory
{
    private static IPlatformService? _instance;

    public static IPlatformService Get()
    {
        if (_instance != null) return _instance;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _instance = new WindowsPlatformService();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            _instance = new MacPlatformService();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _instance = new LinuxPlatformService();
        }
        else
        {
            // Fallback or throw? 
            // For now, default to Linux as it's the most generic *nix
            _instance = new LinuxPlatformService();
        }

        return _instance;
    }
}
