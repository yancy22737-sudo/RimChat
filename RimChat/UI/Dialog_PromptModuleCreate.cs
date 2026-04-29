using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Config;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: PromptUnifiedNodeSchemaCatalog, PromptUnifiedNodeSlot enum.
    /// Responsibility: collect ID, display name, and slot for a new custom prompt module.
    /// </summary>
    public sealed class Dialog_PromptModuleCreate : Window
    {
        private readonly Action<string, string, PromptUnifiedNodeSlot> _onCreate;
        private string _moduleId = string.Empty;
        private string _displayName = string.Empty;
        private PromptUnifiedNodeSlot _selectedSlot = PromptUnifiedNodeSlot.MainChainAfter;
        private string _validationError = string.Empty;

        public override Vector2 InitialSize => new Vector2(520f, 340f);

        internal Dialog_PromptModuleCreate(Action<string, string, PromptUnifiedNodeSlot> onCreate)
        {
            _onCreate = onCreate;
            doCloseButton = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "RimChat_ModuleCreateTitle".Translate());
            Text.Font = GameFont.Small;
            float y = inRect.y + 36f;

            Widgets.Label(new Rect(inRect.x, y, 100f, 24f), "RimChat_ModuleCreateIdLabel".Translate());
            _moduleId = Widgets.TextField(new Rect(inRect.x + 105f, y, inRect.width - 105f, 24f), _moduleId);
            y += 28f;

            GUI.color = Color.gray;
            Widgets.Label(new Rect(inRect.x + 105f, y, inRect.width - 105f, 20f), "RimChat_ModuleCreateIdHint".Translate());
            GUI.color = Color.white;
            y += 24f;

            Widgets.Label(new Rect(inRect.x, y, 100f, 24f), "RimChat_ModuleCreateNameLabel".Translate());
            _displayName = Widgets.TextField(new Rect(inRect.x + 105f, y, inRect.width - 105f, 24f), _displayName);
            y += 32f;

            Widgets.Label(new Rect(inRect.x, y, 100f, 24f), "RimChat_ModuleCreateSlotLabel".Translate());
            Rect slotButtonRect = new Rect(inRect.x + 105f, y, 200f, 24f);
            if (Widgets.ButtonText(slotButtonRect, GetSlotLabel(_selectedSlot)))
            {
                ShowSlotMenu();
            }
            y += 36f;

            if (!string.IsNullOrEmpty(_validationError))
            {
                GUI.color = Color.red;
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), _validationError);
                GUI.color = Color.white;
                y += 24f;
            }

            Rect createRect = new Rect(inRect.xMax - 220f, inRect.yMax - 34f, 100f, 30f);
            Rect cancelRect = new Rect(inRect.xMax - 110f, inRect.yMax - 34f, 100f, 30f);

            if (Widgets.ButtonText(createRect, "RimChat_CreateButton".Translate()))
            {
                if (TryValidate())
                {
                    string normalizedId = _moduleId.Trim().ToLowerInvariant();
                    string name = _displayName.Trim();
                    _onCreate?.Invoke(normalizedId, name, _selectedSlot);
                    Close();
                }
            }

            if (Widgets.ButtonText(cancelRect, "RimChat_CancelButton".Translate()))
            {
                Close();
            }
        }

        private bool TryValidate()
        {
            string id = _moduleId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                _validationError = "RimChat_ModuleCreateIdEmpty".Translate();
                return false;
            }

            if (!IsValidId(id))
            {
                _validationError = "RimChat_ModuleCreateIdInvalid".Translate();
                return false;
            }

            string normalized = id.ToLowerInvariant();
            if (PromptUnifiedNodeSchemaCatalog.TryGet(normalized, out _))
            {
                _validationError = "RimChat_ModuleCreateIdCollision".Translate();
                return false;
            }

            if (string.IsNullOrWhiteSpace(_displayName))
            {
                _validationError = "RimChat_ModuleCreateNameEmpty".Translate();
                return false;
            }

            _validationError = string.Empty;
            return true;
        }

        private static bool IsValidId(string id)
        {
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (!char.IsLower(c) && !char.IsDigit(c) && c != '_')
                {
                    return false;
                }
            }

            return true;
        }

        private void ShowSlotMenu()
        {
            List<FloatMenuOption> options = Enum.GetValues(typeof(PromptUnifiedNodeSlot))
                .Cast<PromptUnifiedNodeSlot>()
                .Select(slot => new FloatMenuOption(GetSlotLabel(slot), () => _selectedSlot = slot))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static string GetSlotLabel(PromptUnifiedNodeSlot slot)
        {
            switch (slot)
            {
                case PromptUnifiedNodeSlot.MetadataAfter:
                    return "RimChat_PromptNodeSlot_MetadataAfter".Translate().ToString();
                case PromptUnifiedNodeSlot.MainChainBefore:
                    return "RimChat_PromptNodeSlot_MainChainBefore".Translate().ToString();
                case PromptUnifiedNodeSlot.MainChainAfter:
                    return "RimChat_PromptNodeSlot_MainChainAfter".Translate().ToString();
                case PromptUnifiedNodeSlot.DynamicDataAfter:
                    return "RimChat_PromptNodeSlot_DynamicDataAfter".Translate().ToString();
                case PromptUnifiedNodeSlot.ContractBeforeEnd:
                    return "RimChat_PromptNodeSlot_ContractBeforeEnd".Translate().ToString();
                default:
                    return slot.ToSerializedValue();
            }
        }
    }
}
