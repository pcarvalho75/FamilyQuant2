using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Evidence
{
    public sealed class EvidenceProteinOverride
    {
        public int ProteinIndex { get; init; }
        public string Family { get; init; } = string.Empty;
        public string? Name { get; init; }
    }

    public sealed class EvidenceApplySelection
    {
        public string SourceFingerprint { get; init; } = string.Empty;
        public IReadOnlyList<int> SelectedProteinIndices { get; init; } = Array.Empty<int>();
        public IReadOnlyDictionary<string, string> FamilyNames { get; init; } = new Dictionary<string, string>();
        public IReadOnlyList<EvidenceArchitectureChoice> ArchitectureChoices { get; init; } = Array.Empty<EvidenceArchitectureChoice>();
        public IReadOnlyList<EvidenceProteinOverride> ProteinOverrides { get; init; } = Array.Empty<EvidenceProteinOverride>();
        public NeighbourSearch? SessionSearch { get; init; }
        public IReadOnlyList<NeighbourReference>? SessionReferences { get; init; }
        public IReadOnlyList<MotifPattern>? CheckedMotifs { get; init; }
        public EvidenceTaskChoice? ConfirmedTask { get; init; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public Panels.FamilyPanel? FamilyPanel { get; init; }
    }

    public sealed class EvidenceFamilyChoice
    {
        public string Family { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Selected { get; set; }
    }

    public sealed class EvidenceAssignment
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public EvidenceProteinProposal? AutomaticEvidence { get; set; }
        public int ProteinIndex { get; set; }
        public string Identifier { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string GroupKey { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int PreviousFamilyId { get; set; }
        public int PreviousSearchId { get; set; }
        public int FamilyId { get; set; }
        public EvidenceTier Tier { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public sealed partial class EvidenceRecipe
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<EvidenceManualChoice>? ManualChoices { get; set; }
        public string SourceFingerprint { get; set; } = string.Empty;
        public string SourceStateFingerprint { get; set; } = string.Empty;
        public string AppliedStateFingerprint { get; set; } = string.Empty;
        public string RequestFingerprint { get; set; } = string.Empty;
        public string SourceSha256 { get; set; } = string.Empty;
        public EvidenceClassificationOptions Options { get; set; } = new EvidenceClassificationOptions();
        public List<int> SelectedProteinIndices { get; set; } = new List<int>();
        public List<EvidenceFamilyChoice> Families { get; set; } = new List<EvidenceFamilyChoice>();
        public List<EvidenceProteinOverride> ProteinOverrides { get; set; } = new List<EvidenceProteinOverride>();
        public List<EvidenceAssignment> Assignments { get; set; } = new List<EvidenceAssignment>();
        public string EvidenceProvider { get; set; } = string.Empty;
        public string EvidenceRelease { get; set; } = string.Empty;
        public List<string> EvidenceReleases { get; set; } = new List<string>();
        public string ProgramVersion { get; set; } = string.Empty;
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public bool IncludesSessionEvidence { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public NeighbourSearch? SessionSearch { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<NeighbourReference>? SessionReferences { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<MotifPattern>? CheckedMotifs { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<MotifResult>? CheckedMotifResults { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public EvidenceTaskChoice? ConfirmedTask { get; set; }
    }

    public sealed class EvidenceApplyResult
    {
        public bool Changed { get; init; }
        public EvidenceRecipe Recipe { get; init; } = new EvidenceRecipe();
        public GroupingSnapshot? Undo { get; init; }
    }

    public static partial class EvidenceApply
    {
        public static EvidenceApplyResult Apply(FamilyClusterFile file, EvidenceProposal proposal,
            EvidenceApplySelection selection, string programVersion, Operations.OperationContext? operation = null)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(proposal);
            ArgumentNullException.ThrowIfNull(selection);
            operation?.Report("evidence-apply", "Validating evidence choices");
            selection = Clone(selection);
            string initialPanel = Hash(file.FamilyPanel);
            Panels.FamilyPanel? familyPanel = selection.FamilyPanel ?? file.FamilyPanel;
            familyPanel = familyPanel == null ? null : Panels.FamilyPanelValidation.Clone(familyPanel);
            if (selection.FamilyPanel != null && proposal.PanelContext == null)
                throw new ArgumentException("A panel override requires a panel evidence proposal.", nameof(selection));
            if (familyPanel != null) Panels.FamilyPanelValidation.Validate(familyPanel);
            if (selection.ConfirmedTask != null) EvidenceTaskDiscovery.Validate(file, selection.ConfirmedTask, proposal.Options.ScopeTerms);
            if (selection.SessionSearch == null && selection.SessionReferences != null)
                throw new ArgumentException("Session references require a session search.", nameof(selection));
            bool sessionInput = selection.SessionSearch != null || selection.CheckedMotifs != null;
            if (string.IsNullOrWhiteSpace(programVersion)) throw new ArgumentException("Supply the running program version.", nameof(programVersion));
            if (selection.SourceFingerprint != proposal.SourceFingerprint)
                throw new InvalidOperationException("The selection does not belong to this evidence proposal.");
            string request = Hash(new
            {
                proposal.SourceFingerprint, proposal.Options,
                Proteins = proposal.Proteins.Select(p => new { p.ProteinIndex, p.Identifier, p.Family, p.Tier, p.PreviousFamilyId }),
                Selected = selection.SelectedProteinIndices.OrderBy(i => i),
                Names = selection.FamilyNames.OrderBy(p => p.Key, StringComparer.Ordinal),
                Splits = selection.ArchitectureChoices.OrderBy(c => c.Family, StringComparer.Ordinal).ThenBy(c => c.ArchitectureKey, StringComparer.Ordinal),
                Overrides = selection.ProteinOverrides.OrderBy(o => o.ProteinIndex), programVersion,
                Provider = sessionInput ? file.EvidenceSource?.Provider ?? string.Empty : file.EvidenceSource?.Provider,
                Release = sessionInput ? file.EvidenceSource?.Release ?? string.Empty : file.EvidenceSource?.Release,
            });
            if (sessionInput) request = Hash(new { Request = request, selection.SessionSearch, selection.SessionReferences, selection.CheckedMotifs });
            if (selection.ConfirmedTask != null) request = Hash(new { Request = request, selection.ConfirmedTask });
            if (familyPanel != null) request = Hash(new { Request = request, FamilyPanel = familyPanel });
            if (proposal.PanelContext != null) request = Hash(new { Request = request, proposal.PanelContext });
            if (proposal.ManualChoices?.Count > 0) request = Hash(new { Request = request, ManualChoices = proposal.ManualChoices.OrderBy(c => c.ProteinIndex) });
            if (file.EvidenceRecipe?.RequestFingerprint == request
                && file.EvidenceRecipe.AppliedStateFingerprint == StateFingerprint(file, session: file.EvidenceRecipe))
                return new EvidenceApplyResult { Recipe = Clone(file.EvidenceRecipe) };
            if (EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems) != proposal.SourceFingerprint)
                throw new InvalidOperationException("The evidence proposal no longer matches the current proteins.");
            ManualFamilyChoices.ValidateProposal(file, proposal);
            ValidatePanelProposal(file, proposal, selection, familyPanel);
            string initialState = StateFingerprint(file);
            string initialMotifs = Hash(file.Motifs);
            PreparedSession session = PrepareSession(file, proposal, selection, operation);

            var selected = selection.SelectedProteinIndices.ToHashSet();
            if (selected.Count == 0 || selected.Count != selection.SelectedProteinIndices.Count)
                throw new ArgumentException("Select distinct assigned protein indices.", nameof(selection));
            var proposed = proposal.Proteins.ToDictionary(p => p.ProteinIndex);
            foreach (int index in selected)
            {
                if (!proposed.TryGetValue(index, out EvidenceProteinProposal? row) || row.Tier == EvidenceTier.Unassigned || row.Family.Length == 0
                    || index < 0 || index >= file.MyExtendedFastaItems.Count || NearestReviewed.IsDecoy(file.MyExtendedFastaItems[index]))
                    throw new ArgumentException("Only assigned target proteins can be selected.", nameof(selection));
                if (file.LockedUserIds.Contains(file.MyExtendedFastaItems[index].UserID))
                    throw new InvalidOperationException("A selected protein belongs to a locked family. Unlock it before applying the selection.");
            }
            var familyComparer = proposal.PanelContext == null ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var familyNames = proposal.Families.ToDictionary(f => f.Family, f => f.SuggestedName, familyComparer);
            foreach (var pair in selection.FamilyNames)
            {
                if (!familyNames.ContainsKey(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                    throw new ArgumentException("Family names must refer to the proposal and cannot be empty.", nameof(selection));
                familyNames[pair.Key] = pair.Value.Trim();
            }
            var architecture = (proposal.PanelContext == null ? EvidenceArchitecture.Suggest(file.MyExtendedFastaItems, proposal)
                : Array.Empty<EvidenceArchitectureProposal>())
                .ToDictionary(a => a.Family, StringComparer.OrdinalIgnoreCase);
            var splits = new Dictionary<string, EvidenceArchitectureChoice>(StringComparer.Ordinal);
            foreach (EvidenceArchitectureChoice choice in selection.ArchitectureChoices)
            {
                if (!architecture.TryGetValue(choice.Family, out EvidenceArchitectureProposal? family)
                    || !family.Groups.Any(g => g.Key == choice.ArchitectureKey))
                    throw new ArgumentException("An architecture choice does not belong to the proposal.", nameof(selection));
                string key = GroupKey(family.Family, choice.ArchitectureKey);
                if (!splits.TryAdd(key, new EvidenceArchitectureChoice
                {
                    Family = family.Family, ArchitectureKey = choice.ArchitectureKey, Split = choice.Split,
                    Name = string.IsNullOrWhiteSpace(choice.Name) ? family.Groups.Single(g => g.Key == choice.ArchitectureKey).SuggestedName : choice.Name.Trim(),
                })) throw new ArgumentException("An architecture choice occurs more than once.", nameof(selection));
            }
            var overrides = new Dictionary<int, EvidenceProteinOverride>();
            foreach (EvidenceProteinOverride item in selection.ProteinOverrides)
            {
                if (!selected.Contains(item.ProteinIndex) || string.IsNullOrWhiteSpace(item.Family)
                    || (item.Name != null && string.IsNullOrWhiteSpace(item.Name)) || !overrides.TryAdd(item.ProteinIndex, item))
                    throw new ArgumentException("Each protein override must refer to one selected protein and supply a family.", nameof(selection));
            }
            var assignments = new List<EvidenceAssignment>();
            foreach (int index in selected.OrderBy(i => i))
            {
                EvidenceProteinProposal row = proposed[index];
                string family = row.Family;
                string name = familyNames[family];
                string key = GroupKey(family, string.Empty);
                if (overrides.TryGetValue(index, out EvidenceProteinOverride? item))
                {
                    family = familyNames.Keys.FirstOrDefault(f => f.Equals(item.Family.Trim(), StringComparison.OrdinalIgnoreCase)) ?? item.Family.Trim();
                    name = item.Name?.Trim() ?? familyNames.GetValueOrDefault(family, family);
                    // A named single-protein override is an explicit group choice, independent of inferred architecture.
                    key = item.Name == null ? GroupKey(family, string.Empty) : GroupKey(family, "name", name);
                }
                else if (proposal.PanelContext == null)
                {
                    EvidenceArchitectureGroup group = architecture[family].Groups.Single(g => g.ProteinIndices.Contains(index));
                    if (splits.TryGetValue(GroupKey(family, group.Key), out EvidenceArchitectureChoice? split) && split.Split)
                    {
                        key = GroupKey(family, group.Key);
                        name = split.Name;
                    }
                }
                FastaItemExtended protein = file.MyExtendedFastaItems[index];
                assignments.Add(new EvidenceAssignment
                {
                    ProteinIndex = index, Identifier = protein.SequenceIdentifier, Family = family, Name = name, GroupKey = key,
                    PreviousFamilyId = protein.UserID, PreviousSearchId = protein.USearchID, Tier = row.Tier, Reason = row.Reason,
                    AutomaticEvidence = row.AutomaticEvidence == null ? null : Clone(row.AutomaticEvidence),
                });
            }
            int next = MaximumId(file);
            var existingFamilies = CurrentFamilyIdentities(file);
            foreach (var group in assignments.GroupBy(a => a.GroupKey, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                int reused = ReusableFamilyId(existingFamilies, group.First().Family, group.Key, proposal.PanelContext != null);
                if (reused > 0)
                {
                    foreach (EvidenceAssignment assignment in group) assignment.FamilyId = reused;
                    continue;
                }
                if (next == int.MaxValue) throw new InvalidOperationException("There are no unused family numbers above the existing numbers.");
                next++;
                foreach (EvidenceAssignment assignment in group) assignment.FamilyId = next;
            }
            var recipe = new EvidenceRecipe
            {
                SourceFingerprint = proposal.SourceFingerprint,
                SourceSha256 = file.SourceSha256, RequestFingerprint = request, Options = Clone(proposal.Options),
                SelectedProteinIndices = selected.OrderBy(i => i).ToList(),
                Families = familyNames.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new EvidenceFamilyChoice
                {
                    Family = p.Key, Name = p.Value, Selected = assignments.Any(a => familyComparer.Equals(a.Family, p.Key)),
                }).ToList(),
                ArchitectureChoices = splits.Values.OrderBy(c => c.Family, StringComparer.Ordinal).ThenBy(c => c.ArchitectureKey, StringComparer.Ordinal).ToList(),
                ProteinOverrides = Clone(selection.ProteinOverrides.OrderBy(o => o.ProteinIndex).ToList()), Assignments = assignments,
                ManualChoices = proposal.ManualChoices?.Select(c => c with { Selected = selected.Contains(c.ProteinIndex) }).ToList(),
                EvidenceProvider = file.EvidenceSource?.Provider ?? string.Empty, EvidenceRelease = file.EvidenceSource?.Release ?? string.Empty,
                EvidenceReleases = file.MyExtendedFastaItems.Select(p => p.Evidence?.Source?.Release).Append(file.EvidenceSource?.Release)
                    .Where(r => !string.IsNullOrEmpty(r)).Select(r => r!).Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).ToList(),
                ProgramVersion = programVersion,
                IncludesSessionEvidence = sessionInput, SessionSearch = session.Search, SessionReferences = session.References,
                CheckedMotifs = session.Motifs, CheckedMotifResults = session.Results,
                ConfirmedTask = selection.ConfirmedTask,
                FamilyPanel = familyPanel,
                PanelContext = proposal.PanelContext == null ? null : Clone(proposal.PanelContext),
            };
            recipe.FamilyIdentities = ProjectFamilyIdentities(file, recipe);
            foreach (var family in assignments.Where(a => !familyNames.ContainsKey(a.Family)).GroupBy(a => a.Family, StringComparer.Ordinal))
                recipe.Families.Add(new EvidenceFamilyChoice { Family = family.Key, Name = family.First().Name, Selected = true });
            if (proposal.PanelContext != null)
            {
                recipe.PanelProteinAssignmentsPanelFingerprint = Panels.PanelDerivedDataCurrentness.FamilyFingerprint(familyPanel!);
                recipe.PanelProteinAssignments = PanelAssignmentEvidenceStore.Stage(proposal, assignments);
                recipe.PanelProteinAssignmentsFingerprint = PanelAssignmentEvidenceStore.Fingerprint(file,
                    recipe.PanelProteinAssignments, recipe, SessionSource(file, recipe.SessionSearch), assignments);
            }
            if (initialState != StateFingerprint(file) || initialMotifs != Hash(file.Motifs)
                || Hash(file.FamilyPanel) != initialPanel)
                throw new InvalidOperationException("The curation changed while the evidence choices were validated.");
            recipe.SourceStateFingerprint = StateFingerprint(file, session: recipe);
            recipe.AppliedStateFingerprint = StateFingerprint(file, assignments, recipe, projectSession: true);
            return Commit(file, recipe, operation);
        }

        public static EvidenceApplyResult ApplyRecipe(FamilyClusterFile file, EvidenceRecipe recipe, Operations.OperationContext? operation = null)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(recipe);
            operation?.Report("evidence-apply", "Validating the saved evidence recipe");
            recipe = Clone(recipe);
            if (recipe.FamilyPanel != null) Panels.FamilyPanelValidation.Validate(recipe.FamilyPanel);
            if (recipe.ConfirmedTask != null) EvidenceTaskDiscovery.Validate(file, recipe.ConfirmedTask, recipe.Options.ScopeTerms);
            string state = StateFingerprint(file, session: recipe);
            if (state == recipe.AppliedStateFingerprint && file.EvidenceRecipe != null
                && JsonSerializer.Serialize(file.EvidenceRecipe) == JsonSerializer.Serialize(recipe))
                return new EvidenceApplyResult { Recipe = recipe };
            if (state != recipe.SourceStateFingerprint || file.SourceSha256 != recipe.SourceSha256
                || EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems) != recipe.SourceFingerprint)
                throw new InvalidOperationException("The saved recipe does not match this source or its applied state.");
            ValidatePanelRecipe(file, recipe);
            ManualFamilyChoices.ValidateRecipe(file, recipe);
            if (recipe.Assignments.Count == 0 || recipe.Assignments.Select(a => a.ProteinIndex).Distinct().Count() != recipe.Assignments.Count
                || !recipe.Assignments.Select(a => a.ProteinIndex).OrderBy(i => i).SequenceEqual(recipe.SelectedProteinIndices))
                throw new ArgumentException("The recipe's assignments do not match its selection.", nameof(recipe));
            int maximum = MaximumId(file);
            var existingFamilies = CurrentFamilyIdentities(file);
            foreach (EvidenceAssignment assignment in recipe.Assignments)
            {
                if (assignment.ProteinIndex < 0 || assignment.ProteinIndex >= file.MyExtendedFastaItems.Count)
                    throw new ArgumentException("A recipe protein index is outside the source.", nameof(recipe));
                FastaItemExtended protein = file.MyExtendedFastaItems[assignment.ProteinIndex];
                if (NearestReviewed.IsDecoy(protein) || assignment.Identifier != protein.SequenceIdentifier
                    || assignment.PreviousFamilyId != protein.UserID || assignment.PreviousSearchId != protein.USearchID
                    || assignment.FamilyId <= 0
                    || (assignment.FamilyId <= maximum && (recipe.FamilyIdentities == null
                        || ReusableFamilyId(existingFamilies, assignment.Family, assignment.GroupKey, recipe.PanelContext != null) != assignment.FamilyId))
                    || string.IsNullOrWhiteSpace(assignment.Name)
                    || file.LockedUserIds.Contains(protein.UserID))
                    throw new ArgumentException("A recipe assignment is incompatible with its source.", nameof(recipe));
            }
            if (recipe.Assignments.GroupBy(a => a.FamilyId).Any(g => g.Select(a => (a.GroupKey, a.Name)).Distinct().Count() != 1)
                || recipe.Assignments.GroupBy(a => a.GroupKey, StringComparer.Ordinal).Any(g => g.Select(a => a.FamilyId).Distinct().Count() != 1)
                || StateFingerprint(file, recipe.Assignments, recipe, projectSession: true) != recipe.AppliedStateFingerprint)
                throw new ArgumentException("The recipe's resulting grouping does not match its recorded state.", nameof(recipe));
            ValidateSearch(file, recipe.SessionSearch, recipe.SessionReferences, recipe.Options, operation, allowLegacyCoverage: true,
                allowPanelReferences: recipe.PanelContext != null);
            foreach (MotifPattern motif in recipe.CheckedMotifs ?? new List<MotifPattern>()) MotifCheck.Compile(motif.Pattern);
            return Commit(file, recipe, operation);
        }

        private static EvidenceApplyResult Commit(FamilyClusterFile file, EvidenceRecipe recipe, Operations.OperationContext? operation)
        {
            EvidenceRecipe stored = Clone(recipe);
            EvidenceRecipe returned = Clone(recipe);
            EvidenceProvenance? source = SessionSource(file, stored.SessionSearch);
            List<MotifPattern>? motifs = stored.CheckedMotifs == null ? file.Motifs : Clone(stored.CheckedMotifs);
            Panels.FamilyPanel? panel = stored.FamilyPanel == null ? file.FamilyPanel : Panels.FamilyPanelValidation.Clone(stored.FamilyPanel);
            var panelAssignments = stored.PanelProteinAssignments == null ? null : Clone(stored.PanelProteinAssignments);
            var subclassLabels = stored.ProteinSubclassLabels == null ? null : Clone(stored.ProteinSubclassLabels);
            GroupingSnapshot undo = GroupingSnapshot.CaptureEvidence(file);
            operation?.Report("evidence-apply", "Publishing the reviewed evidence choices", 1, 1);
            void Publish()
            {
                if (StateFingerprint(file, session: stored) != stored.SourceStateFingerprint)
                    throw new InvalidOperationException("The curation changed while the evidence transaction was prepared.");
                ValidateFamilyIdentities(file, stored);
                operation?.ThrowIfCancellationRequested();
                foreach (EvidenceAssignment assignment in stored.Assignments)
                {
                    FastaItemExtended protein = file.MyExtendedFastaItems[assignment.ProteinIndex];
                    protein.UserID = assignment.FamilyId;
                    protein.UserAnnotation = assignment.Name;
                    file.LockedUserIds.Add(assignment.FamilyId);
                }
                file.EvidenceSource = source;
                file.Motifs = motifs;
                file.EvidenceRecipe = stored;
                file.FamilyPanel = panel;
                file.PanelProteinAssignments = panelAssignments;
                file.PanelProteinAssignmentsFingerprint = stored.PanelProteinAssignmentsFingerprint;
                file.PanelProteinAssignmentsPanelFingerprint = stored.PanelProteinAssignmentsPanelFingerprint;
                file.ProteinSubclassLabels = subclassLabels;
                file.ProteinSubclassLabelsPanelFingerprint = stored.ProteinSubclassLabelsPanelFingerprint;
                file.ProteinSubclassLabelsFingerprint = stored.ProteinSubclassLabelsFingerprint;
            }
            if (operation == null) Publish();
            else operation.Commit(Publish);
            return new EvidenceApplyResult { Changed = true, Recipe = returned, Undo = undo };
        }

        private static int MaximumId(FamilyClusterFile file) => file.MyExtendedFastaItems.Select(p => Math.Max(p.UserID, p.USearchID))
            .Concat(file.LockedUserIds).Append(0).Max();

        private static string StateFingerprint(FamilyClusterFile file, IReadOnlyList<EvidenceAssignment>? assignments = null,
            EvidenceRecipe? session = null, bool projectSession = false)
        {
            var rows = file.MyExtendedFastaItems.ToList();
            var locks = new HashSet<int>(file.LockedUserIds);
            if (assignments != null)
            {
                foreach (EvidenceAssignment assignment in assignments)
                {
                    FastaItemExtended source = rows[assignment.ProteinIndex];
                    rows[assignment.ProteinIndex] = new FastaItemExtended
                    {
                        SequenceIdentifier = source.SequenceIdentifier, Sequence = source.Sequence, Evidence = source.Evidence,
                        UserID = assignment.FamilyId, USearchID = source.USearchID, UserAnnotation = assignment.Name,
                    };
                    locks.Add(assignment.FamilyId);
                }
            }
            string curation = Hash(new
            {
                Proteins = EvidenceClassifier.Fingerprint(rows), file.SourceSha256, file.SourceSequenceCount,
                file.WordSimilarity, file.IsKeywordSuperCluster, Locks = locks.OrderBy(i => i),
                file.DomainSource, EvidenceSource = CurationSource(projectSession ? SessionSource(file, session?.SessionSearch) : file.EvidenceSource),
            });
            // Keep earlier recipes' state hashes unchanged when they have no session evidence.
            string state = session?.IncludesSessionEvidence == true ? Hash(new
            {
                Curation = curation, Motifs = projectSession ? session.CheckedMotifs ?? file.Motifs : file.Motifs,
                Session = Hash(new { session.SessionSearch, session.SessionReferences, session.CheckedMotifs, session.CheckedMotifResults }),
            }) : curation;
            if (session?.ConfirmedTask != null) state = Hash(new { State = state, session.ConfirmedTask });
            if (session?.FamilyIdentities != null) state = Hash(new { State = state, session.FamilyIdentities });
            return session?.FamilyPanel == null ? state : Hash(new
            {
                State = state, FamilyPanel = session.PanelContext == null ? (object?)(projectSession ? session.FamilyPanel : file.FamilyPanel)
                    : (projectSession ? session.FamilyPanel : file.FamilyPanel) is { } currentPanel
                        ? Panels.PanelDerivedDataCurrentness.FamilyFingerprint(currentPanel) : null,
            });
        }

        private static string GroupKey(params string[] parts) => JsonSerializer.Serialize(parts);
        private static EvidenceProvenance? CurationSource(EvidenceProvenance? source)
        {
            if (source?.FamilyWorld == null) return source;
            var curation = Clone(source);
            curation.FamilyWorld = null;
            return curation;
        }
        private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
        internal static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    }
}
