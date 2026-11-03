# Curation homology-hit cache

The family attribution screen keeps its compact peptide-to-protein hits in the
program's local `familyquant_fullxic_cache/curation-homology` folder. These files
are disposable: they are not part of the project file and are never written beside
the acquisition inputs. A missing, unreadable, truncated or checksum-damaged cache
causes the usual fresh search. A completed entry is published through a temporary
file and atomic rename; failed writes do not discard the fresh attribution.

The key includes the stored quant rows, the protein-set fingerprint and ordered
identifier/sequence pairs, all homology search options, the identified best-score
rule and the cache/search method version. Family IDs and annotations are not in the
protein key: regrouping reuses the hits and joins them to the current families.
After a read or search, the input fingerprint is checked again before a result is
reused or a cache entry is published. Search and cache stages are identified in the
operation progress line.

Entries preserve the ordered protein identifiers, both peptide sides, each hit's
protein indices and localization offsets. On reuse the program validates the digest,
query list, indices and offsets, and reconstructs the homology database. This keeps
position studies and family attribution on the same hit set. The cache version must
be advanced if the search or localization semantics change.

The test-only `--measure-curation-hit-cache <project-copy.fqproj> <new-output-folder>`
command opens the given copy twice with a private cache, records attribution times,
compares the composition CSV and stored run-share bytes, and checks the project hash
before and after. It does not run an extraction or read acquisition files.
