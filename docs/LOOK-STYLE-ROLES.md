# De novo style role evidence

The explicit map in `LOOK-STYLE-ROLES.json` covers the 24 rows delegated by EV.
The frozen source hash, exact line, style key, target type, setter property and
old value are checked against the immutable inventory and source export.

Five roles remain in the current view: compact state badge container, state
badge text, section heading, neutral result/counter and wrapping explanatory
text. Each maps to its named current style and an actual consumer in the native
De novo control. Layout and typography setter values remain equal.

The four foreground setters deliberately change: White, gold `#FFD700` and gray
`#AAAAAA` become the theme's `TextBrush`. Dark resolves to `#FFF5F5F5`, Light to
`#FF1A1A1A`. This preserves the text role while following the 2.0 requirements
for theme tokens and neutral plain values. It does not preserve the old colors.

The native producer lays out the control, verifies actual style use, and reads
the resolved values on styled native instances. Each of the 24 rows has its own
Dark and Light assertion and records old declaration, current declaration,
resolved value, token value and consumer. The JSON result is byte-identical
across two runs. A planted mapping back to gold fails both theme checks.

```text
FamilyQuant.Tests.exe --prove-look-style-roles <frozen-source-root> docs/LOOK-STYLE-ROLES.json <fresh-output> <timestamp-from-date>
```

These are resolved-style checks. They do not establish handler behavior,
scientific parity, OS scaling or Paulo's final visual sign-off. The generated
`LK-style-roles.json` is a proposal for independent review.
