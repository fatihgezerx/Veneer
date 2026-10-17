using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// Base class of a studio-styled tool window. It builds the root (kit sheet, brand), a title bar with the logo, the title, a state pill
    /// and toolbar buttons, a content area, and a standard empty state that replaces the content when there is nothing to show. Derive,
    /// override <see cref="BuildContent"/> (and optionally <see cref="BuildToolbar"/>), and open the window as usual. Schedulers made with
    /// <see cref="Every"/> are paused when the window's tree leaves the panel and stopped when it is rebuilt.
    /// </summary>
    public abstract class VeneerWindow : EditorWindow
    {
        private readonly List<IVisualElementScheduledItem> schedules = new List<IVisualElementScheduledItem>(2);
        private VisualElement empty;
        private Label emptyTitle;
        private Label emptyText;
        private Label pill;

        /// <summary>The brand of the window (accent, logo, icon folder); null uses the default look.</summary>
        protected virtual VeneerBrand Brand => null;

        /// <summary>The title shown in the title bar and on the window tab. Defaults to the window class name split into words.</summary>
        protected virtual string WindowTitle => ObjectNames.NicifyVariableName(GetType().Name);

        /// <summary>The root element of the window's tree (flex-grow 1, kit sheet applied); null before <c>CreateGUI</c>.</summary>
        protected VisualElement RootElement { get; private set; }

        /// <summary>The area <see cref="BuildContent"/> fills.</summary>
        protected VisualElement Content { get; private set; }

        /// <summary>The toolbar at the right of the title bar, for icon buttons.</summary>
        protected VisualElement Toolbar { get; private set; }

        /// <summary>Builds the window's tree (called by Unity when the window opens or the domain reloads).</summary>
        public void CreateGUI()
        {
            DisposeUi();
            VeneerBrand brand = Brand;
            titleContent = new GUIContent(WindowTitle, brand != null ? brand.Logo : null);

            RootElement = VeneerStyle.Root(brand, "vn-window");
            RootElement.style.flexGrow = 1f;
            rootVisualElement.style.flexGrow = 1f;
            rootVisualElement.Add(RootElement);

            RootElement.Add(BuildTitleBar(brand));

            empty = VeneerStyle.EmptyState("info", string.Empty, string.Empty, out emptyTitle, out emptyText, brand);
            VeneerStyle.SetDisplay(empty, false);
            RootElement.Add(empty);

            Content = new VisualElement();
            Content.style.flexGrow = 1f;
            Content.style.flexShrink = 1f;
            RootElement.Add(Content);

            BuildToolbar(Toolbar);
            BuildContent(Content);
        }

        /// <summary>Adds the window's content to <paramref name="content"/>. Called every time the tree is built.</summary>
        protected abstract void BuildContent(VisualElement content);

        /// <summary>Adds icon buttons (<see cref="VeneerStyle.IconButton"/>) to the title bar's toolbar. Optional.</summary>
        protected virtual void BuildToolbar(VisualElement toolbar)
        {
        }

        /// <summary>Shows or changes the title bar's state pill (hidden until first used); kinds are <c>info</c>, <c>ok</c>, <c>idle</c>, <c>warn</c>, <c>error</c>, <c>special</c>.</summary>
        protected void SetPill(string text, string kind)
        {
            if (pill == null)
            {
                return;
            }

            VeneerStyle.SetDisplay(pill, true);
            VeneerStyle.SetPill(pill, kind, text);
        }

        /// <summary>Replaces the content with the standard empty state (a big icon, a title and a text).</summary>
        /// <param name="icon">Icon name; null or empty shows no picture.</param>
        protected void ShowEmptyState(string icon, string title, string text)
        {
            if (empty == null)
            {
                return;
            }

            VisualElement picture = empty[0];
            VeneerStyle.SetDisplay(picture, !string.IsNullOrEmpty(icon));
            if (!string.IsNullOrEmpty(icon))
            {
                VeneerStyle.SetIcon(picture, icon, Brand);
            }

            emptyTitle.text = title;
            emptyText.text = text;
            VeneerStyle.SetDisplay(empty, true);
            VeneerStyle.SetDisplay(Content, false);
        }

        /// <summary>Shows the content again after <see cref="ShowEmptyState"/>.</summary>
        protected void HideEmptyState()
        {
            if (empty == null)
            {
                return;
            }

            VeneerStyle.SetDisplay(empty, false);
            VeneerStyle.SetDisplay(Content, true);
        }

        /// <summary>
        /// Runs <paramref name="action"/> every <paramref name="milliseconds"/> while the window's tree is on a panel. It is paused when the
        /// tree leaves the panel and dropped when the tree is rebuilt; prefer 200 ms or slower.
        /// </summary>
        protected void Every(long milliseconds, Action action)
        {
            IVisualElementScheduledItem item = RootElement.schedule.Execute(action).Every(milliseconds);
            schedules.Add(item);
            if (schedules.Count == 1)
            {
                RootElement.RegisterCallback<AttachToPanelEvent>(OnAttached);
                RootElement.RegisterCallback<DetachFromPanelEvent>(OnDetached);
            }
        }

        private VisualElement BuildTitleBar(VeneerBrand brand)
        {
            var bar = new VisualElement();
            bar.AddToClassList("vn-titlebar");
            Texture2D logo = brand != null ? brand.Logo : null;
            if (logo != null)
            {
                var logoElement = new VisualElement();
                logoElement.AddToClassList("vn-titlebar__logo");
                logoElement.pickingMode = PickingMode.Ignore;
                logoElement.style.backgroundImage = new StyleBackground(logo);
                bar.Add(logoElement);
            }

            Label title = VeneerStyle.Title(WindowTitle);
            title.style.fontSize = 15f;
            title.AddToClassList("vn-ellipsis");
            bar.Add(title);

            pill = VeneerStyle.Pill(string.Empty, "info");
            VeneerStyle.SetDisplay(pill, false);
            bar.Add(pill);
            bar.Add(VeneerStyle.Spacer());

            Toolbar = new VisualElement();
            Toolbar.AddToClassList("vn-row");
            bar.Add(Toolbar);
            return bar;
        }

        private void OnAttached(AttachToPanelEvent evt)
        {
            for (int i = 0; i < schedules.Count; i++)
            {
                schedules[i].Resume();
            }
        }

        private void OnDetached(DetachFromPanelEvent evt)
        {
            for (int i = 0; i < schedules.Count; i++)
            {
                schedules[i].Pause();
            }
        }

        private void DisposeUi()
        {
            for (int i = 0; i < schedules.Count; i++)
            {
                schedules[i].Pause();
            }

            schedules.Clear();
            if (RootElement != null && RootElement.parent != null)
            {
                RootElement.RemoveFromHierarchy();
            }

            RootElement = null;
            Content = null;
            Toolbar = null;
            empty = null;
            emptyTitle = null;
            emptyText = null;
            pill = null;
        }
    }
}
