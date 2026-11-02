# N6j: five remaining producer observations

`AS-figure-gaps` covers the five remaining frozen observations in
`FamilyQuant/PresentationFigures.cs`. The protected source is only read and
executed. No deck, real project or real presentation input is read or written.
The native fixture restricts every export path to `D:/FamilyQuant-data/out-assay`.

Two rows are the original prefix literals consumed by
`ReferenceComponentSummary.FromCuration`. An identical fixture runs against the
frozen and current assemblies: empty curation, individual target/decoy/contaminant
identifiers, prefix case boundaries, an internal prefix, a wrapped prefix, and a
mixed target/decoy family. All eleven native summary values agree. No result is
calculated outside the program.

Three rows were classified as `shortcut` by the frozen scanner because it saw
`g.Key.SourceDisplayName`, `g.Key.ConfidenceField` and `g.Key.MinimumConfidence`.
They are manifest data fields in the consolidation chart. The receipt preserves
that frozen inventory kind but makes no keyboard shortcut claim.

The same linked fixture exports the actual native chart at baseline, then with
each of those three fields changed separately. It reads the exported chart's
native glyph drawing and requires the changed field value to appear and its old
value to disappear. Frozen/current drawings agree for baseline and each mutation.
Every observation binds its original source offset, ID, physical line and class;
a matching field in another consumer cannot supply the anchor.

Validation on the initial head:

- Clean Release solution and dedicated frozen-host builds.
- Receipt guards red 3/3/0, then green 6/0/0.
- Native positives 9/0/0 twice; negative 2/7/0, rejecting all five observation
  proofs and both kind guards.
- Proof logs, actual values, native summary/render values and all eight exported
  PNGs are byte-identical across the two runs.

The final register records the combined tree after the M72 through M75 master
syncs, at merge `11521b99c62000083e489a7ebe2f4479acc46ce5`.
That sync required refreshing the four existing FQAssay Spectrum registers for
their changed implementation mappings. The two intentional C13 tooltip changes
remain OPEN and on Paulo's wording list. Other lanes' registers are not edited.
No quick or golden run is required for this tests/receipts/docs change under M64.

Final packets are `D:/FamilyQuant-data/out-assay/n6j-final-native-1` and
`n6j-final-native-2`; the register uses run 2. Both pass 9/0/0 with all twelve
native-value/log/PNG files byte-identical. The final negative is 2/7/0. Focused
producer, Spectrum, row-isolation and Build guards pass 6/0/0, 20/0/0, 4/0/0 and
5/0/0 respectively. Release solution and dedicated frozen-host builds are clean.
The native ledger check passes after the final import. All five N6j rows are
VERIFIED; the refreshed Spectrum cohort has 183 VERIFIED and twelve OPEN wording
rows. The USearch text deferral under M71 remains unchanged.

Producer: `FamilyQuant.Tests.exe --ledger-assay-figure-gaps <frozen-root>
<fresh-output> <date> [--negative-control]`. Output must remain under out-assay.
Build the dedicated `ReferenceGoldenHost.csproj` in Release first.
