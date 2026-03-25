using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Memory;
using RimChat.Persistence;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// Dependencies: diplomacy session message model, prompt negotiator resolver, and RimWorld pawn generation/portrait APIs.
 /// Responsibility: keep diplomacy message speakers consistent and render per-message bubble avatars.
 ///</summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private const float MessageAvatarSize = 36f;
        private const float MessageAvatarGap = 3f;
        private const float MessageSidePadding = 0f;
        private const float MessageAvatarTopInset = 0f;
        private const float MessageMaxWidthPercent = 0.92f;
        private const float AvatarPortraitRequestSize = 192f;
        private static readonly Vector3 AvatarCameraOffset = new Vector3(0f, 0f, 0.35f);
        private const float AvatarCameraZoom = 1.95f;
        private static readonly HashSet<string> BubbleLayoutWarnings = new HashSet<string>(StringComparer.Ordinal);
        private Pawn sessionFallbackFactionSpeaker;

        private void EnsureSessionMessageSpeakers(FactionDialogueSession currentSession)
        {
            if (currentSession?.messages == null)
            {
                return;
            }

            for (int i = 0; i < currentSession.messages.Count; i++)
            {
                EnsureMessageSpeaker(currentSession, currentSession.messages[i]);
            }
        }

        private void EnsureMessageSpeaker(FactionDialogueSession currentSession, DialogueMessageData message)
        {
            if (message == null || message.IsSystemMessage())
            {
                return;
            }

            if (IsOutboundPrisonerInfoMessage(message))
            {
                EnsureOutboundPrisonInfoSpeaker(message);
                return;
            }

            if (message.isPlayer)
            {
                EnsurePlayerMessageSpeaker(message);
                return;
            }

            EnsureFactionMessageSpeaker(currentSession, message);
        }

        private void EnsurePlayerMessageSpeaker(DialogueMessageData message)
        {
            Pawn playerPawn = message.ResolveSpeakerPawn() ?? ResolvePlayerSpeakerPawn();
            if (IsEligibleSpeakerPawn(playerPawn))
            {
                message.SetSpeakerPawn(playerPawn);
            }

            message.sender = ResolvePlayerSenderName(playerPawn);
        }

        private void EnsureOutboundPrisonInfoSpeaker(DialogueMessageData message)
        {
            Pawn playerPawn = ResolvePlayerSpeakerPawn();
            if (IsEligibleSpeakerPawn(playerPawn))
            {
                message.SetSpeakerPawn(playerPawn);
            }
        }

        private void EnsureFactionMessageSpeaker(FactionDialogueSession currentSession, DialogueMessageData message)
        {
            Faction currentFaction = currentSession?.faction ?? faction;
            Pawn speakerPawn = message.ResolveSpeakerPawn();
            if (!IsEligibleSpeakerPawn(speakerPawn, currentFaction))
            {
                speakerPawn = ResolveFactionSpeakerPawn(currentSession, currentFaction);
            }

            if (IsEligibleSpeakerPawn(speakerPawn, currentFaction))
            {
                message.SetSpeakerPawn(speakerPawn);
            }

            if (!string.IsNullOrWhiteSpace(message.sender))
            {
                return;
            }

            message.sender = ResolveFactionSenderName(currentFaction, speakerPawn);
        }

        private Pawn ResolvePlayerSpeakerPawn()
        {
            if (IsEligibleSpeakerPawn(negotiator))
            {
                return negotiator;
            }

            Pawn fallback = PromptPersistenceService.Instance.ResolveBestPlayerNegotiator(negotiator);
            return IsEligibleSpeakerPawn(fallback) ? fallback : null;
        }

        private Pawn ResolveFactionSpeakerPawn(FactionDialogueSession currentSession, Faction currentFaction)
        {
            if (IsEligibleSpeakerPawn(sessionFallbackFactionSpeaker, currentFaction))
            {
                return sessionFallbackFactionSpeaker;
            }

            if (TryGetSessionFactionSpeaker(currentSession, currentFaction, out Pawn persistedSpeaker))
            {
                sessionFallbackFactionSpeaker = persistedSpeaker;
                return sessionFallbackFactionSpeaker;
            }

            if (IsEligibleSpeakerPawn(currentFaction?.leader, currentFaction))
            {
                sessionFallbackFactionSpeaker = currentFaction.leader;
                return sessionFallbackFactionSpeaker;
            }

            if (TryGetExistingFactionSpeakerPawn(currentFaction, out Pawn existingSpeaker))
            {
                sessionFallbackFactionSpeaker = existingSpeaker;
                return sessionFallbackFactionSpeaker;
            }

            if (TryGenerateFactionSpeakerPawn(currentFaction, out Pawn generatedSpeaker))
            {
                sessionFallbackFactionSpeaker = generatedSpeaker;
                return sessionFallbackFactionSpeaker;
            }

            return null;
        }

        private static bool TryGetSessionFactionSpeaker(
            FactionDialogueSession currentSession,
            Faction currentFaction,
            out Pawn speakerPawn)
        {
            speakerPawn = null;
            if (currentSession?.messages == null)
            {
                return false;
            }

            for (int i = 0; i < currentSession.messages.Count; i++)
            {
                DialogueMessageData message = currentSession.messages[i];
                if (message == null || message.isPlayer || message.IsSystemMessage())
                {
                    continue;
                }

                Pawn candidate = message.ResolveSpeakerPawn();
                if (IsEligibleSpeakerPawn(candidate, currentFaction))
                {
                    speakerPawn = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetExistingFactionSpeakerPawn(Faction currentFaction, out Pawn speakerPawn)
        {
            speakerPawn = null;
            if (currentFaction == null)
            {
                return false;
            }

            List<Pawn> candidates = PawnsFinder.AllMapsWorldAndTemporary_Alive
                .Where(pawn => IsEligibleSpeakerPawn(pawn, currentFaction))
                .ToList();
            if (candidates.Count == 0)
            {
                return false;
            }

            speakerPawn = candidates.RandomElement();
            return true;
        }

        private static bool TryGenerateFactionSpeakerPawn(Faction currentFaction, out Pawn speakerPawn)
        {
            speakerPawn = null;
            if (currentFaction == null || currentFaction.defeated)
            {
                return false;
            }

            PawnKindDef kindDef = currentFaction.def?.basicMemberKind ?? ResolveFallbackHumanlikeKind();
            if (kindDef == null)
            {
                return false;
            }

            try
            {
                speakerPawn = GenerateFactionSpeakerPawn(currentFaction, kindDef);
                if (speakerPawn == null)
                {
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimChat] Failed to generate fallback diplomacy speaker for faction '{currentFaction.Name}': {ex.Message}");
                return false;
            }
        }

        private static Pawn GenerateFactionSpeakerPawn(Faction currentFaction, PawnKindDef kindDef)
        {
            var request = new PawnGenerationRequest(kindDef, currentFaction, PawnGenerationContext.NonPlayer, -1, true);
            Pawn generated = PawnGenerator.GeneratePawn(request);
            if (generated == null)
            {
                return null;
            }

            if (generated.Faction != currentFaction)
            {
                generated.SetFaction(currentFaction);
            }

            Find.WorldPawns?.PassToWorld(generated);
            return generated;
        }

        private static PawnKindDef ResolveFallbackHumanlikeKind()
        {
            return DefDatabase<PawnKindDef>.AllDefsListForReading
                .FirstOrDefault(def => def?.RaceProps?.Humanlike == true);
        }

        private static bool IsEligibleSpeakerPawn(Pawn pawn, Faction expectedFaction = null)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.RaceProps?.Humanlike != true)
            {
                return false;
            }

            return expectedFaction == null || pawn.Faction == expectedFaction;
        }

        private string ResolvePlayerSenderName(Pawn playerPawn)
        {
            string name = ResolvePawnLabel(playerPawn);
            return string.IsNullOrWhiteSpace(name) ? "RimChat_You".Translate().ToString() : name;
        }

        private static string ResolveFactionSenderName(Faction currentFaction, Pawn factionSpeaker)
        {
            string speakerName = ResolvePawnLabel(factionSpeaker);
            if (!string.IsNullOrWhiteSpace(speakerName))
            {
                return speakerName;
            }

            return currentFaction?.Name ?? "Unknown";
        }

        private string GetDisplaySenderName(DialogueMessageData message)
        {
            if (message == null)
            {
                return string.Empty;
            }

            if (IsOutboundPrisonerInfoMessage(message))
            {
                return ResolvePlayerSenderName(ResolveVisualSpeakerPawn(message));
            }

            if (!string.IsNullOrWhiteSpace(message.sender))
            {
                return message.sender;
            }

            Pawn speakerPawn = ResolveMessageSpeakerPawn(message);
            return IsPlayerVisualMessage(message)
                ? ResolvePlayerSenderName(speakerPawn)
                : ResolveFactionSenderName(faction, speakerPawn);
        }

        private Pawn ResolveMessageSpeakerPawn(DialogueMessageData message)
        {
            if (message == null || message.IsSystemMessage())
            {
                return null;
            }

            if (IsOutboundPrisonerInfoMessage(message))
            {
                Pawn playerSpeaker = ResolvePlayerSpeakerPawn();
                if (IsEligibleSpeakerPawn(playerSpeaker))
                {
                    message.SetSpeakerPawn(playerSpeaker);
                }
                return playerSpeaker;
            }

            Pawn speaker = message.ResolveSpeakerPawn();
            if (message.isPlayer)
            {
                Pawn playerSpeaker = IsEligibleSpeakerPawn(speaker) ? speaker : ResolvePlayerSpeakerPawn();
                if (IsEligibleSpeakerPawn(playerSpeaker))
                {
                    message.SetSpeakerPawn(playerSpeaker);
                }
                return playerSpeaker;
            }

            Pawn factionSpeaker = IsEligibleSpeakerPawn(speaker, faction)
                ? speaker
                : ResolveFactionSpeakerPawn(session, faction);
            if (IsEligibleSpeakerPawn(factionSpeaker, faction))
            {
                message.SetSpeakerPawn(factionSpeaker);
            }
            return factionSpeaker;
        }

        private Pawn ResolveVisualSpeakerPawn(DialogueMessageData message)
        {
            if (message == null)
            {
                return null;
            }

            if (IsOutboundPrisonerInfoMessage(message))
            {
                Pawn playerSpeaker = ResolvePlayerSpeakerPawn();
                if (IsEligibleSpeakerPawn(playerSpeaker))
                {
                    message.SetSpeakerPawn(playerSpeaker);
                    return playerSpeaker;
                }
            }

            return ResolveMessageSpeakerPawn(message);
        }

        private static bool IsOutboundPrisonerInfoMessage(DialogueMessageData message)
        {
            return message != null &&
                   message.HasInlineImage() &&
                   string.Equals(message.imageSourceUrl, RansomProofImageSourceUrl, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsPlayerVisualMessage(DialogueMessageData message)
        {
            return message?.isPlayer == true || IsOutboundPrisonerInfoMessage(message);
        }

        private static string ResolvePawnLabel(Pawn pawn)
        {
            if (pawn?.Name != null)
            {
                string shortName = pawn.Name.ToStringShort;
                if (!string.IsNullOrWhiteSpace(shortName))
                {
                    return shortName;
                }
            }

            return pawn?.LabelShort;
        }

        private float GetBubbleXForMessage(DialogueMessageData message, float viewportWidth, float bubbleWidth)
        {
            float leftEdge = MessageSidePadding + MessageAvatarSize + MessageAvatarGap;
            float rightEdge = viewportWidth - MessageSidePadding - MessageAvatarSize - MessageAvatarGap;
            float maxX = Mathf.Max(leftEdge, rightEdge - bubbleWidth);
            return IsPlayerVisualMessage(message) ? maxX : leftEdge;
        }

        private void TryLogBubbleLayoutOutOfTrackOnce(DialogueMessageData message, Rect bubbleRect, float viewportWidth)
        {
            if (message == null || message.IsSystemMessage())
            {
                return;
            }

            float leftEdge = MessageSidePadding + MessageAvatarSize + MessageAvatarGap;
            float rightEdge = viewportWidth - MessageSidePadding - MessageAvatarSize - MessageAvatarGap;
            bool outOfTrack = bubbleRect.x < leftEdge - 1f || bubbleRect.xMax > rightEdge + 1f;
            if (!outOfTrack)
            {
                return;
            }

            string key = $"{message.GetGameTick()}:{message.isPlayer}:{Mathf.RoundToInt(bubbleRect.width)}:{Mathf.RoundToInt(viewportWidth)}";
            if (!BubbleLayoutWarnings.Add(key))
            {
                return;
            }

            Log.Warning($"[RimChat][UI_ASSERT] bubble out of track: tick={message.GetGameTick()}, player={message.isPlayer}, x={bubbleRect.x:F1}, xMax={bubbleRect.xMax:F1}, left={leftEdge:F1}, right={rightEdge:F1}, viewport={viewportWidth:F1}");
        }

        private float GetMaxBubbleWidth(float viewportWidth)
        {
            return Mathf.Clamp(GetMessageBubbleTrackWidth(viewportWidth) * MessageMaxWidthPercent, 140f, viewportWidth);
        }

        private float GetMaxSystemMessageWidth(float viewportWidth)
        {
            return Mathf.Max(140f, viewportWidth - 60f);
        }

        private static float GetMessageBubbleTrackWidth(float viewportWidth)
        {
            float left = MessageSidePadding + MessageAvatarSize + MessageAvatarGap;
            float right = viewportWidth - MessageSidePadding - MessageAvatarSize - MessageAvatarGap;
            return Mathf.Max(140f, right - left);
        }

        private void DrawMessageAvatar(DialogueMessageData message, Rect bubbleRect)
        {
            if (message == null || message.IsSystemMessage())
            {
                return;
            }

            Rect avatarRect = BuildAvatarRect(message, bubbleRect);
            Pawn speakerPawn = ResolveVisualSpeakerPawn(message);
            Texture portrait = ResolveSpeakerPortrait(speakerPawn);

            if (portrait != null)
            {
                GUI.DrawTexture(avatarRect, portrait, ScaleMode.ScaleAndCrop, true);
            }
            else
            {
                DrawAvatarFallback(avatarRect, GetDisplaySenderName(message));
            }
        }

        private Rect BuildAvatarRect(DialogueMessageData message, Rect bubbleRect)
        {
            float x = IsPlayerVisualMessage(message)
                ? bubbleRect.xMax + MessageAvatarGap
                : bubbleRect.x - MessageAvatarGap - MessageAvatarSize;
            float y = bubbleRect.y + MessageAvatarTopInset;
            return new Rect(x, y, MessageAvatarSize, MessageAvatarSize);
        }

        private static Texture ResolveSpeakerPortrait(Pawn pawn)
        {
            if (!IsEligibleSpeakerPawn(pawn))
            {
                return null;
            }

            try
            {
                return PortraitsCache.Get(
                    pawn,
                    new Vector2(AvatarPortraitRequestSize, AvatarPortraitRequestSize),
                    Rot4.South,
                    AvatarCameraOffset,
                    AvatarCameraZoom);
            }
            catch
            {
                return null;
            }
        }

        private static void DrawAvatarFallback(Rect avatarRect, string label)
        {
            string letter = string.IsNullOrWhiteSpace(label) ? "?" : label.Trim()[0].ToString().ToUpperInvariant();
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.9f, 0.92f, 0.97f, 0.95f);
            Widgets.Label(avatarRect, letter);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }
}
