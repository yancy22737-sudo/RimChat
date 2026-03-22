using System.Collections.Generic;
using RimChat.AI;

namespace RimChat.Dialogue
{
    /// <summary>
    /// Stage-A parsed response envelope. UI/action mutation happens in stage B only.
    /// </summary>
    public sealed class DialogueResponseEnvelope
    {
        public string RawResponse { get; set; }
        public string DialogueText { get; set; }
        public List<LLMRpgApiResponse.ApiAction> Actions { get; set; } = new List<LLMRpgApiResponse.ApiAction>();
        public bool IsStaleDropped { get; set; }
        public string DropReason { get; set; }
    }
}
