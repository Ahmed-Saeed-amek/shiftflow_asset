# TRANSLATIONS-admin.md

New EN → AR strings introduced by the admin UI pass (branch `ui/ui-admin`).
Append these to `ShiftFlow.Web/Localization/Translations.cs` on merge — that file is owned by the
shared branch, so nothing here has been written into it yet.

Every entry below was checked against the current `Translations.cs`; keys that already existed
(`All Statuses`, `records`, `Actions`, `Profile`, `Deactivate`, `Activate`, `Details`, `View`,
`Toggle all`, `Permissions`, `Portal Login`, `New user scope`, `New group scope`, …) are reused
rather than duplicated.

| EN key | AR |
| --- | --- |
| `More` | المزيد |
| `Asset scope` | نطاق الأصول |
| `No users match these filters` | لا يوجد مستخدمون مطابقون لهذه الفلاتر |
| `No users yet` | لا يوجد مستخدمون بعد |
| `Try a different search term, role, or status.` | جرّب كلمة بحث أو دورًا أو حالة مختلفة. |
| `Add the first account to get started.` | أضف أول حساب للبدء. |
| `Import from directory` | استيراد من الدليل |
| `Type a name or email above, then pick a match to import that account from the Microsoft directory instead of creating a password login.` | اكتب الاسم أو البريد الإلكتروني بالأعلى ثم اختر نتيجة مطابقة لاستيراد الحساب من دليل مايكروسوفت بدلاً من إنشاء حساب بكلمة مرور. |
| `Search by name, contact, or email…` | ابحث بالاسم أو جهة الاتصال أو البريد الإلكتروني… |
| `No vendors match these filters` | لا يوجد موردون مطابقون لهذه الفلاتر |
| `No vendors yet` | لا يوجد موردون بعد |
| `Try a different search term or status.` | جرّب كلمة بحث أو حالة مختلفة. |
| `Add the first vendor to start assigning work orders.` | أضف أول مورد لتتمكن من إسناد أوامر العمل. |
| `The vendor signs in to the portal with this address and a temporary password.` | يسجّل المورد الدخول إلى البوابة بهذا البريد الإلكتروني وكلمة مرور مؤقتة. |
| `Create a reusable group of employees so you can assign a whole team to an inspection order at once.` | أنشئ مجموعة موظفين قابلة لإعادة الاستخدام لإسناد فريق كامل إلى أمر فحص دفعة واحدة. |
| `Zone Category` | فئة المنطقة |
| `Edit user scope` | تعديل نطاق المستخدم |
| `Edit group scope` | تعديل نطاق المجموعة |
| `No employee scopes yet` | لا توجد نطاقات موظفين بعد |
| `Without a scope an employee sees every asset they are otherwise permitted to see.` | بدون نطاق، يرى الموظف كل الأصول المسموح له بها. |
| `No group scopes yet` | لا توجد نطاقات مجموعات بعد |
| `Scope a whole group at once instead of repeating the same rule per employee.` | حدّد نطاق مجموعة كاملة دفعة واحدة بدلاً من تكرار القاعدة لكل موظف. |
| `Delete this scope? {0} will see every asset they are otherwise permitted to see.` | حذف هذا النطاق؟ سيرى {0} كل الأصول المسموح له بها. |
| `Delete this scope? Members of {0} will see every asset they are otherwise permitted to see.` | حذف هذا النطاق؟ سيرى أعضاء {0} كل الأصول المسموح لهم بها. |
| `Assistant` | المساعد |
| `Ask STEP about inspection orders, groups, and more — by voice or text.` | اسأل STEP عن أوامر الفحص والمجموعات والمزيد — صوتًا أو نصًا. |

## Terminology note

`Location Category` is still a live key elsewhere in the app (Zones, Assets). This pass renamed it
to **Zone Category** only in the two asset-visibility scope forms
(`Views/UserAssetScopes/_Form.cshtml`, `_GroupForm.cshtml`), per the product decision. The
underlying model property is still `LocationCategoryId` — only the label changed. If the rest of
the app adopts the same wording later, the `Location Category` key can be retired.
