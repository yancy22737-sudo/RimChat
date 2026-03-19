using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Core;
using RimChat.DiplomacySystem;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// Dependencies: GameComponent_DiplomacyManager social APIs and social-circle post models.
 /// Responsibility: render the diplomacy-window social-circle tab as a world-news feed.
 ///</summary>
    public partial class Dialog_DiplomacyDialogue
    {
        private enum DialogueMainTab
        {
            Chat,
            SocialCircle
        }

        private DialogueMainTab currentMainTab = DialogueMainTab.Chat;
        private Vector2 socialPostScrollPosition = Vector2.zero;
        private SocialPostCategory? socialCategoryFilter;
        private bool socialReadMarked;
        private string socialToast = string.Empty;
        private float socialToastUntil = -100f;

        private float DrawDialogueMainTabs(Rect rect)
        {
            int unreadCount = GameComponent_DiplomacyManager.Instance?.GetUnreadSocialPostCount() ?? 0;
            string socialLabel = unreadCount > 0
                ? "RimChat_SocialCircleTabWithUnread".Translate(unreadCount)
                : "RimChat_SocialCircleTab".Translate();
            Rect chatRect = new Rect(rect.x, rect.y, 122f, 28f);
            Rect socialRect = new Rect(chatRect.xMax + 6f, rect.y, 145f, 28f);
            Rect albumRect = new Rect(socialRect.xMax + 8f, rect.y, 98f, 28f);
            Rect selfieRect = new Rect(albumRect.xMax + 6f, rect.y, 98f, 28f);
            DrawDialogueMainTabButton(chatRect, "RimChat_DialogueMainTabChat".Translate(), currentMainTab == DialogueMainTab.Chat, DialogueMainTab.Chat);
            DrawDialogueMainTabButton(socialRect, socialLabel, currentMainTab == DialogueMainTab.SocialCircle, DialogueMainTab.SocialCircle);
            bool blockImageFeatures = ImageGenerationAvailability.IsBlocked();
            string blockedTooltip = blockImageFeatures ? ImageGenerationAvailability.GetBlockedMessage() : string.Empty;
            DrawActionTabButton(albumRect, "RimChat_DialogueMainTabAlbum".Translate(), OpenAlbumWindow, !blockImageFeatures, blockedTooltip);
            bool canSelfie = !blockImageFeatures && negotiator != null;
            DrawActionTabButton(
                selfieRect,
                "RimChat_DialogueMainTabSelfie".Translate(),
                OpenSelfieWindow,
                canSelfie,
                canSelfie ? string.Empty : (blockImageFeatures ? blockedTooltip : "RimChat_SelfieUnavailableNoNegotiator".Translate()));
            DrawSocialToast(new Rect(selfieRect.xMax + 8f, rect.y + 6f, rect.width - selfieRect.xMax + rect.x - 8f, 20f));
            return 32f;
        }

        private bool IsChatTabActive()
        {
            return currentMainTab == DialogueMainTab.Chat;
        }

        private void DrawDialogueMainTabButton(Rect rect, string label, bool active, DialogueMainTab targetTab)
        {
            Color previous = GUI.color;
            GUI.color = active ? new Color(0.22f, 0.52f, 0.95f, 0.95f) : new Color(0.14f, 0.14f, 0.18f, 0.95f);
            if (Widgets.ButtonText(rect, label))
            {
                SetDialogueMainTab(targetTab);
            }

            GUI.color = previous;
        }

        private void DrawActionTabButton(Rect rect, string label, Action onClick, bool enabled, string disabledTooltip = "")
        {
            Color previous = GUI.color;
            GUI.color = enabled ? new Color(0.14f, 0.14f, 0.18f, 0.95f) : new Color(0.12f, 0.12f, 0.14f, 0.65f);
            GUI.enabled = enabled;
            if (Widgets.ButtonText(rect, label))
            {
                onClick?.Invoke();
            }

            GUI.enabled = true;
            GUI.color = previous;
            if (!enabled && !string.IsNullOrWhiteSpace(disabledTooltip))
            {
                TooltipHandler.TipRegion(rect, disabledTooltip);
            }
        }

        private void SetDialogueMainTab(DialogueMainTab tab)
        {
            if (currentMainTab == tab)
            {
                return;
            }

            currentMainTab = tab;
            if (tab == DialogueMainTab.SocialCircle)
            {
                socialReadMarked = false;
            }
        }

        private void DrawSocialCirclePanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.075f, 0.075f, 0.095f, 0.98f));
            GUI.color = new Color(0.24f, 0.24f, 0.3f, 0.9f);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            GameComponent_DiplomacyManager manager = GameComponent_DiplomacyManager.Instance;
            if (manager == null)
            {
                Widgets.Label(rect.ContractedBy(12f), "RimChat_SocialUnavailable".Translate());
                return;
            }

            if (!socialReadMarked)
            {
                manager.MarkSocialPostsRead();
                socialReadMarked = true;
            }

            Rect inner = rect.ContractedBy(10f);
            Rect toolbarRect = new Rect(inner.x, inner.y, inner.width, 34f);
            DrawSocialToolbar(toolbarRect, manager);
            Rect listRect = new Rect(inner.x, toolbarRect.yMax + 6f, inner.width, inner.height - 40f);
            DrawSocialPosts(listRect, manager);
        }

        private void DrawSocialToolbar(Rect rect, GameComponent_DiplomacyManager manager)
        {
            Rect allRect = new Rect(rect.x, rect.y, 58f, rect.height);
            Rect militaryRect = new Rect(allRect.xMax + 4f, rect.y, 78f, rect.height);
            Rect ecoRect = new Rect(militaryRect.xMax + 4f, rect.y, 78f, rect.height);
            Rect dipRect = new Rect(ecoRect.xMax + 4f, rect.y, 78f, rect.height);
            Rect anomalyRect = new Rect(dipRect.xMax + 4f, rect.y, 78f, rect.height);
            Rect countRect = new Rect(rect.xMax - 180f, rect.y + 8f, 180f, 18f);

            DrawSocialFilterButton(allRect, "RimChat_SocialFilterAll".Translate(), !socialCategoryFilter.HasValue, null);
            DrawSocialFilterButton(militaryRect, "RimChat_NewsCategoryMilitary".Translate(), socialCategoryFilter == SocialPostCategory.Military, SocialPostCategory.Military);
            DrawSocialFilterButton(ecoRect, "RimChat_NewsCategoryEconomic".Translate(), socialCategoryFilter == SocialPostCategory.Economic, SocialPostCategory.Economic);
            DrawSocialFilterButton(dipRect, "RimChat_NewsCategoryDiplomatic".Translate(), socialCategoryFilter == SocialPostCategory.Diplomatic, SocialPostCategory.Diplomatic);
            DrawSocialFilterButton(anomalyRect, "RimChat_NewsCategoryAnomaly".Translate(), socialCategoryFilter == SocialPostCategory.Anomaly, SocialPostCategory.Anomaly);

            GUI.color = new Color(0.75f, 0.8f, 0.86f);
            Widgets.Label(countRect, "RimChat_SocialNewsCount".Translate(GetVisibleSocialPosts(manager).Count));
            GUI.color = Color.white;
        }

        private void DrawSocialFilterButton(Rect rect, string label, bool active, SocialPostCategory? category)
        {
            Color previous = GUI.color;
            GUI.color = active ? new Color(0.25f, 0.45f, 0.8f, 0.95f) : new Color(0.16f, 0.16f, 0.2f, 0.95f);
            if (Widgets.ButtonText(rect, label))
            {
                socialCategoryFilter = category;
            }

            GUI.color = previous;
        }

        private void DrawSocialPosts(Rect rect, GameComponent_DiplomacyManager manager)
        {
            List<PublicSocialPost> posts = GetVisibleSocialPosts(manager);
            if (posts.Count == 0)
            {
                GUI.color = new Color(0.8f, 0.82f, 0.88f);
                Widgets.Label(rect.ContractedBy(12f), "RimChat_SocialNoPosts".Translate());
                GUI.color = Color.white;
                return;
            }

            float viewWidth = rect.width - 16f;
            float cardWidth = viewWidth - 6f;
            float totalHeight = 0f;
            foreach (PublicSocialPost post in posts)
            {
                totalHeight += GetSocialPostCardHeight(post, cardWidth - 20f) + 8f;
            }

            Rect viewRect = new Rect(0f, 0f, viewWidth, Mathf.Max(rect.height, totalHeight));
            Widgets.BeginScrollView(rect, ref socialPostScrollPosition, viewRect);
            float cursorY = 0f;
            foreach (PublicSocialPost post in posts)
            {
                float cardHeight = GetSocialPostCardHeight(post, cardWidth - 20f);
                DrawSocialPostCard(new Rect(0f, cursorY, cardWidth, cardHeight), post);
                cursorY += cardHeight + 8f;
            }

            Widgets.EndScrollView();
        }

        private List<PublicSocialPost> GetVisibleSocialPosts(GameComponent_DiplomacyManager manager)
        {
            IEnumerable<PublicSocialPost> posts = manager.GetSocialPosts();
            if (socialCategoryFilter.HasValue)
            {
                posts = posts.Where(post => post.Category == socialCategoryFilter.Value);
            }

            return posts.ToList();
        }

        private float GetSocialPostCardHeight(PublicSocialPost post, float contentWidth)
        {
            float height = 54f;
            height += GetTextHeight(post?.Headline, contentWidth, GameFont.Medium) + 6f;
            height += GetTextHeight(post?.Lead, contentWidth, GameFont.Small) + 10f;
            height += GetSectionHeight(post?.Cause, contentWidth);
            height += GetSectionHeight(post?.Process, contentWidth);
            height += GetSectionHeight(post?.Outlook, contentWidth);
            if (!string.IsNullOrWhiteSpace(post?.Quote))
            {
                height += GetQuoteHeight(post, contentWidth);
            }

            return Mathf.Max(156f, height + 12f);
        }

        private float GetSectionHeight(string content, float width)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return 0f;
            }

            return 20f + GetTextHeight(content, width, GameFont.Small) + 8f;
        }

        private float GetQuoteHeight(PublicSocialPost post, float width)
        {
            float contentWidth = width - 20f;
            float quoteHeight = GetTextHeight(post?.Quote, contentWidth, GameFont.Small);
            float attributionHeight = GetTextHeight(BuildQuoteAttribution(post), contentWidth, GameFont.Tiny);
            return quoteHeight + attributionHeight + 24f;
        }

        private void DrawSocialPostCard(Rect rect, PublicSocialPost post)
        {
            Color accent = GetCategoryAccent(post.Category);
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.11f, 0.14f, 0.98f));
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 3f, rect.height), accent);
            GUI.color = new Color(0.2f, 0.22f, 0.28f, 1f);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            float x = rect.x + 10f;
            float width = rect.width - 20f;
            float y = rect.y + 6f;

            y = DrawMetaLine(new Rect(x, y, width, 18f), post);
            y = DrawHeadline(new Rect(x, y, width, 40f), post) + 4f;
            y = DrawActorsLine(new Rect(x, y, width, 18f), post);
            y = DrawLead(new Rect(x, y, width, 200f), post) + 6f;
            y = DrawNewsSection(x, y, width, "RimChat_SocialNewsCauseLabel", post.Cause);
            y = DrawNewsSection(x, y, width, "RimChat_SocialNewsProcessLabel", post.Process);
            y = DrawNewsSection(x, y, width, "RimChat_SocialNewsOutlookLabel", post.Outlook);
            DrawQuoteBlock(x, y, width, post, accent);
            GUI.color = Color.white;
        }

        private float DrawMetaLine(Rect rect, PublicSocialPost post)
        {
            string sourceLabel = SocialCircleService.ResolveDisplayLabel(post.SourceLabel);
            string category = SocialCircleService.GetCategoryLabel(post.Category);
            string credibility = SocialCircleService.ResolveDisplayLabel(post.CredibilityLabel);
            Rect leftRect = new Rect(rect.x, rect.y, rect.width - 90f, rect.height);
            Rect rightRect = new Rect(rect.xMax - 88f, rect.y, 88f, rect.height);
            GUI.color = new Color(0.72f, 0.76f, 0.83f);
            Widgets.Label(leftRect, "RimChat_SocialNewsMetaLine".Translate(sourceLabel, category, credibility));
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(rightRect, FormatSocialPostTime(post.CreatedTick));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            return rect.yMax;
        }

        private float DrawHeadline(Rect rect, PublicSocialPost post)
        {
            float height = GetTextHeight(post?.Headline, rect.width, GameFont.Medium);
            Rect drawRect = new Rect(rect.x, rect.y, rect.width, height);
            Text.Font = GameFont.Medium;
            GUI.color = new Color(0.94f, 0.95f, 0.98f);
            Widgets.Label(drawRect, post?.Headline ?? string.Empty);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            return drawRect.yMax;
        }

        private float DrawActorsLine(Rect rect, PublicSocialPost post)
        {
            string sourceName = post?.SourceFaction?.Name;
            string targetName = post?.TargetFaction?.Name;
            bool hasSource = !string.IsNullOrWhiteSpace(sourceName);
            bool hasTarget = !string.IsNullOrWhiteSpace(targetName);

            if (!hasSource && !hasTarget)
            {
                return rect.y;
            }

            GUI.color = new Color(0.77f, 0.84f, 0.91f);
            if (hasSource && hasTarget)
            {
                Widgets.Label(rect, "RimChat_SocialNewsActorsLine".Translate(sourceName, targetName));
            }
            else
            {
                string factionName = hasSource ? sourceName : targetName;
                Widgets.Label(rect, "RimChat_SocialNewsSingleFactionLine".Translate(factionName));
            }

            GUI.color = Color.white;
            return rect.yMax + 2f;
        }

        private float DrawLead(Rect rect, PublicSocialPost post)
        {
            float height = GetTextHeight(post?.Lead, rect.width, GameFont.Small);
            Rect drawRect = new Rect(rect.x, rect.y, rect.width, height);
            GUI.color = new Color(0.88f, 0.9f, 0.95f);
            Widgets.Label(drawRect, post?.Lead ?? string.Empty);
            GUI.color = Color.white;
            return drawRect.yMax;
        }

        private float DrawNewsSection(float x, float y, float width, string labelKey, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return y;
            }

            Rect labelRect = new Rect(x, y, width, 18f);
            GUI.color = new Color(0.68f, 0.82f, 0.93f);
            Widgets.Label(labelRect, labelKey.Translate());
            GUI.color = Color.white;

            float height = GetTextHeight(content, width, GameFont.Small);
            Rect textRect = new Rect(x, labelRect.yMax, width, height);
            GUI.color = new Color(0.86f, 0.89f, 0.94f);
            Widgets.Label(textRect, content);
            GUI.color = Color.white;
            return textRect.yMax + 8f;
        }

        private void DrawQuoteBlock(float x, float y, float width, PublicSocialPost post, Color accent)
        {
            if (string.IsNullOrWhiteSpace(post?.Quote))
            {
                return;
            }

            float contentWidth = width - 20f;
            float quoteHeight = GetTextHeight(post.Quote, contentWidth, GameFont.Small);
            float attributionHeight = GetTextHeight(BuildQuoteAttribution(post), contentWidth, GameFont.Tiny);
            float height = quoteHeight + attributionHeight + 24f;
            Rect blockRect = new Rect(x, y, width, height);
            Widgets.DrawBoxSolid(blockRect, new Color(0.12f, 0.14f, 0.18f, 0.96f));
            Widgets.DrawBoxSolid(new Rect(blockRect.x, blockRect.y, 3f, blockRect.height), accent * 0.9f);

            Rect contentRect = blockRect.ContractedBy(10f);
            Rect quoteRect = new Rect(contentRect.x, contentRect.y, contentRect.width, quoteHeight);
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.93f, 0.94f, 0.98f);
            Widgets.Label(quoteRect, post.Quote ?? string.Empty);

            Rect attributionRect = new Rect(contentRect.x, quoteRect.yMax + 4f, contentRect.width, attributionHeight);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.76f, 0.84f);
            Widgets.Label(attributionRect, BuildQuoteAttribution(post));

            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private string BuildQuoteAttribution(PublicSocialPost post)
        {
            string attribution = string.IsNullOrWhiteSpace(post?.QuoteAttribution)
                ? "RimChat_SocialNewsUnnamedSource".Translate().ToString()
                : post.QuoteAttribution;
            return "RimChat_SocialNewsQuoteAttribution".Translate(attribution);
        }

        private void DrawSocialToast(Rect rect)
        {
            if (string.IsNullOrEmpty(socialToast) || Time.realtimeSinceStartup > socialToastUntil)
            {
                return;
            }

            float alpha = Mathf.Clamp01((socialToastUntil - Time.realtimeSinceStartup) / 2.2f);
            GUI.color = new Color(0.75f, 0.9f, 0.75f, alpha);
            Widgets.Label(rect, socialToast);
            GUI.color = Color.white;
        }

        private float GetTextHeight(string text, float width, GameFont font)
        {
            GameFont previous = Text.Font;
            Text.Font = font;
            float height = Text.CalcHeight(text ?? string.Empty, width);
            Text.Font = previous;
            return height;
        }

        private Color GetCategoryAccent(SocialPostCategory category)
        {
            switch (category)
            {
                case SocialPostCategory.Military:
                    return new Color(0.86f, 0.38f, 0.38f);
                case SocialPostCategory.Economic:
                    return new Color(0.88f, 0.72f, 0.35f);
                case SocialPostCategory.Anomaly:
                    return new Color(0.58f, 0.7f, 0.95f);
                default:
                    return new Color(0.45f, 0.84f, 0.72f);
            }
        }

        private string FormatSocialPostTime(int tick)
        {
            int currentTick = Find.TickManager?.TicksGame ?? tick;
            int diff = Math.Max(0, currentTick - tick);
            float days = diff / GenDate.TicksPerDay;
            if (days < 1f)
            {
                float hours = diff / 2500f;
                return "RimChat_SocialHoursAgo".Translate(hours.ToString("F1"));
            }

            return "RimChat_SocialDaysAgo".Translate(days.ToString("F1"));
        }
    }
}
