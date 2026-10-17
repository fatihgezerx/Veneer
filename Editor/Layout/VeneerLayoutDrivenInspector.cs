using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// The base of a system's hand-named editor that is drawn from a <see cref="VeneerLayout"/>: the user's layout of the type (an asset in
    /// the project, made in the Designer) when there is one with content, otherwise the system's own default layout
    /// (<see cref="IVeneerDefaultLayoutProvider"/>). Derive a class, put <c>[CustomEditor(typeof(T))]</c> (and
    /// <c>[CanEditMultipleObjects]</c> when it fits) on it and override <see cref="VeneerInspector.Brand"/> for the system's look.
    /// Unity allows one editor per type, so the system's editor is this one and its technical pieces are registered custom items.
    /// </summary>
    /// <typeparam name="T">The edited type.</typeparam>
    public abstract class VeneerLayoutDrivenInspector<T> : VeneerInspector<T>, IVeneerLayoutDriven where T : UnityEngine.Object
    {
        /// <summary>The edited type (<c>typeof(T)</c>).</summary>
        public Type TargetType => typeof(T);

        /// <summary>
        /// Draws the Inspector for a selection of several objects. Return true when you built everything (header included): nothing else is
        /// built then. The default returns false, so the layout is drawn for the first object like for one selection.
        /// </summary>
        protected virtual bool BuildMultiSelection(VeneerInspectorBuilder b)
        {
            return false;
        }

        /// <summary>Draws the multi-selection override, or else the user's layout, or else the system's default layout.</summary>
        protected override void Build(VeneerInspectorBuilder b)
        {
            if (TargetCount > 1 && BuildMultiSelection(b))
            {
                return;
            }

            VeneerLayout layout = ResolveLayout();
            if (layout == null)
            {
                b.Hint("No Veneer layout was found for " + typeof(T).Name + "; showing Unity's default Inspector.", "warn");
                var plain = new VisualElement();
                if (target != null)
                {
                    InspectorElement.FillDefaultInspector(plain, serializedObject, this);
                }

                b.Add(plain);
                return;
            }

            VeneerLayoutRenderer.Build(b, layout, serializedObject);
        }

        // The user's layout (when layouts are switched on), else the system's default. The type of the first target is asked so a
        // subclass with its own layout or provider is honoured; base types are searched too.
        private VeneerLayout ResolveLayout()
        {
            Type type = target != null ? target.GetType() : typeof(T);
            if (VeneerLayoutSettings.Enabled)
            {
                VeneerLayout user = VeneerLayoutRegistry.Find(type);
                if (user != null && user.HasContent)
                {
                    // The items of the system must be known even though its default layout is not drawn.
                    VeneerDefaultLayouts.EnsureRegistered();
                    return user;
                }
            }

            return VeneerDefaultLayouts.TryGet(type, out VeneerLayout layout) ? layout : null;
        }
    }
}
