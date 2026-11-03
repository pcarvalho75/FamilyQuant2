# Ledger receipts for moved composition and TIC facts

The declaration comparator is unchanged: an exact literal must have the same
count within the uniquely bound member, and the complete member tokens must
match. Neither condition holds for the old signatures below. Each old signature
now forwards to a new overload; its literal count is zero. The two new overloads
also have changed bodies, so a relocation alone cannot establish equality.

The frozen four-argument `CsvReport.BuildFamilyComposition` had these facts.
The four comma observations identify four occurrences of the same literal; the
other literals each occurred once. Each now occurs zero times in that original
signature. The existing shell composition proof independently invokes the
frozen and current historical overloads for all policies, several denominator
and data cases, and both cultures; it records complete equal CSV bytes and
checks the relevant fragment appears in both outputs.

| Frozen observation | Literal | Frozen count | Original-signature current count |
|---|---|---:|---:|
| FQ15-DE97BFC1DC1A | comma at line 93 | 4 | 0 |
| FQ15-7D56F063F66C | comma at line 106 | 4 | 0 |
| FQ15-7036894F9DF7 | comma at line 111 | 4 | 0 |
| FQ15-DC72052AA7DC | comma at line 115 | 4 | 0 |
| FQ15-04AAB575A5CB | family_user_id | 1 | 0 |
| FQ15-B32233DB2707 | description | 1 | 0 |
| FQ15-E67177435835 | signal_primary_estimate | 1 | 0 |
| FQ15-06E740B998A5 | fraction | 1 | 0 |
| FQ15-DF6B9D200673 | basis | 1 | 0 |
| FQ15-24ED395047BF | semicolon and space | 1 | 0 |
| FQ15-83110D07824B | shared-row policy phrase | 1 | 0 |
| FQ15-9FDF0CC7EACE | shared-between-families row | 1 | 0 |
| FQ15-2025778EF37B | not-attributed row | 1 | 0 |

The frozen three-argument `PrepareFullXic` had the following eight distinct
literals, each once; each occurs zero times in the current three-argument
wrapper. They now occur in the five-argument overload, whose search call changed
to use the curation cache. The native TIC proof runs the frozen member and
current five-argument production overload on synthetic no-TIC, missing-run,
duplicate-run, invalid-extra-TIC and valid cases in both cultures, and repeats
the current calls with private caches. It checks the full refusal, run count and
TIC, and binds each frozen message fragment to an observed equal result. It does
not claim that complete member tokens or homology searches are identical.

| Frozen observation | Message fragment | Frozen count | Original-signature current count |
|---|---|---:|---:|
| FQ15-83E85F7C2532 | the TIC of run | 1 | 0 |
| FQ15-17EC0A3B6C6C | is recorded twice with different values | 1 | 0 |
| FQ15-3F227ACEA320 | the project records no total ion current | 1 | 0 |
| FQ15-A04A4E4C4066 | no positive TIC is recorded for run(s) | 1 | 0 |
| FQ15-CC4116F91EF3 | comma and space | 1 | 0 |
| FQ15-3B44414FB4F4 | and | 1 | 0 |
| FQ15-E013A7902055 | more | 1 | 0 |
| FQ15-1C817C5A8896 | a recorded TIC is not a positive number | 1 | 0 |

The native proof outputs are bounded fixture evidence. They are not evidence of
fresh acquisition processing or a scientific accuracy claim.
