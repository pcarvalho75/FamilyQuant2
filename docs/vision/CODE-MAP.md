# FamilyQuant 2.0 code map (reading notes for the vision study)

Branch fable/vision, read from master 7a54be1 on 2026-11-01. "Read" means read in the code at the
line given; "measured" means run on data in this session; "estimated" is my judgement. Where a document
and the code disagree, the code wins and the line is given.

## 1. How a FASTA becomes a family number (identity families)

1. `USearchTools.Cluster` (FamilyQuant.Core/USearchTools.cs:182) runs `usearch -cluster_fast <fasta> -id <identity/100>`
   (line 200) in the FASTA's own folder, parses the `.uc` (ParseClusterFile, line 652): column 1 is the cluster
   number, column N-2 the label; identifier = label up to the first space, description = the rest (line 680).
   Every item gets `USearchID = UserID = cluster number` (line 693). A duplicate identifier in the FASTA throws
   (line 662); a label not in the FASTA throws (line 686). Items are sorted by USearchID (line 703).
2. Exhaustive pass (`ApplyExhaustiveSecondPass`, line 363): `usearch -allpairs_global <fasta> -acceptall -userout`
   with fields query+target+id+alnlen+ql+tl (line 446). Edges are memoised per FASTA path + content SHA-256
   (line 408). `PassesExhaustiveGate` (line 275): id >= identity AND alnlen >= identity% of the LONGER sequence.
   `MergeFirstPassClusters` (line 292) is union-find over first-pass cluster ids; survivor = smallest id
   (`WordCluster.BuildStableMapping`, WordCluster.cs:122). Result written into BOTH ids (line 383).
   An empty/failed alignment falls back to the fast result with a warning (line 371), cancellation propagates.
   READ: the memo holds EVERY accepted pair in memory as a List of PairwiseEdge (line 61). With -acceptall the
   number of pairs is N(N-1)/2: 584 sequences (Bjararaca) is 170k edges; 63,554 (Viperidae with decoys) is
   2.0e9 edges, which is why the roadmap ruled Viperidae exhaustive out (ROADMAP X3). So on Ana's database
   the identity families are FAST-ONLY in practice. This is the first rigid point: the "exhaustive" idea is
   right but its O(N^2) all-against-all with everything kept cannot reach the files that matter.
3. The document: `FamilyClusterFile` (FamilyQuantFile.cs:13) holds `MyExtendedFastaItems`, `ClusteringIdentity`,
   `SourceSha256`, `ClusteringMethod`, `WordSimilarity`, `IsKeywordSuperCluster`, `LockedUserIds`,
   `DomainSource`, `EvidenceSource` (partial class, more fields in Evidence/*.cs: `EvidenceRecipe`, `Motifs`,
   `LastSaveCheck`). `GroupedFastaItems` groups by UserID (line 75). Persisted as indented System.Text.Json
   (line 120); loading migrates pre-lock files (line 176). Save writes temp + SavedFileCheck + move (line 192).
4. `FastaItemExtended` (FastaItemExtended.cs:10): USearchID, UserID, UserAnnotation, Domains (string "; "-joined),
   Evidence (ProteinEvidence), XIC (legacy, read only), XICUser, FullXicShare (not persisted). The taxon is
   NOT a field: species lives only in the description text (OS=...), parsed nowhere in Core except by the word
   matcher cutting at " OS=".
5. Scoring (FamilyClusterScoring.cs): `CalculateGroupingScore` (line 14) = target-only / decoy-only / mixed
   families; `CalculateFDR` (line 56) returns mixed/all, documented in its own comment as NOT an FDR and blind
   to target-target over-merging (line 47). `ScanIdentities` (line 69) reclusters per step; `DivideAndConquerDetailed`
   (line 121) bisects for the lowest identity with mixing <= bound; a database with no decoys throws (line 134).
6. Redundancy removal (RedundancyTools.cs:33): identity 100 = substring elimination, else MAligner's
   `Compress().CompressByIdentity` (line 58); order preserved.

## 2. Word grouping and keyword super clusters

- `WordCluster` (WordCluster.cs:43): one `FastaClusterElement` per protein with `FamilyId = UserID` (line 69);
  terms from `DescriptionWordMatcher.Read` (DescriptionWordMatcher.cs:39): description cut at " OS=", lower-cased
  (current culture, line 55, "part of the frozen grouping behavior"), split on whitespace/-/_//, stop words
  removed (the list at line 12 includes "protein" and "fragment"), bigrams added (line 50).
- `GroupByWords` (WordCluster.cs:87): per-family TF-IDF vectors (idf = log((1+N)/(1+df)) + 1, line 255),
  L2-normalised; `MergeClusters` (line 287) is a greedy seed-first single pass sorted by vector size, cosine >=
  threshold, NOT transitive beyond the seed (a family is merged into the first seed it matches). Locked families
  contribute to IDF but are held out (line 99).
- Keywords: `DescriptionWordMatcher.MatchKeywords` (line 108): All/Any over retained words; phrases over RawTokens;
  excludes recorded per protein and per family. `SuperClusterPreview` (SuperClusterPreview.cs:72) is the pure model
  behind the window; `Join` (line 300) moves ticked proteins to the smallest whole-joining family number or to a
  fresh number past every id and lock (line 304).
- `FamilyFilter.Apply` (FamilyFilter.cs:206) is the Search box: any term, substring, in description, domains or
  identifier. `FamilyNaming.Name` (FamilyNaming.cs:296): annotation (most frequent), else most frequent protein
  name before " OS=". `FamilyNaming.Accession` (line 341) is the UniProt accession regex over '|' fields.
- `GroupingSnapshot` (GroupingSnapshot.cs:21) undo; `BatchCuration.Apply` (BatchCuration.cs:148) batch edit.

## 3. The evidence engine (FamilyQuant.Core/Evidence)

- Fetch (EvidenceFetcher.cs:33): ONE UniProt REST call shape, `/uniprotkb/accessions` with fields
  accession, reviewed, protein_families, keyword, cc_tissue_specificity, xref_pfam, xref_panther, ft_transmem,
  fragment; 500 per batch; all or nothing; release from the `x-uniprot-release` header (UniProtHttp.cs:146).
  Family is PARSED FROM THE SIMILARITY COMMENT TEXT ("Belongs to the ... family", regex at line 36 and
  `ApplyFamilyAnnotations`, line 142); several memberships are kept (FamilyMembership.cs). No GO terms, no
  InterPro, no signal peptide, no taxonomy, no sequence version, no PTM, no literature are fetched.
  Only well-formed accessions are asked (ProteinDomains.Batches, ProteinDomains.cs:216), so a non-UniProt
  header gets no evidence at all.
- Reference panel (ReferencePanel.cs:80): per family named in the file, `/uniprotkb/search` with
  `(reviewed:true) AND (family:"...")`, paged by the Link header, with sequences; the announced total must
  arrive (line 134); tissue text kept, SIMILARITY text dropped (line 128). Families come FROM THE DATA
  (`FamiliesFound`, line 54).
- Nearest reviewed (NearestReviewed.cs:211): `usearch -search_global` of every target against the references
  (the file's reviewed proteins + the panel, in scope when a scope is given, line 260), gate identity >= 50% and
  alignment >= 70% of the QUERY (`PassesGate`, line 153), best by identity then alignment then accession (line 170),
  never the same accession (isoforms folded, line 112), ties across families reported not settled (line 182).
  Memoised on the content of both sets (line 230). 2,000 queries per USEARCH run (line 93).
- Classifier (EvidenceClassifier.cs:14), tiers in order (lines 54-128): contaminant -> never; multiple UniProt
  families -> flagged, user decides; in scope + one UniProt family -> that family; in scope + neighbour >= 60% ->
  neighbour's family; in scope + learned marker domain (>= 80% of labelled in-scope carriers vote, line 199,
  ties refused line 197) -> that family; out of scope + neighbour >= 70% whose reference is in scope and whose
  domains overlap the family's/reference's and do not conflict with a marker -> family; else unassigned.
  Scope = phrases matched in Keywords and TissueSpecificity ONLY (ScopeMatcher, line 269), never in the name.
  Contaminant = identifier prefix "contaminant_" (line 213).
- Coherence (CoherenceCheck.cs:61): per family profile from reviewed members (file + panel, each accession once):
  length and cysteine ranges on complete members, max transmembrane, domain carriers; a range needs >= 10 members
  (line 50); flags on length above/below, cysteines, helices, a domain no reviewed member carries (line 121).
- Architecture (EvidenceArchitecture.cs:266): groups a family's members by their exact Pfam accession SET
  (JSON of the sorted accessions, line 299); a complete member with no domain call joins the single combination
  whose complete reviewed members (>= 10) span its length and cysteines (line 308); can split when > 1 group
  with domains (line 360).
- Apply (EvidenceApply.cs:87): validates the selection against fingerprints, gives each (family, group key) a
  NEW number past every id and lock (line 193), writes UserID + UserAnnotation (= the family's name) and LOCKS
  each new family (line 282); stores an `EvidenceRecipe` with Assignments, fingerprints, release, program
  version, session search and motifs. `ApplyRecipe` (line 227) replays only on the identical source state.
  Undo is `GroupingSnapshot.CaptureEvidence` (EvidenceUndo.cs:187).
- Validation (ValidationReport.cs:94): leave-one-out via NearestReviewed on the reviewed targets (file, optionally
  + panel); right = neighbour at >= 60% with IDENTICAL family label set. Route comparison (`WordsRoute`,
  `ReclusterRoute`, `ScoreRoute`, line 158-199) scores the keyword preview against reviewed membership.
- LeftOutList (LeftOutList.cs:36): carriers of a learned marker not placed in that family, with the reason.
- MotifCheck (MotifCheck.cs:161): user regex / "count C 12-16" / "not ..." over complete members.
- SavedFileCheck (SavedFileCheck.cs:371): reopen and hash sequences, decoys, unassigned, locks, source, curation.

What the evidence engine does NOT have (read, absent): any use of the taxon; any sequence feature beyond length,
cysteine count, transmembrane count and Pfam presence; any alignment-free similarity; any HMM; any use of the
quantitation; any link from a family to the peptides observed. It classifies proteins of a FASTA, period.

Document vs code: CLAUDE.md says the evidence rule "Tiers: ... out of scope with a neighbour at 70% or more and
compatible domains". The code adds the E16 condition that the NEIGHBOUR itself must be in scope
(EvidenceClassifier.cs:99-105) and refuses "neighbour_out_of_scope"; SCIENTIFIC_METHOD.md's tier list omits it,
SCIENCE-GATE.md "After E16" has it. Code wins.
