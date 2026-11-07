using System;
using System.IO;

namespace FamilyQuant
{
    public static class ApplicationPaths
    {
        public const string DataFolderName = "FamilyQuant2";

        /// <summary>Isolates native test caches and logs; null retains the installed program's location.</summary>
        public static string? LocalDataDirectoryOverride { get; set; }

        public static string LocalDataDirectory => LocalDataDirectoryOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolderName);

        public static string ScratchDirectory => Path.Combine(Path.GetTempPath(), DataFolderName);

        public static string EvidenceFetchCacheDirectory => Path.Combine(LocalDataDirectory, "evidence-fetch");
    }
}
