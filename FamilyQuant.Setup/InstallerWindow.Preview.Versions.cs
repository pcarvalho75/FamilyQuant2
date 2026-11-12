namespace FamilyQuant.Setup;

public static partial class InstallerWindowPreview
{
    private static void CheckVersionChoices(List<string> checks)
    {
        void Check(bool condition, string description) { Require(condition, description); checks.Add("PASS " + description); }
        PreviewController Existing(string version) => new()
        {
            PayloadVersion = "2.1.4.0", Existing = new(PreviewController.Folder, version, true, true)
        };
        var older = Existing("2.1.17.0");
        var window = new InstallerWindow(older);
        Check(window.PageTitle.Text == "FamilyQuant is already installed"
            && window.PageDescription.Text == "FamilyQuant 2.1.17 is already installed. This setup holds 2.1.4."
            && window.PrimaryButton.Content.Equals("_Open FamilyQuant"), "older Setup shows both versions with Open as primary, never Upgrade");
        Click(window.PrimaryButton);
        Check(older.Launches == 1 && older.Installs == 0 && window.ExitCode == 0, "older Setup Open launches current app without installing");

        older = Existing("2.1.17.0"); window = new InstallerWindow(older);
        Click(window.ExistingActionButton);
        Check(window.PageTitle.Text == "Install this older version?" && older.Installs == 0 && older.Launches == 0,
            "older installation requires a separate confirmation");
        Click(window.BackButton);
        Check(window.PrimaryButton.Content.Equals("_Open FamilyQuant") && older.Installs == 0, "back from older-version confirmation changes no files");
        Click(window.ExistingActionButton); Click(window.PrimaryButton); Click(window.PrimaryButton);
        Check(window.OptionsPanel.Visibility == System.Windows.Visibility.Visible && older.Installs == 0,
            "confirmed older choice reaches install options without installing yet");
        var operation = window.ExecuteOperationAsync(); PumpUntil(() => operation.IsCompleted); operation.GetAwaiter().GetResult();
        Check(older.Installs == 1 && older.LastAllowOlderVersion, "explicit older choice alone reaches controller with downgrade permission");
        window.Close();

        var equal = Existing("2.1.4"); window = new InstallerWindow(equal);
        Check(window.PageTitle.Text == "FamilyQuant is already installed" && window.ExistingActionButton.Content.Equals("_Repair"),
            "equal normalized version offers Open and quiet Repair");
        Click(window.PrimaryButton);
        Check(equal.Launches == 1 && equal.Installs == 0, "equal version Open installs nothing");
        equal = Existing("2.1.4.0"); window = new InstallerWindow(equal);
        Click(window.ExistingActionButton); Click(window.PrimaryButton);
        operation = window.ExecuteOperationAsync(); PumpUntil(() => operation.IsCompleted); operation.GetAwaiter().GetResult();
        Check(equal.Installs == 1 && !equal.LastAllowOlderVersion, "Repair runs ordinary install without downgrade permission");
        window.Close();
    }
}
