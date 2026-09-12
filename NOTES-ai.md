# Notes for the owner of `AiInspectionToolFunctions.cs`

## 1. Tool permission gaps (orchestrator registry — I own that file, but flagging the intent)

`findEmployee`, `listGroups` and `getGroupDetail` are registered in
`AiAssistantOrchestrator.Tools` with `RequiredPermission: null`, i.e. anyone holding the blanket
`AiAssistant.Use` policy can enumerate every user's name and email address and every group's
membership. Those tools are not self-scoped to the caller, so `null` is wrong.

I did **not** change them in this branch, because the right permission depends on what
`AiInspectionToolFunctions` already enforces internally (it takes `userId` but the registry is
the only gate the orchestrator applies). Suggested exact change in
`ShiftFlow.Web/Application/AI/AiAssistantOrchestrator.cs`, `Tools` list:

```csharp
new(ChatTool.CreateFunctionTool("findEmployee", …),
    PermissionCatalog.InspectionOrderManage),   // was: null

new(ChatTool.CreateFunctionTool("listGroups", …),
    PermissionCatalog.GroupManage),             // was: null

new(ChatTool.CreateFunctionTool("getGroupDetail", …),
    PermissionCatalog.GroupManage),             // was: null
```

Rationale for those specific permissions: the only reason the assistant resolves an employee or
a group is to fill in `createInspectionOrder` / `create|add|removeGroupMember`, all of which are
already manager-gated — so a non-manager has no legitimate use for the lookup. If a non-manager
flow does need `listGroups` (e.g. "which groups am I in?"), the better fix is a new
self-scoped tool in `AiInspectionToolFunctions` that returns only the caller's own groups,
leaving the unscoped `listGroups` behind `GroupManage`.

## 2. Defensive change already made on my side

`AiAssistantOrchestrator.DispatchToolAsync` now catches **every** exception from a tool call
(previously only `InvalidOperationException`), logs it, and returns
`{ error = "action_failed", … }` to the model with a generic message. `OperationCanceledException`
is deliberately re-thrown. So tool functions no longer need to wrap `DbUpdateException` /
`KeyNotFoundException` / `HttpRequestException` themselves to avoid aborting the turn — but
`InvalidOperationException` is still the only exception whose `.Message` is surfaced to the
model, so keep using it for messages meant for the user.
