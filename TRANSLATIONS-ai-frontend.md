# New EN → AR strings from the AI assistant frontend

`Localization/Translations.cs` is off-limits on this branch, so the strings the new UI introduces
are collected here. Append them to the dictionary as-is; every one is already wrapped in `Loc.T`
in the views / in `aiConfig.strings` in `_Layout.cshtml`, so they will pick the Arabic up with no
further code change.

Checked against the current `Translations.cs`: `Confirm`, `Cancel`, `Cancelled`, `Done`, `Close`
and `Clear` are already there and are **not** repeated below.

```csharp
// --- AI assistant: drawer chrome ---
["STEP Assistant"] = "مساعد STEP",
["Open full page"] = "فتح الصفحة الكاملة",
["Stop using this page as context"] = "إيقاف استخدام هذه الصفحة كسياق",
["Suggested prompts"] = "اقتراحات",
["Ask me anything…"] = "اسألني أي شيء…",
["Viewing"] = "تعرض",

// --- AI assistant: message actions ---
["Retry"] = "إعادة المحاولة",
["Copy reply"] = "نسخ الرد",
["Copied"] = "تم النسخ",
["Download"] = "تنزيل",
["…and more"] = "…والمزيد",

// --- AI assistant: quick chips (work order page) ---
["Summarize"] = "تلخيص",
["Summarize this order"] = "لخّص هذا الأمر",
["Asset history"] = "سجل الأصل",
["What's the asset's history?"] = "ما هو سجل الأصل؟",
["Send to vendor"] = "إرسال إلى المورد",
["Send to vendor…"] = "إرسال إلى المورد…",

// --- AI assistant: quick chips (asset page) ---
["Health summary"] = "ملخص الحالة",
["Open work orders"] = "أوامر العمل المفتوحة",
["Report a defect"] = "الإبلاغ عن عطل",

// --- AI assistant: quick chips (everywhere else) ---
["My attention"] = "يحتاج انتباهي",
["What needs my attention today?"] = "ما الذي يحتاج انتباهي اليوم؟",
["My open orders"] = "أوامري المفتوحة",
["Low stock parts"] = "قطع غيار منخفضة المخزون",
["Expiring contracts"] = "عقود على وشك الانتهاء",
["Contracts expiring soon"] = "العقود التي ستنتهي قريبًا",
["Export to Excel"] = "تصدير إلى Excel",
["Export work orders to Excel"] = "تصدير أوامر العمل إلى Excel",
```

## Also worth doing while you're in there

Status badges rendered inside an assistant answer are translated through
`window.i18n.status`, which `_Layout` builds from `StatusStyle.JsLabelNames`. That array was
sized for the zone-map popups, so statuses the assistant now surfaces regularly still render in
English under Arabic. Adding these names to `StatusStyle.JsLabelNames` (`Services/StatusStyle.cs`,
also outside this branch) fixes them — no other change needed:

```
"Pending Approval", "Overdue", "Completed", "Cancelled", "Planned",
"Low Stock", "Out of Stock", "In Stock", "Under Maintenance", "Faulty",
"Low", "Medium", "High", "Critical", "Urgent"
```
