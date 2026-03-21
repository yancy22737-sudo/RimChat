using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace RimChat.Persistence
{
    /// <summary>
    /// Dependencies: reflection metadata and in-file JSON token parser.
    /// Responsibility: deserialize public-field object graphs from stable JSON emitted by ReflectionJsonFieldSerializer.
    /// </summary>
    internal static class ReflectionJsonFieldDeserializer
    {
        public static bool TryDeserialize<T>(string json, out T value) where T : class
        {
            value = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            if (!TryParseRoot(json, out object root))
            {
                return false;
            }

            object converted = ConvertValue(root, typeof(T));
            value = converted as T;
            return value != null;
        }

        private static bool TryParseRoot(string json, out object root)
        {
            try
            {
                var parser = new JsonParser(json);
                root = parser.Parse();
                return true;
            }
            catch
            {
                root = null;
                return false;
            }
        }

        private static object ConvertValue(object raw, Type targetType)
        {
            if (targetType == null)
            {
                return null;
            }

            Type nullableType = Nullable.GetUnderlyingType(targetType);
            if (nullableType != null)
            {
                return ConvertNullable(raw, nullableType);
            }

            if (raw == null)
            {
                return CreateDefaultValue(targetType);
            }

            if (targetType == typeof(string))
            {
                return Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
            }

            if (targetType == typeof(bool))
            {
                return ConvertBoolean(raw);
            }

            if (targetType.IsEnum)
            {
                return ConvertEnum(raw, targetType);
            }

            if (IsNumericType(targetType))
            {
                return ConvertNumber(raw, targetType);
            }

            if (TryConvertList(raw, targetType, out object listValue))
            {
                return listValue;
            }

            if (raw is Dictionary<string, object> dict)
            {
                return ConvertObject(dict, targetType);
            }

            return targetType.IsAssignableFrom(raw.GetType()) ? raw : CreateDefaultValue(targetType);
        }

        private static object ConvertNullable(object raw, Type nullableType)
        {
            if (raw == null)
            {
                return null;
            }

            return ConvertValue(raw, nullableType);
        }

        private static bool ConvertBoolean(object raw)
        {
            switch (raw)
            {
                case bool boolValue:
                    return boolValue;
                case string text:
                    return string.Equals(text.Trim(), "true", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(text.Trim(), "1", StringComparison.OrdinalIgnoreCase);
                default:
                    return Convert.ToInt64(raw, CultureInfo.InvariantCulture) != 0L;
            }
        }

        private static object ConvertEnum(object raw, Type enumType)
        {
            if (raw is string text && !string.IsNullOrWhiteSpace(text))
            {
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long enumNumber))
                {
                    return Enum.ToObject(enumType, enumNumber);
                }

                if (Enum.IsDefined(enumType, text))
                {
                    return Enum.Parse(enumType, text, ignoreCase: true);
                }
            }

            long numeric = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
            return Enum.ToObject(enumType, numeric);
        }

        private static object ConvertNumber(object raw, Type targetType)
        {
            object defaultValue = CreateDefaultValue(targetType);
            if (raw == null)
            {
                return defaultValue;
            }

            try
            {
                if (raw is string text)
                {
                    if (text.Length == 0)
                    {
                        return defaultValue;
                    }

                    if (targetType == typeof(float) || targetType == typeof(double) || targetType == typeof(decimal))
                    {
                        return Convert.ChangeType(text, targetType, CultureInfo.InvariantCulture);
                    }

                    long integer = long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                    return Convert.ChangeType(integer, targetType, CultureInfo.InvariantCulture);
                }

                return Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
            }
            catch
            {
                return defaultValue;
            }
        }

        private static bool TryConvertList(object raw, Type targetType, out object converted)
        {
            converted = null;
            if (!(raw is List<object> source))
            {
                return false;
            }

            if (targetType.IsArray)
            {
                converted = ConvertArray(source, targetType.GetElementType());
                return true;
            }

            Type itemType = ResolveListItemType(targetType);
            if (itemType == null || !typeof(IList).IsAssignableFrom(targetType))
            {
                return false;
            }

            IList list = CreateListInstance(targetType, itemType);
            foreach (object item in source)
            {
                list.Add(ConvertValue(item, itemType));
            }

            converted = list;
            return true;
        }

        private static object ConvertArray(List<object> source, Type itemType)
        {
            if (itemType == null)
            {
                return Array.Empty<object>();
            }

            Array array = Array.CreateInstance(itemType, source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                array.SetValue(ConvertValue(source[i], itemType), i);
            }

            return array;
        }

        private static Type ResolveListItemType(Type targetType)
        {
            if (targetType == null)
            {
                return null;
            }

            if (targetType.IsGenericType)
            {
                Type[] args = targetType.GetGenericArguments();
                return args.Length == 1 ? args[0] : null;
            }

            Type listInterface = targetType
                .GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>));
            if (listInterface == null)
            {
                return null;
            }

            Type[] interfaceArgs = listInterface.GetGenericArguments();
            return interfaceArgs.Length == 1 ? interfaceArgs[0] : null;
        }

        private static IList CreateListInstance(Type targetType, Type itemType)
        {
            if (targetType.IsInterface || targetType.IsAbstract)
            {
                Type fallbackList = typeof(List<>).MakeGenericType(itemType);
                return (IList)Activator.CreateInstance(fallbackList);
            }

            try
            {
                return (IList)Activator.CreateInstance(targetType);
            }
            catch
            {
                Type fallbackList = typeof(List<>).MakeGenericType(itemType);
                return (IList)Activator.CreateInstance(fallbackList);
            }
        }

        private static object ConvertObject(Dictionary<string, object> dict, Type targetType)
        {
            object instance = CreateInstance(targetType);
            if (instance == null)
            {
                return null;
            }

            FieldInfo[] fields = targetType
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(field => !field.IsStatic)
                .ToArray();
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (!TryGetValue(dict, field.Name, out object rawField))
                {
                    continue;
                }

                object fieldValue = ConvertValue(rawField, field.FieldType);
                field.SetValue(instance, fieldValue);
            }

            return instance;
        }

        private static bool TryGetValue(Dictionary<string, object> source, string key, out object value)
        {
            if (source.TryGetValue(key, out value))
            {
                return true;
            }

            foreach (KeyValuePair<string, object> item in source)
            {
                if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = item.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static object CreateDefaultValue(Type type)
        {
            return type != null && type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        private static object CreateInstance(Type type)
        {
            if (type == null)
            {
                return null;
            }

            if (type.IsValueType)
            {
                return Activator.CreateInstance(type);
            }

            if (type.IsInterface || type.IsAbstract)
            {
                return null;
            }

            try
            {
                return Activator.CreateInstance(type);
            }
            catch
            {
                return null;
            }
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

        private sealed class JsonParser
        {
            private readonly string _json;
            private int _index;

            internal JsonParser(string json)
            {
                _json = json ?? string.Empty;
            }

            internal object Parse()
            {
                SkipWhitespace();
                object result = ParseValue();
                SkipWhitespace();
                if (_index != _json.Length)
                {
                    throw new FormatException("Unexpected trailing JSON token.");
                }

                return result;
            }

            private object ParseValue()
            {
                SkipWhitespace();
                if (_index >= _json.Length)
                {
                    throw new FormatException("Unexpected end of JSON input.");
                }

                char token = _json[_index];
                switch (token)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': return ParseLiteral("true", true);
                    case 'f': return ParseLiteral("false", false);
                    case 'n': return ParseLiteral("null", null);
                    default: return ParseNumber();
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                Expect('{');
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                SkipWhitespace();
                if (TryConsume('}'))
                {
                    return result;
                }

                while (true)
                {
                    string key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    object value = ParseValue();
                    result[key] = value;
                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        return result;
                    }

                    Expect(',');
                }
            }

            private List<object> ParseArray()
            {
                Expect('[');
                var result = new List<object>();
                SkipWhitespace();
                if (TryConsume(']'))
                {
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (TryConsume(']'))
                    {
                        return result;
                    }

                    Expect(',');
                }
            }

            private string ParseString()
            {
                Expect('"');
                var chars = new List<char>();
                while (_index < _json.Length)
                {
                    char c = Read();
                    if (c == '"')
                    {
                        return new string(chars.ToArray());
                    }

                    if (c != '\\')
                    {
                        chars.Add(c);
                        continue;
                    }

                    chars.Add(ReadEscapedChar());
                }

                throw new FormatException("Unterminated JSON string.");
            }

            private char ReadEscapedChar()
            {
                if (_index >= _json.Length)
                {
                    throw new FormatException("Invalid JSON escape sequence.");
                }

                char escape = Read();
                switch (escape)
                {
                    case '"': return '"';
                    case '\\': return '\\';
                    case '/': return '/';
                    case 'b': return '\b';
                    case 'f': return '\f';
                    case 'n': return '\n';
                    case 'r': return '\r';
                    case 't': return '\t';
                    case 'u': return ReadUnicodeChar();
                    default: throw new FormatException("Unsupported JSON escape.");
                }
            }

            private char ReadUnicodeChar()
            {
                if (_index + 4 > _json.Length)
                {
                    throw new FormatException("Invalid unicode escape length.");
                }

                string code = _json.Substring(_index, 4);
                _index += 4;
                return (char)int.Parse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            private object ParseNumber()
            {
                int start = _index;
                if (TryConsume('-'))
                {
                    // Optional leading minus consumed.
                }

                ConsumeDigits();
                bool hasFraction = TryConsume('.');
                if (hasFraction)
                {
                    ConsumeDigits();
                }

                bool hasExponent = TryConsume('e') || TryConsume('E');
                if (hasExponent)
                {
                    TryConsume('+');
                    TryConsume('-');
                    ConsumeDigits();
                }

                string text = _json.Substring(start, _index - start);
                if (text.Length == 0)
                {
                    throw new FormatException("Invalid JSON number.");
                }

                if (hasFraction || hasExponent)
                {
                    return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                }

                return long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
            }

            private object ParseLiteral(string token, object value)
            {
                if (!Match(token))
                {
                    throw new FormatException($"Invalid JSON literal '{token}'.");
                }

                return value;
            }

            private void ConsumeDigits()
            {
                int start = _index;
                while (_index < _json.Length && char.IsDigit(_json[_index]))
                {
                    _index++;
                }

                if (start == _index)
                {
                    throw new FormatException("Expected numeric digits.");
                }
            }

            private bool Match(string token)
            {
                if (_index + token.Length > _json.Length)
                {
                    return false;
                }

                for (int i = 0; i < token.Length; i++)
                {
                    if (_json[_index + i] != token[i])
                    {
                        return false;
                    }
                }

                _index += token.Length;
                return true;
            }

            private bool TryConsume(char c)
            {
                if (_index >= _json.Length || _json[_index] != c)
                {
                    return false;
                }

                _index++;
                return true;
            }

            private void Expect(char c)
            {
                SkipWhitespace();
                if (!TryConsume(c))
                {
                    throw new FormatException($"Expected '{c}'.");
                }
            }

            private char Read()
            {
                if (_index >= _json.Length)
                {
                    throw new FormatException("Unexpected end of JSON content.");
                }

                return _json[_index++];
            }

            private void SkipWhitespace()
            {
                while (_index < _json.Length && char.IsWhiteSpace(_json[_index]))
                {
                    _index++;
                }
            }
        }
    }
}
