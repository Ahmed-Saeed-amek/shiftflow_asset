# -*- coding: utf-8 -*-
"""Part 5: Data Model, External Interfaces, Non-Functional Requirements, Appendices."""


def build_part5(ctx):
    doc = ctx["doc"]
    h1_ = ctx["h1"]; h2 = ctx["h2"]; h3 = ctx["h3"]
    para = ctx["para"]; bullet = ctx["bullet"]; numbered = ctx["numbered"]
    add_diagram = ctx["add_diagram"]; simple_table = ctx["simple_table"]
    page_break = ctx["page_break"]

    # ================= 16. DATA MODEL =================
    h1_("16. Data Model")

    h2("16.1 Asset, Vendor, Contract & Work Order Domain")
    add_diagram("er_diagram_assets.png", "Figure 16.1 — Asset Management / Work Order entity-relationship diagram", width_in=6.5)

    h2("16.2 Scheduling & Operations Domain")
    add_diagram("er_diagram_scheduling.png", "Figure 16.2 — Shift Scheduling / Operations entity-relationship diagram", width_in=6.5)

    h2("16.3 Entity Inventory")
    para(
        "The application defines 34 explicit entity tables (beyond the 7 standard ASP.NET Core Identity "
        "tables), grouped as follows:"
    )
    simple_table(
        ["Group", "Entities"],
        [
            ["Core / Audit", "Location, AuditLog"],
            ["Scheduling Infrastructure", "RotationTemplate, RotationTemplateDay, ShiftGroup, UserGroupMembership, ShiftSchedule, DailyGroupShift, ShiftAssignment, OvertimeAssignment, ShiftOverride"],
            ["Change Requests", "ShiftChangeRequest, EmployeeShiftException"],
            ["Shift Operations", "ShiftTask, ShiftTaskCompletion, ShiftReport, ShiftReportAttachment, ShiftIncident"],
            ["Work Areas", "WorkArea"],
            ["RBAC", "Permission, RolePermission, UserPermission"],
            ["Asset Management", "AssetCategory, Governorate, Area, Zone, Asset, Vendor, WorkOrder, WorkOrderStageEvent, Contract, ContractAsset, UserAssetScope, AssetActionType, AssetActionCause, WorkOrderPart, WorkOrderBlockReason, WorkOrderAttachment"],
            ["Identity (inherited)", "ApplicationUser, IdentityRole, and the standard Identity join/claims/login/token tables"],
        ],
        widths=[1.7, 4.6],
    )

    page_break()

    # ================= 17. EXTERNAL INTERFACE REQUIREMENTS =================
    h1_("17. External Interface Requirements")

    h2("17.1 User Interfaces")
    bullet("Server-rendered Razor Views styled with Bootstrap 5.3 (standard and RTL builds) and Bootstrap Icons")
    bullet("A collapsible sidebar with two structurally different navigation templates — a manager template (Overview / Operations / Administration / AI Assistant) and a personal template (My Home / My Tasks / Requests / Scheduling / etc.) — selected per user based on whether they hold the ShiftAnalytics.View permission, plus a fully separate vendor-only sidebar for Vendor-role logins")
    bullet("Reusable interactive components: a debounced AJAX employee search/typeahead picker, a chip-based multi-asset picker (with category-based bulk add), cascading category/subcategory and location dropdowns, and status badges with consistent color coding")
    bullet("Exported document interfaces: styled Excel workbooks (EPPlus) and branded PDF documents (iText7) for schedules, task lists, and work order/asset lists")

    h2("17.2 Internal JSON API Endpoints")
    para(
        "A number of lightweight, authenticated JSON endpoints back the interactive picker widgets. These are "
        "internal to the application (consumed only by its own JavaScript, not a published public API), but are "
        "listed here for completeness:"
    )
    simple_table(
        ["Endpoint", "Purpose"],
        [
            ["GET /api/users/search", "Typeahead employee search (by name/email/employee number), with optional role/work-area/shift-date filters; unconditionally excludes Vendor-role accounts, since this endpoint backs employee-facing pickers only."],
            ["GET /Assets/Search", "Typeahead asset search by tag or name, for the contract asset picker."],
            ["GET /Assets/ByCategory", "Returns every asset in a category (and its subcategories, for a top-level pick) — powers the category bulk-add control."],
            ["GET /AssetCategories/ByParent", "Returns the subcategories of a given top-level category — powers cascading category/subcategory dropdowns."],
            ["GET /AssetActionTypes/ByCategory", "Returns the action types available for a category, including inherited parent-category action types for a subcategory."],
            ["GET /WorkOrders/DownloadAttachment", "Authenticated, ownership-checked download of a work order fix-report attachment (staff with WorkOrder.View, or the owning vendor, only)."],
        ],
        widths=[2.3, 4.0],
    )

    h2("17.3 External Service Interfaces")
    simple_table(
        ["Interface", "Purpose", "Configuration"],
        [
            ["Microsoft Entra ID (OpenID Connect)", "Federated “Sign in with Microsoft” login", "AzureAd: Instance, TenantId, ClientId, ClientSecret, CallbackPath — feature fully disabled unless ClientId and TenantId are both set"],
            ["Microsoft Graph (app-only)", "Search and import users from the organization's Entra directory", "Uses the same AzureAd app registration"],
            ["SMTP", "Transactional email (credential delivery)", "Smtp: Host, Port, EnableSsl, User, Password, From"],
            ["Twilio WhatsApp", "Transactional WhatsApp messages (credential delivery)", "Twilio: AccountSid, AuthToken, WhatsAppFrom"],
            ["OpenAI / Azure OpenAI", "AI Assistant chat completions", "OpenAI: ApiKey, Model — or AzureOpenAI: Endpoint, ApiKey, DeploymentName as fallback"],
            ["Azure Cognitive Services Speech", "AI Assistant voice input/output and talking-avatar video session", "AzureSpeech: Key, Region"],
        ],
        widths=[1.9, 2.5, 1.9],
    )

    page_break()

    # ================= 18. NON-FUNCTIONAL REQUIREMENTS =================
    h1_("18. Non-Functional Requirements")

    h2("18.1 Security")
    bullet("All authentication is handled by ASP.NET Core Identity with lockout-on-failure (5 attempts / 5-minute lockout) and an 8-hour sliding, HttpOnly authentication cookie")
    bullet("Every sensitive controller action is gated by an explicit permission policy, evaluated server-side on every request (never trusting client-side UI hiding alone)")
    bullet("File uploads are validated by extension allowlist and magic-byte signature check, and stored outside the web root so they cannot be reached via the static-file pipeline — only through an authenticated, ownership-checked download action")
    bullet("A hand-written Content-Security-Policy, X-Content-Type-Options, X-Frame-Options: DENY, Referrer-Policy, and Permissions-Policy are applied to every response")
    bullet("The Vendor Portal re-verifies work-order ownership on every request server-side, independent of role membership, preventing cross-vendor data access even via a guessed URL")
    bullet("The Kestrel Server response header is suppressed and IIS's automatic authentication is disabled to reduce information disclosure and prevent host-level auth context leakage")
    bullet("An explicit prompt-injection guardrail instructs the AI Assistant to treat all tool-returned free text as data, never as instructions")

    h2("18.2 Performance")
    bullet("Effective user permissions are cached in memory for 5 minutes (sliding), explicitly invalidated on any permission-affecting write, to avoid recomputing role/override joins on every authorized request")
    bullet("Dashboard KPIs are cached for 2 minutes, keyed by role")
    bullet("List/report queries resolve roles and work areas in bulk (single query per page load) rather than per-row, avoiding N+1 query patterns")
    bullet("Detail tables in self-service metrics views are explicitly capped (e.g. 500 rows) to bound response size and rendering time")

    h2("18.3 Usability")
    bullet("A single reusable typeahead picker component is used everywhere a user needs to be selected, replacing plain dropdowns with search-as-you-type for scalability beyond a handful of options")
    bullet("Consistent status-badge color coding is applied across every module (shift, task, incident, change-request, work-order, contract statuses)")
    bullet("The sidebar navigation structure adapts to the signed-in user's role and permissions, so a user only ever sees links they can actually use")

    h2("18.4 Reliability and Auditability")
    bullet("Every work order stage transition is recorded as an immutable, timestamped, attributed audit event — never overwritten or deleted")
    bullet("Every shift task status change is recorded to a completion-history table, including automatic handover transitions")
    bullet("A general-purpose audit log records create/update actions across users, roles, contracts, work orders, and other sensitive entities, including a distinct tag for actions taken via the AI Assistant versus the ordinary web UI")
    bullet("Data-consistency self-repair runs automatically at application startup for a known class of schedule/work-area mismatch")

    h2("18.5 Maintainability")
    bullet("A single, centralized permission catalog and evaluation service is the sole authority for access decisions, avoiding scattered, inconsistent authorization checks")
    bullet("The permission catalog is self-curating at boot — legacy/renamed permissions are automatically retired from role grants, user overrides, and the catalog itself")
    bullet("Reusable UI partials (employee picker, asset multi-picker, status badge) are shared across every module that needs the same interaction pattern, rather than being reimplemented per screen")

    h2("18.6 Localization and Accessibility")
    bullet("Every screen supports both English and Arabic with genuine RTL layout mirroring, not just a text swap")
    bullet("Untranslated strings degrade gracefully to their English key rather than breaking the page")
    bullet("Data-annotation validation messages are localized through the same translation mechanism as the rest of the UI")

    page_break()

    # ================= APPENDICES =================
    h1_("Appendix A — Permission Catalog")
    para("The complete set of 45 permissions, exactly as seeded, grouped by category.")

    catalog = [
        ("Users", [
            ("User.View", "See the list of user accounts, their roles, and profile details"),
            ("User.Manage", "Create new users, edit their details, assign roles, and deactivate or delete accounts"),
        ]),
        ("Rotation Templates", [
            ("Schedule.Template.View", "See the shift rotation patterns used to auto-generate schedules"),
            ("Schedule.Template.Manage", "Create and edit the rotation patterns used to auto-generate schedules"),
        ]),
        ("Scheduling", [
            ("Schedule.View", "See published and draft shift schedules"),
            ("Schedule.Create", "Start a new shift schedule for a work area"),
            ("Schedule.Generate", "Auto-fill a draft schedule's shifts from its rotation template"),
            ("Schedule.Publish", "Make a draft schedule live so employees can see their shifts"),
            ("Schedule.Archive", "Move a published schedule to the archive once it's no longer active"),
            ("Schedule.Override.Apply", "Manually move an employee onto a different shift, overriding the normal rotation"),
            ("Schedule.Delete", "Permanently remove a shift schedule"),
            ("Group.Member.Manage", "Add or remove employees from shift groups"),
        ]),
        ("Change Requests", [
            ("ChangeRequest.Submit", "Request a shift swap, absence, or other change to your own schedule"),
            ("ChangeRequest.Review", "Approve or reject change requests submitted by employees"),
            ("ChangeRequest.View.All", "See every change request across the organization, not just your own work area"),
        ]),
        ("Shift Operations", [
            ("ShiftOps.View", "Open the live shift view for shifts you're scheduled on or your work area"),
            ("ShiftOps.Activate", "Start a shift, marking it as in progress"),
            ("ShiftOps.Attendance.Update", "Mark employees present, late, absent, or excused on a shift"),
            ("ShiftOps.Task.Add", "Create tasks for a shift's handover checklist"),
            ("ShiftOps.Task.Delete", "Remove a task from a shift's handover checklist"),
            ("ShiftOps.Task.UpdateStatus", "Mark shift tasks as in progress, done, or blocked"),
            ("ShiftOps.Close", "End a shift and hand over any open tasks"),
            ("ShiftOps.Incident.Add", "Record a safety or operational incident during a shift"),
            ("ShiftOps.Report.View", "Read shift handover reports"),
            ("ShiftOps.ManageAll", "See and manage shifts across every work area, not just your own group"),
            ("ShiftOps.Report.ManageAll", "See and manage every shift report, not just ones you're assigned to"),
        ]),
        ("Reports & Analytics", [
            ("ShiftAnalytics.View", "See the task-completion and performance analytics dashboard"),
        ]),
        ("Locations", [
            ("Location.Manage", "Add, edit, or remove physical site locations"),
        ]),
        ("AI Assistant", [
            ("AiAssistant.Use", "Access the AI assistant for shift-related questions and actions"),
        ]),
        ("Administration", [
            ("AuditLog.View", "See the history of who changed what across the system"),
            ("Rbac.Manage", "Create roles, assign permissions, and control what each user or role can access"),
            ("System.IsAdmin", "Grants every permission in the system, overriding all other settings — use with caution"),
        ]),
        ("Asset Management", [
            ("Asset.View", "See the asset register — tags, categories, locations, and status"),
            ("Asset.Manage", "Add, edit, or retire assets in the register"),
            ("AssetCategory.Manage", "Create and edit asset categories"),
            ("Vendor.View", "See the list of maintenance vendors"),
            ("Vendor.Manage", "Add, edit, or suspend maintenance vendors"),
            ("WorkOrder.View", "See maintenance work orders and their status"),
            ("WorkOrder.Manage", "Create work orders and advance them through their stages"),
            ("WorkOrder.Assign", "Assign a vendor to a work order"),
            ("WorkOrder.Export", "Export asset and work order lists to Excel or PDF"),
            ("Contract.View", "See vendor contracts and which assets they cover"),
            ("Contract.Manage", "Create and edit vendor contracts and link them to assets"),
            ("Asset.ReportAction", "Report a failure or other action on an asset, creating a draft work order for admin review"),
            ("Asset.ScopeManage", "Restrict which zone, area, or category of assets a specific employee can see"),
        ]),
    ]

    for category, perms in catalog:
        h3(category)
        simple_table(
            ["Permission", "Description"],
            [[name, desc] for name, desc in perms],
            widths=[1.9, 4.4],
        )

    page_break()

    h1_("Appendix B — Role → Permission Matrix")
    para(
        "As seeded exactly. A blank cell means the permission is not granted at the role level. The Vendor "
        "role holds no rows in this matrix — it is governed entirely by the separate Vendor Portal ownership "
        "check (Section 12.3), not the permission catalog. System.IsAdmin is never granted to any role by "
        "default — it is only ever assigned manually, via the RBAC admin UI or a per-user override."
    )

    roles = ["Admin", "ShiftMgr", "Supervisor", "Sec. Head", "Sr. Eng.", "Engineer", "Op. Eng.", "Technician", "HR"]
    matrix_perms = [
        "User.Manage", "User.View",
        "Schedule.Template.View", "Schedule.Template.Manage",
        "Schedule.View", "Schedule.Create", "Schedule.Generate", "Schedule.Publish",
        "Schedule.Archive", "Schedule.Override.Apply", "Schedule.Delete", "Group.Member.Manage",
        "ChangeRequest.Submit", "ChangeRequest.Review", "ChangeRequest.View.All",
        "ShiftOps.View", "ShiftOps.Activate", "ShiftOps.Attendance.Update",
        "ShiftOps.Task.Add", "ShiftOps.Task.Delete", "ShiftOps.Task.UpdateStatus", "ShiftOps.Close",
        "ShiftOps.Incident.Add", "ShiftOps.Report.View", "ShiftOps.ManageAll", "ShiftOps.Report.ManageAll",
        "ShiftAnalytics.View", "Location.Manage", "AiAssistant.Use", "AuditLog.View", "Rbac.Manage",
        "Asset.View", "Asset.Manage", "AssetCategory.Manage", "Asset.ScopeManage",
        "Vendor.View", "Vendor.Manage",
        "WorkOrder.View", "WorkOrder.Manage", "WorkOrder.Assign", "WorkOrder.Export",
        "Contract.View", "Contract.Manage",
    ]
    grants = {
        "Admin": {"User.Manage", "User.View", "Schedule.Template.View", "Schedule.Template.Manage", "Schedule.View",
                  "Schedule.Create", "Schedule.Generate", "Schedule.Publish", "Schedule.Archive", "Schedule.Override.Apply",
                  "Schedule.Delete", "Group.Member.Manage", "ChangeRequest.Submit", "ChangeRequest.Review", "ChangeRequest.View.All",
                  "ShiftOps.View", "ShiftOps.Activate", "ShiftOps.Attendance.Update", "ShiftOps.Task.Add", "ShiftOps.Task.Delete",
                  "ShiftOps.Task.UpdateStatus", "ShiftOps.Close", "ShiftOps.Incident.Add", "ShiftOps.Report.View", "ShiftOps.ManageAll",
                  "ShiftOps.Report.ManageAll", "AiAssistant.Use", "AuditLog.View", "Location.Manage", "Rbac.Manage", "ShiftAnalytics.View",
                  "Asset.View", "Asset.Manage", "AssetCategory.Manage", "Asset.ScopeManage", "Vendor.View", "Vendor.Manage",
                  "WorkOrder.View", "WorkOrder.Manage", "WorkOrder.Assign", "WorkOrder.Export", "Contract.View", "Contract.Manage"},
        "ShiftMgr": {"User.View", "Schedule.Template.View", "Schedule.View", "Schedule.Create", "Schedule.Generate",
                     "Schedule.Publish", "Schedule.Override.Apply", "Group.Member.Manage", "ChangeRequest.Review",
                     "ChangeRequest.View.All", "ShiftOps.View", "ShiftOps.Activate", "ShiftOps.Attendance.Update",
                     "ShiftOps.Task.Add", "ShiftOps.Task.Delete", "ShiftOps.Task.UpdateStatus", "ShiftOps.Close",
                     "ShiftOps.Incident.Add", "ShiftOps.Report.View", "ShiftOps.ManageAll", "ShiftOps.Report.ManageAll",
                     "AiAssistant.Use", "ShiftAnalytics.View", "Asset.View", "Asset.Manage", "Asset.ScopeManage",
                     "Vendor.View", "Vendor.Manage", "WorkOrder.View", "WorkOrder.Manage", "WorkOrder.Assign",
                     "WorkOrder.Export", "Contract.View", "Contract.Manage"},
        "Supervisor": {"Schedule.View", "ChangeRequest.Review", "ChangeRequest.View.All", "ShiftOps.View",
                       "ShiftOps.Attendance.Update", "ShiftOps.Task.Add", "ShiftOps.Task.Delete",
                       "ShiftOps.Task.UpdateStatus", "ShiftOps.Incident.Add", "ShiftOps.Report.View",
                       "Asset.View", "Asset.Manage", "Asset.ScopeManage", "Vendor.View",
                       "WorkOrder.View", "WorkOrder.Manage", "WorkOrder.Assign", "WorkOrder.Export", "Contract.View"},
        "Sec. Head": {"Schedule.View", "ChangeRequest.Review", "ChangeRequest.View.All", "ShiftOps.View",
                      "ShiftOps.Report.View", "Asset.View", "Vendor.View", "WorkOrder.View"},
        "Sr. Eng.": {"ChangeRequest.Submit", "ShiftOps.View", "ShiftOps.Task.UpdateStatus", "ShiftOps.Incident.Add",
                     "Asset.View", "WorkOrder.View", "WorkOrder.Manage"},
        "Engineer": {"ChangeRequest.Submit", "ShiftOps.View", "ShiftOps.Task.UpdateStatus", "Asset.View",
                     "WorkOrder.View", "WorkOrder.Manage"},
        "Op. Eng.": {"ChangeRequest.Submit", "ShiftOps.View", "ShiftOps.Task.UpdateStatus", "Asset.View",
                     "WorkOrder.View", "WorkOrder.Manage"},
        "Technician": {"ChangeRequest.Submit", "ShiftOps.View", "ShiftOps.Task.UpdateStatus", "Asset.View",
                       "WorkOrder.View", "WorkOrder.Manage"},
        "HR": {"Schedule.View", "Asset.View"},
    }

    headers = ["Permission"] + roles
    rows = []
    for perm in matrix_perms:
        row = [perm]
        for role in roles:
            row.append("Y" if perm in grants[role] else "")
        rows.append(row)
    simple_table(headers, rows, widths=[1.7] + [0.55] * 9)

    page_break()

    h1_("Appendix C — Status / Enum Reference")
    simple_table(
        ["Entity.Field", "Values"],
        [
            ["ShiftSchedule.Status", "Draft, Published, Archived"],
            ["DailyGroupShift.Status", "Draft, Active, Closed"],
            ["DailyGroupShift.ShiftType", "Morning, Evening, Night, Off"],
            ["ShiftAssignment.AttendanceStatus", "Scheduled, Present, Late, Absent, Excused, OnLeave, Replaced"],
            ["OvertimeAssignment.AttendanceStatus", "Scheduled, Present, Late, Absent, Excused"],
            ["ShiftTask.Status", "Pending, InProgress, Done, HandedOver, Blocked"],
            ["ShiftIncident.Severity", "Low, Medium, High, Critical"],
            ["ShiftIncident.Status", "Open, UnderInvestigation, Resolved, Closed"],
            ["ShiftChangeRequest.RequestType", "Absence, Swap, Replacement, Overtime, TempGroupChange, PermanentGroupChange"],
            ["ShiftChangeRequest.Status", "Pending, Approved, Rejected, Cancelled"],
            ["Asset.Status", "Working, Defective, Maintenance, Retired"],
            ["Vendor.Status", "Active, Suspended"],
            ["Contract.ContractType", "Purchase, Warranty, Service, Insurance"],
            ["WorkOrder.Priority", "Low, Medium, High, Critical"],
            ["WorkOrder.Stage", "Draft, Rejected, New, Sent to Vendor, Blocked, Fixed - Pending Confirmation, Closed"],
        ],
        widths=[2.1, 4.2],
    )

    page_break()

    h1_("Appendix D — Glossary")
    simple_table(
        ["Term", "Meaning"],
        [
            ["Draft (schedule)", "A schedule that exists but is not yet visible to ordinary employees."],
            ["Published (schedule)", "A schedule visible to all affected employees; the live, current plan."],
            ["Archived (schedule)", "A retired schedule, no longer editable or regenerable."],
            ["Handed Over (task)", "A task rolled forward from a closing shift onto the next shift because it wasn't finished."],
            ["Draft (work order)", "An employee-reported failure, awaiting admin accept/reject."],
            ["Sent to Vendor", "A work order actively assigned to and awaiting response from a maintenance vendor."],
            ["Blocked (work order)", "A work order the vendor cannot currently proceed with, pending admin resolution."],
            ["Service contract", "The only contract type consulted for automatic work-order vendor resolution."],
            ["Effective permission set", "The final list of permissions a user actually holds, after applying role grants and Deny/Allow overrides."],
            ["System.IsAdmin", "A super-permission that implicitly grants the entire permission catalog to whoever holds it."],
        ],
        widths=[1.9, 4.4],
    )
