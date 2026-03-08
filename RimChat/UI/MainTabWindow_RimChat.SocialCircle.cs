using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.DiplomacySystem;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>/// Dependencies: GameComponent_DiplomacyManager social APIs.
 /// Responsibility: social-circle tab entry, filtering, and feed rendering.
 ///</summary>
    public partial class MainTabWindow_RimChat
    {
        private enum RimChatMainTab
        {
            Factions,
            SocialCircle
        }

        private RimChatMainTab currentMainTab = RimChatMainTab.Factions;
        private Vector2 socialPostScrollPosition = Vector2.zero;
        private SocialPostCategory? socialCategoryFilter = null;
        private bool socialReadMarked;

        private void DrawMainTabButtons(Rect headerRect)
        {
            int unreadCount = GameComponent_DiplomacyManager.Instance?.GetUnreadSocialPostCount() ?? 0;
            string socialLabel = unreadCount > 0
                ? "RimChat_SocialCircleTabWithUnread".Translate(unreadCount)
                : "RimChat_SocialCircleTab".Translate();

            Rect factionsRect = new Rect(headerRect.xMax - 285f, headerRect.y + 42f, 130f, 24f);
            Rect socialRect = new Rect(headerRect.xMax - 150f, headerRect.y + 42f, 135f, 24f);

            DrawMainTabButton(factionsRect, "RimChat_FactionsTab".Translate(), currentMainTab == RimChatMainTab.Factions, RimChatMainTab.Factions);
            DrawMainTabButton(socialRect, socialLabel, currentMainTab == RimChatMainTab.SocialCircle, RimChatMainTab.SocialCircle);
        }

        private void DrawMainTabButton(Rect rect, string label, bool active, RimChatMainTab targetTab)
        {
            Color prev = GUI.color;
            if (active)
            {
                GUI.color = new Color(0.27f, 0.55f, 0.95f, 0.95f);
            }
            else
            {
                GUI.color = new Color(0.18f, 0.18f, 0.22f, 0.95f);
            }

            if (Widgets.ButtonText(rect, label))
            {
                SetMainTab(targetTab);
            }
            GUI.color = prev;
        }

        private void SetMainTab(RimChatMainTab tab)
        {
            if (currentMainTab == tab) return;
            currentMainTab = tab;
            if (tab == RimChatMainTab.SocialCircle)
            {
                socialReadMarked = false;
            }
        }

        private void DrawSocialCirclePanel(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, PanelColor);
            GUI.color = BorderColor;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            var manager = GameComponent_DiplomacyManager.Instance;
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

            Rect inner = rect.ContractedBy(12f);
            Rect toolbarRect = new Rect(inner.x, inner.y, inner.width, 34f);
            DrawSocialToolbar(toolbarRect, manager);

            Rect listRect = new Rect(inner.x, toolbarRect.yMax + 6f, inner.width, inner.height - 40f);
            DrawSocialPostList(listRect, manager);
        }

        private void DrawSocialToolbar(Rect rect, GameComponent_DiplomacyManager manager)
        {
            Rect allRect = new Rect(rect.x, rect.y, 65f, rect.height);
            Rect militaryRect = new Rect(allRect.xMax + 4f, rect.y, 90f, rect.height);
            Rect ecoRect = new Rect(militaryRect.xMax + 4f, rect.y, 90f, rect.height);
            Rect dipRect = new Rect(ecoRect.xMax + 4f, rect.y, 90f, rect.height);
            Rect anomalyRect = new Rect(dipRect.xMax + 4f, rect.y, 90f, rect.height);
            Rect countRect = new Rect(rect.xMax - 180f, rect.y + 7f, 180f, 20f);

            DrawFilterButton(allRect, "RimChat_SocialFilterAll".Translate(), !socialCategoryFilter.HasValue, null);
            DrawFilterButton(militaryRect, "RimChat_NewsCategoryMilitary".Translate(), socialCategoryFilter == SocialPostCategory.Military, SocialPostCategory.Military);
            DrawFilterButton(ecoRect, "RimChat_NewsCategoryEconomic".Translate(), socialCategoryFilter == SocialPostCategory.Economic, SocialPostCategory.Economic);
            DrawFilterButton(dipRect, "RimChat_NewsCategoryDiplomatic".Translate(), socialCategoryFilter == SocialPostCategory.Diplomatic, SocialPostCategory.Diplomatic);
            DrawFilterButton(anomalyRect, "RimChat_NewsCategoryAnomaly".Translate(), socialCategoryFilter == SocialPostCategory.Anomaly, SocialPostCategory.Anomaly);

            int total = manager.GetSocialPosts(999).Count;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = TextSecondary;
            Widgets.Label(countRect, "RimChat_SocialTotalPosts".Translate(total));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawFilterButton(Rect rect, string label, bool active, SocialPostCategory? category)
        {
            Color prev = GUI.color;
            GUI.color = active ? new Color(0.28f, 0.55f, 0.95f, 0.95f) : new Color(0.16f, 0.16f, 0.2f, 0.95f);
            if (Widgets.ButtonText(rect, label))
            {
                socialCategoryFilter = category;
            }
            GUI.color = prev;
        }

        private void DrawSocialPostList(Rect rect, GameComponent_DiplomacyManager manager)
        {
            List<PublicSocialPost> posts = manager.GetSocialPosts(999);
            if (socialCategoryFilter.HasValue)
            {
                posts = posts.Where(p => p.Category == socialCategoryFilter.Value).ToList();
            }

            if (posts.Count == 0)
            {
                Widgets.Label(rect, "RimChat_SocialNoPosts".Translate());
                return;
            }

            float curY = 0f;
            float viewWidth = rect.width - 16f;
            List<float> heights = new List<float>(posts.Count);
            for (int i = 0; i < posts.Count; i++)
            {
                float cardHeight = GetPostCardHeight(posts[i], viewWidth - 24f);
                heights.Add(cardHeight);
                curY += cardHeight + 8f;
            }

            Rect viewRect = new Rect(0f, 0f, viewWidth, Math.Max(curY + 8f, rect.height));
            socialPostScrollPosition = GUI.BeginScrollView(rect, socialPostScrollPosition, viewRect);

            float y = 0f;
            for (int i = 0; i < posts.Count; i++)
            {
                Rect cardRect = new Rect(6f, y, viewRect.width - 12f, heights[i]);
                DrawSocialPostCard(cardRect, posts[i]);
                y += heights[i] + 8f;
            }

            GUI.EndScrollView();
        }

        private float GetPostCardHeight(PublicSocialPost post, float width)
        {
            if (post == null) return 110f;
            float contentHeight = Text.CalcHeight(post.Content ?? string.Empty, width);
            return Math.Max(110f, 74f + contentHeight);
        }

        private void DrawSocialPostCard(Rect rect, PublicSocialPost post)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.11f, 0.11f, 0.14f, 0.95f));
            GUI.color = BorderColor;
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            string sourceName = post.SourceFaction?.Name ?? "RimChat_Unknown".Translate();
            string targetName = post.TargetFaction?.Name ?? "RimChat_SocialNoTarget".Translate();
            string categoryLabel = SocialCircleService.GetCategoryLabel(post.Category);
            string credibility = Mathf.RoundToInt(post.Credibility * 100f).ToString();

            Rect headerRect = new Rect(rect.x + 10f, rect.y + 6f, rect.width - 20f, 20f);
            GUI.color = TextSecondary;
            Widgets.Label(headerRect, "RimChat_SocialPostHeader".Translate(sourceName, targetName, categoryLabel, credibility));

            Rect contentRect = new Rect(rect.x + 10f, rect.y + 28f, rect.width - 20f, rect.height - 52f);
            GUI.color = TextPrimary;
            Widgets.Label(contentRect, post.Content ?? string.Empty);

            Rect footerLeft = new Rect(rect.x + 10f, rect.yMax - 20f, rect.width - 160f, 18f);
            GUI.color = new Color(0.7f, 0.85f, 0.7f);
            Widgets.Label(footerLeft, "RimChat_SocialPostEffectLine".Translate(post.EffectSummary ?? string.Empty));

            Rect footerRight = new Rect(rect.xMax - 150f, rect.yMax - 20f, 140f, 18f);
            GUI.color = TextSecondary;
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(footerRight, FormatSocialPostTime(post.CreatedTick));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private string FormatSocialPostTime(int tick)
        {
            int currentTick = Find.TickManager?.TicksGame ?? tick;
            int diff = Math.Max(0, currentTick - tick);
            float days = diff / 60000f;
            if (days < 1f)
            {
                float hours = diff / 2500f;
                return "RimChat_SocialHoursAgo".Translate(hours.ToString("F1"));
            }
            return "RimChat_SocialDaysAgo".Translate(days.ToString("F1"));
        }
    }
}


