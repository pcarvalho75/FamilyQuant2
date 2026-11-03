using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Anthropic.Exceptions;
using Microsoft.Win32;

namespace FamilyQuant.Ai;

public sealed class AiKeySettingsWindow : Window
{
    public PasswordBox KeyEditor { get; } = new();
    public Button SaveKey { get; } = new();
    public Button ImportKey { get; } = new();
    public Button RemoveKey { get; } = new();
    public Button TestKey { get; } = new();
    public AiKeySettingsWindow(AiKeyStore store, Func<string?>? chooseFile = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        Title = "AI assistant key"; Width = 650; Height = 390; MinWidth = 480; MinHeight = 320;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SecondaryWindowView.Theme(this);
        var layout = new StackPanel();
        var title = SecondaryWindowView.Text(Title); title.SetResourceReference(StyleProperty, "PageTitle"); layout.Children.Add(title);
        layout.Children.Add(SecondaryWindowView.Text("The optional AI draft uses your own Anthropic API key. "
            + "It is protected for this Windows user and is not saved in the project or report. "
            + "You will see the project information and confirm before an AI draft is sent.", true));
        layout.Children.Add(new Label { Content = "Paste or replace key", Target = KeyEditor });
        KeyEditor.Margin = new Thickness(0, 0, 0, 12); KeyEditor.ToolTip = "Enter a key, then choose Save key or Test key.";
        layout.Children.Add(KeyEditor);
        var actions = new WrapPanel();
        foreach (var (button, label) in new[] { (SaveKey, "Save key"), (ImportKey, "Import key from file"),
            (TestKey, "Test key"), (RemoveKey, "Remove key") })
        {
            button.Content = label; button.Margin = new Thickness(0, 0, 8, 8); actions.Children.Add(button);
        }
        TestKey.ToolTip = "Sends a short request to Anthropic to check the key; usage charges may apply.";
        layout.Children.Add(actions);
        var status = SecondaryWindowView.Text("", true); layout.Children.Add(status);
        var closing = new CancellationTokenSource();
        Closed += (_, _) => closing.Cancel();
        void Error() => status.Text = "Could not access the key. Check the file and Windows account and try again.";
        SaveKey.Click += (_, _) =>
        {
            try { store.Save(KeyEditor.Password); KeyEditor.Clear(); status.Text = "Key saved for this Windows user."; }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or CryptographicException) { Error(); }
        };
        ImportKey.Click += (_, _) =>
        {
            var file = chooseFile != null ? chooseFile() : ChooseFile();
            if (file == null) return;
            try { store.Import(file); KeyEditor.Clear(); status.Text = "Key imported for this Windows user."; }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or CryptographicException) { Error(); }
        };
        RemoveKey.Click += (_, _) =>
        {
            try { store.Remove(); KeyEditor.Clear(); status.Text = "Saved key removed."; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error(); }
        };
        TestKey.Click += async (_, _) =>
        {
            try
            {
                string? key = KeyEditor.Password.Length > 0 ? KeyEditor.Password : store.Read();
                if (string.IsNullOrWhiteSpace(key)) { status.Text = "Enter or import a key first."; return; }
                TestKey.IsEnabled = false; status.Text = "Testing key...";
                AiResult result = await new AiTransport().SendAsync(key, "Reply with OK.", 32, closing.Token);
                status.Text = result.Outcome switch
                {
                    AiOutcome.Success or AiOutcome.Incomplete or AiOutcome.Refusal => "Key works.",
                    AiOutcome.WrongKey => "The key was not accepted.",
                    AiOutcome.Billing => "The account has a billing or credit problem.",
                    AiOutcome.RateLimited => "Rate limited. Try again later.",
                    AiOutcome.NoConnection => "Could not connect.",
                    _ => "The service could not complete the test. Try again later.",
                };
            }
            catch (OperationCanceledException) { status.Text = "Key test cancelled."; }
            catch (AnthropicException) { status.Text = "The service could not complete the test. Try again later."; }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or CryptographicException) { Error(); }
            finally { TestKey.IsEnabled = true; }
        };
        Content = SecondaryWindowView.Frame(layout);
    }

    private string? ChooseFile()
    {
        var dialog = new OpenFileDialog { Title = "Import AI assistant key", Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*" };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }
}
