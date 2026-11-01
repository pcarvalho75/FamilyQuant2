# FamilyQuant appearance

V1 uses the .NET 10 WPF Fluent theme, with two directions on the real curation
screen. Light uses neutral paper surfaces and dark ink. Dark uses neutral charcoal
surfaces and light ink. Selection and primary actions use the blue accent. Plain
values use the same ink as labels; small status text can indicate readiness or a
problem. Dark is the default, selected by Paulo.
The View menu keeps both choices available.

`Tokens.xaml` is the one place for colour values and the shared spacing, type and
radius scale. `Controls.xaml` inherits the built-in Fluent control styles. It does
not copy their templates. `ThemeManager.Apply` switches both Fluent `ThemeMode`
and application tokens. Dynamic resources keep the same curation control alive.
`NumericPalette` colours parts of the existing numeric editor template because
that dependency's internal parts retain a light background in the dark theme.
It does not replace the editor template, parsing, bounds, increments or commands.

The three explanatory facts removed from the curation panel are under
"How quantitation works" in Help. Both old curation tabs, all commands, options,
tooltips, bindings and test seams remain reachable. Layout consolidation and the
rest of the application's colour migration follow in their roadmap steps.

The implementation follows the WPF team's
[Fluent theme guide](https://github.com/dotnet/wpf/blob/main/Documentation/docs/using-fluent.md).
`ThemeMode` is experimental, so `WPF0001` is suppressed locally around its API
use. System mode follows Windows' app appearance and refreshes the token set on
preference changes through the UI dispatcher. Event handlers detach on app exit.
The real app saves the choice in `%LOCALAPPDATA%/FamilyQuant2/appearance.json`.
Missing, unknown or damaged settings fall back to Dark. Test and render hosts do
not initialize profile persistence, so a capture never changes the user's choice.
Unrebuilt views keep their original named surface resources until their replacements
land, preserving their existing text contrast during the staged rewrite.

Use `FamilyQuant.Tests/SCREENS.md` for reproducible captures. These captures show
client content and exclude native title bars. They do not prove keyboard focus,
screen reader support, high contrast mode or another Windows version.
