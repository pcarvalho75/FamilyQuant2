using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ThermoFisher.CommonCore.Data.Business;
using ThermoFisher.CommonCore.RawFileReader;

namespace FamilyQuant.DeNovo
{
    /// <summary>The instrument-selected precursor metadata for one MS2 scan. Charge comes from the RAW
    /// trailer; m/z comes from the scan reaction/filter. Either can be unknown independently, and that
    /// absence is retained rather than replaced with a search-engine report.</summary>
    public sealed class RawScanPrecursorMetadata
    {
        public string RunName { get; set; } = string.Empty;
        public int ScanNumber { get; set; }
        public int Charge { get; set; }
        public double PrecursorMz { get; set; }
        public bool ChargeKnown => Charge > 0;
        public bool PrecursorMzKnown => PrecursorMz > 0 && !double.IsNaN(PrecursorMz)
            && !double.IsInfinity(PrecursorMz);
    }

    /// <summary>Reads the instrument trailer's selected-precursor charge from requested MS2 scans for
    /// diagnostic reporting. It is not an automatic PSM filter: a co-isolated ion can carry a different
    /// charge, and both PatternLab and PEAKS database output may agree on that alternative hypothesis.</summary>
    public static class RawScanChargeReader
    {
        public static Dictionary<int, int> Read(string rawPath, IEnumerable<int> scanNumbers)
            => ReadMetadata(rawPath, scanNumbers).ToDictionary(kv => kv.Key, kv => kv.Value.Charge);

        /// <summary>Reads RAW-selected charge and precursor m/z for the requested MS2 scans. A scan with
        /// an unreadable/missing field is still returned with zero for that field so downstream joins can
        /// say explicitly that they fell back to source-reported metadata.</summary>
        public static Dictionary<int, RawScanPrecursorMetadata> ReadMetadata(
            string rawPath, IEnumerable<int> scanNumbers)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) throw new ArgumentException("A RAW path is required.", nameof(rawPath));
            if (!File.Exists(rawPath)) throw new FileNotFoundException("The RAW file was not found.", rawPath);
            if (scanNumbers == null) throw new ArgumentNullException(nameof(scanNumbers));

            using var raw = RawFileReaderAdapter.FileFactory(rawPath);
            if (!raw.IsOpen) throw new InvalidOperationException("The RAW file could not be opened: " + rawPath);
            raw.SelectInstrument(Device.MS, 1);

            var headers = raw.GetTrailerExtraHeaderInformation();
            int chargeIndex = headers == null ? -1 : Array.FindIndex(headers, h =>
                (h.Label ?? string.Empty).IndexOf("Charge State", StringComparison.OrdinalIgnoreCase) >= 0);

            string run = DeNovoReconciler.NormalizeRunName(rawPath);
            var result = new Dictionary<int, RawScanPrecursorMetadata>();
            foreach (int scan in scanNumbers.Where(s => s > 0).Distinct().OrderBy(s => s))
            {
                int charge = 0;
                double precursorMz = 0;
                try
                {
                    // Some valid RAW acquisitions do not expose a Charge State trailer column at all.
                    // That is an unknown charge for every requested scan, not a reason to discard the
                    // independently readable precursor m/z values from the scan filter.
                    if (chargeIndex >= 0)
                    {
                        var values = raw.GetTrailerExtraValues(scan, true);
                        if (chargeIndex < values.Length)
                        {
                            string text = Convert.ToString(values[chargeIndex], CultureInfo.InvariantCulture) ?? string.Empty;
                            string token = new string(text.Trim().TakeWhile(c => char.IsDigit(c) || c == '-').ToArray());
                            int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out charge);
                        }
                    }
                }
                catch { charge = 0; }

                try
                {
                    var filter = raw.GetFilterForScanNumber(scan);
                    try { precursorMz = filter.GetReaction(0).PrecursorMass; }
                    catch { precursorMz = filter.GetMass(0); }
                    if (!(precursorMz > 0) || double.IsNaN(precursorMz) || double.IsInfinity(precursorMz))
                        precursorMz = 0;
                }
                catch { precursorMz = 0; }

                result[scan] = new RawScanPrecursorMetadata
                {
                    RunName = run,
                    ScanNumber = scan,
                    Charge = charge,
                    PrecursorMz = precursorMz,
                };
            }
            return result;
        }
    }
}
