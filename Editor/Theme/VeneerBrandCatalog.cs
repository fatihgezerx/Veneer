using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Veneer
{
    /// <summary>
    /// Every <see cref="VeneerBrand"/> the project defines, found by reflection: brands are not registered anywhere, a system keeps
    /// one in a static field or property of an internal class, so the catalog scans the loaded assemblies that reference the kit
    /// (and the kit itself) and reads every static member of type <see cref="VeneerBrand"/>. The result is built on first use and kept
    /// until the next domain reload or <see cref="Refresh"/>. The kit's own default brand (<see cref="VeneerBrand.Default"/>) is left out.
    /// </summary>
    public static class VeneerBrandCatalog
    {
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static List<VeneerBrand> brands;

        /// <summary>The brands of the project ordered by name, one per name (the first one found wins); never null.</summary>
        public static IReadOnlyList<VeneerBrand> All
        {
            get
            {
                if (brands == null)
                {
                    brands = Scan();
                }

                return brands;
            }
        }

        /// <summary>The brand with this exact name; null when there is none.</summary>
        public static VeneerBrand Find(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            IReadOnlyList<VeneerBrand> list = All;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Name, name, StringComparison.Ordinal))
                {
                    return list[i];
                }
            }

            return null;
        }

        /// <summary>Forgets the result so the next access scans the assemblies again (a list already handed out stays valid and unchanged).</summary>
        public static void Refresh()
        {
            brands = null;
        }

        // A one-off scan, so plain loops and LINQ-free code are for clarity, not speed.
        private static List<VeneerBrand> Scan()
        {
            var found = new List<VeneerBrand>(16);
            var names = new HashSet<string>(StringComparer.Ordinal);
            Assembly kit = typeof(VeneerBrand).Assembly;
            string kitName = kit.GetName().Name;
            VeneerBrand defaultBrand = VeneerBrand.Default;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly.IsDynamic || (assembly != kit && !References(assembly, kitName)))
                {
                    continue;
                }

                Type[] types = TypesOf(assembly);
                for (int t = 0; t < types.Length; t++)
                {
                    if (types[t] != null && !types[t].ContainsGenericParameters)
                    {
                        ReadType(types[t], found, names, defaultBrand);
                    }
                }
            }

            found.Sort(CompareByName);
            return found;
        }

        private static void ReadType(Type type, List<VeneerBrand> found, HashSet<string> names, VeneerBrand defaultBrand)
        {
            FieldInfo[] fields;
            PropertyInfo[] properties;
            try
            {
                fields = type.GetFields(StaticMembers);
                properties = type.GetProperties(StaticMembers);
            }
            catch (Exception)
            {
                // A type whose members cannot be listed (a missing dependency) has no brand we could read.
                return;
            }

            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType == typeof(VeneerBrand))
                {
                    Add(Read(type, fields[i], null), found, names, defaultBrand);
                }
            }

            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                if (property.PropertyType == typeof(VeneerBrand) && property.CanRead && property.GetIndexParameters().Length == 0)
                {
                    Add(Read(type, null, property), found, names, defaultBrand);
                }
            }
        }

        // Reading a static member runs the class's static initialiser, which can throw: log it and go on.
        private static VeneerBrand Read(Type type, FieldInfo field, PropertyInfo property)
        {
            try
            {
                object value = field != null ? field.GetValue(null) : property.GetValue(null);
                return value as VeneerBrand;
            }
            catch (Exception exception)
            {
                Exception cause = exception.InnerException ?? exception;
                string member = field != null ? field.Name : property.Name;
                Debug.LogWarning("Veneer: could not read the brand " + type.FullName + "." + member + ": " + cause.Message);
                return null;
            }
        }

        private static void Add(VeneerBrand brand, List<VeneerBrand> found, HashSet<string> names, VeneerBrand defaultBrand)
        {
            if (brand == null || ReferenceEquals(brand, defaultBrand) || string.IsNullOrEmpty(brand.Name) || !names.Add(brand.Name))
            {
                return;
            }

            found.Add(brand);
        }

        private static bool References(Assembly assembly, string name)
        {
            AssemblyName[] referenced;
            try
            {
                referenced = assembly.GetReferencedAssemblies();
            }
            catch (Exception)
            {
                return false;
            }

            for (int i = 0; i < referenced.Length; i++)
            {
                if (string.Equals(referenced[i].Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static Type[] TypesOf(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                // Some types failed to load; the rest are still usable.
                return exception.Types;
            }
            catch (Exception)
            {
                return new Type[0];
            }
        }

        private static int CompareByName(VeneerBrand a, VeneerBrand b)
        {
            int order = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            return order != 0 ? order : string.CompareOrdinal(a.Name, b.Name);
        }
    }
}
