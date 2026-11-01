using System;
using System.Collections.Generic;
using System.Linq;
using ThermoFisher.CommonCore.Data.Business;
using ThermoFisher.CommonCore.RawFileReader;

namespace FamilyQuant.Tests
{
    internal static class RawScanAudit
    {
        public static void Print(string rawPath, int scanNumber)
        {
            using var raw = RawFileReaderAdapter.FileFactory(rawPath);
            if (!raw.IsOpen) throw new InvalidOperationException("The RAW file could not be opened: " + rawPath);
            raw.SelectInstrument(Device.MS, 1);

            Console.WriteLine("RAW scan audit: " + rawPath + " scan " + scanNumber);
            var filter = raw.GetFilterForScanNumber(scanNumber);
            Console.WriteLine("filter precursor m/z: " + filter.GetMass(0).ToString("0.#####"));
            Console.WriteLine("reaction precursor m/z: " + filter.GetReaction(0).PrecursorMass.ToString("0.#####"));

            var headers = raw.GetTrailerExtraHeaderInformation();
            var values = raw.GetTrailerExtraValues(scanNumber, true);
            string[] wanted = { "charge", "monoisotopic", "master scan", "scan event", "isolation", "precursor" };
            for (int i = 0; i < Math.Min(headers.Length, values.Length); i++)
            {
                string label = headers[i].Label ?? string.Empty;
                if (wanted.Any(w => label.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                    Console.WriteLine("trailer: " + label + " = " + values[i]);
            }
        }
    }
}
