using System;
using System.Linq;
using RimChat.UI;
using Verse;

namespace RimChat.Dialogue
{
    /// <summary>
    /// Centralized fail-fast window open coordinator for diplomacy and RPG dialogues.
    /// </summary>
    public static class DialogueWindowCoordinator
    {
        public static bool TryOpen(DialogueOpenIntent intent, out string reason)
        {
            reason = string.Empty;
            if (intent?.RuntimeContext == null)
            {
                reason = "intent_context_null";
                return false;
            }

            DialogueRuntimeContext snapshot = intent.RuntimeContext.WithCurrentRuntimeMarkers();
            if (!DialogueContextResolver.TryResolveLiveContext(snapshot, out DialogueLiveContext liveContext, out reason))
            {
                return false;
            }

            if (!DialogueContextValidator.ValidateWindowOpen(snapshot, liveContext, out reason))
            {
                return false;
            }

            if (IsDuplicateWindow(snapshot))
            {
                reason = "duplicate_window";
                return false;
            }

            if (snapshot.Channel == DialogueChannel.Diplomacy)
            {
                var window = new Dialog_DiplomacyDialogue(
                    liveContext.Faction,
                    liveContext.Negotiator,
                    intent.MuteOpenSound,
                    snapshot,
                    snapshot.WindowKey);
                Find.WindowStack.Add(window);
                return true;
            }

            var rpgWindow = new Dialog_RPGPawnDialogue(
                liveContext.Initiator,
                liveContext.Target,
                intent.ProactiveOpening,
                snapshot,
                snapshot.WindowKey);
            Find.WindowStack.Add(rpgWindow);
            return true;
        }

        private static bool IsDuplicateWindow(DialogueRuntimeContext runtimeContext)
        {
            if (Find.WindowStack?.Windows == null || runtimeContext == null)
            {
                return false;
            }

            return Find.WindowStack.Windows.Any(window =>
            {
                if (window is Dialog_DiplomacyDialogue diplomacyWindow)
                {
                    return diplomacyWindow.MatchesWindowLifecycleKey(runtimeContext.WindowKey);
                }

                if (window is Dialog_RPGPawnDialogue rpgWindow)
                {
                    return rpgWindow.MatchesWindowLifecycleKey(runtimeContext.WindowKey);
                }

                return false;
            });
        }
    }
}
