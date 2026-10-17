# Changelog

## [1.4.1] - 2026-10-17

### Added
- `VeneerStyle.TabSwitcher` is public: windows that build their own tab bar with `VeneerStyle.Tabs` can switch their pages with the same fade + slide as `TabView` (`Switch`, `Snap`, `Animating`).

### Fixed
- The X / Y / Z / W letters inside Vector2 / Vector3 / Vector4 / Rect fields no longer get the label-column width, so the number boxes stay inside the card.

## [1.4.0] - 2026-10-17

### Added
- **Veneer icon and brand** (`Editor/Brand/logo.png`, `VeneerKitBrand`): the Inspector Designer, the Theme Editor and the Project Settings page show it on their tabs and title bars. The brand also appears as "Veneer" in the Theme Editor's Systems list, so the kit's own windows can be themed too.
- **Motion:** hover/press/focus animations for buttons, chips, segmented buttons, tabs and text fields (USS); a slider with a fill and a growing, accent-coloured handle; tab content slide + fade; show-if reveal, chip pop and pill pulse (`VeneerStyle.Reveal`); an animated, themed dropdown (`VeneerDropdown`, `VeneerControls.EnumDropdown`, `VeneerInspectorBuilder.Dropdown`, `VeneerWidget.Dropdown`) that plain enum fields now use; the theme setting **Motion** (Off / Subtle / Normal / Lively, `VeneerTheme.Motion`, `VeneerMotionKit`).
- All 54 system Inspector editors are layout-driven (see 1.3.0), new engine features used by them: toggle icons and same-row chips, section headings, soft ranges and min-max sliders, show-if on tabs and cards, 'Other' card in the last tab, header-badge / after-header / footer items, item claims. Not run in Unity yet.

### Changed
- Kit-wide spacing: children of a row keep the 6 px gap (the weight of the row rules was raised above the margin resets of single components); a wrapped row no longer cancels its bottom gap.

## [1.3.0] - 2026-10-17

### Added
- **Layout-driven system editors:** `VeneerLayoutDrivenInspector<T>`, `IVeneerDefaultLayoutProvider` / `IVeneerLayoutDriven`, `VeneerDefaultLayouts`, the fluent `VeneerLayoutBuilder`, custom items and widgets (`VeneerCustomItems`, slots Item / HeaderBadge / Footer, `VeneerInspectorBuilder.Poll`). The Designer shows a system's default design read-only with **Customize** (copy to `Assets/Veneer Layouts/`) and **Reset to system default**.
- **Nested paths** in layouts and in the Designer's field list (expandable structs, "partly placed" marks); the *Other* card keeps unplaced leaf fields; show-if on nested paths, layer masks and enums.
- `VeneerWidget.Tag`, slider end texts (`SliderLeft` / `SliderRight`), enum button icons (`EnumIcons`) as entry options.
- The Designer warns about a type's own (non-layout-driven) editor also in the "No layout yet" state.
- Pilot: Interaction System's `InteractData` Inspector is layout-driven.

## [1.2.0] - 2026-10-17

### Added
- **Theme Editor** (`Tools > Veneer > Theme Editor`, `Editor/ThemeEditor/`): a live sample built through the kit's real path, per-setting overrides, autosave with Undo, Create / Reset all / Delete. Not run in Unity yet.
- **Themes** (`Editor/Theme/`): `VeneerTheme` assets restyle one system (by `VeneerBrand.Name`) or all of them (empty name = global base): accent, card colour and border, text colours, corner radius, density, tab style, logo and banner. `VeneerThemeRegistry`, `VeneerThemeSheet`, `VeneerBrandCatalog`. Applied automatically by `VeneerStyle.Root` / `ApplyBrand`; open Inspectors and windows rebuild when a theme changes.
- Tab styles `vn-tabstyle--pill` and `vn-tabstyle--boxed`.

### Changed (later the same day)
- **Themes are reusable looks.** The Theme Editor's left list has Global, Systems (each system's own look, filled in, no Create button: the asset is made at the first change) and Themes; **+ New theme** adds a named theme as a full snapshot of the selected look; rename and delete (layouts using a deleted theme fall back to the default look). Corner radius and density are sliders (`VeneerTheme.RadiusScale` 0..2, `DensityScale` 0.6..1.5) instead of three presets; tab style is unchanged.
- **Theme per script.** `VeneerLayout.Theme` / `ThemeBrand` choose the look of a script's Inspector (a custom theme or a system's look); `UseThemeLogo` and `VeneerLayoutBanner.UseThemePicture` take the header icon and the banner from it. The Designer's Header pane has a Theme card and Theme/Own choices for icon and banner. New API: `VeneerThemeRegistry.Custom/FindCustom/EffectiveFor`, `VeneerStyle.ApplyTheme`, `VeneerLayoutTheme.ThemeBrandOf/ThemeLogo/ThemeBanner/ApplyTo`.

### Changed
- **Designer and Theme Editor windows resize like a professional tool:** the two side panes have a fixed width (draggable between limits) and only the centre grows; every pane scrolls instead of clipping; property rows have a fixed label column and capped controls; the Designer's tree/preview split is draggable. The Theme Editor shows the system's CURRENT look (accent, colours, radius, density, tab style, logo, banner) in its controls while an override is off, and ticking an override starts from that value.
- `Veneer.uss`: corner radii and spacings are variables (`--vn-radius-*`, `--vn-gap`, `--vn-pad-*`, `--vn-row-h`, `--vn-ctl-h*`, `--vn-tab-h`) whose defaults equal the old numbers; with no theme the look is unchanged (checked value by value). Margin/padding shorthands became side-by-side longhands.
- `VeneerStyle.ApplyBrand` also runs for the default brand; `VeneerBrand.Logo` / `Banner` honour the theme; `VeneerWindow` gets `RebuildOnThemeChange`.

## [1.1.0] - 2026-10-17

### Added
- **Inspector Designer** (`Tools > Veneer > Inspector Designer`, `Editor/Designer/`): restyle the Inspector of any MonoBehaviour or ScriptableObject without code. Fields pane, layout tree (tabs > cards > fields) with drag and drop, properties pane (icons, script icon, banner with size and preserve aspect, accent colour, label/tooltip/unit/widget/read-only/show-if per field), live preview, kit icon picker, autosave, undo and warnings (own custom editor, missing fields, unsupported scripts). Not run in Unity yet.
- The banner is part of the header: with a banner image and "Header text on banner" on, the header icon, title and subtitle are drawn on the picture (`VeneerLayoutBanner.Fit` Cover/Fit/Stretch replaces `PreserveAspect`; new `OverHeader`, `Dim`, `IconSize`). In the Designer the banner is a card of the Header row.
- Notes in cards (Info / Warning / Error message boxes with free text, `VeneerLayoutEntry.IsNote`) and three new show-if conditions (`IsNull`, `IsEmpty`, `NotEmpty`). The Designer has an Add note button, a note pane and a readable condition popup.
- **Layout engine** (`Editor/Layout/`): `VeneerLayout` asset, `VeneerLayoutRegistry`, `VeneerLayoutRenderer`, `VeneerLayoutTheme` (companion `.uss` for a custom accent), `VeneerScriptIcon`, and two generic editors for `MonoBehaviour` and `ScriptableObject` that draw a layout when the type has one and Unity's plain default Inspector otherwise. Types with their own editor are never overridden. `Project Settings > Veneer` has a switch that turns the generic editors off.

## [1.0.0] - 2026-10-17

### Added
- Named **Veneer** (the working title was "Studio Editor Kit": its classes were `Studio*`, its define `HAS_STUDIO_EDITOR_KIT`, its style classes `sk-*`; they are `Veneer*`, `HAS_VENEER` and `vn-*` now).
- First version: the editor-only assembly `Veneer.Editor` (UI Toolkit, Unity 6000.3).
- Style sheet `Styles/Veneer.uss` (`vn-*` classes, dark and light skin, accent variables and nine accent presets), extracted from the
  Optimized Rigidbody editor sheet and generalised.
- 104 white glyph icons in `Icons/` (33 copied from the Optimized Rigidbody set, the rest generated in the same style).
- `VeneerBrand` (name, subtitle, accent, logo, banner, extra icon folder and sheet), `VeneerStyle` (root, cards, pills, buttons,
  segmented icons, chips, tabs, banner, hints, KPI cards, bars, toolbar, search field, empty state), the icon import postprocessor.
- Field controls in `VeneerControls`: `NumberRow`, `SliderRow`, `ToggleChip`, `FlagChips`, `EnumSegmented`, `ObjectSlot`, `ListEditor`,
  `Vector3Row`, `ColorRow`, `CurveRow` (multi-object editing, prefab override marks, Undo/Redo refresh).
- The builder layer: `VeneerInspector`, `VeneerInspector<T>`, `VeneerInspectorBuilder`; `VeneerAutoInspector` (layout from the data alone).
- `VeneerSettingsPage` (Project Settings pages), `VeneerWindow` (tool windows), `VeneerGraph` / `VeneerSparkline` / `VeneerSeries` /
  `VeneerPalette` / `VeneerNumberText`.
