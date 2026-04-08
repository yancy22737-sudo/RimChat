using System;
using System.Collections.Generic;
using System.Linq;
using RimChat.Config;
using RimChat.Core;
using RimChat.DiplomacySystem;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimChat.UI
{
    /// <summary>
    /// Dependencies: diplomacy image settings/service, player colonist roster, and portrait rendering APIs.
    /// Responsibility: select a player colonist, assemble selfie prompt options, and trigger image generation.
    /// </summary>
    public sealed class Dialog_DiplomacySelfieConfig : Window
    {
        private const int PortraitSize = 512;
        private const string DefaultPromptText = "帮我生成图片：将图片的动漫风格角色转为低饱和二次元摄影cos风格，做自拍动作，采用现实对脸自拍构图，保留核心服饰元素，背景设为派系典型场景，光线偏暖调以增强氛围感，全身照。比例 4:3。";

        private readonly Faction faction;
        private readonly Pawn negotiator;
        private readonly List<Pawn> selectableColonists;
        private Pawn selectedColonist;
        private string promptText = string.Empty;
        private string sizeText = DiplomacyImageApiConfig.DefaultImageSize;
        private string captionText = string.Empty;
        private bool watermark;
        private bool isGenerating;
        private string status = string.Empty;
        private Vector2 scrollPos = Vector2.zero;

        private bool includeAge = true;
        private bool includeGender = true;
        private bool includeFaction = true;
        private bool includeRole = true;
        private bool includeBodyType = true;
        private bool includeHair = true;
        private bool includeXenotype = true;
        private bool includeApparel = true;
        private bool includeHediffs = true;
        private bool includeHealth = true;
        private bool includeWeapon = true;
        private bool includeEquipment = true;
        private bool includePositivePrompt;
        private bool includeNegativePrompt;

        private const string PositivePromptDefault = "masterwork, masterpiece, best quality, detailed, depth of field, high detail, very aesthetic, dynamic pose, dynamic angle";
        private const string NegativePromptDefault = "lowres, worst quality, low quality, bad anatomy, bad hands, jpeg, artifacts ((signature, watermark, text, logo, artist name, patreon_username, web_address, username):1.5), extra digits, censored, chibi, sweat, particles, parted lips, artist logo";

        private string positivePromptText = PositivePromptDefault;
        private string negativePromptText = NegativePromptDefault;

        public override Vector2 InitialSize => new Vector2(720f, 640f);

        public Dialog_DiplomacySelfieConfig(Faction faction, Pawn negotiator)
        {
            this.faction = faction;
            this.negotiator = negotiator;
            selectableColonists = ResolveSelectableColonists();
            selectedColonist = ResolveInitialColonist(selectableColonists, negotiator);
            doCloseX = true;
            closeOnAccept = false;
            closeOnCancel = true;
            absorbInputAroundWindow = true;

            RimChatSettings settings = Core.RimChatMod.Settings;
            DiplomacyImageApiConfig imageConfig = settings?.DiplomacyImageApi;
            if (imageConfig != null)
            {
                sizeText = imageConfig.DefaultSize;
                watermark = imageConfig.DefaultWatermark;
            }

            ApplyPersistedState(settings);

            string colonistName = selectedColonist?.LabelShort ?? negotiator?.LabelShort ?? "Pawn";
            captionText = string.IsNullOrWhiteSpace(captionText)
                ? "RimChat_SelfieDefaultCaption".Translate(colonistName)
                : captionText;
            promptText = string.IsNullOrWhiteSpace(promptText) ? DefaultPromptText : promptText;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "RimChat_SelfieConfigTitle".Translate());

            Rect formRect = new Rect(inRect.x, inRect.y + 34f, inRect.width, inRect.height - 84f);
            DrawForm(formRect);

            Rect statusRect = new Rect(inRect.x, inRect.yMax - 48f, inRect.width - 130f, 20f);
            DrawStatus(statusRect);

            Rect runRect = new Rect(inRect.xMax - 120f, inRect.yMax - 56f, 120f, 28f);
            GUI.enabled = !isGenerating && selectedColonist != null;
            if (Widgets.ButtonText(runRect, "RimChat_SelfieGenerate".Translate()))
            {
                StartGenerate();
            }

            GUI.enabled = true;
        }

        private void DrawForm(Rect rect)
        {
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, 1220f);
            Widgets.BeginScrollView(rect, ref scrollPos, viewRect);
            float y = 0f;

            y += DrawColonistSelector(new Rect(0f, y, viewRect.width, 90f));
            Widgets.Label(new Rect(0f, y, viewRect.width, 24f), "RimChat_SelfiePrompt".Translate());
            y += 26f;
            promptText = Widgets.TextArea(new Rect(0f, y, viewRect.width, 120f), promptText);
            y += 128f;

            Widgets.Label(new Rect(0f, y, 120f, 24f), "RimChat_SelfieSize".Translate());
            sizeText = Widgets.TextField(new Rect(120f, y, 170f, 24f), sizeText);
            y += 30f;

            Widgets.Label(new Rect(0f, y, 120f, 24f), "RimChat_SelfieCaption".Translate());
            captionText = Widgets.TextField(new Rect(120f, y, viewRect.width - 120f, 24f), captionText);
            y += 30f;

            Widgets.CheckboxLabeled(new Rect(0f, y, viewRect.width, 24f), "RimChat_SelfieWatermark".Translate(), ref watermark);
            y += 32f;

            y += DrawInjectionSwitches(new Rect(0f, y, viewRect.width, 360f));
            y += 10f;
            DrawContextPreview(new Rect(0f, y, viewRect.width, 220f));
            Widgets.EndScrollView();
        }

        private float DrawColonistSelector(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.13f, 0.95f));
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 24f), "选择自拍殖民者");
            Rect buttonRect = new Rect(rect.x + 8f, rect.y + 34f, rect.width - 16f, 28f);
            string label = selectedColonist?.LabelShortCap ?? "请选择正式殖民者";
            if (Widgets.ButtonText(buttonRect, label))
            {
                var options = selectableColonists
                    .Select(pawn => new FloatMenuOption(pawn.LabelShortCap, () =>
                    {
                        selectedColonist = pawn;
                        captionText = "RimChat_SelfieDefaultCaption".Translate(selectedColonist?.LabelShort ?? "Pawn");
                        PersistUiState();
                    }))
                    .ToList();
                if (options.Count == 0)
                {
                    options.Add(new FloatMenuOption("没有可用正式殖民者", null));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }

            Widgets.Label(new Rect(rect.x + 8f, rect.y + 66f, rect.width - 16f, 20f), "仅显示玩家阵营正式殖民者。生成时会提取所选小人的 RimWorld 肖像渲染图。");
            return rect.height;
        }

        private float DrawInjectionSwitches(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.1f, 0.1f, 0.13f, 0.95f));
            GUI.color = new Color(0.85f, 0.88f, 0.94f);
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 24f), "附加提示词选项");
            GUI.color = Color.white;

            float x = rect.x + 8f;
            float y = rect.y + 32f;
            float leftWidth = rect.width * 0.48f;
            float rightX = rect.x + rect.width * 0.5f;
            float rightWidth = rect.width * 0.48f;

            Widgets.CheckboxLabeled(new Rect(x, y, leftWidth, 24f), "年龄", ref includeAge); y += 24f;
            Widgets.CheckboxLabeled(new Rect(x, y, leftWidth, 24f), "性别", ref includeGender); y += 24f;
            Widgets.CheckboxLabeled(new Rect(x, y, leftWidth, 24f), "派系", ref includeFaction); y += 24f;
            Widgets.CheckboxLabeled(new Rect(x, y, leftWidth, 24f), "身份/职业", ref includeRole); y += 24f;
            Widgets.CheckboxLabeled(new Rect(x, y, leftWidth, 24f), "体型", ref includeBodyType); y += 24f;
            Widgets.CheckboxLabeled(new Rect(x, y, leftWidth, 24f), "发型", ref includeHair);

            float rightY = rect.y + 32f;
            Widgets.CheckboxLabeled(new Rect(rightX, rightY, rightWidth, 24f), "种族/异种型", ref includeXenotype); rightY += 24f;
            Widgets.CheckboxLabeled(new Rect(rightX, rightY, rightWidth, 24f), "服饰", ref includeApparel); rightY += 24f;
            Widgets.CheckboxLabeled(new Rect(rightX, rightY, rightWidth, 24f), "hediff", ref includeHediffs); rightY += 24f;
            Widgets.CheckboxLabeled(new Rect(rightX, rightY, rightWidth, 24f), "当前健康状态", ref includeHealth); rightY += 24f;
            Widgets.CheckboxLabeled(new Rect(rightX, rightY, rightWidth, 24f), "武器", ref includeWeapon); rightY += 24f;
            Widgets.CheckboxLabeled(new Rect(rightX, rightY, rightWidth, 24f), "装备", ref includeEquipment);

            float promptY = rect.y + 182f;
            Widgets.CheckboxLabeled(new Rect(rect.x + 8f, promptY, rect.width - 16f, 24f), "附加 Positive prompt", ref includePositivePrompt);
            if (includePositivePrompt)
            {
                positivePromptText = Widgets.TextArea(new Rect(rect.x + 8f, promptY + 26f, rect.width - 16f, 42f), positivePromptText ?? string.Empty);
            }

            float negativeY = includePositivePrompt ? promptY + 74f : promptY + 26f;
            Widgets.CheckboxLabeled(new Rect(rect.x + 8f, negativeY, rect.width - 16f, 24f), "附加 Negative prompt", ref includeNegativePrompt);
            if (includeNegativePrompt)
            {
                negativePromptText = Widgets.TextArea(new Rect(rect.x + 8f, negativeY + 26f, rect.width - 16f, 56f), negativePromptText ?? string.Empty);
            }

            return rect.height;
        }

        private void DrawContextPreview(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.14f, 0.95f));
            GUI.color = new Color(0.82f, 0.86f, 0.92f);
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, 24f), "RimChat_SelfieContextPreview".Translate());
            GUI.color = Color.white;

            Text.Font = GameFont.Tiny;
            string preview = BuildContextSummary();
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 30f, rect.width - 16f, rect.height - 34f), preview);
            Text.Font = GameFont.Small;
        }

        private string BuildContextSummary()
        {
            if (selectedColonist == null)
            {
                return "未选择正式殖民者。";
            }

            return BuildPromptAppendix(selectedColonist, includeBase64Preview: false);
        }

        private void DrawStatus(Rect rect)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return;
            }

            GUI.color = isGenerating ? new Color(0.9f, 0.85f, 0.4f) : new Color(0.9f, 0.9f, 0.92f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(rect, status);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        private void StartGenerate()
        {
            PersistUiState();
            if (isGenerating)
            {
                return;
            }

            if (ImageGenerationAvailability.IsBlocked())
            {
                status = ImageGenerationAvailability.GetBlockedMessage();
                return;
            }

            if (selectedColonist == null)
            {
                status = "请选择正式殖民者。";
                return;
            }

            RimChatSettings settings = Core.RimChatMod.Settings;
            DiplomacyImageApiConfig imageConfig = settings?.DiplomacyImageApi;
            if (imageConfig == null || !imageConfig.IsConfigured())
            {
                status = "RimChat_SelfieConfigInvalid".Translate();
                return;
            }

            isGenerating = true;
            status = "RimChat_SelfieGenerating".Translate();

            byte[] sourceImageBytes = null;
            TryExportPortraitPngBytes(selectedColonist, out sourceImageBytes);

            var request = new DiplomacyImageGenerationRequest
            {
                Faction = faction,
                Endpoint = imageConfig.Endpoint,
                ApiKey = imageConfig.ApiKey,
                Model = imageConfig.Model,
                Prompt = BuildSelfiePrompt(),
                Caption = string.IsNullOrWhiteSpace(captionText) ? "RimChat_SelfieDefaultCaption".Translate(selectedColonist?.LabelShort ?? "Pawn") : captionText.Trim(),
                Size = DiplomacyImageApiConfig.NormalizeImageSize(sizeText, imageConfig.DefaultSize),
                Watermark = watermark,
                TimeoutSeconds = imageConfig.TimeoutSeconds,
                Mode = imageConfig.Mode,
                SchemaPreset = imageConfig.SchemaPreset,
                AuthMode = imageConfig.AuthMode,
                ApiKeyHeaderName = imageConfig.ApiKeyHeaderName,
                ApiKeyQueryName = imageConfig.ApiKeyQueryName,
                ResponseUrlPath = imageConfig.ResponseUrlPath,
                ResponseB64Path = imageConfig.ResponseB64Path,
                AsyncSubmitPath = imageConfig.AsyncSubmitPath,
                AsyncStatusPathTemplate = imageConfig.AsyncStatusPathTemplate,
                AsyncImageFetchPath = imageConfig.AsyncImageFetchPath,
                ComfyUiImageLoaderNode = imageConfig.ComfyUiImageLoaderNode,
                PollIntervalMs = imageConfig.PollIntervalMs,
                PollMaxAttempts = imageConfig.PollMaxAttempts,
                SourceImageBytes = sourceImageBytes,
                SourceImageMimeType = "image/png",
                PreferImageToImage = sourceImageBytes != null && sourceImageBytes.Length > 0
            };

            DiplomacyImageGenerationService.Instance.GenerateImage(request, OnGenerated);
        }

        private string BuildSelfiePrompt()
        {
            string basePrompt = (promptText ?? string.Empty).Trim();
            string appendix = BuildPromptAppendix(selectedColonist, includeBase64Preview: false);
            if (string.IsNullOrWhiteSpace(appendix))
            {
                return basePrompt;
            }

            return string.IsNullOrWhiteSpace(basePrompt)
                ? appendix
                : basePrompt + "\n\n" + appendix;
        }

        private string BuildPromptAppendix(Pawn pawn, bool includeBase64Preview)
        {
            var lines = new List<string>();
            if (includeAge)
            {
                lines.Add($"年龄={ResolveAgeText(pawn)}");
            }
            if (includeGender)
            {
                lines.Add($"性别={pawn?.gender.ToString() ?? "Unknown"}");
            }
            if (includeFaction)
            {
                lines.Add($"派系={pawn?.Faction?.Name ?? faction?.Name ?? "Unknown"}");
            }
            if (includeRole)
            {
                lines.Add($"身份/职业={pawn?.story?.TitleCap ?? pawn?.kindDef?.label ?? "Colonist"}");
            }
            if (includeBodyType)
            {
                lines.Add($"体型={pawn?.story?.bodyType?.label ?? "unknown"}");
            }
            if (includeHair)
            {
                lines.Add($"发型={pawn?.story?.hairDef?.label ?? "unknown"}");
            }
            if (includeXenotype)
            {
                lines.Add($"种族/异种型={pawn?.genes?.XenotypeLabelCap ?? pawn?.def?.label ?? "unknown"}");
            }
            if (includeApparel)
            {
                lines.Add($"服饰={ResolveApparelText(pawn)}");
            }
            if (includeHediffs)
            {
                lines.Add($"hediff={ResolveHediffText(pawn)}");
            }
            if (includeHealth)
            {
                lines.Add($"当前健康状态={pawn?.health?.summaryHealth?.SummaryHealthPercent.ToStringPercent() ?? "unknown"}");
            }
            if (includeWeapon)
            {
                lines.Add($"武器={pawn?.equipment?.Primary?.LabelCap ?? "none"}");
            }
            if (includeEquipment)
            {
                lines.Add($"装备={ResolveApparelText(pawn)}");
            }
            if (includePositivePrompt && !string.IsNullOrWhiteSpace(positivePromptText))
            {
                lines.Add($"Positive prompt: {positivePromptText.Trim()}");
            }
            if (includeNegativePrompt && !string.IsNullOrWhiteSpace(negativePromptText))
            {
                lines.Add($"Negative prompt: {negativePromptText.Trim()}");
            }

            if (lines.Count == 0)
            {
                return string.Empty;
            }

            return "以下内容为参考补充信息，供自拍图生成时保持人物一致性：\n- " + string.Join("\n- ", lines);
        }

        private void ApplyPersistedState(RimChatSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            includeAge = settings.SelfieIncludeAge;
            includeGender = settings.SelfieIncludeGender;
            includeFaction = settings.SelfieIncludeFaction;
            includeRole = settings.SelfieIncludeRole;
            includeBodyType = settings.SelfieIncludeBodyType;
            includeHair = settings.SelfieIncludeHair;
            includeXenotype = settings.SelfieIncludeXenotype;
            includeApparel = settings.SelfieIncludeApparel;
            includeHediffs = settings.SelfieIncludeHediffs;
            includeHealth = settings.SelfieIncludeHealth;
            includeWeapon = settings.SelfieIncludeWeapon;
            includeEquipment = settings.SelfieIncludeEquipment;
            includePositivePrompt = settings.SelfieIncludePositivePrompt;
            includeNegativePrompt = settings.SelfieIncludeNegativePrompt;

            promptText = string.IsNullOrWhiteSpace(settings.SelfiePromptText) ? DefaultPromptText : settings.SelfiePromptText;
            captionText = settings.SelfieCaptionText ?? string.Empty;
            sizeText = string.IsNullOrWhiteSpace(settings.SelfieSizeText) ? sizeText : settings.SelfieSizeText;
            watermark = settings.SelfieWatermark;
            positivePromptText = string.IsNullOrWhiteSpace(settings.SelfiePositivePromptText) ? PositivePromptDefault : settings.SelfiePositivePromptText;
            negativePromptText = string.IsNullOrWhiteSpace(settings.SelfieNegativePromptText) ? NegativePromptDefault : settings.SelfieNegativePromptText;

            if (!string.IsNullOrWhiteSpace(settings.SelfieSelectedColonistThingId))
            {
                Pawn persisted = selectableColonists.FirstOrDefault(pawn => pawn != null && pawn.ThingID == settings.SelfieSelectedColonistThingId);
                if (persisted != null)
                {
                    selectedColonist = persisted;
                }
            }
        }

        private void PersistUiState()
        {
            RimChatSettings settings = Core.RimChatMod.Settings;
            if (settings == null)
            {
                return;
            }

            settings.SelfieSelectedColonistThingId = selectedColonist?.ThingID ?? string.Empty;
            settings.SelfiePromptText = string.IsNullOrWhiteSpace(promptText) ? DefaultPromptText : promptText;
            settings.SelfieCaptionText = captionText ?? string.Empty;
            settings.SelfieSizeText = string.IsNullOrWhiteSpace(sizeText) ? DiplomacyImageApiConfig.DefaultImageSize : sizeText;
            settings.SelfieWatermark = watermark;
            settings.SelfieIncludeAge = includeAge;
            settings.SelfieIncludeGender = includeGender;
            settings.SelfieIncludeFaction = includeFaction;
            settings.SelfieIncludeRole = includeRole;
            settings.SelfieIncludeBodyType = includeBodyType;
            settings.SelfieIncludeHair = includeHair;
            settings.SelfieIncludeXenotype = includeXenotype;
            settings.SelfieIncludeApparel = includeApparel;
            settings.SelfieIncludeHediffs = includeHediffs;
            settings.SelfieIncludeHealth = includeHealth;
            settings.SelfieIncludeWeapon = includeWeapon;
            settings.SelfieIncludeEquipment = includeEquipment;
            settings.SelfieIncludePositivePrompt = includePositivePrompt;
            settings.SelfieIncludeNegativePrompt = includeNegativePrompt;
            settings.SelfiePositivePromptText = string.IsNullOrWhiteSpace(positivePromptText) ? PositivePromptDefault : positivePromptText;
            settings.SelfieNegativePromptText = string.IsNullOrWhiteSpace(negativePromptText) ? NegativePromptDefault : negativePromptText;
            Core.RimChatMod.Instance?.WriteSettings();
        }

        private static string ResolveAgeText(Pawn pawn)
        {
            return pawn?.ageTracker == null ? "unknown" : Math.Floor(pawn.ageTracker.AgeBiologicalYearsFloat).ToString();
        }

        private static string ResolveApparelText(Pawn pawn)
        {
            string apparel = pawn?.apparel?.WornApparel == null
                ? "none"
                : string.Join("、", pawn.apparel.WornApparel.Where(item => item != null).Select(item => item.LabelCap).Take(8));
            return string.IsNullOrWhiteSpace(apparel) ? "none" : apparel;
        }

        private static string ResolveHediffText(Pawn pawn)
        {
            string hediffs = pawn?.health?.hediffSet?.hediffs == null
                ? "none"
                : string.Join("、", pawn.health.hediffSet.hediffs.Where(h => h != null).Select(h => h.LabelCap).Distinct().Take(10));
            return string.IsNullOrWhiteSpace(hediffs) ? "none" : hediffs;
        }

        private void OnGenerated(DiplomacyImageGenerationResult result)
        {
            isGenerating = false;
            if (result == null || !result.Success)
            {
                status = "RimChat_SelfieGenerateFailed".Translate(result?.Error ?? "unknown");
                return;
            }

            status = "RimChat_SelfieGenerateSuccess".Translate();
            AlbumImageEntry metadata = new AlbumImageEntry
            {
                SavedTick = Find.TickManager?.TicksGame ?? 0,
                SourcePath = result.LocalPath,
                Caption = result.Caption ?? string.Empty,
                FactionId = faction?.Name ?? string.Empty,
                NegotiatorId = selectedColonist?.ThingID ?? negotiator?.ThingID ?? string.Empty,
                Size = sizeText ?? string.Empty,
                SourceType = AlbumImageEntry.SourceSelfie
            };

            Find.WindowStack.Add(new Dialog_DiplomacySelfiePreview(result.LocalPath, result.Caption, metadata));
            Close();
        }

        private static List<Pawn> ResolveSelectableColonists()
        {
            return Find.Maps
                .Where(map => map?.IsPlayerHome == true && map.mapPawns?.FreeColonistsSpawned != null)
                .SelectMany(map => map.mapPawns.FreeColonistsSpawned)
                .Where(pawn => pawn != null && pawn.IsColonistPlayerControlled)
                .GroupBy(pawn => pawn.thingIDNumber)
                .Select(group => group.First())
                .OrderBy(pawn => pawn.LabelShortCap)
                .ToList();
        }

        private static Pawn ResolveInitialColonist(List<Pawn> colonists, Pawn negotiator)
        {
            if (negotiator != null && colonists.Contains(negotiator))
            {
                return negotiator;
            }

            return colonists.FirstOrDefault();
        }

        private static bool TryExportPortraitPngBytes(Pawn pawn, out byte[] pngBytes)
        {
            pngBytes = null;
            if (pawn == null)
            {
                return false;
            }

            Texture portrait;
            try
            {
                portrait = PortraitsCache.Get(
                    pawn,
                    new Vector2(PortraitSize, PortraitSize),
                    Rot4.South,
                    Vector3.zero,
                    1f);
            }
            catch
            {
                return false;
            }

            if (portrait is Texture2D texture2D)
            {
                try
                {
                    pngBytes = texture2D.EncodeToPNG();
                    return pngBytes != null && pngBytes.Length > 0;
                }
                catch
                {
                    return false;
                }
            }

            if (!(portrait is RenderTexture renderTexture))
            {
                return false;
            }

            RenderTexture previous = RenderTexture.active;
            Texture2D readable = null;
            try
            {
                RenderTexture.active = renderTexture;
                readable = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
                readable.Apply();
                pngBytes = readable.EncodeToPNG();
                return pngBytes != null && pngBytes.Length > 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                if (readable != null)
                {
                    UnityEngine.Object.Destroy(readable);
                }
            }
        }
    }
}
