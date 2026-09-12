# New EN → AR strings (branch `ui/ui-orders`)

Append these to `ShiftFlow.Web/Localization/Translations.cs` (that file is owned by the shared-UI
branch, so they are listed here instead of edited in). Every one is already referenced through
`Loc.T(...)` by the order views on this branch; until they are added, the Arabic UI falls back to
the English text.

| English key | Arabic |
| --- | --- |
| `Excel: inspection orders` | إكسل: أوامر المعاينة |
| `Excel: maintenance orders` | إكسل: أوامر الصيانة |
| `No orders match these filters` | لا توجد أوامر مطابقة لهذه الفلاتر |
| `Try a different search or clear the filters.` | جرّب بحثًا آخر أو امسح الفلاتر. |
| `Inspection surveys and direct-fix maintenance orders will show up here.` | ستظهر هنا عمليات المعاينة وأوامر الصيانة المباشرة. |
| `Vendor and in-house repair jobs, from report to close` | أعمال الإصلاح لدى الموردين وداخليًا، من البلاغ حتى الإغلاق |
| `All Stages` | كل المراحل |
| `All Priorities` | كل الأولويات |
| `No work orders match these filters` | لا توجد أوامر عمل مطابقة لهذه الفلاتر |
| `No work orders yet` | لا توجد أوامر عمل بعد |
| `Work orders raised from a defect or a vendor job will show up here.` | ستظهر هنا أوامر العمل الناتجة عن عطل أو عمل لدى مورد. |
| `Raise a repair job against one asset` | إنشاء عمل إصلاح لأصل واحد |
| `Breadcrumb` | مسار التنقل |
| `More` | المزيد |
| `Stops this order — recorded outcomes are kept, nothing further can be reported.` | يوقف هذا الأمر — تُحفظ النتائج المسجّلة ولا يمكن تسجيل المزيد. |
| `Record outcome` | تسجيل النتيجة |
| `Send this order back to the assignee to redo?` | إعادة هذا الأمر إلى المكلَّف لإعادة التنفيذ؟ |
| `Every work order assigned to your company` | كل أوامر العمل المسندة إلى شركتك |
| `Photos or documents — a rejected file is named back to you with the reason.` | صور أو مستندات — يُعرض اسم أي ملف مرفوض مع سبب الرفض. |
| `Try a wider period or a different category.` | جرّب فترة أوسع أو فئة أخرى. |
| `Anything assigned to you or your groups shows up here.` | يظهر هنا كل ما يُسند إليك أو إلى مجموعاتك. |
| `Months you had orders in show up here.` | تظهر هنا الأشهر التي لديك فيها أوامر. |

Note: the em dashes above are U+2014 and must match the view text exactly, or the lookup misses.

Already present in `Translations.cs` and reused unchanged by these views (no action needed):
`Export`, `All Types`, `All Statuses`, `No orders yet`, `Filter`, `Clear`, `records`, `Period`,
`This month`, `All time`, `Last 30 days`, `Last 90 days`, `Zone`, `Saving…`, `Saved`,
`Force Close`, `Force-close reason`, `Reason (optional)`, `Cancel Inspection Order`,
`Cancel Maintenance Order`, `Return to Open`, `Reassign`.
