using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace FamilyQuant;

public sealed partial class CurationViewModel
{
    private FamilyWorldSnapshot? describedWorld;
    private string worldDescription = "UniProt families: not fetched.";

    public string FamilyWorldState
    {
        get
        {
            var world = Document.EvidenceSource?.FamilyWorld;
            if (world == null) return "UniProt families: not fetched.";
            if (!ReferenceEquals(world, describedWorld))
            {
                describedWorld = world;
                string proteinLabel = world.Entries.Count == 1 ? "entry" : "entries";
                string familyLabel = world.Families.Count == 1 ? "family" : "families";
                worldDescription = "UniProt families in the open project (release " + world.Release + ", scope "
                    + string.Join(", ", world.Task.ScopeTerms) + "): "
                    + world.Families.Count.ToString("N0", CultureInfo.CurrentCulture) + " " + familyLabel + ", "
                    + world.Entries.Count.ToString("N0", CultureInfo.CurrentCulture) + " " + proteinLabel + " ("
                    + (JsonSerializer.SerializeToUtf8Bytes(world).Length / 1048576.0).ToString("N2", CultureInfo.CurrentCulture)
                    + " MB). Use File > Save to keep them. Quantify uses these references.";
            }
            return worldDescription;
        }
    }

    public async Task FetchFamilyWorldAsync(EvidenceTaskChoice task, OperationContext operation, HttpMessageHandler? handler = null)
    {
        // The Core fetch owns the single atomic commit, including source and cancellation checks.
        await FamilyWorldFetcher.FetchAndStoreAsync(Document, task, operation, handler);
        Changed(nameof(FamilyWorldState));
        RefreshGroupingWorkflow();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }
}
