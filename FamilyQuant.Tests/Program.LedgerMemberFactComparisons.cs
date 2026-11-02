using System;
using System.Collections.Generic;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void LedgerMemberFactComparisonTests()
    {
        const string declaration = "public partial class View : BaseView";
        const string declared = "namespace Fixture; public partial class View : BaseView { private string Copy = \"retained\"; }";
        Check("a partial relocation can pin its parsed primary type declaration",
            LedgerMemberFactComparison.HasTypeDeclaration(declared, "Fixture.View", declaration));
        Check("primary type declaration parsing ignores harmless comments and whitespace",
            LedgerMemberFactComparison.HasTypeDeclaration(declared.Replace("class View", "class /* primary */ View", StringComparison.Ordinal), "Fixture.View", declaration));
        Check("a changed base type cannot authorize a partial relocation",
            !LedgerMemberFactComparison.HasTypeDeclaration(declared.Replace("BaseView", "OtherBase", StringComparison.Ordinal), "Fixture.View", declaration));
        Check("a string containing the former header cannot authorize a changed type",
            !LedgerMemberFactComparison.HasTypeDeclaration("namespace Fixture; class View : OtherBase { string Copy = \"public partial class View : BaseView\"; }", "Fixture.View", declaration));
        Check("an unrelated type cannot lend its primary type declaration",
            !LedgerMemberFactComparison.HasTypeDeclaration(declared, "Fixture.Missing", declaration));
        const string old = "namespace Fixture;\npublic class View\n{\n    private string Export(int mode)\n    {\n        if (mode == 1) return \".csv\";\n        return \".csv\";\n    }\n    private string Other() => \"other\";\n}";
        var row = new FeatureLedger.Observation("fixture", "FamilyQuant/View.cs", 6, "format/export", "\".csv\"");
        LedgerMemberFactComparison.Result Compare(string current, FeatureLedger.Observation? supplied = null,
            LedgerMemberFactComparison.MemberMapping? mapping = null) => LedgerMemberFactComparison.Compare(supplied ?? row,
                old, new Dictionary<string, string> { ["FamilyQuant/Current.cs"] = current }, mapping);
        var same = Compare(old);
        Check("member literal comparer retains the exact raw format value and auditable members",
            same.Matched && same.Before == row.Detail && same.Actual == row.Detail
            && same.FrozenMember == same.CurrentMember && same.CurrentMember.Contains("Export", StringComparison.Ordinal)
            && same.FrozenMemberSha256 == same.CurrentMemberSha256 && same.FrozenMemberSha256.Length == 64);
        Check("member literal comparer preserves repeated literal multiplicity", same.FrozenOccurrences == 2 && same.CurrentOccurrences == 2);
        Check("member literal comparison tolerates comments and source formatting",
            Compare(old.Replace("if (mode == 1)", "/* retained branch */ if(mode==1)", StringComparison.Ordinal)).Matched);
        Check("a current unrelated member cannot lend the missing member its format literal",
            !Compare(old.Replace("Export(int mode)", "Unrelated(int mode)", StringComparison.Ordinal)).Matched);
        Check("a same-named current member in another type is not an automatic destination",
            !Compare(old.Replace("class View", "class DifferentView", StringComparison.Ordinal)).Matched);
        Check("changed enclosing type declarations require an explicitly reviewed relocation",
            !Compare(old.Replace("class View", "class View : OtherBase", StringComparison.Ordinal)).Matched);
        Check("a changed branch cannot close from unchanged literal text",
            !Compare(old.Replace("mode == 1", "mode == 2", StringComparison.Ordinal)).Matched);
        Check("a changed literal multiplicity remains a nonmatch",
            !Compare(old.Replace("if (mode == 1) return \".csv\";", "if (mode == 1) return \".tsv\";", StringComparison.Ordinal)).Matched);
        Check("a changed exact string spelling remains a nonmatch",
            !Compare(old.Replace("\".csv\"", "@\".csv\"", StringComparison.Ordinal)).Matched);
        Check("a wrong frozen line cannot borrow a matching token elsewhere",
            !Compare(old, row with { Line = 9 }).Matched);
        Check("handler observations cannot be verified by member literal comparison",
            !Compare(old, row with { Kind = "handler" }).Matched);
        Check("unrecorded frozen literal syntax remains a nonmatch",
            !Compare(old, row with { Detail = "\".tsv\"" }).Matched);
        var duplicateFiles = new Dictionary<string, string> { ["one.cs"] = old, ["two.cs"] = old };
        Check("duplicate current member destinations remain ambiguous",
            !LedgerMemberFactComparison.Compare(row, old, duplicateFiles).Matched);
        const string duplicateLine = "namespace Fixture; class View { string Export() { return \".csv\" + \".csv\"; } }";
        Check("two indistinguishable frozen tokens on one line remain ambiguous",
            !LedgerMemberFactComparison.Compare(row with { Line = 1 }, duplicateLine,
                new Dictionary<string, string> { ["one.cs"] = duplicateLine }).Matched);
        var relocation = new LedgerMemberFactComparison.MemberMapping(same.FrozenMember, "FamilyQuant/Current.cs",
            same.CurrentMember.Replace("Fixture.View::", "Fixture.DifferentView::", StringComparison.Ordinal));
        Check("an explicit relocation still requires identical complete member tokens",
            Compare(old.Replace("class View", "class DifferentView", StringComparison.Ordinal), mapping: relocation).Matched);
        Check("an explicit relocation cannot approve changed code surrounding the literal",
            !Compare(old.Replace("class View", "class DifferentView", StringComparison.Ordinal)
                .Replace("mode == 1", "mode == 2", StringComparison.Ordinal), mapping: relocation).Matched);
        Check("a relocation must identify the actual frozen member",
            !Compare(old, mapping: relocation with { FrozenMember = "unrelated" }).Matched);
        Check("conditional compilation cannot silently lend inactive declarations",
            !Compare("#if SOMETHING\n" + old + "\n#endif").Matched);
        Check("unterminated source fails conservatively", !Compare(old + " /*").Matched);

        const string fields = "namespace Fixture { class Options { private const string Format = \".fqproj\"; public string Name => \"Project\"; public string Label { get; } = \"Project\"; } }";
        var fieldRow = row with { Line = 1, Detail = "\".fqproj\"" };
        Check("a retained field literal is scoped to its complete declaration",
            LedgerMemberFactComparison.Compare(fieldRow, fields, new Dictionary<string, string> { ["options.cs"] = fields }).Matched);
        Check("moving a field value to an unrelated field does not preserve its declaration",
            !LedgerMemberFactComparison.Compare(fieldRow, fields, new Dictionary<string, string>
            { ["options.cs"] = fields.Replace("Format =", "Unrelated =", StringComparison.Ordinal) }).Matched);
        const string property = "namespace Fixture { class Options { public string Label { get; } = \"Project\"; } }";
        Check("an auto-property initializer belongs to its property member",
            LedgerMemberFactComparison.Compare(row with { Line = 1, Kind = "code fact", Detail = "\"Project\"" }, property,
                new Dictionary<string, string> { ["options.cs"] = property }).Matched);
        const string interpolated = "namespace Fixture { class Options { public string Name(int n) => $\"file-{n.ToString(\"D2\")}.csv\"; } }";
        Check("an interpolated declaration retains exact syntax without resolving a runtime value",
            LedgerMemberFactComparison.Compare(row with { Line = 1, Detail = "$\"file-{n.ToString(\"D2\")}.csv\"" }, interpolated,
                new Dictionary<string, string> { ["options.cs"] = interpolated }).Matched);
        const string operators = "namespace Fixture { class Options { string Export(int n) { n++; return \".csv\"; } } }";
        Check("operator token boundaries cannot collapse into a different expression",
            !LedgerMemberFactComparison.Compare(row with { Line = 1 }, operators,
                new Dictionary<string, string> { ["options.cs"] = operators.Replace("n++", "n+ +", StringComparison.Ordinal) }).Matched);
        const string generic = "namespace Fixture { class Options { string Export<T>() where T : class { return \".csv\"; } } }";
        Check("method generic constraints cannot masquerade as enclosing type declarations",
            LedgerMemberFactComparison.Compare(row with { Line = 1 }, generic,
                new Dictionary<string, string> { ["options.cs"] = generic }).Matched);
    }
}
