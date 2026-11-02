# FamilyQuant 2.0: the extra mile (study, not a roadmap)

Written 2026-11-01 under a hard stop (M2: 20:06). Companion notes: docs/vision/CODE-MAP.md. Every statement is
tagged read (seen in the code), measured (run in this session on the real files), estimated (my judgement) or
unverified (not checked). The App and the Tests checkers were NOT read (CODE-MAP.md section 6).

## Desk notes, added after Fable's report

Roadmap version 3 takes this direction with these corrections. Fable's text below is unchanged.

1. The Bjararaca database is not 584 proteins: it has 171 targets, 121 contaminants and 292 decoys (measured in `scientific_results.fqproj`). That makes the argument stronger.
2. Reversed decoys measure chance hits, not wrong-family hits, so they cannot test the risk under "Strongest case against": a conserved motif hits a real protein of the wrong family. B1 adds a known-answer test.
3. "Why new" is not established. Only PubMed was searched and the closest papers were not read. B2 does the real search, and until then nothing we write says new or first.
4. A bigger FASTA already widens the search today: Ana's Viperidae file does that. What is new here is the automatic family world, the credit at family level and the gap ranking.
5. Part 4 overreaches: some de novo study files are called from the app project, so X9 moves only those no screen calls.

## Part 1. What we are trying to accomplish

Who this is for. Ana Gisele and Richard quantify venoms. Their real question is not "which protein" but "how
much of this venom is metalloproteinase, how much is serine protease, how much is PLA2", because in a venom a
peptide fits dozens of isoforms (read: `SharedPeptidePolicy` exists precisely because most signal sits on
shared peptides, SharedPeptidePolicy.cs:6) and the database of their species is thin. Ana's bottleneck is the
curation: deciding who is who, one UniProt page at a time.

Why the family is the right unit, and where it is not. The family is the unit at which the peptide ambiguity
collapses (measured on the Bjararaca project: after the 2026-10-28 attribution rule only 0.18% of the quantified
signal stays shared between families, CLAUDE.md). It is also the unit that crosses the species line: a UniProt
family is defined over all taxa, and the reference panel the program already fetches holds every reviewed
member from every species (read: ReferencePanel.cs:80). That second property is the one nobody has used yet,
and it is the whole of Part 2. Where the family is NOT the right unit: short natural peptides (BPPs, 11
residues) that align to their own reversed decoys and set the identity floor (read: FamilyClusterScoring.cs:47);
multi-family precursors (BPP plus CNP in one chain, SCIENCE-GATE V03); and the serum-versus-venom question,
which homology cannot answer (V11).

What is unique here already (read): one extractor measures identified and de novo peptides from the RAW with
the full envelope (DeNovoXicExtractor.cs:489); every number carries provenance and a content hash
(ProjectStore.cs:260); the evidence engine assigns UniProt families with no name rule, learns markers, ranges
and architectures from the data and keeps a replayable recipe (EvidenceApply.cs:87); and the family attribution
of peptides is a target-decoy homology search joined onto the curated family numbers (FamilyEnvelopeQuant.cs:702).

What is weak (read, and in two places the brief is wrong):
1. The exhaustive clustering keeps every pair in memory (USearchTools.cs:61); Viperidae-size databases run
   fast-only in practice. Correction to the brief: Ana's file RECORDS the exhaustive method at 70%
   (measured: `ClusteringMethod` in ana_source.fcf), so it was run once; it is the re-run that is ruled out.
2. The de novo residual. Measured on the Bjararaca project manifest today: identified envelope 28.15% of TIC,
   de novo (reference-calibrated) 15.14%, recovered 43.30%, isotope-series diagnostic 59.60%. The manifest
   carries no family mapping (`FamilyMapping` is null, measured), so the per-family split of the de novo
   signal is not in the stored project; the brief's "larger than the largest family" cannot be regenerated from
   it and is treated as a pointer only.
3. A de novo peptide reaches a family ONLY through >= 80% gapless identity to a protein of the user's FASTA
   (FamilyEnvelopeQuant.cs:721, HomologyScorer.cs:217). The Bjararaca FASTA holds 584 entries (measured) for a
   species whose venom families have thousands of members in Viperidae (Ana's file: 31,777 targets, 330 species,
   75% Crotalus adamanteus, measured). Everything the species database lacks is "unattributed", and the program
   looks no further.
4. Scope is typed; the taxon is never read (the description carries OS= and OX= on all 31,777 targets of Ana's
   file, measured, and nothing in Core parses them except the word cutter at " OS=", read).
5. The evidence engine classifies proteins of a FASTA. It never sees the quantitation, and the quantitation never
   sees the evidence: the two halves of the product do not talk (read, absent).

Code map. CODE-MAP.md sections 1 to 5 give the FASTA-to-family and RAW-to-family paths with file:line, what is
solid (hashes, provenance, all-or-nothing fetches, deterministic ordering) and what is rigid (the O(N^2) edge
memo; the single 80% gate; the user's FASTA as the only universe of attribution; the classifier's deafness to
the data). Surprises: the E16 "neighbour must itself be in scope" rule is in the code (EvidenceClassifier.cs:99)
but not in SCIENTIFIC_METHOD.md's tier list; `FamilyMapping` is null on the talk project; the identity families
of Ana's file are exhaustive, contrary to what the roadmap's X3 wording suggests.

## Part 2. The bet: quantify the venom against the family world, not against the species database

One sentence. Every de novo peptide the species database cannot place is searched, with the same target-decoy
homology engine, against the reviewed family panel the program already downloads from UniProt (and, new, the
unreviewed entries of the sample's clade for the families found), and is credited to the FAMILY of its best
hit. The family becomes the unit that transcends the database, which is the family idea taken to its end.

What changes for the person. Today the result says "43% of the ion current is explained, 28% identified and
15% de novo, and here is the de novo part your database could not place". With this, the result says, per
family: (a) identified in your database, (b) de novo matched to your database, (c) de novo matched only to
the family world (the database gap of that family), (d) still unexplained. And the program proposes the next
move from (c): "SVMP carries N% of its signal from peptides absent from your database: the families to
sequence next are X, Y". That is new information, not a nicer screen.

The flow, step by step, and what the program does alone:
1. Open the project (sepr2, RAW, de novo, FASTA) as today.
2. Build and Families as today. New, automatic: the program reads OS=/OX= from the headers (present on all
   targets of Ana's file, measured) and names the taxon and clade. It then lists the UniProt keywords of the
   reviewed proteins in the file with counts and proposes the scope ("Toxin: 1,092 reviewed in scope"); the
   person accepts or edits. No table of words in the code: the keywords come from the fetch (EvidenceFetcher.cs:33).
3. Fetch the family world: the reviewed panel (exists, ReferencePanelFetcher.FetchAsync) plus one new query per
   family, `(taxonomy_id:<clade>) AND (family:"<family>")` without the reviewed filter, with sequences, all or
   nothing, release recorded, stored in `EvidenceProvenance` beside the panel. Public source checked today:
   rest.uniprot.org answers HTTP 200, release 2026_03, 290 reviewed M12B entries announced (measured).
   UniRef (200), InterPro (200) and NCBI E-utilities (200) are reachable too (measured) but not needed.
4. Quantify as today. New, automatic: the de novo peptides left unattributed by the database search
   (FamilyEnvelopeQuant.cs:524) are searched against the family world with `HomologyMapper.Search` at the same
   80% gate, with the panel's REVERSED sequences as decoys (the program already writes decoys with
   `SequenceLabels.DecoyPrefix` and the mapper competes target against decoy, HomologyMapper.cs:191). A hit
   assigns the hit's UniProt family (the panel carries it, PanelEntry.Evidence.Family), never a protein.
5. Results: the four-part bar per family, and the "database gap" ranking that suggests what to sequence.

Where it asks the person: only the scope (step 2, proposed) and the split choices the evidence window already
asks. Everything else is automatic and replayable: the family world is stored with its release and hashes,
exactly as the panel is now.

LLM: not needed. The one place it could help is translating a non-UniProt header into a family guess, which is
a name rule and is forbidden (rule 2); the program instead leaves such proteins where the evidence engine
leaves them today. Nothing in the numbers is lost without an LLM.

Walk-through on Ana's Viperidae file (estimated, not run): the family world for her 31,777 targets is the
reviewed panel (fetched already in e16-ana-searched.fcf) plus Viperidae unreviewed members per family. A
Bothrops jararaca sample quantified against it: an SVMP tag absent from the B. jararaca entries but 85%
identical to a Crotalus adamanteus SVMP (75% of her file) is credited to the SVMP family. The result for Ana:
the SVMP share includes what her database never contained, and the gap column says how much.

Walk-through outside venoms (estimated): a milk or a plant sample with a poor species database. Families such
as caseins or seed storage proteins have reviewed members across species; the same engine credits a de novo
tag to the family of its cross-species hit. It generalises because nothing venom-specific is in the rule.

Why new, prior art searched (hard stop allowed a PubMed search only): PepExplorer maps de novo tags to the
proteins of the SAME database by homology (Leprevost et al. 2014, Mol Cell Proteomics, PMID 24878498; the
program's mapper is a port of it, read); PEAKS SPIDER does the same inside PEAKS; snake venomics workflows
BLAST de novo peptides by hand against NCBI (Calvete, J Proteomics, cited from memory, unverified). I found no
tool that attributes unassigned de novo ion current to UniProt FAMILIES through a target-decoy search against
a reviewed cross-species panel and reports the database gap per family with provenance. PubMed IDs returned
today for the closest queries (unverified, not read): 38990011, 33460477, 26828374, 35411045, 26334921.

What a paper could claim: "X% of the venom's ion current belongs to family F, of which Y% is carried by
peptides absent from the species database", with a target-decoy error estimate per family, from one reproducible
project file. Today the claim stops at the database.

Strongest case against: a 7 to 15 residue tag at 80% gapless identity to a protein of another species is
weak evidence of family for conserved motifs (a disintegrin loop, a PLA2 active site), so the family-world
attribution may inflate the big conserved families. The reversed-panel decoys measure that per family, which
is why they are part of the design and not an afterthought.

How it fits the code. Reuses: `HomologyDatabase.Build` over any (id, sequence) list (HomologyDatabase.cs:123),
`HomologyMapper.Search` and the decoy competition, `FamilyEnvelopeQuant.BuildFromPeptides` which takes resolver
functions (FamilyEnvelopeQuant.cs:420), `ReferencePanelFetcher` and `UniProtHttp` for the fetch, the
manifest's provenance map. Replaces: nothing. Does not exist yet: the clade fetch query, the reversed-panel
decoy set, the second attribution pass and its four-part result row, the gap ranking, the scope proposal from
keyword counts. Needs more than an addition only in the manifest (`FamilyMapping` grows a second provenance
block) and in `FamilyEnvelopeQuantRow` (two new columns).

The one-day experiment, with the number fixed now: take scientific_results.fqproj (31,710 de novo rows,
measured) and e16-ana-searched.fcf as the family world (panel sequences plus the 31,777 targets, with their
reversed copies as decoys); run the existing mapper at 80% over the de novo sequences unattributed by the
Bjararaca database; PASS if at least 20% of the unattributed de novo reference-calibrated envelope lands on a
target family with a decoy rate at or below 5% of the landed envelope; KILL if under 5% lands or the decoy rate
exceeds 20%. Between the two, the identity gate is the knob to study before building.

## Part 3. Runners-up

1. Data-driven family coherence: use the identified peptides to test the grouping (a peptide identical in two
   identity families is evidence they are one), filling the hole the decoy score cannot see (FamilyClusterScoring.cs:47).
2. Scope from the data: propose the scope words from the UniProt keyword counts of the file instead of typing them.
3. Evidence-aware identity: feed the evidence engine's family of each protein into the single-linkage merge as a
   veto, so two UniProt families never chain through a shared domain.

## Part 4. What I would cut to make room

The thirteen de novo study files and the talk-figure producers (about 4,000 lines of Core, read by header
only) belong in the planned Reports tool (X9), not in the installed product; and the exhaustive all-pairs memo
should stream edges rather than hold N^2 of them before any new work touches clustering.

## Part 5. Questions only Paulo or Ana can answer (with my pick)

1. Is a family-level claim for a peptide absent from the species database acceptable to Ana? My pick: yes, with
   the decoy rate printed beside it.
2. Clade for the family world: Viperidae or Serpentes? My pick: Viperidae, the panel adds the rest.
3. Keep the 80% gate for the cross-species pass or study it first? My pick: study it in the one-day experiment.
4. Should the scope be proposed from keyword counts or stay typed? My pick: proposed, Ana confirms.
5. Does the gap ranking ("sequence family X next") belong in the product or in the paper only? My pick: product.
