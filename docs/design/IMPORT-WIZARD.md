# Import your family list

Z12p2a design proposal. These are mock screens, not an implemented importer.
Entry: File > Import family panel. The existing panel editor and its editing,
Pfam checking, template export and Undo contracts remain available.
FQKimi approved Z12p2a and Paulo received the screens (desk M53). Desk M50
authorised the functional build against Z12p1 while review remarks arrive.

## Purpose and boundaries

A person with an ordinary consensus workbook can see their sheet, choose its
columns, resolve ambiguous rows and confirm evidence matches before importing a
family panel. A Decision column is optional: the default is Quantify. Names in
another language and every original column remain stored as source text. Import
does not assign proteins, change their groups or recompute quantitation. The next
action is the Panel checklist in Group by evidence, where protein choices are
reviewed and applied separately.

All work through Summary is a detached draft. Back retains choices. Cancel
discards the draft without changing the project or workbook. Import is the sole
publication point and creates one existing panel Undo step. File > Save persists
the imported panel with the project. No network requests are made by this wizard.

## Frame and navigation

Use the application's native Fluent controls and current theme. Surface, text,
border, selection and button colours come from CanvasBrush, SurfaceBrush,
SubtleBrush, EdgeBrush, TextBrush, MutedBrush, AccentBrush and AccentInkBrush.
Typography uses PageTitle, SectionTitle, BodyText and CaptionText. Padding uses
PanelPadding, SectionPadding and ControlPadding. No new colour literals.

At 1366 x 768 and 1920 x 1080, a 220 DIP step rail stays on the left, with a
24 DIP gutter and a flexible content region. The minimum window is 1120 x 720.
The content has one heading, one short explanation line, then controls and data.
Help contains detailed instructions. Large tables scroll inside their own region;
the heading and footer never scroll away. Column names and example values wrap,
and full source text is available on focus and in tooltips. The right pane has
no fixed height that could hide the footer. Native focus and hover states remain.

The rail reads: 1 Welcome; 2 File and sheet; 3 Columns; 4 Special rows;
5 Evidence matches; 6 Summary; 7 Done. It announces the current step by number
and name and marks completed steps with the word Done. Completed steps can be
revisited with Back or by their rail button. Future steps are disabled until the
current page validates. No forward navigation silently accepts a suggestion.

Back and Cancel are quiet buttons, and the forward action is the only primary
button. Pages 1 to 5 use Next. Summary uses Import, naming the actual commit.
Done uses Open family checklist and Close; Back returns to a read-only summary.
Done never presents Cancel as an implied rollback. Undo is an explicit quiet
action on Done and remains available through the project's existing Undo command.
Welcome has Back disabled. A busy read disables forward navigation and allows
Cancel. Escape invokes Cancel before import, Close after import. F1 opens Help.

## Pages and exact authored words

The following copy is the proposed complete default page copy. Table values and
counts in the mocks are labelled fictitious data. Real source text is user data.

### 1. Welcome

Heading: **Import your family list**

Explanation: "Bring a spreadsheet of abbreviations and family names; review every choice before import."

Three compact information groups use labels and fragments:

| Label | Content |
|---|---|
| You need | Excel workbook (.xlsx); Abbreviation column; Family name column |
| Kept with your list | Other-language names; Notes and original rows |
| Imported into | This project's family panel |
| Next, after import | Review protein choices in Panel checklist |

Quiet actions: **Try with the example**, **Save a template**, **Edit current panel**.
The example opens File and sheet with Site's shipped fictitious workbook and an
Example badge. Save a template uses Opus's Z12p2d implementation. Edit current
panel opens the retained editor as a separate transaction after this draft closes.
Without the shipped example/template dependencies, the corresponding action is
disabled with "The example is not available." / "The template is not available."
No open project: entry is disabled with "Open a project before importing a family list."

### 2. File and sheet

Heading: **Choose your spreadsheet**

Explanation: "Choose a file and sheet, then check the preview against your spreadsheet."

Labels: **Workbook**, **Sheet**, **Header row**, **Spreadsheet preview**.
Quiet actions: **Browse**, **Use another header row**. Empty drop area:
"Drop an Excel workbook here". The preview shows actual row numbers, column
letters, the detected header row, source cell text and visible cell fills. Titles,
merged text, accents, embedded newlines and the filter row remain visible as
source data. A small **Source colours** label identifies fills as spreadsheet data,
not review status. Colour is never the only indication of row meaning.

Next requires a readable workbook, selected sheet and unambiguous header row.
The header detector proposes a row; it never guesses between equal candidates.
With two candidates: "Choose the row containing the column names."
Reading: "Reading spreadsheet...". No progress percentage is invented.
Failures: "This workbook could not be read. Choose another file." or a plain
reader diagnostic such as "Row 8, Name: this cell has no saved value."
File/sheet changes preserve earlier mappings only when their header identities
still exist; dependent row decisions and suggestions are invalidated visibly.
For a workbook with additional sheets, **Other sheets** offers **Ignore the other
sheets** (default) or **Keep as source notes**. Show the affected sheet names before
acceptance. Retained text carries sheet/row/column provenance; it is never parsed
as subclass rules, evidence links or measurements. The original workbook remains
unchanged in either case. Do not infer which tab holds families from its filename.

### 3. Columns

Heading: **Choose what each column means**

Explanation: "Check the suggested columns; an example from your sheet appears below each choice."

Role labels: **Abbreviation**, **Family name**, **Other-language name (optional)**,
**Notes (optional)**, **Pfam codes (optional)**, **Decision (optional)**.
Each role has an editable picker with actual source headers and an **Example**
value. Optional pickers include **Not used**. Header suggestions can recognise
general role/language labels, including Sigla, Nome (portugues) and Nome (ingles);
they never recognise a family, organism or sample name. Ambiguous suggestions
stay unselected. The required role pair must refer to distinct columns.
The language of the mapped second name is retained with the source header.
All unmapped columns are still preserved as named source notes.

Default decision control: **Quantify every family**. Mapping an optional Decision
column reveals an explicit mapping table: **Source value**, **Use as**,
**Merge into abbreviation**. Options are Quantify, Report under others,
Not a family, Merge into family. No unknown label is accepted silently.
Missing choice: "Choose an abbreviation column and a family name column."
Duplicate role: "Choose a different column for each name and abbreviation."
Incomplete decision mapping: "Choose how to use each decision value."

With a mapped Decision column, **Review decision values** opens a subview of
Columns headed **Review spreadsheet decisions**. Its one explanation is "Check
each proposed action and its source rows, then confirm the mapping." Columns are
**Source value**, **Source rows**, **Use as (proposed)** and **Merge into abbreviation**.
The core may propose meanings for general decision words, including their
accented forms, and a merge target only when its abbreviation matches one unique
imported row. Each proposal is changeable. An unfamiliar or ambiguous value
stays **Choose an action**. Conditional wording is retained verbatim even when
Quantify is proposed; no scientific condition becomes an executable rule.
**Confirm these actions** explicitly accepts the reviewed mappings as a batch;
**Back to columns** preserves the draft and does not accept it. Mapping a free-text
label once applies to its listed rows only, and the full source text survives.
Thirteen labels use a scrolling table rather than thirteen confirmation dialogs.

### 4. Special rows

Heading: **Review special rows**

Explanation: "Resolve each row marked Needs a choice before continuing."

Left table: **Row**, **Abbreviation**, **What needs review**, **Status**.
Right editor always shows the entire original row and the relevant choice.

| Case | Exact prompt and controls | Next condition |
|---|---|---|
| Several names | "Row 16 lists several families."; **One family per name**, **One group reported under others**; editable proposed names and abbreviations; **Use this choice** | Explicit choice and unique, nonblank resulting abbreviations/names |
| Repeated abbreviation | "Rows 4 and 15 use AB."; **New abbreviation for row 15**, suggested value AB-2; **Keep row 4 and skip row 15**; **Use this choice** | Explicit rename or skip; resulting keys unique |
| Empty row | "Row 17 is empty."; default **Skip empty row**; **Show source row** | Visible automatic skip, reversible by returning to mapping if data were missed |
| Missing required value | "Row 9 needs a family name."; editable missing value or **Skip this row** | Explicit correction or skip |

Several names are not split from punctuation alone. The core proposes a case and
the person confirms its interpretation; they may mark any row as several names
or keep it as one. No final family is silently discarded. Decorative/empty rows
are counted separately and their source row numbers remain inspectable.
The persistent summary counts Needs a choice, Ready and Skipped. Back preserves
all explicit choices. Changes to name/key mappings invalidate affected choices.

### 5. Evidence matches

Heading: **Match your list to stored evidence**

Explanation: "Confirm or skip the suggestions for each family; nothing is matched for you."

Left table: **Abbreviation**, **Family name**, **Choice** (Needs a choice,
Confirmed, Skipped). Right table: **Use**, **Stored evidence**, **Type**,
**Proteins**, **Reviewed**. Types are UniProt family, Keyword and Pfam domain.
Counts are distinct database entries for each suggestion, never abundances; they
must not be summed across overlapping suggestions. A details tooltip gives the
matching source text and count denominator supplied by Z12p4.
Actions: **Use selected matches**, **Skip this family**. The table supports
editing/removing accepted matches and reviewing a stored match's full text.
No match: "No stored evidence matches this name." with **Skip this family**.
No evidence: "Fetch evidence in Curate families to see matching suggestions."
and a deliberate **Continue without matches** action that records every family
as skipped. Fetching itself belongs outside this wizard. The later checklist
shows that these families still need matching; it never implies membership.
Changing a family name invalidates its unconfirmed suggestions and asks for
review. Accepted evidence links are persisted only on Import, via Z12p4's API.

### 6. Summary

Heading: **Review your family list**

Explanation: "Check what will be created; nothing has changed yet."

Labels: **Panel name**, **Families to create**, **Rows split**,
**Abbreviations renamed**, **Rows skipped**, **Evidence matches**,
**Stored source**, **Current panel**. Panel name is editable and required.
The final table shows Abbreviation, Family name, Other-language name, Decision,
Matches and Notes. Notes open the complete retained source fields.
Current panel says **None** or **Replace [current panel name]** and reports its
family count. Import is an explicit replacement, never an implicit merge.
Footer status: **Ready to import** or **[count] rows need a choice**.
Import is enabled only if all required choices are resolved and core validation
succeeds. Any remaining problem links back to the relevant row/control.
The project identity and initial panel fingerprint are checked immediately before
publication. Changed project: "The project changed. Review the import again."
Read-only/failed validation leaves the draft open and project untouched.

### 7. Done

Heading: **Your family list is imported**

Explanation: "Open the family checklist to review which proteins belong in each family."

Read-only facts: **[count] families imported**, **Panel: [name]**,
**Workbook unchanged**, **Protein grouping unchanged**, **Project not saved yet**.
Actions: **Undo import**, **Open family checklist**, **Close**, **Back**.
Undo uses the same single panel snapshot as the existing project Undo command,
restores prior panel/currentness and transitions to Summary with **Import undone**.
It is disabled if another edit has since become the current Undo step, with
"Use the project's Undo command to review later changes first."
Back shows the committed summary read-only and never imports twice.
Open family checklist closes the wizard and requests Fetch's Panel checklist tab
in Group by evidence on the same captured project. If that project has closed or
changed, show "Open the family checklist from Curate families." and do not target
another project. No automatic grouping or Apply takes place.

## Accessibility and state retention

Use AutomationProperties.Name and LabeledBy on every picker, grid and editable
value. A heading is focused/announced on page change: "Step 3 of 7, Columns".
Validation is a live-region status with a linked focus target, never colour only.
Tab order is step rail, page heading/actions, controls in reading order, Help,
Back, forward action, Cancel/Close. Arrow keys navigate native grids and pickers.
Enter activates the enabled forward action outside multiline/table edit mode;
Escape cancels safely. Buttons expose their full purpose when disabled.
Selection, mappings, language, edited names, row choices, selected evidence links,
panel name and scroll positions belong to one detached draft and survive Back.
Changing upstream data clears only invalid dependent choices and explains which
page needs review. No animation is required; no animated page transition.

## Dependency seams and decisions before implementation

| Owner | Required contract |
|---|---|
| Opus Z12p1 | Read-only workbook/sheet snapshot, source hash, header candidates and diagnostics with row/column, suggested role mapping, optional Decision, original text, cell fill and merged-range metadata; detached import/validation/replay |
| Opus Z12p3 | Explicit several-name choices and repeated-key/skip edits retained for replay; no UI punctuation parser |
| Fetch Z12p4 | Suggestions/denominators from stored evidence, explicit accepted/skipped links, validation/persistence; no automatic acceptance |
| Fetch Z12n1/n2 | Open the checklist on the captured project; no change to Home's existing layout or hand-family dialog |
| Site Z12p2c | Shipped fictitious workbook, Help screenshots/walkthrough, matching example row numbers and choices |
| Opus Z12p2d | Save-template action and default import round trip |

Site's corrected writer is `FamilyQuant.Examples.ExampleFamilyList.Write(path,
titleRow)`, with `DefaultFileName` and `FamilySheetName` (desk M54). The functional
wizard and its tests use this organism-neutral writer. The test-only design
pages here remain detached static stand-ins.

The desk approved the source-colour boundary in M50: workbook fills are source
data, not theme colours; yellow/gold fills are shown neutrally with the original
colour named in cell details. Mock fills use tokens. The functional preview reads
the stored fill and never turns its colour into a decision or membership rule.

## Mock fixture and review packet

The test-only stub uses general, labelled fictitious data, never Ana's workbook.
There are 13 nonempty source rows: 11 unique single families (rows 4 to 14),
one repeated AB at row 15, and one two-name row 16. Row 17 is empty.
Resolve the repeated key to AB-2 and split row 16 into two named families:
the Summary has 14 families, one row split, one abbreviation renamed and one
empty row skipped. Evidence counts in the mock are invented display facts.
The mock is a static proposal, including disabled/unchecked states. It never
reads a workbook, produces a panel or mutates a project.

Render with `FamilyQuant.Tests.exe --test-import-wizard-mocks <new-output-dir>`.
It uses ScreenHarness.Save and real app resources for all seven pages at both
sizes/themes. Names are `dark/1366x768/01-welcome.png` through `07-done.png`,
and the same under light and 1920x1080. The build packet must later replace these
with captures of the functional wizard and per-page tests.

Four additional mock states cover a neutral six-sheet workbook with 41 rows,
15 source columns and a header at row 1; a 13-label Decision mapping table;
the repeated-abbreviation editor; and the empty-row detail. They are suffixed
`-wide-sheet`, `-decisions`, `-duplicate` and `-empty` under pages 02, 03 and 04.
The richer workbook stand-in uses invented family labels, general Portuguese
decision words, fictitious group names, two Name (PFxxxxx) entries in a cell and
long notes. It copies structural quirks only, not any real row or family list.
The review packet contains 44 PNGs. Real workbooks and their contents are not
included in tests, commits or reviewer artifacts.

## Z12p2b acceptance checks

1. File > Import family panel enters Welcome; old panel editing/export/Pfam
   functions still exist. Try example and template use the owners' shipped APIs.
2. A title/filter/header fixture with accents, merges, line breaks and cell fills
   renders with actual row numbers; wrong/unreadable files have plain diagnostics.
3. Required and optional mapping suggestions can all be changed; examples update;
   optional Decision defaults to Quantify and every source field survives replay.
4. Multi-name/duplicate/missing-value cases block Next until explicit choices;
   skips are visible; Back preserves every choice and changing upstream mappings
   invalidates only affected choices.
5. No evidence match is accepted by default; confirm/skip persists exactly; no
   network fetch is hidden in the wizard and count denominators stay explicit.
6. Summary counts match final output; replacement is visible; Cancel leaves the
   entire project byte-identical; changed-project commit is refused atomically.
7. Import changes only the panel transaction, once. Save/reopen retains choices,
   one Undo restores the prior project, Done opens the correct checklist, and
   repeated forward/Back clicks cannot reapply the import.
8. Native keyboard/screen-reader labels, bounded layout and one primary action
   are checked on every page at both sizes/themes, including long text, empty
   suggestions and a validation error. Focused checks join DefaultSuite.

## Functional implementation notes

The window is `FamilyListImportWindow`, with typed state in
`FamilyListImportViewModel` and the guarded `FamilyListImportTransaction`.
`FamilyListStoredEvidenceMatches` is a thin adapter over Fetch's
`PanelEvidenceMatching`; it adds no second name matcher. New choices start
unchecked. On re-import, `CarryConfirmedMatches` retains records by stable key,
the page says Kept from previous panel, and skipping new matching preserves those
values. Explicitly confirmed replacements win. Removed-key notices are shown
in Summary. All source and project guards repeat before publication.

The Columns page opens a separate, bounded decision-review subview when a mapped
free-text column needs confirmation. The header proposals, examples and optional
colour categories remain on the column form. Ambiguous punctuation can be kept
as one family explicitly. Source notes open in a read-only report, keeping the
Summary table compact. Raw merged text is drawn once at its leading cell, while
cell details retain the complete raw value and merged range.

The actual example writer and `PanelImportTemplate.Write` have production callers
in the wizard; the File menu also offers Save family list template. The retained
panel editor is reached through Edit current panel. Done opens Group by evidence
with a checklist request that selects the tab after the person's explicit Update
proposal; it performs no implicit update or Apply. H1 was reassigned to FQFloat
by desk M56; Home's packaging step waits for the named integrated commit.

Native checks use `--test-family-list-import <output-dir>` and the shipped neutral
example, and join DefaultSuite. They include actual `.fqproj` save/reopen,
source/project mutation refusal, in-flight cancellation, re-import keep/replace,
one Undo, template defaults, source-colour handling and the deferred checklist
handoff. No real workbook is used in tests or review captures.

Best case remaining: review accepts the functional wizard and the integrated
release checks pass in the next release cycle. Realistic remaining: independent
polish, the real-file acceptance walk and the desk's full suite/integration, then
packaging; external review and release timing remain outside the builder's control.
