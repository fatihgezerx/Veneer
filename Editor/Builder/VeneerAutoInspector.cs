using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// An Inspector that lays itself out from the serialized data alone: <c>[CustomEditor(typeof(Foo), true)] class FooEditor :
    /// VeneerAutoInspector { }</c> is all a system needs. A header card names the type; every <c>[Header]</c> starts a card in field order
    /// (fields before the first Header go into a card called General); with more than four groups they become tabs. Arrays and Lists use
    /// <see cref="VeneerControls.ListEditor"/>, nested serializable structs get a nested bordered card, floats and ints with <c>[Range]</c>
    /// become sliders and those with <c>[Min]</c> number rows; everything else is a <see cref="UnityEditor.UIElements.PropertyField"/>, so
    /// tooltips, custom drawers and prefab marks stay. <c>[HideInInspector]</c> fields and <c>m_Script</c> are skipped, <c>[Space]</c> is
    /// ignored. Override the virtual members to brand it, rename it or add content above and below.
    /// A nested struct that has its own <c>PropertyDrawer</c> is also expanded into a card (the kit cannot ask Unity for drawers); put such
    /// a field in a hand-written <see cref="VeneerInspector"/> if that matters.
    /// </summary>
    [CanEditMultipleObjects]
    public class VeneerAutoInspector : VeneerInspector
    {
        private const int MaxNesting = 3;

        /// <summary>Title of the header card. Defaults to the type's name split into words.</summary>
        protected virtual string Title => ObjectNames.NicifyVariableName(target.GetType().Name);

        /// <summary>Muted line under the title. Defaults to a count when several objects are selected, otherwise none.</summary>
        protected virtual string Subtitle => targets.Length > 1 ? targets.Length + " objects selected" : null;

        /// <summary>Icon name of the header (a kit or brand icon); null shows the brand's logo when it has one.</summary>
        protected virtual string IconName => null;

        /// <summary>More groups than this become tabs instead of stacked cards.</summary>
        protected virtual int TabThreshold => 4;

        /// <summary>Icon of the tab for a group, or null for none (only used when the groups become tabs).</summary>
        protected virtual string TabIcon(string groupTitle)
        {
            return null;
        }

        /// <summary>Content added between the header and the generated groups.</summary>
        protected virtual void ExtraTopContent(VeneerInspectorBuilder b)
        {
        }

        /// <summary>Content added after the generated groups.</summary>
        protected virtual void ExtraBottomContent(VeneerInspectorBuilder b)
        {
        }

        /// <inheritdoc />
        protected sealed override void Build(VeneerInspectorBuilder b)
        {
            b.Header(Title, Subtitle, IconName);
            ExtraTopContent(b);

            List<Group> groups = CollectGroups();
            if (groups.Count == 0)
            {
                b.Hint("This component has no visible fields.");
            }
            else if (groups.Count > TabThreshold)
            {
                var tabs = new (string label, string icon)[groups.Count];
                for (int i = 0; i < groups.Count; i++)
                {
                    tabs[i] = (groups[i].Title, TabIcon(groups[i].Title));
                }

                VisualElement[] pages = b.Tabs(tabs);
                for (int i = 0; i < groups.Count; i++)
                {
                    using (b.Into(pages[i]))
                    {
                        VisualElement card = b.Card();
                        using (b.Into(card))
                        {
                            AddGroup(b, groups[i]);
                        }
                    }
                }
            }
            else
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    VisualElement card = b.Card(groups[i].Title, null);
                    using (b.Into(card))
                    {
                        AddGroup(b, groups[i]);
                    }
                }
            }

            ExtraBottomContent(b);
        }

        private List<Group> CollectGroups()
        {
            var groups = new List<Group>(4);
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enter = true;
            while (iterator.NextVisible(enter))
            {
                enter = false;
                if (iterator.propertyPath == "m_Script")
                {
                    continue;
                }

                string header = VeneerFieldInfo.GetHeader(iterator);
                if (header != null || groups.Count == 0)
                {
                    groups.Add(new Group { Title = header ?? "General" });
                }

                groups[groups.Count - 1].Properties.Add(iterator.Copy());
            }

            return groups;
        }

        private static void AddGroup(VeneerInspectorBuilder b, Group group)
        {
            for (int i = 0; i < group.Properties.Count; i++)
            {
                AddProperty(b, group.Properties[i], 0);
            }
        }

        private static void AddProperty(VeneerInspectorBuilder b, SerializedProperty property, int depth)
        {
            string path = property.propertyPath;
            if (property.isArray && property.propertyType != SerializedPropertyType.String)
            {
                b.List(path);
                return;
            }

            if (depth < MaxNesting && IsNestedStruct(property))
            {
                VisualElement nested = b.Card(property.displayName, null);
                nested.AddToClassList("vn-card--nested");
                nested.tooltip = property.tooltip;
                using (b.Into(nested))
                {
                    SerializedProperty child = property.Copy();
                    SerializedProperty end = property.GetEndProperty();
                    bool enter = true;
                    while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
                    {
                        enter = false;
                        AddProperty(b, child.Copy(), depth + 1);
                    }
                }

                return;
            }

            if (property.propertyType == SerializedPropertyType.Float || property.propertyType == SerializedPropertyType.Integer)
            {
                if (VeneerFieldInfo.TryGetRange(property, out _, out _))
                {
                    b.Slider(path);
                    return;
                }

                VeneerFieldInfo.GetLimits(property, out float min, out _);
                if (!float.IsNegativeInfinity(min))
                {
                    b.Number(path);
                    return;
                }
            }

            b.Field(path);
        }

        // A plain [Serializable] class or struct: not a UnityEvent, not an object reference, not a managed reference.
        private static bool IsNestedStruct(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.Generic || property.isArray)
            {
                return false;
            }

            if (!VeneerFieldInfo.TryResolve(property, out System.Reflection.FieldInfo field, out Type type) || type == null)
            {
                return false;
            }

            return Attribute.IsDefined(type, typeof(SerializableAttribute), false)
                && !typeof(UnityEngine.Object).IsAssignableFrom(type)
                && !typeof(UnityEngine.Events.UnityEventBase).IsAssignableFrom(type)
                && field != null;
        }

        private sealed class Group
        {
            public string Title;
            public readonly List<SerializedProperty> Properties = new List<SerializedProperty>(8);
        }
    }
}
