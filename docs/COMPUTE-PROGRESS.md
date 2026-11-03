# Compute and project-open progress

Quantify > Compute and project-open attribution use the shared operation host. The wait
screen shows the current phase and item, a bar against a known total, elapsed time for the
whole operation, time since the last step and Cancel. Opening and its attribution share
one start time and log. Compute and its attached family attribution also share them.

The existing production loops report inputs, source files and rows, source studies,
acquisitions, precursor cache-check and extraction batches, detector scans, peptide
queries, family-world phases and stored studies. Cache answers count as work and say
that they came from the cache. Query, row and precursor updates use batches of 128;
the last batch always reports the exact final count. The scoring, extraction, filtering,
study calculations and their order remain the production defaults.

Every counted step is recorded with its UTC timestamp in a UTF-8 operation log under
`%LOCALAPPDATA%/FamilyQuant2/operation-logs/`. The wait screen's Operation log button
shows the current record. A native driver can read `ComputeXicViewModel.OperationLogPath`
or `MainWindow.ProjectOpenLogPath`. Legacy engine detail lines are retained in the log
without replacing the counted bar. Failures retain their diagnostics there.

After a Release build, run:

```powershell
& ./FamilyQuant.Tests/bin/Release/net10.0-windows7.0/FamilyQuant.Tests.exe --test-compute-progress <fresh-output>
```

The check is registered once in DefaultSuite and runs in en-US and pt-BR. It runs public Compute on two synthetic
acquisitions with validated extraction and detector caches. A per-call internal reader
supplies only the fixture TIC; production reads the native acquisition chromatogram.
It checks ordered phases and totals, cache batches, the visible clocks, persistent logs,
then opens the computed fixture twice to check a search and a verified cache hit.
Stored quantitation, spectrum rows and every manifest field are compared byte for byte
with the native no-progress baseline, apart from the two computation timestamp fields.
This synthetic/cache proof does not establish fresh acquisition reproduction.

The worker-only windowless measurement command is `--measure-compute-progress <config.json>`.
Its configuration names `Project`, `Raw`, `Sepro`, `DeNovo`, `Cache` and a fresh `Output`.
Mutable paths must be inside the lane's owned proof folder; shared lab inputs are read only.
It checks complete validated precursor and detector cache coverage before Compute so a missing
entry cannot unexpectedly load a full MS1 map. It writes the actual PID, timestamped steps,
per-step gaps, peak memory, Compute completion and input-preservation result. Saving a
second full project is optional (`SaveProject: true`), separate from the measured Compute;
it can require additional serialization memory. The normal measurement retains the
computed spectrum CSV and does not request that extra copy.
