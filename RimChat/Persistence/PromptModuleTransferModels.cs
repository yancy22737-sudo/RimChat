using System;
using System.Collections.Generic;
using RimChat.Config;
using Verse;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: PromptUnifiedNodeSlot enum.
    /// Responsibility: data model for single-module export/import JSON transport.
    /// </summary>
    [Serializable]
    internal sealed class PromptModuleExportPayload
    {
        public int FormatVersion = 1;
        public string NodeId = string.Empty;
        public string DisplayName = string.Empty;
        public string Slot = PromptUnifiedNodeSlot.MainChainAfter.ToSerializedValue();
        public int Order = 1000;
        public bool Enabled = true;
        public string Content = string.Empty;
    }

    /// <summary>
    /// Dependencies: PromptModuleExportPayload.
    /// Responsibility: multi-module export/import bundle envelope.
    /// </summary>
    [Serializable]
    internal sealed class PromptModuleExportBundle
    {
        public int FormatVersion = 1;
        public List<PromptModuleExportPayload> Modules = new List<PromptModuleExportPayload>();
    }

    /// <summary>
    /// Dependencies: none.
    /// Responsibility: persist custom node registration metadata in the unified catalog.
    /// </summary>
    [Serializable]
    public sealed class PromptUnifiedNodeRegistration : IExposable
    {
        public string NodeId = string.Empty;
        public string DisplayName = string.Empty;

        public void ExposeData()
        {
            Scribe_Values.Look(ref NodeId, "nodeId", string.Empty);
            Scribe_Values.Look(ref DisplayName, "displayName", string.Empty);
        }

        public PromptUnifiedNodeRegistration Clone()
        {
            return new PromptUnifiedNodeRegistration
            {
                NodeId = NodeId,
                DisplayName = DisplayName
            };
        }
    }
}
