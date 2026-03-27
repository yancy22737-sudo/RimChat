using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    public class RimChatQuestPublicationRecord : IExposable
    {
        public int QuestId;
        public string QuestDefName;
        public string FactionUniqueId;
        public string FactionDefName;
        public string FactionName;
        public int PublishedTick;

        public RimChatQuestPublicationRecord()
        {
            QuestDefName = string.Empty;
            FactionUniqueId = string.Empty;
            FactionDefName = string.Empty;
            FactionName = string.Empty;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref QuestId, "questId", -1);
            Scribe_Values.Look(ref QuestDefName, "questDefName", string.Empty);
            Scribe_Values.Look(ref FactionUniqueId, "factionUniqueId", string.Empty);
            Scribe_Values.Look(ref FactionDefName, "factionDefName", string.Empty);
            Scribe_Values.Look(ref FactionName, "factionName", string.Empty);
            Scribe_Values.Look(ref PublishedTick, "publishedTick", 0);
        }
    }

    public partial class GameAIInterface
    {
        private List<RimChatQuestPublicationRecord> _rimChatQuestPublicationRecords = new List<RimChatQuestPublicationRecord>();

        internal void ExposeQuestPublicationData()
        {
            Scribe_Collections.Look(ref _rimChatQuestPublicationRecords, "rimChatQuestPublicationRecords", LookMode.Deep);
            if (Scribe.mode != LoadSaveMode.PostLoadInit)
            {
                return;
            }

            _rimChatQuestPublicationRecords ??= new List<RimChatQuestPublicationRecord>();
            CleanupQuestPublicationRecords();
        }

        internal static HashSet<int> CaptureCurrentQuestIdsForTracking()
        {
            List<Quest> quests = Find.QuestManager?.QuestsListForReading;
            if (quests == null || quests.Count == 0)
            {
                return new HashSet<int>();
            }

            return new HashSet<int>(quests.Where(quest => quest != null).Select(quest => quest.id));
        }

        internal void TryTrackCreateQuestResult(
            string requestedQuestDefName,
            Dictionary<string, object> parameters,
            APIResult result,
            HashSet<int> questIdsBefore)
        {
            if (result == null || !result.Success)
            {
                return;
            }

            Faction faction = ResolveQuestPublicationFaction(parameters);
            if (faction == null)
            {
                return;
            }

            Quest createdQuest = ResolveNewQuestFromSnapshot(questIdsBefore);
            if (createdQuest == null)
            {
                return;
            }

            string normalizedDefName = ResolveQuestDefNameFromResult(result, requestedQuestDefName);
            var record = new RimChatQuestPublicationRecord
            {
                QuestId = createdQuest.id,
                QuestDefName = normalizedDefName,
                FactionUniqueId = GetFactionUniqueId(faction),
                FactionDefName = faction.def?.defName ?? string.Empty,
                FactionName = faction.Name ?? string.Empty,
                PublishedTick = Find.TickManager?.TicksGame ?? 0
            };

            UpsertQuestPublicationRecord(record);
        }

        public bool HasActiveRimChatQuestForFaction(Faction faction)
        {
            if (faction == null)
            {
                return false;
            }

            CleanupQuestPublicationRecords();
            string factionId = GetFactionUniqueId(faction);
            string factionDefName = faction.def?.defName ?? string.Empty;
            List<Quest> quests = Find.QuestManager?.QuestsListForReading;
            if (quests == null || quests.Count == 0)
            {
                return false;
            }

            foreach (RimChatQuestPublicationRecord record in _rimChatQuestPublicationRecords)
            {
                if (record == null || !IsRecordFactionMatch(record, factionId, factionDefName))
                {
                    continue;
                }

                Quest quest = quests.FirstOrDefault(item => item != null && item.id == record.QuestId);
                if (quest != null && quest.State == QuestState.Ongoing)
                {
                    return true;
                }
            }

            return false;
        }

        private static Faction ResolveQuestPublicationFaction(Dictionary<string, object> parameters)
        {
            if (parameters == null)
            {
                return null;
            }

            if (parameters.TryGetValue("askerFaction", out object askerFaction))
            {
                Faction resolved = ResolveFactionParameterValue(askerFaction);
                if (resolved != null)
                {
                    return resolved;
                }
            }

            if (!parameters.TryGetValue("faction", out object factionValue))
            {
                return null;
            }

            return ResolveFactionParameterValue(factionValue);
        }

        private static Faction ResolveFactionParameterValue(object value)
        {
            if (value is Faction faction)
            {
                return faction;
            }

            if (!(value is string raw))
            {
                return null;
            }

            return Find.FactionManager?.AllFactions?
                .FirstOrDefault(item =>
                    item != null &&
                    (string.Equals(item.Name, raw, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(item.def?.defName, raw, StringComparison.OrdinalIgnoreCase)));
        }

        private static Quest ResolveNewQuestFromSnapshot(HashSet<int> questIdsBefore)
        {
            List<Quest> quests = Find.QuestManager?.QuestsListForReading;
            if (quests == null || quests.Count == 0)
            {
                return null;
            }

            IEnumerable<Quest> candidates = quests.Where(quest => quest != null);
            if (questIdsBefore != null && questIdsBefore.Count > 0)
            {
                candidates = candidates.Where(quest => !questIdsBefore.Contains(quest.id));
            }

            return candidates
                .OrderByDescending(quest => quest.id)
                .FirstOrDefault();
        }

        private static string ResolveQuestDefNameFromResult(APIResult result, string fallback)
        {
            if (result?.Data == null)
            {
                return fallback ?? string.Empty;
            }

            object resolved = ReadMember(result.Data, "QuestDefName");
            string text = resolved?.ToString();
            return string.IsNullOrWhiteSpace(text) ? (fallback ?? string.Empty) : text.Trim();
        }

        private static object ReadMember(object target, string name)
        {
            if (target == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            Type type = target.GetType();
            var property = type.GetProperty(name);
            if (property != null)
            {
                return property.GetValue(target, null);
            }

            var field = type.GetField(name);
            return field?.GetValue(target);
        }

        private void UpsertQuestPublicationRecord(RimChatQuestPublicationRecord record)
        {
            _rimChatQuestPublicationRecords ??= new List<RimChatQuestPublicationRecord>();
            int existingIndex = _rimChatQuestPublicationRecords.FindIndex(item => item != null && item.QuestId == record.QuestId);
            if (existingIndex >= 0)
            {
                _rimChatQuestPublicationRecords[existingIndex] = record;
            }
            else
            {
                _rimChatQuestPublicationRecords.Add(record);
            }
        }

        private void CleanupQuestPublicationRecords()
        {
            _rimChatQuestPublicationRecords ??= new List<RimChatQuestPublicationRecord>();
            List<Quest> quests = Find.QuestManager?.QuestsListForReading ?? new List<Quest>();
            _rimChatQuestPublicationRecords = _rimChatQuestPublicationRecords
                .Where(record => record != null && record.QuestId >= 0)
                .Where(record => quests.Any(quest => quest != null && quest.id == record.QuestId))
                .ToList();
        }

        private static bool IsRecordFactionMatch(RimChatQuestPublicationRecord record, string factionId, string factionDefName)
        {
            if (!string.IsNullOrWhiteSpace(record.FactionUniqueId) && !string.IsNullOrWhiteSpace(factionId))
            {
                if (string.Equals(record.FactionUniqueId, factionId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return !string.IsNullOrWhiteSpace(record.FactionDefName) &&
                   !string.IsNullOrWhiteSpace(factionDefName) &&
                   string.Equals(record.FactionDefName, factionDefName, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetFactionUniqueId(Faction faction)
        {
            return faction?.GetUniqueLoadID() ?? string.Empty;
        }
    }
}
