using System;

namespace FamilyQuant
{
    /// <summary>
    /// The version the title bar, the project manifest and the Compute window report.
    ///
    /// An installed copy is identified by its ClickOnce DEPLOYMENT version (ApplicationVersion in the
    /// publish profile, which Visual Studio's Publish increments on every publish), not by the assembly
    /// version in the csproj, which a routine publish leaves alone: the title read 1.5.0.0 on an installed
    /// 1.5.0.2 (2026-10-25). The .NET ClickOnce launcher (Launcher.exe, the manifest's entry point) hands
    /// the running deployment's version to the application in the ClickOnce_CurrentVersion environment
    /// variable; outside ClickOnce (a build, the test runner) it is absent and the assembly version is used.
    /// </summary>
    public static class AppVersionInfo
    {
        public const string ClickOnceCurrentVersionVariable = "ClickOnce_CurrentVersion";

        public static string Current()
        {
            return Resolve(Environment.GetEnvironmentVariable(ClickOnceCurrentVersionVariable),
                           typeof(AppVersionInfo).Assembly.GetName().Version);
        }

        /// <summary>The deployment version when it parses as a version, otherwise the assembly version,
        /// all four parts either way.</summary>
        public static string Resolve(string? deployedVersion, Version? assemblyVersion)
        {
            if (!string.IsNullOrWhiteSpace(deployedVersion) &&
                Version.TryParse(deployedVersion.Trim(), out Version? deployed))
            {
                return deployed.ToString();
            }

            return assemblyVersion?.ToString() ?? "unknown";
        }
    }
}
