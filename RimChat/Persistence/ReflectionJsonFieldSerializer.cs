using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: reflection metadata and StringBuilder.
    /// Responsibility: serialize public-field object graphs into stable JSON for prompt persistence models that Unity JsonUtility truncates.
    /// </summary>
    internal static class ReflectionJsonFieldSerializer
    {
        public static string Serialize(object value, bool prettyPrint = true)
        {
            var sb = new StringBuilder(4096);
            WriteValue(sb, value, prettyPrint, depth: 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value, bool prettyPrint, int depth)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            Type type = value.GetType();
            if (type == typeof(string) || type == typeof(char))
            {
                WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
                return;
            }

            if (type == typeof(bool))
            {
                sb.Append((bool)value ? "true" : "false");
                return;
            }

            if (type.IsEnum)
            {
                sb.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                return;
            }

            if (IsNumericType(type))
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }

            if (value is IEnumerable enumerable && !(value is string))
            {
                WriteArray(sb, enumerable, prettyPrint, depth);
                return;
            }

            WriteObject(sb, value, prettyPrint, depth);
        }

        private static void WriteArray(StringBuilder sb, IEnumerable values, bool prettyPrint, int depth)
        {
            List<object> items = values.Cast<object>().ToList();
            sb.Append('[');
            if (items.Count == 0)
            {
                sb.Append(']');
                return;
            }

            if (prettyPrint)
            {
                sb.AppendLine();
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (prettyPrint)
                {
                    AppendIndent(sb, depth + 1);
                }

                WriteValue(sb, items[i], prettyPrint, depth + 1);
                if (i < items.Count - 1)
                {
                    sb.Append(',');
                }

                if (prettyPrint)
                {
                    sb.AppendLine();
                }
            }

            if (prettyPrint)
            {
                AppendIndent(sb, depth);
            }

            sb.Append(']');
        }

        private static void WriteObject(StringBuilder sb, object value, bool prettyPrint, int depth)
        {
            FieldInfo[] fields = value.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(field => !field.IsStatic)
                .OrderBy(field => field.MetadataToken)
                .ToArray();

            sb.Append('{');
            if (fields.Length == 0)
            {
                sb.Append('}');
                return;
            }

            if (prettyPrint)
            {
                sb.AppendLine();
            }

            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (prettyPrint)
                {
                    AppendIndent(sb, depth + 1);
                }

                WriteString(sb, field.Name);
                sb.Append(prettyPrint ? ": " : ":");
                WriteValue(sb, field.GetValue(value), prettyPrint, depth + 1);
                if (i < fields.Length - 1)
                {
                    sb.Append(',');
                }

                if (prettyPrint)
                {
                    sb.AppendLine();
                }
            }

            if (prettyPrint)
            {
                AppendIndent(sb, depth);
            }

            sb.Append('}');
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            string text = value ?? string.Empty;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    default:
                        if (char.IsControl(c))
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
        }

        private static void AppendIndent(StringBuilder sb, int depth)
        {
            sb.Append(' ', depth * 4);
        }

        private static bool IsNumericType(Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return true;
                default:
                    return false;
            }
        }
    }
}
