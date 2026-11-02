# B2 citation check

2026-11-01 21:23:20 -0800
Result: CHECKED WITH CORRECTIONS

The central distinction in PRIOR-ART.md is supported: the checked literature publishes cross-species homology identification of de novo tags and family-level abundance after identification, but I did not find the proposed single method that assigns unidentified de novo ion current to UniProt protein families and reports its four-part database gap. This is a bounded search result, not proof of novelty.

The note is not yet a complete prior-art record. It should correct one evidence label and add two close 2024 papers, NovoLign and pi-HelixNovo. Neither omission overturns the safe wording, but NovoLign is close enough that leaving it out weakens the search.

## Papers read in the note

- **PepExplorer, PMID 24878498, PMC4159663: verified from the [full text](https://pmc.ncbi.nlm.nih.gov/articles/PMC4159663/).** The paper supports the note's parsers, user-supplied target-decoy database, Gotoh/Smith-Waterman alignment, RBF neural-network inputs, global FDR, protein report fields and maximum-parsimony grouping. The PFU table contains ProLuCID 585 (0.16), Mod-A 499 (0.16), PepExplorer 311 (0.17), and the three mutation spacings are as stated. The Reptilia plus Amphibia database has 302,287 sequences after 127 contaminants and reversed decoys. The plasma results are exactly 3,862 alignments, 1,333 peptides, 199 proteins and 86 groups at 1% FDR, versus 349 spectra, 83 peptides, 17 proteins and 12 groups for ProLuCID/SEPro. PEAKS thresholds also match. PatternLab supplies downstream quantitative analysis; PepExplorer itself reports homologous proteins, not family ion current. High-scoring rejected de novo sequences are indeed discussed as possible novel molecules.
- **SPIDER 2004, PMID 16448014: bibliographic record and algorithm verified.** The [PubMed record](https://pubmed.ncbi.nlm.nih.gov/16448014/) and IEEE record exist with the stated title, DOI and pages. The proceedings' indexed text confirms all ten Table 1 counts: 35/13, 90/36, 76/49, 78/52 and 52/24. The database sizes 146,193 and 28,926, the 144 tags, four match modes, overlapping de novo error/mutation model, reconstructed intermediate peptide, and absence of a decoy experiment are supported. The direct proceedings PDF request returned the IEEE HTML viewer rather than the paper, so I did not independently read that PDF end to end. I did read the accessible [2005 journal full text](https://cs.uwaterloo.ca/~binma/pub/spider.pdf), PMID 16108090, which contains the same algorithm, experiment and table. The note's distinctions are accurate: the output is a peptide/homologous protein, not a protein-family quantity, and the human-subset test asks whether a homolog was found rather than whether the right family was recovered.
- **Ma and Johnson 2012, PMID 22090170, PMC3277775: verified from the [full text](https://pmc.ncbi.nlm.nih.gov/articles/PMC3277775/?report=xml).** The homology section supports the CIDentify, GutenTag, OpenSea and SPIDER distinctions, including mass-equivalent rescoring and SPIDER's overlapping error/mutation model. The worked examples include beans/ESTs, Dunaliella, sugarbeet, Nepenthes, macaque/human homologs and the mouse-liver periodate example. Habermann's over-80% result is explicitly a simulation with computer-generated tags within the mammalian subkingdom, not experimental spectra. No family quantitation or family gap is presented.
- **PEAKS DB, PMID 22186715, PMC3322562: verified from the [abstract and full text](https://pmc.ncbi.nlm.nih.gov/articles/PMC3322562/).** The abstract supports de novo assisted database search and decoy fusion. The Protein Shortlisting section contains the exact sentence that homologous proteins receive no special treatment and a larger database may require a longer shortlist. This is not the SPIDER homology module and does not provide a family gap.
- **Reghu, Nair and Vanuopadath 2026, PMID 42942222, PMC13706370: verified from the [MDPI full text](https://www.mdpi.com/2075-1729/16/10/1636) and Europe PMC XML.** The paper names PLA2, SVMP, SVSP and three-finger toxin family measurements, warns that these combine isoforms and that shared peptides obscure isoform assignment, and lists the quantitation approaches stated in the note. It explicitly recommends reporting shared peptides at protein-family or protein-group level in the proteo-transcriptomics passage. Case-insensitive counts in the full XML extraction match the note exactly: `de novo` 5, `homology` 1, `toxin famil` 6, `protein family` 1, `unassigned` 0 and `database gap` 0. It does not describe the proposed second pass or wrong-family error.
- **The program mapper citation is accurate.** `FamilyQuant.Core/Homology/HomologyMapper.cs:12-37` says the mapper places de novo sequences onto proteins of a family database and then families, names the two-stage PepExplorer-derived method, and lists the deliberate port differences. This confirms only the project's own description of the port, not independent provenance of the algorithm.

## Records the note calls abstract-only

The records exist and their abstracts support the limited claims made:

- [PMID 38990011](https://pubmed.ncbi.nlm.nih.gov/38990011/): equine pre-implantation embryo proteome, four pooled blastocysts, PEAKS/Proteome Discoverer/SearchGUI/PepExplorer. Not a family-quantitation method.
- [PMID 33460477](https://pubmed.ncbi.nlm.nih.gov/33460477/): pooled ovine embryos and the same four search tools. Equally distant from the proposed method.
- [PMID 26828374](https://pubmed.ncbi.nlm.nih.gov/26828374/): Grammostola iheringi MudPIT, PepExplorer against phylogenetically close organisms, 395 proteins, including predicted/putative proteins, proteases and CRISPs. The abstract has no ion-current gap.
- [PMID 35411045](https://pubmed.ncbi.nlm.nih.gov/35411045/): PatternLab V covers database preparation, identification, filtering and labeled/label-free quantitation. The quoted PepExplorer sentence is verbatim and concerns peptides absent from the database, not family ion current.
- [PMID 26334921](https://pubmed.ncbi.nlm.nih.gov/26334921/): the protocol abstract restates homolog proteins, target-decoy alignment, RBF inputs, user-selected FDR and downstream PatternLab quantitation.
- **Correction for [PMID 18639657](https://pmc.ncbi.nlm.nih.gov/articles/PMC2644553/).** PRIOR-ART.md line 90 says the abstract describes MASCOT followed by PepNovo/MS-BLAST on unmatched spectra for insects and plants. The actual abstract has only three general sentences and does not contain those details. The open full text does support the claim in its Introduction, Methods, Results and Figure 5. Change the evidence label from abstract to full text; the scientific description itself is correct.

## Named work and reachability

- **Calvete 2007, [PMID 17621391](https://pubmed.ncbi.nlm.nih.gov/17621391/):** abstract read. It supports assignment of isolated venom toxins to known protein families after chromatography and MS, not the proposed second-pass gap. Full text not reached.
- **Calvete et al. 2024, [PMID 37155340](https://pubmed.ncbi.nlm.nih.gov/37155340/):** abstract read. It covers quantitative methods and their misuse in non-model species, plus elemental/molecular MS. Full text not reached, so it remains a possible place for additional relevant detail as the note says.
- **Calvete 2018, [PMID 30005583](https://pubmed.ncbi.nlm.nih.gov/30005583/):** abstract read. It covers absolute intact-protein quantification and top-down venomics. Full text not reached.
- **Shevchenko et al. 2001, [PMID 11354471](https://www.ebi.ac.uk/europepmc/webservices/rest/search?query=DOI:10.1021/ac0013709&resultType=core&format=json):** metadata and abstract read. The abstract confirms the MS-BLAST protocol for imperfect automated de novo predictions in unsequenced organisms. Full text not reached. The note correctly labels its more detailed description as second-hand from Han and Ma.
- **SPIDER 2005 journal version:** full text reached at Waterloo, as described above.
- **OpenSea, [PMID 15080731](https://www.ebi.ac.uk/europepmc/webservices/rest/search?query=DOI:10.1021/ac035258x&resultType=core&format=json):** abstract read. It confirms mass-based alignment of ambiguous de novo sequences to database sequences for proteins, variants and modifications. Full text not reached. The length-three/no-overlap/greedy details are supported only second-hand by Han, exactly as the note states.
- **CIDentify, Taylor and Johnson 1997, [PMID 9204580](https://www.ebi.ac.uk/europepmc/webservices/rest/search?query=TITLE:%22Sequence%20database%20searches%20via%20de%20novo%20peptide%20sequencing%20by%20tandem%20mass%20spectrometry%22&resultType=core&format=json):** abstract read. It confirms modified FASTA searching over ambiguous de novo candidates. Full text not reached; detailed rescoring comes from Ma and PepExplorer.
- **FASTS, Mackey et al. 2002, [PMID 12096132](https://www.ebi.ac.uk/europepmc/webservices/rest/search?query=DOI:10.1074/mcp.m100004-mcp200&resultType=core&format=json):** abstract read. It confirms multiple short peptide sequences and homolog identification. Full text not reached.
- **GutenTag, [PMID 14640709, PMC2915448](https://pmc.ncbi.nlm.nih.gov/articles/PMC2915448/):** full text read. It infers tags and finds exact database tag/flanking-mass matches, with partial matches for variants and modifications. This supports Ma's distinction from homology-tolerant SPIDER.
- **MS-Homology/Protein Prospector, [PMID 15937296](https://www.ebi.ac.uk/europepmc/webservices/rest/search?query=DOI:10.1074/mcp.D500002-MCP200&resultType=core&format=json):** metadata and abstract read. The open PDF request returned HTTP 403. Ma supplies the MS-Shotgun renaming and algorithm description used by the note.

The six abstract-only application/protocol records above were not reached in full. No claim in the note requires more than their abstracts. The PEAKS user manual was not read, consistent with the note.

## Close prior art missed by the note

### NovoLign

Kleikamp HBC et al. NovoLign: metaproteomics by sequence alignment. ISME Communications. 2024;4:ycae121. doi:10.1093/ismeco/ycae121. PMID 39493671. [Full text](https://pmc.ncbi.nlm.nih.gov/articles/PMC11530927/).

This is the closest omission. NovoLign aligns complete sets of de novo sequences and scrambled decoys against generic cross-species reference databases using DIAMOND. It evaluates correct, wrong-taxonomy and decoy matches, uses lowest-common-ancestor taxonomic assignment, and reports taxonomic-family composition from sequence counts. Its `database_qc.py` compares `DN_only`, `DN_all`, `DB_only` and `DB_all` by order, family and genus, and it specifically processes unmatched high-quality spectra to identify missed taxonomies/proteins and assess database coverage.

That is already a decoy-controlled, cross-species de novo database-gap analysis at taxonomic-family level. It is not the FamilyQuant proposal: the family is taxonomic rather than a UniProt protein family, abundance is sequence/spectral count rather than unidentified MS1 ion current, and there is no four-part identified/de-novo-in-database/de-novo-only-in-family-world/unexplained partition or per-family pair of decoy and known-answer right rates. NovoLign can accept intensity/area as an input filter, but that is not ion-current attribution. The note should cite it and make these distinctions explicitly.

### pi-HelixNovo

Yang T et al. Introducing pi-HelixNovo for practical large-scale de novo peptide sequencing. Briefings in Bioinformatics. 2024;25(2):bbae021. doi:10.1093/bib/bbae021. PMID 38340092. [Full text](https://pmc.ncbi.nlm.nih.gov/articles/PMC10858680/).

Its gut-metaproteome workflow de novo sequences spectra left unidentified by database search, validates the resulting peptides with `T\D\DS` against target genomes plus reversed decoys while excluding peptides already found by database search, and evaluates taxonomic resolution with Unipept. This is close to residual-spectrum target-decoy validation. It does not perform the proposed cross-species family-world homology attribution, protein-family assignment, ion-current split or per-family database-gap ranking. It belongs in a complete nearby-methods paragraph, but does not change the main conclusion.

## Search result and recommended disposition

Targeted searches combined de novo with protein family, UniProt family, Pfam, target-decoy, ion current, toxin family, database gap, database coverage, per-family and family-level quantification. They found the two papers above and the already cited homology tools, but no exact match to the proposed protein-family ion-current method.

Before B2 is treated as complete, the owner should:

1. Add NovoLign and pi-HelixNovo with the boundaries above, especially NovoLign's taxonomic-family database-coverage analysis.
2. Change PMID 18639657 from abstract-only to full-text support.
3. Keep the existing restrained wording. Do not upgrade it to new or first. The defensible statement remains that the exact combined protein-family, ion-current, four-part gap and per-family error reporting method was not found in this bounded reading.
