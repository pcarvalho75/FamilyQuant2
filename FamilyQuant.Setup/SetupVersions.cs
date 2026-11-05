namespace FamilyQuant.Setup;

public static class SetupVersions
{
    public static int Compare(string installed, string payload) => Normalize(installed).CompareTo(Normalize(payload));
    public static string Display(string value)
    {
        var version = Normalize(value);
        return version.Revision == 0 ? version.ToString(3) : version.ToString(4);
    }
    private static Version Normalize(string value)
    {
        if (!Version.TryParse(value, out var version)) throw new IOException("The installation version is invalid.");
        return new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    }
    public static string AlreadyInstalled(string installed, string payload) =>
        "FamilyQuant " + Display(installed) + " is already installed. This setup holds " + Display(payload) + ".";
}
