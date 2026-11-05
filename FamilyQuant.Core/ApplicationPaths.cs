using System;
using System.IO;

namespace FamilyQuant
{
    public static class ApplicationPaths
    {
        public const string DataFolderName = "FamilyQuant2";

        public static string LocalDataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolderName);

        public static string ScratchDirectory => Path.Combine(Path.GetTempPath(), DataFolderName);

        public static string EvidenceFetchCacheDirectory => Path.Combine(LocalDataDirectory, "evidence-fetch");
    }
}
