using FamilyQuant.Setup.Engine;

namespace FamilyQuant.Setup;

public static class SetupCommandLine
{
    public static SetupOptions Parse(string[] arguments)
    {
        var options = new SetupOptions(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string argument in arguments)
        {
            int split = argument.IndexOf('=');
            string key = (split < 0 ? argument : argument[..split]).ToLowerInvariant();
            string value = split < 0 ? "" : argument[(split + 1)..];
            if (!seen.Add(key)) throw new ArgumentException("A setup option was supplied more than once: " + key);
            options = key switch
            {
                "/silent" when split < 0 => options with { Silent = true },
                "/uninstall" when split < 0 => options with { Uninstall = true },
                "/desktop" when split < 0 => options with { DesktopShortcut = true },
                "/test-mode" when split < 0 => options with { TestMode = true },
                "/dir" when value.Length > 0 => options with { Directory = value },
                "/test-root" when value.Length > 0 => options with { TestRoot = value },
                "/result" when value.Length > 0 => options with { ResultFile = value },
                "/waitpid" when int.TryParse(value, out int pid) && pid > 0 => options with { WaitPid = pid },
                "/parentpid" when int.TryParse(value, out int parent) && parent > 0 => options with { ParentPid = parent },
                _ => throw new ArgumentException("Unknown or invalid setup option: " + argument),
            };
        }
        if (options.WaitPid != null && !options.Silent) throw new ArgumentException("/waitpid requires /silent.");
        if (options.ParentPid != null && !options.Uninstall) throw new ArgumentException("The parent process option is only for uninstall.");
        if (!options.TestMode && (options.TestRoot != null || options.ResultFile != null)) throw new ArgumentException("Test output options require /test-mode.");
        if (options.TestMode)
        {
            _ = new TestSetupPlatform(options.TestRoot ?? throw new ArgumentException("Test mode requires /test-root."));
            if (options.Directory == null || !SetupPaths.Within(options.TestRoot, SetupPaths.FullDirectory(options.Directory))
                || SetupPaths.Same(options.TestRoot, options.Directory)) throw new ArgumentException("Test mode requires /dir inside its temporary root.");
            if (options.ResultFile != null)
            {
                if (!Path.IsPathFullyQualified(options.ResultFile) || !SetupPaths.Within(options.TestRoot, options.ResultFile)
                    || SetupPaths.Same(options.TestRoot, options.ResultFile)) throw new ArgumentException("Test result output must stay under the temporary root.");
                SetupPaths.CheckAncestors(Path.GetDirectoryName(options.ResultFile)!);
                if (File.Exists(options.ResultFile) && (File.GetAttributes(options.ResultFile) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Test output must not be a file link.");
            }
        }
        return options;
    }

    internal static IEnumerable<string> Arguments(SetupOptions options)
    {
        if (options.Silent) yield return "/silent";
        if (options.Uninstall) yield return "/uninstall";
        if (options.DesktopShortcut) yield return "/desktop";
        if (options.TestMode) yield return "/test-mode";
        if (options.Directory != null) yield return "/dir=" + options.Directory;
        if (options.TestRoot != null) yield return "/test-root=" + options.TestRoot;
        if (options.ResultFile != null) yield return "/result=" + options.ResultFile;
        if (options.WaitPid != null) yield return "/waitpid=" + options.WaitPid;
        if (options.ParentPid != null) yield return "/parentpid=" + options.ParentPid;
    }
}
