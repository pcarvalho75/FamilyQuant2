# Workflow names

One name for each step and screen, in the order Home shows them (roadmap version 12, Z4a, from
Ana's suggestions of 11-05). The code copy is `FamilyQuant.Core/WorkflowNames.cs`; every sidebar
entry, menu item, window title, message and Help page uses these names.

## The names table

| Step | Name | Under the name | Where the user meets it |
|---|---|---|---|
| | Home | | Sidebar (Alt+0), View > Home, the status bar |
| 1 | Build families | Group by sequence identity, USearch | Home, sidebar, File > Build families (Ctrl+N), the Build screen |
| 2 | Curate families | a Group by evidence, b Group by words | Home, sidebar, View > Curate families, the families screen with its two parts |
| 3 | Quantify | Full XIC from the RAW files | Home, sidebar, the Quantify screen and its window |
| 4 | Results | Family shares, figures and report | Home, sidebar, the Results screen |
| 5 | De novo review (optional) | Inspect de novo calls beside PatternLab | Home, sidebar, the De novo review screen and its Help |

The flow in one line, as Help draws it:

`Home > 1 Build families > 2 Curate families (a Group by evidence, b Group by words) > 3 Quantify > 4 Results; 5 De novo review (optional)`

Menus follow the standard Windows order: File, Edit, View, Tools, Help. Tools holds only the
extra tools (Spectrum comparison, Clustering diagnostic, Reannotate SEPro); the five steps live on
Home and the sidebar.

## Retired names

| Old name | Replaced by |
|---|---|
| Start page, Start (the Home button) | Home |
| New from database, Build from database | Build families |
| Families or Open project as a step name, View > Families | Curate families |
| Compute full XIC | Quantify |
| Analytics / report | Results |
| De novo evidence, the bare step name De novo | De novo review |
| Analysis menu, Analysis > | Tools (extra tools only); the steps by their own names |

"Full XIC" stays the name of the measurement and its column; only "Compute full XIC" as the name
of a command or screen is retired. Lower-case "de novo evidence" in a sentence is ordinary prose.
"De novo" as an adjective (De novo sequence, De novo source) stays; the bare step name is caught
where a sentence uses it as a place, such as "open De novo", "shared by Quantify and De novo" or
"in De Novo Step 2".

## The check

`WorkflowNameTests` (DefaultSuite, and `--shell-checks`) reads every XAML attribute and text node
and every C# string literal (literal-only concatenations joined, comments skipped) in
`FamilyQuant` and `FamilyQuant.Core`, and fails when a retired name appears. While W4 is under
way, the sites a later step renames are listed in `PendingRetiredNameSites` with their counts;
each step removes its own entries, and the list is empty when W4 is complete. A renamed site that
keeps its entry fails as stale, so the list cannot hide a site. A renamed command keeps its
handler, shortcut and control name, so no function is lost; the feature ledger maps each old name
to the new one in its "Added contracts after the frozen source" section.
