# Changelog

## [Unreleased]

### Changed
- **Layouts and custom themes are saved inside the Veneer folder:** `<Veneer folder>/Data/Layouts` (Designer) and `<Veneer folder>/Data/Themes` (Theme Editor) instead of `Assets/Veneer Layouts` and `Assets/Veneer Themes`. The Veneer folder is found through the kit's own location (`VeneerDataFolders`, new), so the data travels with the kit wherever it sits; missing folders are made on demand. Existing assets were moved with their `.meta` files, so nothing loses its references.

## [1.7.2] - 2026-10-19

### Changed
- **Every field label is shown in Title Case** ("Starts At", "Attack Speed", "Poise Bonus (Super Armor)", "Fwd-Left"), whatever the system wrote in its layout or took from the field name. One shared function, `VeneerText.TitleCase`, is applied where a row gets its label: `VeneerStyle.FieldRow` (number, enum, dropdown, switch, checkbox, radio, object, colour, curve, vector rows), the slider and min-max rows, list titles (`ListEditor` and item lists), toggle chips, and the plain field the builder makes from a `PropertyField`. Only a lower-case first letter of a word (after a space, a hyphen or an opening parenthesis) is raised; `NPCs`, `HDR`, `UV` and words starting with a digit or symbol stay as they are, and a label that is already right is returned unchanged (no allocation). Card titles, section headings, notes, hints, tooltips and button texts are not changed. The Designer's own field rows use the same helper, so they are Title Case too.

## [1.7.1] - 2026-10-19

### Added
- **`bow` icon** (`Icons/bow.png`): a drawn bow with an arrow, same outline style as `sword`, `shield` and `bolt` (64x64, white, 3.5 px stroke).

## [1.7.0] - 2026-10-18

### Added
- **Item design** for a list of a `[Serializable]` class (`VeneerLayout.Items`, `VeneerItemLayout`, `VeneerItemList`): a card or a flat row per element with a drag handle, a title field, a count pill, fold, remove, Add, an empty text, drag-to-reorder and nested lists (three levels). Entry paths are relative to the element and use every entry option (widget, label, width / height, picture size, same line, show-if...). `VeneerItemDecorators.Register(type, listPath, ctx => ...)` lets a system colour or tool-tip a row. Builder methods `ItemList`, `ItemParts`, `Item`, `Size`, `PictureSize`, `HideLabel`; `VeneerLayoutEntry.HideLabel`. The Designer has an "Item design" card in the Properties pane and shows the element's fields as informational rows (`groups[].header`).
- **`VeneerModelPreview`**: a UI Toolkit 3D model preview (drag turns, middle button pans, the wheel zooms towards the pointer; overlay zoom in / zoom out / reset buttons and a segmented group of named views the camera glides to; keyboard: arrows, Shift+arrows, +/-, Home). Model-agnostic: it shows an `IVeneerModelPreviewSource`; `VeneerModelPreviewScene` wraps `PreviewRenderUtility`. Also `VeneerModelFocus`, `VeneerModelCamera`, `VeneerModelPreviewSettings`, `VeneerModelPreviewOrbit`, `Styles/VeneerModelPreview.uss`.
- **`VeneerTimeline`**: lanes of spans and draggable grips (pointer capture, snapping to a grid, to other grips and the playhead, to frames with Shift, free with Ctrl/Cmd, Esc cancels), a playhead, a hover / drag bubble with seconds and frame, keyboard and navigation parity, its own style sheet (Motion-aware). API: `AddLane`, `AddSpan`, `SetSpan`, `AddHandle`, `SetHandle`, `SetHandleVisible`, `SetPlayhead`, `Span`, `SnapStep`, `FrameDuration`, `ShowRuler`, `PlayheadEnabled`, events `HandleMoved`, `DragStarted`, `DragEnded`, `PlayheadMoved`.

- Item lists: `VeneerItemLayout.AddAtEnd` (builder `ItemAddAtEnd`, a switch in the Item design card: no header line, the Add button under the items), `VeneerItemContext.Count` (the count pill of a card head, for a decorator), `VeneerItemDecorators.RegisterNew` / `TryGetNew` (what a new element starts as after Add; without it a copy of the last one).

### Changed
- **No elastic easing anywhere:** the dropdown's opening height, and the press / release spring of buttons, chips, segments and icon buttons in the Lively motion setting, use the back ease (a single small overshoot) instead of the elastic one, which wobbled several times and made controls jitter on mouse-up and when the pointer left them.
- **Narrow Inspectors:** the text of a tab shrinks with an ellipsis first; when the bar is too narrow (under about 64 px per tab) a tab that has an icon shows only the icon (`vn-tab--compact`); tabs never overlap or run out of the bar. Segmented buttons and radio lists shrink the same way (their text gets an ellipsis) instead of running past the card.

## [1.6.0] - 2026-10-18

### Added
- **Size per field** (`VeneerLayoutEntry.Width` / `Height`, pixels, 0 = automatic): any field can be given a width and a height in the Designer; a width never goes past the card.
- **Picture size** (`PictureWidth` / `PictureHeight`) for an object reference: the small preview of the prefab, sprite or material can be made bigger or smaller than the default 36 px.
- **Sample data in the Designer**: the temporary instance gets one sample element in every list of a `[Serializable]` class (also inside the element, three levels deep), so a list such as Pool Data's Groups shows real rows in the preview. A *Sample picture* slot in the Fields pane fills the empty object slots of that sample (a sprite also fits a texture slot and the other way round). `VeneerFieldInfo.GetElementType` reads a list's element type from the type, so it works on an empty list.

## [1.5.0] - 2026-10-18

### Added
- **Three looks for a bool**: the chip (as before, `VeneerWidget.Toggle`), a sliding **switch** (`VeneerWidget.Switch`: `VeneerStyle.Switch`, `VeneerControls.ToggleSwitch`, `VeneerInspectorBuilder.Switch`) and a **checkbox** (`VeneerWidget.Checkbox`: `VeneerStyle.Checkbox`, `VeneerControls.ToggleCheckbox`, builder `Checkbox`). Both show a mixed multi-edit state (knob in the middle / a dash); the knob slides with an overshoot and the tick pops in, following the Motion setting.
- **Three looks for an enum**: segmented and dropdown (as before) and a **radio list** (`VeneerWidget.Radio`: `VeneerStyle.RadioList`, `VeneerControls.EnumRadio`, builder `Radio`), with an icon and a tooltip per member.
- `VeneerDropdown.CreateEnum<TEnum>` and `VeneerEnumDropdownField<TEnum>` (an enum value held in code: `EnumValue`, `EnumChanged`), `VeneerControls.EnumDropdownField` (the bare bound field for a header or a table cell). The Designer offers the new widgets in its Widget list (appended to the enum, so saved layouts keep their meaning).
- Theme Editor sample: an "Other looks" card with the switch, the checkbox and the radio list.

### Fixed
- The dropdown's list drew its rows but no text: it was added to the panel's tree, above the window root that carries the editor's default style sheet (fonts, text colours), so its labels had no font. The list now goes under the window root (the button's ancestor right below the panel tree), still drawn over everything in the window.

### Changed
- Theme Editor sample: the Limits tab is gone (Max count moved into Fields); two tabs remain, General and Lists.
- The **information hint** has a colour of its own (`--vn-info`, a calm neutral grey, with `--vn-info-bg`), the same in every system and theme, like the warning's yellow and the error's red; it no longer follows the accent.
- The chip's pop when it turns on is a nudge (1.04, plain ease-out back), no longer a bounce.
- Theme Editor sample: one card per kind of control (Fields; Toggles: chip, switch, checkbox; Choices: segmented icons, segmented text, dropdown, radio list; Ranges; Actions; Messages; Load) and no States tab: hover, press and selected states show by themselves when the pointer is on a control. `VeneerThemeEditorWindow.States.cs` is gone.
- The dropdown opens like a balloon pulled towards the button and let go: besides the elastic height it now starts a few pixels towards the button and drops into place (`translate` transition, back ease), scaled by the Motion setting.
- Every system editor that still used Unity's popup now uses the kit's dropdown: the Designer's own Widget / show-if / theme / enum rows (and its toggles became switches), the Combat profile's Body part and Style rows, the Attack Timing window's style, the Gore Studio's mesh type / rig / mesh, the Easy UI studio inspector's enum, fill-origin and dropdown-value rows, the Surface groups' entry type, the N-Hance gender row. The IMGUI fallback windows (no Veneer) keep Unity's popups.

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
- **Layout-driven system editors:** `VeneerLayoutDrivenInspector<T>`, `IVeneerDefaultLayoutProvider` / `IVeneerLayoutDriven`, `VeneerDefaultLayouts`, the fluent `VeneerLayoutBuilder`, custom items and widgets (`VeneerCustomItems`, slots Item / HeaderBadge / Footer, `VeneerInspectorBuilder.Poll`). The Designer shows a system's default design read-only with **Customize** (copy to the Veneer folder's `Data/Layouts/`) and **Reset to system default**.
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
- Style sheet `Styles/Veneer.uss` (`vn-*` classes, dark and light skin, accent variables and nine accent presets), written for the
  first editor sheet and generalised.
- 104 white glyph icons in `Icons/` (33 copied from the first set, the rest generated in the same style).
- `VeneerBrand` (name, subtitle, accent, logo, banner, extra icon folder and sheet), `VeneerStyle` (root, cards, pills, buttons,
  segmented icons, chips, tabs, banner, hints, KPI cards, bars, toolbar, search field, empty state), the icon import postprocessor.
- Field controls in `VeneerControls`: `NumberRow`, `SliderRow`, `ToggleChip`, `FlagChips`, `EnumSegmented`, `ObjectSlot`, `ListEditor`,
  `Vector3Row`, `ColorRow`, `CurveRow` (multi-object editing, prefab override marks, Undo/Redo refresh).
- The builder layer: `VeneerInspector`, `VeneerInspector<T>`, `VeneerInspectorBuilder`; `VeneerAutoInspector` (layout from the data alone).
- `VeneerSettingsPage` (Project Settings pages), `VeneerWindow` (tool windows), `VeneerGraph` / `VeneerSparkline` / `VeneerSeries` /
  `VeneerPalette` / `VeneerNumberText`.
