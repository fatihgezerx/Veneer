# Changelog

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
