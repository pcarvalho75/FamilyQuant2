using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace FamilyQuant.Tests;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class LookExportCultureCaseAttribute : Attribute { }

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CompositionExportCultureCommands() => new[]
    {
        new TestCommand("--test-composition-export-culture", 10000, TestCommandPhase.Normalized,
            args => args.Contains("--test-composition-export-culture", StringComparer.Ordinal), (_, work) =>
            {
                CompositionExportCultureTests(Path.Combine(work, "composition-export-culture"));
                Console.WriteLine($"{passed} passed, {failed} failed, {skipped} skipped");
                return failed == 0 ? 0 : 1;
            })
    };

    private static FamilyCompositionData LookCultureData(bool pooled) => new(new[]
    {
        new FamilyCompositionValue("a", "Family A", 12.5, pooled ? null : 1.25, pooled ? 0 : 3,
            new[] { new FamilySubclassValue("a1", "Subclass A", 7.5), new FamilySubclassValue("", "Subclass not resolved", 5) }, 2, 1),
        new FamilyCompositionValue("b", "Family B", 87.5, pooled ? null : 1.25, pooled ? 0 : 3,
            new[] { new FamilySubclassValue("b1", "Subclass B", 87.5) }, 5, 3)
    }, pooled ? "Credited signal" : "Mean per-run credited signal", !pooled);

    [LookExportCultureCase]
    private static IEnumerable<AnalyticsFigure> LookCompositionCultureCases() =>
        FamilyCompositionFigures.Build(LookCultureData(false), LookCultureData(true))
            .Append(FamilyCompositionFigures.BuildAbundanceDiversity(LookCultureData(false)));

    private static void CompositionExportCultureTests(string folder)
    {
        Section("Every owned chart export - byte-identical invariant text under en-US and pt-BR");
        EnsureApplication(); Directory.CreateDirectory(folder);
        var factories = typeof(Program).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(m => m.IsDefined(typeof(LookExportCultureCaseAttribute), false)).OrderBy(m => m.Name, StringComparer.Ordinal).ToArray();
        var previous = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var culture = CultureInfo.CurrentCulture; var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string locale in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale); CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                foreach (var factory in factories)
                    foreach (var figure in (IEnumerable<AnalyticsFigure>)factory.Invoke(null, null)!)
                    {
                        byte[] bytes = FamilyCompositionFigures.RenderPng(figure);
                        File.WriteAllBytes(Path.Combine(folder, locale + "-" + figure.FileName), bytes);
                        Check(figure.FileName + " restores " + locale + " display culture after export",
                            CultureInfo.CurrentCulture.Name == locale && CultureInfo.CurrentUICulture.Name == locale);
                        if (previous.TryGetValue(figure.FileName, out byte[]? first))
                            Check(figure.FileName + " export bytes are identical under en-US and pt-BR", bytes.SequenceEqual(first));
                        else previous.Add(figure.FileName, bytes);
                    }
            }
        }
        finally { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = uiCulture; }
        Check("every registered chart factory supplies a real PNG export", factories.Length > 0 && previous.Count >= 3);
    }
}
