# -*- coding: utf-8 -*-
"""Part 3: Change Requests, Reports & Analytics, AI Assistant."""


def build_part3(ctx):
    doc = ctx["doc"]
    h1_ = ctx["h1"]; h2 = ctx["h2"]; h3 = ctx["h3"]
    para = ctx["para"]; bullet = ctx["bullet"]; numbered = ctx["numbered"]
    add_diagram = ctx["add_diagram"]; simple_table = ctx["simple_table"]
    page_break = ctx["page_break"]

    # ================= 7. CHANGE REQUESTS =================
    h1_("7. Functional Requirements — Change Requests")

    h2("7.1 Request Types")
    para(
        "The system supports six change-request types: Absence, Swap, Replacement, Overtime, TempGroupChange "
        "(“Temp Transfer”), and PermanentGroupChange. All share one status lifecycle — Pending, Approved, "
        "Rejected, Cancelled — and one review mechanism, but Temp Transfer and Overtime carry an additional "
        "employee accept/decline gate described below."
    )

    h2("7.2 Temp Transfer")
    para(
        "A Temp Transfer moves one employee, for a single shift/day only, into a different group's shift on the "
        "same date — without altering their permanent group membership. It can be initiated by a manager on an "
        "employee's behalf (the common case: covering a shortage) or self-submitted by the employee. Before it "
        "can be approved, the system re-validates that the employee still has a Scheduled (not yet attended) "
        "assignment for that date, that their source shift is still Draft, and that the target group actually "
        "has a shift that date."
    )

    h2("7.3 Overtime")
    para(
        "An Overtime request books an employee onto an additional shift on top of their regular assignment for "
        "the day — recorded in a separate table from the normal roster, since a regular assignment is limited "
        "to one row per employee per day. Validation ensures the target shift is not already Closed and the "
        "employee does not already hold overtime on that same shift."
    )

    h2("7.4 Approval Workflow")
    para(
        "A defining rule of both Temp Transfer and Overtime: when the requester is not the affected employee "
        "(i.e. a manager initiated it on someone else's behalf), a manager cannot approve it directly — the "
        "affected employee must personally accept or decline it. Self-submitted requests (employee is both "
        "requester and affected party) follow the normal manager approve/reject path. On approval, applicability "
        "is re-validated (since shift state may have changed since submission) before any data is mutated, so a "
        "request that has become invalid fails loudly and stays Pending rather than silently applying incorrect "
        "data."
    )
    add_diagram("change_request_flow.png", "Figure 7.1 — Temp Transfer / Overtime approval workflow")
    para(
        "Approving a Temp Transfer materializes an audit exception record and re-points the employee's shift "
        "assignment (group, shift, and shift type) to the destination shift, resetting attendance fields since "
        "it is a fresh attendance instance. Approving Overtime materializes the same style of audit exception "
        "record and additionally inserts a new overtime-assignment row linked back to the approving request."
    )

    h2("7.5 Other Request Types")
    bullet("Absence — on approval, sets the affected assignment's attendance status to Absent")
    bullet("Swap — two employees on the same shift date exchange their group/shift assignments")
    bullet("Replacement — marks the absent employee Replaced and creates a new assignment for the replacement employee")
    bullet("Permanent Group Change — closes the old group membership and opens a new one from the effective date, and re-points only future, not-yet-attended assignments to the new group across every schedule")

    h2("7.6 Review and Administration")
    para(
        "Reviewers holding only ChangeRequest.Review (not the organization-wide ChangeRequest.View.All) see "
        "requests scoped to their own current Work Area; holders of View.All see everything. The original "
        "requester may cancel their own still-Pending request at any time."
    )
    simple_table(
        ["Functional Requirement ID", "Requirement"],
        [
            ["FR-CR-01", "A manager-initiated Temp Transfer or Overtime request shall require the affected employee's explicit accept or decline before it can be applied."],
            ["FR-CR-02", "Approval shall re-validate applicability against current state immediately before mutating any scheduling data."],
            ["FR-CR-03", "A requester shall be able to cancel their own Pending request."],
            ["FR-CR-04", "Reviewers without organization-wide visibility shall see only requests within their own current Work Area."],
        ],
        widths=[1.7, 4.6],
    )

    page_break()

    # ================= 8. REPORTS & ANALYTICS =================
    h1_("8. Functional Requirements — Reports, Dashboard & Analytics")

    h2("8.1 Executive Dashboard")
    para(
        "Available to holders of ShiftAnalytics.View, the dashboard presents organization-wide KPIs — total "
        "active engineers, open shift tasks, pending change requests, open incidents, today's shift and "
        "overtime counts, total and defective assets, and open/critical work orders (cached for 2 minutes, "
        "keyed by role) — alongside three charts: task-status distribution (fixed category order for a stable "
        "visual), open tasks by Work Area, and incidents by severity, plus recent-activity widgets for the "
        "latest incidents and pending change requests."
    )

    h2("8.2 Task Analytics")
    para(
        "A dedicated analytics view (same ShiftAnalytics.View policy) lets a manager select a date range "
        "(today/week/month/custom) and optionally one Work Area, and see: completion-rate KPIs, a status donut "
        "chart, a daily completion trend line, per-Work-Area and per-Shift-Group breakdown tables, and a list of "
        "the most recently handed-over (rolled-over) tasks — effectively an audit view of what didn't get "
        "finished and was carried forward. A drill-down view shows the full task list for one Work Area in the "
        "selected range, and a “jump to current location” action follows a rolled-over task's chain forward "
        "(cycle-guarded) to land the manager directly on whichever live shift the task actually lives on today."
    )

    h2("8.3 My Metrics (Self-Service)")
    para(
        "Any authenticated user — no special analytics permission required — can view their own personal "
        "metrics: attendance breakdown by status, task breakdown by status, overtime shifts worked, and full "
        "group-membership history, over a selectable range (month/30/90/all days). The same underlying "
        "profile-building logic also powers the admin-facing profile view for any user, keeping the two "
        "presentations consistent."
    )

    page_break()

    # ================= 9. AI ASSISTANT =================
    h1_("9. Functional Requirements — AI Assistant")

    h2("9.1 Overview")
    para(
        "The AI Assistant is a chat-based interface (gated by AiAssistant.Use, rate-limited to 15 requests per "
        "minute per user/IP) backed by an OpenAI-compatible chat completion model using the standard tool/"
        "function-calling pattern, with optional voice input/output and a WebRTC “talking avatar” video session "
        "via Azure Cognitive Services Speech. Each conversation turn allows up to 6 rounds of tool calls before "
        "the assistant gives up with a localized “unable to complete” message."
    )

    h2("9.2 Tool Capabilities")
    para(
        "27 tool functions are registered, each explicitly paired with a required permission (or none, for "
        "tools that are inherently self-scoped, such as “my own shift today”). Broadly:"
    )
    bullet("Self-service reads: today's/upcoming shifts, my tasks, my change requests, dashboard KPIs, my shift reports and their detail/attachment links (metadata/download-link only — file contents are never returned into the chat)")
    bullet("Permission-gated reads: pending change requests, engineer lookup, shift detail/roster/incidents, task history")
    bullet("Employee-initiated writes (ChangeRequest.Submit): submit swap/absence/temp-transfer/overtime/replacement requests, cancel a request, accept or decline a transfer")
    bullet("Manager-only writes: add/update tasks, approve/reject change requests, log/update incidents, update attendance, activate/close a shift")

    h2("9.3 Guardrails")
    para(
        "Every tool call is dispatched generically: an unregistered tool name is refused outright, and the "
        "caller's permission is checked before the underlying domain service is ever invoked — an unauthorized "
        "request returns a structured “forbidden” result to the model rather than executing anything, while "
        "keeping the conversation alive so the assistant can explain the refusal in natural language. Business-"
        "rule failures from the underlying service (invalid ID, wrong shift status, attendance already recorded, "
        "etc.) are likewise caught and surfaced as structured errors rather than crashing the turn."
    )
    add_diagram("ai_assistant_flow.png", "Figure 9.1 — AI Assistant tool-dispatch flow")
    para(
        "The system prompt includes an explicit prompt-injection guardrail instructing the model to treat any "
        "free text returned from tool results (reasons, summaries, incident descriptions, review notes written "
        "by other users) strictly as data to report back, never as instructions to follow. Role-specific "
        "instructions are appended (a manager is told to confirm details with the user before approving/"
        "rejecting a request, adding a task, or closing a shift; a non-manager is told explicitly which actions "
        "they cannot perform). Arabic responses are forced into Modern Standard Arabic. Every write action is "
        "double-audited — once by the underlying domain service under its normal action name, and again by the "
        "orchestrator under an “AI:{functionName}” audit entry — so AI-originated changes remain distinguishable "
        "from the same action taken through the ordinary web UI."
    )

    page_break()
