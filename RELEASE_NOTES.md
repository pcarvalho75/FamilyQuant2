# FamilyQuant release notes

This file preserves historical release notes, including their original pending-publish labels. It is not the current
checkout release status. Current work and release gates are in [the roadmap](docs/ROADMAP.md), with build/test commands
in [the catalog](docs/INDEX.md). No historical version heading is changed by this note.

## 2.1.4 (release candidate, not published)

- **Database entries in the dot plot.** The interactive report's dot plot can
  show each family's database entries on X, Y or Size. When the project has these
  counts, it opens with database entries against mean ion current share.
- **Existing projects.** Use **File > Export report** to show the new axis in an
  existing project's interactive report, with no Compute needed. Stored and
  printed numbers are unchanged.

## 2.1.3 (release candidate, not published)

- **A Windows installer.** Install FamilyQuant for your Windows user account,
  without administrator access. The installer adds a Start menu shortcut; remove
  it through Windows Apps & features. Upgrades replace the program in place while
  keeping your projects and application data.
- **An unsigned installer.** Windows may show "Windows protected your PC".
  Choose **More info > Run anyway** to continue with the installer.
- **Updates when you start.** Each start checks for a newer version in the
  background. A non-blocking notice offers one click to install it. You can also
  use **Help > Install update**, or turn start-up checks off in settings.
- **The zip is still available.** People who prefer to extract and run the
  portable package can continue to use the zip download.

## 2.1.2 (release candidate, not published)

- **An interactive HTML report.** File > Export report now writes `report.html`
  beside the Word report and its supplement, from the same stored results. Explore
  composition and subclasses, compare runs, and open family cards for coverage,
  credited signal and the Database gap. Pop-ups show exact values.
- **Offline viewing.** Open the self-contained page offline in any modern browser.
  It includes light and dark themes, phone layouts and a print view. The Word
  report is unchanged.
- **Existing projects.** A project computed with 2.1.1 gets the page on its
  next Export report, with no Compute needed.

## 2.1.1 (release candidate, not published)

The program, report and deployment profile identify this version as 2.1.1.0.
This candidate awaits the release gate and packaged report export.

- **Faster Compute.** A new search engine reduced Compute from about 600 seconds
  to about 80 seconds on the same full-run project and machine, with lower peak
  memory use. The search change preserved the stored scientific results byte for
  byte; timings on other projects depend on their inputs and machine.
- **One headline total per panel family.** When your panel groups several families,
  the headline and composition figures now use their combined panel-family total.
  A peptide shared within that panel family is credited once. The supplement keeps
  the original family-level results as well.
- **More precise number checks.** The report's number check uses the family, run or
  option named beside a number to find its source cell. If several unrelated cells
  have the same value and the subject cannot resolve them, it reports an ambiguous
  match rather than citing the first equal cell.
- **Homology science options.** Score tier is on by default: it credits a de novo
  peptide only to proteins tied at its best score. Some family shares can therefore
  move slightly after Compute. The per-length and tag gates are available but off
  by default.
- **More release checks.** Additional automated checks now run before release,
  including family-panel behavior, analysis options and report exports.

**Projects computed with 2.1.0.** Opening a saved project keeps its stored results
and report values until you run Compute; opening alone does not apply the new
best-score rule. When you compute in 2.1.1, de novo family credit uses the best
protein-score tier and a separate cache entry, while identified-peptide rules stay
the same.

## 2.1.0 (release candidate, not published)

This candidate includes the accepted family analysis and reporting work for 2.1.0.
Publication waits for the full release gate and the final project export. The program,
report and deployment profile identify this version as 2.1.0.0.

- **Your own family panel.** Import a spreadsheet by choosing its columns and mapping each
  decision to Quantify, Report under others, Not a family or Merge into another family.
  Edit the panel, save a reusable spreadsheet template, and keep names, domain lists,
  subclass rules, literature text, notes and sources in the project. Pfam checking lists
  accession/name mismatches and reports when an online check is unavailable.
- **Families by evidence, with confirmation.** Source families and domain combinations
  are linked to your panel from reviewed evidence. Ambiguous links are shown for you to
  confirm once and save with an explanatory note. Accompanying domains distinguish shared
  domains. Proteins without qualifying evidence remain outside the panel families. Apply
  is one undoable change; the audit records each protein's evidence and reason.
- **Subclasses and unresolved signal.** Define subclasses with required and absent domains
  or source subfamily text. Eligible reviewed-reference evidence can carry a subclass to
  another protein. Peptides reach the deepest level they distinguish; unresolved signal
  stays labelled within its family.
- **Run statistics and panel-family totals.** Family shares are available for each run,
  with mean, standard deviation and coefficient of variation. Protein counts show a
  labelled greedy database-protein estimate and support from proteins' own peptides.
  Panel-family briefs and exports use stored totals for effective panel families, including
  merged groups; candidate keys are combined before shared-peptide credit. The existing
  headline and source-group studies retain their stated units. Abundance and database-protein
  diversity stay separate, with the estimate and denominator printed beside the share.
- **Composition and coverage figures.** Family and subclass charts, a family/subclass ring,
  abundance against protein diversity, signal-source comparisons and protein coverage maps
  use stored results. Captions explain the figures and include values from those results.
  Large figure exports are bounded or paged, with detail data retained when a figure is capped.
- **A Word report and its supplement.** File > Export report lets you choose sections and
  figures and saves those choices. The companion `<report>_supplement.xlsx` retains detailed
  protein, peptide, run and audit tables, plus printed inputs and settings. Long list values
  move to item sheets linked to their original row, while the original cell gives the item
  count and sheet reference. Word tables fit their headers and keep rows and their
  captions together. Native table cells and scientific values are retained.
- **A program-written Summary of findings.** Fixed templates report values, counts, ranks,
  ranges and available comparisons from stored studies. Statements appear only when their
  data exist. The supplement holds the numbers used by the summary. The program supplies
  this section without an AI service.
- **Saved report details and methods.** Title, authors, sample description, workflow,
  database kind and written interpretation are saved with the project. Input names, hashes
  and recorded search settings supply the methods. The report prints the running version.
  A saved reporting threshold, initially 0.1%, drives the same Others membership in tables
  and family briefs; the detailed list remains in the supplement.
- **Numbers that can be traced.** A command checks numeric text in the Word file against
  companion CSV/workbook cells and lists unmatched values with their context. Family-rule
  leave-one-out measurements and reasoned differences from a prior grouping can also be
  exported. File > Export > Interpretation input (Markdown) writes report details, the
  panel, stored results and methods as one plain file for an assistant of your choice.
- **Optional AI drafts under your control.** Help > AI assistant key lets you add, test,
  replace or remove your Anthropic key, protected for your Windows user. You can import a
  key file beside a project. Draft interpretation with AI shows the input before a request,
  checks returned numbers and requires your review before adding a labelled draft to Report
  details. Its preview matches the Interpretation input export, and number checks read
  the exported table cells. The key is kept outside saved projects and reports.
- **Progress and reusable search results.** Compute and project opening show counted phases,
  live elapsed clocks and cancellation, with timestamped operation logs. Report export
  names the current section, figure or sheet and its counts. Matching stored inputs and
  search settings allow attribution and source census results to be reused. Compute also
  reuses its current attribution for the family-world pass instead of repeating the search.
  Online evidence requests are batched for larger reference sets, retaining provenance.
- **Saving larger projects.** Curation JSON is streamed through saving and saved-file
  checks, reducing memory use while retaining its existing format.
- **Fixes found during real-data work.** Compute caches and spectrum exports use a per-user
  folder. The de novo picker recognizes files in the selected folder. Family-world quality
  rows explain when credited signal is too small for a rate. New report/audit protein names
  omit the header's organism tail. Export becomes available when attribution finishes;
  report-thread handling and diagnostic messages cover full-project exports. A completed
  study shows zero with "no peptide observed" when no peptide reaches a family; an unavailable
  result retains its reason. Help
  explains the panel-to-report workflow, scope phrases, progress, cached reopening and link notes.

## 2.0 development update (not published)

FamilyQuant 2.0 continues as a separate installation beside 1.5. Release acceptance remains
with the roadmap's science, preservation and deployment gates; these notes do not announce
a published final release.

- **A separated engine and desktop application.** Clustering, evidence, quantitation, homology,
  de novo readers and project formats live behind headless, cancellable operation boundaries.
  Existing file and override seams remain available.
- **Rebuilt family work.** Curation and family inspection retain editable IDs, locks, grouping,
  annotations, copy/export and undo through tested view models and operation services.
  The File-first shell, shared busy handling and saved-file checks keep one project bundle.
- **Quantitation stays explicit.** Source-native PEAKS/DeepNovo filters remain distinct; the
  primary envelope estimate, M+0 sensitivity and unadjusted diagnostics retain separate labels.
  Shared-family membership and attribution provenance survive saving and export.
- **Evidence stays reviewable.** Scope, direct source labels, reviewed-neighbour measurements,
  domain compatibility, flags and user choices remain separate. An unannotated assignment
  requires a qualifying in-scope reviewed neighbour and compatible observed domains.
- **Compatibility and dependencies.** Old projects remain readable, old-compatible FCF export
  is retained, unused JSON/theme packages were removed, and the bundled USEARCH source/hash
  is recorded with real-data parity evidence.
- **Measured clustering diagnostics.** D14 preserves existing clustering defaults and avoids
  automatic merging from shared peptides or an automatic reviewed-family veto. The diagnostic
  records the counterfactual measurements. D15 uses one exact contaminant-prefix rule, with
  one exact decoy unwrap. See [the measured candidates](docs/IMPROVEMENTS.md).

The family-world work remains subject to its own gate and result-stage acceptance. A green
build, a stored-project golden check or declaration receipts alone do not establish fresh
RAW reproduction, universal accuracy or completion of the feature ledger. The protected
presentation and its frozen producers retain their separate acceptance boundary.

## 2.0 preview 1 (pending publish)

FamilyQuant 2.0 is a new program that installs beside 1.5, with its own install address and data folder. 1.5 stays
installed and untouched, and 2.0 opens its files.

- **Group by evidence.** Each protein is placed in a family from UniProt annotation, its nearest reviewed protein and
  its domains, never from the words of its name. Each one gets a family or none, with a tier and a reason, and the
  recipe is stored so the result can be reproduced. Against the 224 reviewed venom metalloproteinases on the Viperidae
  file, the evidence route took 224 and no other reviewed protein; the words route took 213 and 12 others.
- **A review window.** Families with ticks, proteins with reasons and flags, domain-architecture splits, motifs and a
  left-out list. Nothing changes until Create, and one Undo reverses it.
- **Checks you can read.** Leave-one-out validation, a check of the file after every save, and audit and left-out CSV
  files from the program.
- **A new look.** Dark by default, light in View. File menu first, a navigation rail, one quiet status bar, and rebuilt
  Build, Quantify, Results, De novo and secondary screens. New logo.
- **One Save.** `.fqproj` is the file. `.fcf` still opens and stays as an export.
- **Retired from the De novo screen:** Extract areas and Partition ion current. 1.5 keeps them. The homology CSV moved
  to Results.

Not in this preview: the rebuilt curation screen and family view, an `.fcf` export that 1.5 can open, and the proof that
every function of 1.5 is kept. Those arrive before the final release.

## 1.5.0.0 (pending publish)

This release makes PEAKS de novo-only and DeepNovo first-class, source-aware evidence and makes the
venom-family analysis reproducible from a saved FamilyQuant project plus its generated evidence pack.

- **PEAKS and DeepNovo keep their own score semantics.** PEAKS is filtered by `ALC (%)`; DeepNovo is
  filtered by `SCORE (%)`. Each caller must also meet the configured peptide-length and consecutive
  local-confidence rules. The two score scales are stored, displayed, and plotted separately; an equal
  numeric value is not treated as equal confidence or as a calibrated de novo FDR.
- **One precursor-level evidence table.** After full XIC, FamilyQuant writes and can display a table with
  acquisition file, MS2 scan, PEAKS sequence/score, DeepNovo sequence/score, PatternLab sequence/XCorr,
  quantitation, clean sequences, charge/m/z, RAW diagnostics, candidate counts, filter status, family,
  TIC, credit state, and join diagnostics. Annotated sequences remain intact for audit while PTM-cleaned
  residue sequences drive the explicit equality fields. Many-to-many family mapping remains explicit:
  legacy `FamilyUserId` is populated only for exactly one family, `FamilyUserIds` stores every ID as a
  semicolon-delimited list, and `FamilyAssignments`/legacy `FamilyAssignment` carry every full label.
  FamilyQuant never chooses an arbitrary first family. `FamilyMappingMinimumIdentity` records the
  homology threshold used for the row, while `FamilyMappingStatus` distinguishes mapping-not-run,
  unusable-curation, mapped, and run-but-unattributed rows; an empty family list is therefore never
  interpreted without its mapping status.
- **Same scan no longer means same precursor by assumption.** Comparisons start with normalized file and
  scan, then require equal source-reported charge and a non-chaining 20-ppm precursor-m/z cluster. A scan
  can therefore carry several incompatible precursor hypotheses without creating a false sequence
  disagreement. RAW-selected charge and m/z are retained as diagnostics rather than silently replacing
  a caller's reported hypothesis.
- **Deterministic, source-aware union.** Compatible PEAKS/DeepNovo observations receive one XIC. Caller
  priority chooses the primary sequence without comparing unlike score scales, while every source claim
  remains available for Venn, family-source, and audit exports.
- **Program-native paper/talk evidence.** The evidence pack now includes exact plot CSVs and 1920x1080
  PNGs for the source-filter funnels and Venn regions, accepted-call score agreement, family source
  sequence counts, database-search concordance, identification consolidation, and assigned-signal
  diagnostics. The same studies are persisted in the `.fqproj` and can be reopened through the UI.
  The spectrum comparison table is the master table for scan-, source-, and precursor-quant plots;
  identity, reference-model, and family-sensitivity figures use their own dedicated exported tables.
  The saved project and evidence-pack exporter regenerate the complete figure set, no single CSV is
  claimed to contain every analysis.
- **Scientifically explicit assigned signal.** Observed search-engine envelope and the de novo
  identified-reference envelope estimate are reported separately. The de novo term is a mass-bin model
  with cross-validation and sensitivity diagnostics, not a directly observed full envelope, calibrated
  identification accuracy, or confidence interval. Unadjusted isotope-window totals remain diagnostic.
- **Scalable project persistence.** Project format 2 stores the large spectrum-comparison table in its
  own compressed streaming entry instead of embedding hundreds of megabytes of JSON in `manifest.json`.
  Version-1 projects remain readable.
- **Release hardening.** Missing RAW trailer charge metadata no longer prevents available precursor-m/z
  diagnostics from loading; accepted-call charts derive their axes from the configured/data range; and
  filter membership includes the local-confidence evidence that actually determines acceptance.
- **Compute refuses a RAW folder that matches no run.** Runs are paired by file name, so an acquisition
  renamed on the way (for example `..._01-003.raw` for run `..._01`) paired with nothing, and Compute
  used to report success with zero rows, replacing the project's quantitation. It now stops before
  opening any file, keeps the existing quantitation, lists the `.raw` names and the run names of the
  identifications and de novo calls, and suggests the rename. When only some runs pair, the computation
  proceeds and the status names every run and RAW it left out.
- **Divide and Conquer and the identity scan name the families that mix target and decoy.** With the
  exhaustive clustering, one short peptide can hold the floor up on its own: on the Bothrops jararaca
  database the search answers 73%, and the only family mixing at 72% is the 11-residue
  bradykinin-potentiating peptides, which are nearly palindromic and align to their own reversed decoys.
  The answer now says so, and the scan report lists the mixed families at every identity, each with its
  length.
- **Batch edit of selected rows.** "Apply to selected rows" in Layer 1 gives every selected row one User
  ID and/or one annotation at once (Ctrl+click, Shift+click or Ctrl+A to select), so joining proteins,
  splitting some out of a family, or gathering the cellular proteins into one family labelled "cellular
  proteins" is a single action. It locks the families it changes like a hand edit, and one Undo reverses
  it, annotations included.
- **Several terms in Search, phrases in Super Cluster.** The Search box takes comma-separated
  alternatives: "metalloprotease, SVMP" shows the rows carrying either (it used to look for that whole
  text and list nothing), and a scoped recluster follows every term. A Super Cluster keyword in quotes is
  a phrase matched as consecutive words of the description: "ribosomal subunit", "ribosomal protein"
  with "match any word" gathers either, and a common word such as "protein" counts inside a phrase.
  Unquoted keywords and a single search term behave exactly as before.
- **Create Super Cluster shows what will happen first, protein by protein.** A preview opens and nothing changes
  until you press Create. Keywords find the families; every protein of those families is listed with a tick, its
  Locus, its description as the database has it, its domains, its family, and how many of that family carry a
  keyword. You no longer have to guess how a protein is named: the ADAM proteins are called "Disintegrin and
  metalloproteinase domain-containing protein", so the word ADAM finds nothing, and the preview says so and they
  are there on the list. Keywords, exclude words and the match choice can be edited there, and everything follows
  as you type. If the Search box hides families that match, one tick brings them in, and families it shows without
  a keyword are counted. What you settled stays in the boxes, Undo reverses it in one step, and the message that
  used to follow the merge is gone because the preview already said it.
- **A protein can stay behind without its family.** A protein with none of your words is in red, first on the list:
  it came with another protein of its family, as a venom disintegrin comes with a metalloproteinase. Untick it and
  it stays in its family while the rest joins. "Untick the red ones", "Tick all", "Untick selected" and "Tick
  selected" act on many at once. The exclude words work the same way now: they keep out the proteins carrying the
  word, not their whole family, which on a Viperidae database raised the SVMPs kept from 644 to 655 of 657. Part of a
  family that joins moves to a new family number, and what stays keeps the old one. That new family is locked, so a
  Recluster by Words cannot put back what you left out, and a note warns when an annotation stays behind on a family
  that gave only part.
- **Pfam domains.** Fetch Domains asks UniProt once for each protein's Pfam domains and saving keeps them in the
  file with the UniProt release, so the answer does not change afterwards and needs no internet. They fill a new
  Domains column, the Search box finds them, and Keep domains in the preview unticks the proteins with none of the
  domains you name, whatever those proteins are called. On a Viperidae database of 31,777 entries the fetch took
  85 seconds. Entries UniProt no longer has are counted and left as they were. A protein with no domains stays ticked if
  it carries a keyword, because the short disintegrins and many
  fragments are too short for Pfam. Domains do not separate everything: most ADAMs have the same ones as a P-III SVMP.
- **Copy from the tables.** In the preview and the Data Browser a right-click copies the UniProt code (P31982
  from sp|P31982|VM2I_CROCC) or the description, or opens the UniProt page. Every selected row gets one line,
  an empty one where there is nothing to copy, so a paste beside another column stays aligned. Ctrl+C copies
  the column you clicked instead of the whole row, or the whole row after a click on a row number, and now says
  so when another program is holding the clipboard.
- **The Exclude Words boxes accept commas.** Both boxes split on spaces only, so "inhibitor, matrix, ATP"
  reached the matcher as "inhibitor," and "matrix," and only the last word excluded anything, with no
  warning: a Super Cluster then gathered the very families it was told to leave out. Commas and semicolons
  now separate words, as they do in the keyword box. In the Super Cluster boxes a word may also be hyphenated
  or a quoted phrase ("domain-containing"), punctuation around a word no longer hides it ("(Fragment)" counts
  as fragment), and an exclude word such as "protein" counts. Matching stays on whole words of the description
  before OS=, because "adam" as a fragment would also remove adamalysin, a venom metalloproteinase.
- **Families are named by your annotation, or by their most common protein name.** The name a family
  carries in the Full XIC composition CSV, the Analytics chart, the evidence pack, the spectrum table, the
  de novo family table and the lock list used to be the description of its first protein. It is now
  its User Annotation when you typed one (so "cellular proteins" reaches the composition), otherwise the
  protein name most of its members share ("OS=" and "(Fragment)" ignored).
- **Short tooltips that wrap.** Every tooltip is rewritten to one or two plain sentences (about a third of
  the old text), and a long one wraps at a readable width instead of running off the screen. The full
  explanations stay in Help.
- **The title bar shows the installed version.** It showed the build's assembly version (1.5.0.0) on an
  installed 1.5.0.2; it now shows the ClickOnce version that was deployed, all four parts, and so do the
  project manifest and the Compute window.
- **Compute leads with the share of the TIC explained.** The summary after Compute / update full XIC now
  starts with, per run, the share of the TIC the identified peptides explain (plus the de novo estimate
  and the total when de novo was included). The resolved isotope-series diagnostic (about 60% on the
  jararaca runs) used to be the only percentage on screen and was read as the share explained; it is now
  labelled as signal that looks like peptide isotope series, identified or not.
- **The open project stays open on every screen.** After a Compute started from the Home card, Save project
  was disabled and every way back to the curation screen reopened the file, discarding the computed
  quantitation. The open project is now kept, and saveable, on the Home, Analytics and De novo screens; the
  Home "Curate families" card returns to it. Run USearch, which starts a different database, still closes it.
- **The full-XIC numbers in the curation table.** A "Full XIC share" column shows each family's share of
  the project's own quantitation and follows every regrouping, User ID edit, batch edit and Undo at once.
  Layer 3's options now work without a PatternLab .plp: "Shared peptides" applies to this column, and a new
  "Full XIC share of" chooses the quantified signal or the TIC of the runs. With "Family-unique only", the
  peptides shared between families are their own amount (as the users asked), shown in the summary line and
  in a new "Export family composition (CSV)" whose rows add up to 1 (one row per family, the shared row and
  the unattributed row): the table a pie chart is drawn from. The evidence pack writes the same table as
  family_composition.csv, Analytics names the shared share, the table CSV gains the column, and "Show only
  XIC > 0" counts either quantitation. The value is the primary estimate (identified observed envelope plus
  the de novo identified-reference, mass-calibrated estimate) and is labelled so.
- **Family attribution reaches every qualifying protein.** A peptide is attributed to every family of every
  target protein that passes the homology identity gate, instead of the families of the mapper's top three
  hits only: with several equally good or near-equal proteins in different families, the cut could drop a
  family and call a shared peptide family-unique. This moves the per-family numbers of Analytics, the
  spectrum table and the evidence pack on redundant databases; figures made with an earlier version should
  be regenerated before they are compared.
- **An identified peptide counts only for the families whose proteins contain it.** The rule above applied
  the de novo homology search to identified peptides as well, so a peptide the database search had
  identified could become shared through a protein that merely resembles it (80% identity or more) in
  another family. An identified peptide now reaches only the proteins tied at its best homology score,
  which are exactly the proteins containing it (isoleucine and leucine treated as one), the way PatternLab
  maps a peptide to its proteins on the .plp route. A de novo sequence, which has no protein of its own, still
  reaches every protein past the identity gate. On the Bothrops jararaca curation this turns 178 of 4,720
  identified sequences from shared to unique (40 more stay shared in fewer families), shrinks the shared
  bucket of the composition from 1.39% to 0.18% of the quantified signal, and moves the most-changed
  family by 0.82 points (serine proteases 16.82% to 17.64%). A spectrum table stored by an earlier version keeps
  its family columns and says so until Compute runs again. The comparison is reproducible with the
  `--attribution-rule-study` lane of the test runner.
- **The spectrum table's families follow regroupings.** Its family columns, built at Compute, are re-joined
  onto the current family numbers before a Save, the spectrum view and Analytics, but only when the recorded
  mapping provenance (protein set, identity gate, method) still matches. A project from an earlier version,
  or mapped against another database, keeps its columns as computed and says so in each row; a refresh that
  fails marks them unavailable rather than leaving them looking current.
- **A clearer workflow.** Layer 2 is labelled optional ("OPTIONAL · no .plp loaded", grey) instead of a red
  "BLOCKED"; the Home de novo card says it is for inspecting the de novo evidence, since Compute reads the de
  novo folder itself; the homology search after Compute or on opening a project runs in the background.

The legacy `.sepr2` security boundary is unchanged: the external SEPro reader uses `BinaryFormatter`, so
only trusted local `.sepr2` packages should be opened.

## 1.4.0.0 (pending publish)

The isotope-partition interpretations below document the 1.4 implementation history. They are not the
primary scientific claims of 1.5; use the source-aware evidence and model-qualified language above for
new analyses and presentations.

This is the release of the whole Q-series: FamilyQuant now measures its own quantitation straight from the
RAW acquisitions, keeps it in a project of its own, reports the recovery, and ties the screens into one
task flow.

- **FamilyQuant computes its own full-envelope XIC.** File - Compute / update full XIC re-extracts every
  identified peptide, and any kept de novo precursor, from the RAW files with the full isotope envelope,
  one acquisition at a time and off the main window. Nothing is read from a captured PatternLab value any
  more: the numbers are measured here.
- **Two settable quantitation rules, on the Compute window.** *Isotopes to credit* is a ceiling, now six
  (M+0 to M+5): each isotope is looked for at its own m/z and an isotope that is not there is simply not
  credited, so raising the ceiling only adds signal that is really present and nothing is filled in for
  what is absent. The isotopes-credited graph reports how many were found; on run _01, of 12,539 peptides,
  1,427 were credited one isotope and 2,001 reached all six. *Minimum points per peak* is how many points
  a chromatographic peak must carry before it is quantified at all, now three, and it is applied to every
  peak separately, isotope by isotope, so a thin M+1 cannot slip a badly measured number into an envelope
  whose M+0 is sound. Set it to 1 to reproduce the legacy rule exactly, which credits a single-point peak
  its raw height. Both settings are written into the project, so a stored quantitation always says which
  rule produced it.
- **Identified but not quantified: its own number, not part of the remainder.** A peak the minimum-points
  rule refuses is no longer simply absent. Its ion current is measured in the same units as everything
  else (a peak with two or more points already has an area; a single-point peak has no width, so its area
  is estimated from the run's median MS1 scan spacing and is labelled an estimate wherever it appears) and
  it is carried through as a per-run quantity, stored in the project, drawn as its own labelled block on
  the recovery bar, and given its own columns in the analytics CSV. So the recovery decomposition still
  accounts for the whole ion current with nothing migrating silently into "unidentified". On run _01
  nothing is refused at the three-point minimum, and the figure says so rather than leaving it unsaid.
- **The recovery bar, grouped for a talk.** The top bar now reads as four blocks that total 100%:
  quantified, identified but not quantified, peptide-like but unidentified, and not peptide-like. The
  identified / de novo split of what was quantified is still in the analytics CSV, in the per-family
  chart, and in the same figure's lower panel.
- **The detected peptide-like signal is now reported by charge, and the claim is graded honestly.** At
  charge 1, "this feature has a carbon-13 isotope" is true of essentially any organic molecule, so a
  singly charged two-isotope feature is weak evidence that it is a peptide; at charge 2 or more the
  half-dalton or third-of-a-dalton isotope spacing is something a singly charged small molecule cannot
  produce, so it is strong evidence. Reported merged, the two overstate the claim. The detected level is
  therefore measured and stored as two parts, with the whole per-charge breakdown beside it: the recovery
  figure draws the detected bar as multiply charged then singly charged and names both in its legend, and
  the analytics CSV gains a column for each. The total is unchanged and is exactly the sum of the two
  parts. The singly charged part is NOT called non-peptide, and never will be: in venom the natural
  peptides, the bradykinin-potentiating peptides and the angiotensin-like fragments among them, are
  frequently singly charged, and this dataset contains identified charge-1 peptides. On run _01 the
  detected level is 60.90% of the total ion current, of which 57.70% is multiply charged and 3.20% is
  singly charged.
- **Saved figures come out on a light background, tuned for a projector.** Every figure written by Save
  figures (and by the identity sweep's own PNG button) now uses a light palette instead of the screen's
  dark one, because a slide is projected onto white. It is not the screen colours with the background
  flipped: the screen's pale blue and mint green hold barely two to one against white and disappear in a
  lit room, so each series is re-drawn in a darker, more saturated version of the same colour, with
  near-black text and a light grid. Blue stays blue, green stays green, amber stays amber. Nothing on
  screen changes.
- **The family chart states the identity it stands on.** Its heading now names the homology identity
  threshold the attribution was made at, because the de novo census can be run at a different identity and
  a figure that does not name its threshold can disagree with the census beside it without either looking
  wrong.
- **A project of its own: the .fqproj.** A FamilyQuant project (.fqproj) holds the families, your User
  IDs and annotations, the computed per-peptide quantitation, and the parameters it was computed with, in
  one file. Save project writes it; opening it restores both the curation and the quantitation. Old
  Family Cluster Files (.fcf) still open exactly as before, with the quantitation empty until you compute
  it, so nothing you already have is left behind.
- **The Analytics screen: the recovery figures for the paper and the talk.** Analytics reads the open
  project's stored quantitation and draws four figures with a CSV export: the XIC by mass, how many
  isotopes were credited per peptide, the percent of ion current by mass, and the recovery of the total
  ion current split into what was quantified, what was identified but not quantified, what else is
  peptide-like, and the rest. It reads only; it never reopens a RAW. Measured on run _01: the identified
  envelope explains 27.84% of the total ion current, the de novo envelope adds 22.82%, and together they
  recover 50.67%; the monoisotopic identified figure is 11.89%, and it does not move with the isotope
  ceiling.
- **One home, one flow.** The program now opens on a task-oriented home screen that lays out the whole
  flow as four numbered steps (build families, curate families, compute full XIC, analytics) plus an
  optional de novo side-branch, each a card that opens the same command as the menu. The menu bar is
  reordered to match and gains a Home item, and the banner under it is a persistent step indicator that
  always names the current stage and the open .fqproj project.
- **The de novo screen, folded into the flow.** Because de novo quantitation now flows through Compute
  full XIC and Analytics, the de novo screen leads with its two core steps, Import and filter and
  Reconcile and review (the manual review queue is the one thing only it produces). The extract, attribute
  and partition computations are the same ones the main screens now run, so they are collapsed into an
  optional "Standalone run (advanced)" section kept for a de-novo-only run that has no project.

## 1.3.0.0 (pending publish)

Three additions to the family-clustering workspace (Layer 1), for curating a database by hand:

- **Undo.** An Undo button now steps back through the clustering and the edits you make, one at a time,
  without reloading the file. It reverses a Recluster by Words, a Create Super Cluster, a User ID number
  you changed, or a lock you removed, restoring the family numbers and the locks exactly as they were
  just before. It remembers the last 50 steps of the current session, does not survive closing the file,
  and there is no redo. The button is greyed out until there is a step to take back.
- **Super Cluster: match all words or any word, and exclude words.** Create Super Cluster now lets you
  choose whether a family must contain ALL of your keywords or just ANY one of them before it joins the
  cluster; the default is "match all words", so a multi-word phrase such as "acidic phospholipase" pins
  one class instead of gathering everything that shares a single common word. A single keyword behaves
  the same either way. There is also a Super Cluster "Exclude Words" box: a family whose description
  contains an excluded word is kept out of the cluster even when its keywords match, so you can gather
  the "phospholipase" families while leaving "phospholipase inhibitor" out of them. It is separate from
  the Recluster by Words exclude box, which drops words from the comparison rather than dropping a whole
  family.
- **Search now scopes reclustering.** When you type in the Search box, Recluster by Words and Create
  Super Cluster act ONLY on the families the search shows, leaving every other family's numbers exactly
  as they were. Clear the Search box and both operate on the whole database again, as before. This lets
  you organise one subgroup at a time; the word vectors are computed within the shown families, which is
  the intended "organise just this subgroup" behaviour. Locks still hold within the subgroup.

## 1.2.0.0 (pending publish)

The identity clustering now closes gaps the fast first pass left. With the new "exhaustive" option (on
by default), after the initial clustering every pair of proteins is aligned in full and two families are
merged when the proteins are at least the chosen identity over most of the longer sequence. Proteins
that only share a short motif are correctly kept apart. This groups, for example, angiotensin fragments
and full-length metalloproteinases that the fast pass had split. On very large databases (over 10000
sequences) it warns first, since the full alignment takes longer, but it still runs. Turn the option off
to use the faster first-pass-only clustering.

## 1.1.2.0 (pending publish)

Clicking a column header now toggles between ascending and descending as expected. The USearch ID and
User ID columns were stuck on ascending; both now sort either way, and the USearch ID column sorts by
its own numbers.

## 1.1.1.0 (pending publish)

Automatic reclustering now keeps the USearch ID and User ID columns in step. Before, a Recluster by
Words or Create Super Cluster left the merged families showing their old USearch numbers while the
User ID column showed the merged number; now both columns move together, and the two differ only
where you renumbered a family by hand. A family you had renumbered by hand is untouched, as before.

## 1.1.0.0 (pending publish)

Everything listed under 1.0.9.0 below is in this version too: an update carries you straight to the
newest one, so read both sections if you are coming from 1.0.8.0.

### For everyone

- **New screen: De Novo (PEAKS).** A workflow of its own, on its own menu. It needs no Family Cluster
  File to start, it changes no family, no User ID and no quantitation, and it writes nothing except
  the CSV files you ask it for. It answers one question: what does a run hold that the database
  search did not find? It works in five steps.

  1. **Import and filter.** Point it at a PEAKS export folder and it reads every `*.denovoOnly.csv`
     underneath, including the per-run sub-folders, and keeps the sequences that clear four criteria
     you can set: a minimum length, a minimum ALC, and a run of consecutive residues at a confidence
     floor. That last pair is one criterion in two boxes, and it is the sharp one: it keeps a
     sequence with one clearly readable region and drops one that a good average flattered. You are
     told how many files were found, how many rows they held, how many survived, and the same
     breakdown for each run separately.
  2. **Reconcile with PatternLab.** Give it the SEPro search of the same runs and every kept row
     comes back as one of five things: new territory, a co-isolated second species, a redundant
     confirmation, an agreement PatternLab itself scored weakly, or a straight disagreement. The
     disagreements and the weak agreements are collected into a review queue, with both sequences
     side by side and the evidence for each, because those are decisions and the program does not
     pretend to make them for you. The per-run summary and the queue both export to CSV.
  3. **Extract XIC.** It finds each kept run's `.raw` by file name, says plainly which runs found one
     and which did not, and measures every kept precursor, one run at a time. The results are cached
     beside the acquisitions, so opening the same project again costs seconds instead of minutes.
  4. **Homology to families.** It searches the kept sequences against a Family Cluster File and
     reports how many reached a real protein, how many reached only reversed ones, how many reached
     nothing, and which families were found, with a per-peptide export. This step does not wait for
     step 3: homology reads peptides, not chromatograms.
  5. **Three-way partition.** See below.

  A value this screen cannot accept is refused in the line at the bottom, with the previous value
  left in force. Nothing here interrupts you with a dialog.

- **The three-way chart.** The last step of the de novo screen takes each run's whole ion current and
  cuts it into three parts: what PatternLab identified, what the de novo set adds on top of that, and
  what is still without an owner. It draws one bar per run and one for the runs together, saves every
  number behind it as CSV, and opens in a window of its own when you want to look at it rather than
  work through it. Each number says how it was obtained, in one of three words: **measured** from the
  RAW, **recorded** in a file that wrote it down, or **derived** from the other two by arithmetic.
  The remainder is always the derived one, which is why it can come out negative, and a negative
  remainder is shown rather than hidden: it means the two measured parts claim more signal than the
  run actually holds. Which source supplied the PatternLab numbers is printed word for word,
  including its refusal when it could not answer per run.

- **Locks can now be seen and removed.** Layer 1 always says how many families are locked, and
  "Manage locks..." lists each one with how many proteins carry it, its description, and whether its
  number is still in use at all. Select rows and press "Remove selected locks" to let those families
  take part in automatic reclustering again. Removal takes effect at once in the open file and
  reaches the disk when you save the `.fcf`, exactly like every other change.

- **Help for the new screen.** The de novo screen has a Help button beside its status line, and while
  that screen is open Help · How to read these numbers shows its page instead of the family one. The
  page says what the de novo only list is, what the four criteria mean, why a co-isolated spectrum is
  extra coverage rather than a duplicate, what the review queue decides and why nothing decides it
  for you, what the extracted numbers count, why the decoy-only count is a control and not an FDR,
  and what the three-way bar partitions. Help · What do I do first? now ends with the de novo flow as
  an optional block.

### Known limitation, read this if you curated in 1.0.8.0

It still applies, unchanged: see the section of the same name under 1.0.9.0 below.

### Other known behaviors

- A lock can outlive its family: a merge can take a locked number out of use while the lock remains.
  "Manage locks..." lists such a lock as "no, merged away" rather than hiding it, because the number
  becomes live again the moment a family adopts it.
- Removing a lock is not a save. Like every other change, it reaches the disk only when you save the
  Family Cluster File from the main window.
- The de novo screen closes the family workspace when it opens, because it is a separate workflow and
  not a step of the family one. Save your family edits first.
- The de novo review queue is never resolved automatically. A cross-correlation and an ALC are not on
  the same scale and are not calibrated against each other, so an automatic rule there would be an
  invented threshold wearing the clothes of a result.

## 1.0.9.0

### For everyone

- **Manual curation is now protected.** Typing a family number into the User ID column locks the
  families involved: automatic reclustering (Recluster by Words and Create Super Cluster) will never
  merge a locked family again, even when the descriptions are nearly identical. The locks are saved
  inside the `.fcf`.
- **Reclustering no longer renumbers families it did not touch.** An untouched family keeps its
  exact number, and a merge adopts the smallest member's number. After a reclustering, the User ID
  column differs from USearch ID only where something actually merged or where you edited.
- Files saved by older versions that were **never reclustered** are migrated on load: any family you
  had renumbered by hand arrives already locked.
- The identity scan now opens with a curve above the table (families against target/decoy mixing per
  identity, with the clean floor marked).
- In-program redundancy removal for FASTA files, preserving the original sequence order.

### Known limitation, read this if you curated in 1.0.8.0

A file saved by **1.0.8.0 after a reclustering** cannot be told apart from its renumbering, so
manual numbers typed into it after that reclustering do **not** arrive locked. Re-typing the number
once in this version locks the family from then on.

### Other known behaviors

- Inputs the User ID column cannot accept (letters, decimals, negative numbers) are rejected; the
  cell stays in edit mode.

### For the analyst

- The quantitation run-count now comes from the matrix rows rather than the widest peptide, fixing
  shares that could exceed 100% on projects where no peptide spans every run.
- The word-similarity weighting was corrected so families with identical descriptions always merge
  at the strictest threshold, and families sharing only one common word do not.
- The CSV export has seven columns (the share over the frozen USearch grouping was removed).
