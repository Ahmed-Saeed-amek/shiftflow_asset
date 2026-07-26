# -*- coding: utf-8 -*-
"""Part 2: System Architecture, Identity & RBAC, Shift Scheduling, Shift Operations."""


def build_part2(ctx):
    doc = ctx["doc"]
    h1_ = ctx["h1"]; h2 = ctx["h2"]; h3 = ctx["h3"]; h4 = ctx["h4"]
    para = ctx["para"]; bullet = ctx["bullet"]; numbered = ctx["numbered"]
    add_diagram = ctx["add_diagram"]; simple_table = ctx["simple_table"]
    page_break = ctx["page_break"]

    # ================= 3. SYSTEM ARCHITECTURE =================
    h1_("3. System Architecture")

    h2("3.1 Technology Stack")
    simple_table(
        ["Layer", "Technology"],
        [
            ["Runtime / Framework", "ASP.NET Core 8 MVC (.NET 8), C#, nullable reference types enabled"],
            ["Data Access", "Entity Framework Core 8, code-first migrations, SQL Server / LocalDB"],
            ["Identity", "ASP.NET Core Identity (Microsoft.AspNetCore.Identity.EntityFrameworkCore), custom RBAC layer on top"],
            ["External Login", "Microsoft.AspNetCore.Authentication.OpenIdConnect — Microsoft Entra ID, conditionally registered"],
            ["Views", "Razor Views + Bootstrap 5.3 (LTR and RTL builds), Bootstrap Icons, vanilla JavaScript for AJAX widgets"],
            ["Excel Export", "EPPlus 7.5"],
            ["PDF Export", "iText7 8.0 (+ BouncyCastle adapter)"],
            ["AI / LLM", "Azure.AI.OpenAI SDK (direct OpenAI key preferred, Azure OpenAI fallback), Azure Cognitive Services Speech (voice + talking-avatar)"],
            ["Notifications", "SMTP (email), Twilio (WhatsApp)"],
            ["Directory Integration", "Microsoft Graph (app-only) for Entra directory search/import"],
            ["Logging", "Serilog — console + rolling daily file sink (logs/sf-.log)"],
            ["Caching", "Microsoft.Extensions.Caching.Memory — 5-minute sliding cache for effective permissions, 2-minute cache for dashboard KPIs"],
            ["Mobile Shell", ".NET MAUI (net10.0), targeting Android / iOS / Mac Catalyst / Windows"],
        ],
        widths=[1.8, 4.5],
    )

    h2("3.2 Middleware Pipeline")
    para("Requests flow through the following ordered pipeline (Program.cs):")
    numbered("Global exception handler + HSTS (outside Development)")
    numbered("Response compression")
    numbered("HTTPS redirection")
    numbered("Custom security-headers middleware — sets X-Content-Type-Options, X-Frame-Options: DENY, Referrer-Policy, Permissions-Policy, and a hand-written Content-Security-Policy (deliberately permissive on connect-src/media-src/worker-src to support the AI Assistant's voice/avatar feature and OpenStreetMap/Leaflet map tiles for asset locations)")
    numbered("Static files")
    numbered("Routing")
    numbered("Authentication")
    numbered("Authorization")
    numbered("Rate limiting (a named “ai” fixed-window policy: 15 requests/minute per user or IP, scoped to the AI Assistant endpoints)")
    numbered("Default MVC route: {controller=Dashboard}/{action=Index}/{id?}")
    para(
        "At startup, the application runs DbSeeder.SeedAsync (which itself applies pending EF Core "
        "migrations) inside a scoped service resolution, then performs a data-consistency self-repair pass: "
        "any Draft schedule whose shifts reference groups from a different Work Area than the schedule itself "
        "is automatically rebuilt via the same rotation-fill and assignment-generation pipeline used at schedule "
        "creation time."
    )
    para(
        "Two hardening details worth noting: the Kestrel “Server” response header is explicitly suppressed "
        "(reduces information disclosure about the hosting stack), and IIS's own automatic authentication is "
        "disabled so that a host-level authentication context can never leak into the application's ClaimsPrincipal."
    )

    h2("3.3 Deployment Topology")
    para(
        "ShiftFlow.Web is deployed as a single server-rendered application instance (or a scaled-out set behind "
        "a load balancer, keeping in mind the in-memory permission/dashboard caches are per-instance and not "
        "distributed) against one SQL Server database. ShiftFlow.Mobile is a genuinely separate build artifact "
        "per platform (Android/iOS/Mac Catalyst/Windows) but contains no independent backend logic — it is "
        "purely a native WebView host pointed at a configured production URL, so a mobile app-store release "
        "requires no separate backend deployment beyond keeping ShiftFlow.Web's production URL stable."
    )

    page_break()

    # ================= 4. IDENTITY & RBAC =================
    h1_("4. Authentication, Identity & Access Control")

    h2("4.1 Authentication")
    h3("4.1.1 Local Login")
    para(
        "Users sign in with an email and password via ASP.NET Core Identity's SignInManager, with lockout-on-"
        "failure protection (5 failed attempts triggers a 5-minute lockout). Deactivated accounts (IsActive = "
        "false) are rejected and signed back out even with correct credentials. A first-login temporary password "
        "carries a must_change_password claim, forcing a redirect to the Change Password screen before any other "
        "page is reachable."
    )
    h3("4.1.2 Microsoft Entra ID (Sign in with Microsoft)")
    para(
        "When AzureAd:ClientId and AzureAd:TenantId are both configured, an additional OpenID Connect scheme "
        "(“EntraID”) is registered, letting a user authenticate against the organization's Entra ID tenant. "
        "On callback, three cases are handled: an already-linked external login signs in directly; an email "
        "matching an existing local account auto-links the Entra login to it; a brand-new email auto-provisions "
        "a new account with no local password, defaulting to the lowest-privilege Technician role pending an "
        "admin's re-assignment. If Entra is not configured, the feature is hidden entirely and never attempted."
    )
    h3("4.1.3 Landing Page Routing")
    para(
        "After a successful sign-in, the user is routed based on role and permissions, so the landing page "
        "always matches the sidebar navigation they are about to see:"
    )
    add_diagram("login_routing.png", "Figure 4.1 — Login and landing-page routing")

    h2("4.2 Role-Based Access Control (RBAC) Model")
    para(
        "Every state-changing or sensitive-viewing controller action is gated by an ASP.NET Core authorization "
        "Policy, one policy per permission name in the catalog. A custom PermissionAuthorizationHandler resolves "
        "whether the current user holds that permission via PermissionService, which computes an “effective "
        "permission set” per user with strict precedence: an explicit per-user Deny override always wins over "
        "an Allow override or a role-level grant; the effective set is cached in memory for 5 minutes (sliding), "
        "invalidated immediately on any override or role-grant change. A user holding the special System.IsAdmin "
        "permission is granted the entire catalog implicitly, including permissions added to the catalog after "
        "they received System.IsAdmin."
    )
    add_diagram("rbac_evaluation.png", "Figure 4.2 — Permission evaluation flow")

    h2("4.3 Identity Roles")
    para("Ten Identity roles are seeded at startup:")
    bullet("Admin, ShiftManager, Engineer, HR, Supervisor, Section Head, Senior Engineer, Operation Engineer, Technician, Vendor")
    para(
        "The Vendor role is structurally distinct from the other nine: it holds no rows in the permission-catalog "
        "matrix at all, since Vendor accounts never touch the internal ShiftFlow application — they are confined "
        "entirely to the separate Vendor Portal area (Section 12), where authorization is enforced by role "
        "membership plus an explicit per-request ownership check, not by the permission catalog."
    )

    h2("4.4 Permission Catalog and Role Matrix")
    para(
        "The complete permission catalog (45 permissions across 10 categories) and the exact role-to-permission "
        "matrix as seeded are reproduced in full in Appendix A and Appendix B, since they are authoritative "
        "reference tables rather than narrative content."
    )
    para(
        "The catalog is also actively curated on every application boot: three legacy permission names "
        "(Shift.Manage, the un-namespaced IsAdmin, and Report.View/Report.Export) are automatically retired — "
        "their rows are removed from the Permissions, RolePermissions, and UserPermissions tables — reflecting "
        "features that were removed or renamed since those permissions were first introduced."
    )

    h2("4.5 RBAC Administration")
    para("A dedicated administration area (gated by Rbac.Manage) provides:")
    bullet("Role CRUD — creating and deleting Identity roles (deletion blocked while any user still holds the role)")
    bullet("Bulk role assignment — adding/removing multiple roles across multiple selected users in one operation")
    bullet("A role-permission grid — checkbox matrix per role, saved as a diff against current grants")
    bullet("Per-user permission overrides — explicit Allow/Deny entries layered on top of a user's role-derived permissions, shown alongside the role-inherited baseline for clarity")

    h2("4.6 User Account Management")
    bullet("Directory: filterable by role, work area (derived dynamically from active group membership, not stored), and free-text search")
    bullet("Create: generates a cryptographically random temporary password (guaranteed upper/lower/digit/special character mix), assigns the chosen role, flags the account for a forced password change, and delivers credentials via email and, if a phone number is present, WhatsApp")
    bullet("Delete: blocked for self-deletion; if the database rejects the delete due to foreign-key-protected activity history (shift assignments, audit records), the user is shown a friendly message recommending deactivation instead of deletion")
    bullet("Deactivate / Reactivate: soft-disables login without deleting history; deactivated users are rejected at every login attempt")
    bullet("Entra Directory Search / Import: looks up users in the organization's Entra ID directory via Microsoft Graph and imports them as Entra-only accounts (no local password) that auto-link on their first Microsoft sign-in")
    bullet("Self-service profile: Profile/MyMetrics builds attendance KPIs, task KPIs, overtime-shift counts, and group-membership history for any user (admin-facing) or the caller only (self-service, no special permission required)")

    page_break()

    # ================= 5. SHIFT SCHEDULING =================
    h1_("5. Functional Requirements — Shift Scheduling")

    h2("5.1 Work Areas and Shift Groups")
    para(
        "A Work Area represents a physical or organizational site. Every Work Area is automatically provisioned "
        "with exactly five fixed Shift Groups — A, B, C, D, and F (there is deliberately no “E,” to avoid visual "
        "confusion with the Evening shift code) — both at initial seeding and automatically whenever a new Work "
        "Area is created. Deactivating a Work Area does not hard-delete its groups (they are referenced by "
        "historical shift and attendance data); instead its groups are deactivated and any open group memberships "
        "are force-closed."
    )

    h2("5.2 Rotation Templates")
    para(
        "A Rotation Template defines a reusable 5-day repeating pattern: for each of the 5 days, which group "
        "works Morning, which works Evening, which works Night — any group not named for a given day is "
        "implicitly Off that day. The seeded default template (“Standard 5-Day A-B-C-D-F”) rotates each group "
        "forward by one shift per day across the cycle. A schedule may instead be built with a one-off inline "
        "rotation defined at creation time (persisted as its own auto-named template), or — if no template is "
        "attached at all — fall back to a hard-coded default rotation."
    )
    simple_table(
        ["Functional Requirement ID", "Requirement"],
        [
            ["FR-SCHED-01", "The system shall allow a user holding Schedule.Template.Manage to create, edit, and mark rotation templates as default or inactive."],
            ["FR-SCHED-02", "Every Work Area shall always have exactly five Shift Groups (A, B, C, D, F), auto-provisioned on creation."],
            ["FR-SCHED-03", "A Rotation Template Day shall define Morning/Evening/Night group assignments only; Off status is derived, not stored."],
        ],
        widths=[1.7, 4.6],
    )

    h2("5.3 Schedule Lifecycle")
    para(
        "A Shift Schedule progresses through three statuses: Draft, Published, and Archived. Creating a schedule "
        "is a three-step pipeline executed in one operation: the schedule shell is created (Draft), then every "
        "DailyGroupShift row is generated for its date range by applying the rotation template day-by-day (this "
        "wipes and regenerates the full group/day plan — the operational source of truth), then every individual "
        "employee's ShiftAssignment is generated by expanding the plan against currently active group memberships."
    )
    add_diagram("rotation_generation_sequence.png", "Figure 5.1 — Schedule creation and publication sequence")
    para(
        "Non-scheduler users can only ever see a schedule once it is Published — Draft schedules are visible "
        "only to holders of scheduling permissions. Archiving a schedule blocks any further assignment "
        "regeneration or manual overrides against it; deleting a schedule cascades to its generated shift and "
        "assignment data."
    )

    h2("5.4 Manual Planner and Overrides")
    para(
        "A 14-day rolling planner grid lets a scheduler (Schedule.Override.Apply) edit an individual group's "
        "shift type for a specific date directly, immediately patching both the DailyGroupShift plan row and any "
        "already-generated ShiftAssignment rows so the two stay consistent. A swap mode exchanges two groups' "
        "shift types on the same date in one action. Every single-cell override is recorded as an auditable "
        "ShiftOverride row (original value, new value, reason, who, when) and can be reverted, restoring the "
        "original value and removing the audit row. The whole plan for a schedule can also be wiped and "
        "regenerated from its rotation template in one action. All override operations are blocked once a "
        "schedule is Archived."
    )

    h2("5.5 Group Membership Management")
    para(
        "Assigning an employee to a Shift Group closes any existing open membership and opens a new one from a "
        "chosen effective date. Membership changes automatically propagate to already-generated assignments, but "
        "only for shifts still in Draft status — an Active or Closed shift's already-recorded attendance and "
        "roster data is never silently rewritten by a later membership change."
    )

    h2("5.6 Export")
    para(
        "Schedules can be exported as a styled monthly Excel workbook or PDF roster grid — groups as rows, days "
        "as columns, single-letter shift codes (M/E/N/O) — suitable for printed physical posting."
    )

    page_break()

    # ================= 6. SHIFT OPERATIONS =================
    h1_("6. Functional Requirements — Shift Operations")

    h2("6.1 Access Model")
    para(
        "A non-manager user (without ShiftOps.ManageAll) may act on a given shift only if the shift's group "
        "belongs to their own current Work Area, or if they are personally rostered on that specific shift "
        "(covering a temporarily transferred or overtime employee acting on a shift outside their home Work "
        "Area). This rule applies consistently across activation, attendance, task, and incident actions."
    )

    h2("6.2 Shift Lifecycle")
    add_diagram("shift_ops_lifecycle.png", "Figure 6.1 — DailyGroupShift lifecycle")
    para(
        "A DailyGroupShift starts Draft (as generated by the schedule pipeline), becomes Active when a scheduler "
        "or supervisor activates it (stamping ActivatedAt), and becomes Closed when the closing supervisor formally "
        "closes it (stamping ClosedAt). Attendance, task, and incident actions are only permitted while the shift "
        "is Active; task creation/deletion and overrides are blocked once Closed."
    )

    h2("6.3 Attendance Management")
    para(
        "Each roster member's attendance is tracked as one of: Scheduled, Present, Late, Absent, Excused, "
        "OnLeave, or Replaced (a parallel, slightly narrower set — Scheduled/Present/Late/Absent/Excused — "
        "applies to Overtime roster members). Marking a member Present or Late auto-stamps a clock-in time on "
        "first transition; marking Absent, OnLeave, or Replaced clears any recorded clock-out time."
    )

    h2("6.4 Task Management and Automatic Handover")
    para(
        "A Shift Task belongs to one DailyGroupShift, may be assigned to a specific employee or left unassigned "
        "(whole group), and may be flagged mandatory-for-handover. Its status progresses through Pending, "
        "InProgress, Done, Blocked, or HandedOver. Every status change is recorded to an auditable completion "
        "history, and the task's own assignee may update or delete it even without broader shift-management "
        "rights. When a shift is closed, every task not already Done or HandedOver is automatically rolled "
        "forward onto the group's next shift as a brand-new task (preserving title, description, and the "
        "mandatory flag, and recording which original task it was rolled over from), while the original task's "
        "status flips to HandedOver. If no future shift exists to hand the task over to, the shift closure is "
        "blocked entirely rather than silently dropping the task."
    )
    add_diagram("task_handover_flow.png", "Figure 6.2 — Shift closure and task handover")

    h2("6.5 Incident Management")
    para(
        "Incidents record severity (Low/Medium/High/Critical) and status (Open/UnderInvestigation/Resolved/"
        "Closed). If no closing report yet exists for the shift when the first incident is logged, one is "
        "automatically created so an incident is never orphaned from its parent report even before formal "
        "closure. Resolving or closing an incident stamps a resolution timestamp."
    )

    h2("6.6 Shift Closure and Reporting")
    para(
        "Closing a shift requires a non-empty summary, computes attendee/absent counts from the final roster "
        "state, creates the ShiftReport record, performs the task-handover pass described above, and accepts "
        "file attachments — validated (extension allowlist plus magic-byte signature check) and stored under "
        "App_Data/uploads/shiftreports/{reportId}/, outside wwwroot and therefore unreachable except through an "
        "authenticated, ownership-checked download action. Rejected files are reported back to the closing "
        "supervisor without failing the rest of the closure."
    )

    h2("6.7 History and Personal Views")
    bullet("History: a paginated, filterable list of Closed shifts, scoped to the caller's own Work Area unless they hold organization-wide reporting rights")
    bullet("My Schedule: a personal calendar of the caller's own upcoming assignments, restricted to Published schedules and their current Work Area")

    page_break()
