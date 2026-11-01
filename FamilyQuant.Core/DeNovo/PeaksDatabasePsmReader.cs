using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FamilyQuant.DeNovo
{
    /// <summary>One PEAKS database-search PSM from db.psms.csv, kept separate from the PEAKS
    /// denovoOnly call because both can coexist on one scan and answer different questions.</summary>
    public sealed class PeaksDatabasePsm
    {
        public string FileName { get; set; } = string.Empty;
        public int ScanNumber { get; set; }
        public string Sequence { get; set; } = string.Empty;
        public double Minus10LogP { get; set; }
        public double NeutralMass { get; set; }
        public double PrecursorMz { get; set; }
        public double RawPrecursorMz { get; set; }
        public int ChargeState { get; set; }
    }

    public static class PeaksDatabasePsmReader
    {
        public static List<PeaksDatabasePsm> Read(string path)
        {
            using var reader = new StreamReader(path);
            string? headerLine = reader.ReadLine();
            if (headerLine == null) return new List<PeaksDatabasePsm>();
            List<string> names = PeaksCsvReader.SplitCsvLine(headerLine);
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < names.Count; i++) if (!index.ContainsKey(names[i])) index.Add(names[i], i);
            int Require(string name) => index.TryGetValue(name, out int i) ? i
                : throw new InvalidDataException("PEAKS db.psms.csv is missing \"" + name + "\": " + path);
            int iPep = Require("Peptide"), iScore = Require("-10LgP"), iMass = Require("Mass"),
                iMz = Require("m/z"), iRawMz = Require("raw m/z"), iZ = Require("z"),
                iScan = Require("Scan"), iFile = Require("Source File");

            var result = new List<PeaksDatabasePsm>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                List<string> f = PeaksCsvReader.SplitCsvLine(line);
                if (f.Count <= Math.Max(iFile, Math.Max(iScan, iPep))) continue;
                result.Add(new PeaksDatabasePsm
                {
                    Sequence = f[iPep],
                    Minus10LogP = ParseDouble(f[iScore]),
                    NeutralMass = ParseDouble(f[iMass]),
                    PrecursorMz = ParseDouble(f[iMz]),
                    RawPrecursorMz = ParseDouble(f[iRawMz]),
                    ChargeState = int.Parse(f[iZ], CultureInfo.InvariantCulture),
                    ScanNumber = int.Parse(f[iScan], CultureInfo.InvariantCulture),
                    FileName = f[iFile],
                });
            }
            return result;
        }

        private static double ParseDouble(string value) => string.IsNullOrWhiteSpace(value) ? 0
            : double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
