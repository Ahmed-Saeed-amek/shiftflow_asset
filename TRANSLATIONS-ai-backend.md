# Translations needed — ai/ai-backend

New EN → AR strings this branch would use. Nothing here is wired into `Loc.T(...)` yet: the
`TranslationsTests.EveryKeyUsedByLocCallsExists` test fails for any key that isn't in
`Translations.cs`, and this branch doesn't own that file. Whoever merges should add these to
`Translations.cs` and then switch the noted call sites over to `_loc.T("...")`.

| English | Arabic | Where |
|---|---|---|
| Confirmation token is required | رمز التأكيد مطلوب | `AiAssistantController.Confirm` / `.Dismiss` — currently returned unlocalized (400 guard for a malformed request body) |
| Done — the action has been carried out. | تم تنفيذ الإجراء. | `AiAssistantOrchestrator.ConfirmAsync` — already emitted in Arabic via a `lang == "ar"` branch, not via `Loc.T`; move to `Translations.cs` if that pattern is consolidated |
| That confirmation has expired, was already used, or isn't yours. | انتهت صلاحية هذا التأكيد أو تم استخدامه بالفعل أو أنه لا يخصك. | `AiAssistantOrchestrator.ExecutePendingAsync` — reaches the user as assistant text |
| You don't have permission to do that. | ليس لديك صلاحية للقيام بذلك. | `AiAssistantOrchestrator` — tool/permission rejection |

## Note on the other assistant-facing English strings

Tool results (card field labels like "Open work orders", table column headers like "Priority",
confirm summaries like "Retire A-1 …") are deliberately **not** localized in C#. They are written
for the model, which is instructed to reply in the user's language and rewrites/renders them
itself; localizing them would give the model two languages in one turn. If the owner later wants
the attachment chrome (column labels, card field labels, confirm action labels) localized too,
that is a follow-up: they all live in the `Ai*ToolFunctions` classes and would need the
`ILanguageService` injected.
