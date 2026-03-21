using RimWorld;
using Verse;

namespace RimChat.DiplomacySystem
{
    public static class DiplomacyNotificationManager
    {
        public static void SendNotification(Faction faction, string titleKey, string messageKey, LetterDef letterDef = null, params object[] args)
        {
            if (faction == null) return;

            string title = titleKey.Translate();
            string messageTemplate = messageKey.Translate().ToString();
            string message = args != null && args.Length > 0
                ? string.Format(messageTemplate, args)
                : messageTemplate;

            if (letterDef == null)
            {
                letterDef = LetterDefOf.NeutralEvent;
            }

            Find.LetterStack.ReceiveLetter(
                title,
                message,
                letterDef
            );

            Log.Message($"[RimChat] Sent notification: {title} - {faction.Name}");
        }

        public static void SendAIActionNotification(Faction faction, AIActionType actionType, string detail = "")
        {
            switch (actionType)
            {
                case AIActionType.AdjustGoodwill:
                    SendNotification(faction, 
                        "RimChat_AIAdjustGoodwillTitle", 
                        "RimChat_AIAdjustGoodwillDesc", 
                        LetterDefOf.NeutralEvent, 
                        faction.Name, detail);
                    break;
                case AIActionType.SendGift:
                    SendNotification(faction, 
                        "RimChat_AISendGiftTitle", 
                        "RimChat_AISendGiftDesc", 
                        LetterDefOf.PositiveEvent, 
                        faction.Name, detail);
                    break;
                case AIActionType.RequestAid:
                    SendNotification(faction, 
                        "RimChat_AIRequestAidTitle", 
                        "RimChat_AIRequestAidDesc", 
                        LetterDefOf.PositiveEvent, 
                        faction.Name, detail);
                    break;
                case AIActionType.DeclareWar:
                    SendNotification(faction, 
                        "RimChat_AIDeclareWarTitle", 
                        "RimChat_AIDeclareWarDesc", 
                        LetterDefOf.ThreatBig, 
                        faction.Name, detail);
                    break;
                case AIActionType.MakePeace:
                    SendNotification(faction, 
                        "RimChat_AIMakePeaceTitle", 
                        "RimChat_AIMakePeaceDesc", 
                        LetterDefOf.PositiveEvent, 
                        faction.Name, detail);
                    break;
                case AIActionType.RequestCaravan:
                    SendNotification(faction, 
                        "RimChat_AIRequestCaravanTitle", 
                        "RimChat_AIRequestCaravanDesc", 
                        LetterDefOf.PositiveEvent, 
                        faction.Name, detail);
                    break;
                case AIActionType.RejectRequest:
                    SendNotification(faction, 
                        "RimChat_AIRejectRequestTitle", 
                        "RimChat_AIRejectRequestDesc", 
                        LetterDefOf.NeutralEvent, 
                        faction.Name, detail);
                    break;
                case AIActionType.RequestRaid:
                    SendNotification(faction, 
                        "RimChat_AIRequestRaidTitle", 
                        "RimChat_AIRequestRaidDesc", 
                        LetterDefOf.ThreatBig, 
                        faction.Name, detail);
                    break;
                case AIActionType.CreateQuest:
                    SendNotification(faction, 
                        "RimChat_AICreateQuestTitle", 
                        "RimChat_AICreateQuestDesc", 
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
                        "RimChat_DelayedCaravanScheduledTitle", 
                        "RimChat_DelayedCaravanScheduledDesc", 
                        LetterDefOf.PositiveEvent, 
                        faction.Name, detail, delayDays.ToString("F1"));
                    break;
                case DelayedEventType.Aid:
                    SendNotification(faction, 
                        "RimChat_DelayedAidScheduledTitle", 
                        "RimChat_DelayedAidScheduledDesc", 
                        LetterDefOf.PositiveEvent, 
                        faction.Name, detail, delayDays.ToString("F1"));
                    break;
                case DelayedEventType.Raid:
                    SendNotification(faction, 
                        "RimChat_DelayedRaidScheduledTitle", 
                        "RimChat_DelayedRaidScheduledDesc", 
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
