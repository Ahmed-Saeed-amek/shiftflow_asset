# New translation strings — frontend-shared

Every English string added by the `fix/frontend-shared` work that is **not** already in
`ShiftFlow.Web/Localization/Translations.cs`. Add each as `Ar["<english>"] = "<arabic>";`
(that file is owned by another agent, so nothing here was written into it directly).

Note the exact punctuation: these use the U+2014 em dash (—) and the U+2026 ellipsis (…),
matching the rest of the dictionary. A mismatched dash means the lookup silently falls back
to English.

| English | Arabic |
|---|---|
| `Couldn't load — please try again.` | `تعذّر التحميل — يُرجى المحاولة مرة أخرى.` |
| `asset(s)` | `أصل/أصول` |
| `Point the camera at an asset's QR label…` | `وجّه الكاميرا نحو ملصق رمز QR الخاص بالأصل…` |
| `Looking up asset…` | `جارٍ البحث عن الأصل…` |
| `That QR code isn't an asset label — keep scanning…` | `رمز QR هذا ليس ملصق أصل — تابع المسح…` |
| `Camera access needs a secure connection (HTTPS), or http://localhost on this same computer — scanning over a plain http:// LAN address like this one is blocked by the browser.` | `يتطلب الوصول إلى الكاميرا اتصالاً آمناً (HTTPS)، أو http://localhost على هذا الجهاز نفسه — المسح عبر عنوان شبكة محلية http:// عادي مثل هذا يحظره المتصفح.` |
| `This browser can't access the camera.` | `لا يستطيع هذا المتصفح الوصول إلى الكاميرا.` |
| `Camera access was denied — allow camera access for this site in your browser settings and try again.` | `تم رفض الوصول إلى الكاميرا — اسمح بالوصول إلى الكاميرا لهذا الموقع من إعدادات المتصفح ثم حاول مرة أخرى.` |
| `No camera was found on this device.` | `لم يتم العثور على كاميرا في هذا الجهاز.` |
| `The camera is already in use by another app or browser tab.` | `الكاميرا قيد الاستخدام بالفعل بواسطة تطبيق آخر أو تبويب آخر في المتصفح.` |
| `Camera access is unavailable — check your browser/device permissions.` | `الوصول إلى الكاميرا غير متاح — تحقق من أذونات المتصفح/الجهاز.` |
| `The QR scanner couldn't be loaded — check your connection and try again.` | `تعذّر تحميل ماسح رمز QR — تحقق من اتصالك ثم حاول مرة أخرى.` |
| `Couldn't look that asset up — please try again.` | `تعذّر البحث عن هذا الأصل — يُرجى المحاولة مرة أخرى.` |
| `Skip to main content` | `تخطَّ إلى المحتوى الرئيسي` |
| `Export Excel` | `تصدير Excel` |
| `Scan QR Code` | `مسح رمز QR` |
| `Page not found` | `الصفحة غير موجودة` |
| `Access denied` | `تم رفض الوصول` |
| `Action not available this way` | `هذا الإجراء غير متاح بهذه الطريقة` |
| `Something went wrong` | `حدث خطأ ما` |
| `The page you're looking for doesn't exist or may have been moved.` | `الصفحة التي تبحث عنها غير موجودة أو ربما تم نقلها.` |
| `You don't have permission to view this page.` | `ليس لديك إذن لعرض هذه الصفحة.` |
| `That action can't be reached by visiting its link directly. Please use the button in the app instead.` | `لا يمكن الوصول إلى هذا الإجراء بفتح رابطه مباشرة. يُرجى استخدام الزر داخل التطبيق بدلاً من ذلك.` |
| `An unexpected error occurred. Please try again, and contact support if the problem continues.` | `حدث خطأ غير متوقع. يُرجى المحاولة مرة أخرى، والتواصل مع الدعم إذا استمرت المشكلة.` |
| `Go to Home` | `الذهاب إلى الرئيسية` |
| `My History` | `سجلّي` |
| `Report Action` | `الإبلاغ عن إجراء` |
| `Scan an asset's QR label to report an action on it` | `امسح ملصق رمز QR الخاص بالأصل للإبلاغ عن إجراء عليه` |

## Already present — no action needed

These are used by the new code but the dictionary already has them:
`Are you sure?`, `Close`, `Remove`, `No matches`, `View`, `Export PDF`, and every status name in
`StatusStyle.JsLabelNames` (`Working`, `Maintenance`, `Defective`, `New`, `Assigned`,
`In Progress`, `Open`, `Dispatched`, `Resolved`, `Closed`, `Sent to Vendor`, `Blocked`,
`Fixed - Pending Confirmation`).
