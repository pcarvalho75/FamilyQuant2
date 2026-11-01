using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant
{
    /// <summary>Which of the two house palettes a chart paints itself in.</summary>
    public enum ChartPaletteVariant
    {
        /// <summary>The screen palette: light ink on the dark backdrop the application has always used.</summary>
        Dark,

        /// <summary>The slide palette: near-black ink on a near-white backdrop, with the series re-tuned
        /// for a projector.</summary>
        Light,
    }

    /// <summary>
    /// A chart that takes its colours from a <see cref="ChartPalette"/> instead of owning them.
    ///
    /// It is what lets <see cref="ChartImageExporter"/> render the very same chart object in either
    /// variant: the exporter sets the palette, the chart invalidates and redraws. A chart that does not
    /// implement it is still exported, just in whatever colours it paints itself.
    /// </summary>
    public interface IPalettedChart
    {
        /// <summary>The palette this chart paints itself in. Never null; assigning null restores
        /// <see cref="ChartPalette.Dark"/>. Setting it must invalidate the visual.</summary>
        ChartPalette Palette { get; set; }
    }

    /// <summary>
    /// The one colour palette every house chart paints from, in two variants.
    ///
    /// WHY IT EXISTS. Each chart used to carry its own private copy of the same six or seven brushes, so
    /// "the blue" was written out six times and a figure could not be re-coloured for a slide without
    /// editing every chart. The brushes are named for what they MEAN (the identified series, the grid,
    /// the ink) and not for what they look like, so the same chart code draws either variant.
    ///
    /// DARK is the screen palette and carries EXACTLY the values the charts hardcoded before this type
    /// existed, so nothing on screen changes. It stays the default of every chart.
    ///
    /// LIGHT is the slide palette, and it is NOT the dark one with the background inverted. On white the
    /// dark palette's light blue (0x6FA8F0) and mint green (0x3FD39C) wash out - they carry about 2:1
    /// contrast against white, which is the worst case on a projector in a lit room. The light variant
    /// therefore re-tunes every series to a darker, more saturated version of the SAME identity: the blue
    /// stays blue, the green stays green, the amber stays amber, and the greys stay grey. Every fill it
    /// puts a percentage label on is dark enough for the near-white <see cref="SegmentLabel"/> to be read
    /// on it, and adjacent segments of a stacked bar are separated by hue AND by lightness rather than by
    /// hue alone.
    ///
    /// COLOUR VISION. The two most important series - identified and de novo - are never told apart by a
    /// red/green pair. They are blue against green, which survives protanopia and deuteranopia because
    /// the two differ in lightness as well as hue (in Light, CIELAB dE is about 97 and the lightness gap
    /// about 22 L* units). The one warm-versus-cool pair that sits side by side in a bar, the burnt amber
    /// and the slate grey, differs in chroma rather than in a red/green axis.
    ///
    /// THREAD SAFETY. Every brush is frozen, so a palette can be read from a render pass on any thread
    /// and can be shared by every chart in the application.
    /// </summary>
    public sealed class ChartPalette
    {
        private ChartPalette(ChartPaletteVariant variant)
        {
            Variant = variant;
        }

        /// <summary>Which variant this is.</summary>
        public ChartPaletteVariant Variant { get; }

        // ---- structure ---------------------------------------------------------------------------

        /// <summary>The panel a chart paints behind everything else. A chart paints it explicitly rather
        /// than relying on its host, so a headless render to a bitmap shows what the window shows instead
        /// of ink on transparency.</summary>
        public Brush Backdrop { get; private set; } = Brushes.Transparent;

        /// <summary>The strong text: headings, the highlighted run, the value on top of a bar.</summary>
        public Brush Ink { get; private set; } = Brushes.Transparent;

        /// <summary>The quiet text: axis numbers, captions, legend names.</summary>
        public Brush Muted { get; private set; } = Brushes.Transparent;

        /// <summary>The grid lines and the thin outline around a bar.</summary>
        public Brush Grid { get; private set; } = Brushes.Transparent;

        /// <summary>A quiet inset panel used to group explanatory text inside a chart. It must remain
        /// visibly distinct from <see cref="Backdrop"/> without competing with plotted data.</summary>
        public Brush Panel { get; private set; } = Brushes.Transparent;

        /// <summary>The two axis lines. Heavier than <see cref="Grid"/> on purpose.</summary>
        public Brush Axis { get; private set; } = Brushes.Transparent;

        /// <summary>The ink for a label drawn ON TOP of a filled series segment. It is not
        /// <see cref="Ink"/>: a percentage printed inside a deep blue block has to contrast with the
        /// block, not with the panel.</summary>
        public Brush SegmentLabel { get; private set; } = Brushes.Transparent;

        // ---- the named series --------------------------------------------------------------------

        /// <summary>The identified (DDA) side. The blue.</summary>
        public Brush Identified { get; private set; } = Brushes.Transparent;

        /// <summary>The de novo side. The green.</summary>
        public Brush DeNovo { get; private set; } = Brushes.Transparent;

        /// <summary>The two assigned sides drawn as ONE block (the talk bar's quantified share): the
        /// blue and the green's common blue-green, so the eye reads it as the same signal and not as a
        /// fourth category.</summary>
        public Brush Quantified { get; private set; } = Brushes.Transparent;

        /// <summary>Identified but NOT quantified. A violet: neither claimed signal nor unclaimed signal,
        /// and it must not be mistaken for either neighbour.</summary>
        public Brush NotQuantified { get; private set; } = Brushes.Transparent;

        /// <summary>Peptide-like but unidentified: an amber, distinct from both the assigned blue-greens
        /// and the grey noise block, so the eye reads it as "signal we did not claim".</summary>
        public Brush PeptideLikeUnidentified { get; private set; } = Brushes.Transparent;

        /// <summary>Not peptide-like / noise, and the single "without an owner" block of the two-level
        /// fallback bar. A grey.</summary>
        public Brush Remainder { get; private set; } = Brushes.Transparent;

        /// <summary>The detected peptide-like level of the absolute panel, both charges together.</summary>
        public Brush Detected { get; private set; } = Brushes.Transparent;

        /// <summary>The MULTIPLY CHARGED part of the detected level (z &gt;= 2): strong evidence of a
        /// peptide, because a 0.5 or 0.33 Da isotope spacing is not something a small organic molecule
        /// produces. The stronger of the two ambers, and the same value as <see cref="Detected"/> so an
        /// unsplit figure and a split one read as the same level.</summary>
        public Brush DetectedMultiplyCharged { get; private set; } = Brushes.Transparent;

        /// <summary>The SINGLY CHARGED part of the detected level (z == 1): weaker evidence, because at
        /// charge 1 "has a C13 isotope" is true of essentially any organic molecule. The paler amber - it
        /// is a weaker claim, NOT a different substance.</summary>
        public Brush DetectedSinglyCharged { get; private set; } = Brushes.Transparent;

        /// <summary>The raw MS1 total ion current bar.</summary>
        public Brush Tic { get; private set; } = Brushes.Transparent;

        /// <summary>The identity sweep's family-count curve.</summary>
        public Brush Families { get; private set; } = Brushes.Transparent;

        /// <summary>The identity sweep's target/decoy mixing curve. The warm alarm colour.</summary>
        public Brush Mixing { get; private set; } = Brushes.Transparent;

        /// <summary>The identity sweep's clean floor marker.</summary>
        public Brush Floor { get; private set; } = Brushes.Transparent;

        /// <summary>The bar outline that marks something which is NOT one family: the fold and the
        /// unattributed residual.</summary>
        public Brush NotAFamily { get; private set; } = Brushes.Transparent;

        /// <summary>The percent-of-ion-current chart's solid "share of the credited envelope" bar.</summary>
        public Brush Found { get; private set; } = Brushes.Transparent;

        /// <summary>The percent-of-ion-current chart's outlined "share of the TIC" overlay.</summary>
        public Brush OfTic { get; private set; } = Brushes.Transparent;

        /// <summary>The isotope histogram's three mass bands, low mass to high, so a stacked bar reads
        /// bottom-up by mass. Always three entries.</summary>
        public IReadOnlyList<Brush> MassBands { get; private set; } = Array.Empty<Brush>();

        // ---- the two variants --------------------------------------------------------------------

        /// <summary>
        /// The screen palette, with EXACTLY the values the charts hardcoded before this type existed.
        /// Any change to a number here changes what the application looks like.
        /// </summary>
        public static ChartPalette Dark { get; } = BuildDark();

        /// <summary>
        /// The slide palette. See the type remarks for why it is not an inversion of
        /// <see cref="Dark"/>.
        /// </summary>
        public static ChartPalette Light { get; } = BuildLight();

        /// <summary>The palette for a variant. Unknown values fall back to <see cref="Dark"/>, which is
        /// what the application looks like, rather than throwing inside a render pass.</summary>
        public static ChartPalette For(ChartPaletteVariant variant) =>
            variant == ChartPaletteVariant.Light ? Light : Dark;

        /// <summary>A frozen snapshot of the current application tokens for on-screen charts.
        /// Read on the application dispatcher and read again after ThemeManager.ThemeChanged.
        /// The historical Dark and slide Light palettes remain independent.</summary>
        public static ChartPalette UiPalette
        {
            get
            {
                var app = Application.Current ?? throw new InvalidOperationException("An application is required for UI chart tokens.");
                app.Dispatcher.VerifyAccess();
                Brush Token(string key)
                {
                    var brush = ((Brush)app.FindResource(key)).CloneCurrentValue();
                    brush.Freeze(); return brush;
                }
                var backdrop = Token("CanvasBrush");
                var identified = Token("ChartIdentifiedBrush"); var deNovo = Token("ChartDeNovoBrush");
                var mixing = Token("ErrorBrush"); var detected = Token("ChartDetectedBrush");
                var muted = Token("MutedBrush");
                return new ChartPalette(RelativeLuminance(backdrop) > 0.5 ? ChartPaletteVariant.Light : ChartPaletteVariant.Dark)
                {
                    Backdrop = backdrop, Ink = Token("TextBrush"), Muted = muted, Grid = Token("EdgeBrush"),
                    Panel = Token("SubtleBrush"), Axis = muted, SegmentLabel = Token("AccentInkBrush"),
                    Identified = identified, DeNovo = deNovo, Quantified = Token("ChartQuantifiedBrush"),
                    NotQuantified = Token("ChartNotQuantifiedBrush"), PeptideLikeUnidentified = Token("ChartPeptideLikeBrush"),
                    Remainder = Token("ChartRemainderBrush"), Detected = detected, DetectedMultiplyCharged = detected,
                    DetectedSinglyCharged = Token("ChartSinglyBrush"), Tic = Token("ChartTicBrush"),
                    Families = identified, Mixing = mixing, Floor = Token("SuccessBrush"),
                    NotAFamily = Token("ChartPeptideLikeBrush"), Found = identified, OfTic = deNovo,
                    MassBands = new[] { identified, deNovo, mixing }
                };
            }
        }

        private static ChartPalette BuildDark()
        {
            Brush backdrop = Frozen(0x33, 0x39, 0x49);
            Brush muted = Frozen(0xA8, 0xA4, 0x9A);
            Brush identified = Frozen(0x6F, 0xA8, 0xF0);
            Brush deNovo = Frozen(0x3F, 0xD3, 0x9C);
            Brush mixing = Frozen(0xF0, 0x85, 0x4C);
            Brush peptideLike = Frozen(0xD8, 0xA6, 0x4B);
            Brush detected = Frozen(0xC9, 0x9A, 0x40);

            return new ChartPalette(ChartPaletteVariant.Dark)
            {
                Backdrop = backdrop,
                Ink = Frozen(0xF2, 0xF0, 0xE9),
                Muted = muted,
                Grid = Frozen(0x44, 0x4B, 0x5C),
                Panel = Frozen(0x2A, 0x2F, 0x3D),
                // The charts drew their axis lines with the muted brush; keeping the same value keeps the
                // pixels identical.
                Axis = muted,
                // A label inside a segment was drawn in the backdrop colour, which on this palette is the
                // darkest thing available. Same value, same pixels.
                SegmentLabel = backdrop,

                Identified = identified,
                DeNovo = deNovo,
                Quantified = Frozen(0x55, 0xBD, 0xC6),
                NotQuantified = Frozen(0xB0, 0x6C, 0xC8),
                PeptideLikeUnidentified = peptideLike,
                Remainder = Frozen(0x6B, 0x73, 0x8A),
                Detected = detected,
                DetectedMultiplyCharged = detected,
                // The same warm family, DESATURATED: a muted tan reads as the washed-out sibling of the
                // saturated amber, which is what "weaker evidence" should look like. CIELAB 29.5 from it,
                // and 3.8:1 on the backdrop, so the two parts of one bar cannot be confused.
                DetectedSinglyCharged = Frozen(0xA9, 0x90, 0x6A),
                Tic = Frozen(0x8A, 0x93, 0xA8),

                Families = identified,
                Mixing = mixing,
                Floor = deNovo,
                NotAFamily = peptideLike,
                Found = identified,
                OfTic = deNovo,
                MassBands = new[] { identified, deNovo, mixing },
            };
        }

        private static ChartPalette BuildLight()
        {
            // Every value below was chosen against the near-white backdrop, not derived from the dark
            // one. The comment on each says what it has to survive.
            Brush backdrop = Frozen(0xFB, 0xFA, 0xF7);   // near-white, a hair warm so it is not a glare
            Brush muted = Frozen(0x56, 0x5C, 0x68);      // ~7.4:1 on the backdrop: quiet, still readable
            Brush identified = Frozen(0x10, 0x42, 0x8C); // deep blue, ~9.3:1 on white
            Brush deNovo = Frozen(0x1F, 0x8F, 0x5A);     // deep green, ~4.0:1, ~22 L* lighter than the blue
            Brush mixing = Frozen(0xC2, 0x41, 0x0C);     // burnt orange-red: the alarm curve, still warm
            Brush peptideLike = Frozen(0xA8, 0x5F, 0x13);// burnt amber, ~4.7:1, white label readable on it
            Brush detected = Frozen(0x8A, 0x5A, 0x12);   // the same amber taken darker for the level bar

            return new ChartPalette(ChartPaletteVariant.Light)
            {
                Backdrop = backdrop,
                Ink = Frozen(0x16, 0x18, 0x1E),          // near-black, ~18:1 on the backdrop
                Muted = muted,
                Grid = Frozen(0xDD, 0xDD, 0xE3),         // light grey: present, never competing with a bar
                Panel = Frozen(0xEE, 0xEF, 0xED),        // neutral inset on the warm near-white ground
                Axis = muted,
                SegmentLabel = Frozen(0xFF, 0xFF, 0xFF), // every labelled fill below is >= 4:1 against it

                Identified = identified,
                DeNovo = deNovo,
                Quantified = Frozen(0x0F, 0x6B, 0x84),   // deep teal: the blue and the green's common
                NotQuantified = Frozen(0x7B, 0x2D, 0x9E),// deep violet, unmistakable beside the teal
                PeptideLikeUnidentified = peptideLike,
                // Slate grey. Against the amber above it the contrast RATIO is only ~1.2, but the CIELAB
                // distance is ~67: they are separated by chroma and hue, which is what survives a
                // projector and a colour-blind reader, and neither of them is red or green.
                Remainder = Frozen(0x5F, 0x66, 0x75),
                Detected = detected,
                DetectedMultiplyCharged = detected,
                // Muted khaki: the same warm family with the chroma taken out, which is what "weaker
                // evidence" should look like beside the saturated amber. A lighter tint of the amber was
                // tried first and is NOT enough - it lands at CIELAB 16 from it, close enough that two
                // segments of one bar blur together on a projector. This is CIELAB 31 from it and still
                // holds 3.9:1 on white.
                DetectedSinglyCharged = Frozen(0x8C, 0x7C, 0x5C),
                Tic = Frozen(0x7B, 0x83, 0x94),          // the "all signal" bar: lighter than the remainder

                Families = identified,
                Mixing = mixing,
                Floor = deNovo,
                NotAFamily = peptideLike,
                Found = identified,
                OfTic = deNovo,
                MassBands = new[] { identified, deNovo, mixing },
            };
        }

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        // ---- the measurements the tests and the comments above stand on --------------------------

        /// <summary>The colour of a solid brush. Anything that is not a <see cref="SolidColorBrush"/> is
        /// not a palette entry and is a caller error.</summary>
        public static Color ColorOf(Brush brush)
        {
            if (brush is SolidColorBrush solid) return solid.Color;
            throw new ArgumentException("A palette brush is a SolidColorBrush; got " +
                                        (brush == null ? "null" : brush.GetType().Name) + ".", nameof(brush));
        }

        /// <summary>WCAG relative luminance, 0 for black and 1 for white. The number the "is the light
        /// variant really light" check is made of.</summary>
        public static double RelativeLuminance(Brush brush)
        {
            Color c = ColorOf(brush);
            return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
        }

        /// <summary>WCAG contrast ratio between two palette entries, 1 for identical and 21 for black on
        /// white. Used for text legibility, not for "are these two fills distinguishable" - two fills of
        /// different hue and equal lightness are perfectly distinguishable at a ratio of 1.</summary>
        public static double ContrastRatio(Brush a, Brush b)
        {
            double la = RelativeLuminance(a);
            double lb = RelativeLuminance(b);
            double hi = Math.Max(la, lb);
            double lo = Math.Min(la, lb);
            return (hi + 0.05) / (lo + 0.05);
        }

        /// <summary>
        /// The CIE76 colour difference (dE) in CIELAB under D65, which is the honest measure of "can these
        /// two be told apart": it counts hue, chroma and lightness together. Roughly, 2.3 is the just-
        /// noticeable difference, 25 is comfortably distinct on a screen, and the pairs this palette puts
        /// side by side in a bar are all above 50.
        /// </summary>
        public static double PerceptualDistance(Brush a, Brush b)
        {
            (double la, double aa, double ba) = Lab(ColorOf(a));
            (double lb, double ab, double bb) = Lab(ColorOf(b));
            double dl = la - lb, da = aa - ab, db = ba - bb;
            return Math.Sqrt(dl * dl + da * da + db * db);
        }

        private static double Linear(byte channel)
        {
            double s = channel / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        /// <summary>sRGB to CIELAB under the D65 white point, the reference illuminant sRGB is defined
        /// against, so no chromatic adaptation is needed.</summary>
        private static (double L, double A, double B) Lab(Color c)
        {
            double r = Linear(c.R), g = Linear(c.G), b = Linear(c.B);

            double x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / 0.95047;
            double y = (0.2126729 * r + 0.7151522 * g + 0.0721750 * b) / 1.00000;
            double z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / 1.08883;

            double fx = F(x), fy = F(y), fz = F(z);
            return (116.0 * fy - 16.0, 500.0 * (fx - fy), 200.0 * (fy - fz));
        }

        private static double F(double t) =>
            t > 216.0 / 24389.0 ? Math.Cbrt(t) : (841.0 / 108.0) * t + 4.0 / 29.0;
    }
}
