using System;
using RimChat.AI;
using RimWorld;
using Verse;

namespace RimChat.Dialogue
{
    public enum DialogueChannel
    {
        Diplomacy = 1,
        Rpg = 2
    }

    /// <summary>
    /// Stable runtime context for dialogue lifecycle checks.
    /// Stores stable ids only and resolves live objects on demand.
    /// </summary>
    public sealed class DialogueRuntimeContext
    {
        public DialogueChannel Channel { get; }
        public string DialogueSessionId { get; }
        public string WindowKey { get; }
        public string InitiatorPawnId { get; }
        public string TargetPawnId { get; }
        public string NegotiatorPawnId { get; }
        public int FactionLoadId { get; }
        public int MapUniqueId { get; }
        public int GameContextId { get; }
        public int ContextVersion { get; }

        private DialogueRuntimeContext(
            DialogueChannel channel,
            string dialogueSessionId,
            string windowKey,
            string initiatorPawnId,
            string targetPawnId,
            string negotiatorPawnId,
            int factionLoadId,
            int mapUniqueId,
            int gameContextId,
            int contextVersion)
        {
            Channel = channel;
            DialogueSessionId = dialogueSessionId ?? string.Empty;
            WindowKey = windowKey ?? string.Empty;
            InitiatorPawnId = initiatorPawnId ?? string.Empty;
            TargetPawnId = targetPawnId ?? string.Empty;
            NegotiatorPawnId = negotiatorPawnId ?? string.Empty;
            FactionLoadId = factionLoadId;
            MapUniqueId = mapUniqueId;
            GameContextId = gameContextId;
            ContextVersion = contextVersion;
        }

        public static DialogueRuntimeContext CreateDiplomacy(
            Faction faction,
            Pawn negotiator,
            Map map,
            string dialogueSessionId = null,
            string windowKey = null)
        {
            int factionLoadId = faction?.loadID ?? -1;
            int mapId = map?.uniqueID ?? negotiator?.Map?.uniqueID ?? -1;
            string negotiatorId = negotiator?.GetUniqueLoadID() ?? string.Empty;
            string sessionId = string.IsNullOrWhiteSpace(dialogueSessionId)
                ? $"diplomacy:{factionLoadId}:{mapId}:{negotiatorId}"
                : dialogueSessionId.Trim();
            string key = string.IsNullOrWhiteSpace(windowKey)
                ? $"window:{sessionId}"
                : windowKey.Trim();

            return new DialogueRuntimeContext(
                DialogueChannel.Diplomacy,
                sessionId,
                key,
                string.Empty,
                string.Empty,
                negotiatorId,
                factionLoadId,
                mapId,
                GetCurrentGameContextId(),
                GetCurrentContextVersion());
        }

        public static DialogueRuntimeContext CreateRpg(
            Pawn initiator,
            Pawn target,
            Map map,
            string dialogueSessionId = null,
            string windowKey = null)
        {
            string initiatorId = initiator?.GetUniqueLoadID() ?? string.Empty;
            string targetId = target?.GetUniqueLoadID() ?? string.Empty;
            int mapId = map?.uniqueID ?? initiator?.Map?.uniqueID ?? target?.Map?.uniqueID ?? -1;
            string sessionId = string.IsNullOrWhiteSpace(dialogueSessionId)
                ? $"rpg:{initiatorId}:{targetId}:{mapId}"
                : dialogueSessionId.Trim();
            string key = string.IsNullOrWhiteSpace(windowKey)
                ? $"window:{sessionId}"
                : windowKey.Trim();

            return new DialogueRuntimeContext(
                DialogueChannel.Rpg,
                sessionId,
                key,
                initiatorId,
                targetId,
                string.Empty,
                -1,
                mapId,
                GetCurrentGameContextId(),
                GetCurrentContextVersion());
        }

        public DialogueRuntimeContext WithCurrentRuntimeMarkers()
        {
            return new DialogueRuntimeContext(
                Channel,
                DialogueSessionId,
                WindowKey,
                InitiatorPawnId,
                TargetPawnId,
                NegotiatorPawnId,
                FactionLoadId,
                MapUniqueId,
                GetCurrentGameContextId(),
                GetCurrentContextVersion());
        }

        private static int GetCurrentContextVersion()
        {
            return AIChatServiceAsync.Instance.GetCurrentContextVersionSnapshot();
        }

        public static int GetCurrentGameContextId()
        {
            return Current.Game == null ? 0 : Current.Game.GetHashCode();
        }

        public static Faction GetDiplomacyFactionFromSessionId(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || Find.FactionManager == null)
            {
                return null;
            }

            if (sessionId.StartsWith("diplomacy:"))
            {
                string[] parts = sessionId.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[1], out int factionId) && factionId > 0)
                {
                    return Find.FactionManager.AllFactionsListForReading?
                        .FirstOrDefault(f => f != null && f.loadID == factionId);
                }
            }

            return null;
        }
    }
}
