using FamilyQuant.DeNovo;
using FamilyQuant.Homology;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static FamilyQuant.DeNovoScreenOperations;

namespace FamilyQuant;

/// <summary>De novo commands over a tested session, common filters and Core workflows.</summary>
public sealed partial class DeNovoViewModel : DeNovoSession
{
    public AppOperationHost Operations { get; set; } = new();
    private static Task<T> RunPhase<T>(FamilyQuant.Operations.OperationContext operation, Func<T> work) => Task.Run(() =>
    {
        operation.ThrowIfCancellationRequested();
        T result = work();
        operation.ThrowIfCancellationRequested();
        return result;
    });
    public DeNovoViewModel(DeNovoFilterViewModel? filters = null) : base(filters) => observedMode = Filters.ModeTag;

        public async Task ImportAsync()
        {
            if (IsBusy || Operations.IsBusy) return;
            // The criteria in force before this pass. A pass that imports nothing puts them back:
            // see RestoreCriteria.
            DeNovoFilterCriteria previousCriteria = Criteria;

            if (!ReadCriteriaFromBoxes()) return;   // Refuse() has already said which box, and why

            string directory = ImportDirectory.Trim();
            DeNovoSource source = ActiveDeNovoSource;

            if (directory.Length == 0 || !Directory.Exists(directory))
            {
                SetStatus(
                    "Nothing was imported: " + (directory.Length == 0
                        ? "no folder has been chosen yet."
                        : "the folder \"" + directory + "\" does not exist or cannot be reached.") +
                    RestoreCriteria(previousCriteria) +
                    " Next step: press Browse and pick the folder holding the selected de novo export.");
                return;
            }

            // Every Layer, not only the two this operation writes: see SetScreenEnabled.
            using var operation = Operations.Begin("Load and filter de novo");
            SetScreenEnabled(false);

            SetStatus("Reading the de novo exports (source: " + source.DisplayName + ") under " + directory + " and applying " +
                      CriteriaSentence() + ". A real export is a few hundred thousand rows, so this " +
                      "takes a moment.");

            try
            {
                var criteria = Criteria;
                Filters.TryBuild(out _, out var selections, out _);
                Func<string, DeNovoFilterCriteria, Task<DeNovoImport>>? seam = ImportOverride;

                // A real export is about 180,000 rows across three files. On the UI thread that freezes
                // the window; the whole screen is disabled meanwhile so nothing can be pressed twice.
                DeNovoImport import = seam != null
                    ? await seam(directory, criteria)
                    : await RunPhase(operation.Context, () => DeNovoSourceImport.Run(directory, criteria, selections));

                // The test seam predates source selection. Stamp its value object with the source the UI
                // actually showed so ApplyImport/status never mislabels the rows.
                if (seam != null || selections.Count == 1)
                {
                    import.SourceId = source.Id;
                    import.SourceDisplayName = source.DisplayName;
                    import.ConfidenceField = source.ConfidenceColumn;
                }

                // Explicitly back onto the UI thread: everything below writes controls.
                operation.Context.ThrowIfCancellationRequested();
                ApplyImport(import);
            }
            catch (OperationCanceledException) when (operation.Context.IsCancellationRequested)
            {
                SetStatus("Cancelled. Previous evidence and results were kept." + RestoreCriteria(previousCriteria));
            }
            catch (Exception ex)
            {
                SetStatus(
                    "The export could not be read, so nothing was imported and the screen still shows " +
                    "whatever it showed before. Reason given by the program: " + ex.Message +
                    RestoreCriteria(previousCriteria) +
                    " Next step: check that the folder really holds the selected export and that its files are " +
                    "not open in another program, then press \"Load and filter\" again.");
            }
            finally
            {
                // Re-enable the containers and let the state machine decide what is actually allowed:
                // an enabled group box does not enable a button whose Layer has nothing to work on.
                SetScreenEnabled(true);
                UpdateLayerStates();
            }
        }

        public async Task ReconcileAsync()
        {
            if (IsBusy || Operations.IsBusy) return;
            if (FilteredRows.Count == 0)
            {
                SetStatus("Nothing was reconciled: no de novo rows have been kept, so there is nothing " +
                          "to join. Next step: import the selected de novo export in Step 1 first.");
                return;
            }

            string path = SeproPackage.Trim();

            if (path.Length == 0 || !File.Exists(path))
            {
                SetStatus(
                    "Nothing was reconciled: " + (path.Length == 0
                        ? "no SEPro package has been chosen yet."
                        : "the file \"" + path + "\" does not exist or cannot be reached.") +
                    " Next step: press Browse in Step 2 and pick the .sepr2 of the same runs.");
                return;
            }

            // Every Layer, not only the two this operation writes: see SetScreenEnabled.
            using var operation = Operations.Begin("Reconcile de novo");
            SetScreenEnabled(false);

            SetStatus("Reading " + Path.GetFileName(path) + " and joining it to " +
                      FilteredRows.Count.ToString("N0", CultureInfo.CurrentCulture) +
                      " kept de novo row(s), by run file name, MS2 scan, reported charge and precursor m/z. A real " +
                      "package is tens of megabytes, so this takes a while.");

            try
            {
                var filtered = FilteredRows;

                int rawRows = LastImport?.RowsRead ?? filtered.Count;
                DeNovoFilterCriteria criteria = Criteria;
                DeNovoImport? imported = LastImport;
                DeNovoReconciliation outcome = await RunPhase(operation.Context, () =>
                    ReconcileForInspection(filtered, path, rawRows, criteria, imported));

                operation.Context.ThrowIfCancellationRequested();
                ApplyReconciliation(outcome);
            }
            catch (OperationCanceledException) when (operation.Context.IsCancellationRequested)
            {
                SetStatus("Cancelled. Previous evidence and results were kept.");
            }
            catch (Exception ex)
            {
                SetStatus(
                    "The SEPro package could not be read, so nothing was reconciled and the screen " +
                    "still shows whatever it showed before. Reason given by the program: " + ex.Message +
                    " Next step: check that the .sepr2 opens in SEPro and that it is not open in " +
                    "another program, then press Reconcile again.");
            }
            finally
            {
                SetScreenEnabled(true);
                UpdateLayerStates();
            }
        }

        public async Task ExtractAsync()
        {
            if (IsBusy || Operations.IsBusy) return;
            if (LastReconciliation == null)
            {
                SetStatus("Nothing was extracted: no reconciliation has been run, so it is not known " +
                          "which rows are the kept ones. Next step: press Reconcile in Step 2 first.");
                return;
            }

            RefreshRawMatchPreview();

            if (RawMatches.Count == 0 || RawMatches.Any(m => !m.Found))
            {
                SetStatus("Nothing was extracted: " + MissingRunsSentence() +
                          " was not found in \"" + (RawDirectory.Trim()) + "\". Next step: " +
                          "give the folder those acquisitions really are in. Extracting only the runs " +
                          "that were found would draw a de novo slice with a whole run silently missing.");
                UpdateLayerStates();
                return;
            }

            var matches = RawMatches.ToList();
            var keptByRun = KeptRowsByRun();
            string rawDirectory = RawDirectory.Trim();

            using var operation = Operations.Begin("Extract de novo XIC");
            SetScreenEnabled(false);
            SetStatus("Extracting " + matches.Count.ToString("N0", CultureInfo.CurrentCulture) +
                      " run(s), one at a time. Each acquisition is opened once and its MS1 spectra take " +
                      "tens of seconds to load; anything already in the cache beside the RAW files is " +
                      "answered without opening them at all.");

            try
            {
                // Progress arrives from the background thread, so it is marshalled rather than written
                // straight into a control.
                Action<string> progress = operation.Context.Messages("de-novo-extraction");

                DeNovoXicExtraction outcome = await RunPhase(operation.Context,
                    () => ExtractXic(matches, keptByRun, rawDirectory, progress));

                operation.Context.ThrowIfCancellationRequested();
                ApplyExtraction(outcome);
            }
            catch (OperationCanceledException) when (operation.Context.IsCancellationRequested)
            {
                SetStatus("Cancelled. Previous evidence and results were kept.");
            }
            catch (Exception ex)
            {
                SetStatus(
                    "The extraction did not finish, so nothing was measured and the screen still shows " +
                    "whatever it showed before. Reason given by the program: " + ex.Message +
                    " Next step: check that the .raw files are really acquisitions of these runs and " +
                    "that they are not open in another program, then press Extract again.");
            }
            finally
            {
                SetScreenEnabled(true);
                UpdateLayerStates();
            }
        }

        public async Task SearchAsync()
        {
            if (IsBusy || Operations.IsBusy) return;
            if (!ReadHomologySettingsFromBoxes()) return;   // Refuse() has already said which box, and why

            List<string> peptides = PeptidesForHomology();

            if (peptides.Count == 0)
            {
                SetStatus("Nothing was searched: no kept de novo row carries a sequence. Next step: " +
                          "import the selected de novo export in Step 1 first.");
                return;
            }

            string fcfPath = HomologyFcf.Trim();

            if (fcfPath.Length == 0 || !File.Exists(fcfPath))
            {
                SetStatus(
                    "Nothing was searched: " + (fcfPath.Length == 0
                        ? "no Family Cluster File has been chosen yet."
                        : "the file \"" + fcfPath + "\" does not exist or cannot be reached.") +
                    " Next step: press Browse in Step 4 and pick the .fcf clustered from the database " +
                    "these peptides should be attributed to.");
                return;
            }

            var options = HomologySettings;

            using var operation = Operations.Begin("Search peptide families");
            SetScreenEnabled(false);
            SetStatus("Searching " + peptides.Count.ToString("N0", CultureInfo.CurrentCulture) +
                      " distinct peptide(s) against " + Path.GetFileName(fcfPath) + " at " +
                      options.MinIdentity.ToString("P0", CultureInfo.CurrentCulture) +
                      " identity. Tens of thousands of alignments take a couple of minutes.");

            try
            {
                DeNovoHomology outcome = await RunPhase(operation.Context, () =>
                {
                    FamilyClusterFile fcf = LoadHomologyCuration(fcfPath);
                    return SearchHomology(peptides, fcf, options, fcfPath, operation.Context);
                });

                operation.Context.ThrowIfCancellationRequested();
                ApplyHomology(outcome);
            }
            catch (OperationCanceledException) when (operation.Context.IsCancellationRequested)
            {
                SetStatus("Cancelled. Previous evidence and results were kept.");
            }
            catch (Exception ex)
            {
                SetStatus(
                    "The search did not finish, so nothing was attributed and the screen still shows " +
                    "whatever it showed before. Reason given by the program: " + ex.Message +
                    " Next step: check that the file really is a .fcf written by FamilyQuant and that " +
                    "it is not open in another program, then press Search again.");
            }
            finally
            {
                SetScreenEnabled(true);
                UpdateLayerStates();
            }
        }

        public async Task PartitionAsync()
        {
            if (IsBusy || Operations.IsBusy) return;
            if (LastExtraction == null)
            {
                SetStatus("Nothing was partitioned: no run has been extracted, so there is no de novo " +
                          "segment to draw. Next step: press Extract in Step 3 first.");
                return;
            }

            string plpPath = PartitionPlp.Trim();
            string xic2Path = PartitionXic2.Trim();

            if (plpPath.Length == 0 && xic2Path.Length == 0)
            {
                SetStatus("Nothing was partitioned: neither a PatternLab project nor a .xic2 has been " +
                          "chosen, so the identified segment of each bar has no source. Next step: press " +
                          "Browse in Step 5 and pick the .plp of these runs.");
                return;
            }

            if (plpPath.Length > 0 && !File.Exists(plpPath))
            {
                SetStatus("Nothing was partitioned: the file \"" + plpPath + "\" does not exist or " +
                          "cannot be reached. Next step: correct that path and press \"Build partition\" " +
                          "again.");
                return;
            }

            var matches = RawMatches.ToList();
            var xicByRun = new Dictionary<string, DeNovoBatchStats>(LastExtraction.ByRun,
                                                                    StringComparer.OrdinalIgnoreCase);

            using var operation = Operations.Begin("Build de novo partition");
            SetScreenEnabled(false);
            SetStatus("Measuring the whole chromatogram of " +
                      matches.Count.ToString("N0", CultureInfo.CurrentCulture) +
                      " run(s) and asking the PatternLab side for its quants per run. Reading three " +
                      "whole chromatograms takes a while.");

            try
            {
                Action<string> progress = operation.Context.Messages("de-novo-partition");

                DeNovoPartition outcome = await RunPhase(operation.Context,
                    () => BuildPartition(matches, xicByRun, plpPath, xic2Path.Length == 0 ? null : xic2Path,
                                         progress));

                operation.Context.ThrowIfCancellationRequested();
                ApplyPartition(outcome);
            }
            catch (OperationCanceledException) when (operation.Context.IsCancellationRequested)
            {
                SetStatus("Cancelled. Previous evidence and results were kept.");
            }
            catch (Exception ex)
            {
                SetStatus(
                    "The partition could not be built, so nothing was drawn and the screen still shows " +
                    "whatever it showed before. Reason given by the program: " + ex.Message +
                    " Next step: check that the .plp and the .xic2 belong to these same runs and that " +
                    "neither is open in another program, then press \"Build partition\" again.");
            }
            finally
            {
                SetScreenEnabled(true);
                UpdateLayerStates();
            }
        }
}
