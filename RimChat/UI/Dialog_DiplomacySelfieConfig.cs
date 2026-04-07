using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        private bool includeBasicProfile = true;
        private bool includeAppearanceProfile = true;
        private bool includeStatusProfile = true;
        private bool includeEquipmentProfile = true;

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

            string colonistName = selectedColonist?.LabelShort ?? negotiator?.LabelShort ?? "Pawn";
            captionText = "RimChat_SelfieDefaultCaption".Translate(colonistName);
            promptText = "帮我生成图片：将图片的动漫风格角色转为低饱和二次元摄影cos风格，做自拍动作，采用现实对脸自拍构图，保留核心服饰元素，背景设为派系典型场景，光线偏暖调以增强氛围感，全身照。比例 4:3。";
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
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, 860f);
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

            y += DrawInjectionSwitches(new Rect(0f, y, viewRect.width, 116f));
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

            float leftX = rect.x + 8f;
            float rightX = rect.x + rect.width * 0.5f;
            float rowY = rect.y + 32f;
            float leftWidth = rect.width * 0.5f - 12f;
            float rightWidth = rect.width * 0.5f - 12f;

            Widgets.CheckboxLabeled(new Rect(leftX, rowY, leftWidth, 24f), "基础参数：年龄/性别/派系/身份", ref includeBasicProfile);
            rowY += 24f;
            Widgets.CheckboxLabeled(new Rect(leftX, rowY, leftWidth, 24f), "外观参数：体型/发型/种族/服饰", ref includeAppearanceProfile);
            rowY += 24f;
            Widgets.CheckboxLabeled(new Rect(rightX, rect.y + 32f, rightWidth, 24f), "状态参数：hediff/伤痕/义体/健康", ref includeStatusProfile);
            Widgets.CheckboxLabeled(new Rect(rightX, rect.y + 56f, rightWidth, 24f), "装备参数：武器/装备", ref includeEquipmentProfile);
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
            var sb = new StringBuilder();
            sb.AppendLine((promptText ?? string.Empty).Trim());
            sb.AppendLine();
            sb.AppendLine(BuildPromptAppendix(selectedColonist, includeBase64Preview: false));
            return sb.ToString().Trim();
        }

        private string BuildPromptAppendix(Pawn pawn, bool includeBase64Preview)
        {
            var sb = new StringBuilder();
            sb.AppendLine("以下内容为参考补充信息，供自拍图生成时保持人物一致性：");

            if (includeBasicProfile)
            {
                sb.AppendLine(BuildBasicProfileLine(pawn));
            }

            if (includeAppearanceProfile)
            {
                sb.AppendLine(BuildAppearanceProfileLine(pawn));
            }

            if (includeStatusProfile)
            {
                sb.AppendLine(BuildStatusProfileLine(pawn));
            }

            if (includeEquipmentProfile)
            {
                sb.AppendLine(BuildEquipmentProfileLine(pawn));
            }

            return sb.ToString().Trim();
        }

        private string BuildBasicProfileLine(Pawn pawn)
        {
            string age = pawn?.ageTracker == null ? "unknown" : Math.Floor(pawn.ageTracker.AgeBiologicalYearsFloat).ToString();
            string gender = pawn?.gender.ToString() ?? "Unknown";
            string factionName = pawn?.Faction?.Name ?? faction?.Name ?? "Unknown";
            string role = pawn?.story?.TitleCap ?? pawn?.kindDef?.label ?? "Colonist";
            return $"基础参数：姓名={pawn?.LabelShortCap ?? "Unknown"}；年龄={age}；性别={gender}；派系={factionName}；身份/职业={role}。";
        }

        private string BuildAppearanceProfileLine(Pawn pawn)
        {
            string bodyType = pawn?.story?.bodyType?.label ?? "unknown";
            string hair = pawn?.story?.hairDef?.label ?? "unknown";
            string xenotype = pawn?.genes?.XenotypeLabelCap ?? pawn?.def?.label ?? "unknown";
            string apparel = pawn?.apparel?.WornApparel == null
                ? "none"
                : string.Join("、", pawn.apparel.WornApparel.Where(item => item != null).Select(item => item.LabelCap).Take(8));
            if (string.IsNullOrWhiteSpace(apparel))
            {
                apparel = "none";
            }

            return $"外观参数：体型={bodyType}；发型={hair}；种族/异种型={xenotype}；服饰={apparel}。";
        }

        private string BuildStatusProfileLine(Pawn pawn)
        {
            string hediffs = pawn?.health?.hediffSet?.hediffs == null
                ? "none"
                : string.Join("、", pawn.health.hediffSet.hediffs.Where(h => h != null).Select(h => h.LabelCap).Distinct().Take(10));
            if (string.IsNullOrWhiteSpace(hediffs))
            {
                hediffs = "none";
            }

            string health = pawn?.health?.summaryHealth?.SummaryHealthPercent.ToStringPercent() ?? "unknown";
            return $"状态参数：hediff/伤痕/义体={hediffs}；当前健康状态={health}。";
        }

        private string BuildEquipmentProfileLine(Pawn pawn)
        {
            string weapon = pawn?.equipment?.Primary?.LabelCap ?? "none";
            string equipment = pawn?.apparel?.WornApparel == null
                ? "none"
                : string.Join("、", pawn.apparel.WornApparel.Where(item => item != null).Select(item => item.LabelCap).Take(8));
            if (string.IsNullOrWhiteSpace(equipment))
            {
                equipment = "none";
            }

            return $"装备参数：武器={weapon}；装备={equipment}。";
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
