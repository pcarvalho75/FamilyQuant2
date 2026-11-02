# FamilyQuant working guide

FamilyQuant 2.0 is a Windows/.NET 10 WPF program for protein-family clustering and quantitation.
It installs beside frozen 1.5, opens its files and retains an old-compatible `.fcf` export.
[ROADMAP.md](docs/ROADMAP.md) owns unfinished work; [INDEX.md](docs/INDEX.md) catalogs commands.
The complete previous guide, measurements and implementation notes are in
[HISTORY.md](docs/HISTORY.md). Historical counts are not current validation.

## Rules

- Only the program produces scientific results. Scratch scripts are not a delivery route.
- Evidence and data determine grouping. No family, protein-name, domain-code or keyword
  exception lists belong in an automatic rule. Store user choices in the project/recipe.
- Preserve the [feature ledger](docs/FEATURE-LEDGER.md). Searches, planned checks and green
  builds do not close rows; executed evidence and independent review do.
- Preserve frozen numbers unless the roadmap authorizes a measured change. D14 retains
  clustering defaults; D15 defines the exact common contaminant predicate.
- Never tune a rule to recover a known answer. Report unsupported cases plainly.
- Use InvariantCulture for parsing/exports, current culture for display. Repeat data outputs
  byte-for-byte. Fixtures, stored-project parity and fresh RAW validation are different checks.
- Keep protected presentation inputs/producers untouched. Paulo owns publishing, versions and
  publish profiles. Workers do not push. The roadmap and mission own file/lane boundaries.
- Use ordinary hyphens and plain language. Read a module and its checks before changing it.

## Build and test

Run from the checkout root, one build/test operation at a time. On the shared team machine:

```powershell
$env:DOTNET_PROCESSOR_COUNT = '4'
dotnet build FamilyQuant2.sln -c Release -v q
powershell -NoProfile -ExecutionPolicy Bypass -File D:/FamilyQuant-wt/desk/quick-suite.ps1
```

The desk runs the full suite at merges. During a step, run focused checks; quick/golden belong
at READY. Merge master when its dependency is needed or at the READY checkpoint. Use the
lane-specific TEMP setup in the catalog to avoid fixture collisions.

Tests are a console executable, not a `dotnet test` project. Optional positional inputs are
`.plp`, FASTA/`.T-R`, de novo export root, then the acquisition-backed data directory. Use `.`
for an absent slot: Windows PowerShell 5.1 drops empty native arguments. Missing real data
skips those checks; quick has no Desktop fallback. PASS/FAIL/SKIP and exit code are the result.
Stored-project `--golden` normally compares four exports; 4/0/0 is not fresh extraction or a
biological accuracy claim. Detailed commands are in [INDEX.md](docs/INDEX.md).

## Projects and boundaries

- `FamilyQuant.Core`: WPF-free clustering, evidence, quantitation, homology, de novo and files.
- `FamilyQuant/FamilyQuant.App.csproj`: WPF views, view models, shell, dialogs and Help.
- `FamilyQuant.Tests`: console checks, frozen comparison helpers and study-only checkers.

Core raises no message boxes. Its operation context provides progress, cancellation and a
final commit, not automatic background scheduling. See [CORE-OPERATIONS.md](docs/CORE-OPERATIONS.md).
HintPath references do not flow through ProjectReference; each consumer names vendor types.

## Clustering and curation

`USearchTools.GenerateFCF` delegates the typed operation. Fast centroid clustering uses identity.
The optional exhaustive pass single-links those families through edges satisfying identity and
`alnlen >= identity/100 * max(query length, target length)`. It cannot undo a fast join. A connected
component does not promise that every member pair meets the threshold. Production exhaustive
clustering is not capped; confirmation above 10,000 records is not sampling. Pairwise tables are
memoized on disk by path/content SHA256 and streamed; failed/empty tables retry and fallback is
explicit. The diagnostic's optional all-pairs mode has a separate 1,000-record limit.
See [IMPROVEMENTS.md](docs/IMPROVEMENTS.md) and [CLUSTERING-REFACTOR.md](docs/CLUSTERING-REFACTOR.md).

`UserID` is the working grouping used by quantitation, tables and exports. `USearchID` tracks
last automatic grouping and differs for hand-curated locked families. Old `XIC` loads but new
writes omit it; `XICUser` is the PatternLab reading. Locks survive reclustering. Snapshots retain
IDs, locks, word settings and, for batch undo, touched annotations. Keep `PushUndoSnapshot()`
single and non-overloaded for retained delegate/reflection seams.

Word grouping uses TF-IDF cosine similarity. Word boxes share `ParseKeywords` and whole-word/
phrase matching before ` OS=`; Search uses comma/semicolon alternatives across descriptions,
identifiers and domains. Locked families stay outside automatic merges. Family names come from
user annotations or the stable most-frequent protein name. Details and old regressions remain
in history and [CURATION-PRESENTATION.md](docs/CURATION-PRESENTATION.md).

`SequenceLabels` owns prefixes. D15 unwraps one exact decoy prefix, then checks the exact-case
database contaminant prefix. Names are not a contaminant rule. `CalculateFDR` is a compatibility
name for target/decoy family mixing, not statistical FDR or a measure of over-merging.

## Scientific contracts

The optional PatternLab route attributes to current UserID families with an explicit denominator
and shared-peptide policy. Recorded denominators are identified signal, TIC, extracted peptide
signal and charge-restricted signal; missing values are unavailable, never substituted. Its
policies are family-unique, equal splitting and full counting in every family. Overlapping full
counts must not be summed. No "unbiased" normalization remains.

The Full XIC primary estimate combines identified observed envelope and mass-stratified
reference-calibrated de novo M+0. Keep primary, M+0 sensitivity and unadjusted diagnostics distinct.
The legacy three-way partition compares M+0 on both sides and never clamps its remainder.
Runs match by normalized file name, never index. Sum distinct peaks, not MS2 anchors. Six isotopes
is an empirical ceiling; minimum points is per cluster/isotope. Refused areas and estimate flags
survive. Do not introduce an averagine crediting policy or mix numeric bases in one chart.

Identified attribution keeps target proteins tied at the best score, equivalent to exact
containment with I/L folding. De novo keeps all targets past its identity gate. A decoy wins an
exact tie. Family attribution must not use the mapper's ordinary top-three cutoff. Spectrum
rows retain every family ID/label, mapping threshold and not-run/unavailable/unattributed state.
See [SCIENTIFIC_METHOD.md](SCIENTIFIC_METHOD.md) and [QUANTITATION-PIPELINE.md](docs/QUANTITATION-PIPELINE.md).

PEAKS ALC and DeepNovo SCORE are separate native scales; length/local-confidence gates also
apply. Shared filter edits prepare the next request, not rewrite stored quantitation.
[DENOVO-ENGINE.md](docs/DENOVO-ENGINE.md) maps readers, inspection and reports. Homology's deliberate
PepExplorer departures are documented in its source; do not silently restore old orchestration.

Evidence fetches retain source/reference provenance and fail without partial application.
Neighbours exclude the same entry/accession. An unannotated protein needs a qualifying reviewed
neighbour in user scope and compatible observed domains, with no conflicting learned marker.
Preserve both conditions. Proposals, source labels and user selections are distinct. Explicit
application checks membership/provenance and stages one undoable change. Recipes replay settings;
SavedFileCheck verifies reopening. Reading a source family is not independent validation of it.
See [SCIENCE-GATE.md](docs/SCIENCE-GATE.md).

## UI, files and deployment

MainWindow delegates to shell/document/presentation/Help components. The File-first menu and
Start/Families/Quantify/Results/De novo rail retain one open bundle. Async completion checks that
project, quantitation and manifest still match. Failure/cancellation retains prior results.
Curation and secondary windows use tested models. Results reads persisted quantitation/studies;
missing studies are not invented. Retired extraction/partition controls are compatibility seams.

Save uses a captured, verified, staged `.fqproj` bundle. Old `.fcf` export does not clear dirty
state. Save/Discard/Cancel protects replacement and closing. SEPro reannotation writes a separate
`*_regrouped.sepr2` and leaves its input intact. Help holds detailed explanations; tooltips stay
short. Colors belong in tokens/palettes, with screen and frozen export palettes kept separate.

Vendored DLLs are opaque externals. PatternTools refreshes must retain its invariant-decimal
parser fix and pass locale checks; installed timestamps do not prove build age. LiteDB remains
for `.xic2`; InvariantGlobalization must remain false because it reconstructs file LCIDs.
[DEPENDENCIES.md](docs/DEPENDENCIES.md) records pins and checks. Nullable is enabled; `.editorconfig`
suppresses CS8603.

USEARCH is embedded under `FamilyQuant.MyResources.resources`, verified and extracted to a
payload-SHA256 filename in the per-user FamilyQuant2 folder. `FAMILYQUANT_USEARCH` overrides it.
Scratch names are unique; process-wide current directory is unchanged. See
[USEARCH-PROVENANCE.md](docs/USEARCH-PROVENANCE.md).

ClickOnce rehearsal needs Visual Studio's .NET Framework MSBuild; `dotnet publish` cannot run
the bootstrapper and can clear existing publish output before failing. Use fresh local
PublishDir/PublishUrl. Only Paulo publishes. Assembly and deployment versions live in separate
files and must agree when he changes them; keep the force-added profile tracked. Tests are not
publishable. Full publishing commands/payload requirements are preserved in history.
