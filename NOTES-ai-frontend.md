# AI assistant frontend — notes for the merger

Branch `ai/ai-frontend`. Touches the assistant UI only: the chat markup, its script and
stylesheet, the layout wiring, and the floating launcher. No controller, no `Translations.cs`,
no feature view was edited.

## What's where

| File | Role |
| --- | --- |
| `Views/Shared/_AiChat.cshtml` | **new** — the shared chat: message log, context pill, composer, chip strip. Rendered by both the page and the drawer. |
| `Views/Shared/_AiDrawer.cshtml` | **new** — Bootstrap offcanvas (end side) wrapping `_AiChat`. |
| `Views/Shared/_FloatingAvatar.cshtml` | the bubble now opens the drawer (`data-bs-toggle="offcanvas"`); its hover card is gone. |
| `Views/AiAssistant/Index.cshtml` | page header + avatar card + `_AiChat`. No config, no `@section Scripts`. |
| `Views/Shared/_Layout.cshtml` | emits `<meta name="ai-context">`, `window.aiAssistantConfig`, `ai-assistant.css`/`.js`, and the drawer. |
| `wwwroot/js/ai-assistant.js` | all behaviour. Razor-free. |
| `wwwroot/css/ai-assistant.css` | page + drawer + attachment styles. |
| `wwwroot/js/site.js` | `initFloatingAvatar` lost its toggle half (Bootstrap opens the drawer now). |
| `wwwroot/css/site.css` | floating-avatar block: `.avatar-card` rules removed, `body.ai-drawer-open` hides the bubble. |

Only one chat instance is ever on a page: the layout skips both `_FloatingAvatar` and
`_AiDrawer` on the AI Assistant page itself, so `_AiChat`'s element ids stay unique.

## The one line detail views should add

Nothing is required — the layout already derives a usable context from the route. But a detail
view that knows its own record should say so precisely, at the top of the view:

```csharp
ViewData["AiContext"] = new { entityType = "WorkOrder", entityId = Model.Id, title = Model.WorkOrderNumber };
```

`entityType` values the frontend understands today: `WorkOrder`, `InspectionOrder`,
`MaintenanceOrder`, `Asset`, `Zone`, `SparePart`, `Contract`, `Vendor`, `User`, `Group`.
`title` is what the user sees in the context pill ("Viewing WO-2026-0004"), so use the human
identifier (order number, asset tag, vendor name) rather than a database id.

Worth adding to at least: `WorkOrders/Details`, `Orders/Details`, `Assets/Details`,
`Contracts/Details`, `Vendors/Details`, `SpareParts/Details`.

**Fallback when it isn't set:** `{ page = "{controller}/{action}", title = ViewData["Title"],
entityType = <mapped from controller, only when the route has an id>, entityId = RouteData["id"] }`.
`entityType` is deliberately null on list pages — claiming the user is looking at one record when
they're looking at all of them would be worse than saying nothing.

## Contract the client implements

`POST /AiAssistant/Query` — body `{ text, history: [{role,text}], context?: {...} }`, anti-forgery
header as before. Response `{ answerText, voice, attachments }`.

- Assistant turns in `history` carry **only** `answerText`; attachments never go back up.
- Voice reads `answerText` only, never attachment content.
- A missing/empty `attachments` renders fine — this branch works against today's text-only
  endpoint as-is.
- Attachment kinds rendered: `table`, `cards`, `links`, `download`, `confirm`. An unknown `kind`
  is skipped silently rather than breaking the turn.
- `confirm` → Confirm posts `{ token }` to `/AiAssistant/Confirm` and renders its
  `{ answerText, attachments }` as a new assistant turn; Cancel posts `{ token }` to
  `/AiAssistant/Dismiss` (fire-and-forget) and marks the card cancelled. The settled state is
  stored on the attachment inside the persisted turn, so it survives re-render, navigation and
  reload — the same token can't be confirmed twice from the UI.

### Table rows

`_url` makes the row clickable (`tr[data-href]`, picked up by site.js's delegated handler) and
turns the first cell into a link. `_status` renders the first status-like cell as a badge —
"status-like" means the cell value equals `_status`, or the column key matches
`/status|state|priority|stage|condition/i`.

## Things the merger should know

1. **The Speech SDK is now lazy.** It used to be a `<script>` tag on the AI Assistant page; it's
   now injected by `loadSpeechSdk()` on the first voice request. The drawer exists on every page,
   so eagerly loading ~1MB of SDK app-wide was not an option. The CDN URL lives in
   `aiConfig.urls.speechSdk` in `_Layout`.
2. **Arabic badge labels are partly untranslated.** `window.i18n.status` only carries
   `StatusStyle.JsLabelNames`, which omits e.g. "Pending Approval", "Overdue", "Low Stock".
   `StatusStyle.cs` is outside this branch's allowed files — adding those names to
   `JsLabelNames` is a one-line change that fixes the remaining English badges in RTL.
3. **`NavPermissionSet` already had the property** (`CanUseAi`, not `CanUseAiAssistant`), so
   `Services/NavPermissions.cs` was not touched.
4. **New EN→AR strings** are listed in `TRANSLATIONS-ai-frontend.md` and need appending to
   `Translations.cs` — until then the new chips/labels render in English under Arabic.
5. **Session state keys**: `step.ai.conversation` (turns, capped at 40) and
   `step.ai.drawer.open`. Both `sessionStorage`, both read/written inside try/catch so a private
   window or blocked storage degrades to a non-persistent chat rather than a broken one.
6. The avatar (WebRTC video) stays full-page only. In the drawer, voice falls back to
   audio-only TTS — the drawer has no `#avatarScene`, and every avatar call site is guarded.

## Verified in the browser

Chromium via Playwright against a local dev server, admin login, with `/AiAssistant/Query`
stubbed to return one of each attachment kind:

- `<meta name="ai-context">` on `/WorkOrders/Details/1` →
  `{"page":"WorkOrders/Details","title":"WO-2026-0001","entityType":"WorkOrder","entityId":"1"}`
- chips on that page → Summarize / Asset history / Send to vendor (default set elsewhere)
- badges → `bg-danger-subtle…` for Blocked, `bg-info-subtle…` for `InProgress` rendered as
  "In Progress", `bg-warning-subtle…` for `PendingApproval` → "Pending Approval"
- auto-links → `WO-2026-0004 → /WorkOrders?search=…`, `AST-0001 → /Assets?q=…`,
  `INS-2026-0003 → /Orders?search=…`
- confirm → Confirm disables both buttons, posts the token, renders the follow-up turn, and the
  card still reads "Done" after navigating to another page
- Escape closes the drawer and focus returns to the launcher bubble
- conversation and drawer open/closed state survive navigation
- 390px: full-width drawer, single-column cards, no horizontal page scroll
- Arabic: drawer slides in from the start (left) side, pill/table/chips all mirrored
