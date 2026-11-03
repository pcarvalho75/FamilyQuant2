using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FamilyQuant.Panels;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void PanelEditorTemplateRoundtripTests()
    {
        EnsureApplication();
        var editor = new PanelEditorWindow(null);
        editor.Draft.Families.Add(new PanelEditorRow { Key = "added", Name = "Added family", Pfam = "PF00003", Subclasses = "part" });
        var project = new FamilyClusterFile();
        var viewModel = new CurationViewModel(project);
        viewModel.ApplyFamilyPanel(editor.Draft.Build());
        string projectPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fqproj");
        string templatePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
        try
        {
            ProjectStore.WriteFqproj(projectPath, project, new List<QuantRow>(), new QuantManifest());
            var opened = ProjectStore.ReadFqproj(projectPath).fcf;
            FamilyPanelTemplate.Write(templatePath, opened.FamilyPanel!);
            var reopenedEditor = new PanelEditorWindow(null);
            try
            {
                reopenedEditor.LoadTemplate(templatePath);
                Check("J1e edited panel survives project save and template export and import",
                    JsonSerializer.Serialize(reopenedEditor.Draft.Build()) == JsonSerializer.Serialize(opened.FamilyPanel)
                    && reopenedEditor.Draft.Families.Any(x => x.Key == "added" && x.Subclasses == "part"));
            }
            finally { reopenedEditor.Close(); }
        }
        finally { if (File.Exists(projectPath)) File.Delete(projectPath); if (File.Exists(templatePath)) File.Delete(templatePath); editor.Close(); }
    }
}
