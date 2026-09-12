# New translation keys — fix/security-config

Append these to `ShiftFlow.Web/Localization/Translations.cs` (that file is owned by another agent
this round, so they are staged here instead). Missing keys already fall back to English at runtime.

| English key | Arabic |
|---|---|
| `Account locked. Try again in {0} minutes.` | `الحساب مقفل. حاول مرة أخرى بعد {0} دقيقة.` |
| `That role is not available to assign.` | `هذا الدور غير متاح للتعيين.` |
| `Directory search is unavailable right now. Contact an administrator.` | `البحث في الدليل غير متاح حالياً. يرجى التواصل مع المسؤول.` |
| `'{0}' has order or audit history and cannot be deleted. Deactivate the account instead to remove their access.` | `'{0}' لديه سجل طلبات أو تدقيق ولا يمكن حذفه. قم بتعطيل الحساب بدلاً من ذلك لإلغاء وصوله.` |
| `User {0} deleted.` | `تم حذف المستخدم {0}.` |
| `Unknown role '{0}'.` | `دور غير معروف '{0}'.` |
| `Unknown permission '{0}'.` | `صلاحية غير معروفة '{0}'.` |
| `Only a system administrator can assign administrator roles.` | `يمكن لمسؤول النظام فقط تعيين أدوار المسؤولين.` |
| `Only a system administrator can grant '{0}'.` | `يمكن لمسؤول النظام فقط منح '{0}'.` |
| `An email is required to create a login.` | `البريد الإلكتروني مطلوب لإنشاء حساب دخول.` |
| `Enter a valid email address.` | `أدخل بريداً إلكترونياً صالحاً.` |
| `Login created.` | `تم إنشاء حساب الدخول.` |
| `Password reset.` | `تم إعادة تعيين كلمة المرور.` |
| `Deactivate {0}?` | `تعطيل {0}؟` |
| `Activate {0}?` | `تفعيل {0}؟` |
| `Delete {0}? This cannot be undone.` | `حذف {0}؟ لا يمكن التراجع عن هذا.` |
| `Delete role '{0}'?` | `حذف الدور '{0}'؟` |
| `Filter` | `تصفية` |

The old key `Account locked. Try in 5 min.` (already in Translations.cs) is now unused — the
lockout window is derived from `IdentityOptions` instead of hardcoded.
