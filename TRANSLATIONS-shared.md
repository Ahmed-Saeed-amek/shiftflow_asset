# New EN → AR strings needed (shared layer)

Append these to `ShiftFlow.Web/Localization/Translations.cs`. Added by the shared-layer pass;
every other key used by the shared partials already exists in the table.

| English | Arabic |
| --- | --- |
| `More` | `المزيد` |
| `More actions` | `إجراءات أخرى` |
| `Reference Data` | `البيانات المرجعية` |

Notes:
- `Reference Data` is the sidebar group heading shown instead of `Administration` to users who
  hold no administrative permission (the group then contains only lookup lists).
- `More` / `More actions` are the label and `aria-label` of the header overflow menu
  (`_HeaderMoreMenu.cshtml`).
