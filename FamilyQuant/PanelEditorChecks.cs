using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FamilyQuant.Panels;

namespace FamilyQuant;

public sealed record PanelPfamDisplayRow(string Family, string Accession, string Status, string Reason);

public interface IPanelPfamReview
{
    Task<IReadOnlyList<PanelPfamDisplayRow>> CheckAsync(FamilyPanel panel, CancellationToken cancellationToken);
}

public sealed class PanelPfamReview : IPanelPfamReview
{
    public async Task<IReadOnlyList<PanelPfamDisplayRow>> CheckAsync(FamilyPanel panel, CancellationToken cancellationToken)
    {
        var report = await PanelPfamChecker.CheckAsync(panel, cancellationToken: cancellationToken).ConfigureAwait(false);
        return report.Rows.Select(row => new PanelPfamDisplayRow(row.FamilyKey, row.Accession,
            row.Status switch { PanelPfamCheckStatus.Match => "Match", PanelPfamCheckStatus.NameMismatch => "Name mismatch",
                PanelPfamCheckStatus.NotFound => "Not found", _ => "Could not check" },
            row.Reason + (row.ProviderName.Length == 0 ? "" : " Provider: " + row.ProviderName))).ToArray();
    }
}

public interface IPanelAssignmentReadiness
{
    string DescribeAfterChange(FamilyClusterFile file);
}

public sealed class PanelAssignmentReadiness : IPanelAssignmentReadiness
{
    public string DescribeAfterChange(FamilyClusterFile file)
    {
        var result = PanelDerivedDataCurrentness.Check(file);
        return result.IsOutOfDate ? result.Message : string.Empty;
    }
}
