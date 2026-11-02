# Study code used only by reports

X9 is a small source move. The Results screen exports an evidence pack, so its
presentation code stays in App. No new Reports project or separate tool is added.
Only two static study classes move to the existing headless report/test harness.
Their class bodies are unchanged, and their chart models keep their Core types.

| Moved operation class | New source | Caller |
|---|---|---|
| DeNovoScoreScatterStudy | `FamilyQuant.Tests/Studies/DeNovoScoreScatterStudy.cs` | Report harness |
| SetNoveltyStudy | `FamilyQuant.Tests/Studies/SetNoveltyStudy.cs` | Report harness |

| Kept code | Location | Reason |
|---|---|---|
| PresentationFigures, PresentationEvidencePack and talk chart bases | App | Results calls the evidence-pack exporter |
| DeNovoScoreScatter, ScoreScatterPoint, SetNovelty, SetNoveltyRegion, SetNoveltyConfidence | Core | App chart declarations use these model types |
| Agreement, SourceVenn, IdentificationConcordance and AcceptedScoreScatter studies | Core | Full XIC computation calls them |
| DeNovoOverlapStudy | Core | The De novo screen calls it |
| ReferenceEnvelopeEstimator | Core | Quantitation and presentation use it |
| PsmDeNovoCounterpartStudy | Core | The family-world validation work still depends on it |

The chart bases, axis ladders, RGB factories and presentation validation named in
[H1 notes](housekeeping/H1-NOTES.md) are unchanged. Protected talk producers,
the deck and canonical outputs are untouched. The moved classes are compiled
only into Tests, which App does not reference or publish.

`--test-report-study-boundary` checks assembly ownership, model identity and the
retained screen dependencies. `--report-study-snapshot <new-folder>` writes six
labelled synthetic fixtures through the program: two CSVs, two Markdown files
and two PNGs. Compare their bytes before the move, after it and on a repeat run.
These fixtures are checks, not scientific deliverables.
Both flags register in `Program.Commands.ResultsStudies.cs`; Main gains no flag branch.

A fresh stored-project golden export checks the four frozen CSVs. Compare the
whole evidence pack against the prior app pack as well. Its existing
root `generatedUtc` value comes from the clock and is the sole permitted difference;
mask that value only and compare every remaining byte. No timestamp or
scientific rule in the exporter is changed by this move.

The first synthetic set-novelty PNG probe differed at 18 anti-aliased text pixels.
A controlled rebuild with the original classes produced all six files byte equal
to both moved-class runs. Thus that variation also occurs without the move.
The first baseline, original-class control and both moved runs are retained with
hash tables under the lane's X9 output. No presentation source was edited to
make the probe match. The strict whole-pack check still permits only the root generatedUtc.

The final native golden check passed 4/0/0. The complete app evidence packs had
41 files each, identical file sets, and zero unexpected byte differences. Forty
files were byte equal. The JSON matched after masking only its one root
generatedUtc value; the five stored variant timestamps and every other byte
remained checked. Comparison evidence is `out-results/X9/pack-byte-comparison.csv`.
