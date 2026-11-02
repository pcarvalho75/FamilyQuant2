# X13 documentation and test organization

The working guide was 50,461 bytes before this step and is about 10 KB after separating history.
`docs/HISTORY.md` retains the complete prior guide, including historical measurements, limitations,
commands, invariants, vendor guidance and implementation notes. Table rows keep their column order.
A lexical-fact multiset check confirms nothing from the original text was lost; the archive is
reflowed to at most 120 characters per line, with ordinary hyphens and corrected active links.
The exact original is retained under `D:/FamilyQuant-data/out-assay/x13-docs/CLAUDE-before.md`.

`SCIENTIFIC_METHOD.md` now documents the existing unannotated-protein route accurately:
a qualifying reviewed neighbour must itself be in user scope, observed domains must establish
compatibility, and no conflicting learned marker may support another family. Missing conditions
leave the protein unassigned. Query coverage counts actually paired query letters, with gaps
earning no credit. No scientific code or threshold changed in this step.

Release notes add an explicitly unpublished 2.0 development account without changing historical
version headings. The historical scientific revision report moves to
`docs/history/SCIENTIFIC_REVISION_REPORT.md`; its prose/facts are retained and reflowed.
M46 keeps `SCIENTIFIC_TALK_SCRIPT.md` at its exact original path and content as a protected input.
The catalog links the current guide, working history and relocated historical report.

## Exact test splits

The native syntax utility moves complete methods, never edits their bodies or assertions.
It compares every method's token kind/text sequence and verifies all remaining fields, nested
types and their initialization order. No default-run call or dispatch branch changes.

| Original file | Methods preserved | Original lines | Remaining lines | Largest resulting partial |
|---|---:|---:|---:|---:|
| Program.Charts.cs | 18 | 1,643 | 212 | 733 |
| Program.DeNovoScreen.cs | 5 | 1,289 | 770 | 770 |
| Program.UiLayout.cs | 26 | 1,320 | 257 | 638 |

All 49 method bodies/signatures are unchanged. The 13 resulting source files match their
recorded hashes after restoring the parked work; every file is under 1,000 lines.
Check-call sites are also unchanged: Charts 108 to 108, DeNovoScreen 180 to 180 and UiLayout
88 to 88. This is preservation of the original checks; newer dependency commits separately
add their own checks to the overall suite.
The utility's source, original files and `split-receipt.json` are retained under
`D:/FamilyQuant-data/out-assay/x13-split-tool` and `x13-splits-after-sync`.
No protected presentation producer was split or edited.

## Scope decisions and receipts

M44 removes `dotnet format` from X13. The earlier `--verify-no-changes` audit was read-only and
exited 2; despite its diagnostic wording, it made no source edits. No formatting command that
applies changes was run. Global cosmetic changes would cross protected ownership and stale
implementation hashes across lanes, so they are deliberately outside this revised done-check.

The 27 implementation files mapped by the AS declaration register retain their exact hashes;
these test-file moves do not invalidate them. N6-AS-2 separately refreshed the approved register
after V3 changed ThreeWayWindow bytes, at commit `355ccdca5371c30e869507280f15cea6969e7311`.
No row is closed from a documentation search or a build. The ledger's independent review and
unproven rows remain governed by N6.

Edited guides, method text, release notes, history and the relocated report have no line over
300 characters and no new long-dash characters. Local Markdown links in those documents and
the catalog resolve. The protected talk script is excluded from reflow and remains byte-unchanged.
Release build is clean and the existing focused curation-layout command passes 108/0/0.
The READY quick suite is 2,951 passed, zero failed and 17 skipped at the current dependency
tree. Stored-project golden is 4/0/0 with all four files byte-identical. No full suite, fresh
RAW extraction, install or new biological accuracy claim is made by this housekeeping step.
