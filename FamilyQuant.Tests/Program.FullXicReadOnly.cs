using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FullXicReadOnlyCommands() =>
    [
        new("--check-readonly-fullxic", 371, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--check-readonly-fullxic", (args, _) => ReadOnlyFullXicCheck(args)),
    ];

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string linkPath, string existingPath, IntPtr securityAttributes);

    private static int ReadOnlyFullXicCheck(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("Supply one RAW file, SEPro package, complete legacy cache folder and fresh output folder.");
        string sourceRaw = Path.GetFullPath(args[1]), sourcePackage = Path.GetFullPath(args[2]), sourceCache = Path.GetFullPath(args[3]);
        string output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must be fresh: " + output);
        string raw = Path.Combine(output, "inputs", "raw");
        string legacy = Path.Combine(raw, FullXicComputer.CacheFolderName);
        Directory.CreateDirectory(legacy);
        string linkedRaw = Path.Combine(raw, Path.GetFileName(sourceRaw));
        if (!CreateHardLink(linkedRaw, sourceRaw, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the owned RAW hard link.");
        string package = Path.Combine(output, "inputs", "identifications.sepr2");
        File.Copy(sourcePackage, package);
        string run = DeNovoReconciler.NormalizeRunName(sourceRaw);
        foreach (string name in new[] { "fullxic_identified_" + run + ".json", "ms1envelope_" + run + ".json" })
            File.Copy(Path.Combine(sourceCache, name), Path.Combine(legacy, name));
        var options = new FullXicOptions { AppVersion = "readonly-input-check" };
        string fingerprint = FileFingerprint.Sha256(linkedRaw);
        var psms = SeproPsmAdapter.Load(package).Where(p => string.Equals(DeNovoReconciler.NormalizeRunName(p.FileName), run, StringComparison.OrdinalIgnoreCase)).ToList();
        var requests = IdentifiedQuantExtractor.BuildRequests(psms);
        var cache = DeNovoXicCache.Load(Path.Combine(legacy, "fullxic_identified_" + run + ".json"), run,
            options.Ppm, options.Ms1Tolerance, options.IsotopeCount, options.MinPointsForQuant, fingerprint);
        if (requests.Count == 0 || requests.Any(r => !cache.TryGet(r.ScanNoMS2, Math.Round(r.PrecursorMz, DeNovoXicExtractor.MzDecimals), r.Charge, out _)))
            throw new InvalidDataException("The bounded read-only check requires a complete provenance-matched extraction cache; no fresh extraction was started.");
        object?[] detectorArgs = { Path.Combine(legacy, "ms1envelope_" + run + ".json"), run, fingerprint, null };
        if (!(bool)typeof(Ms1EnvelopeDetector).GetMethod("TryLoadCache", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, detectorArgs)!)
            throw new InvalidDataException("A provenance-matched detector cache is required; no fresh detection was started.");
        Console.WriteLine("Preflight: every identified request and detector result is cached for the exact RAW content.");
        var before = FullXicComputer.ComputeFullXic(raw, package, null, options, progress: Console.WriteLine, restrictToRuns: new[] { run });
        if (before.SpectrumComparisonCsvError.Length > 0) throw new IOException(before.SpectrumComparisonCsvError);
        Console.WriteLine("Writable baseline complete: " + before.Rows.Count + " rows; applying owned read-only directory ACLs.");
        var originals = new List<(DirectoryInfo Directory, DirectorySecurity Access)>();
        var sourceState = (new FileInfo(sourceRaw).Length, File.GetLastWriteTimeUtc(sourceRaw));
        string[] snapshot = ReadOnlyFolderSnapshot(raw);
        FullXicComputeResult after;
        try
        {
            foreach (string file in Directory.GetFiles(legacy)) File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
            var sid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("No Windows user SID.");
            foreach (string directory in new[] { raw, legacy })
            {
                var info = new DirectoryInfo(directory);
                DirectorySecurity access = info.GetAccessControl(AccessControlSections.Access);
                originals.Add((info, access));
                var denied = new DirectorySecurity();
                denied.SetSecurityDescriptorSddlForm(access.GetSecurityDescriptorSddlForm(AccessControlSections.Access), AccessControlSections.Access);
                denied.AddAccessRule(new FileSystemAccessRule(sid,
                    FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories | FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes,
                    InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny));
                info.SetAccessControl(denied);
                Check("owned acquisition directory actually refuses new files: " + info.Name,
                    Throws<UnauthorizedAccessException>(() => File.WriteAllText(Path.Combine(directory, "forbidden.txt"), "not allowed")));
            }
            Console.WriteLine("Read-only write guards active; starting the second Compute.");
            after = FullXicComputer.ComputeFullXic(raw, package, null, options, progress: Console.WriteLine, restrictToRuns: new[] { run });
            Check("read-only Compute preserves every stored quant row bit for bit", JsonSerializer.Serialize(before.Rows) == JsonSerializer.Serialize(after.Rows));
            Check("read-only Compute preserves per-run TIC and derived fractions", JsonSerializer.Serialize(before.Manifest.PerRunTic) == JsonSerializer.Serialize(after.Manifest.PerRunTic)
                && JsonSerializer.Serialize(before.Manifest.DerivedFractions) == JsonSerializer.Serialize(after.Manifest.DerivedFractions));
            Check("read-only Compute preserves the complete spectrum evidence table", JsonSerializer.Serialize(before.SpectrumComparisonRows) == JsonSerializer.Serialize(after.SpectrumComparisonRows));
            Check("read-only Compute writes its CSV outside RAW", after.SpectrumComparisonCsvError.Length == 0
                && File.Exists(after.SpectrumComparisonCsvPath) && !FullXicStoragePaths.IsInRawDirectory(after.SpectrumComparisonCsvPath, raw));
            Check("read-only RAW folder contents and numbers remain unchanged", snapshot.SequenceEqual(ReadOnlyFolderSnapshot(raw)));
            Check("original RAW content and modification time remain unchanged", fingerprint == FileFingerprint.Sha256(sourceRaw)
                && sourceState == (new FileInfo(sourceRaw).Length, File.GetLastWriteTimeUtc(sourceRaw)));
            File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new
            {
                Raw = sourceRaw, OwnedReadOnlyInput = raw, Cache = sourceCache,
                after.SpectrumComparisonCsvPath, Rows = after.Rows.Count,
                RawSha256 = fingerprint, CacheBacked = true, FreshExtraction = false,
                QuantRowsEqual = JsonSerializer.Serialize(before.Rows) == JsonSerializer.Serialize(after.Rows),
                SnapshotUnchanged = snapshot.SequenceEqual(ReadOnlyFolderSnapshot(raw)), Passed = passed, Failed = failed,
            }, new JsonSerializerOptions { WriteIndented = true }));
            File.Copy(after.SpectrumComparisonCsvPath, Path.Combine(output, "spectrum.csv"));
        }
        finally
        {
            // Only owned directories/cache copies are restored. The RAW hard link's file ACL and
            // attributes are never changed, because those belong to the original acquisition too.
            foreach (var owned in originals.AsEnumerable().Reverse())
            {
                var restored = new DirectorySecurity();
                restored.SetSecurityDescriptorSddlForm(owned.Access.GetSecurityDescriptorSddlForm(AccessControlSections.Access), AccessControlSections.Access);
                owned.Directory.SetAccessControl(restored);
            }
            foreach (string file in Directory.GetFiles(legacy)) File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        }
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static string[] ReadOnlyFolderSnapshot(string root) => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .OrderBy(p => p, StringComparer.Ordinal).Select(p => Directory.Exists(p) ? "D\t" + Path.GetRelativePath(root, p)
            : "F\t" + Path.GetRelativePath(root, p) + "\t" + new FileInfo(p).Length + "\t" + File.GetLastWriteTimeUtc(p).Ticks
                + "\t" + FileFingerprint.Sha256(p)).ToArray();
}
