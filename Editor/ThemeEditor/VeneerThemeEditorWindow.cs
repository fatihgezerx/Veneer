using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// The Theme Editor: a window where the look of a system's editors (or of all of them) is changed without code, and where reusable
    /// custom themes are made. The left pane lists the global base, every system's brand (the ready-made looks) and the custom themes, the
    /// centre shows a live sample built through the kit's real path so it is exactly what the Inspectors, windows and settings pages will
    /// look like, and the right pane edits the selected <see cref="VeneerTheme"/>: each setting has an Override checkbox and its control.
    /// A system's or the global theme asset is created silently when the first setting is overridden. Every edit goes through a
    /// <c>SerializedObject</c> (Undo works) and is saved automatically a moment after the last change.
    /// </summary>
    /// <remarks>
    /// Split over several files: <c>.List.cs</c> (left pane), <c>.Sample.cs</c> (live sample), <c>.States.cs</c> (the sample's States
    /// card), <c>.Properties.cs</c> (right pane) and <c>.Custom.cs</c> (making, renaming and deleting custom themes).
    /// </remarks>
    public sealed partial class VeneerThemeEditorWindow : VeneerWindow
    {
        private const string MenuPath = "Tools/Veneer/Theme Editor";
        private const string ThemeFolder = "Assets/Veneer Themes";
        private const string SheetRelativePath = "/ThemeEditor/ThemeEditor.uss";
        private const long SaveDelayMs = 400;

        // Window and pane geometry. The two side panes have a preferred width the user can drag between a minimum and a maximum; the
        // centre pane (the live sample) takes everything that is left, so a bigger window only ever makes the centre bigger.
        private const float WindowMinWidth = 900f;
        private const float WindowMinHeight = 560f;
        private const float ListWidthDefault = 240f;
        private const float ListWidthMin = 200f;
        private const float ListWidthMax = 320f;
        private const float PropsWidthDefault = 340f;
        private const float PropsWidthMin = 300f;
        private const float PropsWidthMax = 460f;
        private const float SampleWidthMin = 360f;

        // A custom theme is selected by the GUID of its asset: the key is this prefix plus the GUID (a rename or a move keeps it).
        private const string CustomPrefix = "@theme:";

        // State that must survive a domain reload (the window is serialized by Unity). Empty = the global theme.
        [SerializeField] private string selectedBrand = string.Empty;
        [SerializeField] private float listWidth = ListWidthDefault;
        [SerializeField] private float propsWidth = PropsWidthDefault;

        // Session objects, rebuilt by ResolveTheme and released in OnDisable, so nothing survives a reload.
        private VeneerTheme theme;
        private SerializedObject themeSo;
        private VeneerBrand selected;

        private bool saveNeeded;
        private bool savePending;
        private double lastEditTime;
        private Action saveAction;

        /// <inheritdoc />
        protected override VeneerBrand Brand => VeneerKitBrand.Value;

        protected override string WindowTitle => "Theme Editor";

        // This window edits themes: it listens to VeneerThemeRegistry.Changed itself, so it keeps its selection, focus and any open colour
        // picker (a rebuilt tree would cut the picker off from its field).
        /// <inheritdoc />
        protected override bool RebuildOnThemeChange => false;

        private bool IsGlobalSelected => string.IsNullOrEmpty(selectedBrand);

        private bool IsCustomSelected => !string.IsNullOrEmpty(selectedBrand) && selectedBrand.StartsWith(CustomPrefix, StringComparison.Ordinal);

        private bool IsSystemSelected => !IsGlobalSelected && !IsCustomSelected;

        /// <summary>Opens the Theme Editor.</summary>
        [MenuItem(MenuPath)]
        public static void Open()
        {
            var window = GetWindow<VeneerThemeEditorWindow>();
            window.minSize = new Vector2(WindowMinWidth, WindowMinHeight);
            window.Show();
        }

        /// <summary>Opens the Theme Editor with one system selected.</summary>
        /// <param name="brandName">The <see cref="VeneerBrand.Name"/> of the system; null or empty selects the global theme.</param>
        public static void Open(string brandName)
        {
            var window = GetWindow<VeneerThemeEditorWindow>();
            window.minSize = new Vector2(WindowMinWidth, WindowMinHeight);
            window.Show();
            window.SelectKey(brandName ?? string.Empty);
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            VeneerThemeRegistry.Changed += OnThemesChanged;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            VeneerThemeRegistry.Changed -= OnThemesChanged;
            FlushSave();
            ClearSample();
            DestroySample();
            ReleaseTheme();
        }

        /// <inheritdoc />
        protected override void BuildToolbar(VisualElement toolbar)
        {
            toolbar.Add(VeneerStyle.IconButton("refresh", "Look for systems again (after adding a brand)", ReloadCatalog, Brand));
        }

        /// <inheritdoc />
        protected override void BuildContent(VisualElement content)
        {
            ResetUiState();
            string kit = VeneerStyle.KitFolder;
            if (kit != null)
            {
                StyleSheet sheet = VeneerStyle.LoadSheet(kit + SheetRelativePath);
                if (sheet != null)
                {
                    RootElement.styleSheets.Add(sheet);
                }
            }

            // A window that was opened before this minimum existed keeps its old (smaller) one until it is rebuilt here.
            minSize = new Vector2(WindowMinWidth, WindowMinHeight);

            // Plain flex panes instead of nested split views: [list: fixed width] [handle] [sample: grows] [handle] [properties: fixed
            // width]. Only the sample has flex-grow, so a bigger window only makes it bigger; the side panes may shrink (down to their
            // minimum) before the sample drops under its own minimum. Every box has min-height/min-width 0 so a long child can never
            // push the window's content out of the window: the pane scroll views do the scrolling.
            RootElement.style.minHeight = 0f;
            RootElement.style.minWidth = 0f;
            content.style.minHeight = 0f;
            content.style.minWidth = 0f;
            var body = new VisualElement();
            body.AddToClassList("vnte-body");
            VisualElement listPane = BuildListPane();
            VisualElement samplePane = BuildSamplePane();
            VisualElement propsPane = BuildPropertiesPane();
            SetPaneWidth(listPane, true, listWidth);
            SetPaneWidth(propsPane, false, propsWidth);
            body.Add(listPane);
            body.Add(BuildSplitHandle(listPane, true));
            body.Add(samplePane);
            body.Add(BuildSplitHandle(propsPane, false));
            body.Add(propsPane);
            content.Add(body);

            LoadBrands();
            PopulateList();
            RefreshAll();

            // A rebuild of the tree drops the scheduled save: schedule it again so the pending edit is not lost.
            if (saveNeeded)
            {
                RequestSave();
            }
        }

        // ------------------------------------------------------------------ pane geometry

        // A vertical scroll view for the content of a pane: the vertical scroller only when it is needed, never a horizontal one, and
        // a content container that is exactly as wide as the pane (rows that are too wide wrap or are cut with an ellipsis, they never
        // widen the pane).
        private static ScrollView NewPaneScroll(string className)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList(className);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scroll.contentContainer.style.width = new Length(100f, LengthUnit.Percent);
            scroll.contentContainer.style.minWidth = 0f;
            scroll.contentContainer.style.flexShrink = 0f;
            return scroll;
        }

        // Sets the preferred width of a side pane (inside its minimum and maximum) and remembers it.
        private void SetPaneWidth(VisualElement pane, bool isList, float width)
        {
            float min = isList ? ListWidthMin : PropsWidthMin;
            float max = isList ? ListWidthMax : PropsWidthMax;
            width = Mathf.Clamp(width, min, max);
            pane.style.width = width;
            pane.style.minWidth = min;
            pane.style.maxWidth = max;
            if (isList)
            {
                listWidth = width;
            }
            else
            {
                propsWidth = width;
            }
        }

        // The draggable divider next to a side pane. Dragging toward the sample makes the list pane wider and the properties pane
        // narrower; the sample absorbs the difference because it is the only pane that grows.
        private VisualElement BuildSplitHandle(VisualElement pane, bool isList)
        {
            var handle = new VisualElement();
            handle.AddToClassList("vnte-handle");
            handle.tooltip = "Drag to resize";
            float startX = 0f;
            float startWidth = 0f;
            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                startX = evt.position.x;
                startWidth = pane.resolvedStyle.width;
                handle.CapturePointer(evt.pointerId);
                handle.AddToClassList("vnte-handle--drag");
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!handle.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                float delta = evt.position.x - startX;
                SetPaneWidth(pane, isList, isList ? startWidth + delta : startWidth - delta);
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (handle.HasPointerCapture(evt.pointerId))
                {
                    handle.ReleasePointer(evt.pointerId);
                    evt.StopPropagation();
                }
            });
            handle.RegisterCallback<PointerCaptureOutEvent>(evt => handle.RemoveFromClassList("vnte-handle--drag"));
            return handle;
        }

        // ------------------------------------------------------------------ catalog, selection, theme

        private void ReloadCatalog()
        {
            VeneerBrandCatalog.Refresh();
            LoadBrands();
            if (RootElement == null)
            {
                return;
            }

            PopulateList();
            RefreshAll();
        }

        private void LoadBrands()
        {
            brands.Clear();
            foreach (VeneerBrand brand in VeneerBrandCatalog.All)
            {
                if (brand != null)
                {
                    brands.Add(brand);
                }
            }
        }

        // Finds the brand of the selection (the default brand for the global theme and for a custom theme); a brand or a custom theme that
        // vanished falls back to Global.
        private void ResolveSelection()
        {
            selected = null;
            if (IsCustomSelected)
            {
                if (FindCustomByKey(selectedBrand) == null)
                {
                    selectedBrand = string.Empty;
                }
            }
            else if (!IsGlobalSelected)
            {
                selected = VeneerBrandCatalog.Find(selectedBrand);
                if (selected == null)
                {
                    selectedBrand = string.Empty;
                }
            }

            if (selected == null)
            {
                selected = VeneerBrand.Default;
            }
        }

        // The theme asset of the selection; null when a system or Global has none yet (it is made when the first override is ticked).
        private VeneerTheme FindSelectedTheme()
        {
            if (IsCustomSelected)
            {
                return FindCustomByKey(selectedBrand);
            }

            return IsGlobalSelected ? VeneerThemeRegistry.Global : VeneerThemeRegistry.Find(selectedBrand);
        }

        // True when this asset is the one the selection (a system or Global) owns.
        private bool BelongsToSelection(VeneerTheme candidate)
        {
            if (candidate == null || candidate.Custom || IsCustomSelected || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(candidate)))
            {
                return false;
            }

            return string.Equals(candidate.BrandName ?? string.Empty, selectedBrand ?? string.Empty, StringComparison.Ordinal);
        }

        // Finds the theme asset of the selection; true when it is another asset than before (the properties pane must be rebuilt).
        private bool ResolveTheme()
        {
            VeneerTheme found = FindSelectedTheme();
            if (found == null && theme != null && BelongsToSelection(theme))
            {
                // The registry has not read a just-made asset yet: keep ours so the pane is not rebuilt in the middle of an edit.
                found = theme;
            }

            if (found == theme && (found == null ? themeSo == null : themeSo != null))
            {
                return false;
            }

            ReleaseTheme();
            theme = found;
            themeSo = theme != null ? new SerializedObject(theme) : null;
            return true;
        }

        private void ReleaseTheme()
        {
            if (themeSo != null)
            {
                themeSo.Dispose();
                themeSo = null;
            }

            theme = null;
        }

        private void SelectKey(string key)
        {
            if (RootElement == null)
            {
                selectedBrand = key;
                return;
            }

            if (string.Equals(key, selectedBrand, StringComparison.Ordinal))
            {
                return;
            }

            FlushSave();
            selectedBrand = key;
            RefreshAll();
        }

        // Rebuilds every pane for the current selection.
        private void RefreshAll()
        {
            if (RootElement == null)
            {
                return;
            }

            if (brands.Count == 0)
            {
                ShowEmptyState(
                    "sliders",
                    "No systems to theme",
                    "Veneer found no brand in the project. A system appears here once its editor code defines a VeneerBrand (a static field or property of that type) and compiles. Press the refresh button at the top right after adding one.");
                return;
            }

            HideEmptyState();
            SyncList();
            ResolveSelection();
            ResolveTheme();
            RefreshSelectionMarks();
            RefreshMarks();
            BuildProperties();
            RebuildSample();
            UpdatePill();
        }

        // The registry's Changed event: the list of themes or a theme's content changed (also after our own save).
        private void OnThemesChanged()
        {
            if (RootElement == null || brands.Count == 0)
            {
                return;
            }

            if (SyncList())
            {
                RefreshAll();
                return;
            }

            bool replaced = ResolveTheme();
            RefreshMarks();
            if (replaced)
            {
                // Created, deleted or moved elsewhere: the pane must show another asset. A plain save of the same asset never rebuilds it
                // (that would drop the focus and close a colour picker).
                BuildProperties();
            }

            UpdatePill();
            ScheduleSample();
        }

        private void OnUndoRedo()
        {
            if (RootElement == null || brands.Count == 0)
            {
                return;
            }

            if (SyncList())
            {
                RefreshAll();
                return;
            }

            bool replaced = ResolveTheme();
            if (!replaced && themeSo != null)
            {
                themeSo.Update();
            }

            RefreshMarks();
            BuildProperties();
            if (theme != null)
            {
                MarkDirty();
            }
            else
            {
                UpdatePill();
                ScheduleSample();
            }
        }

        // ------------------------------------------------------------------ lazy creation, reset

        // The theme asset of a system or of Global is made the moment the first setting is overridden (or a picture is chosen), silently.
        // True when the selection has a theme to write to afterwards.
        private bool EnsureTheme()
        {
            if (theme != null && themeSo != null)
            {
                return true;
            }

            if (!IsSystemSelected && !IsGlobalSelected)
            {
                return false;
            }

            if (!AssetDatabase.IsValidFolder(ThemeFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Veneer Themes");
            }

            string path = AssetDatabase.GenerateUniqueAssetPath(ThemeFolder + "/" + FileBase(selectedBrand) + " Theme.asset");
            var created = CreateInstance<VeneerTheme>();
            created.BrandName = IsGlobalSelected ? string.Empty : selectedBrand;
            AssetDatabase.CreateAsset(created, path);
            Undo.RegisterCreatedObjectUndo(created, "Create Veneer theme");
            ReleaseTheme();
            theme = created;
            themeSo = new SerializedObject(created);
            if (propsBody != null)
            {
                propsBody.TrackSerializedObjectValue(themeSo, OnThemeSoChanged);
            }

            VeneerThemeRegistry.Invalidate();
            RefreshMarks();
            UpdateResetButton();
            return true;
        }

        // True when the theme changes anything: an override that is ticked or a picture.
        private bool HasAnyOverride()
        {
            if (theme == null)
            {
                return false;
            }

            for (int i = 0; i < OverridePaths.Length; i++)
            {
                SerializedProperty flag = themeSo != null ? themeSo.FindProperty(OverridePaths[i]) : null;
                if (flag != null && flag.boolValue)
                {
                    return true;
                }
            }

            return theme.Logo != null || theme.Banner != null;
        }

        // Reset all of a system or Global: every override is unticked and the theme asset (and its sheet) is deleted, so the system is
        // back to its own look. Asks first when the theme still overrides something (a deleted asset cannot be undone).
        private void ResetAll()
        {
            if (theme == null || !(IsSystemSelected || IsGlobalSelected))
            {
                return;
            }

            if (themeSo != null)
            {
                themeSo.Update();
            }

            if (HasAnyOverride())
            {
                string who = IsGlobalSelected ? "the global theme" : selected.Name;
                string message = "Untick every override of " + who + " and delete " + AssetDatabase.GetAssetPath(theme)
                    + "?\n\nThe editors go back to their own look. This cannot be undone.";
                if (!EditorUtility.DisplayDialog("Reset all", message, "Reset all", "Cancel"))
                {
                    return;
                }
            }

            DeleteThemeAsset(theme);
            RefreshAll();
        }

        // Deletes a theme asset and its companion sheet and forgets it; nothing may be written for a theme that is going away.
        private void DeleteThemeAsset(VeneerTheme target)
        {
            string assetPath = AssetDatabase.GetAssetPath(target);
            string sheetPath = VeneerThemeSheet.SheetPath(target);
            bool deleteSheet = !string.IsNullOrEmpty(sheetPath)
                && sheetPath.EndsWith(".uss", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sheetPath, assetPath, StringComparison.Ordinal);

            saveNeeded = false;
            ReleaseTheme();
            if (deleteSheet)
            {
                AssetDatabase.DeleteAsset(sheetPath);
            }

            if (!string.IsNullOrEmpty(assetPath))
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            VeneerStyle.InvalidateIcons();
            VeneerThemeRegistry.Invalidate();
        }

        private static string FileBase(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "Global";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(key.Length);
            for (int i = 0; i < key.Length; i++)
            {
                builder.Append(Array.IndexOf(invalid, key[i]) >= 0 ? '_' : key[i]);
            }

            string text = builder.ToString().Trim();
            return text.Length == 0 ? "Brand" : text;
        }

        // ------------------------------------------------------------------ saving and the title-bar pill

        // Called after every edit of the theme: the sheet and the asset are written once the edits stop for SaveDelayMs.
        private void MarkDirty()
        {
            if (theme == null)
            {
                return;
            }

            EditorUtility.SetDirty(theme);
            saveNeeded = true;
            UpdatePill();
            ScheduleSample();
            RequestSave();
        }

        // A true debounce: every edit moves the save back, the single scheduled tick re-arms itself until the edits have stopped.
        private void RequestSave()
        {
            lastEditTime = EditorApplication.timeSinceStartup;
            if (savePending || RootElement == null)
            {
                return;
            }

            savePending = true;
            if (saveAction == null)
            {
                saveAction = OnSaveTick;
            }

            RootElement.schedule.Execute(saveAction).StartingIn(SaveDelayMs);
        }

        private void OnSaveTick()
        {
            double remaining = (SaveDelayMs / 1000.0) - (EditorApplication.timeSinceStartup - lastEditTime);
            if (remaining > 0.01 && RootElement != null)
            {
                RootElement.schedule.Execute(saveAction).StartingIn((long)(remaining * 1000.0) + 1L);
                return;
            }

            savePending = false;
            FlushSave();
        }

        private void FlushSave()
        {
            if (theme == null)
            {
                saveNeeded = false;
                return;
            }

            if (saveNeeded)
            {
                // Cleared first: the registry's Changed event below must not find a save still pending.
                saveNeeded = false;
                try
                {
                    VeneerThemeSheet.Write(theme);
                    AssetDatabase.SaveAssetIfDirty(theme);
                    VeneerStyle.InvalidateIcons();
                    VeneerThemeRegistry.Invalidate();
                }
                catch (Exception exception)
                {
                    saveNeeded = true;
                    Debug.LogError("Veneer Theme Editor: could not save the theme: " + exception.Message);
                }
            }

            UpdatePill();
        }

        private void UpdatePill()
        {
            if (RootElement == null)
            {
                return;
            }

            if (theme == null)
            {
                SetPill("No theme", "idle");
            }
            else if (saveNeeded || EditorUtility.IsDirty(theme))
            {
                SetPill("Unsaved", "warn");
            }
            else
            {
                SetPill("Saved", "ok");
            }
        }

        private void ResetUiState()
        {
            savePending = false;
            ResetListState();
            ResetSampleState();
            ResetPropertiesState();
        }
    }
}
