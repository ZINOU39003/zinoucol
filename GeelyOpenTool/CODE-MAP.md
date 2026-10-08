# Zinou Coolray — خريطة الكود (زر ← دالة ← أوامر ← ملفات ← root/remount)

C# WinForms .NET 10 — كل التنفيذ عبر `adb.exe` (platform-tools) و `mtk.py` (mtkclient) و PowerShell (التعريب).
لا توجد ملفات .bat / .sh / .py خاصة بالبرنامج؛ السكربتات الوحيدة هي `overlay-ar\*.ps1`.

## الملفات

| الملف | المحتوى |
|---|---|
| `Program.cs` | نقطة الدخول (`Application.Run(new MainForm())`) |
| `Proc.cs` | `Paths` (مسارات adb/mtkclient/backups/packages) + `Proc.RunAsync` (تشغيل أي أداة خارجية وقراءة مخرجاتها، إلغاء = قتل شجرة العملية) |
| `MainForm.cs` | النافذة، القائمة، الطرفية، شريط التقدم، صفحات الجهاز/MTK/التطبيقات/التعريب/الطرفية، دوال adb العامة (`Adb`, `AdbQuiet`, `Shell`, `TargetSerial`, `Run`, `Log`, `Stage`, `Fraction`) |
| `MainForm.Flash.cs` | صفحة الفلاش، **S0 Preflight**، `FullFlash` (S0→S4)، `InstallApkPackage`، `ApplyRegional` (S4) |
| `MainForm.Packages.cs` | نموذج الحزم (`zinou-package.json`)، المكتبة، الصانع، `ValidatePackageFiles`، قاعدة `CarSpecific`، `ReadFirmware`، `Compare`، `FindSystemPackage` |
| `MainForm.Backup.cs` | صفحة النسخ الاحتياطي: `SaveRecoveryInfo`، `DeviceSpecificBackup` (MTK) |
| `MainForm.System.cs` | `RootRemount`، `SystemSnapshot`، `Deploy` (الكتابة الفعلية على /system)، `RestoreDeploy`، dexopt، اكتشاف واجهة السيارة، فحص مفتاح المنصة، مراقبة أزرار المقود |
| `MainForm.Guide.cs` | صفحة «طريقة العمل» |
| `Ui.cs` | الثيم والعناصر المرسومة (أزرار، بطاقات، دائرة/خط التقدم، الشريط العلوي) — لا منطق adb |
| `..\overlay-ar\*.ps1` | استخراج نصوص تطبيق + بناء/توقيع/تثبيت RRO Overlay عربي (aapt2، zipalign، apksigner) |

## ملاحظة مهمة حول S4 / S5

في هذا البرنامج: **S0 فحص، S1 نسخ احتياطي، S2 حزمة النظام، S3 حزمة التطبيقات، S4 الإعدادات الإقليمية.** لا يوجد S5.
**لا يوجد أي كود CarPropertyManager / GInputBridge / Property IDs داخل البرنامج.** هذه موجودة فقط في:
- `research\S4-S5-mapping.md` (توثيق الاستخراج)
- `research\drive_assist\...` (ملفات مصدرها مستودع drive_assist العام: voiceprobe، sysprobe، DATA-CATALOG، field-catalog)

داخل البرنامج يوجد فقط **اكتشاف** (قراءة): `CarApiDiscovery` (يبحث عن vr_res، الصوت، HVAC)، `WatchSteeringKeys` (logcat لـ ECARX_KEY / VOICEASSIST)، `CheckPlatformKey`.

---

## صفحة الفلاش (S0 → S4) — `MainForm.Flash.cs`

### زر «فحص فقط — S0»
```
Preflight(ct, FlashOptions)                      ← قراءة فقط
 1 adb devices                                     ListDevices / TargetSerial
 2 adb shell getprop ...                           ReadFirmware (11 خاصية)
 3 مقارنة البصمة                                   FindSystemPackage / Compare + ValidatePackageFiles
 4 adb root ; adb wait-for-device ; shell id -u    فقط إن كانت الحزمة requiresRoot  (adb root يعيد تشغيل adbd — ليس كتابة)
 5 shell cat /proc/mounts ; [ -d /part ]           حالة ro/rw لكل قسم في الحزمة
 6 ro.boot.veritymode ; shell df -k /part          هل remount ممكن + المساحة
 7 adb pull <apk الأصلي> → ApkCertHashes           مقارنة التواقيع (v1 فقط)
 8 PASS / STOP
```
- يكتب على السيارة: **لا شيء**. على الحاسوب: لا شيء.
- root: يجربه فقط. remount: لا.

### زر «▶ تشغيل الفلاش (S0 → S4)» ← `FullFlash`
| المرحلة | الدالة | الأوامر | يكتب | root | remount |
|---|---|---|---|---|---|
| S0 | `Preflight` | أعلاه | لا | فحص | لا |
| S1 | `SaveRecoveryInfo("flash")` | `shell getprop`، `pm list packages -f/-d`، `cat /proc/mounts`، `df -k`، `ls -l /dev/block/.../by-name`، `cat build.prop`، `settings list` | الحاسوب فقط: `backups\car_<serialno>\<ts>_flash\*` + `preflight.txt` | لا | لا |
| S2 | `Deploy(dir, systemOnly:true)` | انظر Deploy | /system /vendor /product /odm | نعم | نعم |
| S3 | `InstallApkPackage` | `settings put global verifier_* 0`، `adb install -r [-g] [-d]`، `pm grant`، `appops set` | /data/app | لا | لا |
| S4 | `ApplyRegional` | `adb root`، `settings put global auto_time_zone 0`، `setprop persist.sys.timezone`، `setprop persist.sys.locale`، `pm grant MoreLocale CHANGE_CONFIGURATION`، `appops set <steering> GET_USAGE_STATS/SYSTEM_ALERT_WINDOW allow`، `settings put system oobe_phase 2`، `device_provisioned 1`، `user_setup_complete 1`، اختياري `setenforce 0` | خصائص persist + settings | setprop persist يحتاج root | لا |
| S5* | `cmd package compile -m speed -f -a` (اختياري) | | /data/dalvik-cache | لا | لا |

### `Deploy(ct, root, systemOnly)` — `MainForm.System.cs` (الكتابة الحقيقية الوحيدة على النظام)
```
فلترة: تجاهل zinou-package.json و apk\ و apks\ ؛ رفض CarSpecific ؛ رفض أي مسار خارج /system /vendor /product /odm
RootRemount:   adb root → adb wait-for-device → adb remount   (يفشل = توقف قبل أي كتابة)
backups\deploy_<ts>\deploy.json   (يُحفظ بعد كل ملف)
لكل ملف:
  shell stat -c '%a %u:%g' <remote>          ← وجود الملف + صلاحياته
  adb pull <remote> backups\deploy_<ts>\files\...   (إن وُجد)
  إن كان .apk و <dir>/oat موجود: adb pull oat ثم shell rm -rf <dir>/oat
  EnsureRemoteDirs: mkdir + chmod 755 + chown 0:0 + restorecon (ويُسجّل المجلدات المنشأة)
  adb push <local> <remote>
  shell chmod <mode|644> && chown <owner|0:0> && restorecon
shell sync
```
الاستعادة `RestoreDeploy(deploy.json)`: RootRemount ← push الأصليات + chmod/chown/restorecon ← rm الملفات الجديدة ← إعادة oat ← rmdir المجلدات المنشأة ← sync.

## صفحة النسخ الاحتياطي — `MainForm.Backup.cs`
| الزر | الدالة | الأوامر | يكتب على السيارة | root |
|---|---|---|---|---|
| حفظ معلومات الاسترداد | `SaveRecoveryInfo` | shell getprop / pm / mounts / df / by-name / build.prop / settings | لا | لا |
| أخذ لقطة النظام | `SystemSnapshot` (System.cs) | `adb root`، `adb pull` لـ /system/priv-app /system/app /system/etc /system/fonts /system/build.prop /vendor/etc /vendor/build.prop | لا | يجرب root لقراءة كاملة |
| نسخ الأقسام الخاصة بالسيارة | `DeviceSpecificBackup` | `python mtk.py r nvram,nvdata,... <files>` (BROM) | لا (قراءة) | — |
| عرض جدول الأقسام | `Mtk(printgpt)` | `mtk.py printgpt` | لا | — |

## صانع ومكتبة الحزم — `MainForm.Packages.cs`
- كل العمليات على **الحاسوب** فقط (نسخ ملفات إلى `packages\<name>\`)، عدا `ReadFirmware` (getprop قراءة).
- `ValidatePackageFiles`: رفض CAR_SPECIFIC، رفض ملفات خارج البنية حسب النوع، SYSTEM_PACKAGE بدون بصمة = خطأ، فحص install.json/permissions.json.
- قاعدة CAR_SPECIFIC (regex): `nvram|nvdata|nvcfg|proinfo|seccfg|protect1|protect2|protect_f|protect_s|persist|calibration|calib|keybox|attestation|serialno|imei` أو أي `*.img`.

## صفحة نشر النظام (root) — `MainForm.System.cs`
| الزر | الدالة | يكتب | root | remount |
|---|---|---|---|---|
| adb root + remount | `RootRemount` | لا (يركّب rw) | نعم | نعم |
| فحص الحماية | shell getprop build.type/tags/veritymode/privapp + getenforce | لا | لا | لا |
| تعطيل dm-verity | `adb root` + `adb disable-verity` | **vbmeta/verity** | نعم | — |
| استبدال تطبيق نظام موجود | `ReplaceSystemApp` ← `pm path` ← `DeploySingle` ← `Deploy` | /system | نعم | نعم |
| إضافة كتطبيق priv-app جديد | `AddPrivApp` ← `DeploySingle` ← `Deploy` | /system/priv-app | نعم | نعم |
| نشر الحزمة | `Deploy(dir)` | /system… | نعم | نعم |
| تحسين كل التطبيقات / الحزمة / reset | `cmd package compile ...` | /data | لا | لا |
| استعادة | `RestoreDeploy` | /system… | نعم | نعم |
| جمع معلومات واجهة التحكم | `CarApiDiscovery` (17 أمر قراءة: service list، dumpsys car_service، lshal، vr_res، voice، hvac، audio_policy …) | لا | يجرب root | لا |
| فحص مفتاح المنصة | `CheckPlatformKey`: pull framework-res.apk ← SHA-256 الشهادة ← مقارنة بـ AOSP test-key `c8a2e9bc…2ab8` | لا | لا | لا |
| مراقبة زر الصوت في المقود | `WatchSteeringKeys`: `timeout 30 logcat | grep ECARX_KEY|VOICEASSIST|KeyEvent|vr_|codriver` | لا | لا | لا |
| البحث عن ملفات الإعداد | `find /system /vendor /odm /product /data/vendor -iname '*ecarx*|*light*|*ambient*'` | لا | يجرب root | لا |

## صفحة MTK — `MainForm.cs`
`mtk.py printgpt` / `rl <dir> [--skip userdata]` (Full Dump) / `r <part> <file>` / **`w <part> <file>`** / **`wl <dir>`** / `reset`. الكتابة (w / wl) بتأكيد مزدوج.

## ملفات JSON
| الملف | أين | من يكتبه |
|---|---|---|
| `zinou-package.json` | جذر كل حزمة | `CreatePackage` / `SaveManifest` |
| `install.json` | APK_PACKAGE | المستخدم (قالب عند الإنشاء) — `{ "apps": [ { "file", "package", "grantAll", "allowDowngrade" } ] }` |
| `permissions.json` | APK_PACKAGE | المستخدم — `{ "<pkg>": { "grant": [..], "appops": { "OP": "allow" } } }` |
| `deploy.json` | `backups\deploy_<ts>\` | `Deploy` — قائمة الملفات (existed/mode/owner) + المجلدات المنشأة + oat المحذوف |

## نقاط يمكن جعلها أكثر أماناً (مقترحات مفتوحة)
1. `ApplyRegional` يستدعي `adb root` و `setprop persist.*` دون المرور بـ Preflight عند الضغط على «تطبيق S4 فقط».
2. فحص التوقيع يقرأ v1 فقط؛ APK بتوقيع v2/v3 فقط يظهر «غير قابل للقراءة» (تنبيه وليس منعاً).
3. `setenforce 0` و `disable-verity` متاحان بزر — يمكن إخفاؤهما خلف «وضع المطوّر».
4. لا يوجد فحص `privapp-permissions` للتطبيقات الجديدة في priv-app (تنبيه فقط عند `ro.control_privapp_permissions=enforce`).
5. لا تحقق بعد النشر (مثلاً `pm path` + `dumpsys package` لكل تطبيق منشور) قبل إعادة التشغيل.
