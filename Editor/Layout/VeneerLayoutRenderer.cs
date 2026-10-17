using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// Draws a <see cref="VeneerLayout"/> with the kit's <see cref="VeneerInspectorBuilder"/>: the header (with the banner picture as its
    /// background, or a separate banner strip above it), the tabs, the cards and the fields in them, then an automatic "Other" card for
    /// every visible field the layout did not place. A field is never lost:
    /// it is either in a card, in the Other card, or in the layout's hidden list. Everything is built once; the only work afterwards is
    /// the builder's own data-change tracking of the show-if rules.
    /// </summary>
    public static class VeneerLayoutRenderer
    {
        private const int MaxNesting = 3;
        private const string OtherTitle = "Other";
        private const string TitleRowClass = "vn-card__title";
        private const string BaseFieldLabelClass = "unity-base-field__label";

        /// <summary>
        /// Renders <paramref name="layout"/> into the builder's current container. Call it between creating the builder and
        /// <see cref="VeneerInspectorBuilder.Finish"/>. When the layout has its own accent the layout's brand and colour class are applied
        /// to the builder's root.
        /// </summary>
        /// <param name="b">The builder (its root should come from <see cref="VeneerStyle.Root"/>).</param>
        /// <param name="layout">The layout to draw.</param>
        /// <param name="so">The data the fields edit; it should be the builder's own <see cref="VeneerInspectorBuilder.SerializedObject"/>. Null uses that one.</param>
        public static void Build(VeneerInspectorBuilder b, VeneerLayout layout, SerializedObject so)
        {
            if (b == null)
            {
                throw new ArgumentNullException(nameof(b));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (so == null)
            {
                so = b.SerializedObject;
            }

            if (layout.UseAccent)
            {
                b.Brand(VeneerLayoutTheme.BrandFor(layout));
            }

            // The borrowed system look or custom theme, then the layout's own accent last (this also adds the layout's class when UseAccent is on).
            VeneerLayoutTheme.ApplyTo(b.Root, layout);

            // Header badges (a state pill...) are custom items that sit in the layout like any entry but are drawn at the right of the header.
            List<Badge> badges = layout.ShowHeader ? CollectBadges(layout, so) : null;

            VeneerLayoutBanner banner = layout.Banner;
            Texture2D picture = BannerPicture(layout);
            if (picture != null && layout.ShowHeader && banner.OverHeader)
            {
                // One box: the picture is the header's background and the icon and texts sit on it.
                AddBannerHeader(b, layout, so, banner, picture, badges);
            }
            else
            {
                AddBanner(b, banner, picture);
                AddHeader(b, layout, so, badges);
            }

            // Items placed between the header and the tabs (outside them, always visible).
            AddSlotItems(b, layout, so, VeneerCustomItemSlot.AfterHeader);

            // Paths that sit in a card, so the Other card knows what is left and a path used twice is drawn once.
            var placed = new HashSet<string>();
            List<VeneerLayoutPage> pages = layout.Pages;
            int pageCount = pages == null ? 0 : pages.Count;
            bool othersInLastPage = layout.ShowOthers && layout.OtherInLastPage;
            bool othersDone = false;
            if (pageCount >= 2)
            {
                AddTabs(b, layout, so, pages, placed, othersInLastPage);
                othersDone = othersInLastPage;
            }
            else if (pageCount == 1 && pages[0] != null)
            {
                // A single page has no tab bar; a show-if rule on it hides its content as a whole.
                VisualElement host = null;
                if (!string.IsNullOrEmpty(pages[0].ShowIfPath))
                {
                    host = new VisualElement();
                    b.Add(host);
                    AddShowIf(b, so, host, pages[0].ShowIfPath, pages[0].ShowIf, pages[0].ShowIfValue);
                }

                using (host != null ? b.Into(host) : default)
                {
                    AddBlocks(b, layout, so, pages[0], placed);
                    if (othersInLastPage)
                    {
                        AddClaims(layout, so, placed);
                        AddOthers(b, layout, so, placed);
                        othersDone = true;
                    }
                }
            }

            if (layout.ShowOthers && !othersDone)
            {
                AddClaims(layout, so, placed);
                AddOthers(b, layout, so, placed);
            }

            AddSlotItems(b, layout, so, VeneerCustomItemSlot.Footer);
        }

        // ------------------------------------------------------------------ after-header and footer items

        // Custom items of one slot (AfterHeader, Footer) that the layout has an entry for, drawn in registration order into one container
        // added to the current container, outside the tabs.
        private static void AddSlotItems(VeneerInspectorBuilder b, VeneerLayout layout, SerializedObject so, VeneerCustomItemSlot slot)
        {
            Type type = TargetTypeOf(so);
            if (type == null || layout.Pages == null)
            {
                return;
            }

            VisualElement area = null;
            List<VeneerCustomItem> items = VeneerCustomItems.For(type);
            for (int i = 0; i < items.Count; i++)
            {
                VeneerCustomItem item = items[i];
                VeneerLayoutEntry entry = item.Slot == slot ? FindCustomEntry(layout, item.Id) : null;
                if (entry == null)
                {
                    continue;
                }

                if (area == null)
                {
                    area = new VisualElement();
                    b.Add(area);
                }

                using (b.Into(area))
                {
                    AddCustomItem(b, layout, so, entry, item);
                }
            }

            if (area != null && area.childCount == 0)
            {
                area.RemoveFromHierarchy();
            }
        }

        // The property paths that custom items of this layout are responsible for count as placed, so the Other card skips them.
        private static void AddClaims(VeneerLayout layout, SerializedObject so, HashSet<string> placed)
        {
            Type type = TargetTypeOf(so);
            if (type == null || layout.Pages == null)
            {
                return;
            }

            List<VeneerCustomItem> items = VeneerCustomItems.For(type);
            for (int i = 0; i < items.Count; i++)
            {
                IReadOnlyList<string> claims = items[i].Claims;
                if (claims == null || claims.Count == 0 || FindCustomEntry(layout, items[i].Id) == null)
                {
                    continue;
                }

                for (int c = 0; c < claims.Count; c++)
                {
                    if (!string.IsNullOrEmpty(claims[c]))
                    {
                        placed.Add(claims[c]);
                    }
                }
            }
        }

        private static VeneerLayoutEntry FindCustomEntry(VeneerLayout layout, string id)
        {
            List<VeneerLayoutPage> pages = layout.Pages;
            for (int p = 0; p < pages.Count; p++)
            {
                List<VeneerLayoutBlock> blocks = pages[p] != null ? pages[p].Blocks : null;
                for (int k = 0; blocks != null && k < blocks.Count; k++)
                {
                    List<VeneerLayoutEntry> entries = blocks[k] != null ? blocks[k].Entries : null;
                    for (int e = 0; entries != null && e < entries.Count; e++)
                    {
                        if (entries[e] != null && entries[e].IsCustom && entries[e].CustomId == id)
                        {
                            return entries[e];
                        }
                    }
                }
            }

            return null;
        }

        // ------------------------------------------------------------------ banner and header

        // The picture of the banner: the theme's (or system's) banner when UseThemePicture is on, else the layout's own Image. Null when the
        // banner is off or has no picture (no picture = no banner).
        private static Texture2D BannerPicture(VeneerLayout layout)
        {
            VeneerLayoutBanner banner = layout.Banner;
            if (banner == null || !banner.Enabled)
            {
                return null;
            }

            Texture2D picture = banner.UseThemePicture ? VeneerLayoutTheme.ThemeBanner(layout) : banner.Image;
            return picture != null ? picture : null;
        }

        // The header icon picture: the theme's (or system's) logo when UseThemeLogo is on and there is one, else the user's own icon
        // picture; null when neither is set (the icon name, if any, is used then).
        private static Texture2D HeaderIconPicture(VeneerLayout layout)
        {
            if (layout.UseThemeLogo)
            {
                Texture2D logo = VeneerLayoutTheme.ThemeLogo(layout);
                if (logo != null)
                {
                    return logo;
                }
            }

            return layout.HeaderIconTexture != null ? layout.HeaderIconTexture : null;
        }

        // The banner as its own strip above the header (used when it is not drawn over the header, or there is no header).
        // FullWidth, Width and Align only matter here.
        private static void AddBanner(VeneerInspectorBuilder b, VeneerLayoutBanner banner, Texture2D picture)
        {
            if (banner == null || picture == null)
            {
                return;
            }

            var box = new VisualElement();
            box.pickingMode = PickingMode.Ignore;
            box.style.height = Mathf.Max(8f, banner.Height);
            box.style.flexShrink = 0f;
            box.style.marginBottom = 6f;
            if (banner.FullWidth)
            {
                box.style.alignSelf = Align.Stretch;
            }
            else
            {
                box.style.width = Mathf.Max(8f, banner.Width);
                box.style.maxWidth = Length.Percent(100f);
                box.style.alignSelf = banner.Align == VeneerBannerAlign.Center
                    ? Align.Center
                    : banner.Align == VeneerBannerAlign.Right ? Align.FlexEnd : Align.FlexStart;
            }

            var image = new Image
            {
                image = picture,
                scaleMode = ToScaleMode(banner.Fit),
                tintColor = banner.Tint,
            };
            image.pickingMode = PickingMode.Ignore;
            image.style.flexGrow = 1f;
            box.Add(image);
            b.Add(box);
        }

        // The plain header card of the kit, with the user's own icon picture put in when there is one.
        private static void AddHeader(VeneerInspectorBuilder b, VeneerLayout layout, SerializedObject so, List<Badge> badges)
        {
            if (!layout.ShowHeader)
            {
                return;
            }

            string title = ResolveTitle(layout, so);
            string subtitle = ResolveSubtitle(layout, so);
            Texture2D iconPicture = HeaderIconPicture(layout);
            if (iconPicture != null)
            {
                // The theme's logo or the user's own picture wins over the kit icon name; the builder only knows names, so it is put in the header row here.
                VisualElement card = b.Header(title, subtitle, null);
                if (card.childCount > 0)
                {
                    card[0].Insert(0, TextureIcon(iconPicture, "vn-icon--lg"));
                }
            }
            else
            {
                b.Header(title, subtitle, EmptyToNull(layout.HeaderIcon));
            }

            // The right end of the header card's top line is where the kit's own state pills go too.
            AddHeaderBadges(b, layout, so, b.HeaderRight, badges);
        }

        // ------------------------------------------------------------------ header badges

        // A header-badge custom item the layout has an entry for.
        private readonly struct Badge
        {
            public readonly VeneerLayoutEntry Entry;
            public readonly VeneerCustomItem Item;

            public Badge(VeneerLayoutEntry entry, VeneerCustomItem item)
            {
                Entry = entry;
                Item = item;
            }
        }

        // The registered header-badge items that the layout has an entry for (once per id); null when there are none. A badge the layout
        // does not list is not drawn: the user may have deleted it.
        private static List<Badge> CollectBadges(VeneerLayout layout, SerializedObject so)
        {
            Type type = TargetTypeOf(so);
            List<Badge> badges = null;
            List<VeneerLayoutPage> pages = layout.Pages;
            for (int p = 0; pages != null && p < pages.Count; p++)
            {
                List<VeneerLayoutBlock> blocks = pages[p] != null ? pages[p].Blocks : null;
                for (int k = 0; blocks != null && k < blocks.Count; k++)
                {
                    List<VeneerLayoutEntry> entries = blocks[k] != null ? blocks[k].Entries : null;
                    for (int e = 0; entries != null && e < entries.Count; e++)
                    {
                        VeneerLayoutEntry entry = entries[e];
                        if (entry == null || !entry.IsCustom)
                        {
                            continue;
                        }

                        VeneerCustomItem item = VeneerCustomItems.Find(type, entry.CustomId);
                        if (item == null || item.Slot != VeneerCustomItemSlot.HeaderBadge || ContainsBadge(badges, item))
                        {
                            continue;
                        }

                        if (badges == null)
                        {
                            badges = new List<Badge>(2);
                        }

                        badges.Add(new Badge(entry, item));
                    }
                }
            }

            return badges;
        }

        private static bool ContainsBadge(List<Badge> badges, VeneerCustomItem item)
        {
            for (int i = 0; badges != null && i < badges.Count; i++)
            {
                if (badges[i].Item == item)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddHeaderBadges(VeneerInspectorBuilder b, VeneerLayout layout, SerializedObject so, VisualElement container, List<Badge> badges)
        {
            if (badges == null || container == null)
            {
                return;
            }

            using (b.Into(container))
            {
                for (int i = 0; i < badges.Count; i++)
                {
                    AddCustomItem(b, layout, so, badges[i].Entry, badges[i].Item);
                }
            }
        }

        // The header drawn over the banner picture: a card-like box (border, rounded corners, clipped) as tall as the banner, the picture as
        // its background, a flat dark overlay of alpha Dim, and the icon, title and subtitle on top in white whatever the skin.
        private static void AddBannerHeader(
            VeneerInspectorBuilder b, VeneerLayout layout, SerializedObject so, VeneerLayoutBanner banner, Texture2D picture, List<Badge> badges)
        {
            float height = Mathf.Max(24f, banner.Height);
            var box = new VisualElement();
            box.AddToClassList("vn-card");
            box.pickingMode = PickingMode.Ignore;
            box.style.height = height;
            box.style.flexShrink = 0f;
            box.style.paddingTop = 0f;
            box.style.paddingBottom = 0f;
            box.style.paddingLeft = 0f;
            box.style.paddingRight = 0f;
            box.style.overflow = Overflow.Hidden;
            box.style.backgroundImage = new StyleBackground(picture);
            ApplyBackgroundFit(box, banner.Fit);
            box.style.unityBackgroundImageTintColor = banner.Tint;

            float dim = Mathf.Clamp01(banner.Dim);
            if (dim > 0f)
            {
                var overlay = new VisualElement();
                overlay.pickingMode = PickingMode.Ignore;
                overlay.style.position = Position.Absolute;
                overlay.style.left = 0f;
                overlay.style.top = 0f;
                overlay.style.right = 0f;
                overlay.style.bottom = 0f;
                overlay.style.marginBottom = 0f;
                overlay.style.backgroundColor = new Color(0f, 0f, 0f, dim);
                box.Add(overlay);
            }

            var content = new VisualElement();
            content.pickingMode = PickingMode.Ignore;
            content.style.flexGrow = 1f;
            content.style.flexDirection = FlexDirection.Row;
            content.style.alignItems = Align.Center;
            content.style.justifyContent = Justify.FlexStart;
            content.style.paddingLeft = 14f;
            content.style.paddingRight = 14f;
            content.style.marginBottom = 0f;
            box.Add(content);

            VisualElement icon = null;
            Texture2D iconPicture = HeaderIconPicture(layout);
            if (iconPicture != null)
            {
                icon = TextureIcon(iconPicture, null);
            }
            else if (!string.IsNullOrEmpty(layout.HeaderIcon))
            {
                icon = VeneerStyle.IconElement(layout.HeaderIcon, null, b.CurrentBrand);
                icon.style.unityBackgroundImageTintColor = Color.white;
            }

            if (icon != null)
            {
                float size = Mathf.Clamp(banner.IconSize, 8f, Mathf.Max(8f, height - 8f));
                icon.style.width = size;
                icon.style.height = size;
                icon.style.minWidth = size;
                icon.style.marginRight = 12f;
                content.Add(icon);
            }

            var text = new VisualElement();
            text.pickingMode = PickingMode.Ignore;
            text.style.flexShrink = 1f;
            text.style.minWidth = 0f;
            text.style.justifyContent = Justify.Center;
            content.Add(text);

            var title = new Label(ResolveTitle(layout, so));
            StyleOnBanner(title, 22f, Color.white);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            text.Add(title);

            string subtitleText = ResolveSubtitle(layout, so);
            if (subtitleText != null)
            {
                var subtitle = new Label(subtitleText);
                StyleOnBanner(subtitle, 12f, new Color(1f, 1f, 1f, 0.78f));
                subtitle.style.marginTop = 2f;
                text.Add(subtitle);
            }

            if (badges != null && badges.Count > 0)
            {
                // The texts take the free width, so the badges sit at the right end of the banner.
                text.style.flexGrow = 1f;
                VisualElement right = VeneerStyle.Row();
                right.style.flexShrink = 0f;
                right.style.alignItems = Align.Center;
                content.Add(right);
                AddHeaderBadges(b, layout, so, right, badges);
            }

            b.Add(box);
        }

        private static void StyleOnBanner(Label label, float fontSize, Color color)
        {
            label.pickingMode = PickingMode.Ignore;
            label.style.fontSize = fontSize;
            label.style.color = color;
            label.style.marginTop = 0f;
            label.style.marginBottom = 0f;
            label.style.marginLeft = 0f;
            label.style.marginRight = 0f;
            label.style.paddingTop = 0f;
            label.style.paddingBottom = 0f;
            label.style.paddingLeft = 0f;
            label.style.paddingRight = 0f;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
        }

        // Cover and Fit keep the aspect ratio (crop or letterbox); Stretch fills the box. Centred, never tiled. The older
        // unityBackgroundScaleMode is deprecated in Unity 6, so the background-* properties are used.
        private static void ApplyBackgroundFit(VisualElement element, VeneerBannerFit fit)
        {
            switch (fit)
            {
                case VeneerBannerFit.Fit:
                    element.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                    break;
                case VeneerBannerFit.Stretch:
                    element.style.backgroundSize = new BackgroundSize(Length.Percent(100f), Length.Percent(100f));
                    break;
                default:
                    element.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
                    break;
            }

            element.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            element.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            element.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
        }

        private static ScaleMode ToScaleMode(VeneerBannerFit fit)
        {
            switch (fit)
            {
                case VeneerBannerFit.Fit:
                    return ScaleMode.ScaleToFit;
                case VeneerBannerFit.Stretch:
                    return ScaleMode.StretchToFill;
                default:
                    return ScaleMode.ScaleAndCrop;
            }
        }

        private static string ResolveTitle(VeneerLayout layout, SerializedObject so)
        {
            if (!string.IsNullOrEmpty(layout.Title))
            {
                return layout.Title;
            }

            UnityEngine.Object first = so.targetObject;
            return first != null ? ObjectNames.NicifyVariableName(first.GetType().Name) : "Inspector";
        }

        private static string ResolveSubtitle(VeneerLayout layout, SerializedObject so)
        {
            string subtitle = EmptyToNull(layout.Subtitle);
            if (subtitle == null && so.targetObjects.Length > 1)
            {
                subtitle = so.targetObjects.Length + " objects selected";
            }

            return subtitle;
        }

        // ------------------------------------------------------------------ tabs, cards

        private static void AddTabs(
            VeneerInspectorBuilder b, VeneerLayout layout, SerializedObject so, List<VeneerLayoutPage> pages, HashSet<string> placed, bool othersInLastPage)
        {
            var tabs = new (string label, string icon)[pages.Count];
            for (int i = 0; i < pages.Count; i++)
            {
                VeneerLayoutPage page = pages[i];
                if (page == null)
                {
                    tabs[i] = (string.Empty, null);
                    continue;
                }

                tabs[i] = (page.Title ?? string.Empty, page.IconTexture != null ? null : EmptyToNull(page.Icon));
            }

            string key = "Veneer.Layout." + SessionId(layout);
            VisualElement[] containers = b.Tabs(key, tabs);

            // Picture icons: the tab bar is the first child of the view that holds the pages.
            VisualElement view = containers.Length > 0 ? containers[0].parent : null;
            VisualElement bar = view != null && view.childCount > 0 ? view[0] : null;
            for (int i = 0; i < pages.Count; i++)
            {
                VeneerLayoutPage page = pages[i];
                if (page != null && page.IconTexture != null && bar != null && i < bar.childCount)
                {
                    bar[i].Insert(0, TextureIcon(page.IconTexture, null));
                }
            }

            for (int i = 0; i < pages.Count; i++)
            {
                if (pages[i] == null)
                {
                    continue;
                }

                using (b.Into(containers[i]))
                {
                    AddBlocks(b, layout, so, pages[i], placed);
                }
            }

            // The automatic Other card ends the last tab (all tabs are drawn by now, so the placed paths are known).
            if (othersInLastPage)
            {
                using (b.Into(containers[pages.Count - 1]))
                {
                    AddClaims(layout, so, placed);
                    AddOthers(b, layout, so, placed);
                }
            }

            AddPageRules(b, so, pages, bar, containers, key);
        }

        // Show-if rules of tabs. A tab whose rule is false loses its button and its content; when it was the selected tab the first visible
        // tab is selected; when no tab is visible the tab bar is hidden too. The tab view switches pages by itself on a click, so the rule
        // only has to fix the selection when it hides the selected tab.
        private static void AddPageRules(
            VeneerInspectorBuilder b, SerializedObject so, List<VeneerLayoutPage> pages, VisualElement bar, VisualElement[] containers, string key)
        {
            if (bar == null)
            {
                return;
            }

            var rules = new ShowIfRule[pages.Count];
            bool any = false;
            for (int i = 0; i < pages.Count; i++)
            {
                VeneerLayoutPage page = pages[i];
                if (page != null)
                {
                    rules[i] = MakeRule(so, page.ShowIfPath, page.ShowIf, page.ShowIfValue);
                    any |= rules[i] != null;
                }
            }

            if (any)
            {
                var visibility = new PageVisibility(rules, bar, containers, key);
                b.VisibleWhen(bar, visibility.Evaluate, false);
            }
        }

        // Applies the show-if rules of the tabs to the tab bar and the pages (see AddPageRules). Allocates nothing per evaluation.
        private sealed class PageVisibility
        {
            private readonly ShowIfRule[] rules;
            private readonly bool[] visible;
            private readonly VisualElement bar;
            private readonly VisualElement[] pages;
            private readonly string key;

            public PageVisibility(ShowIfRule[] rules, VisualElement bar, VisualElement[] pages, string key)
            {
                this.rules = rules;
                this.bar = bar;
                this.pages = pages;
                this.key = key;
                visible = new bool[rules.Length];
            }

            // Returns whether the tab bar shows (at least one tab is visible).
            public bool Evaluate()
            {
                int first = -1;
                for (int i = 0; i < rules.Length; i++)
                {
                    bool show = rules[i] == null || rules[i].IsVisible();
                    visible[i] = show;
                    if (i < bar.childCount)
                    {
                        VeneerStyle.SetDisplay(bar[i], show);
                    }

                    if (show && first < 0)
                    {
                        first = i;
                    }
                }

                int selected = VeneerStyle.GetSegmentedSelected(bar);
                if (selected >= 0 && selected < visible.Length && visible[selected])
                {
                    return true;
                }

                // The selected tab is gone (or none was selected): move to the first visible one, or show nothing.
                VeneerStyle.SetSegmentedSelected(bar, first);
                if (first >= 0)
                {
                    SessionState.SetInt(key, first);
                }

                for (int i = 0; i < pages.Length; i++)
                {
                    VeneerStyle.SetDisplay(pages[i], i == first);
                }

                return first >= 0;
            }
        }

        private static void AddBlocks(
            VeneerInspectorBuilder b, VeneerLayout layout, SerializedObject so, VeneerLayoutPage page, HashSet<string> placed)
        {
            List<VeneerLayoutBlock> blocks = page.Blocks;
            if (blocks == null)
            {
                return;
            }

            for (int i = 0; i < blocks.Count; i++)
            {
                VeneerLayoutBlock block = blocks[i];
                if (block == null)
                {
                    continue;
                }

                bool titled = block.ShowTitle;
                string title = titled ? EmptyToNull(block.Title) : null;
                string iconName = titled && block.IconTexture == null ? EmptyToNull(block.Icon) : null;
                VisualElement card = b.Card(title, iconName);
                if (titled && block.IconTexture != null)
                {
                    AddCardIcon(card, block.IconTexture);
                }

                AddShowIf(b, so, card, block.ShowIfPath, block.ShowIf, block.ShowIfValue);
                int drawn = 0;
                using (b.Into(card))
                {
                    // SameRow: the previous joinable element and the wrapping row it was moved into (null until a SameRow entry joins it).
                    VisualElement previous = null;
                    VisualElement row = null;
                    List<VeneerLayoutEntry> entries = block.Entries;
                    for (int e = 0; entries != null && e < entries.Count; e++)
                    {
                        VeneerLayoutEntry entry = entries[e];
                        if (entry == null)
                        {
                            continue;
                        }

                        bool joinable = IsJoinable(so, entry);
                        bool join = entry.SameRow && joinable && previous != null;
                        if (join && row == null)
                        {
                            // The previous element and this one share a new wrapping row, put where the previous one was.
                            row = VeneerStyle.Row(true);
                            card.Insert(card.IndexOf(previous), row);
                            row.Add(previous);
                        }
                        else if (!join)
                        {
                            row = null;
                        }

                        VisualElement target = join ? row : card;
                        int before = target.childCount;
                        bool drew;
                        using (b.Into(target))
                        {
                            drew = AddItem(b, layout, so, entry, placed);
                        }

                        if (drew)
                        {
                            drawn++;
                        }

                        // Only a single element can be moved into a row later. An entry that drew nothing leaves the chain as it was.
                        int added = target.childCount - before;
                        if (added == 1 && joinable)
                        {
                            previous = target[target.childCount - 1];
                        }
                        else if (added > 0 || drew)
                        {
                            previous = null;
                        }
                    }
                }

                // A card whose fields are all hidden or gone would be an empty box in the Inspector.
                if (drawn == 0 && block.Entries != null && block.Entries.Count > 0)
                {
                    card.RemoveFromHierarchy();
                }
            }
        }

        // One entry of a card, drawn into the current container. Returns true when it counts as drawn.
        private static bool AddItem(VeneerInspectorBuilder b, VeneerLayout layout, SerializedObject so, VeneerLayoutEntry entry, HashSet<string> placed)
        {
            if (entry.IsNote)
            {
                // A note counts as drawn even while its show-if rule hides it: the rule is live, the card must stay.
                return AddNote(b, so, entry);
            }

            if (entry.IsCustom)
            {
                // A missing id is skipped silently (the system may have dropped the item); header badges and footer items are drawn elsewhere.
                VeneerCustomItem item = VeneerCustomItems.Find(TargetTypeOf(so), entry.CustomId);
                return item != null && item.Slot == VeneerCustomItemSlot.Item && AddCustomItem(b, layout, so, entry, item);
            }

            // An item with no path that is not a note has nothing to draw.
            if (string.IsNullOrEmpty(entry.Path) || layout.IsHidden(entry.Path) || !placed.Add(entry.Path))
            {
                return false;
            }

            SerializedProperty property = so.FindProperty(entry.Path);
            return property != null && AddEntry(b, layout, so, property, entry);
        }

        // Items that may share a row (toggle chips and buttons): a custom item of the card slot, or a bool shown as a toggle chip. A
        // SameRow flag on anything else is ignored and the item is drawn normally.
        private static bool IsJoinable(SerializedObject so, VeneerLayoutEntry entry)
        {
            if (entry.IsNote)
            {
                return false;
            }

            if (entry.IsCustom)
            {
                VeneerCustomItem item = VeneerCustomItems.Find(TargetTypeOf(so), entry.CustomId);
                return item != null && item.Slot == VeneerCustomItemSlot.Item;
            }

            if (string.IsNullOrEmpty(entry.Path) || (entry.Widget != VeneerWidget.Auto && entry.Widget != VeneerWidget.Toggle))
            {
                return false;
            }

            SerializedProperty property = so.FindProperty(entry.Path);
            return property != null && property.propertyType == SerializedPropertyType.Boolean;
        }

        private static void AddCardIcon(VisualElement card, Texture2D texture)
        {
            VisualElement titleRow = card.childCount > 0 && card[0].ClassListContains(TitleRowClass) ? card[0] : null;
            if (titleRow == null)
            {
                titleRow = new VisualElement();
                titleRow.AddToClassList(TitleRowClass);
                card.Insert(0, titleRow);
            }

            titleRow.Insert(0, TextureIcon(texture, null));
        }

        // The Other card: every visible top-level property that is not placed and not hidden. A property with some placed or hidden
        // descendants is not shown whole: only its remaining leaf fields are (so a nested field the layout placed is never drawn twice and
        // one it did not place is never lost).
        private static void AddOthers(VeneerInspectorBuilder b, VeneerLayout layout, SerializedObject so, HashSet<string> placed)
        {
            List<SerializedProperty> others = null;
            SerializedProperty iterator = so.GetIterator();
            bool enter = true;
            while (iterator.NextVisible(enter))
            {
                enter = false;
                string path = iterator.propertyPath;
                if (path == "m_Script" || placed.Contains(path) || layout.IsHidden(path))
                {
                    continue;
                }

                if (others == null)
                {
                    others = new List<SerializedProperty>(8);
                }

                if (IsNestedStruct(iterator) && HasClaimedDescendant(layout, placed, path))
                {
                    CollectLeaves(layout, placed, iterator, others);
                }
                else
                {
                    others.Add(iterator.Copy());
                }
            }

            if (others == null || others.Count == 0)
            {
                return;
            }

            VisualElement card = b.Card(string.IsNullOrEmpty(layout.OtherTitle) ? OtherTitle : layout.OtherTitle, null);
            using (b.Into(card))
            {
                for (int i = 0; i < others.Count; i++)
                {
                    AddOther(b, layout, so, others[i]);
                }
            }
        }

        // True when a placed or hidden path lies below <path> (it starts with "path.").
        private static bool HasClaimedDescendant(VeneerLayout layout, HashSet<string> placed, string path)
        {
            string prefix = path + ".";
            foreach (string claimed in placed)
            {
                if (claimed.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            List<string> hidden = layout.HiddenPaths;
            for (int i = 0; hidden != null && i < hidden.Count; i++)
            {
                if (hidden[i] != null && hidden[i].StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // The unplaced, non-hidden leaf fields below <parent>, going through nested serializable structs but not into arrays or object
        // references. A nested struct that is itself placed or hidden is skipped whole.
        private static void CollectLeaves(VeneerLayout layout, HashSet<string> placed, SerializedProperty parent, List<SerializedProperty> into)
        {
            SerializedProperty child = parent.Copy();
            SerializedProperty end = parent.GetEndProperty();
            bool enter = true;
            while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end))
            {
                enter = false;
                string path = child.propertyPath;
                if (placed.Contains(path) || layout.IsHidden(path))
                {
                    continue;
                }

                if (IsNestedStruct(child))
                {
                    CollectLeaves(layout, placed, child, into);
                }
                else
                {
                    into.Add(child.Copy());
                }
            }
        }

        // One property of the Other card: a custom widget when one is registered for its path, else the automatic widget.
        private static void AddOther(VeneerInspectorBuilder b, VeneerLayout layout, SerializedObject so, SerializedProperty property)
        {
            if (TryAddCustomWidget(b, layout, so, property, null, out _))
            {
                return;
            }

            AddAuto(b, property, null, null, 0, null, null);
        }

        // ------------------------------------------------------------------ custom items and widgets

        private static Type TargetTypeOf(SerializedObject so)
        {
            UnityEngine.Object first = so.targetObject;
            return first != null ? first.GetType() : null;
        }

        // Builds a registered custom item into the current container. With a show-if rule the item is built inside a plain host element
        // (the rule needs one element to hide, and an item may add several through the builder). Returns false when it drew nothing.
        private static bool AddCustomItem(VeneerInspectorBuilder b, VeneerLayout layout, SerializedObject so, VeneerLayoutEntry entry, VeneerCustomItem item)
        {
            if (item.Build == null)
            {
                return false;
            }

            var context = new VeneerCustomItemContext(b, layout, entry);
            return AddHosted(b, so, entry, false, "Custom item '" + item.Id + "'", () => item.Build(context));
        }

        // A custom widget for the property when one is registered for its path. Returns true when a widget is registered (it handled the
        // property, even if it drew nothing: <drawn> says that). <entry> may be null (the Other card).
        private static bool TryAddCustomWidget(
            VeneerInspectorBuilder b, VeneerLayout layout, SerializedObject so, SerializedProperty property, VeneerLayoutEntry entry, out bool drawn)
        {
            drawn = false;
            if (!VeneerCustomItems.TryGetWidget(TargetTypeOf(so), property.propertyPath, out Func<VeneerCustomItemContext, SerializedProperty, VisualElement> build))
            {
                return false;
            }

            var context = new VeneerCustomItemContext(b, layout, entry);
            bool readOnly = entry != null && entry.ReadOnly;
            drawn = AddHosted(b, so, entry, readOnly, "Custom widget '" + property.propertyPath + "'", () => build(context, property));
            return true;
        }

        // Runs <build> with the current container as its target. The result element (when it is not parented yet) is added. When the entry has
        // a show-if rule, or the widget is read-only, everything goes into a host element that carries the rule and the enabled state.
        private static bool AddHosted(
            VeneerInspectorBuilder b, SerializedObject so, VeneerLayoutEntry entry, bool readOnly, string what, Func<VisualElement> build)
        {
            bool wrap = readOnly || (entry != null && !string.IsNullOrEmpty(entry.ShowIfPath));
            VisualElement host = null;
            if (wrap)
            {
                host = new VisualElement();
                b.Add(host);
            }

            VisualElement target = wrap ? host : b.Current;
            int before = target.childCount;
            using (wrap ? b.Into(host) : default)
            {
                try
                {
                    VisualElement result = build();
                    if (result != null && result.parent == null)
                    {
                        b.Add(result);
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    b.Hint(what + " failed: " + exception.Message, "error");
                }
            }

            if (!wrap)
            {
                return target.childCount > before;
            }

            if (host.childCount == 0)
            {
                host.RemoveFromHierarchy();
                return false;
            }

            if (readOnly)
            {
                host.SetEnabled(false);
            }

            AddShowIf(b, so, host, entry);
            return true;
        }

        // ------------------------------------------------------------------ one entry

        private static bool AddEntry(VeneerInspectorBuilder b, VeneerLayout layout, SerializedObject so, SerializedProperty property, VeneerLayoutEntry entry)
        {
            if (TryAddCustomWidget(b, layout, so, property, entry, out bool customDrawn))
            {
                return customDrawn;
            }

            VisualElement element = AddWidget(b, property, entry);
            if (element == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(entry.Tooltip))
            {
                bool composite = element.ClassListContains("vn-card") || (property.isArray && property.propertyType != SerializedPropertyType.String);
                OverrideTooltip(element, entry.Tooltip, !composite);
            }

            if (entry.ReadOnly)
            {
                element.SetEnabled(false);
            }

            AddShowIf(b, so, element, entry);
            return true;
        }

        // A note is free text with line breaks kept: a message box (the kit's Hint: info, warn or error), or for kind Text a plain muted
        // label like the Combat studio's ClipNote. Empty text draws nothing.
        private static bool AddNote(VeneerInspectorBuilder b, SerializedObject so, VeneerLayoutEntry entry)
        {
            if (string.IsNullOrEmpty(entry.NoteText))
            {
                return false;
            }

            string text = entry.NoteText.IndexOf('\r') >= 0 ? entry.NoteText.Replace("\r\n", "\n").Replace('\r', '\n') : entry.NoteText;
            if (entry.NoteKind == VeneerNoteKind.Heading)
            {
                // The kit's section title: small muted capitals, no box.
                VisualElement heading = b.Section(text);
                AddShowIf(b, so, heading, entry);
                return true;
            }

            if (entry.NoteKind == VeneerNoteKind.Text)
            {
                // Plain muted text: no box, border or icon; it wraps and sits between the field rows.
                Label note = VeneerStyle.MutedLabel(text);
                note.style.whiteSpace = WhiteSpace.Normal;
                note.style.marginTop = 2f;
                note.style.marginBottom = 6f;
                b.Add(note);
                AddShowIf(b, so, note, entry);
                return true;
            }

            string kind = entry.NoteKind == VeneerNoteKind.Warning ? "warn" : entry.NoteKind == VeneerNoteKind.Error ? "error" : "info";
            VisualElement hint = b.Hint(text, kind);
            AddShowIf(b, so, hint, entry);
            return true;
        }

        // A widget that does not fit the field's type falls back to Auto: a field is never dropped because of a wrong choice.
        private static VisualElement AddWidget(VeneerInspectorBuilder b, SerializedProperty property, VeneerLayoutEntry entry)
        {
            string path = property.propertyPath;
            string label = EmptyToNull(entry.Label);
            string unit = EmptyToNull(entry.Unit);
            string left = EmptyToNull(entry.SliderLeft);
            string right = EmptyToNull(entry.SliderRight);
            SerializedPropertyType type = property.propertyType;
            bool numeric = type == SerializedPropertyType.Float || type == SerializedPropertyType.Integer;

            // Icons given for an enum make it a segmented group even when the widget was left on Auto.
            VeneerWidget widget = entry.Widget;
            if (widget == VeneerWidget.Auto && type == SerializedPropertyType.Enum && !string.IsNullOrEmpty(entry.EnumIcons))
            {
                widget = VeneerWidget.Enum;
            }

            // A bool with a chip icon, or one that shares a row, is a toggle chip even when the widget was left on Auto.
            if (widget == VeneerWidget.Auto && type == SerializedPropertyType.Boolean && (!string.IsNullOrEmpty(entry.Icon) || entry.SameRow))
            {
                widget = VeneerWidget.Toggle;
            }

            // A range chosen in the layout makes a plain number a clamped number row (or a slider when the field has [Range]).
            if (widget == VeneerWidget.Auto && entry.HasRange && numeric)
            {
                widget = VeneerFieldInfo.TryGetRange(property, out _, out _) ? VeneerWidget.Slider : VeneerWidget.Number;
            }

            switch (widget)
            {
                case VeneerWidget.Number:
                    if (numeric)
                    {
                        VisualElement number = b.Number(path, label, unit);
                        if (entry.HasRange)
                        {
                            ClampToRange(number, property, entry.Min, entry.Max);
                        }

                        return number;
                    }

                    break;
                case VeneerWidget.Slider:
                    if (numeric)
                    {
                        float low = entry.HasRange ? entry.Min : float.NaN;
                        float high = entry.HasRange ? entry.Max : float.NaN;
                        VisualElement slider = b.Slider(path, label, left, right, low, high, unit);
                        if (entry.HasRange)
                        {
                            ClampToRange(slider, property, entry.Min, entry.Max);
                        }

                        return slider;
                    }

                    break;
                case VeneerWidget.MinMax:
                    if (type == SerializedPropertyType.Vector2)
                    {
                        float low = entry.HasRange ? entry.Min : 0f;
                        float high = entry.HasRange ? entry.Max : 1f;
                        return b.Add(VeneerControls.MinMaxRow(property, label, low, high, left, right, unit));
                    }

                    break;
                case VeneerWidget.Tag:
                    if (type == SerializedPropertyType.String)
                    {
                        return b.Add(CreateTagRow(property, label));
                    }

                    break;
                case VeneerWidget.Toggle:
                    if (type == SerializedPropertyType.Boolean)
                    {
                        return b.Chip(path, label, EmptyToNull(entry.Icon));
                    }

                    break;
                case VeneerWidget.Enum:
                    if (type == SerializedPropertyType.Enum)
                    {
                        Type enumType = VeneerFieldInfo.GetValueType(property);
                        bool flags = enumType != null && Attribute.IsDefined(enumType, typeof(FlagsAttribute), false);
                        return flags ? b.Flags(path) : b.Enum(path, ParseIcons(entry.EnumIcons), null, label);
                    }

                    break;
                case VeneerWidget.Dropdown:
                    if (type == SerializedPropertyType.Enum)
                    {
                        return b.Dropdown(path, label);
                    }

                    break;
                case VeneerWidget.Object:
                    if (type == SerializedPropertyType.ObjectReference)
                    {
                        return b.Object(path, label);
                    }

                    break;
                case VeneerWidget.List:
                    if (property.isArray && type != SerializedPropertyType.String)
                    {
                        return b.List(path, label);
                    }

                    break;
                case VeneerWidget.Vector:
                    if (type == SerializedPropertyType.Vector2 || type == SerializedPropertyType.Vector3)
                    {
                        return b.Vector3(path, label);
                    }

                    break;
                case VeneerWidget.Color:
                    if (type == SerializedPropertyType.Color)
                    {
                        return b.Color(path, label);
                    }

                    break;
                case VeneerWidget.Curve:
                    if (type == SerializedPropertyType.AnimationCurve)
                    {
                        return b.Curve(path, label);
                    }

                    break;
            }

            return AddAuto(b, property, label, unit, 0, left, right);
        }

        // Keeps what is typed into the number field of a slider or number row inside Min..Max (a value outside is written back clamped).
        // The row's own [Min]/[Range] clamp stays; this one is the layout's range.
        private static void ClampToRange(VisualElement row, SerializedProperty property, float min, float max)
        {
            if (max < min)
            {
                return;
            }

            SerializedProperty tracked = property.Copy();
            FloatField floatField = row.Q<FloatField>();
            if (floatField != null)
            {
                floatField.RegisterValueChangedCallback(evt =>
                {
                    float clamped = Mathf.Clamp(evt.newValue, min, max);
                    if (clamped != evt.newValue)
                    {
                        tracked.floatValue = clamped;
                        tracked.serializedObject.ApplyModifiedProperties();
                    }
                });
                return;
            }

            IntegerField integerField = row.Q<IntegerField>();
            if (integerField != null)
            {
                integerField.RegisterValueChangedCallback(evt =>
                {
                    int clamped = Mathf.Clamp(evt.newValue, Mathf.CeilToInt(min), Mathf.FloorToInt(max));
                    if (clamped != evt.newValue)
                    {
                        tracked.intValue = clamped;
                        tracked.serializedObject.ApplyModifiedProperties();
                    }
                });
                return;
            }

            DoubleField doubleField = row.Q<DoubleField>();
            if (doubleField != null)
            {
                doubleField.RegisterValueChangedCallback(evt =>
                {
                    double clamped = Math.Min(Math.Max(evt.newValue, min), max);
                    if (clamped != evt.newValue)
                    {
                        tracked.doubleValue = clamped;
                        tracked.serializedObject.ApplyModifiedProperties();
                    }
                });
            }
        }

        // "arrow-right, sphere" becomes { "arrow-right", "sphere" }; an empty entry stays null so that value shows its name. Null for no text.
        private static string[] ParseIcons(string icons)
        {
            if (string.IsNullOrWhiteSpace(icons))
            {
                return null;
            }

            string[] parts = icons.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = EmptyToNull(parts[i].Trim());
            }

            return parts;
        }

        // The origin-tag style row: a tag dropdown (TagField) bound to a string property, wrapped in a kit row for the label and the prefab mark.
        private static VisualElement CreateTagRow(SerializedProperty property, string label)
        {
            property = property.Copy();
            var field = new TagField { tooltip = property.tooltip };
            field.BindProperty(property);
            VisualElement row = VeneerStyle.FieldRow(label ?? property.displayName, field, property.tooltip, out Label labelElement);
            VeneerBinding.Hook(row, property, labelElement, row, null);
            return row;
        }

        // The same choice as VeneerAutoInspector.AddProperty, plus a label and a unit from the layout (and slider end captions).
        private static VisualElement AddAuto(
            VeneerInspectorBuilder b, SerializedProperty property, string label, string unit, int depth, string sliderLeft, string sliderRight)
        {
            string path = property.propertyPath;
            if (property.isArray && property.propertyType != SerializedPropertyType.String)
            {
                return b.List(path, label);
            }

            if (depth < MaxNesting && IsNestedStruct(property))
            {
                return AddNested(b, property, label, depth);
            }

            if (property.propertyType == SerializedPropertyType.Float || property.propertyType == SerializedPropertyType.Integer)
            {
                if (VeneerFieldInfo.TryGetRange(property, out _, out _))
                {
                    return b.Slider(path, label, sliderLeft, sliderRight, float.NaN, float.NaN, unit);
                }

                VeneerFieldInfo.GetLimits(property, out float min, out _);
                if (!float.IsNegativeInfinity(min) || !string.IsNullOrEmpty(unit))
                {
                    return b.Number(path, label, unit);
                }
            }

            return b.Field(path, label);
        }

        private static VisualElement AddNested(VeneerInspectorBuilder b, SerializedProperty property, string label, int depth)
        {
            VisualElement nested = b.Card(label ?? property.displayName, null);
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
                    AddAuto(b, child.Copy(), null, null, depth + 1, null, null);
                }
            }

            return nested;
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

        // ------------------------------------------------------------------ tooltips, icons, show-if

        // Unity puts a field's attribute tooltip on inner elements once the field is built, so the override is pushed down after the first layout.
        private static void OverrideTooltip(VisualElement element, string text, bool deep)
        {
            element.tooltip = text;
            if (!deep)
            {
                return;
            }

            EventCallback<GeometryChangedEvent> callback = null;
            callback = evt =>
            {
                element.UnregisterCallback(callback);
                PushTooltip(element, text);
            };
            element.RegisterCallback(callback);
        }

        private static void PushTooltip(VisualElement parent, string text)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                VisualElement child = parent[i];
                if (!string.IsNullOrEmpty(child.tooltip) || child.ClassListContains(BaseFieldLabelClass))
                {
                    child.tooltip = text;
                }

                PushTooltip(child, text);
            }
        }

        // The user's own picture: shown with its own colours (the kit's icon tint is switched off) and fitted into the icon box.
        private static VisualElement TextureIcon(Texture2D texture, string sizeClass)
        {
            var icon = new VisualElement();
            icon.AddToClassList("vn-icon");
            if (!string.IsNullOrEmpty(sizeClass))
            {
                icon.AddToClassList(sizeClass);
            }

            icon.pickingMode = PickingMode.Ignore;
            icon.style.backgroundImage = new StyleBackground(texture);
            icon.style.unityBackgroundImageTintColor = Color.white;
            return icon;
        }

        private static void AddShowIf(VeneerInspectorBuilder b, SerializedObject so, VisualElement element, VeneerLayoutEntry entry)
        {
            AddShowIf(b, so, element, entry.ShowIfPath, entry.ShowIf, entry.ShowIfValue);
        }

        // Hides <element> while the field at <path> fails the test; a live rule of the builder. An empty path adds nothing.
        private static void AddShowIf(
            VeneerInspectorBuilder b, SerializedObject so, VisualElement element, string path, VeneerCondition condition, string value)
        {
            ShowIfRule rule = MakeRule(so, path, condition, value);
            if (rule != null)
            {
                b.VisibleWhen(element, rule.IsVisible, false);
            }
        }

        // The parsed rule, or null when there is no rule, the watched field is gone (a rule on a field that is gone never hides anything) or
        // the rule cannot be evaluated for its type.
        private static ShowIfRule MakeRule(SerializedObject so, string path, VeneerCondition condition, string value)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            SerializedProperty other = so.FindProperty(path);
            return other == null ? null : ShowIfRule.Create(other, condition, value);
        }

        private static string EmptyToNull(string text)
        {
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private static string SessionId(VeneerLayout layout)
        {
            string guid = VeneerLayoutTheme.GuidOf(layout);
            if (guid.Length > 0)
            {
                return guid;
            }

            // A system's in-memory default layout has no asset: its type name is stable across reloads (an instance id is not).
            return !string.IsNullOrEmpty(layout.TypeName)
                ? layout.TypeName
                : layout.GetInstanceID().ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// One "show only when" test on a field or a note, parsed once. The value text is read by the type of the watched field (bool:
        /// true/false; numbers; enum by name or index; text). IsNull, IsEmpty and NotEmpty work on object references and managed
        /// references (assigned or not), text (empty or not) and lists/arrays (size 0 or not; IsNull is not defined for text and lists).
        /// A rule that cannot be understood or evaluated for the watched type never hides the item. Evaluating allocates nothing.
        /// </summary>
        private sealed class ShowIfRule
        {
            private const double Epsilon = 1e-6;

            private readonly SerializedProperty property;
            private readonly VeneerCondition condition;
            private readonly double number;
            private readonly string text;

            private ShowIfRule(SerializedProperty property, VeneerCondition condition, double number, string text)
            {
                this.property = property;
                this.condition = condition;
                this.number = number;
                this.text = text;
            }

            public static ShowIfRule Create(SerializedProperty watched, VeneerCondition condition, string value)
            {
                bool needsValue = condition == VeneerCondition.Equals || condition == VeneerCondition.NotEquals
                    || condition == VeneerCondition.Greater || condition == VeneerCondition.Less;
                bool emptiness = condition == VeneerCondition.IsNull || condition == VeneerCondition.IsEmpty || condition == VeneerCondition.NotEmpty;
                string raw = value ?? string.Empty;
                double number = 0d;

                // A list or array: only the size can be tested.
                if (watched.isArray && watched.propertyType != SerializedPropertyType.String)
                {
                    return condition == VeneerCondition.IsEmpty || condition == VeneerCondition.NotEmpty
                        ? new ShowIfRule(watched.Copy(), condition, 0d, raw)
                        : null;
                }

                switch (watched.propertyType)
                {
                    case SerializedPropertyType.Boolean:
                        if (emptiness || (needsValue && !TryParseBool(raw, out number)))
                        {
                            return null;
                        }

                        break;
                    case SerializedPropertyType.LayerMask:
                        // A layer mask is its int bit mask: it is "empty" when no layer is selected (0).
                        if (condition == VeneerCondition.IsNull || (needsValue && !TryParseNumber(raw, out number)))
                        {
                            return null;
                        }

                        break;
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.Float:
                        if (emptiness || (needsValue && !TryParseNumber(raw, out number)))
                        {
                            return null;
                        }

                        break;
                    case SerializedPropertyType.Enum:
                        if (emptiness || (needsValue && !TryParseEnum(watched, raw, out number)))
                        {
                            return null;
                        }

                        break;
                    case SerializedPropertyType.String:
                        if (condition == VeneerCondition.IsNull)
                        {
                            return null;
                        }

                        break;
                    case SerializedPropertyType.ObjectReference:
                        break;
                    case SerializedPropertyType.ManagedReference:
                        // Only "is something assigned" can be asked of a managed reference.
                        if (!emptiness && condition != VeneerCondition.NotNull)
                        {
                            return null;
                        }

                        break;
                    default:
                        return null;
                }

                return new ShowIfRule(watched.Copy(), condition, number, raw);
            }

            public bool IsVisible()
            {
                if (property.hasMultipleDifferentValues)
                {
                    return true;
                }

                if (property.isArray && property.propertyType != SerializedPropertyType.String)
                {
                    bool empty = property.arraySize == 0;
                    return condition == VeneerCondition.IsEmpty ? empty : !empty;
                }

                switch (property.propertyType)
                {
                    case SerializedPropertyType.ManagedReference:
                        return CompareAssigned(property.managedReferenceId != UnityEngine.Serialization.ManagedReferenceUtility.RefIdNull);
                    case SerializedPropertyType.Boolean:
                        return Compare(property.boolValue ? 1d : 0d);
                    case SerializedPropertyType.Integer:
                        return Compare(property.longValue);
                    case SerializedPropertyType.LayerMask:
                        return Compare(property.intValue);
                    case SerializedPropertyType.Float:
                        return Compare(property.doubleValue);
                    case SerializedPropertyType.Enum:
                        return Compare(property.enumValueIndex);
                    case SerializedPropertyType.String:
                        return CompareText(property.stringValue);
                    case SerializedPropertyType.ObjectReference:
                        return CompareObject(property.objectReferenceValue);
                    default:
                        return true;
                }
            }

            private bool Compare(double value)
            {
                switch (condition)
                {
                    case VeneerCondition.Equals:
                        return Math.Abs(value - number) < Epsilon;
                    case VeneerCondition.NotEquals:
                        return Math.Abs(value - number) >= Epsilon;
                    case VeneerCondition.Greater:
                        return value > number;
                    case VeneerCondition.Less:
                        return value < number;
                    case VeneerCondition.IsFalse:
                    case VeneerCondition.IsEmpty:
                        // IsEmpty is only reached by a layer mask (no layer selected = 0).
                        return Math.Abs(value) < Epsilon;
                    default:
                        // IsTrue, NotNull and NotEmpty: a non-zero number counts as true.
                        return Math.Abs(value) >= Epsilon;
                }
            }

            private bool CompareText(string value)
            {
                string current = value ?? string.Empty;
                switch (condition)
                {
                    case VeneerCondition.Equals:
                        return string.Equals(current, text, StringComparison.Ordinal);
                    case VeneerCondition.NotEquals:
                        return !string.Equals(current, text, StringComparison.Ordinal);
                    case VeneerCondition.Greater:
                        return string.CompareOrdinal(current, text) > 0;
                    case VeneerCondition.Less:
                        return string.CompareOrdinal(current, text) < 0;
                    case VeneerCondition.IsFalse:
                    case VeneerCondition.IsEmpty:
                        return current.Length == 0;
                    default:
                        // IsTrue, NotNull and NotEmpty.
                        return current.Length > 0;
                }
            }

            // Managed references: IsNull and IsEmpty show when nothing is assigned, the other tests when something is.
            private bool CompareAssigned(bool assigned)
            {
                return condition == VeneerCondition.IsNull || condition == VeneerCondition.IsEmpty ? !assigned : assigned;
            }

            private bool CompareObject(UnityEngine.Object value)
            {
                bool assigned = value != null;
                bool matchesNull = text.Length == 0 || string.Equals(text, "null", StringComparison.OrdinalIgnoreCase);
                switch (condition)
                {
                    case VeneerCondition.Equals:
                        return matchesNull ? !assigned : assigned && string.Equals(value.name, text, StringComparison.Ordinal);
                    case VeneerCondition.NotEquals:
                        return matchesNull ? assigned : !assigned || !string.Equals(value.name, text, StringComparison.Ordinal);
                    case VeneerCondition.IsFalse:
                    case VeneerCondition.IsNull:
                    case VeneerCondition.IsEmpty:
                        return !assigned;
                    case VeneerCondition.Greater:
                    case VeneerCondition.Less:
                        return true;
                    default:
                        return assigned;
                }
            }

            private static bool TryParseBool(string raw, out double number)
            {
                string value = raw.Trim();
                if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("on", StringComparison.OrdinalIgnoreCase))
                {
                    number = 1d;
                    return true;
                }

                if (value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0" || value.Equals("off", StringComparison.OrdinalIgnoreCase))
                {
                    number = 0d;
                    return true;
                }

                number = 0d;
                return false;
            }

            private static bool TryParseNumber(string raw, out double number)
            {
                // A decimal comma is accepted so a value typed on a localised keyboard still works.
                return double.TryParse(raw.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
            }

            private static bool TryParseEnum(SerializedProperty property, string raw, out double number)
            {
                string value = raw.Trim();
                string[] names = property.enumNames;
                string[] display = property.enumDisplayNames;
                for (int i = 0; i < names.Length; i++)
                {
                    if (string.Equals(names[i], value, StringComparison.OrdinalIgnoreCase)
                        || (i < display.Length && string.Equals(display[i], value, StringComparison.OrdinalIgnoreCase)))
                    {
                        number = i;
                        return true;
                    }
                }

                return TryParseNumber(value, out number);
            }
        }
    }
}
