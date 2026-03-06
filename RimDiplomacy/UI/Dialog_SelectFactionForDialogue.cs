using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimDiplomacy.UI
{
    /// <summary>
    /// 派系选择对话框 - 用于通讯台拦截时显示
    /// </summary>
    public class Dialog_SelectFactionForDialogue : Window
    {
        private List<Faction> availableFactions = new List<Faction>();
        private Vector2 scrollPosition = Vector2.zero;
        private Faction selectedFaction;

        public override Vector2 InitialSize => new Vector2(600f, 500f);

        public Dialog_SelectFactionForDialogue()
        {
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            onlyOneOfTypeAllowed = true;

            LoadAvailableFactions();
        }

        private void LoadAvailableFactions()
        {
            availableFactions.Clear();

            if (Find.FactionManager?.AllFactions != null)
            {
                foreach (var faction in Find.FactionManager.AllFactions)
                {
                    if (faction != null && !faction.IsPlayer && !faction.defeated && !faction.Hidden)
                    {
                        availableFactions.Add(faction);
                    }
                }
            }

            // 按好感度排序
            availableFactions = availableFactions.OrderByDescending(f => f.PlayerGoodwill).ToList();

            if (availableFactions.Any())
            {
                selectedFaction = availableFactions.First();
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            var titleRect = new Rect(0f, 0f, inRect.width, 30f);
            Text.Font = GameFont.Medium;
            GUI.Label(titleRect, "RimDiplomacy_SelectFactionForDialogue".Translate());

            Text.Font = GameFont.Small;

            var contentRect = new Rect(0f, 40f, inRect.width, inRect.height - 120f);
            Widgets.DrawMenuSection(contentRect);

            var viewRect = new Rect(0f, 0f, contentRect.width - 16f, availableFactions.Count * 40f);
            Widgets.BeginScrollView(contentRect, ref scrollPosition, viewRect);

            float y = 0f;
            foreach (var faction in availableFactions)
            {
                var factionRect = new Rect(10f, y + 5f, viewRect.width - 20f, 35f);

                // 绘制派系背景
                if (faction == selectedFaction)
                {
                    Widgets.DrawHighlight(factionRect);
                }

                // 绘制派系图标和名称
                var iconRect = new Rect(factionRect.x, factionRect.y, 30f, 30f);
                if (faction.def != null)
                {
                    Texture2D factionIcon = faction.def.FactionIcon;
                    if (factionIcon != null && factionIcon != BaseContent.BadTex)
                    {
                        GUI.DrawTexture(iconRect, factionIcon);
                    }
                }

                var nameRect = new Rect(factionRect.x + 40f, factionRect.y, factionRect.width - 100f, 30f);
                Widgets.Label(nameRect, faction.Name);

                // 绘制好感度
                var goodwillRect = new Rect(factionRect.xMax - 80f, factionRect.y, 70f, 30f);
                var goodwillColor = GetGoodwillColor(faction.PlayerGoodwill);
                var oldColor = GUI.color;
                GUI.color = goodwillColor;
                Widgets.Label(goodwillRect, faction.PlayerGoodwill.ToString("+##;-##;0"));
                GUI.color = oldColor;

                // 点击选择
                if (Widgets.ButtonInvisible(factionRect))
                {
                    selectedFaction = faction;
                }

                y += 40f;
            }

            Widgets.EndScrollView();

            // 底部按钮
            var buttonRect = new Rect(inRect.x, inRect.y + inRect.height - 50f, inRect.width, 40f);
            var startButtonRect = new Rect(buttonRect.x, buttonRect.y, buttonRect.width / 2 - 10f, 40f);
            var cancelButtonRect = new Rect(buttonRect.x + buttonRect.width / 2 + 10f, buttonRect.y, buttonRect.width / 2 - 10f, 40f);

            if (Widgets.ButtonText(startButtonRect, "RimDiplomacy_StartDialogue".Translate()) && selectedFaction != null)
            {
                Close();
                Find.WindowStack.Add(new Dialog_DiplomacyDialogue(selectedFaction));
            }

            if (Widgets.ButtonText(cancelButtonRect, "RimDiplomacy_Cancel".Translate()))
            {
                Close();
            }
        }

        private Color GetGoodwillColor(int goodwill)
        {
            if (goodwill >= 80) return new Color(0.2f, 0.8f, 0.2f); // 盟友 - 绿色
            if (goodwill >= 40) return new Color(0.4f, 0.6f, 0.9f); // 朋友 - 蓝色
            if (goodwill >= -20) return new Color(0.8f, 0.8f, 0.3f); // 中立 - 黄色
            if (goodwill >= -60) return new Color(0.9f, 0.4f, 0.3f); // 敌对 - 橙色
            return new Color(0.9f, 0.2f, 0.2f); // 敌人 - 红色
        }
    }
}
