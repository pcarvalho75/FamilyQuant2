using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed class DeliveryPreparation
    {
        public string Input { get; set; } = string.Empty;
        public string InputSha256 { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string ProgramVersion { get; set; } = string.Empty;
        public string PreparedSha256 { get; set; } = string.Empty;
    }

    private static readonly JsonSerializerOptions DeliveryJson = new() { WriteIndented = true };

    private static (EvidenceGroupingSession Session, EvidenceReviewViewModel Review) DeliveryReview(FamilyClusterFile file, DeliveryPreparation preparation)
    {
        var stored = file.EvidenceSource?.NearestReviewed ?? throw new InvalidDataException("Prepare and save the reviewed search first.");
        var session = new EvidenceGroupingSession(file, preparation.ProgramVersion, (queries, references) =>
            NearestReviewed.IsCurrent(stored, queries, references) ? stored
            : throw new InvalidDataException("The prepared search no longer matches the file and its scoped panel. Prepare it again."));
        return (session, new EvidenceReviewViewModel(session.Propose(preparation.Scope)));
    }

    private static void CompleteEvidenceDelivery(string preparedDirectory, string outputDirectory)
    {
        if (Directory.Exists(outputDirectory)) throw new IOException("Use a new output directory.");
        var preparation = ReadDelivery<DeliveryPreparation>(Path.Combine(preparedDirectory, "preparation.json"));
        string preparedPath = Path.Combine(preparedDirectory, "prepared.fcf");
        if (TestFileHash.Upper(preparedPath) != preparation.PreparedSha256 || TestFileHash.Upper(preparation.Input) != preparation.InputSha256)
            throw new InvalidDataException("The input or prepared file changed. Nothing was created.");
        var file = FamilyClusterFile.Load(preparedPath);
        var (session, review) = DeliveryReview(file, preparation);
        var defaults = review.CaptureSelection();
        string choicePath = Path.Combine(preparedDirectory, "choice.json");
        var choice = File.Exists(choicePath) ? ReadDelivery<EvidenceReviewSelection>(choicePath) : defaults;
        if (choice.SourceFingerprint != defaults.SourceFingerprint || !choice.ProteinIndices.SequenceEqual(defaults.ProteinIndices)
            || JsonSerializer.Serialize(choice.Names.OrderBy(p => p.Key, StringComparer.Ordinal)) != JsonSerializer.Serialize(defaults.Names.OrderBy(p => p.Key, StringComparer.Ordinal))
            || choice.MotifPattern.Length != 0 || choice.ProteinOverrides?.Count > 0)
            throw new InvalidDataException("This delivery command requires the review window's default protein selection and names.");
        var expectedChoices = defaults.ArchitectureChoices ?? Array.Empty<ReviewArchitectureChoice>();
        var actualChoices = choice.ArchitectureChoices ?? Array.Empty<ReviewArchitectureChoice>();
        if (!actualChoices.Select(c => c with { Split = false }).SequenceEqual(expectedChoices.Select(c => c with { Split = false })))
            throw new InvalidDataException("Only an explicit split of a supplied architecture group may differ from the defaults.");
        string leftOut = LeftOutList.BuildCsv(LeftOutList.Build(file.MyExtendedFastaItems, session.Proposal!));
        var applied = session.Apply(choice);
        Directory.CreateDirectory(outputDirectory);
        string delivery = Path.Combine(outputDirectory, "delivery");
        WriteDelivery(file, preparation, choice, review.Source, leftOut, delivery);

        // Replay through the same app session used by the window, including its explicit selection.
        var replay = FamilyClusterFile.Load(preparedPath);
        var (replaySession, replayReview) = DeliveryReview(replay, preparation);
        string replayLeftOut = LeftOutList.BuildCsv(LeftOutList.Build(replay.MyExtendedFastaItems, replaySession.Proposal!));
        var recipe = applied.Recipe;
        var replayChoice = new EvidenceReviewSelection(recipe.SourceFingerprint, recipe.SelectedProteinIndices,
            recipe.Families.Where(f => f.Selected).ToDictionary(f => f.Family, f => f.Name, StringComparer.Ordinal),
            recipe.ArchitectureChoices.Select(c => new ReviewArchitectureChoice(c.Family, c.ArchitectureKey, c.Name, c.Split)).ToArray());
        replaySession.Apply(replayChoice);
        if (replay.ToJsonText() != file.ToJsonText()) throw new InvalidDataException("The app session's saved-recipe replay differs from the created file.");
        string repeated = Path.Combine(outputDirectory, "replay");
        WriteDelivery(replay, preparation, choice, replayReview.Source, replayLeftOut, repeated);
        if (!Directory.GetFiles(delivery).All(path => File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(Path.Combine(repeated, Path.GetFileName(path))))))
            throw new InvalidDataException("The delivery and recipe replay exports are not byte-identical.");
        Check("A1 all native delivery and app-session recipe replay files are byte-identical", true);

        string? previousSettings = MainWindow.ShellSettingsPathOverride;
        MainWindow.ShellSettingsPathOverride = Path.Combine(outputDirectory, "app-check-settings.json");
        var previousChoice = MainWindow.UnsavedProjectChoiceOverride;
        MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
        try
        {
            var main = new MainWindow();
            try
            {
                bool opened = main.OpenProjectPath(Path.Combine(delivery, "families.fcf"));
                if (!opened || main.NavigationModel.Document?.ToJsonText() != file.ToJsonText())
                    throw new InvalidDataException("The saved delivery did not open with the same curation in the app.");
                Check("A1 the saved file opens through the app's ordinary Open path with the same curation", true);
            }
            finally { main.Close(); }
        }
        finally { MainWindow.ShellSettingsPathOverride = previousSettings; MainWindow.UnsavedProjectChoiceOverride = previousChoice; }
        Console.WriteLine("Delivery: " + delivery);
    }

    private static void WriteDelivery(FamilyClusterFile file, DeliveryPreparation preparation, EvidenceReviewSelection choice,
        EvidenceReviewInput review, string leftOut, string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "families.fcf");
        file.Save(path);
        var check = SavedFileCheck.Check(file, path);
        if (!check.Passed) throw new InvalidDataException(check.Message);
        WriteDeliveryJson(directory, "recipe.json", file.EvidenceRecipe);
        WriteDeliveryJson(directory, "selection.json", choice);
        WriteDeliveryJson(directory, "review.json", review);
        WriteDeliveryJson(directory, "file-check.json", check);
        File.WriteAllText(Path.Combine(directory, "audit.csv"), EvidenceExports.BuildAuditCsv(file));
        File.WriteAllText(Path.Combine(directory, "left-out.csv"), leftOut);
        WriteDeliveryJson(directory, "provenance.json", new
        {
            preparation.Input, preparation.InputSha256, preparation.PreparedSha256, preparation.ProgramVersion,
            file.SourceSha256, file.SourceSequenceCount, ScopeTerms = file.EvidenceRecipe!.Options.ScopeTerms,
            EvidenceProvider = file.EvidenceSource?.Provider, EvidenceRelease = file.EvidenceSource?.Release,
            PanelRelease = file.EvidenceSource?.ReferencePanel?.Release,
            RecipeSha256 = TestFileHash.Upper(Path.Combine(directory, "recipe.json")),
            EvidenceUnresolvedAccessions = file.EvidenceSource?.UnresolvedAccessions,
            Quantitation = "Curation only; no XIC computation or abundance claim in this file.",
        });
        string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
        string[] splits = (choice.ArchitectureChoices ?? Array.Empty<ReviewArchitectureChoice>()).Where(c => c.Split)
            .Select(c => c.Family + ": " + c.Name + " [" + c.ArchitectureKey + "]").ToArray();
        File.WriteAllText(Path.Combine(directory, "delivery-note.txt"),
            "FamilyQuant " + preparation.ProgramVersion + "\nUniProt release: " + file.EvidenceSource?.Release
            + "\nScope: " + string.Join("; ", file.EvidenceRecipe.Options.ScopeTerms)
            + "\nSelection: review window defaults, unflagged proposals selected and flagged rows left unselected."
            + "\nSelected proteins: " + Number(choice.ProteinIndices.Count)
            + "\nSelected groups: " + Number(file.EvidenceRecipe.Assignments.Select(a => a.FamilyId).Distinct().Count())
            + "\nFlagged review rows: " + Number(review.Proteins.Count(p => p.Flags.Count > 0))
            + "\nExplicit architecture choices:\n" + (splits.Length == 0 ? "None" : string.Join("\n", splits))
            + "\nOther choices: none.\n"
            + "All unselected proteins, including ambiguous or flagged proposals, retain their source grouping.\n"
            + "These are evidence-based family groupings, not measured abundances. No XIC was computed.\n"
            + check.Message + "\n");
    }

    private static T ReadDelivery<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path))
        ?? throw new InvalidDataException("The delivery document is empty: " + path);
    private static void WriteDeliveryJson<T>(string directory, string name, T value) =>
        File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(value, DeliveryJson));

    private static async Task PrepareEvidenceDelivery(string input, string directory, string scope, string searchSource)
    {
        if (Directory.Exists(directory)) throw new IOException("Use a new preparation directory.");
        string inputHash = TestFileHash.Upper(input);
        var file = FamilyClusterFile.Load(input);
        var evidence = await EvidenceFetcher.FetchAsync(file, progress: new DeliveryProgress("Evidence")).ConfigureAwait(false);
        string[] terms = WordCluster.ParseKeywords(scope).ToArray();
        if (terms.Length == 0) throw new InvalidDataException("Supply the scope confirmed for this delivery.");
        var panel = await ReferencePanelFetcher.FetchAndStoreAsync(file,
            e => EvidenceClassifier.IsInScope(e, terms), progress: new DeliveryProgress("Panel")).ConfigureAwait(false);
        if (evidence.Provenance.Release != panel.Release)
            throw new InvalidDataException("The protein evidence and panel releases differ. Nothing was saved.");
        var queries = NearestReviewed.Targets(file);
        var references = NearestReviewed.References(file, e => EvidenceClassifier.IsInScope(e, terms));
        var prior = FamilyClusterFile.Load(searchSource).EvidenceSource?.NearestReviewed;
        bool reused = NearestReviewed.IsCurrent(prior, queries, references);
        Console.WriteLine("Stored search exactly current: " + reused);
        file.EvidenceSource!.NearestReviewed = reused ? prior : NearestReviewed.Search(queries, references);
        if (TestFileHash.Upper(input) != inputHash) throw new InvalidDataException("The source file changed during preparation.");
        Directory.CreateDirectory(directory);
        string prepared = Path.Combine(directory, "prepared.fcf"); file.Save(prepared);
        var preparation = new DeliveryPreparation { Input = Path.GetFullPath(input), InputSha256 = inputHash,
            PreparedSha256 = TestFileHash.Upper(prepared), Scope = scope, ProgramVersion = AppVersionInfo.Current() };
        WriteDeliveryJson(directory, "preparation.json", preparation);
        var (_, review) = DeliveryReview(file, preparation);
        WriteDeliveryJson(directory, "review.json", review.Source);
        WriteDeliveryJson(directory, "defaults.json", review.CaptureSelection());
        WriteDeliveryJson(directory, "fetch.json", new { evidence.Asked, evidence.Found, evidence.Missing, evidence.Provenance,
            PanelFamilies = panel.Families.Count, PanelEntries = panel.Entries.Count, PanelRelease = panel.Release, ReusedSearch = reused });
        Console.WriteLine("Prepared review: " + directory);
        Console.WriteLine(review.Summary);
    }

    private sealed class DeliveryProgress(string stage) : IProgress<FetchProgress>
    {
        public void Report(FetchProgress value) => Console.WriteLine(stage + " "
            + value.CompletedBatches.ToString(CultureInfo.InvariantCulture) + "/" + value.TotalBatches.ToString(CultureInfo.InvariantCulture)
            + ", found " + value.Found.ToString(CultureInfo.InvariantCulture));
    }

    private static int EvidenceDeliveryCommand(string[] args)
    {
        try
        {
            if (args.Length == 6 && args[1] == "prepare") PrepareEvidenceDelivery(args[2], args[3], args[4], args[5]).GetAwaiter().GetResult();
            else if (args.Length == 4 && args[1] == "create") { EnsureApplication(); CompleteEvidenceDelivery(args[2], args[3]); }
            else if (args.Length == 5 && args[1] == "split")
            {
                var defaults = ReadDelivery<EvidenceReviewSelection>(Path.Combine(args[2], "defaults.json"));
                var groups = defaults.ArchitectureChoices ?? Array.Empty<ReviewArchitectureChoice>();
                if (groups.Count(g => g.Family == args[3] && g.ArchitectureKey == args[4]) != 1)
                    throw new InvalidDataException("Select exactly one architecture key from the program's review packet.");
                var choice = defaults with { ArchitectureChoices = groups.Select(g => g with { Split = g.Family == args[3] && g.ArchitectureKey == args[4] }).ToArray() };
                WriteDeliveryJson(args[2], "choice.json", choice);
                Console.WriteLine(JsonSerializer.Serialize(choice.ArchitectureChoices!.Where(c => c.Split), DeliveryJson));
            }
            else throw new ArgumentException("Use --evidence-delivery prepare <source.fcf> <new-directory> <scope> <stored-search.fcf>, split <prepared-directory> <family> <architecture-key>, or create <prepared-directory> <new-output-directory>.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine(ex.GetBaseException()); return 1; }
    }

    private static void EvidenceDeliveryFixtureTests(string workDir)
    {
        try
        {
            string root = Path.Combine(workDir, "evidence-delivery");
            Directory.CreateDirectory(root);
            string preparation = Path.Combine(root, "prepared"); Directory.CreateDirectory(preparation);
            var file = TaskFile();
            const string scope = "\"Leading scope\"";
            file.EvidenceSource!.NearestReviewed = NearestReviewed.Search(NearestReviewed.Targets(file),
                NearestReviewed.References(file, e => EvidenceClassifier.IsInScope(e, new[] { "Leading scope" })));
            string source = Path.Combine(preparation, "prepared.fcf"); file.Save(source);
            File.WriteAllText(Path.Combine(preparation, "preparation.json"), JsonSerializer.Serialize(new DeliveryPreparation
            {
                Input = source, InputSha256 = TestFileHash.Upper(source), PreparedSha256 = TestFileHash.Upper(source),
                Scope = scope, ProgramVersion = AppVersionInfo.Current(),
            }, DeliveryJson));
            string output = Path.Combine(root, Guid.NewGuid().ToString("N"));
            CompleteEvidenceDelivery(preparation, output);
            var created = FamilyClusterFile.Load(Path.Combine(output, "delivery", "families.fcf"));
            Check("A1 native delivery uses reviewed defaults and records the running version", created.EvidenceRecipe is { } recipe
                && recipe.Assignments.Count == 2 && recipe.ProgramVersion == AppVersionInfo.Current()
                && recipe.Assignments.All(a => file.MyExtendedFastaItems[a.ProteinIndex].Evidence?.Keywords.Contains("Leading scope") == true));
            Check("A1 native delivery publishes its exact recipe and ordinary exports", File.Exists(Path.Combine(output, "delivery", "recipe.json"))
                && File.ReadAllText(Path.Combine(output, "delivery", "audit.csv")) == EvidenceExports.BuildAuditCsv(created)
                && File.Exists(Path.Combine(output, "delivery", "left-out.csv")));
        }
        catch (Exception ex) { Check("A1 native default-review, save, export, replay and app-open fixture", false, ex.GetBaseException().Message); }
    }
}
