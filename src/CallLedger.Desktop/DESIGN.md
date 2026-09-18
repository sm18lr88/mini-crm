# Call Ledger Design System

## 0. Research Log

- Existing system: retained the calm local-ledger structure, but changed the default visual contract to dark charcoal and ink so sensitive call work remains readable in a small always-on-top window.
- Lazyweb: skipped. The task restricts agent execution to build-only foreground commands and prohibits browser work, so shipped-screen research could not be viewed safely.
- StyleGallery: skipped. Its external HTTP recipe is outside the permitted command surface; the list-detail model is documented from the loaded layout reference instead.
- Imagen drafts: skipped. No image-generation tool or concrete visual reference was supplied, and this native utility does not need decorative imagery.
- UI UX database: skipped. Its Python command is outside the permitted command surface.

## 1. Atmosphere & Identity

Call Ledger is a calm dark ledger for a small, sensitive workflow. It uses ink-black canvas, charcoal working surfaces, quiet green-gray rules, and a restrained teal-green only for intent, selection, focus, and the next useful action. The signature is the call workflow: the selected person, phone, timing, and reason stay legible without turning the app into a metrics dashboard.

## 2. Color

| Role | Resource key | Value | Usage |
|---|---|---|---|
| Canvas | `CanvasBrush` | `#0E1413` | App background |
| Surface | `SurfaceBrush` | `#141D1B` | Main regions and editors |
| Surface muted | `SurfaceMutedBrush` | `#1B2724` | Table header and compact selector |
| Surface elevated | `SurfaceElevatedBrush` | `#202D29` | Inputs and dialogs |
| Text primary | `InkBrush` | `#E8F2EE` | Names and body copy |
| Text secondary | `MutedInkBrush` | `#B5C8C0` | Supporting copy |
| Text subtle | `SubtleInkBrush` | `#849B92` | Metadata and disabled text |
| Rule | `RuleBrush` | `#3A4C46` | Whisper borders and dividers |
| Accent | `AccentBrush` | `#197A59` | Primary actions, active filter, focus |
| Accent hover | `AccentHoverBrush` | `#115D42` | Pressed primary actions |
| Accent soft | `AccentSoftBrush` | `#1D4235` | Selected rows and due call rail |
| Accent text | `AccentTextBrush` | `#F4FFFB` | Text on primary actions |
| Warning | `WarningBrush` | `#F0C66D` | Due and caution text |
| Warning soft | `WarningSoftBrush` | `#3B311B` | Due indicator surface |
| Danger | `DangerBrush` | `#FF9A92` | Destructive action and errors |
| Danger soft | `DangerSoftBrush` | `#3B2526` | Error surface |
| Focus | `FocusBrush` | `#66D9AC` | Keyboard focus outline |

Rules: colors are consumed only through the keyed resources in `Themes/Theme.xaml`. Accent is reserved for actions, selection, focus, and due context. Sensitive masking does not use a color as its only cue.

## 3. Typography

| Level | Resource key | Size | Weight | Use |
|---|---|---:|---:|---|
| Title | `TitleTextSize` | 24 | SemiBold | Window title and selected name |
| Section | `SectionTextSize` | 16 | SemiBold | Editor sections and table heading |
| Body | `BodyTextSize` | 14 | Normal | Fields and primary table cells |
| UI | `UiTextSize` | 13 | SemiBold | Buttons, filters, labels |
| Caption | `CaptionTextSize` | 12 | Normal | Help, timestamps, status |
| Micro | `MicroTextSize` | 11 | SemiBold | Compact metadata |

- Primary family: `Segoe UI`, with `Segoe UI Variable Text` where Windows supplies it.
- Icon family: `Segoe MDL2 Assets` only for compact field-action glyphs.
- Mono family: `Consolas` for keyboard shortcut labels only.
- Typography uses device-independent pixels (DIPs): title 24, section 16, body 14, UI 13, caption 12, and micro 11. No external font is loaded.

## 4. Spacing & Layout

The base unit is 4 device-independent pixels.

| Token | Resource key | Value | Use |
|---|---|---:|---|
| 1 | `Space1` | 4 | Icon adjacency |
| 2 | `Space2` | 8 | Tight controls |
| 3 | `Space3` | 12 | Field rhythm |
| 4 | `Space4` | 16 | Standard region padding |
| 6 | `Space6` | 24 | Section separation |
| 8 | `Space8` | 32 | Major separation |

- Expanded layout: a fixed expanded header, then a list-detail split. The customer table owns both its vertical and horizontal scroll; the editor owns a separate vertical scroll. Both are bounded by the window body.
- Compact layout: a small fixed header with only title, pin, Expand, and New; a customer picker; one scrollable shared editor; and a fixed action footer. It never nests another unnamed scroll owner.
- Expanded target size: 1200 by 800. Compact target size: 400 by 600. The layouts preserve a practical minimum rather than simulate a mobile web page.
- Long content wraps in editor fields and truncates in table cells by design. A narrow expanded window preserves the editor as the content authority.

## 5. Components

### Ledger Shell
- **Structure**: title and command bar, due summary, expanded list-detail, or compact title/actions plus a customer picker.
- **States**: loading, empty, ready, busy, sanitized startup failure.
- **Accessibility**: `Ctrl+N`, `Ctrl+S`, `Ctrl+F`, and `Esc`; visible focus and a native title bar.
- **Layout**: list-detail in expanded mode, bounded editor scroll in compact mode, and a fixed Save / Mark called / Cancel / Delete footer in both modes.

### Ledger Button
- **Variants**: primary, secondary, quiet, destructive, field action.
- **Spacing**: `Space2` horizontal adjacency and `Space3` control padding.
- **States**: default, hover, pressed, keyboard focus, disabled, busy.
- **Accessibility**: every icon-only button has an automation name and tooltip.

### Field Editor
- **Structure**: phone, timing, reason, and notes directly after name; identifier fields later; every field retains copy and paste actions, with an optional reveal action for identifiers.
- **Variants**: single-line, multi-line notes, hidden sensitive field.
- **States**: editable, masked read-only display, revealed, invalid, busy.
- **Accessibility**: each copy action states that it copies the actual value and warns about Windows clipboard history. A mask is display-only and never becomes draft data.

### Due Indicator
- **Structure**: short label, count, and contextual call timing.
- **States**: no due calls, due, upcoming, unscheduled.
- **Accessibility**: status uses both text and color.

### Empty State
- **Structure**: plain explanation with one relevant action.
- **States**: no customers, no search matches, no selected customer.
- **Layout**: centered within its owning pane without a decorative card.

## 6. Motion & Interaction

- Native hover, pressed, and keyboard-focus feedback is immediate and quiet. No decorative animation is added to a data-entry surface.
- Any state transition uses opacity only if added later; layout-changing animation is prohibited.
- Field reveal is an explicit button action, not a hover or timed state.
- The busy state disables conflicting write actions while the local encrypted store operation is running.

## 7. Depth & Surface

The strategy is tonal shift plus whisper borders. Ink canvas, charcoal rails, and elevated dark inputs create hierarchy; borders are one-pixel `RuleBrush` lines. No card grid and no heavy shadow system is used. Inputs, the table, and both scrollbars remain dark so no bright system surface interrupts the workflow.

## 8. Accessibility Constraints & Accepted Debt

### Constraints

- Target WCAG 2.2 AA contrast and visible keyboard focus for every interactive control.
- The selected customer, dirty status, due state, save success, error state, and sensitive mask must all be intelligible without color alone.
- Sensitive values are masked by default in the editor and table. Per-field reveal is explicit, and the persistent preference applies to both surfaces.
- Field actions use 28 by 28 device-independent-pixel hit targets, with AutomationProperties names and tooltips.
- Clipboard copy communicates the Windows clipboard-history risk; the application does not make a false security promise by auto-clearing it.

### Accepted Debt

| Item | Location | Why accepted | Owner / Exit |
|---|---|---|---|
| Full visual inspection is delegated to the parent smoke-runner | `--render-smoke` output | Agent execution is prohibited from opening a GUI or browser | Parent inspects expanded and compact PNG captures |
