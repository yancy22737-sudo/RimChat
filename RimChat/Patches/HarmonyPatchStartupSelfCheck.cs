using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimChat.Patches
{
    /// <summary>
    /// Dependencies: Harmony patch metadata and Verse.Translator signature.
    /// Responsibility: emit a minimal startup self-check log for critical startup patches.
    /// </summary>
    internal static class HarmonyPatchStartupSelfCheck
    {
        internal static void Run()
        {
            var failures = new List<string>();
            ValidateTranslatorFallbackPatch(failures);

            if (failures.Count == 0)
            {
                Log.Message("[RimChat][HarmonySelfCheck] Startup patch checks passed: 1/1.");
                return;
            }

            Log.Warning($"[RimChat][HarmonySelfCheck] Startup patch checks failed: {failures.Count} issue(s).");
            for (int i = 0; i < failures.Count; i++)
            {
                Log.Warning($"[RimChat][HarmonySelfCheck] {failures[i]}");
            }
        }

        private static void ValidateTranslatorFallbackPatch(List<string> failures)
        {
            MethodInfo postfix = AccessTools.Method(
                typeof(TranslatorPatch_RimChatEnglishFallback),
                "Postfix");
            if (postfix == null)
            {
                failures.Add("Translator fallback postfix method was not found.");
                return;
            }

            if (!HasRequiredParameter(postfix, "__0") ||
                !HasRequiredParameter(postfix, "__1") ||
                !HasRequiredParameter(postfix, "__result"))
            {
                failures.Add("Translator fallback postfix must use positional parameters (__0/__1) and __result.");
            }

            MethodInfo target = AccessTools.Method(
                typeof(Translator),
                nameof(Translator.TryTranslate),
                new[] { typeof(string), typeof(TaggedString).MakeByRefType() });
            if (target == null)
            {
                failures.Add("Translator.TryTranslate(string, ref TaggedString) target signature was not found.");
            }
        }

        private static bool HasRequiredParameter(MethodInfo method, string name)
        {
            ParameterInfo[] parameters = method.GetParameters();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (string.Equals(parameters[i].Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
