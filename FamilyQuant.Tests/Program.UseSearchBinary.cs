using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private const string UpstreamUseSearchSha256 = "CFB6A77FF0CABCF9D7334EE298A1DF22B2351A54C65418F0EB93F3913A10ACD3";

    private static string AssayRepositoryFile(string relative)
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (DirectoryInfo? folder = new(start); folder != null; folder = folder.Parent)
            {
                string path = Path.Combine(folder.FullName, relative);
                if (File.Exists(path)) return path;
            }
        return Path.Combine(Environment.CurrentDirectory, relative);
    }

    private static byte[] UseSearchPayload(Assembly assembly)
    {
        using Stream stream = assembly.GetManifestResourceStream("FamilyQuant.MyResources.resources")
            ?? throw new InvalidDataException("The Core resource is absent.");
        using var resources = new ResourceReader(stream);
        foreach (DictionaryEntry entry in resources)
            if ((string)entry.Key == "usearch11_0_667_win32") return (byte[])entry.Value!;
        throw new InvalidDataException("The embedded USEARCH is absent.");
    }

    private static void UseSearchBinaryTests(string? publishedCore = null)
    {
        string binary = AssayRepositoryFile("FamilyQuant/DLLs/usearch11.0.667_win32.exe");
        Check("X15: vendored USEARCH matches the pinned upstream artifact", File.Exists(binary) &&
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(binary))) == UpstreamUseSearchSha256);
        Check("X15: embedded USEARCH matches the pinned upstream artifact",
            Convert.ToHexString(SHA256.HashData(UseSearchPayload(typeof(USearchTools).Assembly))) == UpstreamUseSearchSha256);
        string license = AssayRepositoryFile("FamilyQuant/DLLs/USEARCH-CC0.txt");
        Check("X15: upstream CC0 notice is retained beside the binary", File.Exists(license) &&
            File.ReadAllText(license).Contains("CC0 1.0 Universal", StringComparison.Ordinal));
        Check("X15: binary checker has its own command registration",
            TestCommandRegistry.Commands.Any(command => command.Flag == "--test-usearch-binary"));
        UseSearchExtractionTests();
        if (publishedCore == null) return;

        var context = new AssemblyLoadContext("usearch-publish-check", isCollectible: true);
        try
        {
            Assembly assembly = context.LoadFromAssemblyPath(Path.GetFullPath(publishedCore));
            Check("X15: published Core embeds the pinned upstream artifact",
                Convert.ToHexString(SHA256.HashData(UseSearchPayload(assembly))) == UpstreamUseSearchSha256);
        }
        finally { context.Unload(); }
    }

    private static void UseSearchExtractionTests()
    {
        string folder = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "usearch-extraction-" + Guid.NewGuid().ToString("N"));
        var extract = typeof(USearchTools).GetMethod("ExtractBundledExecutable", BindingFlags.NonPublic | BindingFlags.Static)!;
        byte[] firstPayload = { 1, 2, 3 }, secondPayload = { 4, 5, 6 };
        string first = (string)extract.Invoke(null, new object[] { firstPayload, folder })!;
        using var held = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.Read);
        string? second = null;
        string? failure = null;
        try { second = (string)extract.Invoke(null, new object[] { secondPayload, folder })!; }
        catch (TargetInvocationException ex) { failure = ex.InnerException?.Message ?? ex.Message; }
        Check("X15: a locked older payload permits a different payload to extract", second != null && first != second, failure);
        Check("X15: extracting another payload preserves the locked older bytes", File.ReadAllBytes(first).SequenceEqual(firstPayload));
        Check("X15: an identical locked payload is reused without rewriting", (string)extract.Invoke(null,
            new object[] { firstPayload, folder })! == first);

        string concurrentFolder = Path.Combine(folder, "concurrent");
        string? left = null, right = null;
        string? concurrentFailure = null;
        try
        {
            Parallel.Invoke(() => left = (string)extract.Invoke(null, new object[] { secondPayload, concurrentFolder })!,
                () => right = (string)extract.Invoke(null, new object[] { secondPayload, concurrentFolder })!);
        }
        catch (AggregateException ex) { concurrentFailure = ex.Flatten().InnerExceptions[0].GetBaseException().ToString(); }
        Check("X15: concurrent identical extractions converge on complete bytes", left != null && left == right &&
            File.ReadAllBytes(left).SequenceEqual(secondPayload), concurrentFailure);
        Check("X15: successful extraction leaves no staged files", !Directory.EnumerateFiles(folder, "*.tmp", SearchOption.AllDirectories).Any());

        bool stressPassed = true;
        string? stressFailure = null;
        for (int iteration = 0; iteration < 40 && stressPassed; iteration++)
        {
            string iterationFolder = Path.Combine(folder, "stress-" + iteration.ToString(System.Globalization.CultureInfo.InvariantCulture));
            string? a = null, b = null;
            try
            {
                Parallel.Invoke(() => a = (string)extract.Invoke(null, new object[] { secondPayload, iterationFolder })!,
                    () => b = (string)extract.Invoke(null, new object[] { secondPayload, iterationFolder })!);
                stressPassed = a != null && a == b && File.ReadAllBytes(a).SequenceEqual(secondPayload);
            }
            catch (AggregateException ex) { stressPassed = false; stressFailure = ex.Flatten().InnerExceptions[0].GetBaseException().ToString(); }
        }
        Check("X15: forty simultaneous-publication rounds retain complete bytes", stressPassed, stressFailure);
    }

    private static int RunUseSearchBinaryTests(string[] args, string workDir)
    {
        if (args.Length is < 1 or > 2) throw new ArgumentException("Supply an optional published Core assembly.");
        UseSearchBinaryTests(args.Length == 2 ? args[1] : null);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
