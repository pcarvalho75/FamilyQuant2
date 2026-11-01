# Round 1 paper (Fable 5.1): one strong idea for FamilyQuant

Saved verbatim by the convener from Fable's final message, 2026-10-24.

## 1. Brainstorm

**A. A toxin-class layer with the venom composition figure inside FamilyQuant.** Every venomics paper ends in one figure: the share of the venom per toxin class (SVMP, SVSP, PLA2, CTL, LAAO, CRISP, BPP and so on), with the contaminants and the cellular proteins set apart. FamilyQuant stops one level short of it: it quantifies families (`FamilyQuantFile.cs:259-343`, `FamilyEnvelopeQuant.cs:252-346`) and Ana builds the class figure outside, by hand, from the Layer 1 CSV (`05-context.md`, the 10-24 row on the pie chart). The CSV carries User ID, annotation, locus, mass, one share column and the description (`CsvReport.cs:24-36`), so classifying hundreds of families is spreadsheet work repeated per dataset. A rule dictionary (keyword phrases on the description, identifier rules for decoys and contaminants) assigns a class to every family with its provenance, a class column appears in the grid, and the Analytics screen draws the composition per run with the spread across runs. Helps Ana on every dataset; it is the paper figure. Effort M to L (estimate).

**B. Suggested joins from the memoised identity edges.** The exhaustive pass already aligns every pair (`USearchTools.cs:389-412`) but the edges only serve the single-linkage merge; nothing shows Ana "family 7 and family 258 share a 69.4% edge, one point under your threshold", which is exactly the 08-25 acidic PLA2 case. A "nearest families" panel sorted by best cross-family edge, with one-click join through `BatchCuration.Apply` (`BatchCuration.cs:43-86`), would turn her threshold hunting into a ranked list. The edges are kept in memory only and the TSV is deleted (`USearchTools.cs:458`), so a persisted edge table would be new. Effort M (estimate).

**C. Multi-term Search and phrase-capable Super Cluster.** The two OPEN items. Search is one substring (`FamilyFilter.cs:44-51`); super-cluster keywords are matched against the stop-word-stripped term list (`WordCluster.cs:64-68`, `:88`, `:245-255`), so "ribosomal protein" can never match because "protein" is a stop word (`WordCluster.cs:27`). Effort S (estimate). Real, but polish on its own; it becomes a by-product of A because A needs the same matcher.

**D. One-folder project ingest.** Paulo's 08-17 ask: point at a folder with RAW, PEAKS, SEPro, `.plp` and let the program pair them by `NormalizeRunName`. The 10-23 renamed RAW incident shows the value of pairing up front. Effort L (estimate), and it changes workflow rather than results.

**E. Replicate spread for every family and class.** The native table pools the runs (`FamilyEnvelopeQuant.cs:272-339`), although every deduped peptide carries its run (`NativeQuantFractions.cs:25-27`). A per-run table gives mean and spread across the three technical replicates, which is what a reviewer asks for next to any composition. Effort S to M (estimate). On its own it is a column; inside A it is the error bar on the paper figure.

**F. Evidence-named family labels.** The family row's description is the first target member's description (`FamilyEnvelopeQuant.cs:460-466`). On the persisted pack 22 of the 87 quantified families have an empty description (`family_attribution.csv`, ids 172, 206, 18, 178, 41 and others, totalling about 6.1e9 on the primary basis, my hand sum, about 0.7% of the assigned sum): their first target is a `contaminant_KERATIN` or `contaminant_UBIQUITIN` entry whose header carries no description at all (35 such headers in the `.T-R`). A label by the most-identified member, or by class, would remove the blanks. Effort S (estimate). Folded into A.

**G. Class disagreement as an over-merging detector.** The decoy score sees only target-to-decoy mixing and is blind to a fusion of two unrelated target families (`FamilyQuantFile.cs:566-576`). A family whose members carry two different toxin classes is a target-to-target fusion the score cannot see; the class layer detects it for free. Folded into A.

**H. A replayable curation log.** Record every merge, split, batch edit and lock as accession-keyed operations so a curation survives a database refresh. Effort L (estimate). Attractive but the class rules of A already transfer across databases because they key on words, not on ids, which covers the biggest part of the re-curation cost.

## 2. The recommendation: the toxin-class layer

One idea, A, taking E, F and G inside it and producing C as a by-product. The principle: a class is a label on a family, never a grouping. It merges nothing, moves no User ID, touches no lock, and is recomputed from the rules after every regrouping, so the eight invariants are untouched by construction and the result is reproducible from the saved project. Every class label carries its provenance (rule name and the words it matched, or "user"), an unmatched family is "unclassified" and never silently "cellular protein", and a family whose members match different classes is shown as a conflict.

### P1. The matcher and the dictionary (pure)

**What.** New `FamilyQuant/ToxinClass.cs`: `ClassRule` (name, label, `any` phrases, `all` phrases, `none` phrases, identifier substrings), `ClassDictionary` (ordered rules, JSON, a version string and a SHA-256 of its text), `ClassMatcher.Match(item)` returning every rule that matches a protein, and `ClassAssignment.ForFamily(members)` returning class, provenance (`Rule`, `User`, `Unclassified`, `Conflict`) and the matched words. Plus `FamilyQuant/DefaultToxinClasses.json` embedded like USEARCH is (`MyResources.resx`), a draft dictionary for snake venoms written from the real headers of the Bjararaca `.T-R`: metalloproteinase / metalloprotease / disintegrin (SVMP), serine protease / thrombin-like / "platelet-aggregating proteinase" (SVSP), "phospholipase A2" with `none: inhibitor` (PLA2), "phospholipase A2 inhibitor" (PLI), "C-type lectin" / snaclec / botrocetin (CTL), "L-amino-acid oxidase" / "amine oxidase" (LAAO), CRISP, "bradykinin-potentiating" / natriuretic (BPP/CNP), "vascular endothelial growth factor" (VEGF), "nerve growth factor" (NGF), hyaluronidase, "5'-nucleotidase", "phospholipase B", "glutaminyl-peptide cyclotransferase", "beta-defensin", "antihemorrhagic factor", and two identifier rules, `Reverse_` (decoy, `FamilyFilter.cs:14`) and `contaminant` (`FamilyFilter.cs:20-26`). The dictionary is a user statement that Ana validates; it is not a scientific claim of the program, and the paper must say which dictionary version produced the figure.

**Mechanism.** Tokenize the description before " OS=" exactly as `WordCluster` does (`WordCluster.cs:61-66`) but with no stop-word removal, so a phrase is a token sequence and "ribosomal protein" matches. Case-insensitive, whole-token, `InvariantCulture`. Identifier rules run first and are exclusive (a decoy is a decoy whatever its words say). A protein matching two toxin rules reports both; the family level turns that into `Conflict`.

**Invariants.** Pure code, no grouping, no I/O; invariant 8 by `InvariantCulture` throughout.

**Value.** On the persisted evidence pack, grouping `family_attribution.csv` rows by these phrases by hand gives, on the primary basis (sum of family totals 9.22e11): SVMP 3.60e11 (about 39%), SVSP 2.06e11 (22%), CTL 1.53e11 (17%), LAAO 6.6e10 (7%), CRISP 3.4e10 (3.7%), PLA2 2.7e10 (3.0%), VEGF 1.4e10, 5'-nucleotidase 6.7e9, NGF 4.7e9, PLB 2.8e9, QC 1.9e9; all my estimates by keyword grep, to be regenerated by the package. Two findings the rules surface at once: "Jararafibrase-3 (Fragment)" (family 4, 3.2e10, about 3.5%) matches no rule and would be `Unclassified` for Ana to set, and 22 blank-labelled families become "contaminant" by the identifier rule. Note the on-disk pack predates the current `BuildFamilyCsv` header (`PresentationEvidencePack.cs:576-579`); the reviewer regenerates it from `scientific_results.fqproj`.

**Difficulty** M (estimate). **Blocked by** nothing. **Opens** P2 to P7. **Proof** fixture tests in a new `FamilyQuant.Tests/ToxinClassTests.cs`: phrase match, `all`/`none`, identifier precedence, no stop words ("ribosomal protein" matches, "protein" alone does not match a toxin rule), conflict, JSON round trip with the hash, and the default dictionary classifies every target header of a small fixture copied from the `.T-R`. **Worker** pure-logic, headless, any engine. **Files owned** `ToxinClass.cs`, `DefaultToxinClasses.json`, `MyResources.resx` entry, `ToxinClassTests.cs`.

### P2. Persistence, undo and the batch (pure)

**What.** `FastaItemExtended.UserClass` (string, empty by default, per protein like `UserAnnotation`, `FastaItemExtended.cs:14`); `FamilyClusterFile.ClassDictionaryJson` (string, null in old files, meaning the default dictionary; written by `ToJsonText`, `FamilyQuantFile.cs:462`, so the `.fqproj` curation entry carries it for free, `ProjectStore.cs:576`, `:702`); `GroupingSnapshot` captures `UserClass` for the rows an operation is about to rewrite, exactly as it does annotations (`GroupingSnapshot.cs:31-34`, `:61-67`, `:93-94`); `BatchCuration.Apply` gains a `userClass` argument that follows the annotation branch (`BatchCuration.cs:74-83`) and never locks anything on its own, because a class is not a regrouping (`BatchCuration.cs:64-71` stays as is).

**Mechanism.** The family's class is derived: if any member carries a `UserClass`, that is the class with provenance `User` (two different user classes inside one family is `Conflict`); otherwise the rule result over the members. Per protein storage survives merges and splits without any bookkeeping, and `Restore` puts it back literally.

**Invariants.** 5 holds: `LockedUserIds` is neither read nor written by the class path. Old `.fcf` files load through `FromJsonText` (`FamilyQuantFile.cs:486-520`) with the new properties absent, and the pre-lock migration is untouched. `PushUndoSnapshot()` stays a single method (`CLAUDE.md:68`); the batch path keeps using `PushUndoSnapshotWithAnnotations` (`ClusterGenerationControl.xaml.cs:896-901`), extended to record classes.

**Value.** Ana's own `.fcf` for this database shows how little the hand layer is used today: 584 entries, 302 families at identity 75 with word similarity 0.8 and a keyword super cluster, 2 locks, 1 row where the ids diverge, 0 annotations (`C:/Users/paulo/Desktop/AnaG/...NO-Redundancy.fcf`). The class she needs for the figure has nowhere to live in the file.

**Difficulty** S (estimate). **Blocked by** P1. **Proof** round trip of a file with and without the dictionary, undo of a class batch restores classes and leaves a hand-typed annotation alone, a class-only batch adds no lock. **Worker** pure-logic. **Files owned** `FastaItemExtended.cs`, `FamilyQuantFile.cs` (one property), `GroupingSnapshot.cs`, `BatchCuration.cs`, plus their existing test sections in `Program.cs` (`Program.cs:325`, `:464`, `:468`).

### P3. The grid, the batch box and the dictionary window (WPF)

**What.** In Layer 1: a read-only "Class" column next to User Annotation (`ClusterGenerationControl.xaml:428-429`) showing "SVMP (rule)", "cellular proteins (user)", "unclassified" or "CONFLICT: SVMP | PLA2 inhibitor", with the matched words in the cell tooltip; a "Class" box beside the batch annotation box (`ClusterGenerationControl.xaml:204-216`, handler `ClusterGenerationControl.xaml.cs:959-1036`); a counter line "N families unclassified, M in conflict" refreshed in `UpdateGUI` (`ClusterGenerationControl.xaml.cs:753-771`); a code-only `ClassManagerWindow.cs` modelled on `LockManagerWindow.cs` that lists the rules with a live count of families each one matches, edits them, imports and exports the JSON, and resets to the shipped default. The CSV gains "Class" and "Class provenance" columns (`CsvReport.cs:24-36`, `:58-67`), and `ReportView.Sort` learns the column (`ReportView.cs:116`). Help gets one paragraph (`HelpContent.cs:221`).

**Mechanism.** Classification is recomputed in `UpdateGUI`, so after any recluster, batch, undo or lock change the column is current; cost is proteins times rules, negligible against the TF-IDF pass. Dialogs are guarded on `PresentationSource` as the existing ones are (`ClusterGenerationControl.xaml.cs:324`).

**Invariants.** 5: the column reads, never writes, family ids. 8: CSV via `CsvReport.Field` and `InvariantCulture`; the cell text via the current culture. 7 is not in play (this is not the de novo screen).

**Value.** Ana's two 10-24 asks become one action each: select the unclassified non-contaminant rows (the counter names them), type "cellular proteins", apply; the Viperidae database's thousands of rarely identified proteins get their one slice without hiding anything.

**Difficulty** M (estimate). **Blocked by** P2. **Proof** extend the headless UI sections (`Program.cs:460-485`): the column builds, the batch class applies and undoes, the manager edits a rule and the counter follows, the CSV writes the two columns under pt-BR. **Worker** WPF, on the owner's Windows machine. **Files owned** `ClusterGenerationControl.xaml`, `ClusterGenerationControl.xaml.cs`, `ClassManagerWindow.cs`, `CsvReport.cs`, `ReportView.cs`, `HelpContent.cs`.

### P4. The class composition engine (pure)

**What.** New `FamilyQuant/ClassComposition.cs`: `ClassComposition.Build(rows, attribution, familyClass, policy)` producing per run and pooled, per class: identified envelope, de novo primary estimate, peptide counts, share of assigned signal, share of TIC, plus three non-class rows that are never dropped: "shared across classes", "unattributed" and the TIC remainder.

**Mechanism.** The peptide basis is byte for byte the one the family table uses: `NativeQuantFractions.DedupToPeptides` then `ReferenceEnvelopeEstimator.Apply` (`FamilyEnvelopeQuant.cs:262-263`), with the same cross-side suppression (`:275-278`); the peptide-to-family map is the existing `PeptideFamilyAttribution` (`FamilyEnvelopeQuant.cs:203-225`, built at `:412-468`). For each peptide, the set of classes of its families: one class, credit in full; several, policy `ClassUnique` (default, the class-level twin of `SharedPeptidePolicy.FamilyUnique`, `SharedPeptidePolicy.cs:17`) sends it to "shared across classes", `SplitEqually` divides it. There is no count-in-every-class option: a composition is additive by definition, and the overlapping reading already exists in the family table. Per run by `NativePeptideQuant.Run` (`NativeQuantFractions.cs:27`); pooled by summing runs; the spread is the sample standard deviation of the per-run shares over the n runs present, labelled "spread across the n runs of this project" and never a confidence interval (`SCIENTIFIC_METHOD.md` language). Share of TIC uses `manifest.PerRunTic` looked up by normalized run name (`AnalyticsFigures.cs:142-143`).

**Why this is scientifically stronger than the family figure.** The native family table counts a shared peptide in every family it reaches (`FamilyEnvelopeQuant.cs:236-239`, `:311-314`), so its rows overlap and the honest denominator is the measured total (`:133-138`). Most sharing in a venom is between isoforms of one class, so at the class level the shared peptide becomes class-unique and is credited in full instead of being dropped or duplicated. On this pack the overlap is small anyway: the family identified sum is 7.90e11 against 7.62e11 assigned per run in `signal_by_run.csv`, about 3.7% (my estimate, two CSVs of possibly different bases); on the Viperidae database, where `SharedPeptidePolicy.cs:6-8` records up to 4x overlap, the class level is where a composition first becomes honest.

**Invariants.** 1: runs are keyed by the stored normalized name, never by position. 2: the deduped peptide grain, one representative per run, side, peptidoform and charge (`FamilyEnvelopeQuant.cs:234-237`), never anchors. 3: untouched, the three-way bar is not read. 4: the TIC remainder is derived, displayed, and may be negative. 6: every output row names the dictionary hash, the mapping identity (`FamilyEnvelopeQuantResult.IdentityThreshold`, `:107`), the policy, the basis (primary, M+0 or unadjusted, exported under distinct names as the acceptance criteria require) and n. 8: `InvariantCulture`.

**Difficulty** M (estimate). **Blocked by** P1 only (it takes `familyClass` as a function, so it needs no UI). **Proof** new `FamilyQuant.Tests/ClassCompositionTests.cs`: a two-run fixture with three families in two classes and one cross-class peptide; closure per run (classes + shared + unattributed = measured total, bit for bit); `ClassUnique` versus `SplitEqually`; the SD by hand; with no shared peptide and one class per family the pooled numbers equal `FamilyEnvelopeQuant.Build`'s rows. **Worker** pure-logic. **Files owned** `ClassComposition.cs`, `ClassCompositionTests.cs`.

### P5. The figure, the Analytics screen and the evidence pack (WPF)

**What.** `ClassCompositionChart.cs`, an `OnRender` chart like `FamilyContributionChart`, one horizontal bar per class in identified blue and de novo green with a whisker for the spread across runs, the "other classes" fold after top N as `FamilyContributionPlot.Fold` does (`FamilyContributionChart.cs:89-140`) so nothing is dropped, and "shared across classes", "unattributed" and "contaminant" drawn apart from the toxin classes. Heading: dictionary version, mapping identity, policy, denominator, n runs. Added to the one figure list (`AnalyticsFigures.cs:48-51`, `:73-82`) as `class_composition.png` so screen and "Save figures" cannot drift; the evidence pack writes `class_composition.csv` (per run and pooled) through the existing `Header`/`Row`/`Num` helpers (`PresentationEvidencePack.cs:729-731`) beside `family_attribution.csv` (`:178`), and `family_attribution.csv` gains `class` and `class_provenance` columns. The pie Ana wants is drawn from that CSV in her own tool; a pie with fifteen slices is not a figure this program should sign.

**Mechanism.** `AnalyticsControl` already builds the family table from the open curation (`AnalyticsControl.xaml.cs:108`, `:129-142`); the composition is built beside it from the same attribution.

**Invariants.** 6: the figure prints what it stands on; a project with no curation draws the chart's own empty state, as the family chart does (`AnalyticsFigures.cs:55-58`). Colours: the blue/green pair of `ChartPalette`, no new categorical ramp, so the CIELAB rule for adjacent segments (`CLAUDE.md:138`) is not even exercised.

**Difficulty** M (estimate). **Blocked by** P4 and P2. **Proof** the chart renders headless, populated and empty, in both palette variants, and the PNG writer produces it (sections at `Program.cs:412-432`); the CSV round trip under pt-BR. **Worker** WPF, on the owner's machine. **Files owned** `ClassCompositionChart.cs`, `AnalyticsFigures.cs`, `AnalyticsControl.xaml.cs`, `PresentationEvidencePack.cs`.

### P6. The real-data check and the review

**What.** One real-data section (skips without data) that opens `BrProt_Talk/scientific_results.fqproj` with the default dictionary and asserts arithmetic identities only: closure per run, pooled equals the sum of runs, the SVMP class total equals the sum of its families' rows minus the cross-class drop, every blank-description family is classified by an identifier rule, and the 11-residue BPP family lands in BPP/CNP. It prints the table for Ana; no biological number is pinned until she has validated the dictionary (invariant 6). Registration lines in `Program.cs` for the new test files. A reviewer from another model family reads P1's dictionary against the `.T-R` headers and P4's arithmetic against `FamilyEnvelopeQuant.Build`.

**Difficulty** M (estimate). **Blocked by** P1 to P5. **Worker** reviewer, review-gate style. **Files owned** `Program.cs` registration lines only.

### P7. The same matcher behind Search and Super Cluster (closes the two OPEN items)

**What.** `FamilyFilter.Apply` accepts comma-separated terms as OR (`FamilyFilter.cs:44-51`); `GroupByKeywordsCounted` matches phrases against the un-stop-worded tokens from P1 instead of the stripped term list (`WordCluster.cs:245-255`), so "ribosomal subunit" OR "ribosomal protein" works. `ScopeItems` (`WordCluster.cs:190-200`) inherits the multi-term search for free.

**Invariants.** 5: locked families are still removed after matching (`WordCluster.cs:265-270`). **Difficulty** S (estimate). **Blocked by** P1. **Proof** extend `SuperClusterMatchAndExcludeTests` (`Program.cs:973`) and `SearchScopeReclusterTests` (`:1063`). **Worker** pure-logic. **Files owned** `FamilyFilter.cs`, `WordCluster.cs`.

Order: P1, then P2 and P4 and P7 in parallel, then P3 and P5, then P6. My estimate for the whole: six to nine worker-days.

## 3. Why this one

It meets all three of the brief's tests at once. The class composition with its spread across runs is a result the users cannot get from the program today and it is the figure of the paper. The classification of hundreds to thousands of families moves from a spreadsheet to a rule dictionary that is saved in the project and transfers to the next database because it keys on words, not ids. And three silent wrong results become impossible: a composition built from overlapping family rows, a blank label standing for 22 contaminant families, and a target-to-target fusion across toxin classes that the decoy score cannot see (`FamilyQuantFile.cs:566-576`). The suggested-joins panel (B) cuts hand work but still asks Ana to decide every pair; the one-folder ingest (D) is a bigger rewrite for a convenience; the replicate spread (E) alone is a column. A gives E its figure, F its labels and G its detector, and closes C on the way.

## 4. Rejected ideas and traps

**Rejected (one line each).**
- B, suggested joins: strongest runner-up; it opens naturally after A, when the class conflict list tells which joins matter.
- D, one-folder ingest: workflow, not results; revisit after the title-bar version fix.
- H, replayable curation log: the dictionary transfers the part that matters; the rest waits for a second database refresh to justify it.
- A sequence-based classifier by homology to a shipped reference set: better than words for "uncharacterized protein", but it is a new scientific claim needing calibration, and the keyword dictionary already covers this database's vocabulary.
- A pie chart drawn by the program: fifteen slices and adjacent-colour contrast make it a figure the suite cannot sign; the CSV serves it.

**Traps.**
- Shipping "cellular proteins" as a rule: absence of a toxin keyword is not evidence of a cellular protein; the program says "unclassified" and only Ana writes "cellular proteins".
- Letting a class become a grouping: the moment a class merges families it meets locks, undo and reclustering; keep it a label.
- Mixing bases: the family table's count-in-every-family rows must not be summed into classes; P4 goes back to the peptides.
- Quoting the hand sums above as results: they are my keyword greps on an August pack whose header predates the current writer; regenerate from the `.fqproj`.
- Rules with bare common words ("venom", "snake", "fragment") would match everything; the fixture must include a decoy of each.
- Per-run shares with a run that paired with no RAW: the composition reports the runs it has and names the missing one, as `FullXicComputer` does (`CLAUDE.md:144`).
- Forgetting the pubxml version bump when this ships (`CLAUDE.md:24`).
