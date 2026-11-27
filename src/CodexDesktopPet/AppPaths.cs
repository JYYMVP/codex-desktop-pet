using System.IO;

namespace CodexDesktopPet;

internal static class AppPaths
{
    public static string LocalApplicationData
    {
        get
        {
            var reported = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (IsCodexSandboxPath(reported))
            {
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrWhiteSpace(profile))
                    return Path.GetFullPath(Path.Combine(profile, "AppData", "Local"));
            }

            return Path.GetFullPath(reported);
        }
    }

    public static string DataRoot
    {
        get
        {
            var testRoot = Environment.GetEnvironmentVariable("CODEX_DESKTOP_PET_DATA_ROOT");
            var testMode = Environment.GetEnvironmentVariable(
                "CODEX_DESKTOP_PET_ENABLE_TEST_ROOT");
            if (testMode == "1" && !string.IsNullOrWhiteSpace(testRoot))
                return Path.GetFullPath(testRoot);
            return Path.Combine(LocalApplicationData, "CodexDesktopPet");
        }
    }

    internal static bool IsCodexSandboxPath(string path) =>
        path.Contains(@"\AC\#!sandbox.{", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/AC/#!sandbox.{", StringComparison.OrdinalIgnoreCase);
}
