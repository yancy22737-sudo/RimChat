using RimWorld;
using Verse;

namespace RimDiplomacy.DiplomacySystem
{
    public static class DiplomacyNotificationManager
    {
        public static void SendNotification(Faction faction, string titleKey, string messageKey, LetterDef letterDef = null, params object[] args)
        {
            if (faction == null) return;

            string title = titleKey.Translate();
            string message = messageKey.Translate(args);

            if (letterDef == null)
            {
                letterDef = LetterDefOf.NeutralEvent;
            }

            Find.LetterStack.ReceiveLetter(
                title,
                message,
                letterDef
            );

            Log.Message($"[RimDiplomacy] Sent notification: {title} - {faction.Name}");
        }

        public static void SendAIActionNotification(Faction faction, AIActionType actionType, string detail = "")
        {
            switch (actionType)
            {
                case AIActionType.AdjustGoodwill:
                    SendNotification(faction, 
                        "RimDiplomacy_AIAdjustGoodwillTitle", 
                        "RimDiplomacy_AIAdjustGoodwillDesc", 
                        LetterDefOf.NeutralEvent, 
                        faction.Name, detail);
                    break;
                case AIActionType.SendGift:
                    SendNotification(faction, 
                        "RimDiplomacy_AISendGiftTitle", 
                        "RimDiplomacy_AISendGiftDesc", 
                        LetterDefOf.PositiveEvent, 
                        faction.Name, detail);
                    break;
                case AIActionType.RequestAid:
                    SendNotification(faction, 
                        "RimDiplomacy_AIRequestAidTitle", 
                        "RimDiplomacy_AIRequestAidDesc", 
                        LetterDefOf.PositiveEvent, 
                        faction.Name, detail);
                    break;
                case AIActionType.DeclareWar:
                    SendNotification(faction, 
                        "RimDiplomacy_AIDeclareWarTitle", 
                        "RimDiplomacy_AIDeclareWarDesc", 
                        LetterDefOf.ThreatBig, 
                        faction.Name, detail);
                    break;
                case AIActionType.MakePeace:
                    SendNotification(faction, 
                        "RimDiplomacy_AIMakePeaceTitle", 
                        "RimDiplomacy_AIMakePeaceDesc", 
                        LetterDefOf.PositiveEvent, 
                        faction.Name, detail);
                    break;
                case AIActionType.RequestCaravan:
                    SendNotification(faction, 
                        "RimDiplomacy_AIRequestCaravanTitle", 
                        "RimDiplomacy_AIRequestCaravanDesc", 
                        LetterDefOf.PositiveEvent, 
                        faction.Name, detail);
                    break;
                case AIActionType.RejectRequest:
                    SendNotification(faction, 
                        "RimDiplomacy_AIRejectRequestTitle", 
                        "RimDiplomacy_AIRejectRequestDesc", 
                        LetterDefOf.NeutralEvent, 
                        faction.Name, detail);
                    break;
                case AIActionType.RequestRaid:
                    SendNotification(faction, 
                        "RimDiplomacy_AIRequestRaidTitle", 
                        "RimDiplomacy_AIRequestRaidDesc", 
                        LetterDefOf.ThreatBig, 
                        faction.Name, detail);
                    break;
                case AIActionType.CreateQuest:
                    SendNotification(faction, 
                        "RimDiplomacy_AICreateQuestTitle", 
                        "RimDiplomacy_AICreateQuestDesc", 
                        LetterDefOf.PositiveEvent, 
                        faction.Name, detail);
                    break;
            }
        }

        public static void SendDelayedEventScheduledNotification(Faction faction, DelayedEventType eventType, string detail, float delayDays)
        {
            switch (eventType)
            {
                case DelayedEventType.Caravan:
                    SendNotification(faction, 
                        "RimDiplomacy_DelayedCaravanScheduledTitle", 
                        "RimDiplomacy_DelayedCaravanScheduledDesc", 
                        LetterDefOf.PositiveEvent, 
                        faction.Name, detail, delayDays.ToString("F1"));
                    break;
                case DelayedEventType.Aid:
                    SendNotification(faction, 
                        "RimDiplomacy_DelayedAidScheduledTitle", 
                        "RimDiplomacy_DelayedAidScheduledDesc", 
                        LetterDefOf.PositiveEvent, 
                        faction.Name, detail, delayDays.ToString("F1"));
                    break;
                case DelayedEventType.Raid:
                    SendNotification(faction, 
                        "RimDiplomacy_DelayedRaidScheduledTitle", 
                        "RimDiplomacy_DelayedRaidScheduledDesc", 
                        LetterDefOf.ThreatBig, 
                        faction.Name, detail, delayDays.ToString("F1"));
                    break;
            }
        }
    }

    public enum AIActionType
    {
        AdjustGoodwill,
        SendGift,
        RequestAid,
        DeclareWar,
        MakePeace,
        RequestCaravan,
        RejectRequest,
        RequestRaid,
        CreateQuest
    }
}
