using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// The switch and the Project Settings page of the generic layout editors. The switch lives in <see cref="EditorPrefs"/> (it is a
    /// per-machine editor choice, not project data) and is on by default.
    /// </summary>
    public static class VeneerLayoutSettings
    {
        private const string EnabledKey = "Veneer.Layouts.Enabled";

        private static bool loaded;
        private static bool enabled = true;

        /// <summary>
        /// Whether the generic editors draw layouts. When off they show Unity's plain Inspector for every script. Changing it rebuilds the
        /// open Inspectors.
        /// </summary>
        public static bool Enabled
        {
            get
            {
                if (!loaded)
                {
                    enabled = EditorPrefs.GetBool(EnabledKey, true);
                    loaded = true;
                }

                return enabled;
            }

            set
            {
                if (Enabled == value)
                {
                    return;
                }

                enabled = value;
                EditorPrefs.SetBool(EnabledKey, value);
                ActiveEditorTracker.sharedTracker.ForceRebuild();
            }
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            return VeneerSettingsPage.Provider(
                "Project/Veneer",
                "Veneer",
                VeneerKitBrand.Value,
                "Veneer",
                "Editor kit settings",
                Build,
                SettingsScope.Project,
                new[] { "veneer", "inspector", "layout", "theme", "designer" });
        }

        private static void Build(VeneerSettingsPage page)
        {
            VeneerBrand brand = page.Brand;

            VisualElement switchCard = VeneerStyle.Card("Inspector layouts", "layers", brand);
            VisualElement row = VeneerStyle.Row(true);
            row.Add(VeneerStyle.Chip("Veneer layouts", "check", Enabled, on => Enabled = on, brand));
            switchCard.Add(row);
            switchCard.Add(VeneerStyle.Hint(
                "On: scripts that have a layout (made in the Veneer Inspector Designer) are drawn with it. Off: every script shows Unity's plain Inspector again. "
                + "Scripts with their own custom editor are not affected either way.",
                "info",
                brand));
            page.Content.Add(switchCard);

            VisualElement listCard = VeneerStyle.Card("Layouts in this project", "list", brand);
            var list = new VisualElement();
            listCard.Add(list);
            FillList(list, brand);

            // The list follows the registry while the page is open and lets go of it when the page closes.
            Action refresh = () => FillList(list, brand);
            listCard.RegisterCallback<AttachToPanelEvent>(evt => VeneerLayoutRegistry.Changed += refresh);
            listCard.RegisterCallback<DetachFromPanelEvent>(evt => VeneerLayoutRegistry.Changed -= refresh);
            page.Content.Add(listCard);
        }

        private static void FillList(VisualElement list, VeneerBrand brand)
        {
            list.Clear();
            System.Collections.Generic.IReadOnlyList<VeneerLayout> layouts = VeneerLayoutRegistry.All;
            if (layouts.Count == 0)
            {
                list.Add(VeneerStyle.MutedLabel("No layouts yet. Make one in Tools > Veneer > Inspector Designer."));
                return;
            }

            list.Add(VeneerStyle.MutedLabel("Styled scripts (" + layouts.Count + (layouts.Count == 1 ? " layout):" : " layouts):")));
            VisualElement buttons = VeneerStyle.Row(true);
            for (int i = 0; i < layouts.Count; i++)
            {
                VeneerLayout layout = layouts[i];
                if (layout == null)
                {
                    continue;
                }

                Type type = layout.ResolveType();
                string name = type != null ? type.Name : "(script missing)";
                buttons.Add(VeneerStyle.Button(name, "file", "Select " + layout.name, () => Select(layout), false, brand));
            }

            list.Add(buttons);
        }

        private static void Select(VeneerLayout layout)
        {
            if (layout == null)
            {
                return;
            }

            Selection.activeObject = layout;
            EditorGUIUtility.PingObject(layout);
        }
    }
}
