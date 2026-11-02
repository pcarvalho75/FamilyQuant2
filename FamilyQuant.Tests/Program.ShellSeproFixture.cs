using PatternTools;
using SeproPckg2;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int ShellSeproFixtureCheck(string[] args, string workDir)
    {
        string output = Path.GetFullPath(args[1]);
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Fixture path must be fresh.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var fixture = CreateShellSeproFixture([("shell_route_fixture", 1)]);
        fixture.Save(output);
        var reopened = ResultPackage.Load(output);
        Check("owned synthetic SEPro fixture round-trips through the vendor serializer",
            ShellSeproProteinGroups(reopened).SequenceEqual(new[] { ("shell_route_fixture", 1) }));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    // No collaborator input, spectra or search result is used. This is a serialization fixture.
    private static ResultPackage CreateShellSeproFixture(IReadOnlyList<(string Locus, int Group)> values)
    {
        var parameters = new Parameters();
        var manager = new ProteinManager(new List<PSMSearchResult>(), parameters, new List<string>())
        {
            MyProteinList = values.Select(value => new MyProtein(value.Locus, new List<PSMSearchResult>())
            {
                GroupNo = value.Group, Description = "Owned synthetic shell fixture", Sequence = "PEPTIDEK"
            }).ToList()
        };
        return new ResultPackage { MyProteins = manager, MyParameters = parameters };
    }

    private static (string Locus, int Group)[] ShellSeproProteinGroups(ResultPackage package) =>
        package.MyProteins.MyProteinList.Select(protein => (protein.Locus, protein.GroupNo)).ToArray();
}
