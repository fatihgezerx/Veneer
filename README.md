# Veneer

The shared editor-UI kit of the studio's systems: one style sheet, one icon set, one set of controls and a small layout API, so every
Inspector, settings page and tool window looks like part of the same product. Editor only (UI Toolkit, Unity 6000.3), no runtime code.

- Namespace `Veneer`, assembly `Veneer.Editor` (Editor platform, references nothing).
- Systems never reference it from `Runtime/`. A system's **Editor** assembly references it and is gated by `HAS_VENEER`
  (see "Using the kit from a system"); without the kit the system keeps Unity's default Inspectors.

## Folder

```
Veneer/
  README.md  CHANGELOG.md  LICENSE
  Editor/
    Veneer.Editor.asmdef
    VeneerBrand.cs  VeneerStyle.cs  VeneerStyle.Widgets.cs  VeneerIconPostprocessor.cs
    Controls/   VeneerControls.cs  VeneerBinding.cs  VeneerFieldInfo.cs
    Builder/    VeneerInspectorBuilder.cs  VeneerInspector.cs  VeneerAutoInspector.cs
    Pages/      VeneerSettingsPage.cs  VeneerWindow.cs
    Graph/      VeneerGraph.cs  (VeneerSeries, VeneerPalette, VeneerGraph, VeneerSparkline, VeneerNumberText)
    Styles/     Veneer.uss
    Icons/      104 white 64x64 glyphs (lowercase-kebab names)
```

## Using the kit from a system

1. **Brand** (internal, in the system's Editor assembly; keep one cached instance):

```csharp
internal static class CombatBrand
{
    public static readonly VeneerBrand Value = VeneerBrand.FromAssets(
        name: "Combat System",
        subtitle: "Fighters, weapons and AI",
        accent: new Color32(0xFB, 0x92, 0x3C, 255),               // picks the nearest accent preset (orange) for USS parts
        logoPath: "Assets/Scripts/CombatSystem/Editor/Brand/logo.png",
        bannerPath: "Assets/Scripts/CombatSystem/Editor/Brand/banner.png",
        iconFolder: "Assets/Scripts/CombatSystem/Editor/Brand/Icons"); // optional extra glyphs, searched after the kit's
}
```

   PNGs under any `.../Editor/Brand/...` folder get the icon import settings automatically (uncompressed, no mipmaps, bilinear).
   **Accent**: USS cannot take a colour from C#. The brand therefore adds an accent preset class (`vn-accent--blue|violet|magenta|red|orange|amber|green|teal|slate`;
   cyan is the default) - automatically the nearest one to `Accent`, or explicitly with the `accentClass` argument. For an exact
   colour ship a style sheet (`styleSheetPath`) that sets the variables on `.vn-root` (and `.vn-root.vn-root--light` for the light skin):

```css
.vn-root { --vn-accent: #FF6A3D; --vn-accent-soft: rgba(255,106,61,0.16); --vn-accent-line: rgba(255,106,61,0.55); --vn-pressed: rgba(255,106,61,0.24); }
```

   `Accent` stays exact in code that draws with colours (`VeneerGraph.SetAccent(brand.Accent)`).

2. **Editor asmdef** of the system:

```json
{
    "name": "MySystem.Editor",
    "references": [ "MySystem.Runtime", "Veneer.Editor" ],
    "includePlatforms": [ "Editor" ],
    "defineConstraints": [ "HAS_VENEER" ]
}
```

   The system's `DependencyGuard` declares the dependency (name `Veneer`, assembly `Veneer.Editor`, define
   `HAS_VENEER`, repo URL once it exists), exactly like any own-system dependency in ADR-0001. Keep the system's non-kit editor
   code (menus, converters, scene handles) in a separate assembly or in files that do not need the kit if it must compile without it.

3. **Write the editor.**

```csharp
[CustomEditor(typeof(OptimizedRigidbody))]
internal sealed class OptimizedRigidbodyEditor : VeneerInspector<OptimizedRigidbody>
{
    protected override VeneerBrand Brand => PhysicsBrand.Value;

    protected override void Build(VeneerInspectorBuilder b)
    {
        b.Header("Optimized Rigidbody", null, "sphere", out Label _);
        b.StatePill(() => Application.isPlaying ? ("Awake", "ok") : ("Dynamic", "info"));
        b.Live(() => "speed " + Target.LinearVelocity.magnitude.ToString("0.0") + " m/s");

        VisualElement[] tabs = b.Tabs(("Body", "cube"), ("Surface", "layers"), ("Limits", "lock"));
        using (b.Into(tabs[0]))
        {
            b.Card("Shape", "cube", () =>
            {
                b.Enum("shape", new[] { "sphere", "cube", "target" });
                b.VisibleWhen(b.Number("radius", "Radius", "m"), "shape", (int)OptimizedShape.Box, invert: true);
                b.ConditionalHint(() => Target.Mass <= 0f ? "Mass must be above 0." : null, "error");
            });
            b.Card("Physics", "bolt", () =>
            {
                b.Number("mass", "Mass", "kg");
                b.Chips(("useGravity", "Use Gravity", "bolt"), ("isKinematic", "Is Kinematic", "lock"));
            });
        }
        using (b.Into(tabs[1]))
        {
            b.Card("Bounce and grip", "layers", () =>
            {
                b.Slider("bounciness", "Bounciness", "Dead", "Bouncy");
                b.Slider("friction", "Friction", "Slippery", "Grippy", 0f, 2f);
            });
        }
    }
}
```

   One line for a simple component: `[CustomEditor(typeof(Foo), true)] class FooEditor : VeneerAutoInspector { }`.

   Settings page and window:

```csharp
[SettingsProvider]
static SettingsProvider Create() => VeneerSettingsPage.Provider(
    "Project/My System", "My System", MyBrand.Value, "My System", "World settings", page =>
    {
        page.PresetRow("Presets", "preset", presets, index => Apply(index));
        VisualElement[] tabs = page.Tabs("MySystem.SettingsTab", ("Essentials", "star"), ("Advanced", "settings"));
        // fill tabs[i] with VeneerStyle.Card(...) and VeneerControls.NumberRow(property, ...), then page.Content is already mounted
    });

internal sealed class MyWindow : VeneerWindow
{
    protected override VeneerBrand Brand => MyBrand.Value;
    protected override void BuildContent(VisualElement content) { /* KPI cards, VeneerGraph ... */ Every(250, Refresh); }
}
```

## The rules (every restyled Inspector / window)

1. Preserve behaviour: serialized data, validation, buttons, menus, Undo, multi-object editing and prefab overrides keep working; only
   presentation changes. Account for every feature of the old editor.
2. The front tab shows at most 6-8 controls (what people change most); the rest is one tab away. Wiring references and rarely changed
   defaults are `[HideInInspector]`. No foldouts: tabs and cards instead. Friendly labels, one-line tooltips, units next to numbers.
3. UI Toolkit only (`CreateInspectorGUI`, `CreateGUI`, `SettingsProvider.activateHandler`); IMGUI only where UI Toolkit has no equivalent (Scene handles).
4. Allocation-light, leak-free: schedulers and subscriptions are removed on detach; live strips refresh at 5 Hz or slower.
5. Droppable: nothing in a system's Runtime references the kit; the Editor assembly is gated by `HAS_VENEER`.
6. Icons: use names from the list below, or from the brand's own icon folder. A missing icon shows as empty space, never an error.
7. Code conventions: English, doc comments on public API, private fields camelCase without underscore, no `Update`, no commits.

## API reference

### VeneerBrand
`new VeneerBrand(string name, string subtitle = null, Color? accent = null, Texture2D logo = null, Texture2D banner = null, string iconFolder = null, string styleSheetPath = null, string accentClass = null)`
`static VeneerBrand FromAssets(name, subtitle, Color accent, string logoPath, string bannerPath, string iconFolder = null, string styleSheetPath = null, string accentClass = null)`
`static VeneerBrand Default`, `static Color DefaultAccent`, `static string ResolveAccentClass(Color)`.
Properties: `Name`, `Subtitle`, `Accent`, `IconFolder`, `StyleSheetPath`, `AccentClass`, `Logo`, `Banner` (textures reload lazily from their paths).

### VeneerStyle (static, partial)
Assets: `StyleSheet Sheet`, `string KitFolder`, `Texture2D Icon(name, brand = null)` (never null), `IconOrNull`, `StyleSheet LoadSheet(path)`,
`VisualElement Root(VeneerBrand brand = null, string extraClass = null)`, `ApplyBrand(root, brand)`, `InvalidateIcons()`.
Elements: `Card(title, icon, brand)`, `IconElement(icon, extraClass, brand)`, `SetIcon(el, icon, brand)`, `Row(wrap)`, `Spacer()`, `MutedLabel`,
`EllipsisLabel`, `Title`, `Subtitle`, `Section(title)`, `FieldRow(label, control, tooltip, out Label)`, `SetDisplay(el, bool)`.
State: `Pill(text, kind)` / `SetPill(pill, kind, text)` (kinds `info ok idle warn error special`; legacy `awake asleep kinematic` accepted),
`IconButton(icon, tooltip, clicked, brand)`, `Button(text, icon, tooltip, clicked, primary, brand)`.
Choices: `SegmentedIcons(icons, tooltips, selected, changed, brand)` + `Get/SetSegmentedSelected`, `Chip(text, icon, on, changed, brand)` +
`IsChipOn / SetChipOn / SetChipState(chip, on, mixed) / ChipLabel`, `Tabs(icons, labels, tooltips, selected, changed, brand)`,
`TabView(sessionKey, labels, icons, tooltips, brand, out VisualElement[] pages, out VisualElement tabBar)` (selected tab kept in `SessionState`).
Blocks: `Banner(brand, title, subtitle)`, `Hint(text, kind, out Label, brand)` / `Hint(text, kind, brand)` (kinds `info warn error`),
`Kpi(title, icon, out Label value, brand)`, `Bar(kind)` + `SetBarFill(bar, 0..1)` + `SetBarKind`, `Toolbar()`,
`SearchField(Action<string> changed, out TextField field, placeholder, brand)`, `EmptyState(icon, title, text, brand)` (+ overload with out labels).

### VeneerControls (static) - all take a `SerializedProperty`, copy it, work with multi-edit, prefab marks and Undo
`NumberRow(property, label = null, unit = null)` (clamps to `[Min]`/`[Range]` on commit; float, double, int)
`SliderRow(property, label = null, leftEnd = null, rightEnd = null, float low = NaN, float high = NaN, unit = null)` (range from `[Range]` when low/high are NaN)
`ToggleChip(property, label = null, icon = null, brand = null)`, `FlagChips(property, string[] names = null, string[] icons = null, brand = null)`
`EnumSegmented(property, string[] icons = null, string[] tooltips = null, string label = null, brand = null)`
`ObjectSlot(property, label = null, Type objectType = null, brand = null)`, `ListEditor(property, label = null, brand = null)` (binds itself; add/remove/reorder)
`Vector3Row(property, label)` (also Vector2), `ColorRow`, `CurveRow`. Helpers in `VeneerBinding`: `Hook`, `Commit`, `ForEachTarget`, `CountTargets`.

### VeneerInspector / VeneerInspector&lt;T&gt; / VeneerAutoInspector
`abstract class VeneerInspector : Editor` - `virtual VeneerBrand Brand`, `abstract void Build(VeneerInspectorBuilder b)`; the exception of a failing `Build` is logged and shown.
`abstract class VeneerInspector<T> : VeneerInspector where T : UnityEngine.Object` - `T Target`, `int TargetCount`.
`class VeneerAutoInspector : VeneerInspector` - virtual `Brand`, `Title`, `Subtitle`, `IconName`, `TabThreshold` (4), `TabIcon(group)`, `ExtraTopContent(b)`, `ExtraBottomContent(b)`.

### VeneerInspectorBuilder (every content method adds to the current container and returns the element)
`new VeneerInspectorBuilder(SerializedObject, VisualElement root, VeneerBrand brand = null, string sessionPrefix = null)`; `Finish()` (called by `VeneerInspector`).
Structure: `Brand(VeneerBrand)`, `Into(container)` (`using` scope), `Add(element)`, `Header(title, subtitle = null, iconName = null)` / `Header(..., out Label pill)`,
`HeaderRight`, `StatePill(Func<(string text, string kind)>)`, `Tabs(params (string label, string icon)[])` / `Tabs(string sessionKey, ...)` returning page containers,
`Card(title = null, icon = null)` / `Card(title, icon, Action content)`, `Section(title)`, `Row(wrap)`, `Space(height)`.
Fields (by property path): `Field(path, label = null, keepDecorators = false)`, `Number(path, label, unit)`, `Slider(path, label, leftEnd, rightEnd, low, high, unit)`,
`Chip(path, label, icon)`, `Chips(params (path, label, icon)[])`, `Flags(path, names, icons)`, `Enum(path, icons, tooltips, label)`, `Object(path, label, type)`,
`List(path, label)`, `Vector3(path, label)`, `Color(path, label)`, `Curve(path, label)`. A wrong path shows an error hint instead of failing.
Messages: `Hint(text, kind)`, `ConditionalHint(Func<string> text, kind = "warn", bool poll = false)`, `Button(label, icon, action, tooltip, primary)`, `Live(Func<string>)` (Play Mode only).
Rules: `VisibleWhen(element, path, object value, bool invert = false)` (bool / int / enum / string / float / null), `VisibleWhen(element, Func<bool>, bool poll = false)`.
One 200 ms scheduler (paused on detach) drives pills, live strips and polled rules; one value tracker re-evaluates the rest on every data change, Undo included.

### Pages, windows, graphs
`VeneerSettingsPage(brand, title, subtitle)`: `Mount(rootElement)`, `PresetRow(title, icon, IReadOnlyList<VeneerPreset>, Action<int>)`, `RefreshPresets(Func<int,bool>)`,
`Tabs(sessionKey, ...)`, `LiveCard()`, `CreateAssetHint(message, buttonText, tooltip, create)`, `Every(action, ms)`, `Dispose()`, `static Provider(path, label, brand, title, subtitle, build, scope, keywords)`.
`abstract VeneerWindow : EditorWindow` - `Brand`, `WindowTitle`, `abstract BuildContent(content)`, `BuildToolbar(toolbar)`, `SetPill(text, kind)`, `ShowEmptyState(icon, title, text)`, `HideEmptyState()`, `Every(ms, action)`.
`VeneerSeries(capacity)` (`Add`, `Count`, `Capacity`, `Latest`, `Peak`, indexer), `VeneerGraph(primary, secondary = null, gridSamples = 30)` (`Tick`, `SetBudget`, `SetAccent`, `ResetScale`),
`VeneerSparkline(series, minimumRange)`, `VeneerPalette.Current`, `VeneerNumberText(prefix, suffix, format, scale).Get(value)`.

## USS classes (`Styles/Veneer.uss`)
Root and variables: `vn-root`, `vn-root--light`, `vn-accent--<preset>`; variables `--vn-accent -soft -line`, `--vn-text`, `--vn-text-muted`, `--vn-card`, `--vn-card-border`,
`--vn-hover`, `--vn-pressed`, `--vn-track`, `--vn-label-width`, `--vn-ok|idle|warn|error|special` (+ `-bg`, `-line`).
Layout: `vn-grow`, `vn-spacer`, `vn-ellipsis`, `vn-wrap`, `vn-row` (+`--wrap`, `--between`), `vn-section`, `vn-field-row` (`__label`, `__control`), `vn-number`, `vn-unit`, `vn-slider-row` (`__top`, `__slider`, `__ends`),
`vn-card` (+`--nested`; `__title`, `__title-label`), `vn-header__text`, `vn-preset` (+`--on`; `__title`, `__text`).
Text: `vn-title`, `vn-subtitle`, `vn-label-muted`. Icons: `vn-icon` (+`--sm --lg --xl --text --accent --ok --idle --warn --error --special`).
Widgets: `vn-pill` (+`--info --ok --idle --warn --error --special`), `vn-icon-button` (+`--on`), `vn-button` (+`--primary`, `__label`), `vn-segmented` (`__button`, `__button--selected`, `__label`),
`vn-chip` (+`--on`, `__label`), `vn-tabs`, `vn-tab` (+`--selected`, `__label`), `vn-banner` (`__logo __text __title __subtitle`), `vn-kpi` (`__header __title __value`), `vn-bar` (+`--ok --warn --error --special`, `__fill`),
`vn-hint` (+`--info --warn --error`, `__text`), `vn-live` (`__text`), `vn-list-row` (+`--alt`), `vn-list` (`__header __title __view __notice`), `vn-toolbar`, `vn-search`, `vn-empty` (`__title __text`),
`vn-thumb`, `vn-graph`, `vn-window`, `vn-titlebar` (`__logo`), `vn-field--no-decorators`.

## Icons (`Editor/Icons`, 64x64 white glyphs, tinted by USS)
anchor, arrow-down, arrow-left, arrow-right, arrow-up, bell, bolt, book, bookmark, bug, calendar, camera, center, check, check-circle, chevron-down, chevron-left, chevron-right, chevron-up,
clock, close, code, collapse, copy, cube, cursor, database, dot, download, drag-handle, edit, error, expand, external-link, eye, eye-off, file, filter, fit, flag, folder, globe, graph, grid,
hash, heart, help, image, info, key, layers, link, list, lock, mail, map, mask, minus, minus-circle, more, move, music, overlay, paste, pause, percent, play, play-circle, plus, plus-circle,
preset, quality, record, redo, refresh, rotate, save, scale, search, settings, shield, sliders, sort, sound, speed, sphere, star, step, stop, sword, tag, target, text, trash, undo, unlink,
unlock, upload, user, users, warning, wrench, zoom-in, zoom-out.
Lookup order: the kit's folder first, then the brand's `IconFolder`. A brand icon with the same name as a kit icon is therefore never used: pick another name.

## Notes and limits
- `[Header]` / `[Space]` decorators are hidden on kit fields with a USS rule on the class name Unity gives the decorator container (`unity-decorator-drawers-container`); if a Unity version names it differently the header shows twice (cosmetic).
- `VeneerAutoInspector` expands every `[Serializable]` struct/class into a nested card, also ones with their own `PropertyDrawer`; `UnityEvent`s, object references and managed references stay `PropertyField`s.
- `ListEditor` uses Unity's own bound `ListView` items (`PropertyField` per element) and its own header buttons.
- The kit never writes assets; the only import hook is the icon postprocessor (kit `Icons` and any `Editor/Brand` folder).

## DependencyGuard entry (in the system's `Editor/Setup/DependencyGuard.cs`)

```csharp
internal static readonly Dependency[] Dependencies =
{
    // ... the system's other dependencies ...
    // The kit is an own system, so it is installed from its GitHub repo once that exists (a git URL), or copied into Assets/Scripts by hand until then.
    new("Veneer", "Veneer.Editor", "HAS_VENEER", "<github url of the Veneer repo>.git", "studio.editorkit"),
},
```

The guard sets `HAS_VENEER` while an assembly named `Veneer.Editor` exists. Only the system's Editor asmdef carries the
`defineConstraints`; its `Setup` assembly stays reference-free as before.

## Worked example 1: a ScriptableObject Inspector (tabs, cards, slider, list)

```csharp
// Runtime (no kit reference):
public sealed class WeaponData : ScriptableObject
{
    [Tooltip("Damage of one hit.")] [Min(0)] public float damage = 10f;
    [Range(0f, 1f)] public float critChance = 0.1f;
    public bool twoHanded;
    public WeaponKind kind;                         // enum Sword, Axe, Bow
    public GameObject prefab;
    public List<WeaponAttack> attacks = new();      // [Serializable] struct with name + multiplier
}

// Editor (references Veneer.Editor, gated by HAS_VENEER):
[CustomEditor(typeof(WeaponData))]
internal sealed class WeaponDataEditor : VeneerInspector<WeaponData>
{
    protected override VeneerBrand Brand => CombatBrand.Value;

    protected override void Build(VeneerInspectorBuilder b)
    {
        b.Header(Target.name, "Weapon data", "sword");
        VisualElement[] tabs = b.Tabs(("Stats", "sliders"), ("Attacks", "list"));

        using (b.Into(tabs[0]))
        {
            b.Card("Damage", "bolt", () =>
            {
                b.Enum("kind", new[] { "sword", "bolt", "target" }, new[] { "Sword", "Axe", "Bow" });
                b.Number("damage", "Damage", "hp");
                b.Slider("critChance", "Crit chance", "Never", "Always");
                b.Chip("twoHanded", "Two handed", "users");
                b.ConditionalHint(() => Target.damage <= 0f ? "Damage is 0: this weapon never hurts." : null, "warn");
            });
            b.Card("Look", "cube", () => b.Object("prefab", "Prefab"));
        }

        using (b.Into(tabs[1]))
        {
            b.Card("Attacks", "list", () => b.List("attacks", "Attack list"));
        }
    }
}
```

## Worked example 2: a Project Settings page

```csharp
internal static class MySettingsPage
{
    private static readonly VeneerPreset[] Presets =
    {
        new("Default", "Balanced for most games.", "star"),
        new("Debris", "Cheap, lots of small bodies.", "grid"),
    };

    [SettingsProvider]
    private static SettingsProvider Create() => VeneerSettingsPage.Provider(
        "Project/My System", "My System", MyBrand.Value, "My System", "World settings", Build,
        keywords: new[] { "physics", "gravity", "sleep" });

    private static void Build(VeneerSettingsPage page)
    {
        MySettings settings = MySettings.FindOrNull();
        if (settings == null)
        {
            page.CreateAssetHint("No settings asset yet: the system runs on its defaults.", "Create settings asset",
                "Creates the asset with the default values.", () => { MySettings.Create(); /* then rebuild: call page.Mount(rootElement) and Build(page) again with the element the provider received */ });
            return;
        }

        var so = new SerializedObject(settings);
        page.PresetRow("Presets", "preset", Presets, index => { MyPresets.Apply(so, index); });
        VisualElement[] tabs = page.Tabs("MySystem.SettingsTab", ("Essentials", "star"), ("Advanced", "settings"));

        VisualElement world = VeneerStyle.Card("World", "globe", MyBrand.Value);
        world.Add(VeneerControls.NumberRow(so.FindProperty("gravity"), "Gravity", "m/s2"));
        world.Add(VeneerControls.SliderRow(so.FindProperty("quality"), "Quality", "Fast", "Stable"));
        tabs[0].Add(world);
        tabs[0].Bind(so);                       // bind once the tree exists; controls also bind themselves where they must

        VisualElement live = page.LiveCard();   // visible only in Play Mode
        live.Add(VeneerStyle.Kpi("Awake", "play", out Label awake, MyBrand.Value));
        page.Every(() => awake.text = MyWorld.AwakeCount.ToString());
        page.RefreshPresets(i => MyPresets.Matches(so, i));
    }
}
```

## Worked example 3: a tool window

```csharp
internal sealed class MyDebuggerWindow : VeneerWindow
{
    private readonly VeneerSeries stepSeries = new(300);
    private VeneerGraph graph;
    private Label stepValue;

    [MenuItem("Window/My System/Debugger")]
    private static void Open() => GetWindow<MyDebuggerWindow>().Show();

    protected override VeneerBrand Brand => MyBrand.Value;
    protected override string WindowTitle => "My Debugger";

    protected override void BuildToolbar(VisualElement toolbar)
    {
        toolbar.Add(VeneerStyle.IconButton("refresh", "Reset the graph", () => stepSeries.Clear(), Brand));
    }

    protected override void BuildContent(VisualElement content)
    {
        VisualElement row = VeneerStyle.Row();
        row.Add(VeneerStyle.Kpi("Step ms", "step", out stepValue, Brand));
        content.Add(row);
        graph = new VeneerGraph(stepSeries);
        graph.SetAccent(Brand.Accent);
        content.Add(VeneerStyle.Card("History", "graph", Brand));
        content.Add(graph);

        Every(100, () =>
        {
            if (!Application.isPlaying) { ShowEmptyState("play", "Not playing", "Enter Play Mode to see the live numbers."); return; }
            HideEmptyState();
            stepSeries.Add(MyWorld.StepMilliseconds);
            stepValue.text = stepSeries.Latest.ToString("0.00");
            graph.Tick();
            SetPill("Running", "ok");
        });
    }
}
```
