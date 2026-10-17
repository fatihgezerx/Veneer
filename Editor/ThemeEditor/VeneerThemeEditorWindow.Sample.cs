using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    // Centre pane: the live sample. It is built on a root made by the real VeneerStyle.Root(brand), so the theme the engine applies is
    // exactly what is shown here. Its controls are bound to a hidden temporary ScriptableObject that is destroyed with the window.
    public sealed partial class VeneerThemeEditorWindow
    {
        private const string SampleTabKey = "Veneer.ThemeEditor.SampleTab";
        private const long SampleDelayMs = 150;

        private static readonly string[] SampleTabLabels = { "General", "Lists", "Limits", "States" };
        private static readonly string[] SampleTabIcons = { "settings", "list", "lock", "eye" };
        private static readonly string[] ShapeIcons = { "sphere", "cube", "target" };
        private static readonly string[] ShapeTips = { "Sphere", "Box", "Capsule" };

        private ScrollView sampleScroll;
        private VisualElement sampleHost;
        private VisualElement sampleRoot;
        private Label sampleCaption;
        private VeneerThemeSample sampleObject;
        private SerializedObject sampleSo;
        private bool samplePending;
        private Action sampleAction;

        private VisualElement BuildSamplePane()
        {
            var pane = new VisualElement();
            pane.AddToClassList("vnte-pane");
            pane.AddToClassList("vnte-pane--centre");
            pane.style.minWidth = SampleWidthMin;
            VisualElement title = VeneerStyle.Section("Live sample");
            title.AddToClassList("vnte-pane__title");
            pane.Add(title);

            sampleCaption = VeneerStyle.MutedLabel(string.Empty);
            sampleCaption.AddToClassList("vnte-sample__caption");
            pane.Add(sampleCaption);

            sampleScroll = NewPaneScroll("vnte-sample__scroll");
            sampleHost = new VisualElement();
            sampleHost.AddToClassList("vnte-sample__host");
            sampleScroll.Add(sampleHost);
            pane.Add(sampleScroll);
            return pane;
        }

        private void ResetSampleState()
        {
            // The old tree is gone with its elements; only the session objects remain and are reused.
            sampleScroll = null;
            sampleHost = null;
            sampleRoot = null;
            sampleCaption = null;
            samplePending = false;
        }

        // Throttled: the first request schedules one rebuild 150 ms later, requests in between ride along.
        private void ScheduleSample()
        {
            if (samplePending || RootElement == null || sampleHost == null)
            {
                return;
            }

            samplePending = true;
            if (sampleAction == null)
            {
                sampleAction = RebuildSample;
            }

            RootElement.schedule.Execute(sampleAction).StartingIn(SampleDelayMs);
        }

        private void ClearSample()
        {
            if (sampleRoot != null)
            {
                sampleRoot.Unbind();
                sampleRoot.RemoveFromHierarchy();
                sampleRoot = null;
            }

            if (sampleHost != null)
            {
                sampleHost.Clear();
            }
        }

        private void DestroySample()
        {
            if (sampleSo != null)
            {
                sampleSo.Dispose();
                sampleSo = null;
            }

            if (sampleObject != null)
            {
                DestroyImmediate(sampleObject);
            }

            sampleObject = null;
        }

        // The sample object is made on first use (after a domain reload the field is null again, so it is simply made anew).
        private bool EnsureSample()
        {
            if (sampleObject == null)
            {
                DestroySample();
                sampleObject = CreateInstance<VeneerThemeSample>();
                sampleObject.hideFlags = HideFlags.HideAndDontSave;
            }

            if (sampleSo == null && sampleObject != null)
            {
                sampleSo = new SerializedObject(sampleObject);
            }

            return sampleSo != null;
        }

        private void RebuildSample()
        {
            samplePending = false;
            if (RootElement == null || sampleHost == null)
            {
                return;
            }

            ClearSample();
            if (selected == null)
            {
                return;
            }

            if (IsCustomSelected)
            {
                sampleCaption.text = "How a script that chooses this theme looks. A custom theme is not tied to a system: the scripts whose layout chooses it in the Inspector Designer get this look.";
            }
            else if (IsGlobalSelected)
            {
                sampleCaption.text = "How every system's editors look with the global theme (a system's own theme still wins over it).";
            }
            else
            {
                sampleCaption.text = "How " + selected.Name + "'s Inspectors, windows and settings pages look. This is the real kit, drawn as the system's editors draw it.";
            }

            if (!EnsureSample())
            {
                return;
            }

            try
            {
                sampleSo.Update();
                VisualElement root = IsCustomSelected ? BuildCustomRoot() : VeneerStyle.Root(selected);
                root.AddToClassList("vnte-sample__root");
                FillSample(root, selected);
                root.Bind(sampleSo);
                sampleRoot = root;
                sampleHost.Add(root);
            }
            catch (Exception exception)
            {
                ClearSample();
                sampleHost.Add(VeneerStyle.Hint("The sample failed: " + exception.Message, "error", Brand));
            }
        }

        // The sample root of a custom theme: the default look with the global theme, then this theme on top (its sheets and classes, and its
        // tab style and motion instead of the global ones), the way a script that chooses the theme gets it.
        private VisualElement BuildCustomRoot()
        {
            VisualElement root = VeneerStyle.Root(selected);
            VeneerStyle.ApplyTheme(root, theme);
            return root;
        }

        // Header, a motion hint, four tabs and the widgets of the kit, in the order an Inspector uses them.
        private void FillSample(VisualElement root, VeneerBrand brand)
        {
            string title = IsCustomSelected ? (theme != null ? DisplayName(theme) : "Custom theme") : IsGlobalSelected ? "Global theme" : brand.Name;
            string subtitle = string.IsNullOrEmpty(brand.Subtitle) || !IsSystemSelected ? "Sample Inspector" : brand.Subtitle;
            root.Add(VeneerStyle.Banner(brand, title, subtitle));

            // The root carries the motion class of the effective theme, so the hover, press and dropdown animations below are the real ones.
            Label motionHint = VeneerStyle.MutedLabel("Hover and press the controls to feel the motion");
            motionHint.AddToClassList("vnte-sample__hint");
            root.Add(motionHint);

            VisualElement view = VeneerStyle.TabView(SampleTabKey, SampleTabLabels, SampleTabIcons, null, brand, out VisualElement[] pages, out _);
            root.Add(view);

            // Tab 1: the everyday controls: numbers, a dropdown, segmented choices, chips, sliders, buttons, hints and bars.
            VisualElement shape = VeneerStyle.Card("Shape", "cube", brand);
            shape.Add(VeneerControls.NumberRow(sampleSo.FindProperty("Size"), "Radius", "m"));
            shape.Add(VeneerControls.EnumDropdown(sampleSo.FindProperty("ColliderType"), "Collider", brand));
            shape.Add(VeneerControls.EnumSegmented(sampleSo.FindProperty("Mode"), null, null, "Mode", brand));
            shape.Add(VeneerControls.EnumSegmented(sampleSo.FindProperty("Shape"), ShapeIcons, ShapeTips, "Shape", brand));
            VisualElement chips = VeneerStyle.Row(true);
            chips.Add(VeneerControls.ToggleChip(sampleSo.FindProperty("UseGravity"), "Use gravity", "bolt", brand));
            chips.Add(VeneerControls.ToggleChip(sampleSo.FindProperty("Locked"), "Locked", "lock", brand));
            shape.Add(chips);
            shape.Add(VeneerControls.ObjectSlot(sampleSo.FindProperty("Picture"), "Picture", typeof(Texture2D), brand));
            pages[0].Add(shape);

            VisualElement ranges = VeneerStyle.Card("Ranges", "sliders", brand);
            ranges.Add(VeneerControls.SliderRow(sampleSo.FindProperty("Bounce"), "Bounciness", "Dead", "Bouncy"));
            ranges.Add(VeneerControls.SliderRow(sampleSo.FindProperty("Mass"), "Mass", "Light", "Heavy", float.NaN, float.NaN, "kg"));
            ranges.Add(VeneerControls.MinMaxRow(sampleSo.FindProperty("Angle"), "Angle", 0f, 180f, "Flat", "Steep", "deg"));
            pages[0].Add(ranges);

            VisualElement actions = VeneerStyle.Card("Actions", "bolt", brand);
            VisualElement buttons = VeneerStyle.Row(true);
            buttons.Add(VeneerStyle.Button("Apply", "check", "A primary button.", null, true, brand));
            buttons.Add(VeneerStyle.Button("Cancel", "close", "A normal button.", null, false, brand));
            buttons.Add(VeneerStyle.Button("Reset", "refresh", "Another normal button.", null, false, brand));
            buttons.Add(VeneerStyle.IconButton("settings", "An icon button.", null, brand));
            buttons.Add(VeneerStyle.IconButton("trash", "Another icon button.", null, brand));
            actions.Add(buttons);
            pages[0].Add(actions);

            VisualElement messages = VeneerStyle.Card("Messages", "info", brand);
            messages.Add(VeneerStyle.Hint("Information: how this setting works.", "info", brand));
            messages.Add(VeneerStyle.Hint("Warning: this value is unusually high.", "warn", brand));
            messages.Add(VeneerStyle.Hint("Error: a required reference is missing.", "error", brand));
            pages[0].Add(messages);

            VisualElement load = VeneerStyle.Card("Load", "graph", brand);
            VisualElement kpis = VeneerStyle.Row();
            kpis.Add(VeneerStyle.Kpi("Awake", "play", out Label awake, brand));
            kpis.Add(VeneerStyle.Kpi("Steps", "step", out Label steps, brand));
            awake.text = "128";
            steps.text = "4.2";
            load.Add(kpis);
            VisualElement fill = VeneerStyle.Bar();
            VeneerStyle.SetBarFill(fill, 0.62f);
            load.Add(fill);
            VisualElement high = VeneerStyle.Bar("warn");
            VeneerStyle.SetBarFill(high, 0.85f);
            high.style.marginTop = 6f;
            load.Add(high);
            pages[0].Add(load);

            // Tab 2: a list.
            VisualElement list = VeneerStyle.Card("Attacks", "list", brand);
            list.Add(VeneerControls.ListEditor(sampleSo.FindProperty("Items"), "Attack list", brand));
            pages[1].Add(list);

            // Tab 3: a small card, to show a tab with little in it.
            VisualElement limits = VeneerStyle.Card("Limits", "lock", brand);
            limits.Add(VeneerControls.NumberRow(sampleSo.FindProperty("Count"), "Max count"));
            limits.Add(VeneerControls.ToggleChip(sampleSo.FindProperty("Locked"), "Locked", "lock", brand));
            pages[2].Add(limits);

            // Tab 4: every control in its states, side by side.
            pages[3].Add(BuildStatesCard(brand));
        }
    }
}
