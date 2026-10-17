using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Veneer
{
    /// <summary>
    /// Implemented by a plain class of a system to ship the default design of its editor. Providers are found with
    /// <c>TypeCache</c> and created with <see cref="Activator.CreateInstance(Type)"/>, so they need a public parameterless constructor.
    /// </summary>
    public interface IVeneerDefaultLayoutProvider
    {
        /// <summary>The type whose editor this layout is for (the layout also serves types derived from it).</summary>
        Type TargetType { get; }

        /// <summary>Makes the default layout, normally with <see cref="VeneerLayoutBuilder"/>. Called once per type; the result is cached.</summary>
        VeneerLayout CreateLayout();

        /// <summary>Registers the system's custom items and custom widgets (see <see cref="VeneerCustomItems"/>). Called once after discovery.</summary>
        void RegisterItems();
    }

    /// <summary>Implemented by <see cref="VeneerLayoutDrivenInspector{T}"/> so the Designer can recognise an editor that is drawn from a layout.</summary>
    public interface IVeneerLayoutDriven
    {
        /// <summary>The type the editor edits.</summary>
        Type TargetType { get; }
    }

    /// <summary>
    /// The registry of the systems' default layouts. Providers (<see cref="IVeneerDefaultLayoutProvider"/>) are discovered once, each
    /// provider's <see cref="IVeneerDefaultLayoutProvider.RegisterItems"/> is called once, and each layout is made once and cached. The
    /// layouts are in-memory objects, never assets; they are destroyed when the domain reloads.
    /// </summary>
    public static class VeneerDefaultLayouts
    {
        private static readonly Dictionary<Type, IVeneerDefaultLayoutProvider> Providers = new Dictionary<Type, IVeneerDefaultLayoutProvider>(8);
        private static readonly Dictionary<Type, VeneerLayout> Layouts = new Dictionary<Type, VeneerLayout>(8);
        private static readonly List<VeneerLayout> Created = new List<VeneerLayout>(8);
        private static readonly List<Type> TargetTypes = new List<Type>(8);

        private static bool discovered;

        static VeneerDefaultLayouts()
        {
            // The layouts are native objects: free them before the managed side is thrown away by a reload.
            AssemblyReloadEvents.beforeAssemblyReload += DestroyAll;
        }

        /// <summary>The target type of every provider that was found (discovers them first).</summary>
        public static IReadOnlyList<Type> All
        {
            get
            {
                EnsureRegistered();
                return TargetTypes;
            }
        }

        /// <summary>
        /// The system's default layout for <paramref name="type"/>: the provider of exactly that type, otherwise the one of the nearest base
        /// type. False when no provider fits or the provider failed (a warning is logged).
        /// </summary>
        public static bool TryGet(Type type, out VeneerLayout layout)
        {
            layout = null;
            if (type == null)
            {
                return false;
            }

            EnsureRegistered();
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                if (!Providers.TryGetValue(t, out IVeneerDefaultLayoutProvider provider))
                {
                    continue;
                }

                if (Layouts.TryGetValue(t, out VeneerLayout cached) && cached != null)
                {
                    layout = cached;
                    return true;
                }

                VeneerLayout made = null;
                try
                {
                    made = provider.CreateLayout();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Veneer: the default layout of " + t.Name + " failed: " + exception.Message);
                }

                if (made == null)
                {
                    return false;
                }

                Layouts[t] = made;
                Created.Add(made);
                layout = made;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Clears the caches: the next lookup discovers the providers, registers their items and makes the layouts again. The layouts made
        /// before are NOT destroyed (a window may still show one); they are freed when the domain reloads.
        /// </summary>
        public static void Invalidate()
        {
            discovered = false;
            Providers.Clear();
            Layouts.Clear();
            TargetTypes.Clear();
        }

        /// <summary>
        /// Discovers the providers and calls their <see cref="IVeneerDefaultLayoutProvider.RegisterItems"/> if that has not happened yet.
        /// Every lookup of <see cref="VeneerCustomItems"/> and <see cref="TryGet"/> does this; it costs one flag check afterwards.
        /// </summary>
        public static void EnsureRegistered()
        {
            if (discovered)
            {
                return;
            }

            // Set first: a provider that asks for items while registering must not discover again.
            discovered = true;
            var found = new List<IVeneerDefaultLayoutProvider>(8);
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IVeneerDefaultLayoutProvider>())
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition || type.ContainsGenericParameters)
                {
                    continue;
                }

                try
                {
                    var provider = (IVeneerDefaultLayoutProvider)Activator.CreateInstance(type);
                    Type target = provider.TargetType;
                    if (target == null)
                    {
                        continue;
                    }

                    if (Providers.ContainsKey(target))
                    {
                        Debug.LogWarning("Veneer: two default layout providers target " + target.Name + "; " + type.Name + " is ignored.");
                        continue;
                    }

                    Providers.Add(target, provider);
                    TargetTypes.Add(target);
                    found.Add(provider);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Veneer: could not create the layout provider " + type.Name + ": " + exception.Message);
                }
            }

            for (int i = 0; i < found.Count; i++)
            {
                try
                {
                    found[i].RegisterItems();
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Veneer: " + found[i].GetType().Name + ".RegisterItems failed: " + exception.Message);
                }
            }
        }

        private static void DestroyAll()
        {
            for (int i = 0; i < Created.Count; i++)
            {
                if (Created[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(Created[i]);
                }
            }

            Created.Clear();
            Invalidate();
        }
    }
}
