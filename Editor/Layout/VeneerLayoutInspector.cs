using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// The shared part of the two generic editors. When the edited type has a <see cref="VeneerLayout"/> with content (and the
    /// "Veneer layouts" switch is on) the Inspector is drawn by <see cref="VeneerLayoutRenderer"/> inside a Veneer root; in every other
    /// case it is Unity's plain default Inspector (<see cref="InspectorElement.FillDefaultInspector"/>), so a script without a layout
    /// looks and behaves as before. Unity picks the most specific custom editor for a type, so hand-written editors (the studio's
    /// systems, packages) are never replaced by these two.
    /// </summary>
    internal abstract class VeneerLayoutEditorBase : VeneerInspector
    {
        private VeneerLayout activeLayout;

        /// <summary>
        /// The brand of the layout being drawn (its name and accent); null while the plain Inspector is shown. The root is made from it by
        /// <see cref="VeneerInspector.CreateInspectorGUI"/>; the theme or system look the layout borrows is put on that root by
        /// <see cref="VeneerLayoutRenderer.Build"/> through <see cref="VeneerLayoutTheme.ApplyTo"/>, with the layout's own accent last.
        /// </summary>

        protected override VeneerBrand Brand => activeLayout != null ? VeneerLayoutTheme.BrandFor(activeLayout) : null;

        /// <summary>Draws the layout of the target's type, or the plain default Inspector when there is none.</summary>
        public override VisualElement CreateInspectorGUI()
        {
            activeLayout = FindLayout();
            if (activeLayout != null)
            {
                return base.CreateInspectorGUI();
            }

            var plain = new VisualElement();
            if (target != null)
            {
                InspectorElement.FillDefaultInspector(plain, serializedObject, this);
            }

            return plain;
        }

        /// <summary>Renders the layout found in <see cref="CreateInspectorGUI"/>.</summary>
        protected override void Build(VeneerInspectorBuilder b)
        {
            VeneerLayoutRenderer.Build(b, activeLayout, serializedObject);
        }

        private VeneerLayout FindLayout()
        {
            if (!VeneerLayoutSettings.Enabled || target == null)
            {
                return null;
            }

            // A component whose script is missing has nothing to lay out; leave it to Unity.
            SerializedProperty script = serializedObject.FindProperty("m_Script");
            if (script != null && script.propertyType == SerializedPropertyType.ObjectReference && script.objectReferenceValue == null)
            {
                return null;
            }

            VeneerLayout layout = VeneerLayoutRegistry.Find(target.GetType());
            return layout != null && layout.HasContent ? layout : null;
        }
    }

    /// <summary>The generic editor for every <see cref="MonoBehaviour"/> that has no more specific editor.</summary>
    [CustomEditor(typeof(MonoBehaviour), true)]
    [CanEditMultipleObjects]
    internal sealed class VeneerLayoutBehaviourEditor : VeneerLayoutEditorBase
    {
    }

    /// <summary>The generic editor for every <see cref="ScriptableObject"/> that has no more specific editor.</summary>
    [CustomEditor(typeof(ScriptableObject), true)]
    [CanEditMultipleObjects]
    internal sealed class VeneerLayoutAssetEditor : VeneerLayoutEditorBase
    {
    }
}
