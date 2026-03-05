using System.Collections.Generic;
using RimWorld.QuestGen;
using Verse;
using Verse.Grammar;

namespace RimDiplomacy.DiplomacySystem
{
    /// <summary>
    /// 将 Slate 中的变量注入到 Grammar Rules 中，以便在任务文本中使用 [variable] 引用
    /// </summary>
    public class QuestNode_InjectSlateToGrammar : QuestNode
    {
        public SlateRef<string> prefix;

        protected override bool TestRunInt(Slate slate)
        {
            return true;
        }

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            string p = prefix.GetValue(slate) ?? "";

            // 注入 title
            if (slate.Exists("title"))
            {
                string val = slate.Get<string>("title");
                QuestGen.AddQuestNameRules(new List<Rule> { new Rule_String(p + "title", val) });
            }

            // 注入 description
            if (slate.Exists("description"))
            {
                string val = slate.Get<string>("description");
                QuestGen.AddQuestDescriptionRules(new List<Rule> { new Rule_String(p + "description", val) });
            }
            
            // 注入 rewardDescription
            if (slate.Exists("rewardDescription"))
            {
                string val = slate.Get<string>("rewardDescription");
                QuestGen.AddQuestDescriptionRules(new List<Rule> { new Rule_String(p + "rewardDescription", val) });
            }
        }
    }
}
