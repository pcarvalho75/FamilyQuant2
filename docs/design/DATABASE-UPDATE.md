# Updating a project's database (design note Z14c1)

Status: a design for Paulo to read (Z14c2). Nothing is built. Written by FQOpus on 2026-11-06 from
master 5a431fb (FamilyQuant 2.1.10 plus the roadmap). Every claim about today's program names the
file and line it was read from; section 12 lists what was not verified.

## 1. The idea, and how I read it

Ana's idea (11-06): when UniProt changes, update the project's database without starting over. New
entries come in, removed or changed ones go out or are flagged, and the work already done is kept:
families, names, locks, the evidence grouping and the quantitation choices.

Today the only route is File > Build families again, which replaces the family file and warns that
"User IDs, hand-merged families and annotations" cannot be recovered (FamilyQuant/USearch.Actions.cs:76-80).

The idea can be read three ways:

| Reading | What it means | Recommendation |
|---|---|---|
| A. A new database file | Ana makes a new FASTA the way she made the first one (UniProt download, her ordering, redundancy removal, the contaminants, the target-decoy file) and gives it to the program, which compares it with the project and carries the curation over | **Recommended.** It is the file her PatternLab search will use, so the project and the search stay on one database. |
| B. The program downloads UniProt itself | The program repeats the original UniProt query and builds the new database | Not now. Her database is a processed file (sorted, subsets removed with their accessions appended to the survivor's header, 381 contaminants, decoys) that the program would have to reproduce exactly, and PatternLab would still need the same FASTA. It could come later as a "what changed in UniProt" report. |
| C. Only the annotations changed | Same proteins, newer UniProt facts (reviewed status, keywords, families) | Already possible: Fetch evidence again. It replaces protein evidence, domains and the reference panel and leaves families, names and locks alone (EvidenceFetcher.cs:288-294, EvidenceFetchSession.cs:78-90 and :117-124). It does invalidate the confirmed scope, because the task's source fingerprint includes every protein's evidence (EvidenceTaskDiscovery.cs:169-180), so the scope is confirmed again. Help should say so; no new build. |

Two smaller choices inside reading A, with my recommendation:

- A protein absent from the new file: **remove it from the project and record it** (identifier, family,
  name, reason) rather than keep it flagged. The project's proteins must equal the database the next
  search uses; a kept ghost protein would still attract de novo peptides through the homology mapping.
- A protein whose sequence changed under the same accession: **update it in place, keep its family,
  and flag it** rather than remove and re-add it, which would lose its curation.

The rest of this note describes reading A with these two choices.

## 2. What Ana does and sees

1. She opens her saved project. If it has unsaved changes, the program asks her to save first.
2. File > Update database (new item under Build families, MainWindow.xaml:18). She chooses the new
   FASTA or target-decoy file.
3. The program reads it and compares it with the project (section 3), then places the new proteins
   (section 4), with progress and Cancel. Nothing in the project changes yet.
4. A preview window shows the counts and one table, for example (invented numbers):

   | | Proteins |
   |---|---:|
   | Unchanged | 31,520 |
   | Header changed (for example now reviewed, tr to sp) | 140 |
   | Same sequence under a new accession | 6 |
   | Sequence changed (kept in its family, flagged) | 9 |
   | Replaced by a longer record that contains it | 3 |
   | Added, joining an existing family | 412 (into 180 families, 61 of them locked) |
   | Added, in new families | 28 (in 17 new families) |
   | Added, close to several families (joined the nearest, families not merged) | 5 |
   | Removed | 77 (2 families emptied) |
   | Ambiguous, needs her choice (section 3) | 2 |
   | Family names that would change (section 4) | 4 (3 kept by default, 1 to decide) |

   The table lists every protein that is not unchanged: kind, old and new identifier, family number and
   name, and for an added protein the kept protein it was placed by, with identity and aligned length.
   It can be copied and saved as CSV.
5. Apply update makes the change as one step; Cancel leaves everything as it was. The project is then
   unsaved: the file on disk is untouched until she saves, and the window says that Save as keeps both.
6. The status line then says what to do next, in her words: "Database updated: 440 proteins added,
   77 removed, 9 sequences changed. 449 proteins have no evidence yet: Fetch evidence. Results are from
   the previous database: Quantify again." Group by evidence then works on the new proteins with her
   saved scope offered again.

## 3. How each protein is matched

Each row of the project is matched to a record of the new file by its key: the accession that
`FamilyNaming.Accession` reads (FamilyNaming.cs:84-96), the isoform suffix kept, plus whether the row
is a decoy. The whole identifier is not the key, because it changes when an entry is reviewed: in
Z12a1, A0A6H1Z3X8 was `tr|A0A6H1Z3X8|A0A6H1Z3X8_TRIPP` in her download and is the reviewed entry
VM2P_TRIPP in UniProt 2026_04, so her next download will say `sp|A0A6H1Z3X8|VM2P_TRIPP`. A decoy has no
accession (FamilyNaming.cs:87), so a decoy is keyed by the accession of the identifier after the decoy
prefix (SequenceLabels.cs:7), on the decoy side. An identifier with no UniProt accession (her two
affinity tags) is keyed by the whole identifier, as `Accession` already returns it.

Matching is one to one: every old row and every new record takes part in at most one match, and a
match is made only when it is the only possible one on both sides. The steps run in this order, and a
row or record matched in one step leaves the pools of the next. A target never matches a decoy.

| Step | Rule | Class and what happens |
|---|---|---|
| 1. By key | A key held by exactly one old row and exactly one new record pairs them | Unchanged (same sequence and header): nothing. Header changed (same sequence, other identifier or description): identifier and description updated; family, number, name, lock kept. Sequence changed: sequence and header updated, family kept, flagged (section 5) |
| 1b. Repeated key | A key held by several old rows or several new records: an old row and a new record of that key with the identical sequence pair only when neither has another identical-sequence partner of that key | As step 1. Whatever is left of that key goes to the ambiguous list |
| 2. Same sequence, new accession | Among old rows whose key is absent from the new file and new records whose key is absent from the project: a sequence held by exactly one such old row and exactly one such new record pairs them | Treated as a header change (redundancy removal can pick another representative of identical sequences) |
| 3. Replaced by a longer record | Among the rows and records still unmatched: an old row whose sequence is contained in exactly one unmatched new record, when that new record contains no other unmatched old row, pairs with it | The new record takes the old protein's family, number and name. This is what the program's own Remove redundancy does: a subset is dropped and its identifier appended to the survivor's description (RedundancyTools.cs:69-94); her file carries such headers |
| Ambiguous | Any case where a step finds more than one candidate on either side: two new records of one old key with no unique identical sequence, one new record containing several unmatched old rows, one old row contained in several new records, a sequence held by several unmatched rows or records | Nothing is carried over automatically. The preview lists each case with its candidates and the step whose test found them; she may pick one pair per case, and the pair then has the outcome of that step: a repeated-key pick (step 1b) is a header change, or a sequence change when the sequences differ; a same-sequence pick (step 2) is a header change; a containment pick (step 3: one new record containing several old rows, or one old row contained in several new records) gives the chosen new record the chosen old protein's family, number and name. In every kind, the rows and records of the case she did not pair are handled as removed and added. Without her pick, the old rows of the case are treated as removed and its new records as added, so no old curation reaches a new record by chance |
| Removed | An old row left unmatched and not ambiguous | Row removed; recorded |
| Added | A new record left unmatched and not ambiguous | Placed (section 4) |

The ambiguous count is shown before Apply, and Apply names how many cases will be handled as removed and
added. Build families already refuses a repeated identifier inside one file (USearchTools.cs:735-748), so
a repeated key can only arise from accessions shared by different identifiers (for example an isoform
written without its suffix). Example from Z12a1 for "Removed": A0A2H4N390 is inactive in UniProt
2026_04, merged into P85314, which her file already holds as `sp|P85314`; a new download will not contain
it, so it is removed and recorded, and P85314 keeps its own family.

## 4. How a new protein joins a family

Build families has two passes (USearchTools.cs:200-281): a fast greedy pass (`usearch -cluster_fast`,
each sequence joins the first centroid at or above the identity), then, when exhaustive is on, an
all-pairs pass that single-links families through every pair passing the gate: identity at or above
the threshold and aligned length at least identity/100 times the longer sequence
(`PassesExhaustiveGate`, USearchTools.cs:308-316; `ApplyExhaustiveSecondPass`, :395-422). The update
uses the same two tests, at the project's own identity (`ClusteringIdentity`, FamilyQuantFile.cs:17)
and with the exhaustive pass only if the project was built with it (`ClusteringMethod`, USearchTools.cs:275):

1. Fast pass against the kept proteins. The added sequences are aligned with every kept sequence by the
   program's existing `USearchTools.SearchGlobal` (USearchTools.cs:536-546: `-search_global`, every pair
   aligned, `-maxaccepts 0 -maxrejects 0`, the call Group by evidence's neighbour search already makes,
   NearestReviewed.cs:410), at the project's identity. A hit at or above the identity is the test
   cluster_fast makes against its centroids. The file does not store centroids, so every kept protein
   serves as a seed.
2. Second pass. The same hits are kept as links when they pass `PassesExhaustiveGate`. The added
   proteins are also clustered among themselves with the existing `USearchTools.Cluster`.
3. Nearest kept neighbour. An added protein with any link (its fast-pass hit or a gate link) joins the
   family (UserID) of its best link: a link that also passes the second-pass test first (so a perfect
   match over a short fragment does not outrank a full-length relative), then highest identity, then
   longest alignment, then lowest family number. Its USearchID is that kept protein's USearchID, so a
   curated split inside one automatic family is respected: the new protein goes to the side it is
   closest to.
4. Its own new relatives. An added protein with no link to a kept protein, but in the same added-only
   family as placed proteins, joins the family that most of those placed relatives joined.
5. New families. An added-only family with no placed member becomes a new family, numbered above every
   number in use (the same rule Group by evidence uses, EvidenceApply.cs:211 and :336-337), so a removed
   family's number is never reused.
6. Never merged. Where Build families would chain two kept families through an added protein (its
   second-pass links reach both), the update does not: the protein joins the nearest and the table lists
   it as "close to several families". A fast-pass hit alone never chains families in Build families, so it
   is not listed.

Locked families. A lock means automatic reclustering never merges that family into anything
(FamilyQuantFile.cs:38-46). Adding a protein is not a merge, and Ana's Group by evidence families are all
locked (each applied family is locked, EvidenceApply.cs:318), so if new proteins could not join a locked
family, every new member of her curated families would arrive alone. Recommendation: an added protein may
join a locked family; the family stays locked and the table counts those joins separately. She moves
any she disagrees with by hand.

Names. A family's name is computed from its current members each time: the most frequent User
Annotation of its members, else the most frequent protein name (FamilyNaming.cs:9-23, :39-56). No name is
stored on the family, so removing members or changing descriptions can change it. The update therefore
computes every surviving family's name before and after the planned change and shows each one that
would change, with a "Keep the old name" tick:

- Ticked by default when the old name came from a User Annotation (a name she typed, or one Group by
  evidence wrote when she applied it, EvidenceApply.cs:317) and no surviving member carries a different
  annotation.
- Not ticked by default when a surviving member carries a different User Annotation: the preview lists
  those members beside the family, because keeping the name replaces their annotation, and she chooses.
- Not ticked by default when the old name came from protein descriptions; it then follows UniProt.

Keeping a name writes it into the User Annotation (FastaItemExtended.cs:42) of every member of the
family, kept and joining alike, replacing any different annotation. Every member then carries the same
annotation, so FamilyNaming's first rule returns it with no tie to break (the most frequent annotation,
ties to the first member met, FamilyNaming.cs:44-48 and :118-137). This uses the existing
per-protein field, not a new one: the 1.5 export already writes and reads User Annotation (the frozen
reader snapshot covers annotation, docs/PROJECT-FORMATS.md:49), so a 1.5 reader shows the same name. The
rows whose annotation is written this way are listed in the update record. A family whose members are all
removed has no name to keep; its last name is recorded.

Order. Removed rows are deleted and added rows appended at the end; kept rows keep their order, so name
ties, which go to the member met first, do not move (FamilyNaming.cs:21-22).

Cost. On a synthetic database of 60,000 sequences (3,000 families of 20, lengths 150 to 600), 1,000
added sequences aligned as in step 1 took 64 seconds with the bundled USEARCH and 4 threads on this
machine (the indexed `-usearch_global` took 2 seconds but missed 450 of the 9,639 hits, so it is not
proposed). The all-pairs pass of Build families grows with the square of the database; the placement
grows with added times kept.

What differs from a rebuild, said in Help and the report: the fast pass is order-sensitive, so a
rebuild could form a few families differently; the update never moves or merges a kept protein. A
rebuild stays available (Build families).

## 5. Removed and changed proteins

- Removed: the row leaves the project and goes into the update record with its last family number and
  name. A family left with no target proteins disappears; its lock is removed with it. Optional later:
  ask UniProt why (inactive: merged into, deleted, demerged), one request per removed accession.
- Sequence changed: the protein keeps its family, number and name; its evidence and domains are cleared,
  because positions and domains belong to one sequence (EvidenceFetcher.cs:235-249 keys previous
  positions by accession and sequence hash). The new sequence is tested against its own family's other
  members with the same two tests; if none links, it is flagged "sequence changed; nearest now family N"
  and not moved.
- Header changed: identifier and description are replaced; evidence is kept (the same accession and
  sequence) until the next Fetch evidence. The same sequence under a new accession (step 2) and a
  replacement by a longer record (step 3) keep the family, number and name but start without evidence,
  because evidence belongs to one UniProt entry.

## 6. What changes in the file

The project file (.fqproj) holds the family file as its curation.json entry, byte-identical to a .fcf
(FamilyQuantFile.cs:119-133), beside quant.tsv.gz, manifest.json and the spectrum table
(Storage/ProjectFileSchema.cs:25-37). Only curation.json changes:

| Field (FamilyQuantFile.cs and FastaItemExtended.cs) | After the update |
|---|---|
| `MyExtendedFastaItems` | Kept rows unchanged except as section 3 says and except a User Annotation written to keep a family name (section 4); removed rows deleted; added rows appended with `USearchID`, `UserID`, `UserAnnotation` (section 4), no evidence, no domains, `XICUser` 0 |
| `SourceSha256`, `SourceSequenceCount` | The new file's SHA-256 and record count |
| `ClusteringIdentity`, `ClusteringMethod`, `WordSimilarity`, `IsKeywordSuperCluster` | Unchanged (the update is recorded separately) |
| `LockedUserIds` | Kept; numbers of emptied families removed |
| `DomainSource`, `EvidenceSource` | Kept; they still describe the kept proteins' evidence (one release) |
| `EvidenceRecipe`, `FamilyPanel`, `Motifs`, `PanelProteinAssignments`, `ProteinSubclassLabels`, `ReportDetails`, `ReportSelection`, `ReportThreshold` | Kept as stored (section 7 says which become out of date) |
| New: `DatabaseUpdates` | A list, written only when not empty, one record per update (section 8) |

Old projects without `DatabaseUpdates` keep their bytes. The 1.5 export (.fcf) goes through the same
`Save` and must stay readable by the frozen reader, which tolerates the 2.0 fields today
(docs/PROJECT-FORMATS.md, X7 table); Z14c3 reruns that check.

## 7. What is kept, what goes out of date, what is recomputed

| Part | After the update | Read from |
|---|---|---|
| Families, numbers, locks, annotations of kept proteins | Kept exactly | section 6 |
| Word or keyword grouping settings | Kept | FamilyQuantFile.cs:28-36 |
| Protein evidence of kept proteins | Kept (still that release); cleared for changed sequences; none for added | section 5 |
| Reference panel, family world, confirmed scope | Out of date: the confirmed task binds the exact protein set (EvidenceTaskDiscovery.cs:154-180), so Group by evidence asks to confirm the scope again, offering the saved terms | EvidenceTaskDiscovery.cs |
| Group by evidence choices (scope terms, names, splits, motifs) | Kept in the recipe and offered again; the recipe itself cannot be replayed, because `ApplyRecipe` refuses a different source (EvidenceApply.cs:267-270) | EvidenceApply.cs |
| Panel family assignments and subclass labels | Out of date automatically: their fingerprint hashes every protein's index, identifier, sequence and family (PanelAssignmentEvidenceStore.cs:117-131), so they read as not current until recomputed | PanelAssignmentEvidenceStore.cs |
| Family panel (Ana's families, domains, subclasses) | Kept; it is keyed by panel family keys, not proteins | Panel/FamilyPanel.cs:7-34 |
| Stored quantitation (peptide areas, manifest settings) | Kept as computed; the quantitation choices stay for the next Quantify | Storage/QuantManifest.cs |
| Results studies | Out of date: "The saved family results do not match the current family membership. Run Quantify again to refresh them." (Quantitation/FamilyStudyMembership.cs:21) | |
| Spectrum table family columns | Kept as computed with the reason "mapped at Compute against a different protein database than the open curation" (FamilyEnvelopeQuant.cs:100-102) | |
| Families screen share column | Recomputed by mapping the stored peptides to the new protein set again (CurationQuantitationViewModel.Operations.cs:34). The update must trigger this: the prepared mapping assumes the protein set never changes during curation (CurationQuantitationViewModel.cs:112-114) | |
| PatternLab reading (`XICUser`) | Kept for kept rows, 0 for added, refreshed by the next PatternLab attribution | FastaItemExtended.cs:54 |
| Undo | Emptied, with a sentence saying why: earlier steps refer to rows that no longer exist; evidence undo refuses a changed protein list (EvidenceUndo.cs:42-45) and grouping undo restores by identifier only (GroupingSnapshot.cs:86-110). To go back: close without saving. | |

One gap the update exposes, a separate decision: Group by evidence gives every applied group a new
family number (EvidenceApply.cs:211-217), so a new protein that is in Ana's evidence family by UniProt
evidence but below the sequence identity (and so in a new family after placement) would be proposed as
a second family with the same name, not added to the existing one. Proposed separate step: when a
proposal group's key equals a group key in the stored recipe whose family still exists, Apply reuses
that family number. It changes a scientific transaction, so it needs its own science review.

## 8. What the report records

Each update adds a record to `DatabaseUpdates`: date (UTC), program version, previous and new file name,
SHA-256 and record count, identity and whether the exhaustive test was used, the placement method text
and version, the counts of section 2, and one row per protein that is not unchanged (kind, old and new
identifier, family number and name, the placing protein with identity and aligned length, note).

Report Methods (Report/ReportMethods.cs, which already writes "Database source SHA-256" at :32 and
"Clustering method" at :44) gains one sentence per update, for example: "The database was updated on
2026-11-20 from <file> (31,777 records, SHA-256 ...) to <file> (32,140 records): 440 proteins added,
412 of them placed in existing families by the clustering identity test, 77 removed, 9 sequences
changed; kept families were not merged." The exported report folder gains database_updates.csv.

## 9. What can go wrong, and what the program does

| Risk | What the program does |
|---|---|
| The wrong file (another clade or another pipeline) | Before placement: if fewer than half of the project's target proteins are kept, it warns strongly and suggests Build families |
| Decoys or contaminants present in one file and not the other | Refuses, naming what differs; decoys and contaminants are rows like any other and are matched and placed by the same rules |
| An ambiguous match (a repeated key, several supersets, several identical sequences) | Lists it as ambiguous; carries no curation over unless she picks the pair (section 3) |
| A family name that would change | Shown in the preview with "Keep the old name" (section 4) |
| Placement differs from a rebuild | Said in the preview, Help and the report; Build families remains |
| A new protein links two curated families | Joins the nearest; listed; never merged |
| A new protein joins a locked family she would have kept apart | Listed separately; she moves it by hand |
| PatternLab was searched against the old database | Said in the status line and Help: search again with the new file for identifications of the new proteins; until then they receive only de novo signal and identified peptides they share with kept proteins |
| Evidence of mixed releases | Fetch evidence fetches every protein in one release and refuses a release change between batches (EvidenceFetcher.cs:172, :221-222), so the file returns to one release |
| Cancel or failure during the update | Nothing changes: compare and place work on copies; one operation commit applies (Core operations contract, docs/CORE-OPERATIONS.md) |
| Index-based records after rows are deleted | `ProteinIndex` in the recipe, panel assignments and subclass labels refers to the old order. Z14c3 must check that every reader of those indices is behind a currentness test that fails after the update; candidates found: FamilySubclassCurationAdapter.cs:27, PanelAuditCsv.cs:38, EffectivePanelFamilyStudy.Parts.cs:19, ReportFindings.cs:57, FamilyReportThreshold.cs:31 |
| Many added proteins (more than about a third of the database) | The preview suggests a rebuild may be the better choice; the update still runs |

## 10. Tests for Z14c3 (synthetic databases only)

1. Matching: an old and a new synthetic target-decoy FASTA with each class of section 3 (including tr to
   sp with a new entry name, an isoform, a decoy, a contaminant, an affinity tag without accession, a
   subset replaced by its superset); every class and count asserted. One-to-one: two new records with
   one old key and no unique identical sequence, one new record containing two old rows, one old row
   contained in two new records, and a sequence held by two unmatched rows each end in the ambiguous list
   with no curation carried over; picking a pair carries exactly that one; no row or record is matched
   twice.
2. Placement: an added protein above the identity joins an unlocked family (UserID and USearchID); one
   joins a locked family and the lock stays; one close to two families joins the nearest and both stay
   apart; added proteins linked only to each other form one new family numbered above the maximum; an
   added protein linked only through another added one follows it; a fragment that passes the fast test
   but not the gate still joins (fast pass); a project built without the exhaustive pass uses the fast
   test only.
3. Same rules: where no kept family would be merged, the update's partition equals a fresh Build
   families of the new database (family numbers aside).
4. Kept exactly: every kept row's UserID, USearchID and annotation (except annotations written by a ticked Keep the old name), the lock set, panel, motifs, report
   details and row order are equal before and after; names of untouched families are equal
   (FamilyNaming.Names); a family whose annotated members were all removed keeps its name when ticked
   (the default for annotation names) and falls back to its protein names when unticked; a description
   name follows the new descriptions unless ticked; the 1.5 export shows the kept name. Mixed
   annotations: the old-name carriers are removed while a member with a different annotation survives
   (the tick starts unticked, the member is listed, and ticking writes the old name on every member so
   it wins); the same with two different surviving annotations of equal count (a tie that member order
   would otherwise settle); and with surviving members carrying the old name and a different one at
   equal count.
5. Out of date: FamilyStudyMembership.Problem reports the change; the spectrum refresh keeps the
   "different protein database" reason; panel assignments read as not current; the confirmed task is
   refused; evidence is cleared only for changed sequences.
6. Refusals and atomicity: repeated key, decoy mismatch, cancellation at each stage leaves the file
   byte-identical.
7. Save and reopen: SavedFileCheck passes; DatabaseUpdates round-trips; the 1.5 export opens in the
   frozen reader (`--export-fcf-compatibility`).
8. Report: the Methods sentence and database_updates.csv on the synthetic project.
9. Repeat: the same two inputs give byte-identical results twice.
10. Shell: the menu item, preview counts, Apply, Cancel and the unsaved-changes refusal on the synthetic
    fixture, with new test switches registered in SuiteCommandInventory.cs.

## 11. Size estimate and a proposed split

The roadmap gives Z14c3 two hours. My estimate for reading A is about 9 to 11 hours with review, so I
propose splitting it:

| Step | Work | Diff | Hours |
|---|---|---|---:|
| Z14c3a | Core: matching and classes, the update record model, tests 1 and 9 | M | 2 |
| Z14c3b | Core: placement (`SearchGlobal` and `Cluster` reused, the rules of section 4), tests 2 and 3 | H | 2.5 |
| Z14c3c | Core: the apply transaction (rows, names, locks, evidence clearing, the index-reader check), tests 4 to 7 | M | 2 |
| Z14c3d | App: File > Update database, preview window, progress and Cancel, undo emptied, share column refreshed, status line, test 10 and screens | M | 2 |
| Z14c3e | Help page and report Methods sentence and CSV, test 8 | L | 1 |
| Later, own decision | Group by evidence reuses an existing evidence family number (section 7) | M | 1.5 |
| Later, optional | Ask UniProt why each removed accession left | L | 1 |

A smaller first version, if time is short: Z14c3a to Z14c3c plus a summary dialog with the counts and a
CSV beside the project instead of the preview window (about 7 hours).

## 12. What I did not verify

- I did not build or run any of this; the only runs were the synthetic timings in section 4 (bundled
  USEARCH 11.0.667, 60,000 by 1,000 sequences).
- Whether `search_global`'s identity equals `allpairs_global`'s on every alignment; both are global
  alignments with the same default definition as far as I know, and test 3 is meant to catch a
  difference.
- How Ana makes her FASTA step by step (her redundancy tool, ordering and contaminant list); section 3
  relies on the header format seen in her file and on RedundancyTools.cs.
- The family world's own currentness check after an update, and every reader of `ProteinIndex` listed in
  section 9.
- That the frozen 1.5 reader ignores the new `DatabaseUpdates` field (expected from how it handles the 2.0
  fields; test 7 checks it).
