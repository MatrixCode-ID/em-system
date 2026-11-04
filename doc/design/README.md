# Em design library

Layout and mockup designs for the Em engine, made with FigCraft (`.figcraft`). Every engine design lives
in this folder; screens of engine-owned modules sit at the top level as `<Subject>.figcraft`.

| File | Contents |
| --- | --- |
| `_BASELINE/em-wpf.figcraft` | **Em WPF Baseline**: assets (page `00 Assets`), `Em Palette` variables (Light/Dark), Main Window, Login, Context Menu, Buttons & Toolbar |
| `BusinessModuleManager.figcraft` | **Business Module Manager**: drawer navigation, Tenants, Departments, Positions (with Copy from tenant), Employees, Employee Editor |

## Baseline

The baseline is product neutral: Em branding only (`doc/assets/logo/Logo.png`, title "Em System",
"© 2026 Matrix Code"). A new design starts as a copy of the baseline, gets its own document name
(`file.rename`) and drops the baseline pages it does not need, keeping `00 Assets`.

- Screens use **instances** of the components in `00 Assets`. A new asset is first created there as a
  component (`Icon/<Name>`, `Tree/<Name>`, `Button/<Name>`, ...), with its source in the description.
- Colours are bound to the `Em Palette` collection. Dark frames are duplicates of the Light frame with
  the collection mode set to Dark.
- Icons: Font Awesome 7 Free Solid, 20×20 components, glyph colour bound to a palette variable.
- WPF screens are drawn on a Full HD canvas (1920×1080).

## Working with FigCraft

Design work goes through the FigCraft control channel so it is visible in the app: start FigCraft with
`figcraft --control 7979 <file>` (the RunBay action **Open FigCraft design (control 7979)** of this repo
does this; `EM_DESIGN_FILE` picks another file) and attach MCP with `figcraft-cli mcp --attach 7979`.
Port 7979 is a local TCP control channel, not an HTTP endpoint, and other local design tools may use the
same port: check which app owns it before attaching.

Before committing a design file, make sure it carries no unused images or version history from another
product (the file is a zip; `images/` and `versions/` entries are kept until removed).

## Business Module Manager (2026-11-05)

- Navigation between BMM pages uses a **left drawer with a TreeView**, replacing the tab strip of the
  current screen. The tree items are filled **manually** by BMM, not generated from the menu.
- Two node kinds: **Folder** (`Tree/Folder`, a group card with icon tile, title with child count, subtitle
  and expand/collapse chevron) and **App** (`Tree/App`, an indented card with icon tile, title and
  subtitle). App nodes have **no arrow, pin or overflow (three-dot) button**.
- Icon colours: glyph `secondary` on a `secondaryContainer` tile. The selected App node uses
  `primaryContainer` with an `onPrimary` glyph on a `primary` tile.
- Current tree: **Organization** → Tenants, Departments, Positions; **People** → Employees.
- Page `10 Business Module Manager`: Tenants, Departments, Employees and Positions, Light/Dark. Toolbar order:
  tenant selector and Refresh on the left, page tools in the middle, the primary New action on the right.
  The Departments/Employees tenant combo shows the tenant name with its code as a subtitle.
- **Positions** (2026-11-05): the position list a tenant offers (e.g. CEO, Engineer), without level or rank.
  Left: positions with active employee count, code and INACTIVE chip; right: Code, Name and Note. Toolbar:
  tenant, Refresh, **Copy from tenant**, Save, Deactivate, New position. The **Copy from tenant** dialog
  (Light/Dark frames) picks a source tenant and checks the positions to copy; codes that already exist in
  the target are marked EXISTS and cannot be checked. Discussion: [tenant-position idea](../ideas/tenant-position.md).
- Page `20 Employee Editor`: the dedicated editor navigation, main data panel | splitter | sub detail tabs
  with independent default selection, Light/Dark, one state per tab. **Position** is a combo of the
  tenant's active positions (no free text):
  - **Communications**: entry list (kind, value, DEFAULT/INACTIVE chips) and the selected entry's Kind and Value.
  - **Addresses**: entry list (label, location with ZIP below, DEFAULT chip), Add address, and the
    selected address's Label, ZIP and multi-line Address. Save needs an active default address with a location.
  - **Contact** (2026-11-05): a saved employee keeps its tenant and contact; there is no Search contact
    button. The field is **Contact full name**, editable, with the linked user below it as read-only
    text (the user name is changed in User Manager, not here). **New employee**
    (Light/Dark) is the only state with **Search contact**, to pick an existing person or create a contact
    before the first save.
  - **Note**: the employee's own multi-line note (per tenant, not the contact's), after Department.
- Sample data is fictitious. This is a design for review, not an implementation decision.
