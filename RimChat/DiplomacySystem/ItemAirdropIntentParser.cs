using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace RimChat.DiplomacySystem
{
    /// <summary>
    /// Dependencies: none.
    /// Responsibility: tokenize need text and infer airdrop need family.
    /// </summary>
    internal static class ItemAirdropIntentParser
    {
        private static readonly string[] MedicineKeywords =
        {
            "medicine", "medical", "med", "drug", "医疗", "医药", "药", "药品", "药物", "草药", "绷带", "医疗包"
        };

        private static readonly string[] WeaponKeywords =
        {
            "weapon", "gun", "ammo", "rifle", "melee", "武器", "枪", "弹药", "步枪", "手枪", "霰弹", "子弹"
        };

        private static readonly string[] ApparelKeywords =
        {
            "apparel", "armor", "cloth", "wear", "jacket", "hat", "护甲", "衣服", "服装", "外套", "头盔", "防具"
        };

        private static readonly string[] FoodKeywords =
        {
            "food", "meal", "nutrition", "eat", "ration", "食物", "食材", "食品", "口粮", "干粮", "肉饼", "生存餐", "营养膏"
        };

        private static readonly string[] ResourceKeywords =
        {
            "resource", "resources", "material", "materials", "chemfuel", "fuel", "steel", "component", "components", "plasteel",
            "uranium", "neutroamine", "cloth", "textile", "leather", "wood", "lumber", "stone", "blocks",
            "资源", "材料", "化合燃料", "燃料", "钢铁", "钢材", "零部件", "组件", "塑钢", "铀", "中性胺", "布料", "纺织", "皮革", "木材", "木头", "石块"
        };

        private static readonly HashSet<string> StopTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "please", "need", "want", "some", "for", "to", "of", "and",
            "give", "send", "drop", "supply",
            "给", "需要", "想要", "一些", "用于", "的", "和", "我", "你", "空投", "请求"
        };

        private static readonly HashSet<string> NoiseUnitTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "个", "份", "组", "包", "箱", "件", "把", "瓶", "支", "只", "块", "斤", "千克", "克",
            "kg", "g", "x"
        };

        private static readonly HashSet<string> ShortTokenWhitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ag", "au", "cu", "fe", "pb", "sn"
        };

        public static List<string> Tokenize(string text, bool includeNoise = false)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string>();
            }

            string normalized = NormalizeDelimiters(text);
            string[] raw = normalized.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            var tokens = new List<string>();
            for (int i = 0; i < raw.Length; i++)
            {
                List<string> parts = SplitMixedToken(raw[i]);
                for (int j = 0; j < parts.Count; j++)
                {
                    string token = parts[j].Trim().ToLowerInvariant();
                    if (!includeNoise && IsNoiseToken(token))
                    {
                        continue;
                    }

                    tokens.Add(token);
                }
            }

            var result = tokens.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Log.Message($"[RimChat][Tokenize] input=\"{text}\" -> tokens=[{string.Join(",", result)}]");
            return result;
        }

        public static ItemAirdropNeedFamily ResolveFamily(List<string> tokens)
        {
            if (tokens == null || tokens.Count == 0)
            {
                return ItemAirdropNeedFamily.Unknown;
            }

            if (ContainsAny(tokens, MedicineKeywords))
            {
                return ItemAirdropNeedFamily.Medicine;
            }

            if (ContainsAny(tokens, WeaponKeywords))
            {
                return ItemAirdropNeedFamily.Weapon;
            }

            if (ContainsAny(tokens, ApparelKeywords))
            {
                return ItemAirdropNeedFamily.Apparel;
            }

            if (ContainsAny(tokens, ResourceKeywords))
            {
                return ItemAirdropNeedFamily.Resource;
            }

            if (ContainsAny(tokens, FoodKeywords))
            {
                return ItemAirdropNeedFamily.Food;
            }

            return ItemAirdropNeedFamily.Unknown;
        }

        private static bool ContainsAny(List<string> tokens, params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i];
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                for (int j = 0; j < tokens.Count; j++)
                {
                    string token = tokens[j];
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        continue;
                    }

                    if (token.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string NormalizeDelimiters(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (IsBoundaryDelimiter(ch))
                {
                    sb.Append(' ');
                    continue;
                }

                sb.Append(ch);
            }

            return sb.ToString();
        }

        private static List<string> SplitMixedToken(string token)
        {
            var parts = new List<string>();
            if (string.IsNullOrWhiteSpace(token))
            {
                return parts;
            }

            var sb = new StringBuilder(token.Length);
            CharBucket prev = CharBucket.Other;
            for (int i = 0; i < token.Length; i++)
            {
                char ch = token[i];
                CharBucket current = GetCharBucket(ch);
                if (sb.Length > 0 && (ShouldSplit(prev, current) || IsUpperCharBucketTransition(token, i)))
                {
                    parts.Add(sb.ToString());
                    sb.Clear();
                }

                sb.Append(ch);
                prev = current;
            }

            if (sb.Length > 0)
            {
                parts.Add(sb.ToString());
            }

            return parts;
        }

        private static bool IsNoiseToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token) || StopTokens.Contains(token))
            {
                return true;
            }

            if (NoiseUnitTokens.Contains(token))
            {
                return true;
            }

            bool allDigits = token.All(char.IsDigit);
            if (allDigits)
            {
                return true;
            }

            bool isCjk = IsCjkToken(token);
            if (!isCjk && token.Length < 2 && !ShortTokenWhitelist.Contains(token))
            {
                return true;
            }

            return false;
        }

        private static bool IsCjkToken(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            foreach (char ch in token)
            {
                if ((ch >= 0x4E00 && ch <= 0x9FFF) ||
                    (ch >= 0x3400 && ch <= 0x4DBF) ||
                    (ch >= 0xF900 && ch <= 0xFAFF) ||
                    (ch >= 0x2E80 && ch <= 0x2EFF))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsBoundaryDelimiter(char ch)
        {
            return ch == ',' || ch == ';' || ch == ':' || ch == '.' ||
                   ch == '|' || ch == '/' || ch == '\\' ||
                   ch == '、' || ch == '，' || ch == '。' || ch == '；' || ch == '：';
        }

        private static bool ShouldSplit(CharBucket prev, CharBucket current)
        {
            if (prev == CharBucket.Other || current == CharBucket.Other)
            {
                return false;
            }

            if (prev != current)
            {
                return true;
            }

            return false;
        }

        private static bool IsUpperCharBucketTransition(string token, int i)
        {
            if (i <= 0 || i >= token.Length)
            {
                return false;
            }

            char prev = token[i - 1];
            char curr = token[i];
            return char.IsLower(prev) && char.IsUpper(curr);
        }

        private static CharBucket GetCharBucket(char ch)
        {
            if (char.IsDigit(ch))
            {
                return CharBucket.Digit;
            }

            if (IsCjk(ch))
            {
                return CharBucket.Cjk;
            }

            if (char.IsLetter(ch))
            {
                return CharBucket.Letter;
            }

            return CharBucket.Other;
        }

        private static bool IsCjk(char ch)
        {
            return ch >= 0x4E00 && ch <= 0x9FFF;
        }

        private enum CharBucket
        {
            Other = 0,
            Letter = 1,
            Digit = 2,
            Cjk = 3
        }
    }
}
