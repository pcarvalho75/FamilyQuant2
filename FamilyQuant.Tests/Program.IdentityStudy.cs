using System;
using System.IO;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void IdentityStudyTests()
    {
        var probes = IdentitySearchStudy.ReadProbes("1168lines parsed in the fasta DB.\n" +
            "Identity 65% -> 39053 groups (19522 with targets, 1 mixed), score 0.00% (of target families 0.01%)\n");
        Check("identity study retains nonzero mixing when percentage display rounds it to zero",
            probes.Count == 1 && probes[0].Identity == 65 && probes[0].Groups == 39053 &&
            probes[0].Score == 1.0 / 39053 && probes[0].TargetRelativeScore == 1.0 / 19522);
        Check("identity study refuses a missing probe log instead of reporting an empty study",
            Throws<InvalidDataException>(() => IdentitySearchStudy.ReadProbes("no probes recorded")));
        var scan = new[]
        {
            new IdentityScanRow { Identity = 95, Score = 0 },
            new IdentityScanRow { Identity = 90, Score = 0 },
            new IdentityScanRow { Identity = 85, Score = 0.01 }
        };
        Check("identity study distinguishes a five-percent grid from a one-percent probe answer",
            IdentitySearchStudy.LowestAcceptable(scan, 0) == 90 &&
            IdentitySearchStudy.LowestAcceptable(scan, 0) != 87);
    }
}
