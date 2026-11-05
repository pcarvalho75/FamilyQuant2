using FamilyQuant.DeNovo;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant;

public sealed record ComputeXicRequest(string RawDirectory, string SeproPackage, string? DeNovoDirectory, FullXicOptions Options)
{
    public OperationContext? Operation { get; init; }
}

/// <summary>Quantify state, validation, re-entry protection and completed-result publication.</summary>
public sealed class ComputeXicViewModel : ScreenViewModel
{
    private readonly string appVersion;
    private readonly FamilyClusterFile? curation;
    private readonly Func<ComputeXicRequest, IProgress<string>, CancellationToken, Task<FullXicComputeResult>> compute;
    private string raw = "", sepro = "", deNovo = "", status = "Choose the RAW folder and the identification package.", summary = "No computation yet.", details = HelpWorkflowFacts.Quantify;
    private int? isotopes = DeNovoXicExtractor.DefaultEnvelopeIsotopeCount, points = DeNovoXicExtractor.DefaultMinPointsForQuant;
    private bool busy;
    private bool patternLabWinsScanConflicts = true;
    public bool PatternLabWinsScanConflicts { get => patternLabWinsScanConflicts; set => Set(ref patternLabWinsScanConflicts, value); }
    private CancellationTokenSource? cancellation;
    public AppOperationHost Operations { get; set; } = new();
    public string? OperationLogPath { get; private set; }
    public DateTimeOffset? OperationStartedAtUtc { get; private set; }
    public DeNovoFilterViewModel Filters { get; }
    public string RawDirectory { get => raw; set => Set(ref raw, value); }
    public string SeproPackage { get => sepro; set => Set(ref sepro, value); }
    public string DeNovoDirectory { get => deNovo; set { if (Set(ref deNovo, value)) Filters.ProposeSourceForDirectory(value.Trim()); } }
    private string searchParametersPath = "";
    public string SearchParametersPath { get => searchParametersPath; set => Set(ref searchParametersPath, value); }
    public Report.ReportInputProvenance? CompletedInputProvenance { get; private set; }
    public string? SourceDatabaseFileName { get; internal set; }
    public string? SourceDatabaseFileSha256 { get; internal set; }
    public int? IsotopeCeiling { get => isotopes; set { Set(ref isotopes, value); Changed(nameof(Basis)); } }
    public int? MinimumPoints { get => points; set { Set(ref points, value); Changed(nameof(Basis)); } }
    public int IsotopeCount => IsotopeCeiling is >= 1 ? IsotopeCeiling.Value : DeNovoXicExtractor.DefaultEnvelopeIsotopeCount;
    public int MinPointsForQuant => MinimumPoints is >= 1 ? MinimumPoints.Value : DeNovoXicExtractor.DefaultMinPointsForQuant;
    public string Basis => $"Up to {IsotopeCount} isotopes; at least {MinPointsForQuant} points per peak. " +
        $"Window {DeNovoXicExtractor.ProductionPpm} ppm; MS1 tolerance {DeNovoXicExtractor.ProductionMs1Tolerance}.";
    public string BasisDetails => "An absent isotope adds nothing. The point count includes the two padding points. " +
        "A refused peak is reported as identified but not quantified. At the legacy one-point rule a single detection contributes its height rather than an integrated area.";
    public bool IsBusy { get => busy; private set { Set(ref busy, value); Changed(nameof(CanEdit)); ComputeCommand.Refresh(); CancelCommand.Refresh(); } }
    public bool CanEdit => !IsBusy;
    public string Status { get => status; private set => Set(ref status, value); }
    public string RunSummary { get => summary; private set => Set(ref summary, value); }
    public string Details { get => details; private set => Set(ref details, value); }
    public FullXicComputeResult? Result { get; private set; }
    public bool Computed => Result != null;
    public ScreenCommand ComputeCommand { get; }
    public ScreenCommand CancelCommand { get; }
    public event Action<FullXicComputeResult>? Completed;

    public ComputeXicViewModel(string appVersion, QuantManifest? manifest = null, FamilyClusterFile? familyCuration = null,
        DeNovoFilterViewModel? filters = null,
        Func<ComputeXicRequest, IProgress<string>, CancellationToken, Task<FullXicComputeResult>>? compute = null)
    {
        this.appVersion = appVersion;
        curation = familyCuration;
        patternLabWinsScanConflicts = manifest?.PatternLabWinsScanConflicts ?? true;
        Filters = filters ?? new(manifest);
        this.compute = compute ?? RunEngine;
        ComputeCommand = new(async () => await ComputeAsync(), () => !IsBusy);
        CancelCommand = new(() => { Operations.Cancel(); Status = "Cancelling after the current phase..."; }, () => IsBusy);
    }

    public void ShowStatus(string message) => Status = message;

    public async Task ComputeAsync()
    {
        if (IsBusy || Operations.IsBusy) return;
        string rawPath = RawDirectory.Trim(), seproPath = SeproPackage.Trim(), deNovoPath = DeNovoDirectory.Trim();
        if (!Directory.Exists(rawPath)) { Status = "Nothing was computed: the RAW folder does not exist or cannot be reached: " + rawPath + ". Enter the folder containing the .raw acquisitions."; return; }
        if (!File.Exists(seproPath)) { Status = "Nothing was computed: the SEPro identification package does not exist or cannot be reached: " + seproPath + ". Choose the .sepr2 of the same runs as the acquisitions."; return; }
        if (deNovoPath.Length > 0 && !Directory.Exists(deNovoPath)) { Status = "Nothing was computed: the de novo folder does not exist or cannot be reached: " + deNovoPath + ". Enter its export root, or leave it empty for identified peptides only."; return; }
        if (IsotopeCount > 6 || MinPointsForQuant > 20) { Status = "Use 1 to 6 isotopes and 1 to 20 points per peak."; return; }
        Filters.ProposeSourceForDirectory(deNovoPath);
        if (!Filters.TryBuild(out var criteria, out var selections, out string error)) { Status = error; return; }
        var request = new ComputeXicRequest(rawPath, seproPath, deNovoPath.Length == 0 ? null : deNovoPath,
            new FullXicOptions { IsotopeCount = IsotopeCount, MinPointsForQuant = MinPointsForQuant,
                Ppm = DeNovoXicExtractor.ProductionPpm, Ms1Tolerance = DeNovoXicExtractor.ProductionMs1Tolerance,
                AppVersion = appVersion, FamilyCuration = curation, DeNovoCriteria = criteria, DeNovoSelections = selections,
                PatternLabWinsScanConflicts = PatternLabWinsScanConflicts });
        OperationStartedAtUtc = DateTimeOffset.UtcNow; OperationLogPath = AppOperationHost.CreateLogPath("compute");
        using var operation = Operations.Begin(WorkflowNames.Quantify, OperationStartedAtUtc, OperationLogPath);
        request = request with { Operation = operation.Context };
        var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(operation.Context.CancellationToken);
        using var tokenLifetime = tokenSource;
        cancellation = tokenSource;
        IsBusy = true;
        Status = "Computing full XIC...";
        var progress = new Progress<string>(message =>
        {
            if (!ReferenceEquals(cancellation, tokenSource) || tokenSource.IsCancellationRequested) return;
            Status = message;
            Operations.LogMessage("full-xic-detail", message);
        });
        try
        {
            operation.Context.Report("compute-provenance", "Reading input provenance", 0, 1);
            var inputProvenance = Report.ReportInputCapture.Read(SourceDatabaseFileName ?? curation?.ReportInputProvenance?.DatabaseFileName,
                seproPath, string.IsNullOrWhiteSpace(SearchParametersPath) ? null : SearchParametersPath.Trim(),
                SourceDatabaseFileSha256 ?? curation?.ReportInputProvenance?.DatabaseFileSha256 ?? "");
            operation.Context.Report("compute-provenance", "Input provenance read", 1, 1);
            FullXicComputeResult outcome = await compute(request, progress, tokenSource.Token);
            tokenSource.Token.ThrowIfCancellationRequested();
            operation.Context.Report("compute-publish", "Attaching completed results", 0, 1);
            operation.Context.Report("compute-publish", "Attaching completed results", 1, 1);
            Result = outcome;
            CompletedInputProvenance = inputProvenance;
            RunSummary = $"{outcome.Rows.Count.ToString("N0", CultureInfo.CurrentCulture)} quant rows across " +
                $"{outcome.Manifest.PerRunTic.Count.ToString("N0", CultureInfo.CurrentCulture)} runs. " +
                $"{outcome.IdentifiedRows:N0} identified; {outcome.DeNovoRows:N0} de novo.";
            Details = string.Join("\n", outcome.TicShareSummary.Concat(outcome.Notes)) +
                (outcome.Manifest.DeNovoInputs.Count == 0 ? "" : "\nDe novo inputs: " + string.Join("; ",
                    outcome.Manifest.DeNovoInputs.GroupBy(i => i.SourceId).Select(g => g.First().SourceDisplayName +
                    " (" + g.First().CutoffDescription + ", " + g.Count() + " files)"))) +
                (string.IsNullOrWhiteSpace(outcome.SpectrumComparisonCsvPath) ? "" : "\nCSV: " + outcome.SpectrumComparisonCsvPath) +
                (string.IsNullOrWhiteSpace(outcome.SpectrumComparisonCsvError) ? "" : "\n" + outcome.SpectrumComparisonCsvError) +
                "\n\n" + HelpWorkflowFacts.Quantify;
            Status = "Computed. The results are held in memory; the project itself has not been saved yet. Use File > Save to keep the quant rows and complete spectrum evidence table.";
            Changed(nameof(Result)); Changed(nameof(Computed));
            operation.Dispose();
            Completed?.Invoke(outcome);
        }
        catch (OperationCanceledException) when (tokenSource.IsCancellationRequested) { Status = "Cancelled. The previous project results were kept."; }
        catch (FullXicNoMatchedRunsException ex) { Operations.LogFailure(ex); Status = ex.Message; }
        catch (Exception ex) { Operations.LogFailure(ex); Status = "The computation did not finish. " + ex.Message; }
        finally { cancellation = null; IsBusy = false; }
    }

    private static Task<FullXicComputeResult> RunEngine(ComputeXicRequest request, IProgress<string> progress, CancellationToken token) =>
        Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            return FullXicComputer.ComputeFullXic(request.RawDirectory, request.SeproPackage, request.DeNovoDirectory,
                request.Options, null, message => { token.ThrowIfCancellationRequested(); progress.Report(message); }, operation: request.Operation);
        });
}
