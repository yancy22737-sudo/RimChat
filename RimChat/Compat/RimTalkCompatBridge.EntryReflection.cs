using System;
using System.Reflection;

namespace RimChat.Compat
{
    /// <summary>/// Dependencies: reflection runtime conversion utilities.
 /// Responsibility: assign reflective PromptEntry members and enum/value conversions.
 ///</summary>
    public static partial class RimTalkCompatBridge
    {
        private static object CreateEnumValue(Type enumType, string desiredName, int defaultIndex)
        {
            if (enumType == null)
            {
                return null;
            }

            if (!enumType.IsEnum)
            {
                return desiredName;
            }

            Array values = Enum.GetValues(enumType);
            if (values == null || values.Length == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(desiredName))
            {
                try
                {
                    return Enum.Parse(enumType, desiredName, true);
                }
                catch
                {
                    if (int.TryParse(desiredName, out int parsedIndex) &&
                        parsedIndex >= 0 &&
                        parsedIndex < values.Length)
                    {
                        return values.GetValue(parsedIndex);
                    }
                }
            }

            int index = defaultIndex;
            if (index < 0)
            {
                index = 0;
            }

            if (index >= values.Length)
            {
                index = values.Length - 1;
            }

            return values.GetValue(index);
        }

        private static bool SetPropertyOrField(object target, string memberName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
            {
                return false;
            }

            Type type = target.GetType();
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null && property.CanWrite)
            {
                if (TryConvertForTargetType(value, property.PropertyType, out object converted))
                {
                    property.SetValue(target, converted, null);
                    return true;
                }

                return false;
            }

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null)
            {
                if (TryConvertForTargetType(value, field.FieldType, out object converted))
                {
                    field.SetValue(target, converted);
                    return true;
                }

                return false;
            }

            return false;
        }

        private static bool TryConvertForTargetType(object value, Type targetType, out object converted)
        {
            converted = null;
            if (targetType == null)
            {
                return false;
            }

            Type nonNullable = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (value == null)
            {
                if (nonNullable.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                {
                    converted = Activator.CreateInstance(nonNullable);
                }
                return true;
            }

            if (nonNullable.IsInstanceOfType(value))
            {
                converted = value;
                return true;
            }

            try
            {
                if (nonNullable.IsEnum)
                {
                    if (value is string text)
                    {
                        converted = Enum.Parse(nonNullable, text, true);
                        return true;
                    }

                    converted = Enum.ToObject(nonNullable, value);
                    return true;
                }

                if (nonNullable == typeof(string))
                {
                    converted = value.ToString() ?? string.Empty;
                    return true;
                }

                if (nonNullable == typeof(int))
                {
                    if (value is string intText && int.TryParse(intText, out int parsedInt))
                    {
                        converted = parsedInt;
                        return true;
                    }

                    converted = Convert.ToInt32(value);
                    return true;
                }

                if (nonNullable == typeof(bool))
                {
                    if (value is string boolText && bool.TryParse(boolText, out bool parsedBool))
                    {
                        converted = parsedBool;
                        return true;
                    }

                    converted = Convert.ToBoolean(value);
                    return true;
                }

                converted = Convert.ChangeType(value, nonNullable);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object ParseExistingEntryEnum(object entry, string memberName, string fallbackText)
        {
            if (entry == null || string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            Type type = entry.GetType();
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null && property.PropertyType.IsEnum)
            {
                return CreateEnumValue(property.PropertyType, fallbackText, 0);
            }

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null && field.FieldType.IsEnum)
            {
                return CreateEnumValue(field.FieldType, fallbackText, 0);
            }

            return fallbackText;
        }
    }
}
