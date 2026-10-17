using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Veneer
{
    /// <summary>
    /// Reads what a serialized property's C# field says about it: its type, its <c>[Min]</c> / <c>[Range]</c> limits and its
    /// <c>[Header]</c> text. Unity's own lookup for this is internal, so the field is found by walking the property path through the
    /// target's type. Results are cached per (type, path); a property that cannot be resolved (managed references, missing fields) simply
    /// reports no information.
    /// </summary>
    internal static class VeneerFieldInfo
    {
        private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly Dictionary<(Type, string), Resolved> Cache = new Dictionary<(Type, string), Resolved>(64);

        private struct Resolved
        {
            public bool Found;
            public FieldInfo Field;
            public Type ValueType;
        }

        /// <summary>Finds the field and the type of a property's value (for an array element: the element type).</summary>
        internal static bool TryResolve(SerializedProperty property, out FieldInfo field, out Type valueType)
        {
            field = null;
            valueType = null;
            UnityEngine.Object target = property.serializedObject.targetObject;
            if (target == null)
            {
                return false;
            }

            Type root = target.GetType();
            string path = property.propertyPath;
            var key = (root, path);
            if (!Cache.TryGetValue(key, out Resolved resolved))
            {
                resolved = Walk(root, path);
                Cache[key] = resolved;
            }

            field = resolved.Field;
            valueType = resolved.ValueType;
            return resolved.Found;
        }

        /// <summary>The C# type of the value a property holds, or null when it cannot be resolved.</summary>
        internal static Type GetValueType(SerializedProperty property)
        {
            return TryResolve(property, out _, out Type type) ? type : null;
        }

        /// <summary>The smallest and largest value allowed by <c>[Min]</c> or <c>[Range]</c> (infinities when the field has neither).</summary>
        internal static void GetLimits(SerializedProperty property, out float min, out float max)
        {
            min = float.NegativeInfinity;
            max = float.PositiveInfinity;
            if (!TryResolve(property, out FieldInfo field, out _) || field == null)
            {
                return;
            }

            var range = (RangeAttribute)Attribute.GetCustomAttribute(field, typeof(RangeAttribute), true);
            if (range != null)
            {
                min = range.min;
                max = range.max;
                return;
            }

            var minimum = (MinAttribute)Attribute.GetCustomAttribute(field, typeof(MinAttribute), true);
            if (minimum != null)
            {
                min = minimum.min;
            }
        }

        /// <summary>Whether the field carries <c>[Range]</c> (so a slider makes sense); fills the slider ends.</summary>
        internal static bool TryGetRange(SerializedProperty property, out float min, out float max)
        {
            min = 0f;
            max = 1f;
            if (!TryResolve(property, out FieldInfo field, out _) || field == null)
            {
                return false;
            }

            var range = (RangeAttribute)Attribute.GetCustomAttribute(field, typeof(RangeAttribute), true);
            if (range == null)
            {
                return false;
            }

            min = range.min;
            max = range.max;
            return true;
        }

        /// <summary>The text of the field's <c>[Header]</c> attribute, or null.</summary>
        internal static string GetHeader(SerializedProperty property)
        {
            if (!TryResolve(property, out FieldInfo field, out _) || field == null)
            {
                return null;
            }

            var header = (HeaderAttribute)Attribute.GetCustomAttribute(field, typeof(HeaderAttribute), true);
            return header == null ? null : header.header;
        }

        private static Resolved Walk(Type root, string path)
        {
            Type type = root;
            FieldInfo field = null;
            int i = 0;
            while (i < path.Length)
            {
                int dot = path.IndexOf('.', i);
                string part = dot < 0 ? path.Substring(i) : path.Substring(i, dot - i);
                i = dot < 0 ? path.Length : dot + 1;
                if (part == "Array")
                {
                    // "Array.data[n]": the next part names the element, which has the element type; the field stays the list's.
                    type = ElementType(type);
                    if (type == null)
                    {
                        return default;
                    }

                    int next = path.IndexOf('.', i);
                    i = next < 0 ? path.Length : next + 1;
                    continue;
                }

                field = FindField(type, part);
                if (field == null)
                {
                    return default;
                }

                type = field.FieldType;
            }

            return new Resolved { Found = field != null, Field = field, ValueType = type };
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, FieldFlags);
                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }

        private static Type ElementType(Type type)
        {
            if (type.IsArray)
            {
                return type.GetElementType();
            }

            if (type.IsGenericType)
            {
                Type[] arguments = type.GetGenericArguments();
                return arguments.Length == 1 ? arguments[0] : null;
            }

            foreach (Type face in type.GetInterfaces())
            {
                if (face.IsGenericType && face.GetGenericTypeDefinition() == typeof(IList<>))
                {
                    return face.GetGenericArguments()[0];
                }
            }

            return typeof(IEnumerable).IsAssignableFrom(type) ? typeof(object) : null;
        }
    }
}
