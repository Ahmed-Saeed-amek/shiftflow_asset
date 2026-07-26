# -*- coding: utf-8 -*-
"""Part 4: Asset Management, Work Orders, Vendor Portal, Contracts, Localization, Mobile."""


def build_part4(ctx):
    doc = ctx["doc"]
    h1_ = ctx["h1"]; h2 = ctx["h2"]; h3 = ctx["h3"]
    para = ctx["para"]; bullet = ctx["bullet"]; numbered = ctx["numbered"]
    add_diagram = ctx["add_diagram"]; simple_table = ctx["simple_table"]
    page_break = ctx["page_break"]

    # ================= 10. ASSET MANAGEMENT =================
    h1_("10. Functional Requirements — Asset Management")

    h2("10.1 Location Hierarchy")
    para(
        "Assets are located through a fixed three-level geographic hierarchy: Governorate → Area → Zone. "
        "Governorates and Areas are seeded, fixed reference data (modeled on Kuwait's six governorates and "
        "their constituent areas) and are not user-editable; Zones are the leaf level where assets actually "
        "attach, and are created freely by administrators (optionally with map coordinates for a Leaflet/"
        "OpenStreetMap location view)."
    )

    h2("10.2 Asset Categories and Subcategories")
    para(
        "Categories form exactly two levels: a top-level category may have subcategories, but a subcategory "
        "cannot itself have children. Categories drive two things: (1) which Action Types and Causes an "
        "employee can select when reporting a failure on an asset in that category — a subcategory's dropdown "
        "is the union of its own action types and its parent category's, so a subcategory automatically "
        "inherits everything its parent supports; and (2) category-based bulk asset selection when linking a "
        "contract (Section 13.3)."
    )

    h2("10.3 Asset Register")
    para(
        "Each Asset carries a tag, name, category, zone, optional model/serial/manufacturer, a status "
        "(Working / Defective / Maintenance / Retired), optional purchase date and warranty expiry, and full "
        "audit fields (created/updated by and when)."
    )
    simple_table(
        ["Functional Requirement ID", "Requirement"],
        [
            ["FR-AM-01", "The system shall enforce exactly two levels of asset category nesting."],
            ["FR-AM-02", "A subcategory's available action types shall be the union of its own and its parent category's action types."],
            ["FR-AM-03", "Every asset shall belong to exactly one Zone and one Category (or Subcategory)."],
        ],
        widths=[1.7, 4.6],
    )

    h2("10.4 Asset Reporting and Visibility Scoping")
    para(
        "Any user holding Asset.ReportAction can report a failure or requested action on an asset they can see, "
        "which creates a Draft work order awaiting admin review (Section 11). Visibility of the asset register "
        "itself can be further restricted per user via User Asset Scopes — an admin (Asset.ScopeManage) can "
        "confine a specific employee's visible assets to one Zone, one Area, or one Category, at most one scope "
        "per user. This is independent of, and layered on top of, the ordinary Asset.View permission."
    )

    page_break()

    # ================= 11. WORK ORDERS =================
    h1_("11. Functional Requirements — Work Order & Vendor-Driven Repair Workflow")

    h2("11.1 Overview")
    para(
        "A Work Order tracks an asset repair from initial report through vendor resolution to closure. Two "
        "creation paths exist: an employee reporting a failure (via Asset.ReportAction) always starts a work "
        "order in Draft, pending admin review; an admin/manager creating a work order directly (WorkOrder.Manage) "
        "starts it in New, skipping the draft-review step. Critically, the vendor is never freely picked from an "
        "open list — it is resolved only from the asset's currently active Service-type contract(s), enforcing "
        "that repair work always routes through a contractually valid maintenance provider."
    )

    h2("11.2 Work Order Lifecycle")
    add_diagram("workorder_lifecycle.png", "Figure 11.1 — Work order stage lifecycle")
    para(
        "Stages: Draft and Rejected (side-states for employee-reported work orders), New (admin-created, "
        "not yet sent), Sent to Vendor (the linear happy path's middle stage — reached either by an admin "
        "accepting a Draft or sending a New work order), Blocked (a detour off Sent to Vendor, not a forward "
        "step), Fixed - Pending Confirmation, and Closed. If an asset has more than one active Service contract "
        "(from different vendors), the admin is presented a choice; if it has none, accepting or sending the "
        "work order is blocked outright with a clear message to link a contract first."
    )

    h2("11.3 End-to-End Sequence")
    add_diagram("workorder_sequence.png", "Figure 11.2 — Employee → Admin → Vendor → Admin sequence")
    para(
        "A vendor's Fix report is structured (description, cost, completion date, a repeatable parts-used "
        "list) and supports file attachments, stored outside wwwroot and downloadable only by staff holding "
        "WorkOrder.View or the vendor that owns the work order. A vendor's Block report requires a categorized "
        "reason plus free-text detail; once the admin resolves the underlying issue, Resend returns the work "
        "order to the same vendor automatically, with the block fields cleared — full history remains visible "
        "via an immutable, append-only stage-event trail (one row per transition, recording who and when)."
    )

    h2("11.4 Block Reasons")
    para(
        "Block reasons are an admin-managed lookup (Waiting on Parts, Access Denied, Not Covered by Contract, "
        "Needs Site Visit are seeded as starters), each independently activatable/deactivatable — deactivating "
        "a reason hides it from future selection without altering historical work orders that already used it."
    )
    simple_table(
        ["Functional Requirement ID", "Requirement"],
        [
            ["FR-WO-01", "The system shall resolve a work order's vendor only from the asset's active Service-type contract(s), never from a free vendor list."],
            ["FR-WO-02", "The system shall block sending a work order to a vendor when the asset has zero active Service contracts."],
            ["FR-WO-03", "A vendor Block action shall require a categorized reason and shall route back to the same vendor on Resend."],
            ["FR-WO-04", "Every stage transition shall be recorded as an immutable audit event, including who performed it and when."],
        ],
        widths=[1.7, 4.6],
    )

    page_break()

    # ================= 12. VENDOR PORTAL =================
    h1_("12. Functional Requirements — Vendor Portal")

    h2("12.1 Vendor Identity and Portal Login")
    para(
        "A Vendor record (representing a maintenance company) can have at most one linked portal login — a "
        "regular ApplicationUser assigned the Vendor Identity role. An admin (Vendor.Manage) creates this login "
        "directly from the vendor's detail page, entering an email; the system generates a one-time temporary "
        "password shown once via a transient notice for the admin to relay to the vendor out of band (no "
        "automated email is sent for this specific credential-delivery step). Passwords can subsequently be "
        "reset the same way."
    )

    h2("12.2 Portal Access Flow")
    add_diagram("vendor_portal_flow.png", "Figure 12.1 — Vendor Portal access and action flow")
    para(
        "On login, a Vendor-role account is routed straight to the Vendor Portal rather than the normal "
        "employee/manager dashboard, and its sidebar shows only Vendor Portal navigation — the internal "
        "ShiftFlow application (scheduling, assets, administration, etc.) is entirely unreachable and invisible "
        "to a vendor login."
    )

    h2("12.3 Security Boundary")
    para(
        "Every Vendor Portal action re-verifies ownership in application code — the work order's VendorId must "
        "match the caller's own Vendor record — independent of and in addition to the Vendor role check. This "
        "means a vendor account can never reach another vendor's work order even by guessing or manually typing "
        "a URL: the request is rejected outright (403) rather than merely being hidden from a list."
    )
    simple_table(
        ["Functional Requirement ID", "Requirement"],
        [
            ["FR-VP-01", "A vendor login shall see only work orders whose VendorId matches their own Vendor record."],
            ["FR-VP-02", "Every Vendor Portal request shall re-verify ownership server-side, not rely on the UI alone to hide unrelated data."],
            ["FR-VP-03", "A Vendor-role login shall never render or reach the internal employee/manager application."],
        ],
        widths=[1.7, 4.6],
    )

    page_break()

    # ================= 13. CONTRACTS & VENDOR MANAGEMENT =================
    h1_("13. Functional Requirements — Contracts & Vendor Management")

    h2("13.1 Vendors")
    para(
        "Vendors are maintenance-provider companies, each with a status (Active/Suspended), contact details, "
        "specialization, and an optional linked portal login (Section 12.1). Only Active vendors are offered "
        "when creating a new contract."
    )

    h2("13.2 Contracts")
    para(
        "A Contract belongs to one vendor and has a type — Purchase, Warranty, Service, or Insurance — a "
        "number, start/end dates, cost, and notes, and covers a many-to-many set of assets. Only Service-type "
        "contracts are consulted when the work order workflow resolves a vendor automatically (Section 11.1); "
        "the other types exist for record-keeping (warranty coverage, purchase history, insurance) but do not "
        "drive the repair workflow."
    )

    h2("13.3 Linked Assets: Individual Search and Category-Based Bulk Add")
    para(
        "The contract form's Linked Assets field is a chip-based multi-select supporting two complementary "
        "ways to build the asset list: a type-ahead search by asset tag or name (one asset at a time), and a "
        "bulk “Add by Category” control — choosing a top-level category (optionally narrowed to one of its "
        "subcategories, with the subcategory control hidden entirely when the category has none) and clicking "
        "Add All immediately adds every asset currently in that category (and, for a top-level pick, its "
        "subcategories) as removable chips alongside anything already selected, with duplicates skipped "
        "automatically."
    )
    add_diagram("contract_category_link.png", "Figure 13.1 — Category-based bulk asset linking")
    para(
        "This is a one-time snapshot, not a live link: bulk-added assets become ordinary linked-asset records "
        "that can be individually removed afterward, and assets added to the category later are only picked up "
        "by clicking Add All again."
    )
    simple_table(
        ["Functional Requirement ID", "Requirement"],
        [
            ["FR-CT-01", "The system shall only offer Active vendors when creating a new contract."],
            ["FR-CT-02", "The vendor-resolution logic for work orders shall consider only Contract.ContractType = Service."],
            ["FR-CT-03", "The Linked Assets picker shall support both individual search-based selection and category/subcategory bulk selection in the same control."],
        ],
        widths=[1.7, 4.6],
    )

    page_break()

    # ================= 14. LOCALIZATION =================
    h1_("14. Localization")

    h2("14.1 Bilingual Support")
    para(
        "The entire user interface is available in English and Arabic. Translation is driven by a single "
        "static English-to-Arabic dictionary; a scoped language service resolves the active language from a "
        "persistent cookie and exposes simple lookup and date-token translation helpers to every Razor view. "
        "An untranslated key falls back to displaying its raw English text rather than failing, so partial "
        "translation coverage degrades gracefully instead of breaking a page."
    )

    h2("14.2 RTL/LTR Rendering")
    para(
        "Switching to Arabic is a genuine right-to-left layout change, not just a text swap: the page's HTML "
        "direction attribute flips, and an RTL-mirrored Bootstrap stylesheet build is loaded instead of the "
        "standard LTR build. Data-annotation validation messages are also localized through a custom string-"
        "localizer implementation backed by the same translation dictionary. Language can be switched at any "
        "time from the sidebar, redirecting back to the current page in the newly selected language."
    )
    simple_table(
        ["Functional Requirement ID", "Requirement"],
        [
            ["FR-LOC-01", "Every user-facing page shall render correctly in both English (LTR) and Arabic (RTL)."],
            ["FR-LOC-02", "Switching language shall persist for the user's session/browser and apply immediately without requiring re-login."],
        ],
        widths=[1.7, 4.6],
    )

    page_break()

    # ================= 15. MOBILE COMPANION APP =================
    h1_("15. Mobile Companion App")

    para(
        "ShiftFlow.Mobile is a thin .NET MAUI shell application, not an independently implemented mobile "
        "client. Its interface is a single full-screen WebView loading the same ShiftFlow.Web MVC site, plus a "
        "loading indicator during initial load — there is no native business logic, local data model, or "
        "feature set beyond what ShiftFlow.Web itself renders. It inherits 100% of the web application's "
        "functionality, RBAC enforcement, and localization (including RTL/LTR, since all rendering happens "
        "server-side) automatically and without duplication."
    )
    para(
        "It targets Android (always), plus iOS and Mac Catalyst on non-Windows build hosts, and Windows — a "
        "genuine single-project cross-platform MAUI build. Development builds point at the local development "
        "server using platform-appropriate loopback addressing (the Android emulator's host alias, or "
        "localhost for iOS Simulator/Windows) over plain HTTP, deliberately avoiding the self-signed "
        "development-certificate trust problem on-device; an Android network-security configuration explicitly "
        "whitelists cleartext traffic only to those specific development addresses. Release builds always point "
        "at a fixed production HTTPS URL."
    )
    simple_table(
        ["Functional Requirement ID", "Requirement"],
        [
            ["FR-MOB-01", "The mobile app shall present the full ShiftFlow.Web application inside a native WebView shell, with no divergent feature set."],
            ["FR-MOB-02", "Release builds shall only ever load the application over HTTPS from the configured production URL."],
        ],
        widths=[1.7, 4.6],
    )

    page_break()
