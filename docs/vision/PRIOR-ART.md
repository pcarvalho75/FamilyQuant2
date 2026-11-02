# Prior art for the family world

Written 2026-11-01 by FQReview for roadmap step B2, and corrected the same day after FQKimi's citation check. This is the text to commit as `docs/vision/PRIOR-ART.md`. FQReview does not commit. Nothing written for this project says new or first. The safe wording is in the last section.

The question, taken from roadmap W8 and `docs/vision/VISION.md`. A de novo peptide the species database cannot place is searched against reviewed members of the family from all species, plus the unreviewed entries of the sample's clade. A hit is credited to the family, never to a protein. Each family then has four quantities: identified, de novo matched inside the user's database, de novo matched only in that wider set (the database gap), and still unexplained. The program ranks families by the gap. Every claim carries a decoy rate and a known-answer right rate.

## How this note was made

Searches were PubMed (NCBI E-utilities) and Europe PMC on 2026-11-01, plus the open full texts named below. PMC HTML and Europe PMC XML were used where they answered. Unpaywall rejected the DOI lookups that were tried (HTTP 422), so it did not recover the closed papers.

Read here means the text below was on the page in front of me. An abstract is marked as an abstract. A paper that did not open is listed and is not used for a claim.

`docs/vision/VISION.md` had already named five PubMed ids as unverified hits from a PubMed-only search, and it had cited a Calvete venomics paper from memory. Those ids were looked up. They are not the closest method papers. The Calvete full texts did not open.

## Papers read

### PepExplorer (full text)

Leprevost FV, Valente RH, Lima DB, Perales J, Melani R, Yates JR 3rd, Barbosa VC, Junqueira M, Carvalho PC. PepExplorer: a similarity-driven tool for analyzing de novo sequencing results. Mol Cell Proteomics. 2014;13(9):2480-2489. doi:10.1074/mcp.M113.037002. PMID 24878498. PMC4159663. Read from the open PMC page on 2026-11-01.

What it does. It reads de novo output (parsers for PepNovo, pNovo+, and PEAKS, plus a generic list) and aligns those sequences to a target-decoy protein database with Gotoh's Smith-Waterman algorithm (affine gaps). A radial basis function neural network ranks alignments by alignment score, de novo score, precursor charge, and peptide length. A cutoff on the decoy count gives a global false-discovery rate. The report is a list of homologous proteins: identifier, length, coverage, sequence count, alignment count (described as the count closest to a spectral count), and the alignment of the de novo string to the database string. The user supplies the database, "usually obtained from phylogenetically related species."

The controlled test used a published Pyrococcus furiosus set. On the unmodified database the converted table showed, for proteins with at least two spectral counts at 1% FDR: ProLuCID 585 (average coverage 0.16), Mod-A 499 (0.16), PepExplorer 311 (0.17). The methods name three mutated databases (an insertion every 25, 20, or 15 residues, and a substitution every 15, 10, or 8). Their cell counts did not survive the page conversion, so they are not quoted. The discussion says the spectrum match loses identifications as those edits accumulate, and that the alignment keeps identifications while being the least sensitive of the three on the unmodified database.

The biological example is Bothrops jararaca plasma, not venom, searched against Reptilia plus Amphibia NCBI RefSeq downloaded June 2013 (302,287 sequences after adding 127 contaminant sequences and a reversed decoy of every entry). PEAKS Studio 6 supplied the tags (average local confidence at least 70, total local confidence at least 5). PepExplorer reported 3,862 alignments at 1% FDR, 1,333 peptides, 199 proteins, 86 protein groups. ProLuCID/SEPro on the same files reported 349 spectra, 83 peptides, 17 proteins, 12 protein groups, at 0% FDR after the final filters. The protein groups from ProLuCID were inside the PepExplorer list. The authors then name plasma protein families (transferrin, plasminogen, and others) from those identifications. PatternLab, which hosts PepExplorer, is where quantitative and differential analysis lives. PepExplorer itself does not compute an ion current.

What it leaves out. It assigns a protein, then a maximum-parsimony protein group. It does not read a UniProt family field, does not credit a family instead of a protein, and does not split ion current into "in the species database" and "only in a wider family set." High-scoring de novo sequences that fail the alignment are singled out as molecules the database does not contain. That is the opposite use of an unexplained tag: the paper keeps it off the protein list.

The current program already treats this paper as its alignment engine. `FamilyQuant.Core/Homology/HomologyMapper.cs` lines 12-20 say the mapper places de novo sequences onto the proteins of a family database and from there onto families, by the same two stages as PepExplorer (an identity prefilter, then PAM30MS on every window), taken from `ProgramEngine.AlignMAlign`. The header then lists the deliberate differences (bounded windows, deterministic order, ordinal decoy test, top hits by rank). That header was read in this checkout. It is our description of the port, not a second reading of the PepExplorer source.

### SPIDER (full text of the 2004 proceedings paper)

Han Y, Ma B, Zhang K. SPIDER: software for protein identification from sequence tags with de novo sequencing error. Proceedings of the 2004 IEEE Computational Systems Bioinformatics Conference (CSB 2004), pages 206-215. doi:10.1109/CSB.2004.1332434. PMID 16448014. Read from the proceedings PDF on 2026-11-01.

The journal version (J Bioinform Comput Biol. 2005;3(3):697-716, PMID 16108090) has the same title and the same opening. It was not opened. Citations of the algorithm below are to the 2004 paper that was read. Ma and Johnson 2012 cite the 2005 journal version as their reference 53.

What it does. De novo software emits sequence tags that are often only partly right. The common error is replacement of one segment by another of about the same mass. SPIDER aligns a tag to database peptides under four modes: exact, segment replacement, non-gapped homology, and homology. The homology mode allows a de novo error and a mutation at the same site, builds a candidate real peptide between the tag and the database peptide, and reports the database peptide that minimizes the combined errors. On their account, MS-BLAST, FASTS, and MS-Shotgun adjust ordinary homology search for short tags and for several tags at once, and do not model the segment-replacement error. They also say OpenSea models both error and mutation but not at the same site, limits a de novo error to length three, and uses a greedy alignment. Those three sentences are Han, Ma, and Zhang describing the other tools. The other tools' own papers were not opened, except where Ma and Johnson 2012 repeat the same distinction (below).

Their experiment: PEAKS 2.0 produced 144 tags from 144 ion-trap spectra of known proteins (bovine albumin, rat myosin light chain, rabbit phosphorylase, chicken ovalbumin). Each tag was searched in SwissProt (146,193 proteins; the true proteins are present) and in the human subset (28,926 proteins; the true proteins are absent and homologs are present). Success counts from their Table 1:

| Mode | SwissProt | Human subset |
|---|---:|---:|
| SPIDER exact | 35 | 13 |
| SPIDER segment | 90 | 36 |
| SPIDER non-gapped homology | 76 | 49 |
| SPIDER homology | 78 | 52 |
| MS-BLAST | 52 | 24 |

Segment match was strongest when the true peptide was in the database (90 of 144, which they call about 63%). Homology match was strongest when only a homolog was present (52 of 144). There is no decoy FDR in this experiment. The right protein was known in advance.

What it leaves out. The output is a peptide or a homologous peptide, and through that a protein. There is no family, no quantitation, no ion current, and no rank of missing sequence. A conserved motif landing in the wrong family is outside what this table can show: the human search asks only whether some homolog was found.

### De novo sequencing and homology searching (homology section)

Ma B, Johnson R. De novo sequencing and homology searching. Mol Cell Proteomics. 2012;11(2):O111.014902. doi:10.1074/mcp.O111.014902. PMID 22090170. PMC3277775. The tag-homology section and the worked examples were read from the open PMC page on 2026-11-01. The spectrum-graph and PEAKS algorithm pages were not read.

What it does. It is a tutorial. Ordinary BLAST and FASTA, retuned for short tags, ignore de novo errors, and those errors do not behave like mutations. CIDentify, a FASTA derivative, rescores mass-equivalent substitutions (Gly-Gly against Asn, and mismatched dipeptides of equal mass). GutenTag allows de novo errors and requires the database sequence to be exact. OpenSea and SPIDER allow both an error and an inexact database sequence. SPIDER also allows the error and the mutation to overlap, and it reconstructs a candidate real peptide. The worked examples are cross-species protein identification: beans against ESTs, Dunaliella, sugarbeet seed, Nepenthes pitcher fluid, macaque gel spots against human homologs, and one chemical-modification example (mouse liver, periodate, BLAST of a PepNovo tag that the database search missed). Ma and Johnson also report a simulation by Habermann and colleagues in which simulated tags, not experimental spectra, identified over 80% of proteins inside the mammalian subkingdom. That 80% is their report of a simulation.

What it leaves out. The tutorial stops at identifying the homologous protein or peptide. It does not quantify a family, does not separate an ion current, and does not propose which family to sequence next.

### PEAKS DB (abstract, plus one sentence)

Zhang J, Xin L, Shan B, Chen W, Xie M, Yuen D, Zhang W, Zhang Z, Lajoie GA, Ma B. PEAKS DB: de novo sequencing assisted database search for sensitive and accurate peptide identification. Mol Cell Proteomics. 2012;11(4):M111.010587. doi:10.1074/mcp.M111.010587. PMID 22186715. PMC3322562. The abstract was read from the open full-text XML. The scoring sections were not read. The XML contains no "SPIDER". The one homology sentence that was read says that homologous proteins receive no special treatment when the protein shortlist is built, so a large database may need a longer shortlist.

What it does. It puts de novo results into a database search so that peptide identification is more sensitive at a given false-discovery rate, and it proposes decoy fusion as a check on overconfident scores.

What it leaves out. This paper is a search of peptides that can be explained from the database the user searched. It is not the homology module. The PEAKS product's SPIDER search is the algorithm in the 2004 paper above. The PEAKS user manual was not read.

### Quantification in snake venomics (sections read, full text searched)

Reghu N, Nair BG, Vanuopadath M. Mass spectrometry-based quantification in snake venomics and antivenomics: current trends and future perspectives. Life (Basel). 2026;16(10):1636. doi:10.3390/life16101636. PMID 42942222. PMC13706370. Read on 2026-11-01: the abstract, the introduction, section 2 through Table 1, the opening of the label-free and label-based section, and the proteo-transcriptomics passage. The saved full text was then searched. It contains "de novo" five times, "homology" once, "toxin famil" six times, and "protein family" once. It contains neither "unassigned" nor "database gap".

What it does. Venom work already reports abundance at family level. The families they name are PLA2, SVMP, SVSP, and three-finger toxin. They say a family total mixes isoforms that differ in sequence and in pharmacology, and that shared peptides block a clean assignment to one isoform. Identification of crude venom "generally rely on sequence similarity-based homology-driven approaches" because public sequence collections are incomplete, and that incompleteness can over-represent the proteome. De novo sequencing together with the usual workflow is cited as giving a fuller protein list, and they still say it does not by itself make the abundance quantitative. Quantities in current use, as they list them, are spectral counting, emPAI, NSAF, Top3, iBAQ, iTRAQ, TMT, MS1 intensity, extracted ion chromatograms, targeted MS, and elemental MS of sulfur. For a species-specific transcriptome they say: unique peptides can support a protein or an isoform, and shared peptides should be reported at the protein family or the protein group. They cite Calvete and colleagues as the methodological review of this quantitation (their reference 11). That paper is the 2024 Mass Spectrometry Reviews article below, which did not open.

What it leaves out. The family percentage in this literature is the share of the venom assigned to a toxin family after identification. It is not a four-way split of one family's ion current, and it is not a second automatic pass of the tags the species database missed. The risk they stress is isoform mixing inside a family, and abundance that does not equal potency. They do not test the other risk: a short conserved tag landing in the wrong family.

### Homology-driven identification pipeline (author manuscript)

Junqueira M, Spirin V, Balbuena TS, Thomas H, Adzhubei I, Sunyaev S, Shevchenko A. Protein identification pipeline for the homology-driven proteomics. J Proteomics. 2008;71(3):346-356. doi:10.1016/j.jprot.2008.07.003. PMID 18639657. PMC2644553. The author-manuscript HTML was read on 2026-11-01. The byline above is the one on that page.

The abstract says homology-driven proteomics is a way to characterize proteomes of organisms with unsequenced genomes, and that the paper covers practical aspects of automated homology-driven identification by LC-MS/MS on a hybrid LTQ Orbitrap. The keywords name de novo sequencing and MS BLAST. The abstract does not name MASCOT or PepNovo, and it does not give the pipeline order. An earlier draft of this note attributed that order to the abstract. That sentence was wrong.

The full text describes a layered search: de novo interpretation is aimed at MS/MS spectra that would otherwise stay unmatched by conventional database searching. The methods use MASCOT against an MSDB database, PepNovo (PepNovo2MSB) for de novo interpretation of ion-trap spectra, and MS BLAST for the sequence-similarity search. The samples named in the methods include the insect Triatoma infestans and the Brazilian pine Araucaria angustifolia. One protein reported in the text matched a sequence from Oryza sativa and was not identified by the MASCOT search. No count from the paper is used. The pipeline identifies homologous proteins.

What it leaves out. It does not read a UniProt family field, does not credit a family instead of a protein, and does not split an ion current.

### NovoLign (full text)

Kleikamp HBC, van der Zwaan R, van Valderen R, van Ede JM, Pronk M, Schaasberg P, Allaart MT, van Loosdrecht MCM, Pabst M. NovoLign: metaproteomics by sequence alignment. ISME Communications. 2024;4:ycae121. doi:10.1093/ismeco/ycae121. PMID 39493671. PMC11530927. Read from the Europe PMC full-text XML on 2026-11-01.

What it does. It takes de novo sequences from a whole metaproteomics experiment (tested on PEAKS and DeepNovo), filters them (the PEAKS default is an average local confidence of 70; optional filters include mass error, peptide length, and peak intensity or area), and aligns them with DIAMOND to a generic reference database such as NCBI, UniProtKB, or GTDB. The stated defaults are 85% identity, 80% coverage, and a PAM70 matrix. Decoy sequences are made by scrambling the amino acids in front of the cleavage residue, and randomized sequences estimate the false-positive classifications. A lowest-common-ancestor step turns the alignments into lineages. The taxonomic composition is the frequency of those lineages after a frequency cutoff, written as a table and as bar graphs. Given a database-search result as well, `database_qc.py` compares the two at the ranks order, family, and genus, in four groups: de novo only, all de novo, database search only, and all database search. The matched sequences can also be collected into a reference database for a later search.

What it leaves out. The family in that comparison is a taxonomic rank, not a UniProt protein family. The quantities are peptide and spectrum sets, not an unidentified MS1 ion current. Peak intensity is an optional filter on which sequences enter the alignment. There is no four-way split of one protein family's ion current, and no per-family pair of a decoy rate and a known-answer right rate.

### pi-HelixNovo (full text)

Yang T, Ling T, Sun B, Liang Z, Xu F, Huang X, Xie L, He Y, Li L, He F, Wang Y, Chang C. Introducing pi-HelixNovo for practical large-scale de novo peptide sequencing. Briefings in Bioinformatics. 2024;25(2):bbae021. doi:10.1093/bib/bbae021. PMID 38340092. PMC10858680. Read from the Europe PMC full-text XML on 2026-11-01. The title uses the Greek letter pi. The same XML points at a correction, bbae134 (PMC10998637), which was not opened.

What it does. It is a Transformer de novo model that also encodes a complementary spectrum. On the gut metaproteome set MSV000082287, about 26.5 million peptide-spectrum matches came from database search and about 9.5 million MS2 spectra stayed unidentified. The model is then run on those unidentified spectra. Quality control compares the de novo peptides with a target database built from the corresponding genomes, a decoy database of common contaminants plus reversed entries of those genomes, and the database-search results. Taxonomic identification of the tryptic peptides uses Unipept.

What it leaves out. The product is a peptide and a taxon. It does not map a tag onto a cross-species UniProt protein family, does not split an ion current, and does not rank a database gap per protein family. The reversed entries estimate chance peptide matches.

### The program's mapper (code, this checkout)

`FamilyQuant.Core/Homology/HomologyMapper.cs` lines 12-37, read 2026-11-01. The mapper already turns a de novo tag into protein hits and then into families, and it already competes targets with decoys. `docs/vision/VISION.md`, read the same day, says the database of that search is the user's FASTA, at an 80% gapless identity gate, and that tags the user's FASTA cannot place stay unattributed. Those VISION line citations were not re-measured in the quantitation source during this step.

## Records read only as abstracts

These opened as Europe PMC abstracts on 2026-11-01. The full texts are not in PMC (`isOpenAccess` was N). They are included because `docs/vision/VISION.md` listed the first five as the unverified hits of its PubMed search, or because the abstract is the protocol note for a paper already read.

- PMID 38990011. A prospective study of the proteome of equine pre-implantation embryo. Reprod Domest Anim. 2024. doi:10.1111/rda.14663. Four pooled blastocysts. Identification used PEAKS, Proteome Discoverer, SearchGUI, and PepExplorer. This is an embryo proteome, not a method for family quantitation.
- PMID 33460477. Proteomic profile of pre-implantational ovine embryos produced in vivo. Reprod Domest Anim. 2021. doi:10.1111/rda.13897. Same four search tools on pooled embryos. Same distance from the question.
- PMID 26828374. Borges MH, Figueiredo SG, Leprevost FV, De Lima ME, Cordeiro Mdo N, Diniz MR, Moresco J, Carvalho PC, Yates JR, as the Europe PMC author string. Venomous extract protein profile of Brazilian tarantula Grammostola iheringi: searching for potential biotechnological applications. J Proteomics. 2016. doi:10.1016/j.jprot.2016.01.013. Abstract only. MudPIT of electrically stimulated venom. PepExplorer against phylogenetically close organisms reported 395 proteins, many of them predicted or putative, including proteases and CRISPs. An application of PepExplorer to a venom. The abstract does not describe an ion-current gap.
- PMID 35411045. Santos MDM, Lima DB, Fischer JSG, Clasen MA, Kurt LU, Camillo-Andrade AC, Monteiro LC, de Aquino PF, Neves-Ferreira AGC, Valente RH, Trugilho MRO, Brunoro GVF, Souza TACB, Santos RM, Batista M, Gozzo FC, Durán R, Yates JR, Barbosa VC, Carvalho PC, as the Europe PMC author string. Simple, efficient and thorough shotgun proteomic analysis with PatternLab V. Nat Protoc. 2022. doi:10.1038/s41596-022-00690-x. Abstract only. The abstract says PatternLab V covers database preparation, identification, statistical filtering, and labeled and label-free quantitation, and that "The PepExplorer module can even pinpoint de novo sequenced peptides not already present in the database." That sentence is the whole of what the abstract says about the gap. It points at peptides, not at a family ion current.
- PMID 26334921. Using PepExplorer to Filter and Organize De Novo Peptide Sequencing Results. Curr Protoc Bioinformatics. 2015. doi:10.1002/0471250953.bi1327s51. Abstract only. It restates the 2014 method: homolog proteins, target-decoy alignment, the same neural network, a user-chosen FDR, recommended when no genome sequence is available, with PatternLab used afterwards for quantitation. The author line was not in the abstract download.

## Named work that did not open

- Calvete JJ. Snake venomics. Strategy and applications. J Mass Spectrom. 2007. PMID 17621391. doi:10.1002/jms.1242. No PMC record. `docs/vision/VISION.md` cited a Calvete venomics paper from memory. This is the strategy paper the searches returned first. It was not read. No claim is taken from it.
- Calvete JJ, Lomonte B, Saviola AJ, Calderón Celis F, Ruiz Encinar J, as the Europe PMC author string. Quantification of snake venom proteomes by mass spectrometry: considerations and perspectives. Mass Spectrom Rev. 2024. PMID 37155340. doi:10.1002/mas.21850. Not open. This is reference 11 of Reghu, Nair, and Vanuopadath 2026. Their summary of it was read. The paper itself was not.
- Calvete JJ. Snake venomics, from low-resolution toxin-pattern recognition to toxin-resolved venom proteomes with absolute quantification. Expert Rev Proteomics. 2018. A CSIC repository URL returned a bot-check page (4,446 bytes of HTML), not the PDF. Not read.
- Shevchenko A, Sunyaev S, Loboda A, Shevchenko A, Bork P, Ens W, Standing KG. Charting the proteomes of organisms with unsequenced genomes by MALDI-quadrupole time-of-flight mass spectrometry and BLAST homology searching. Anal Chem. 2001;73(9):1917-1926. PMID 11354471. doi:10.1021/ac0013709. No PMC record. Not read. Han, Ma, and Zhang 2004 and Ma and Johnson 2012 both describe it as MS-BLAST: BLAST retuned so that several imperfect de novo tags can identify a homologous protein. That is second-hand.
- Han, Ma, and Zhang, J Bioinform Comput Biol 2005 (PMID 16108090). Not opened. The 2004 proceedings paper was read instead.
- OpenSea (Searle and colleagues, Anal Chem 2004, as cited by Ma and Johnson), CIDentify, FASTS, GutenTag, and MS-Homology. Not opened. Anything said about them above is Han 2004 or Ma and Johnson 2012 speaking.

## What is already published

These are established, in the papers that were read.

1. De novo tags are aligned to proteins from a related species when the organism's own sequence is missing. PepExplorer does this with a target-decoy database and a global FDR. SPIDER does this with an explicit model of de novo segment errors and of mutations. Ma and Johnson review a decade of the same idea and point it at unsequenced organisms.
2. The alignment engine in this program is that idea. The mapper header says so.
3. Venom composition is already reported as the abundance of toxin families (PLA2, SVMP, SVSP, three-finger toxin, and the rest). Shared peptides are already supposed to be reported at family or protein-group level when an isoform cannot be distinguished. The numbers come from chromatography and from the usual mass-spectrometry abundance measures, after the proteins have been identified.
4. Incomplete public databases, and homology used to cross that gap, are already the stated reason for de novo in venomics.
5. A global decoy false-discovery rate on a homology mapping is already in PepExplorer. SPIDER's published check was a known-answer count on 144 spectra, not a decoy rate.
6. A whole de novo experiment can already be aligned, with scrambled decoys, to a generic sequence database, turned into a taxonomic composition, and compared with the database search at the ranks order, family, and genus (NovoLign). De novo sequencing of spectra a gut database search left unidentified, checked against target and reversed-decoy sequences and passed to a taxonomic peptide tool, is also published (pi-HelixNovo). The Junqueira pipeline already runs de novo and MS BLAST on spectra a database search leaves unmatched.

## What was not found in the papers that were read

Stated as an absence from this reading, not as a proof that no such paper exists. The closed Calvete reviews are the obvious place a matching sentence could still be sitting. NovoLign and pi-HelixNovo were read for the correction. NovoLign's family is a taxonomic rank and its comparison is a count of peptides. pi-HelixNovo stops at the peptide and the taxon. Neither is the list below.

- Crediting the hit to the UniProt family and refusing to name a protein.
- One table, from one run of the program, with four quantities per family: identified, de novo inside the user's database, de novo only in the wider family set, and still unexplained.
- A rank of families to sequence next, taken from that third quantity.
- A decoy rate and a known-answer right rate stored on every family row. PepExplorer's FDR is global. SPIDER's known-answer test is a single peptide-identification count. Neither is a per-family pair, and a reversed decoy still cannot show a conserved tag landing in the wrong real family. Reghu and colleagues describe the isoform-mixing problem inside a family. They do not describe the wrong-family problem.
- Doing the above automatically from the project file (taxon and scope read from the headers, clade fetch stored with the file, replayed offline). PepExplorer and SPIDER search a database the user supplies.

## Wording

Do not write that the family world is new, and do not write that it is first.

A sentence that matches the papers read: homology mapping of de novo tags onto related proteins is published, taxonomic composition from a de novo alignment is published, and so is family-level abundance of identified venom toxins. The roadmap's remaining bet is to run the mapping in the program against a stored cross-species family set, to credit the UniProt family, and to show the database gap of each family beside its error. That bet was not found, as one method, in the papers read. B1 still has to show that the right family comes back.

## Checker

FQKimi checked the first draft on 2026-11-01 (`D:/FamilyQuant-data/reviews/B2-citations.md`). This revision reads the Junqueira author manuscript, NovoLign, and pi-HelixNovo, and it drops the sentence that put the MASCOT, PepNovo, and MS BLAST order into the Junqueira abstract. The closed Calvete papers are still unread. If one of them already prints the four-way family gap, this note is wrong on that point and the wording gets narrower again.
