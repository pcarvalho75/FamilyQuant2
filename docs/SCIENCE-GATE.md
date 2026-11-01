# E14 science gate: the evidence rule against the frozen Python classes

Status: draft verdicts for review by FQReview. Built on fq/opus, to be rerun on master once E6 and C4 land.

## What was compared

- The program: E5's proposal (`EvidenceClassifier.Propose`, scope words `Toxin` and `venom gland`) with E7's coherence flags attached (`CoherenceCheck.Flag`), on Ana's file with E2's evidence, the E4 reference panel and the stored E3 search (`D:/FamilyQuant-data/out-opus/e4-ana-panel-neighbours.fcf`). A protein counts as placed only when it is assigned and not flagged, that is, ticked by default.
- Python: the frozen classes `classes_all.tsv` (exp8, 2,754 proteins) and `classes.tsv` (exp5, the SVMP and disintegrin set). Python's labels are mapped back to UniProt families by exp8's own label table. That table lives in the checker only, as the answer key; the rule in `Evidence/` has no family, name or domain.
- The checker: `FamilyQuant.Tests.exe --science-gate-list`. It writes every disagreement with its verdict ID, the program's tier, reason, flags, nearest reviewed neighbour (and whether that neighbour carries the scope words), the E10 left-out entry, and Python's own reason. The rows are in `docs/science-gate-disagreements.csv`; the checker fails if any row has no verdict.
- E6's architecture split is not merged yet. For the 256 reviewed SVMP and disintegrin entries the checker emulates E6's default: a member of the SVMP family whose only Pfam domain is the disintegrin domain is offered apart, and Ana's default (A4) is to accept that split.

## Result

| Measure | Count |
|---|---:|
| Disagreements, all proteins | 308 |
| Reviewed proteins with a Python class that agree | 1,030 of 1,089 |
| The 256 reviewed SVMP and disintegrin entries of `classes.tsv`, consistent with E6's default split | 250 (SVMP 173 of 173, disintegrin 77 of 83) |
| Of those 256, inconsistent | 6, all verdict V02 |

The done-check "all 256 reviewed venom entries consistent" is **not met**: 6 reviewed short disintegrins have no Pfam domain call and stay in the SVMP family. Verdict V02 explains why and what general change would reach them.

## Verdicts

| ID | Rows | What differs | Who is right | Action |
|---|---:|---|---|---|
| V01 | 47 | Python Disintegrin, program SVMP family; the only domain is the disintegrin domain | Both; a user choice | Resolved by E6's split when the user keeps disintegrins apart |
| V02 | 16 | Python Disintegrin by name; no Pfam domain call; program SVMP family or no family | Python on biology, by a name rule the program may not use | Finding F3 for E6; left for the user |
| V03 | 52 | The source names several families; Python took the first | Program | None; the user picks (E5 contract) |
| V04 | 68 | Program proposes Python's family but a coherence flag unticks it | Mostly Python on membership; the flag is a deliberate review mark | None; the user ticks; see the TrEMBL helix note |
| V05 | 10 | Short fragments with no domain call; Python took a 70% neighbour | Undecidable on this evidence | Left flagged, as ruled for U3TAL0 and T2HQJ8 |
| V06 | 9 | Unannotated SVMP fragments with no qualifying neighbour; Python used the domain or the name | Program withholds correctly | 5 are in the E10 left-out list; 4 only the words route reaches |
| V07 | 20 | Program puts database contaminants in a family | Python | Finding F1 for E5 |
| V08 | 81 | Program puts unannotated proteins in a family through a neighbour outside the scope | Python, in 80 of 81 | Finding F2 for E5 |
| V09 | 4 | Program puts short ADAM-named entries in the SVMP family by a learned marker | Python by a name rule; the program cannot tell | Listed for the user; risk table trigger |
| V10 | 1 | A 40-residue SVMP fragment called disintegrin family by its neighbour | Both defensible | None |

### V01. Disintegrin-only architecture inside the SVMP family (47)

UniProt files the medium and short disintegrins under "venom metalloproteinase (M12B) family, P-II subfamily" because they are released by proteolysis from P-II SVMP precursors (Fox and Serrano 2008). Python classed them by domain composition (only the disintegrin domain), the program by the curated family. Both are correct descriptions of the same proteins. Whether disintegrins form their own family in Ana's result is the A4 question, default yes. E6 offers exactly this group (one Pfam combination) as a split, and with the split accepted all 47 agree with Python.

### V02. Mature disintegrins with no domain call (16: 6 reviewed, 10 unreviewed)

The 6 reviewed entries are leucogastin A and B, ocellatin, eristicophin, eristostatin and morulustatin: 37 to 51 residues with 8 or 9 cysteines, curated as P-II/P-IIa SVMP products. Pfam calls no disintegrin domain on these short chains, so E6's domain split cannot see them, and they stay in the SVMP family. The 10 unreviewed are 26 and 27 residue fragments named "Disintegrin": 4 are placed in the SVMP family, and 6 are left unassigned because their nearest reviewed protein ties across families, so E5 refuses it (`neighbour_refused`).

Biology: these are disintegrins (Python is right about what they are), but Python decided by the word "disintegrin" in the name, which rule 2 forbids. Finding F3 for E6: a member with no domain call could inherit the architecture group of its nearest reviewed neighbour, or a length and cysteine subclass could be learned from the reviewed members of each group (the exp3 idea the roadmap gives to E6 and E7). Either is general and would bring the 6 reviewed entries in line. Until then they are inconsistent with Python and must be shown to the user.

### V03. Several family memberships (52: 10 reviewed)

The bradykinin-potentiating peptide and natriuretic peptide precursors encode several BPPs and a C-type natriuretic peptide in one chain (Murayama et al. 1997), and UniProt states a family for each region. The crotamine, ficolin and one snaclec entry carry two memberships the same way. Python kept the first family listed, an arbitrary choice. E5 leaves the protein unassigned with `multiple_source_families` so the user decides, as agreed in M1. Program right; no change.

### V04. Proposed, then unticked by a coherence flag (68: 1 reviewed)

E5 proposes Python's family; E7 flags the protein as outside what that family's reviewed members show, so it starts unticked. By flag: snaclec 18 (16 longer than any reviewed member), ficolin lectin 13 (only 16 complete reviewed members, so the ranges are narrow), SVMP 13 (8 P-III entries 1 to 28 residues longer than the longest reviewed SVMP, 2 with 42 to 43 cysteines, 3 with one predicted helix), 5'-nucleotidase 8 (all one predicted helix), PLA2 5 (predicted helices), NGF-beta 4, peptidase S1 2, PDE 2, LAAO 1, PLB 1, Kunitz 1.

Verdict: most of these are probably members, and the flag is a review mark, not a refusal; the user ticks them in C4. One limit worth stating: the transmembrane counts of unreviewed entries are predictions, and a signal peptide is often predicted as a helix (Käll et al. 2004), so a single predicted helix on a secreted toxin such as a PLA2 is weak evidence. The rule still reports it, because the same check is what separates ADAMs from SVMPs (E7: 49 of 67 ADAM-named proteins flagged). No change; the message says which fact was outside which range.

### V05. Short fragments with no domain call (10), including U3TAL0 and T2HQJ8

U3TAL0 (serine protease fragment, 25 residues, 92% to a reviewed peptidase S1 member) and T2HQJ8 (56 residues, 87.5%) are the two SVSP cases ruled to stay flagged. The others are two snaclec fragments of 38 and 53 residues, three SVMP fragments of 75 to 98, a 5'-nucleotidase of 113, a vespryn of 70, and Q9PS29, 21 residues named "nerve growth factor" that Python put in svVEGF at 100% to a reviewed VEGF. None carries a scope word or a domain call, so E5 cannot establish compatible domains and flags `domain_compatibility`. Over 21 to 56 residues, identity alone does not decide the family, as Q9PS29 shows (its name and its nearest neighbour disagree). Verdict: left flagged for the user, as ruled.

### V06. Unannotated SVMP fragments without a qualifying neighbour (9)

Crotalus simus GP-I to GP-IV metalloproteinase fragments (188 to 192 residues) and five short "metalloprotease" fragments of 67 to 99 residues. None carries the scope words and none has a reviewed neighbour at 70% over 70% of its length. Python classed five by the Reprolysin domain alone and four by name. A Reprolysin domain alone also fits an ADAM (SVMPs evolved from an ADAM ancestor, Sanz and Calvete 2016), so domain-only assignment over-reaches, and the name rule is forbidden. The program withholds; the five with the learned marker appear in the E10 left-out list with the reason, the other four only through the words route. Program right.

### V07. Database contaminants put in a family (20)

All 20 are `contaminant_` entries (bovine trypsin, chymotrypsinogens, coagulation factors, plasminogen, complement factor B) that E5 puts in the peptidase S1 family through panel neighbours from other mammals. The family is correct as biology, but they are laboratory contaminants, not proteins of the sample's venom, and Python never classed them. Finding F1 for E5: treat entries carrying the database's contaminant label like decoys, never candidates, as E10 already does. This is the database's own labelling convention, not an exception list.

### V08. Unannotated proteins joined through a neighbour outside the scope (81)

E5's last tier assigns a protein with no scope words when a reviewed neighbour is at 70% or more with compatible domains. With the E4 panel, that neighbour can be any reviewed member of the family from any taxon, and in 80 of these 81 rows it carries no scope word itself. What comes in: 36 snake neurotrophin-3 fragments into NGF-beta, amine and polyamine oxidases into the LAAO family, phosphodiesterases and PLA2s whose nearest reviewed protein is a non-venom mammalian enzyme, PDGF-C and PDGF-D, unannotated CRISPs, and others. The nearest neighbours are listed per row in the CSV. Their families are right as biology, but nothing says they are venom proteins, which is what Ana's families are for. exp6 avoided this by construction: its references were reviewed toxins only.

Finding F2 for E5: an unannotated protein should join a family only through a neighbour that itself carries the scope words. That keeps the one row here whose neighbour is in scope, serotriflin (sp|P0CB15, a CRISP of snake serum, whose neighbour Q8JI38 carries the scope words at 79.2%), and drops the 80. Some of the 80 may still be venom proteins (for example "Cysteine-rich venom protein" entries that UniProt left without the keyword); they would then appear in the E10 left-out list for the user rather than being added silently.

### V09. Short ADAM-named entries in the SVMP family (4)

A0A6G5ZVS7, A0A6G5ZUB3, A0A6G5ZVK4 and A0AAW1B982, 135 to 460 residues, named "Disintegrin and metalloproteinase domain-containing protein", with venom annotation and a Reprolysin or disintegrin domain, are put in the SVMP family by the learned marker. These are the E7 remainder: their length, cysteines, helices and domains all lie inside the SVMP ranges, so a general rule cannot separate them, and Python did so only by the name. Per the risk table: shown to the user as conflicts, no name rule added, Paulo told. They are not reviewed, so the risk trigger (reviewed ADAMs left in SVMP) is not met.

### V10. A 40-residue fragment (1)

A7X500 (SVMP-Cau4 fragment) is 85% identical over its whole length to a reviewed protein of the disintegrin family, so E5 places it there; Python placed it in SVMP by name. A 40-residue piece of the disintegrin region of an SVMP precursor fits both. No change.

## Scope question for Paulo and Ana (no rule added)

The calmodulin family holds 125 rows of the E10 left-out list. A few calmodulin-family proteins carry the scope words (venom-gland expression), so E5 learns the family's EF-hand marker, and every EF-hand protein outside the family is then listed. Whether calmodulin-like proteins belong in a venom review at all is a choice of scope words, not something a rule should decide. Question: should the scope stay `Toxin` and `venom gland`, which brings in housekeeping proteins expressed in the gland, or should Ana narrow it?

## Findings for other owners

- F1 (E5): never assign `contaminant_` entries (V07, 20 rows).
- F2 (E5): the unannotated tier joins only through an in-scope neighbour (V08, 80 rows).
- F3 (E6): reach domainless mature disintegrins, by the neighbour's architecture or a learned length and cysteine subclass (V02, 6 reviewed of the 256).

## Errors found in Python, recorded as such

- Names decided the class for 16 disintegrins (V02), 4 SVMP fragments (V06) and the ADAM exclusions (V09); rule 2 forbids this.
- For proteins in several families, Python kept the first family listed (V03).
- Domain alone decided SVMP for 5 unannotated fragments (V06), which an ADAM would also satisfy.
- Q9PS29, named nerve growth factor, was put in svVEGF on 21 residues (V05).

## References

- Fox JW, Serrano SM (2008). Insights into and speculations about snake venom metalloproteinase (SVMP) synthesis, folding and disulfide bond formation and their contribution to venom complexity. FEBS J 275:3016-30. doi:10.1111/j.1742-4658.2008.06466.x (PubMed 18479462)
- Murayama N et al. (1997). Cloning and sequence analysis of a Bothrops jararaca cDNA encoding a precursor of seven bradykinin-potentiating peptides and a C-type natriuretic peptide. PNAS 94:1189-93. doi:10.1073/pnas.94.4.1189 (PubMed 9037028)
- Käll L, Krogh A, Sonnhammer EL (2004). A combined transmembrane topology and signal peptide prediction method. J Mol Biol 338:1027-36. doi:10.1016/j.jmb.2004.03.016 (PubMed 15111065)
- Sanz L, Calvete JJ (2016). Insights into the evolution of a snake venom multi-gene family from the genomic organization of Echis ocellatus SVMP genes. Toxins 8:216. doi:10.3390/toxins8070216 (PubMed 27420095)
