using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        private static void PrefixDefinitionTests()
        {
            CoreBoundaryTests();
            Check("the prefix guard rejects a planted decoy literal",
                PrefixLiterals("string tag = \"Reverse_\";").Count == 1);
            Check("the prefix guard rejects a planted contaminant prefix or marker",
                PrefixLiterals("string tag = \"contaminant_\"; string marker = \"contaminant\";").Count == 2);
            Check("the prefix guard ignores documentation and ordinary contaminant prose",
                PrefixLiterals("// \"Reverse_\"\n/* \"contaminant_\" */ string text = \"a contaminant row\";").Count == 0);

            DirectoryInfo? root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "FamilyQuant2.sln")))
                root = root.Parent;
            Check("the prefix contract can find the checkout", root != null);
            if (root == null) return;

            var violations = new List<string>();
            foreach (string source in Directory.EnumerateFiles(root.FullName, "*", SearchOption.AllDirectories)
                         .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                                     p.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(root.FullName, source).Replace('\\', '/');
                string[] parts = relative.Split('/');
                if (!parts[0].StartsWith("FamilyQuant", StringComparison.Ordinal) ||
                    parts[0] == "FamilyQuant.Tests" || parts.Any(p => p is "bin" or "obj") ||
                    Path.GetFileName(source) == "SequenceLabels.cs") continue;

                string text = File.ReadAllText(source);
                if (source.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (int position in PrefixLiterals(text))
                        violations.Add(relative + ":" + (1 + text.Take(position).Count(c => c == '\n')));
                }
                else
                {
                    foreach (XAttribute attribute in XDocument.Parse(text, LoadOptions.SetLineInfo)
                                 .Descendants().Attributes().Where(a =>
                                     a.Name.LocalName is "ToolTip" or "Text" or "Content" && ContainsPrefix(a.Value)))
                    {
                        int line = ((System.Xml.IXmlLineInfo)attribute).LineNumber;
                        violations.Add(relative + ":" + line);
                    }
                }
            }
            Check("prefix literals occur only in SequenceLabels",
                violations.Count == 0, string.Join(", ", violations));
        }

        private static void CoreBoundaryTests()
        {
            var engine = typeof(FamilyClusterFile).Assembly;
            Check("the headless engines are owned by FamilyQuant.Core",
                new[] { typeof(FamilyClusterFile), typeof(USearchTools), typeof(WordCluster),
                        typeof(ProjectStore), typeof(Homology.HomologySearch), typeof(DeNovo.NativeQuantFractions) }
                    .All(t => t.Assembly.GetName().Name == "FamilyQuant.Core"));
            Check("Core has no WPF assembly references",
                !engine.GetReferencedAssemblies().Any(a =>
                    a.Name is "PresentationFramework" or "PresentationCore" or "WindowsBase" or "System.Xaml" ||
                    (a.Name?.StartsWith("Xceed.Wpf", StringComparison.Ordinal) ?? false)));
            Check("Core is a library without application or window types",
                engine.EntryPoint == null && engine.GetType("FamilyQuant.MainWindow") == null &&
                engine.GetType("FamilyQuant.AppVersionInfo") == null);
            Check("the embedded USEARCH payload keeps its resource namespace beside the engine",
                typeof(USearchTools).Assembly.GetManifestResourceNames().Contains("FamilyQuant.MyResources.resources"));
        }

        private static bool ContainsPrefix(string literal) =>
            literal.Contains("Reverse_", StringComparison.Ordinal) ||
            literal.Contains("contaminant_", StringComparison.Ordinal) || literal == "contaminant";

        private static void CoreMessageBoxTests()
        {
            bool ContainsMessageBox(string source)
            {
                string code = Regex.Replace(source,
                    @"(?s)/\*.*?\*/|//[^\r\n]*|@""(?:""""|[^""])*""|""(?:\\.|[^""\\])*""",
                    token => new string(' ', token.Length));
                return Regex.IsMatch(code, @"\bMessageBox\b");
            }
            Check("the Core message-box guard rejects a planted call", ContainsMessageBox("MessageBox.Show(\"blocked\");"));
            Check("the Core message-box guard ignores comments and user text",
                !ContainsMessageBox("// MessageBox.Show()\nstring text = \"MessageBox\";"));
            DirectoryInfo? root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "FamilyQuant2.sln"))) root = root.Parent;
            var violations = root == null ? new[] { "checkout not found" } :
                Directory.EnumerateFiles(Path.Combine(root.FullName, "FamilyQuant.Core"), "*.cs", SearchOption.AllDirectories)
                    .Where(path => !Path.GetRelativePath(root.FullName, path).Split(Path.DirectorySeparatorChar)
                        .Any(part => part is "bin" or "obj"))
                    .Where(path => ContainsMessageBox(File.ReadAllText(path))).ToArray();
            Check("Core raises no message boxes", violations.Length == 0, string.Join(", ", violations));
        }

        private static List<int> PrefixLiterals(string source)
        {
            var positions = new List<int>();
            foreach (Match token in Regex.Matches(source,
                         @"(?s)/\*.*?\*/|//[^\r\n]*|@""(?:""""|[^""])*""|""(?:\\.|[^""\\])*"""))
            {
                if (token.Value.StartsWith("//", StringComparison.Ordinal) ||
                    token.Value.StartsWith("/*", StringComparison.Ordinal)) continue;
                string literal = token.Value.StartsWith("@", StringComparison.Ordinal)
                    ? token.Value.Substring(2, token.Length - 3).Replace("\"\"", "\"")
                    : token.Value.Substring(1, token.Length - 2);
                if (ContainsPrefix(literal)) positions.Add(token.Index);
            }
            return positions;
        }
    }
}
