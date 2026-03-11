#if !NET8_0_OR_GREATER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace System.Text.Json
{
    public enum JsonSerializerDefaults
    {
        General,
        Web,
    }

    public abstract class JsonNamingPolicy
    {
        public static JsonNamingPolicy CamelCase { get; } = new CamelCaseNamingPolicy();

        public abstract string ConvertName(string name);

        private sealed class CamelCaseNamingPolicy : JsonNamingPolicy
        {
            public override string ConvertName(string name)
            {
                if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
                {
                    return name;
                }

                return char.ToLowerInvariant(name[0]) + name.Substring(1);
            }
        }
    }

    public sealed class JsonSerializerOptions
    {
        public JsonSerializerOptions()
        {
        }

        public JsonSerializerOptions(JsonSerializerDefaults defaults)
        {
            PropertyNameCaseInsensitive = defaults == JsonSerializerDefaults.Web;
        }

        public bool PropertyNameCaseInsensitive { get; set; }
        public bool WriteIndented { get; set; }
        public JsonNamingPolicy PropertyNamingPolicy { get; set; }
    }

    public static class JsonSerializer
    {
        public static string Serialize<T>(T value)
        {
            return Serialize(value, new JsonSerializerOptions());
        }

        public static string Serialize<T>(T value, JsonSerializerOptions options)
        {
            var safeOptions = options ?? new JsonSerializerOptions();
            var serializer = new JavaScriptSerializer();
            var payload = ToSerializableGraph(value, safeOptions);
            return serializer.Serialize(payload);
        }

        public static T Deserialize<T>(string json)
        {
            return Deserialize<T>(json, new JsonSerializerOptions());
        }

        public static T Deserialize<T>(string json, JsonSerializerOptions options)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return default(T);
            }

            var safeOptions = options ?? new JsonSerializerOptions();
            var serializer = new JavaScriptSerializer();
            var raw = serializer.DeserializeObject(json);
            var converted = ConvertTo(typeof(T), raw, safeOptions);
            return (T)converted;
        }

        private static object ToSerializableGraph(object value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                return null;
            }

            var type = value.GetType();
            if (type == typeof(string) || type == typeof(bool) || type == typeof(int) || type == typeof(long)
                || type == typeof(double) || type == typeof(decimal) || type == typeof(float) || type.IsEnum)
            {
                return value;
            }

            if (type == typeof(DateTimeOffset))
            {
                return ((DateTimeOffset)value).ToString("o", CultureInfo.InvariantCulture);
            }

            if (type == typeof(DateTime))
            {
                return ((DateTime)value).ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            }

            if (value is IDictionary dictionary)
            {
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in dictionary)
                {
                    map[Convert.ToString(entry.Key, CultureInfo.InvariantCulture)] = ToSerializableGraph(entry.Value, options);
                }

                return map;
            }

            if (value is IEnumerable enumerable && !(value is string))
            {
                var list = new List<object>();
                foreach (var item in enumerable)
                {
                    list.Add(ToSerializableGraph(item, options));
                }

                return list;
            }

            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (!prop.CanRead)
                {
                    continue;
                }

                var propValue = prop.GetValue(value, null);
                var name = ResolveSerializedName(prop, options);
                result[name] = ToSerializableGraph(propValue, options);
            }

            return result;
        }

        private static object ConvertTo(Type targetType, object raw, JsonSerializerOptions options)
        {
            if (raw == null)
            {
                if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
                {
                    return null;
                }

                return Activator.CreateInstance(targetType);
            }

            var nullableType = Nullable.GetUnderlyingType(targetType);
            if (nullableType != null)
            {
                return ConvertTo(nullableType, raw, options);
            }

            if (targetType == typeof(string))
            {
                return Convert.ToString(raw, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(DateTimeOffset))
            {
                return DateTimeOffset.Parse(Convert.ToString(raw, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            }

            if (targetType == typeof(DateTime))
            {
                return DateTime.Parse(Convert.ToString(raw, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            }

            if (targetType.IsEnum)
            {
                return Enum.Parse(targetType, Convert.ToString(raw, CultureInfo.InvariantCulture), true);
            }

            if (targetType == typeof(bool) || targetType == typeof(int) || targetType == typeof(long)
                || targetType == typeof(double) || targetType == typeof(decimal) || targetType == typeof(float))
            {
                return Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
            }

            if (targetType.IsArray && raw is object[] rawArray)
            {
                var elementType = targetType.GetElementType();
                var array = Array.CreateInstance(elementType, rawArray.Length);
                for (var i = 0; i < rawArray.Length; i++)
                {
                    array.SetValue(ConvertTo(elementType, rawArray[i], options), i);
                }

                return array;
            }

            if (targetType.IsGenericType && typeof(IList).IsAssignableFrom(targetType))
            {
                var elementType = targetType.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(targetType);
                var source = raw as object[];
                if (source != null)
                {
                    foreach (var item in source)
                    {
                        list.Add(ConvertTo(elementType, item, options));
                    }
                }

                return list;
            }

            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var args = targetType.GetGenericArguments();
                var keyType = args[0];
                var valueType = args[1];
                var dict = (IDictionary)Activator.CreateInstance(targetType);
                var source = raw as Dictionary<string, object>;
                if (source != null)
                {
                    foreach (var pair in source)
                    {
                        var key = keyType == typeof(string)
                            ? (object)pair.Key
                            : Convert.ChangeType(pair.Key, keyType, CultureInfo.InvariantCulture);
                        dict[key] = ConvertTo(valueType, pair.Value, options);
                    }
                }

                return dict;
            }

            var mapRaw = raw as Dictionary<string, object>;
            if (mapRaw == null)
            {
                return Activator.CreateInstance(targetType);
            }

            var instance = Activator.CreateInstance(targetType);
            var properties = targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite)
                .ToArray();

            foreach (var prop in properties)
            {
                var expectedName = ResolveSerializedName(prop, options);
                object rawValue;
                if (!TryGetValue(mapRaw, expectedName, options.PropertyNameCaseInsensitive, out rawValue))
                {
                    if (!TryGetValue(mapRaw, prop.Name, options.PropertyNameCaseInsensitive, out rawValue))
                    {
                        continue;
                    }
                }

                var converted = ConvertTo(prop.PropertyType, rawValue, options);
                prop.SetValue(instance, converted, null);
            }

            return instance;
        }

        private static bool TryGetValue(Dictionary<string, object> map, string key, bool ignoreCase, out object value)
        {
            if (!ignoreCase)
            {
                return map.TryGetValue(key, out value);
            }

            foreach (var pair in map)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static string ResolveSerializedName(PropertyInfo prop, JsonSerializerOptions options)
        {
            var attr = (System.Text.Json.Serialization.JsonPropertyNameAttribute)Attribute.GetCustomAttribute(
                prop,
                typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute));

            if (attr != null && !string.IsNullOrWhiteSpace(attr.Name))
            {
                return attr.Name;
            }

            if (options.PropertyNamingPolicy != null)
            {
                return options.PropertyNamingPolicy.ConvertName(prop.Name);
            }

            return prop.Name;
        }
    }
}

namespace System.Text.Json.Serialization
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class JsonPropertyNameAttribute : Attribute
    {
        public JsonPropertyNameAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; private set; }
    }
}
#endif
