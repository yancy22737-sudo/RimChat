using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Config;
using RimChat.Prompting;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: prompt runtime variable catalog and prompt node schema.
    /// Responsibility: provide runtime-consistent known-variable sets for template validation.
    /// </summary>
    internal sealed class TemplateVariableValidationContext
    {
        private static readonly IReadOnlyDictionary<string, string[]> NodeInjectedVariablePaths =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["api_limits_node_template"] = new[] { "dialogue.api_limits_body" },
                ["response_contract_node_template"] = new[] { "dialogue.response_contract_body" },
                ["strategy_player_negotiator_context_template"] = new[] { "dialogue.strategy_player_negotiator_context_body" },
                ["strategy_fact_pack_template"] = new[] { "dialogue.strategy_fact_pack_body" },
                ["strategy_scenario_dossier_template"] = new[] { "dialogue.strategy_scenario_dossier_body" }
            };

        private readonly HashSet<string> _knownVariables;

        internal string Signature { get; }
        internal IReadOnlyCollection<string> KnownVariables => _knownVariables;

        private TemplateVariableValidationContext(IEnumerable<string> knownVariables, string signature)
        {
            _knownVariables = new HashSet<string>(
                (knownVariables ?? Enumerable.Empty<string>())
                    .Select(NormalizePath)
                    .Where(item => item.Length > 0),
                StringComparer.OrdinalIgnoreCase);
            Signature = signature ?? string.Empty;
        }

        internal bool Contains(string variablePath)
        {
            string normalized = NormalizePath(variablePath);
            return normalized.Length > 0 && _knownVariables.Contains(normalized);
        }

        internal static TemplateVariableValidationContext CreateDefault()
        {
            return BuildContext("runtime.default", Enumerable.Empty<string>());
        }

        internal static TemplateVariableValidationContext FromAdditionalKnownVariables(IEnumerable<string> additionalKnownVariables)
        {
            HashSet<string> extras = NormalizeSet(additionalKnownVariables);
            string signature = "runtime.extra:" + BuildSetSignature(extras);
            return BuildContext(signature, extras);
        }

        internal static TemplateVariableValidationContext ForPromptWorkspaceSection(
            RimTalkPromptChannel rootChannel,
            string promptChannel,
            string sectionId)
        {
            string normalizedChannel = RimTalkPromptEntryChannelCatalog.NormalizeForRoot(promptChannel, rootChannel);
            string normalizedSection = PromptSectionSchemaCatalog.NormalizeSectionId(sectionId);
            string signature = $"workspace.section:{rootChannel}:{normalizedChannel}:{normalizedSection}";
            return BuildContext(signature, Enumerable.Empty<string>());
        }

        internal static TemplateVariableValidationContext ForPromptWorkspaceNode(
            RimTalkPromptChannel rootChannel,
            string promptChannel,
            string nodeId)
        {
            string normalizedChannel = RimTalkPromptEntryChannelCatalog.NormalizeForRoot(promptChannel, rootChannel);
            string normalizedNode = PromptUnifiedNodeSchemaCatalog.NormalizeId(nodeId);
            IEnumerable<string> injected = ResolveNodeInjectedVariablePaths(normalizedNode);
            string signature = $"workspace.node:{rootChannel}:{normalizedChannel}:{normalizedNode}:{BuildSetSignature(injected)}";
            return BuildContext(signature, injected);
        }

        private static TemplateVariableValidationContext BuildContext(
            string signature,
            IEnumerable<string> extraKnownVariables)
        {
            HashSet<string> known = NormalizeSet(PromptVariableCatalog.GetAll());
            foreach (string item in NormalizeSet(extraKnownVariables))
            {
                known.Add(item);
            }

            return new TemplateVariableValidationContext(known, signature);
        }

        private static IEnumerable<string> ResolveNodeInjectedVariablePaths(string nodeId)
        {
            string normalized = PromptUnifiedNodeSchemaCatalog.NormalizeId(nodeId);
            return NodeInjectedVariablePaths.TryGetValue(normalized, out string[] values)
                ? values
                : Enumerable.Empty<string>();
        }

        private static HashSet<string> NormalizeSet(IEnumerable<string> values)
        {
            return new HashSet<string>(
                (values ?? Enumerable.Empty<string>())
                    .Select(NormalizePath)
                    .Where(item => item.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().ToLowerInvariant();
        }

        private static string BuildSetSignature(IEnumerable<string> values)
        {
            return string.Join(",",
                NormalizeSet(values)
                    .OrderBy(item => item, StringComparer.OrdinalIgnoreCase));
        }
    }
}
