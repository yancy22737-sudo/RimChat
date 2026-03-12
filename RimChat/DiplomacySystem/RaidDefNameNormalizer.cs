using System;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: none.
    /// Responsibility: normalize raid strategy/arrival aliases into canonical DefNames.
    /// </summary>
    internal static class RaidDefNameNormalizer
    {
        public static void NormalizeRaidRequestParameters(
            string strategyInput,
            string arrivalInput,
            out string strategyDefName,
            out string arrivalModeDefName)
        {
            string normalizedStrategy = NormalizeRaidStrategyDefName(strategyInput, out string impliedArrival);
            string normalizedArrival = NormalizeRaidArrivalModeDefName(arrivalInput);

            if (string.IsNullOrWhiteSpace(normalizedArrival) && !string.IsNullOrWhiteSpace(impliedArrival))
            {
                normalizedArrival = impliedArrival;
            }

            strategyDefName = normalizedStrategy;
            arrivalModeDefName = normalizedArrival;
        }

        public static string NormalizeRaidStrategyDefName(string value, out string impliedArrivalModeDefName)
        {
            impliedArrivalModeDefName = string.Empty;
            string trimmed = NormalizeInput(value);
            if (string.IsNullOrEmpty(trimmed))
            {
                return string.Empty;
            }

            switch (Canonicalize(trimmed))
            {
                case "immediate":
                case "immediateattack":
                case "default":
                    return "ImmediateAttack";
                case "immediateattacksmart":
                case "smart":
                case "smartattack":
                    return "ImmediateAttackSmart";
                case "stagethenattack":
                case "stageattack":
                case "stagingattack":
                    return "StageThenAttack";
                case "immediateattacksappers":
                case "sapper":
                case "sappers":
                    return "ImmediateAttackSappers";
                case "siege":
                    return "Siege";
                case "immediatedroppod":
                case "immediatedrop":
                case "droppod":
                case "droppodattack":
                    impliedArrivalModeDefName = "CenterDrop";
                    return "ImmediateAttack";
                default:
                    return trimmed;
            }
        }

        public static string NormalizeRaidArrivalModeDefName(string value)
        {
            string trimmed = NormalizeInput(value);
            if (string.IsNullOrEmpty(trimmed))
            {
                return string.Empty;
            }

            switch (Canonicalize(trimmed))
            {
                case "edgewalkin":
                case "walkin":
                    return "EdgeWalkIn";
                case "edgedrop":
                case "edgedroppod":
                    return "EdgeDrop";
                case "edgewalkingroups":
                case "walkingroups":
                    return "EdgeWalkInGroups";
                case "randomdrop":
                case "randomdroppod":
                    return "RandomDrop";
                case "centerdrop":
                case "centerdroppod":
                case "immediatedroppod":
                case "immediatedrop":
                    return "CenterDrop";
                default:
                    return trimmed;
            }
        }

        private static string NormalizeInput(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string Canonicalize(string value)
        {
            return value
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .Replace(" ", string.Empty)
                .ToLowerInvariant();
        }
    }
}
