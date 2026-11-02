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
    private string worldDescription = "Family world: not fetched.";

    public string FamilyWorldState
    {
        get
        {
            var world = Document.EvidenceSource?.FamilyWorld;
            if (world == null) return "Family world: not fetched.";
            if (!ReferenceEquals(world, describedWorld))
            {
                describedWorld = world;
                worldDescription = "Family world stored: release " + world.Release + "; scope: "
                    + string.Join("; ", world.Task.ScopeTerms) + "; "
                    + world.Entries.Count.ToString("N0", CultureInfo.CurrentCulture) + " proteins in "
                    + world.Families.Count.ToString("N0", CultureInfo.CurrentCulture) + " families; "
                    + (JsonSerializer.SerializeToUtf8Bytes(world).Length / 1048576.0).ToString("N2", CultureInfo.CurrentCulture)
                    + " MB. Save to keep it. Compute uses this stored world.";
            }
            return worldDescription;
        }
    }

    public async Task FetchFamilyWorldAsync(EvidenceTaskChoice task, OperationContext operation, HttpMessageHandler? handler = null)
    {
        // The Core fetch owns the single atomic commit, including source and cancellation checks.
        await FamilyWorldFetcher.FetchAndStoreAsync(Document, task, operation, handler);
        Changed(nameof(FamilyWorldState));
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }
}
