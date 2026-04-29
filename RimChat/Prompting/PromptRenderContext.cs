using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Scriban.Runtime;
using RimWorld;
using Verse;

namespace RimChat.Prompting
{
    /// <summary>
    /// Dependencies: Scriban ScriptObject, RimWorld Pawn/Faction types.
    /// Responsibility: hold strict namespace-scoped variables for Scriban rendering.
    /// </summary>
    internal sealed class PromptRenderContext
    {
        private static readonly HashSet<string> AllowedNamespaces =
            new HashSet<string>(new[] { "ctx", "pawn", "world", "dialogue", "system" }, StringComparer.OrdinalIgnoreCase);

        public string TemplateId { get; }
        public string Channel { get; }
        public ScriptObject Root { get; }

        private PromptRenderContext(string templateId, string channel)
        {
            TemplateId = templateId ?? string.Empty;
            Channel = channel ?? string.Empty;
            Root = BuildRootObject();
        }

        public static PromptRenderContext Create(string templateId, string channel)
        {
            return new PromptRenderContext(templateId, channel);
        }

        public PromptRenderContext SetValue(string path, object value)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return this;
            }

            string[] segments = NormalizePath(path);
            ScriptObject parent = ResolveParent(segments);
            parent[segments[segments.Length - 1]] = PromptRenderProjection.Project(value);
            return this;
        }

        public PromptRenderContext SetValues(IReadOnlyDictionary<string, object> values)
        {
            if (values == null)
            {
                return this;
            }

            foreach (KeyValuePair<string, object> pair in values)
            {
                SetValue(pair.Key, pair.Value);
            }

            return this;
        }

        private static ScriptObject BuildRootObject()
        {
            var root = new ScriptObject();
            root["ctx"] = new ScriptObject();
            root["pawn"] = new ScriptObject();
            root["world"] = new ScriptObject();
            root["dialogue"] = new ScriptObject();
            root["system"] = new ScriptObject();
            return root;
        }

        private static string[] NormalizePath(string path)
        {
            string[] segments = path
                .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => segment.Trim())
                .Where(segment => segment.Length > 0)
                .ToArray();
            if (segments.Length < 2 || !AllowedNamespaces.Contains(segments[0]))
            {
                throw new ArgumentException("Variables must use ctx/pawn/world/dialogue/system namespaces.", nameof(path));
            }

            return segments;
        }

        private ScriptObject ResolveParent(string[] segments)
        {
            ScriptObject current = Root;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                string segment = segments[i];
                if (!(GetChildCaseInsensitive(current, segment) is ScriptObject next))
                {
                    next = new ScriptObject();
                    current[segment] = next;
                }

                current = next;
            }

            return current;
        }

        private static object GetChildCaseInsensitive(ScriptObject parent, string key)
        {
            object exact = parent[key];
            if (exact != null)
            {
                return exact;
            }

            foreach (var kvp in parent)
            {
                if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Dependencies: reflection and Verse runtime objects.
    /// Responsibility: project runtime objects into Scriban-safe structures.
    /// </summary>
    internal static class PromptRenderProjection
    {
        private const int MaxDepth = 2;
        private const int MaxProjectedMembers = 40;

        public static object Project(object value)
        {
            return ProjectCore(value, 0);
        }

        private static object ProjectCore(object value, int depth)
        {
            if (value == null)
            {
                return null;
            }

            Type type = value.GetType();
            if (IsScalar(type))
            {
                return ConvertScalar(value);
            }

            if (value is Pawn pawn)
            {
                return ProjectPawn(pawn, depth);
            }

            if (value is Faction faction)
            {
                return ProjectFaction(faction);
            }

            if (value is IEnumerable<string> stringSequence)
            {
                return string.Join(", ", stringSequence.Where(item => !string.IsNullOrWhiteSpace(item)));
            }

            if (value is IDictionary dictionary)
            {
                return ProjectDictionary(dictionary, depth);
            }

            if (depth >= MaxDepth)
            {
                return value.ToString();
            }

            return ProjectObject(value, depth);
        }

        private static object ConvertScalar(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            Type type = value.GetType();
            if (type.IsPrimitive || type == typeof(decimal))
            {
                return value;
            }

            if (value is Enum)
            {
                return value.ToString();
            }

            if (value is DateTime dateTime)
            {
                return dateTime.ToString("O", CultureInfo.InvariantCulture);
            }

            if (value is IFormattable formattable)
            {
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            }

            return value;
        }

        private static bool IsScalar(Type type)
        {
            return type.IsPrimitive ||
                type.IsEnum ||
                type == typeof(string) ||
                type == typeof(decimal) ||
                type == typeof(DateTime) ||
                type == typeof(Guid);
        }

        private static ScriptObject ProjectDictionary(IDictionary dictionary, int depth)
        {
            var result = new ScriptObject();
            foreach (DictionaryEntry entry in dictionary)
            {
                string key = entry.Key?.ToString();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                result[ToSnakeCase(key)] = ProjectCore(entry.Value, depth + 1);
            }

            return result;
        }

        private static ScriptObject ProjectFaction(Faction faction)
        {
            var result = new ScriptObject();
            if (faction == null)
            {
                return result;
            }

            result["name"] = TryReadMember(() => faction.Name, out object nameRaw)
                ? nameRaw?.ToString() ?? string.Empty
                : string.Empty;
            result["def_name"] = TryReadMember(() => faction.def?.defName, out object defNameRaw)
                ? defNameRaw?.ToString() ?? string.Empty
                : string.Empty;
            bool isPlayer = TryReadMember(() => faction.IsPlayer, out object isPlayerRaw) &&
                isPlayerRaw is bool isPlayerValue &&
                isPlayerValue;
            result["is_player"] = isPlayer;
            // Accessing PlayerGoodwill for player faction triggers noisy self-relation error logs in RimWorld.
            if (isPlayer || ReferenceEquals(faction, Faction.OfPlayer))
            {
                result["goodwill"] = 0;
            }
            else
            {
                result["goodwill"] = TryReadMember(() => faction.PlayerGoodwill, out object goodwillRaw) &&
                    goodwillRaw is int goodwill
                    ? goodwill
                    : 0;
            }
            result["is_defeated"] = TryReadMember(() => faction.defeated, out object defeatedRaw) && defeatedRaw is bool defeated && defeated;
            return result;
        }

        private static ScriptObject ProjectPawn(Pawn pawn, int depth)
        {
            if (pawn == null)
            {
                return null;
            }

            var result = new ScriptObject
            {
                ["name"] = pawn.LabelShort ?? string.Empty,
                ["label"] = pawn.LabelShortCap ?? string.Empty,
                ["kind"] = pawn.KindLabel ?? string.Empty,
                ["gender"] = pawn.gender.ToString(),
                ["faction"] = ProjectFaction(pawn.Faction)
            };
            AppendProjectedMembers(result, pawn, depth + 1);
            return result;
        }

        private static ScriptObject ProjectObject(object value, int depth)
        {
            var result = new ScriptObject();
            if (value == null)
            {
                return result;
            }

            AppendProjectedMembers(result, value, depth + 1);
            return result;
        }

        private static void AppendProjectedMembers(ScriptObject target, object source, int depth)
        {
            int count = 0;
            foreach (PropertyInfo property in source.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                if (!TryReadMember(() => property.GetValue(source, null), out object value))
                {
                    continue;
                }

                target[ToSnakeCase(property.Name)] = ProjectCore(value, depth);
                count++;
                if (count >= MaxProjectedMembers)
                {
                    return;
                }
            }
        }

        private static bool TryReadMember(Func<object> getter, out object value)
        {
            try
            {
                value = getter();
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        private static string ToSnakeCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsUpper(c) && i > 0 && char.IsLetterOrDigit(value[i - 1]))
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }

            return sb.ToString();
        }
    }
}
