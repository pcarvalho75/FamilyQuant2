using System;
using System.IO;
using System.Text.Json;

namespace FamilyQuant.Themes
{
    /// <summary>Appearance preferences belong to 2.0 and never change a project or the 1.x profile.</summary>
    public static class ThemeSettings
    {
        public static string DefaultPath => Path.Combine(ApplicationPaths.LocalDataDirectory, "appearance.json");

        public static AppTheme Load(string path)
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                if (json.RootElement.ValueKind == JsonValueKind.Object
                    && json.RootElement.TryGetProperty("Theme", out var value)
                    && value.ValueKind == JsonValueKind.String
                    && Enum.TryParse(value.GetString(), true, out AppTheme theme) && Enum.IsDefined(theme))
                    return theme;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
            return AppTheme.Dark;
        }

        public static bool Save(string path, AppTheme theme)
        {
            string? temporary = null;
            try
            {
                string fullPath = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(new { Theme = theme.ToString() }));
                File.Move(temporary, fullPath, true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
        }
    }
}
