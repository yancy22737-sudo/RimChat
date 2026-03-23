using System;

namespace RimChat.Dialogue
{
    /// <summary>
    /// Dependencies: dialogue request lifecycle reason tags emitted by controllers and request leases.
    /// Responsibility: classify which dropped/cancelled reasons are internal lifecycle noise and must stay invisible to players.
    /// </summary>
    public static class DialogueDropPolicy
    {
        public static bool ShouldSuppressUserFacingDrop(string reason)
        {
            switch ((reason ?? string.Empty).Trim())
            {
                case "pending_request_mismatch":
                case "request_lease_mismatch":
                case "request_lease_invalid":
                case "lease_invalid":
                case "lease_runtime_null":
                case "lease_request_id_empty":
                case "request_context_null":
                case "dialogue_session_mismatch":
                case "session_reference_changed":
                case "context_version_changed":
                case "game_context_changed":
                case "context_changed":
                case "save_context_changed":
                case "request_superseded":
                case "dialogue_window_closed":
                case "strategy_request_cancelled":
                    return true;
                default:
                    return false;
            }
        }
    }
}
