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
 /// Responsibility: social-circle tab rendering inside diplomacy dialogue window.
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
        private SocialPostCategory? socialCategoryFilter = null;
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
            DrawDialogueMainTabButton(chatRect, "RimChat_DialogueMainTabChat".Translate(), currentMainTab == DialogueMainTab.Chat, DialogueMainTab.Chat);
            DrawDialogueMainTabButton(socialRect, socialLabel, currentMainTab == DialogueMainTab.SocialCircle, DialogueMainTab.SocialCircle);
            DrawSocialToast(new Rect(socialRect.xMax + 8f, rect.y + 6f, rect.width - socialRect.xMax + rect.x - 8f, 20f));
            return 32f;
        }

        private bool IsChatTabActive()
        {
            return currentMainTab == DialogueMainTab.Chat;
        }

        private void DrawDialogueMainTabButton(Rect rect, string label, bool active, DialogueMainTab targetTab)
        {
            Color prev = GUI.color;
            GUI.color = active ? new Color(0.22f, 0.52f, 0.95f, 0.95f) : new Color(0.14f, 0.14f, 0.18f, 0.95f);
            if (Widgets.ButtonText(rect, label))
            {
                SetDialogueMainTab(targetTab);
            }
            GUI.color = prev;
        }

        private void SetDialogueMainTab(DialogueMainTab tab)
        {
            if (currentMainTab == tab) return;
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

            int total = manager.GetSocialPosts(999).Count;
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = new Color(0.7f, 0.75f, 0.82f);
            Widgets.Label(countRect, "RimChat_SocialTotalPosts".Translate(total));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawSocialFilterButton(Rect rect, string label, bool active, SocialPostCategory? filter)
        {
            Color prev = GUI.color;
            GUI.color = active ? new Color(0.22f, 0.5f, 0.88f, 0.95f) : new Color(0.13f, 0.14f, 0.17f, 0.95f);
            if (Widgets.ButtonText(rect, label))
            {
                socialCategoryFilter = filter;
            }
            GUI.color = prev;
        }

        private void DrawSocialPosts(Rect rect, GameComponent_DiplomacyManager manager)
        {
            List<PublicSocialPost> posts = manager.GetSocialPosts(999);
            if (socialCategoryFilter.HasValue)
            {
                posts = posts.Where(p => p.Category == socialCategoryFilter.Value).ToList();
            }

            if (posts.Count == 0)
            {
                GUI.color = new Color(0.66f, 0.7f, 0.75f);
                Widgets.Label(rect, "RimChat_SocialNoPosts".Translate());
                GUI.color = Color.white;
                return;
            }

            float viewWidth = rect.width - 18f;
            float totalHeight = 8f;
            List<float> heights = new List<float>(posts.Count);
            for (int i = 0; i < posts.Count; i++)
            {
                float height = GetSocialPostCardHeight(posts[i], viewWidth - 20f);
                heights.Add(height);
                totalHeight += height + 8f;
            }

            Rect viewRect = new Rect(0f, 0f, viewWidth, Mathf.Max(rect.height, totalHeight));
            socialPostScrollPosition = GUI.BeginScrollView(rect, socialPostScrollPosition, viewRect);

            float y = 4f;
            for (int i = 0; i < posts.Count; i++)
            {
                Rect cardRect = new Rect(6f, y, viewRect.width - 12f, heights[i]);
                DrawSocialPostCard(cardRect, posts[i], manager);
                y += heights[i] + 8f;
            }

            GUI.EndScrollView();
        }

        private float GetSocialPostCardHeight(PublicSocialPost post, float contentWidth)
        {
            float contentHeight = Text.CalcHeight(post?.Content ?? string.Empty, contentWidth);
            return Mathf.Max(128f, 86f + contentHeight);
        }

        private void DrawSocialPostCard(Rect rect, PublicSocialPost post, GameComponent_DiplomacyManager manager)
        {
            Color accent = GetCategoryAccent(post.Category);
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.11f, 0.14f, 0.98f));
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 3f, rect.height), accent);
            GUI.color = new Color(0.2f, 0.22f, 0.28f, 1f);
            Widgets.DrawBox(rect);
            GUI.color = Color.white;

            string sourceName = post.SourceFaction?.Name ?? "RimChat_Unknown".Translate();
            string targetName = post.TargetFaction?.Name ?? "RimChat_SocialNoTarget".Translate();
            string categoryLabel = SocialCircleService.GetCategoryLabel(post.Category);
            string credibility = Mathf.RoundToInt(post.Credibility * 100f).ToString();
            string likesText = "RimChat_SocialLikeCount".Translate(post.CurrentLikeCount);

            Rect headerRect = new Rect(rect.x + 10f, rect.y + 6f, rect.width - 20f, 20f);
            GUI.color = new Color(0.75f, 0.8f, 0.86f);
            Widgets.Label(headerRect, "RimChat_SocialPostHeaderEnhanced".Translate(sourceName, targetName, categoryLabel, credibility));

            Rect contentRect = new Rect(rect.x + 10f, rect.y + 28f, rect.width - 20f, rect.height - 64f);
            GUI.color = new Color(0.92f, 0.94f, 0.98f);
            Widgets.Label(contentRect, post.Content ?? string.Empty);

            Rect footerLeft = new Rect(rect.x + 10f, rect.yMax - 30f, rect.width - 270f, 20f);
            GUI.color = new Color(0.72f, 0.9f, 0.72f);
            Widgets.Label(footerLeft, "RimChat_SocialPostEffectLine".Translate(post.EffectSummary ?? string.Empty));

            Rect likesRect = new Rect(rect.xMax - 250f, rect.yMax - 30f, 75f, 20f);
            GUI.color = new Color(0.82f, 0.82f, 0.9f);
            Widgets.Label(likesRect, likesText);

            Rect timeRect = new Rect(rect.xMax - 170f, rect.yMax - 30f, 80f, 20f);
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = new Color(0.72f, 0.74f, 0.8f);
            Widgets.Label(timeRect, FormatSocialPostTime(post.CreatedTick));
            Text.Anchor = TextAnchor.UpperLeft;

            Rect likeBtnRect = new Rect(rect.xMax - 82f, rect.yMax - 32f, 72f, 22f);
            DrawLikeButton(likeBtnRect, post, manager, accent);
            GUI.color = Color.white;
        }

        private void DrawLikeButton(Rect rect, PublicSocialPost post, GameComponent_DiplomacyManager manager, Color accent)
        {
            if (post.LikedByPlayer)
            {
                GUI.color = new Color(accent.r, accent.g, accent.b, 0.75f);
                Widgets.DrawBoxSolid(rect, new Color(0.2f, 0.26f, 0.33f, 0.88f));
                Widgets.DrawBox(rect);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "RimChat_SocialLikedTag".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            if (!Widgets.ButtonText(rect, "RimChat_SocialLikeButton".Translate()))
            {
                return;
            }

            bool success = manager.TryLikeSocialPost(post.PostId, out int goodwillBonus);
            if (!success)
            {
                PushSocialToast("RimChat_SocialLikeAlready".Translate());
                return;
            }

            string text = goodwillBonus > 0
                ? "RimChat_SocialLikeReward".Translate(goodwillBonus)
                : "RimChat_SocialLikeNoReward".Translate();
            PushSocialToast(text);
        }

        private void DrawSocialToast(Rect rect)
        {
            if (string.IsNullOrEmpty(socialToast) || Time.realtimeSinceStartup > socialToastUntil) return;
            float alpha = Mathf.Clamp01((socialToastUntil - Time.realtimeSinceStartup) / 2.2f);
            GUI.color = new Color(0.75f, 0.9f, 0.75f, alpha);
            Widgets.Label(rect, socialToast);
            GUI.color = Color.white;
        }

        private void PushSocialToast(string text)
        {
            socialToast = text ?? string.Empty;
            socialToastUntil = Time.realtimeSinceStartup + 2.2f;
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

