using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// The Inspector Designer: a window where anybody restyles the Inspector of a MonoBehaviour or ScriptableObject without code. Drop a
    /// script on it, add tabs and cards, drag the script's fields into them, pick icons, a banner and an accent colour. The result is
    /// a <see cref="VeneerLayout"/> asset that the kit's generic editors render; the script is never touched. The window has three panes
    /// (Fields, Layout, Properties) and an optional live preview of the real Inspector. Everything is saved automatically.
    /// </summary>
    /// <remarks>
    /// Split over several files: <c>.Fields.cs</c> (left pane and the temporary instance), <c>.Layout.cs</c> (the tree and its commands),
    /// <c>.Properties.cs</c> (right pane), <c>.Preview.cs</c> (live preview) and <c>.DragDrop.cs</c> (pointer dragging).
    /// </remarks>
    public sealed partial class VeneerDesignerWindow : VeneerWindow
    {
        private const string MenuPath = "Tools/Veneer/Inspector Designer";
        private const string LayoutFolder = "Assets/Veneer Layouts";
        private const string SheetRelativePath = "/Designer/Designer.uss";
        private const long SaveDelayMs = 500;

        private enum NodeKind
        {
            Header = 0,

            // Value 1 was the separate Banner node (now part of the Header). It stays unused so windows saved with the
            // old numbering still map Page/Block/Entry correctly; a stored 1 is rejected by ValidateSelection (falls back to Header).
            Page = 2,
            Block = 3,
            Entry = 4,
            HiddenHeader = 5,
            HiddenItem = 6
        }

        // One row of the flat layout tree. Unused indices are 0; HiddenItem keeps its index into HiddenPaths in Entry.
        private struct Node
        {
            public NodeKind Kind;
            public int Page;
            public int Block;
            public int Entry;

            public Node(NodeKind kind, int page, int block, int entry)
            {
                Kind = kind;
                Page = page;
                Block = block;
                Entry = entry;
            }
        }

        // State that must survive a domain reload (the window is serialized by Unity).
        [SerializeField] private VeneerLayout layout;
        [SerializeField] private MonoScript script;
        [SerializeField] private NodeKind selKind;
        [SerializeField] private int selPage;
        [SerializeField] private int selBlock;
        [SerializeField] private int selEntry;

        // Session objects, rebuilt by Reload() and released in OnDisable, so nothing survives a reload.
        private SerializedObject layoutSo;
        private Type targetType;
        private UnityEngine.Object tempObject;
        private GameObject tempHost;
        private SerializedObject tempSo;
        private string tempProblem;
        private string ownEditorName;
        private string baseLayoutName;

        // True while the window shows the system's own default design (an in-memory layout, never an asset): everything is read-only.
        private bool readOnlyDefault;

        private bool saveNeeded;
        private bool savePending;
        private bool themeDirty;
        private bool placeAllOnCreate = true;
        private Action saveAction;
        private bool lastUseAccent;
        private Color lastAccent;
        private Texture2D lastScriptIcon;

        private ObjectField layoutField;
        private ObjectField scriptField;
        private VisualElement mainPanel;
        private VisualElement createPanel;
        private Label createTitle;
        private Label createButtonLabel;
        private VisualElement createOwnHint;
        private Label createOwnLabel;
        private Button resetButton;
        private VeneerDesignerIcons icons;

        /// <inheritdoc />
        protected override VeneerBrand Brand => VeneerKitBrand.Value;

        protected override string WindowTitle => "Inspector Designer";

        // True when edits are allowed: a layout asset is open (not the system's read-only default).
        private bool Editable => layout != null && !readOnlyDefault;

        /// <summary>Opens the Inspector Designer.</summary>
        [MenuItem(MenuPath)]
        public static void Open()
        {
            var window = GetWindow<VeneerDesignerWindow>();
            window.minSize = MinWindowSize;
            window.Show();
        }

        /// <summary>Opens the designer on an existing layout.</summary>
        /// <param name="value">The layout to edit.</param>
        public static void Open(VeneerLayout value)
        {
            var window = GetWindow<VeneerDesignerWindow>();
            window.minSize = MinWindowSize;
            window.Show();
            window.SelectLayout(value);
        }

        /// <summary>Opens the designer on a script (its layout if there is one, otherwise the offer to create it).</summary>
        /// <param name="value">The MonoBehaviour or ScriptableObject script.</param>
        public static void Open(MonoScript value)
        {
            var window = GetWindow<VeneerDesignerWindow>();
            window.minSize = MinWindowSize;
            window.Show();
            window.SelectScript(value);
        }

        private void OnEnable()
        {
            // Also here: a window restored after a domain reload or a layout change never goes through Open().
            minSize = MinWindowSize;
            Undo.undoRedoPerformed += OnUndoRedo;
            VeneerThemeRegistry.Changed -= OnThemesChanged;
            VeneerThemeRegistry.Changed += OnThemesChanged;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            VeneerThemeRegistry.Changed -= OnThemesChanged;
            FlushSave();
            ClearPreview();
            ReleaseTemp();
        }

        /// <inheritdoc />
        protected override void BuildToolbar(VisualElement toolbar)
        {
            layoutField = new ObjectField { objectType = typeof(VeneerLayout), allowSceneObjects = false, tooltip = "The layout asset being designed." };
            layoutField.style.width = 190f;
            layoutField.RegisterValueChangedCallback(evt => SelectLayout(evt.newValue as VeneerLayout));
            toolbar.Add(layoutField);

            scriptField = new ObjectField { objectType = typeof(MonoScript), allowSceneObjects = false, tooltip = "Drop a script here (or from the Project window onto the window)." };
            scriptField.style.width = 190f;
            scriptField.RegisterValueChangedCallback(evt => SelectScript(evt.newValue as MonoScript));
            toolbar.Add(scriptField);

            resetButton = VeneerStyle.Button(
                "Reset to system default",
                "undo",
                "Deletes your copy of the system's design and goes back to the system's own design.",
                ResetToSystemDefault,
                false,
                Brand);
            VeneerStyle.SetDisplay(resetButton, false);
            toolbar.Add(resetButton);
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

            RootElement.RegisterCallback<DragUpdatedEvent>(OnWindowDragUpdated);
            RootElement.RegisterCallback<DragPerformEvent>(OnWindowDragPerform);
            RootElement.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);

            BuildWarnings(content);
            BuildCreatePanel(content);
            BuildMainPanel(content);

            ghost = new Label();
            ghost.AddToClassList("vnd-ghost");
            ghost.pickingMode = PickingMode.Ignore;
            RootElement.Add(ghost);

            VeneerDesignerIcons.Reload();
            Reload();
        }

        // ------------------------------------------------------------------ choosing a layout or a script

        private void SelectLayout(VeneerLayout value)
        {
            if (layout == value && (value == null || script == value.Script))
            {
                return;
            }

            FlushSave();
            layout = value;
            // Clearing the layout field keeps the script, so the offer to create a layout appears instead of an empty window.
            script = value != null ? value.Script : script;
            if (RootElement != null)
            {
                Reload();
            }
        }

        private void SelectScript(MonoScript value)
        {
            FlushSave();
            script = value;
            layout = null;
            Type type = value != null ? value.GetClass() : null;
            if (type != null)
            {
                VeneerLayout found = VeneerLayoutRegistry.Find(type);
                // A layout found through a base type is shown as a hint, not opened: editing it would restyle the base class.
                if (found != null && found.ResolveType() == type)
                {
                    layout = found;
                }
            }

            if (RootElement != null)
            {
                Reload();
            }
        }

        // Rebuilds everything that depends on the layout and the script: the temporary instance, the field list, every pane.
        // With no layout asset, the system's own default layout (when the type has one) is opened read-only.
        private void Reload()
        {
            if (readOnlyDefault)
            {
                // The in-memory default is never kept as "the" layout: it is looked up again.
                layout = null;
                readOnlyDefault = false;
            }

            ClearPreview();
            ReleaseTemp();
            targetType = layout != null ? layout.ResolveType() : (script != null ? script.GetClass() : null);

            // Asked even when a user layout is open: the lookup registers the system's custom items, which the Fields pane lists.
            VeneerLayout systemDefault = null;
            bool hasDefault = false;
            if (targetType != null)
            {
                try
                {
                    hasDefault = VeneerDefaultLayouts.TryGet(targetType, out systemDefault) && systemDefault != null;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    hasDefault = false;
                }
            }

            baseLayoutName = null;
            if (layout == null)
            {
                if (hasDefault && script != null)
                {
                    layout = systemDefault;
                    readOnlyDefault = true;
                }
                else if (targetType != null)
                {
                    VeneerLayout found = VeneerLayoutRegistry.Find(targetType);
                    baseLayoutName = found != null ? found.name : null;
                }
            }

            layoutSo = layout != null ? new SerializedObject(layout) : null;
            CollectItems();
            BuildTemp();
            CacheTheme();
            RefreshAll();
        }

        private void RefreshAll()
        {
            if (RootElement == null)
            {
                return;
            }

            layoutField.SetValueWithoutNotify(readOnlyDefault ? null : layout);
            scriptField.SetValueWithoutNotify(script);
            VeneerStyle.SetDisplay(resetButton, Editable && layout.FromSystemDefault);
            ValidateSelection();
            RefreshFieldRows();
            RefreshItemRows();
            RefreshTree();
            UpdateFieldMarks();
            RefreshWarnings();
            RefreshVisibility();
            BuildProperties();
            SchedulePreview();
            UpdatePill();
        }

        private void RefreshVisibility()
        {
            if (layout == null && script == null)
            {
                ShowEmptyState(
                    "layers",
                    "Drop a script to design its Inspector",
                    "Drag a MonoBehaviour or ScriptableObject script from the Project window onto this window (or into the Script field above), then add tabs and cards and drag the script's fields into them.");
                return;
            }

            HideEmptyState();
            VeneerStyle.SetDisplay(mainPanel, layout != null);
            bool offerCreate = layout == null && targetType != null && tempProblem == null;
            VeneerStyle.SetDisplay(createPanel, offerCreate);
            if (offerCreate)
            {
                createTitle.text = "No layout for " + targetType.Name + " yet";
                createButtonLabel.text = "Create layout for " + targetType.Name;

                // A type with its own (non-layout-driven) editor would ignore the layout: say so, but keep the button usable.
                bool ownEditor = ownEditorName != null;
                VeneerStyle.SetDisplay(createOwnHint, ownEditor);
                if (ownEditor)
                {
                    createOwnLabel.text = "This type has its own editor (" + ownEditorName + "); a layout would not be used.";
                }
            }
        }

        private void BuildCreatePanel(VisualElement content)
        {
            createPanel = VeneerStyle.EmptyState(
                "layers",
                string.Empty,
                "A layout is a small asset next to your project's files. It restyles the Inspector of this script; deleting it gives the default Inspector back, and the script is never changed.",
                out createTitle,
                out _,
                Brand);
            createOwnHint = VeneerStyle.Hint(string.Empty, "warn", out createOwnLabel, Brand);
            createOwnHint.style.marginTop = 6f;
            createOwnHint.style.maxWidth = 460f;
            VeneerStyle.SetDisplay(createOwnHint, false);
            createPanel.Add(createOwnHint);
            VisualElement chip = VeneerStyle.Chip("Place all fields in a first card", "check", placeAllOnCreate, on => placeAllOnCreate = on, Brand);
            createPanel.Add(chip);
            Button create = VeneerStyle.Button("Create layout", "plus-circle", "Creates the layout asset in Assets/Veneer Layouts.", CreateLayoutForScript, true, Brand);
            create.style.marginTop = 8f;
            createButtonLabel = create.Q<Label>(className: "vn-button__label");
            createPanel.Add(create);
            VeneerStyle.SetDisplay(createPanel, false);
            content.Add(createPanel);
        }

        private void CreateLayoutForScript()
        {
            if (script == null || targetType == null)
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(LayoutFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Veneer Layouts");
            }

            string path = AssetDatabase.GenerateUniqueAssetPath(LayoutFolder + "/" + targetType.Name + " Layout.asset");
            var created = CreateInstance<VeneerLayout>();
            created.Bind(script);
            var page = new VeneerLayoutPage { Title = "General" };
            var block = new VeneerLayoutBlock { Title = "Fields" };
            if (placeAllOnCreate)
            {
                for (int i = 0; i < fields.Count; i++)
                {
                    // Top-level fields only: a nested struct is placed whole, as one card.
                    if (fields[i].Depth == 0)
                    {
                        block.Entries.Add(new VeneerLayoutEntry { Path = fields[i].Path });
                    }
                }
            }

            page.Blocks.Add(block);
            created.Pages.Add(page);
            AssetDatabase.CreateAsset(created, path);
            Undo.RegisterCreatedObjectUndo(created, "Create Veneer layout");
            layout = created;
            Reload();
        }

        // Makes the user's own editable copy of the system's default design and opens it.
        private void CustomizeDefault()
        {
            if (!readOnlyDefault || layout == null || script == null || targetType == null)
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(LayoutFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Veneer Layouts");
            }

            string path = AssetDatabase.GenerateUniqueAssetPath(LayoutFolder + "/" + targetType.Name + " Layout.asset");
            VeneerLayout copy = Instantiate(layout);
            copy.hideFlags = HideFlags.None;
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            copy.Bind(script);
            copy.FromSystemDefault = true;
            AssetDatabase.CreateAsset(copy, path);
            Undo.RegisterCreatedObjectUndo(copy, "Customize Veneer layout");
            VeneerLayoutTheme.WriteSheet(copy);
            if (copy.ScriptIcon != null)
            {
                VeneerScriptIcon.Apply(copy);
            }

            VeneerLayoutRegistry.Invalidate();
            readOnlyDefault = false;
            layout = copy;
            Reload();
        }

        // Deletes the user's copy of a system design (after a confirmation) and shows the read-only system design again.
        private void ResetToSystemDefault()
        {
            if (!Editable || !layout.FromSystemDefault)
            {
                return;
            }

            string message = "This deletes your layout \"" + layout.name + "\" and goes back to the system's own design. Your changes to it are lost.";
            if (!EditorUtility.DisplayDialog("Reset to system default", message, "Reset", "Cancel"))
            {
                return;
            }

            FlushSave();
            string path = AssetDatabase.GetAssetPath(layout);
            string sheet = VeneerLayoutTheme.SheetPath(layout);
            if (layout.ScriptIcon != null)
            {
                VeneerScriptIcon.Clear(layout);
            }

            Undo.ClearUndo(layout);
            layout = null;
            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.DeleteAsset(path);
            }

            if (!string.IsNullOrEmpty(sheet) && !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(sheet)))
            {
                AssetDatabase.DeleteAsset(sheet);
            }

            VeneerLayoutRegistry.Invalidate();
            Reload();
        }

        // ------------------------------------------------------------------ saving, theme, pill

        // Throttled autosave: the first edit schedules one save 500 ms later, edits in between ride along.
        private void MarkDirty()
        {
            if (!Editable)
            {
                return;
            }

            EditorUtility.SetDirty(layout);
            saveNeeded = true;
            UpdatePill();
            if (!savePending && RootElement != null)
            {
                savePending = true;
                if (saveAction == null)
                {
                    saveAction = SaveNow;
                }

                RootElement.schedule.Execute(saveAction).StartingIn(SaveDelayMs);
            }
        }

        private void SaveNow()
        {
            savePending = false;
            FlushSave();
        }

        private void FlushSave()
        {
            if (!Editable)
            {
                saveNeeded = false;
                themeDirty = false;
                return;
            }

            if (themeDirty)
            {
                themeDirty = false;
                VeneerLayoutTheme.WriteSheet(layout);
                // The companion sheet was rewritten: forget the cached copy and rebuild the preview with the new colour.
                VeneerStyle.InvalidateIcons();
                SchedulePreview();
            }

            if (saveNeeded)
            {
                saveNeeded = false;
                AssetDatabase.SaveAssetIfDirty(layout);
            }

            UpdatePill();
        }

        private void UpdatePill()
        {
            if (RootElement == null)
            {
                return;
            }

            if (layout == null)
            {
                SetPill("No layout", "idle");
            }
            else if (readOnlyDefault)
            {
                SetPill("System design", "special");
            }
            else if (saveNeeded || themeDirty || EditorUtility.IsDirty(layout))
            {
                SetPill("Unsaved", "warn");
            }
            else
            {
                SetPill("Saved", "ok");
            }
        }

        private void CacheTheme()
        {
            if (layout == null)
            {
                return;
            }

            lastUseAccent = layout.UseAccent;
            lastAccent = layout.Accent;
            lastScriptIcon = layout.ScriptIcon;
        }

        // Called after every edit of the layout: the accent colour needs its style sheet rewritten and the script icon needs applying.
        private void HandleThemeAndIcon()
        {
            if (layout.UseAccent != lastUseAccent || layout.Accent != lastAccent)
            {
                lastUseAccent = layout.UseAccent;
                lastAccent = layout.Accent;
                themeDirty = true;
            }

            if (layout.ScriptIcon != lastScriptIcon)
            {
                lastScriptIcon = layout.ScriptIcon;
                if (layout.ScriptIcon != null)
                {
                    VeneerScriptIcon.Apply(layout);
                }
                else
                {
                    VeneerScriptIcon.Clear(layout);
                }
            }
        }

        // After a direct change of the model (commands, drag and drop): record nothing here, the caller called Undo.RecordObject.
        private void LayoutEdited()
        {
            if (layout == null || RootElement == null)
            {
                return;
            }

            EditorUtility.SetDirty(layout);
            layoutSo.Update();
            HandleThemeAndIcon();
            ValidateSelection();
            RefreshTree();
            UpdateFieldMarks();
            RefreshWarnings();
            BuildProperties();
            SchedulePreview();
            MarkDirty();
        }

        // After an edit made through a bound control of the Properties pane (the pane itself must not be rebuilt: that would drop focus).
        private void OnLayoutSoChanged(SerializedObject changed)
        {
            if (layout == null || RootElement == null || !EditorUtility.IsDirty(layout))
            {
                return;
            }

            HandleThemeAndIcon();
            RefreshTree();
            UpdateFieldMarks();
            RefreshWarnings();
            if (bannerRefresh != null)
            {
                bannerRefresh();
            }

            if (lookRefresh != null)
            {
                lookRefresh();
            }

            SchedulePreview();
            MarkDirty();
        }

        private void OnUndoRedo()
        {
            if (RootElement == null)
            {
                return;
            }

            if (layout == null)
            {
                Reload();
                return;
            }

            if (readOnlyDefault)
            {
                return;
            }

            layoutSo.Update();
            HandleThemeAndIcon();
            RefreshAll();
            MarkDirty();
        }

        // ------------------------------------------------------------------ selection

        private void ValidateSelection()
        {
            if (layout == null)
            {
                selKind = NodeKind.Header;
                selPage = 0;
                selBlock = 0;
                selEntry = 0;
                return;
            }

            List<VeneerLayoutPage> pages = layout.Pages;
            bool valid;
            switch (selKind)
            {
                case NodeKind.Page:
                    valid = selPage >= 0 && selPage < pages.Count;
                    break;
                case NodeKind.Block:
                    valid = selPage >= 0 && selPage < pages.Count && selBlock >= 0 && selBlock < pages[selPage].Blocks.Count;
                    break;
                case NodeKind.Entry:
                    valid = selPage >= 0 && selPage < pages.Count && selBlock >= 0 && selBlock < pages[selPage].Blocks.Count
                        && selEntry >= 0 && selEntry < pages[selPage].Blocks[selBlock].Entries.Count;
                    break;
                case NodeKind.HiddenItem:
                    valid = selEntry >= 0 && selEntry < layout.HiddenPaths.Count;
                    break;
                case NodeKind.Header:
                case NodeKind.HiddenHeader:
                    valid = true;
                    break;
                default:
                    // Includes the retired Banner value (1) from older saved windows.
                    valid = false;
                    break;
            }

            if (!valid)
            {
                selKind = NodeKind.Header;
                selPage = 0;
                selBlock = 0;
                selEntry = 0;
            }
        }

        private bool IsSelected(Node node)
        {
            return node.Kind == selKind && node.Page == selPage && node.Block == selBlock && node.Entry == selEntry;
        }

        private void Select(Node node)
        {
            if (IsSelected(node))
            {
                return;
            }

            selKind = node.Kind;
            selPage = node.Page;
            selBlock = node.Block;
            selEntry = node.Entry;
            UpdateSelectionVisuals();
            BuildProperties();
        }

        // ------------------------------------------------------------------ dropping a script or a layout on the window

        private static MonoScript DraggedScript()
        {
            UnityEngine.Object[] objects = DragAndDrop.objectReferences;
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] is MonoScript dropped)
                {
                    return dropped;
                }
            }

            return null;
        }

        private static VeneerLayout DraggedLayout()
        {
            UnityEngine.Object[] objects = DragAndDrop.objectReferences;
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] is VeneerLayout dropped)
                {
                    return dropped;
                }
            }

            return null;
        }

        private void OnWindowDragUpdated(DragUpdatedEvent evt)
        {
            if (DraggedScript() != null || DraggedLayout() != null)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                evt.StopPropagation();
            }
        }

        private void OnWindowDragPerform(DragPerformEvent evt)
        {
            MonoScript droppedScript = DraggedScript();
            VeneerLayout droppedLayout = droppedScript == null ? DraggedLayout() : null;
            if (droppedScript == null && droppedLayout == null)
            {
                return;
            }

            DragAndDrop.AcceptDrag();
            evt.StopPropagation();
            if (droppedScript != null)
            {
                SelectScript(droppedScript);
            }
            else
            {
                SelectLayout(droppedLayout);
            }
        }

        private void ResetUiState()
        {
            fieldRows.Clear();
            treeRows.Clear();
            itemRows.Clear();
            itemsBox = null;
            itemsScroll = null;
            readOnlyBar = null;
            savePending = false;
            previewPending = false;
            renaming = null;
            dragPending = false;
            dragActive = false;
            dragCapturer = null;
            highlightRow = -1;
            icons = null;
            propsBody = null;
            fieldsPane = null;
            centrePane = null;
            propsPane = null;
            splitArea = null;
            previewGroup = null;
            previewRoot = null;
            previewSo = null;
        }
    }
}
