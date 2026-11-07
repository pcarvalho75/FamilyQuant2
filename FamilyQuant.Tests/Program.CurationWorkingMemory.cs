using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void CurationWorkingMemoryTests(string folder)
    {
        Section("Z12p5f8 panel-only Undo and borrowed read-only evidence views");
        var source = new EvidenceProvenance
        {
            Provider = "Synthetic source", Release = "Synthetic release", RetrievedAtUtc = DateTimeOffset.UnixEpoch,
            UnresolvedAccessions = new() { "unresolved" }, UnresolvedReasons = new() { ["unresolved"] = "not available" },
            ReferencePanel = new() { Entries = Enumerable.Range(0, 512).Select(i => new PanelEntry
                { Accession = "REFERENCE_" + i, Sequence = new string('A', 8192), Evidence = new() { Family = "Synthetic family" } }).ToList() },
            FamilyWorld = new(), NearestReviewed = new() { References = 512 },
        };
        var file = ManualChoiceFile(); file.EvidenceSource = source;
        var oldPanel = new FamilyPanel { Name = "Previous panel", Families = new() { new() { Key = "old", DisplayName = "Previous family" } } };
        file.FamilyPanel = oldPanel;
        var model = new CurationViewModel(file);
        string before = file.ToJsonText();
        long start = GC.GetAllocatedBytesForCurrentThread();
        model.ApplyFamilyPanel(new FamilyPanel { Name = "Next panel" });
        long panelBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        oldPanel.Name = "Changed outside the snapshot";
        model.Undo();
        Check("Z12p5f8 panel-only Undo restores detached definitions and exact prior content", file.ToJsonText() == before
            && !ReferenceEquals(file.FamilyPanel, oldPanel) && ReferenceEquals(file.EvidenceSource, source));
        Check("Z12p5f8 panel import allocation is independent of the large reference table", panelBytes < 1024 * 1024,
            panelBytes + " allocated bytes for panel-only capture");
        model.ApplyFamilyPanel(new FamilyPanel { Name = "Another panel" });
        source.Release = "A later unrelated edit";
        file.MyExtendedFastaItems[0].UserAnnotation = "A later annotation";
        model.Undo();
        Check("Z12p5f8 panel Undo preserves unrelated evidence and annotation edits", source.Release == "A later unrelated edit"
            && file.EvidenceSource == source && file.MyExtendedFastaItems[0].UserAnnotation == "A later annotation");
        var snapshot = GroupingSnapshot.CaptureFamilyPanel(file);
        Check("Z12p5f8 a panel snapshot refuses another document", Throws<InvalidOperationException>(() => snapshot.Restore(ManualChoiceFile())));
        var originalProtein = file.MyExtendedFastaItems[0]; file.MyExtendedFastaItems[0] = new();
        Check("Z12p5f8 a panel snapshot refuses replaced protein rows", Throws<InvalidOperationException>(() => snapshot.Restore(file)));
        file.MyExtendedFastaItems[0] = originalProtein;
        snapshot.Restore(file); file.FamilyPanel!.Name = "Changed after restore"; snapshot.Restore(file);
        Check("Z12p5f8 repeated panel restore never aliases its retained panel", file.FamilyPanel.Name == "Previous panel");

        var cloneMethod = typeof(EvidenceApply).GetMethod("Clone", BindingFlags.Static | BindingFlags.NonPublic)!;
        T Copy<T>(T value) => (T)cloneMethod.MakeGenericMethod(typeof(T)).Invoke(null, new object?[] { value })!;
        var sessionMethod = typeof(EvidenceApply).GetMethod("SessionSource", BindingFlags.Static | BindingFlags.NonPublic)!;
        var curationMethod = typeof(EvidenceApply).GetMethod("CurationSource", BindingFlags.Static | BindingFlags.NonPublic)!;
        var search = new NeighbourSearch { References = 7, Neighbours = new() { new() { Query = "Synthetic query" } } };
        var expected = Copy(source); expected.NearestReviewed = Copy(search);
        string sourceBefore = JsonSerializer.Serialize(source);
        start = GC.GetAllocatedBytesForCurrentThread();
        var baseline = Copy(source); baseline.NearestReviewed = Copy(search);
        long baselineBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        var session = (EvidenceProvenance)sessionMethod.Invoke(null, new object?[] { file, search })!;
        long viewBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Check("Z12p5f8 session staging preserves every serialized provenance field", JsonSerializer.Serialize(session) == JsonSerializer.Serialize(expected));
        Check("Z12p5f8 session staging borrows owned tables and detaches metadata and the new search",
            session.ReferencePanel == source.ReferencePanel && session.FamilyWorld == source.FamilyWorld
            && session.UnresolvedAccessions != source.UnresolvedAccessions && session.UnresolvedReasons != source.UnresolvedReasons
            && session.NearestReviewed != search && session.NearestReviewed!.Neighbours[0] != search.Neighbours[0]);
        Check("Z12p5f8 session staging cuts allocated bytes by at least one third", viewBytes < baselineBytes * 2 / 3,
            "old=" + baselineBytes + "; view=" + viewBytes + " allocated bytes");
        Console.WriteLine(FormattableString.Invariant($"MEMORY Z12p5f8 allocatedBytes panelUndo={panelBytes} legacySessionClone={baselineBytes} sessionView={viewBytes}"));
        expected = Copy(source); expected.FamilyWorld = null;
        var curation = (EvidenceProvenance)curationMethod.Invoke(null, new object?[] { source })!;
        Check("Z12p5f8 hash projection exactly preserves the previous FamilyWorld omission", JsonSerializer.Serialize(curation) == JsonSerializer.Serialize(expected)
            && curation.ReferencePanel == source.ReferencePanel && curation.NearestReviewed == source.NearestReviewed);
        Check("Z12p5f8 staging and hash views never mutate their source", JsonSerializer.Serialize(source) == sourceBefore);
        Check("Z12p5f8 absent session search returns the owned source without copying", ReferenceEquals(source,
            sessionMethod.Invoke(null, new object?[] { file, null })));
        Check("Z12p5f8 absent source accepts a detached new search", ((EvidenceProvenance)sessionMethod.Invoke(null,
            new object?[] { new FamilyClusterFile(), search })!).NearestReviewed != search);

        var references = source.ReferencePanel!.Entries.Select(e => new NeighbourReference
            { Accession = e.Accession, Sequence = e.Sequence, Family = e.Evidence.Family, Evidence = e.Evidence }).ToList();
        var recipe = new EvidenceRecipe { SessionReferences = references, SessionSearch = search, ProgramVersion = "Synthetic version" };
        start = GC.GetAllocatedBytesForCurrentThread();
        var legacyRecipe = JsonSerializer.Deserialize<EvidenceRecipe>(JsonSerializer.Serialize(recipe))!;
        long legacyRecipeBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        var copiedRecipe = Copy(recipe);
        long recipeBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Check("Z12p5f8 recipe copy preserves the entire default JSON contract", JsonSerializer.Serialize(copiedRecipe) == JsonSerializer.Serialize(legacyRecipe));
        Check("Z12p5f8 recipe copies detach mutable records and reuse only immutable reference sequences",
            copiedRecipe.SessionReferences != references && copiedRecipe.SessionSearch != search
            && copiedRecipe.SessionReferences!.Zip(references).All(pair => pair.First != pair.Second
                && ReferenceEquals(pair.First.Sequence, pair.Second.Sequence) && pair.First.Evidence == null));
        Check("Z12p5f8 recipe copy allocation falls by at least one third", recipeBytes < legacyRecipeBytes * 2 / 3,
            "old=" + legacyRecipeBytes + "; copy=" + recipeBytes);
        var selection = new EvidenceApplySelection { SessionReferences = references, SessionSearch = search, FamilyNames = new Dictionary<string, string> { ["group"] = "Fixture" } };
        var copiedSelection = Copy(selection);
        Check("Z12p5f8 selection copy preserves default JSON and detached mutable collections", JsonSerializer.Serialize(copiedSelection) == JsonSerializer.Serialize(selection)
            && copiedSelection.SessionReferences != references && copiedSelection.FamilyNames != selection.FamilyNames
            && copiedSelection.SessionSearch != search && copiedSelection.SessionReferences!.Zip(references).All(pair => pair.First != pair.Second
                && ReferenceEquals(pair.First.Sequence, pair.Second.Sequence)));
        foreach (var values in new List<NeighbourReference>?[] { null, new(), new() { null! }, new() { new() { Sequence = null! } } })
        {
            var variant = new EvidenceRecipe { SessionReferences = values };
            Check("Z12p5f8 session copy preserves null, empty and null-row/sequence distinctions", JsonSerializer.Serialize(Copy(variant)) == JsonSerializer.Serialize(variant));
            var selectionVariant = new EvidenceApplySelection { SessionReferences = values };
            Check("Z12p5f8 selection init setter preserves null, empty and null-row/sequence distinctions",
                JsonSerializer.Serialize(Copy(selectionVariant)) == JsonSerializer.Serialize(selectionVariant));
        }
        Check("Z12p5f8 object-typed generic copies keep the default JsonElement contract", Copy<object>(recipe) is JsonElement);
        copiedRecipe.SessionReferences!.RemoveAt(0); copiedRecipe.SessionSearch!.Neighbours.Clear();
        Check("Z12p5f8 editing copied recipe collections never edits the input", references.Count == 512 && search.Neighbours.Count == 1);
        Console.WriteLine(FormattableString.Invariant($"MEMORY Z12p5f8 allocatedBytes legacyRecipe={legacyRecipeBytes} recipeCopy={recipeBytes}"));

        recipe.ProteinSubclassLabels = new() { new() { SubclassKey = "prior", Reason = "Prior rule" } };
        recipe.ProteinSubclassLabelsPanelFingerprint = "prior panel";
        recipe.ProteinSubclassLabelsFingerprint = "prior labels";
        file.EvidenceRecipe = recipe;
        file.ProteinSubclassLabels = Copy(recipe.ProteinSubclassLabels);
        file.ProteinSubclassLabelsPanelFingerprint = recipe.ProteinSubclassLabelsPanelFingerprint;
        file.ProteinSubclassLabelsFingerprint = recipe.ProteinSubclassLabelsFingerprint;
        string derivedBefore = file.ToJsonText();
        start = GC.GetAllocatedBytesForCurrentThread();
        var derivedUndo = GroupingSnapshot.CaptureFamilyPanel(file);
        long derivedBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        file.FamilyPanel!.Name = "Changed rules";
        file.EvidenceRecipe.ProgramVersion = "Changed recipe";
        file.EvidenceRecipe.SessionReferences!.Clear();
        file.ProteinSubclassLabels[0].SubclassKey = "changed";
        file.ProteinSubclassLabelsPanelFingerprint = "changed panel";
        file.ProteinSubclassLabelsFingerprint = "changed labels";
        derivedUndo.Restore(file);
        Check("Z12p5f8 panel-derived snapshot restores complete recipe, labels and fingerprints", file.ToJsonText() == derivedBefore
            && file.EvidenceSource == source && file.EvidenceRecipe!.SessionReferences!.Count == 512);
        Check("Z12p5f8 panel-derived snapshot avoids copying large reference sequences", derivedBytes < 3 * 1024 * 1024,
            derivedBytes + " allocated bytes with source table and session references");
        file.EvidenceRecipe!.ProgramVersion = "Changed after restore";
        file.ProteinSubclassLabels![0].Reason = "Changed after restore";
        derivedUndo.Restore(file);
        Check("Z12p5f8 repeated panel-derived restore detaches recipe and label records", file.ToJsonText() == derivedBefore);
        file.EvidenceRecipe!.ProgramVersion = new string('A', 128 * 1024);
        string beforeRecipeUndo = file.ToJsonText();
        var recipeUndo = GroupingSnapshot.CaptureFamilyPanel(file);
        file.FamilyPanel!.Name = "Changed panel before blocked restore";
        file.EvidenceRecipe.ProgramVersion = "Changed recipe before blocked restore";
        file.ProteinSubclassLabels![0].SubclassKey = "changed";
        string recipeFailureState = file.ToJsonText();
        bool recipeUndoFailed = false;
        WithBlockedUndoTemp(() =>
        {
            try { recipeUndo.Restore(file); }
            catch (IOException) { recipeUndoFailed = true; }
        });
        Check("Z12p5f8 failed recipe preparation preserves every panel-derived field", recipeUndoFailed && file.ToJsonText() == recipeFailureState);
        recipeUndo.Restore(file);
        Check("Z12p5f8 a panel-derived Undo retries after recipe storage recovers", file.ToJsonText() == beforeRecipeUndo);
        Console.WriteLine(FormattableString.Invariant($"MEMORY Z12p5f8 allocatedBytes panelDerivedUndo={derivedBytes}"));

        string workbook = Path.Combine(folder, "working-memory-import.xlsx"); PanelImportTemplate.Write(workbook);
        FillTemplate(workbook, new[] { new[] { "group", "Fixture group", "", "", "", "Quantify" } });
        var observer = typeof(FamilyClusterFile).GetProperty("JsonWriteObserved", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = observer.GetValue(null); int copies = 0;
        observer.SetValue(null, (Action)(() => { if (new StackTrace().GetFrames().Any(f => f.GetMethod()?.Name == nameof(FamilyClusterFile.DeepCopy))) copies++; }));
        try
        {
            using var wizard = ReadyFamilyListImport(workbook, new CurationViewModel(ManualChoiceFile()));
            Check("Z12p5f8 native wizard suggestions never copy the whole document", copies == 0 && wizard.Page == 5,
                copies + " full document copies");
            var customFile = ManualChoiceFile(); string unchanged = customFile.ToJsonText();
            var custom = new MutatingImportMatcher();
            using var customWizard = ReadyFamilyListImport(workbook, new CurationViewModel(customFile), matcher: custom);
            Check("Z12p5f8 other matcher adapters still receive an isolated complete document", custom.Called && copies == 1
                && customFile.ToJsonText() == unchanged && customWizard.Page == 5);
        }
        finally { observer.SetValue(null, previous); }
    }

    private sealed class MutatingImportMatcher : IFamilyListImportMatches
    {
        public bool Called { get; private set; }
        public string SourceStatus => "Synthetic adapter";
        public string ChangeNotice => "";
        public IReadOnlyList<ImportMatchFamily> Suggest(FamilyClusterFile project, FamilyPanel panel)
        {
            Called = true;
            project.MyExtendedFastaItems[0].UserAnnotation = "Changed only in the detached input";
            panel.Name = "Changed only in the detached panel";
            return Array.Empty<ImportMatchFamily>();
        }
        public string ConfirmationWarning(IReadOnlyList<ImportEvidenceMatch> choices) => "";
        public FamilyPanel Accept(FamilyPanel panel, IReadOnlyList<ImportMatchFamily> choices) => panel;
    }
}
