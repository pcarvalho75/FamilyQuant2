# C14c keyboard handoff for Paulo

The native capture manifest records each visible, enabled Tab candidate with its
type, name, TabIndex and navigation mode. The focus command paints native focus
states, native chrome/trigger values or FocusVisualStyle templates for Button,
CheckBox, ComboBox and TextBox in both themes at 96/120/144 DPI. It checks changed
pixels, indicator contrast, propagated DPI and that keyboard focus stays absent.
No key traversal is claimed by these previews. The desk accepted this boundary
in M6; Paulo runs the actual keyboard flow at C14e.

```text
FamilyQuant.Tests.exe --look-finish-focus <fresh-output> <timestamp-from-date>
```

The command creates `focus-templates.json`, paired normal/focus PNGs and
`SYNTHETIC-UI-ONLY.fqproj`. Use that generated project for this short check; it is
safe to edit and save and contains synthetic UI data.

1. Open the fixture with Ctrl+O. Use F10 and the arrow keys through the File,
   Edit, View, Analysis and Help menus, then Escape.
2. Alt+0 returns to Start. Tab and Shift+Tab through its enabled actions and the
   navigation rail. The focus indicator stays visible and traversal has no trap.
3. Alt+1 opens Families. Tab through Search, tables and actions. Edit one family
   name with F2, Enter, then Ctrl+Z to restore it.
4. Alt+2 opens Quantify. Tab through its inputs and browse actions. Missing
   inputs keep Compute unavailable. Alt+3 opens Results; Tab to Minimum tag
   length, choose 7, then Ctrl+S. Reopen the fixture with Ctrl+O, Alt+3, and check
   that the stored choice is 7.
5. Alt+4 opens De novo. Tab through source and filter controls and expand the
   advanced section with the keyboard. F1 opens Help; Page Down, select/copy text
   and Alt+F4 return to the application.
6. In View > Appearance choose Light, then Dark, and repeat a short forward and
   reverse Tab pass. Record any failing control and the theme.

Paulo records PASS or the failing step before the final look sign-off. These
checks perform no RAW extraction or scientific validation.
