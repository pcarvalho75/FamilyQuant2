using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace FamilyQuant
{
    /// <summary>One analytics figure: the file name it is saved under, the chart itself, and the height
    /// the analytics screen gives it when it is stacked on screen.</summary>
    public sealed class AnalyticsFigure
    {
        public AnalyticsFigure(string fileName, FrameworkElement chart, double screenHeight)
        {
            FileName = fileName;
            Chart = chart;
            ScreenHeight = screenHeight;
        }

        /// <summary>The stable, descriptive PNG name, e.g. "family_contribution.png".</summary>
        public string FileName { get; }

        public FrameworkElement Chart { get; }

        /// <summary>Height in DIP when this figure is one of the stacked charts on the screen. It is NOT
        /// the export height: the export lays every figure out at the slide size instead.</summary>
        public double ScreenHeight { get; }
    }

    /// <summary>
    /// The one list of analytics figures, so what the screen draws and what "Save figures" writes cannot
    /// drift apart, and so a harness can produce the very same PNGs from real data with no window.
    ///
    /// The screen builds one set and hosts it; the export builds a SECOND set over the same data and
    /// renders that, because measuring and arranging a chart that is already parented fights the layout
    /// it is parented into (see <see cref="ChartImageExporter"/>).
    /// </summary>
    public static class AnalyticsFigures
    {
        public const string XicByMassFile = "xic_by_mass.png";
        public const string IsotopesCreditedFile = "isotopes_credited.png";
        public const string PercentIonCurrentFile = "percent_ion_current_by_mass.png";
        public const string FamilyContributionFile = "family_contribution.png";
        public const string RecoveryFile = "recovery.png";

        /// <summary>Every figure the analytics screen holds, in the order it stacks them.</summary>
        public static IReadOnlyList<string> FileNames { get; } = new[]
        {
            XicByMassFile, IsotopesCreditedFile, PercentIonCurrentFile, FamilyContributionFile, RecoveryFile,
        };

        /// <summary>
        /// Builds a fresh set of charts over the given analytics.
        ///
        /// The family chart is ALWAYS built, even with no family table: it then draws its own "nothing to
        /// show" state, which says a Family Cluster File is what would fill it. A section that vanishes
        /// leaves the reader with no way to learn that it exists.
        /// </summary>
        /// <param name="data">The deduped table and the Q3 fractions.</param>
        /// <param name="families">The per-family envelope table, or null when no curation is open.</param>
        /// <param name="topN">How many families get a bar of their own before the rest are folded.</param>
        /// <param name="asPercentOfTotal">Label the family bars as shares of the whole measured signal.</param>
        public static List<AnalyticsFigure> Build(AnalyticsData data,
                                                  FamilyEnvelopeQuantResult? families,
                                                  int topN = FamilyContributionPlot.DefaultTopN,
                                                  bool asPercentOfTotal = false)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            double groupedTic = data.Grouped?.Tic ?? 0;

            return new List<AnalyticsFigure>
            {
                new AnalyticsFigure(XicByMassFile, new XicMassChart(data.Peptides), 340),
                new AnalyticsFigure(IsotopesCreditedFile, new IsotopeCountChart(data.Peptides), 300),
                new AnalyticsFigure(PercentIonCurrentFile,
                    new PercentIonCurrentChart(data.Peptides, groupedTic), 300),
                new AnalyticsFigure(FamilyContributionFile,
                    new FamilyContributionChart(families, topN, asPercentOfTotal), 420),
                new AnalyticsFigure(RecoveryFile, new RecoveryChart(data.Fractions, data.Grouped, isotopeCeiling: data.IsotopeCeiling), 380),
            };
        }

        /// <summary>
        /// Writes every figure into <paramref name="folder"/> as a presentation-quality PNG and returns
        /// the paths written, in the order they were drawn. The folder is created when it is not there.
        ///
        /// The PNGs come out in the LIGHT palette, which is what a slide wants; the screen is untouched.
        /// A caller who wants the screen's dark colours in a file asks for them.
        /// </summary>
        public static List<string> Save(AnalyticsData data,
                                        FamilyEnvelopeQuantResult? families,
                                        string folder,
                                        int topN = FamilyContributionPlot.DefaultTopN,
                                        bool asPercentOfTotal = false,
                                        double layoutWidth = ChartImageExporter.DefaultLayoutWidth,
                                        double layoutHeight = ChartImageExporter.DefaultLayoutHeight,
                                        double scale = ChartImageExporter.DefaultScale,
                                        ChartPaletteVariant variant = ChartImageExporter.DefaultVariant)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new ArgumentException("A folder is required to save the figures.", nameof(folder));
            }

            Directory.CreateDirectory(folder);

            var written = new List<string>();
            foreach (AnalyticsFigure figure in Build(data, families, topN, asPercentOfTotal))
            {
                written.Add(ChartImageExporter.Save(figure.Chart, Path.Combine(folder, figure.FileName),
                                                    layoutWidth, layoutHeight, scale, variant));
            }

            return written;
        }

        /// <summary>
        /// The programmatic entry point: from a project's stored quant straight to the PNGs, with no
        /// window and no screen in between. The family table is built from the curation when one is given,
        /// through <see cref="FamilyEnvelopeQuant.BuildFromCuration"/> - which is a homology search, so
        /// this call is seconds rather than instant on a real database.
        /// </summary>
        /// <param name="rows">The store's per-peptide quant rows, both sides.</param>
        /// <param name="manifest">The manifest paired with them, for the per-run TIC; may be null.</param>
        /// <param name="curation">The curated family file, or null for no family chart.</param>
        /// <param name="folder">Where to write the PNGs.</param>
        public static List<string> SaveFromProject(IEnumerable<QuantRow> rows,
                                                   QuantManifest? manifest,
                                                   FamilyClusterFile? curation,
                                                   string folder,
                                                   int topN = FamilyContributionPlot.DefaultTopN,
                                                   bool asPercentOfTotal = false,
                                                   ChartPaletteVariant variant = ChartImageExporter.DefaultVariant)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            List<QuantRow> rowList = rows.Where(r => r != null).ToList();

            IReadOnlyDictionary<string, double> tic = manifest?.PerRunTic
                ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            AnalyticsData data = AnalyticsBuilder.Build(rowList, tic, manifest?.IsotopeCount);

            // Same injection the screen makes: the Q6 detected estimate is read from the manifest, never
            // recomputed here.
            if (manifest?.DerivedFractions != null && manifest.DerivedFractions.Count > 0)
                NativeQuantFractions.ApplyDetectedToFractions(data.Fractions, data.Grouped, manifest.DerivedFractions);

            FamilyEnvelopeQuantResult? families = curation == null
                ? null
                : FamilyEnvelopeQuant.BuildFromCuration(rowList, curation);

            return Save(data, families, folder, topN, asPercentOfTotal,
                        ChartImageExporter.DefaultLayoutWidth, ChartImageExporter.DefaultLayoutHeight,
                        ChartImageExporter.DefaultScale, variant);
        }
    }
}
