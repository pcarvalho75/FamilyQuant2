# C14 finishing evidence

C14a's native client matrix covers 16 views, both themes, WPF root scales
100, 125 and 150 percent, and minimum/large client sizes. Results uses a
populated synthetic fixture. The 192 PNGs in
`D:/FamilyQuant-data/out-look/C14/matrix-layout-1` match the second run byte for
byte. Every encoded image has the expected pixel dimensions and DPI. Every
recorded window/content DPI matches the requested scale. No proof window takes
activation or keyboard focus; Windows monitor settings remain unchanged.

C14b's first matrix exposed truncated De novo table headers. Shared table
presentation now measures each native header and reserves its readable width
plus sort space as a minimum. Columns scroll horizontally instead of shrinking
the header below that minimum. Existing maximum-width constraints and source
values stay intact; the decorator restores its previous minimum on detach.

The matrix also exposed a test-geometry defect: bounds included text that WPF had
already clipped inside a column or scroll region. The regression failed at
145/1/0; using WPF's actual render clips passes 146/0/0. The existing oversized
caption and explicit clipping checks still execute. The two complete matrices
now accept all 192 layouts, with no source rows changed by the layout check.

Native Tab candidates are retained per frame. Actual Tab traversal, live
keyboard-focus visibility and native Windows chrome are not exercised by these
off-screen captures. C14c and Paulo's final look sign-off remain separate.

```text
FamilyQuant.Tests.exe --screens <fresh-output> --screen-finish --screen-shell-status
FamilyQuant.Tests.exe --test-curation-layout
```

These are UI fixtures and stored-file checks, not fresh RAW validation or a
scientific result delivered to a collaborator.
