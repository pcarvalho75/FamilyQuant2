using System;
using System.IO;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void SharedTestHelperContracts(string workDir)
    {
        string path = Path.Combine(workDir, "housekeeping-hash.txt");
        File.WriteAllBytes(path, new byte[] { 97, 98, 99 });
        Check("shared test file hash retains upper-case SHA-256 bytes", TestFileHash.Upper(path) ==
            "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD");
        string[] args = { "fixture", "", " " };
        Check("empty-fallback and empty-passthrough argument rules remain distinct",
            TestArguments.EmptyFallsBack(args, 0, 1, "fallback") == "fallback"
            && TestArguments.EmptyPassesThrough(args, 0, 1, "fallback") == "");
        Check("both test argument rules keep whitespace and missing-slot fallback",
            TestArguments.EmptyFallsBack(args, 0, 2, "fallback") == " "
            && TestArguments.EmptyPassesThrough(args, 0, 2, "fallback") == " "
            && TestArguments.EmptyFallsBack(args, 0, 3, "fallback") == "fallback"
            && TestArguments.EmptyPassesThrough(args, 0, 3, "fallback") == "fallback");
        Check("checker CSV retains its no-formula-prefix minimal quoting rule", TestCsvFields.Field("=1") == "=1"
            && TestCsvFields.Field("") == "" && TestCsvFields.Field("a,b") == "\"a,b\""
            && TestCsvFields.Field("a\"b") == "\"a\"\"b\"" && TestCsvFields.Field("a\tb") == "a\tb");
        Check("frozen answer-key accession logic keeps its own prefix/case rule",
            FrozenAnswerKey.PythonAccession("contaminant_P1|Name") == "P1"
            && FrozenAnswerKey.PythonAccession("sp|P1|Name") == "P1"
            && FrozenAnswerKey.PythonAccession("P1") == "P1"
            && FrozenAnswerKey.PythonAccession("CONTAMINANT_P1|Name") == "Name"
            && FrozenAnswerKey.PythonAccession("Reverse_P1") == "Reverse_P1");
    }
}
