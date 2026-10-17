using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// The plumbing every custom control of the kit shares: keeping the control in step with its property (Undo, Redo, prefab reverts and
    /// other edits all change the serialized value, which fires the tracker), the prefab-override look (bold label, "Revert" in the context
    /// menu) that custom controls do not get for free the way <see cref="PropertyField"/> does, and edits that must reach every selected
    /// object one by one. Built once when an Inspector opens; nothing runs per frame.
    /// </summary>
    public static class VeneerBinding
    {
        /// <summary>
        /// Keeps a custom control in step with <paramref name="property"/>: <paramref name="sync"/> runs now, after the control is attached,
        /// and whenever the value changes. When <paramref name="label"/> is given it turns bold while the property is a prefab override,
        /// and <paramref name="menuTarget"/> gets "Revert" in its context menu.
        /// </summary>
        /// <param name="host">Any element of the control; the tracker lives on it and stops with it.</param>
        /// <param name="property">The property the control edits (it is copied).</param>
        /// <param name="label">The control's title, for the override mark; may be null.</param>
        /// <param name="menuTarget">The element that gets the Revert menu entry; may be null.</param>
        /// <param name="sync">Brings the control in line with the data; may be null.</param>
        public static void Hook(VisualElement host, SerializedProperty property, Label label, VisualElement menuTarget, Action sync)
        {
            SerializedProperty tracked = property.Copy();
            bool initial = true;
            Action refresh = () =>
            {
                // A control that left the panel may point at a disposed SerializedObject: do not touch it.
                if (!initial && host.panel == null)
                {
                    return;
                }

                if (sync != null)
                {
                    sync();
                }

                if (label != null)
                {
                    label.style.unityFontStyleAndWeight = tracked.prefabOverride ? FontStyle.Bold : FontStyle.Normal;
                }
            };

            if (menuTarget != null)
            {
                menuTarget.AddManipulator(new ContextualMenuManipulator(evt =>
                {
                    if (tracked.prefabOverride)
                    {
                        evt.menu.AppendAction("Revert", action => PrefabUtility.RevertPropertyOverride(tracked, InteractionMode.UserAction));
                    }
                }));
            }

            host.TrackPropertyValue(tracked, changed => refresh());
            host.RegisterCallback<AttachToPanelEvent>(evt => refresh());
            refresh();
            initial = false;
        }

        /// <summary>Writes the pending changes of the property's serialized object (one Undo step).</summary>
        public static void Commit(SerializedProperty property)
        {
            property.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Runs <paramref name="edit"/> on the same property of every selected object, each through its own serialized object, so objects
        /// that differ keep their other values (used for one bit of a flags value, for example). One Undo group, named
        /// <paramref name="undoName"/>.
        /// </summary>
        public static void ForEachTarget(SerializedProperty property, Action<SerializedProperty> edit, string undoName)
        {
            SerializedObject main = property.serializedObject;
            string path = property.propertyPath;
            main.ApplyModifiedProperties();
            UnityEngine.Object[] targets = main.targetObjects;
            for (int i = 0; i < targets.Length; i++)
            {
                using (var single = new SerializedObject(targets[i]))
                {
                    SerializedProperty own = single.FindProperty(path);
                    if (own != null)
                    {
                        edit(own);
                        single.ApplyModifiedProperties();
                    }
                }
            }

            Undo.SetCurrentGroupName(undoName);
            main.Update();
        }

        /// <summary>How many of the selected objects satisfy <paramref name="test"/> (each looked at through its own serialized object).</summary>
        public static int CountTargets(SerializedProperty property, Func<SerializedProperty, bool> test)
        {
            UnityEngine.Object[] targets = property.serializedObject.targetObjects;
            string path = property.propertyPath;
            int count = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                using (var single = new SerializedObject(targets[i]))
                {
                    SerializedProperty own = single.FindProperty(path);
                    if (own != null && test(own))
                    {
                        count++;
                    }
                }
            }

            return count;
        }
    }
}
