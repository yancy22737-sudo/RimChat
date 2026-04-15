using System.Collections.Generic;
using System.Linq;
using RimChat.Prompting;
using RimChat.UI;
using RimWorld;
using Verse;

namespace RimChat.Config
{
    /// <summary>
    /// Dependencies: shared prompt variable browser caches and custom variable editor dialog.
    /// Responsibility: coordinate custom variable CRUD actions from settings UIs.
    /// </summary>
    public partial class RimChatSettings
    {
        internal void InvalidatePromptVariableBrowserCache()
        {
            _rimTalkVariableSnapshotReady = false;
            _rimTalkVariableCacheRefreshAt = -1f;
            _rimTalkVariableTooltipCache.Clear();
            InvalidatePromptVariableRowCache();
            MarkWorkspaceDirty(WorkspaceDirtySidePanel);
        }

        internal void OpenUserDefinedPromptVariableEditor(string path = null)
        {
            UserDefinedPromptVariableConfig variable = UserDefinedPromptVariableService.FindVariableByPath(path, this)?.Clone();
            var model = new UserDefinedPromptVariableEditModel
            {
                Variable = variable ?? new UserDefinedPromptVariableConfig(),
                FactionRules = variable == null
                    ? new List<FactionPromptVariableRuleConfig>()
                    : UserDefinedPromptVariableService.GetFactionRulesForKey(variable.Key, this),
                PawnRules = variable == null
                    ? new List<PawnPromptVariableRuleConfig>()
                    : UserDefinedPromptVariableService.GetPawnRulesForKey(variable.Key, this)
            };
            Find.WindowStack.Add(new Dialog_UserDefinedPromptVariableEditor(this, model, variable, () =>
            {
                InvalidatePromptVariableBrowserCache();
                _rimTalkSelectedVariableName = UserDefinedPromptVariableService.BuildPath(model.Variable.Key);
            }));
        }

        internal void TryDeleteUserDefinedPromptVariable(string path)
        {
            UserDefinedPromptVariableConfig config = UserDefinedPromptVariableService.FindVariableByPath(path, this);
            if (config == null)
            {
                return;
            }

            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "RimChat_CustomVariableDeleteConfirm".Translate(UserDefinedPromptVariableService.BuildPath(config.Key)),
                () =>
                {
                    if (UserDefinedPromptVariableService.TryDeleteVariable(this, path, out List<UserDefinedPromptVariableReferenceLocation> references))
                    {
                        InvalidatePromptVariableBrowserCache();
                        _rimTalkSelectedVariableName = string.Empty;
                        Messages.Message("RimChat_CustomVariableDeleteSuccess".Translate(UserDefinedPromptVariableService.BuildPath(config.Key)), MessageTypeDefOf.NeutralEvent, false);
                        return;
                    }

                    string details = string.Join("\n", references.Select(item => "- " + item.DisplayText));
                    Messages.Message("RimChat_CustomVariableDeleteBlocked".Translate(details), MessageTypeDefOf.RejectInput, false);
                },
                true,
                "RimChat_DeleteConfirmTitle".Translate()));
        }
    }
}
