using System.Text.Json;

namespace GeelyOpenTool;

public sealed partial class MainForm
{
    private static readonly string[] DeployRoots = { "/system/", "/vendor/", "/product/", "/odm/" };

    private static readonly string[] SnapshotPaths =
    {
        "/system/priv-app", "/system/app", "/system/etc", "/system/fonts", "/system/build.prop",
        "/vendor/etc", "/vendor/build.prop",
    };

    private sealed record DeployedFile(string Remote, bool Existed, string? Mode, string? Owner);

    private sealed class DeployManifest
    {
        public List<DeployedFile> Files { get; set; } = new();
        public List<string> CreatedDirs { get; set; } = new();
        public List<string> RemovedOat { get; set; } = new();
    }

    private Control SystemPage()
    {
        var apk = Txt(480);
        var pkg = Txt(320);
        var deployDir = Txt(480);
        var restoreDir = Txt(480);

        return Page(
            Group("طريقة عمل أدوات الفلاش التجارية لـ IHU624G",
                Row(Lbl("adb root ← adb remount ← نسخ ملفات APK والإعدادات إلى /system ثم إعادة التشغيل. كل عملية هنا تأخذ نسخة احتياطية تلقائياً."))),
            Group("الصلاحيات والحماية",
                Row(Btn("adb root + remount", () => Run("root + remount", RootRemount)),
                    Btn("فحص الحماية", () => Run("فحص الحماية", ct => Shell(ct,
                        "echo build_type=$(getprop ro.build.type); echo build_tags=$(getprop ro.build.tags); echo verity=$(getprop ro.boot.veritymode); " +
                        "echo privapp_permissions=$(getprop ro.control_privapp_permissions); echo selinux=$(getenforce)"))),
                    Btn("تعطيل dm-verity", () =>
                    {
                        if (!Confirm("تعطيل dm-verity يسمح بالكتابة على /system ويتطلب إعادة تشغيل. متابعة؟")) return;
                        Run("disable-verity", async ct =>
                        {
                            await Adb(ct, false, "root");
                            await Adb(ct, false, "wait-for-device");
                            await Adb(ct, true, "disable-verity");
                            Log("أعد تشغيل الوحدة ثم نفّذ root + remount.");
                        });
                    }))),
            Group("S2 استبدال أو إضافة تطبيق نظام (مثل GeelyAutoLauncher)",
                Row(Lbl("APK:"), apk, Plain("...", () => PickFile(apk, "APK|*.apk"))),
                Row(Lbl("اسم الحزمة أو اسم المجلد:"), pkg,
                    Btn("استبدال تطبيق نظام موجود", () =>
                    {
                        if (!File.Exists(apk.Text.Trim())) { Warn("اختر ملف APK."); return; }
                        if (!ConfirmDanger($"سيتم استبدال تطبيق النظام {pkg.Text.Trim()} بالملف:\n{apk.Text.Trim()}")) return;
                        Run("استبدال تطبيق نظام", ct => ReplaceSystemApp(ct, apk.Text.Trim(), Need(pkg, "الحزمة")));
                    }),
                    Btn("إضافة كتطبيق priv-app جديد", () =>
                    {
                        if (!File.Exists(apk.Text.Trim())) { Warn("اختر ملف APK."); return; }
                        var name = string.Concat(pkg.Text.Trim().Where(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.'));
                        if (name.Length == 0) name = Path.GetFileNameWithoutExtension(apk.Text.Trim());
                        if (!ConfirmDanger($"سيتم إنشاء /system/priv-app/{name}/{name}.apk")) return;
                        Run("إضافة priv-app", ct => AddPrivApp(ct, apk.Text.Trim(), name));
                    }))),
            Group("S1 نشر حزمة نظام (مجلد بنفس بنية الجذر: system\\priv-app\\..., vendor\\etc\\...)",
                Row(deployDir, Plain("...", () => PickFolder(deployDir)),
                    Btn("نشر الحزمة", () =>
                    {
                        var dir = deployDir.Text.Trim();
                        if (!Directory.Exists(dir)) { Warn("اختر مجلد الحزمة."); return; }
                        var count = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length;
                        if (!ConfirmDanger($"سيتم نشر {count} ملف من:\n{dir}\nإلى نظام الوحدة.")) return;
                        Run("نشر حزمة نظام", ct => Deploy(ct, dir));
                    }))),
            Group("تحسين الأداء بعد الفلاش (dexopt) — يستغرق عدة دقائق",
                Row(Btn("تحسين كل التطبيقات (compile -m speed -f -a)", () =>
                    {
                        if (!Confirm("سيُعاد تجميع كل التطبيقات بوضع speed. قد يستغرق 5-20 دقيقة ولا تطفئ السيارة أثناءه. متابعة؟")) return;
                        Run("compile speed all", ct => Shell(ct, "cmd package compile -m speed -f -a"));
                    }),
                    Btn("تحسين الحزمة أعلاه فقط", () => Run("compile speed", ct =>
                        Shell(ct, $"cmd package compile -m speed -f {Need(pkg, "الحزمة")}"))),
                    Btn("إرجاع الوضع الافتراضي (--reset)", () =>
                    {
                        if (!Confirm("إرجاع كل التطبيقات لوضع التجميع الافتراضي؟")) return;
                        Run("compile reset", ct => Shell(ct, "cmd package compile --reset -a"));
                    })),
                Row(Lbl("يُنصح به بعد استبدال تطبيقات النظام (تُحذف ملفات oat القديمة) لتسريع الإقلاع والواجهة."))),
            Group("الاستعادة",
                Row(Lbl("مجلد deploy_... من backups:"), restoreDir, Plain("...", () => PickFolder(restoreDir)),
                    Btn("استعادة", () =>
                    {
                        var dir = restoreDir.Text.Trim();
                        if (!File.Exists(Path.Combine(dir, "deploy.json"))) { Warn("هذا المجلد لا يحتوي deploy.json."); return; }
                        if (!Confirm("استعادة الملفات الأصلية وحذف الملفات المضافة؟")) return;
                        Run("استعادة", ct => RestoreDeploy(ct, dir));
                    }))),
            Group("اكتشاف واجهة التحكم بالسيارة (نوافذ، أبواب، مكيف، صوت)",
                Row(Btn("جمع معلومات واجهة التحكم من الوحدة", () => Run("اكتشاف واجهة التحكم", CarApiDiscovery)),
                    Btn("فحص مفتاح المنصة (AOSP test-key)", () => Run("فحص مفتاح المنصة", CheckPlatformKey)),
                    Btn("مراقبة زر الصوت في المقود (30 ث)", () => Run("مراقبة أزرار المقود", WatchSteeringKeys)),
                    Plain("فتح مجلد النسخ", () => OpenFolder(Paths.Backups))),
                Row(Lbl("يحفظ تقريراً في backups\\carapi_... — الخطوة الأولى قبل بناء تطبيق أزرار التحكم والأوامر الصوتية العربية."))),
            Group("S4 ملفات الإضاءة / إعدادات ECARX",
                Row(Btn("البحث عن ملفات الإعداد في الوحدة", () => Run("بحث ملفات الإعداد", async ct =>
                {
                    await Adb(ct, false, "root");
                    await Adb(ct, false, "wait-for-device");
                    await Adb(ct, false, "shell",
                        "find /system /vendor /odm /product /data/vendor -type f \\( -iname '*ecarx*' -o -iname '*local_config*' " +
                        "-o -iname '*light*' -o -iname '*ambient*' -o -iname '*atmosphere*' \\) 2>/dev/null | grep -v -E '\\.(so|apk|odex|vdex|oat)$'");
                }))),
                Row(Lbl("انسخ المسار الذي تريده، ضع ملفك المعدّل في حزمة نشر بنفس المسار ثم انشرها (النسخة الأصلية تُحفظ تلقائياً)."))));
    }

    private async Task RootRemount(CancellationToken ct)
    {
        var r = await Adb(ct, false, "root");
        if (r.Output.Contains("cannot run as root", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("الوحدة لا تسمح بـ adb root (نظام user). يلزم root بطريقة أخرى.");
        await Adb(ct, false, "wait-for-device");
        var m = await Adb(ct, false, "remount");
        if (m.Code != 0 || !m.Output.Contains("remount succeeded", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("فشل remount — جرّب «تعطيل dm-verity» ثم أعد التشغيل.");
    }

    private async Task SystemSnapshot(CancellationToken ct)
    {
        await Adb(ct, false, "root");
        await Adb(ct, false, "wait-for-device");
        var build = (await AdbQuiet(ct, "shell", "getprop ro.build.display.id")).Output.Trim();
        var tag = string.Concat(build.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
        var dir = Path.Combine(Paths.Backups, $"snapshot_{DateTime.Now:yyyyMMdd_HHmmss}_{tag}");
        Directory.CreateDirectory(dir);
        Log($"الحفظ في: {dir}");

        File.WriteAllText(Path.Combine(dir, "getprop.txt"), (await AdbQuiet(ct, "shell", "getprop")).Output);
        File.WriteAllText(Path.Combine(dir, "packages.txt"), (await AdbQuiet(ct, "shell", "pm list packages -f")).Output);
        File.WriteAllText(Path.Combine(dir, "disabled.txt"), (await AdbQuiet(ct, "shell", "pm list packages -d")).Output);
        File.WriteAllText(Path.Combine(dir, "mounts.txt"), (await AdbQuiet(ct, "shell", "cat /proc/mounts")).Output);

        foreach (var remote in SnapshotPaths)
        {
            ct.ThrowIfCancellationRequested();
            var localParent = LocalMirror(dir, remote[..remote.LastIndexOf('/')]);
            Directory.CreateDirectory(localParent);
            Log($"سحب {remote}");
            var r = await AdbQuiet(ct, "pull", remote, localParent);
            Log(r.Code == 0 ? "  ✔" : $"  ✖ {r.Output.Trim().Split('\n').LastOrDefault()}");
        }
        Log("اكتملت اللقطة.");
    }

    private async Task ReplaceSystemApp(CancellationToken ct, string apk, string package)
    {
        var r = await Shell(ct, $"pm path {package}");
        var remote = r.Output.Split('\n').Select(l => l.Trim())
            .Where(l => l.StartsWith("package:")).Select(l => l["package:".Length..])
            .FirstOrDefault();
        if (remote is null) throw new InvalidOperationException($"الحزمة {package} غير موجودة.");
        if (remote.StartsWith("/data/"))
            throw new InvalidOperationException($"{package} مثبّت كتحديث في /data. ألغِ التحديث أولاً ثم أعد المحاولة، أو استعمل التثبيت العادي.");
        if (!DeployRoots.Any(remote.StartsWith))
            throw new InvalidOperationException($"مسار غير متوقع: {remote}");
        await DeploySingle(ct, apk, remote);
    }

    private async Task AddPrivApp(CancellationToken ct, string apk, string name)
    {
        var enforce = (await AdbQuiet(ct, "shell", "getprop ro.control_privapp_permissions")).Output.Trim();
        if (enforce == "enforce")
            Log("تحذير: ro.control_privapp_permissions=enforce — إن طلب التطبيق صلاحيات مميزة غير مُدرجة فقد تتعطل الإقلاع. استعمل الاستعادة عند المشكلة.");
        await DeploySingle(ct, apk, $"/system/priv-app/{name}/{name}.apk");
    }

    private async Task DeploySingle(CancellationToken ct, string localFile, string remote)
    {
        var stage = Path.Combine(Path.GetTempPath(), $"geely_stage_{Guid.NewGuid():N}");
        try
        {
            var target = LocalMirror(stage, remote);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(localFile, target);
            await Deploy(ct, stage);
        }
        finally
        {
            try { Directory.Delete(stage, true); } catch { }
        }
    }

    private async Task Deploy(CancellationToken ct, string root, bool systemOnly = false)
    {
        var items = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetRelativePath(root, f) is var rel
                        && !string.Equals(rel, PackageManifestFile, StringComparison.OrdinalIgnoreCase)
                        && !rel.StartsWith("apk" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && !rel.StartsWith("apks" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Select(f => (Local: f, Remote: "/" + Path.GetRelativePath(root, f).Replace('\\', '/')))
            .Where(i => !systemOnly || DeployRoots.Any(i.Remote.StartsWith))
            .OrderBy(i => i.Remote, StringComparer.Ordinal)
            .ToList();
        if (items.Count == 0) throw new InvalidOperationException("المجلد فارغ.");
        var carSpecific = items.Where(i => CarSpecific.IsMatch(i.Remote)).Select(i => i.Remote).ToList();
        if (carSpecific.Count > 0)
            throw new InvalidOperationException("مرفوض — بيانات خاصة بالسيارة (CAR_SPECIFIC) لا تُنشر أبداً:\n" + string.Join("\n", carSpecific.Take(10)));
        var bad = items.Where(i => !DeployRoots.Any(i.Remote.StartsWith)).Select(i => i.Remote).ToList();
        if (bad.Count > 0)
            throw new InvalidOperationException("مسارات غير مسموحة (يجب أن تبدأ بـ system/ أو vendor/ أو product/ أو odm/):\n" + string.Join("\n", bad.Take(10)));

        await RootRemount(ct);

        var backup = Path.Combine(Paths.Backups, $"deploy_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(backup);
        var manifestPath = Path.Combine(backup, "deploy.json");
        var manifest = new DeployManifest();
        void Save() => File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        Save();
        Log($"النسخة الاحتياطية للنشر: {backup}");

        for (var i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (local, remote) = items[i];
            Log($"[{i + 1}/{items.Count}] {remote}");

            var stat = (await AdbQuiet(ct, "shell", $"stat -c '%a %u:%g' {Q(remote)} 2>/dev/null")).Output.Trim();
            string? mode = null, owner = null;
            var existed = stat.Length > 0 && char.IsDigit(stat[0]);
            if (existed)
            {
                var parts = stat.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                mode = parts[0];
                owner = parts.Length > 1 ? parts[1] : "0:0";
                var localBackup = LocalMirror(Path.Combine(backup, "files"), remote);
                Directory.CreateDirectory(Path.GetDirectoryName(localBackup)!);
                await Adb(ct, true, "pull", remote, localBackup);
            }

            var parent = remote[..remote.LastIndexOf('/')];
            if (remote.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            {
                var oat = parent + "/oat";
                var hasOat = (await AdbQuiet(ct, "shell", $"[ -d {Q(oat)} ] && echo Y")).Output.Trim() == "Y";
                if (hasOat && !manifest.RemovedOat.Contains(oat))
                {
                    var localOatParent = LocalMirror(Path.Combine(backup, "files"), parent);
                    Directory.CreateDirectory(localOatParent);
                    await Adb(ct, true, "pull", oat, localOatParent);
                    await Shell(ct, $"rm -rf {Q(oat)}");
                    manifest.RemovedOat.Add(oat);
                }
            }

            var created = await EnsureRemoteDirs(ct, parent);
            manifest.CreatedDirs.AddRange(created.Where(d => !manifest.CreatedDirs.Contains(d)));

            await Adb(ct, true, "push", local, remote);
            await Shell(ct, $"chmod {mode ?? "644"} {Q(remote)} && chown {owner ?? "0:0"} {Q(remote)} && (restorecon {Q(remote)} 2>/dev/null; true)");
            manifest.Files.Add(new DeployedFile(remote, existed, mode, owner));
            Save();
        }
        await Shell(ct, "sync");
        Log("اكتمل النشر. أعد تشغيل الوحدة لتطبيق التغييرات.");
        if (manifest.RemovedOat.Count > 0)
            Log("حُذفت ملفات oat قديمة: بعد الإقلاع اضغط «تحسين كل التطبيقات» لاستعادة السرعة.");
    }

    private async Task<List<string>> EnsureRemoteDirs(CancellationToken ct, string dir)
    {
        var segments = dir.Trim('/').Split('/');
        var script = new List<string>();
        var path = "";
        foreach (var s in segments)
        {
            path += "/" + s;
            script.Add($"[ -d {Q(path)} ] || {{ mkdir {Q(path)} && chmod 755 {Q(path)} && chown 0:0 {Q(path)} && (restorecon {Q(path)} 2>/dev/null; true) && echo CREATED:{path}; }}");
        }
        var r = await Shell(ct, string.Join("; ", script));
        return r.Output.Split('\n').Select(l => l.Trim())
            .Where(l => l.StartsWith("CREATED:")).Select(l => l["CREATED:".Length..]).ToList();
    }

    private async Task RestoreDeploy(CancellationToken ct, string backup)
    {
        var manifest = JsonSerializer.Deserialize<DeployManifest>(File.ReadAllText(Path.Combine(backup, "deploy.json")))
                       ?? throw new InvalidOperationException("deploy.json غير صالح.");
        await RootRemount(ct);

        foreach (var f in Enumerable.Reverse(manifest.Files))
        {
            ct.ThrowIfCancellationRequested();
            if (f.Existed)
            {
                var local = LocalMirror(Path.Combine(backup, "files"), f.Remote);
                Log($"استعادة {f.Remote}");
                await Adb(ct, true, "push", local, f.Remote);
                await Shell(ct, $"chmod {f.Mode} {Q(f.Remote)} && chown {f.Owner} {Q(f.Remote)} && (restorecon {Q(f.Remote)} 2>/dev/null; true)");
            }
            else
            {
                Log($"حذف {f.Remote}");
                await Shell(ct, $"rm -f {Q(f.Remote)}");
            }
        }

        foreach (var oat in manifest.RemovedOat)
        {
            var parent = oat[..oat.LastIndexOf('/')];
            var local = Path.Combine(LocalMirror(Path.Combine(backup, "files"), parent), "oat");
            if (!Directory.Exists(local)) continue;
            Log($"استعادة {oat}");
            await Adb(ct, true, "push", local, parent);
            await Shell(ct, $"find {Q(oat)} -type d -exec chmod 755 {{}} +; find {Q(oat)} -type f -exec chmod 644 {{}} +; " +
                            $"chown -R 0:0 {Q(oat)}; (restorecon -R {Q(oat)} 2>/dev/null; true)");
        }

        foreach (var d in Enumerable.Reverse(manifest.CreatedDirs))
            await AdbQuiet(ct, "shell", $"rmdir {Q(d)} 2>/dev/null");

        await Shell(ct, "sync");
        Log("اكتملت الاستعادة. أعد تشغيل الوحدة.");
    }

    private static readonly (string File, string Command)[] CarApiProbes =
    {
        ("services.txt", "service list"),
        ("car_service.txt", "dumpsys car_service"),
        ("vehicle_hal.txt", "lshal 2>/dev/null | grep -iE 'vehicle|automotive|ecarx|geely'"),
        ("framework.txt", "ls -la /system/framework /vendor/framework 2>/dev/null"),
        ("privapp_car.txt", "ls -la /system/priv-app /system/app /vendor/app 2>/dev/null | grep -iE 'car|ecarx|geely|flyme|vehicle|adapt|voice|speech|hvac|window|door'"),
        ("packages_car.txt", "pm list packages -f | grep -iE 'car|ecarx|geely|flyme|vehicle|adapt|voice|speech|asr|tts|kiki|iflytek|baidu|hvac'"),
        ("libraries.txt", "pm list libraries"),
        ("features.txt", "pm list features"),
        ("permissions_car.txt", "pm list permissions -g -f 2>/dev/null | grep -iE -B1 -A6 'car|ecarx|vehicle|window|door|hvac'"),
        ("privapp_permissions.txt", "cat /system/etc/permissions/privapp-permissions*.xml /vendor/etc/permissions/*.xml 2>/dev/null | grep -iE 'package=|car|ecarx|vehicle'"),
        ("props_car.txt", "getprop | grep -iE 'ecarx|vehicle|car|geely|flyme|voice|asr|build.tags|build.type|fingerprint'"),
        ("platform_cert.txt", "dumpsys package android | grep -iE 'signatures|cert|flags=' | head -n 20"),
        ("adaptapi_files.txt", "find /system /vendor /product -iname '*adapt*' -o -iname '*vehicle*' -o -iname '*ecarx*' 2>/dev/null | head -n 400"),
        ("voice_vr_res.txt", "getprop | grep -iE 'vr\\.|vr_|voice|asr|tts'; ps -A | grep -iE 'vr_res|voice|asr|baidu|iflytek|kiki|codriver|speech'"),
        ("voice_files.txt", "find /system /vendor /product \\( -iname '*vr_res*' -o -iname '*voice*' -o -iname '*asr*' -o -iname '*tts*' \\) 2>/dev/null | head -n 300"),
        ("hvac_package.txt", "dumpsys package com.flyme.auto.hvac 2>/dev/null | head -n 200"),
        ("audio_inputs.txt", "dumpsys media.audio_policy 2>/dev/null | grep -iE -A2 'IN_BUILTIN_MIC|IN_BACK_MIC|input' | head -n 120"),
        ("activities_voice.txt", "cmd package query-activities -a android.speech.action.RECOGNIZE_SPEECH 2>/dev/null; dumpsys voiceinteraction 2>/dev/null | head -n 80"),
    };

    private async Task CarApiDiscovery(CancellationToken ct)
    {
        var root = await AdbQuiet(ct, "root");
        Log(root.Output.Trim());
        await Adb(ct, false, "wait-for-device");

        var dir = Path.Combine(Paths.Backups, $"carapi_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        foreach (var (file, command) in CarApiProbes)
        {
            ct.ThrowIfCancellationRequested();
            var r = await AdbQuiet(ct, "shell", command);
            File.WriteAllText(Path.Combine(dir, file), r.Output);
            var lines = r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            Log($"{file}: {lines} سطر");
        }

        var carDump = File.ReadAllText(Path.Combine(dir, "car_service.txt"));
        foreach (var key in new[] { "WINDOW", "DOOR", "HVAC", "SEAT", "MIRROR", "SUNROOF", "LIGHT" })
        {
            var hits = carDump.Split('\n').Count(l => l.Contains(key, StringComparison.OrdinalIgnoreCase));
            if (hits > 0) Log($"  car_service يذكر {key}: {hits} مرة");
        }
        Log($"التقرير في: {dir}");
    }

    private const string AospPlatformCertSha256 = "c8a2e9bccf597c2fb6dc66bee293fc13f2fc47ec77bc6b2b0d52c11f51192ab8";

    private async Task CheckPlatformKey(CancellationToken ct)
    {
        Log($"build tags: {(await AdbQuiet(ct, "shell", "getprop ro.build.tags")).Output.Trim()}");
        var local = Path.Combine(Path.GetTempPath(), $"framework-res_{Guid.NewGuid():N}.apk");
        try
        {
            await Adb(ct, true, "pull", "/system/framework/framework-res.apk", local);
            var hashes = ApkCertHashes(local);
            if (hashes.Count == 0)
            {
                Log("لا يوجد توقيع v1 في framework-res.apk — افحصه يدوياً بـ apksigner verify --print-certs.");
                return;
            }
            foreach (var h in hashes) Log($"SHA-256 شهادة المنصة: {h}");
            if (hashes.Contains(AospPlatformCertSha256))
                Log("✔ الوحدة موقّعة بمفتاح AOSP التجريبي العام: تطبيق موقّع به مع sharedUserId=android.uid.system يحصل على uid 1000 ويمكنه التحكم بخصائص السيارة (الإضاءة، المكيف، النوافذ...).");
            else
                Log("✖ مفتاح المنصة خاص بالشركة — التحكم بخصائص السيارة يتطلب تثبيت التطبيق في /system/priv-app مع قائمة صلاحيات.");
        }
        finally
        {
            try { File.Delete(local); } catch { }
        }
    }

    private static List<string> ApkCertHashes(string apk)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(apk);
        var result = new List<string>();
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)
                     && (e.Name.EndsWith(".RSA", StringComparison.OrdinalIgnoreCase)
                         || e.Name.EndsWith(".DSA", StringComparison.OrdinalIgnoreCase)
                         || e.Name.EndsWith(".EC", StringComparison.OrdinalIgnoreCase))))
        {
            using var s = entry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            var cms = new System.Security.Cryptography.Pkcs.SignedCms();
            cms.Decode(ms.ToArray());
            foreach (var cert in cms.Certificates)
                result.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cert.RawData)).ToLowerInvariant());
        }
        return result.Distinct().ToList();
    }

    private async Task WatchSteeringKeys(CancellationToken ct)
    {
        Log("اضغط زر الصوت (وأزرار المقود الأخرى) عدة مرات خلال 30 ثانية...");
        await AdbQuiet(ct, "logcat", "-c");
        await Adb(ct, false, "shell",
            "timeout 30 logcat -v time 2>/dev/null | grep -iE 'keyserver|ECARX_KEY|VOICEASSIST|InputDispatcher|KeyEvent|vr_|codriver' ; true");
        Log("انتهت المراقبة. ابحث عن ecarx.intent.action.ECARX_KEY_RVOICEASSIST_EVENT أو أسماء أحداث مشابهة.");
    }

    private async Task Screenshot(CancellationToken ct)
    {
        var dir = Path.Combine(Paths.Base, "screenshots");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"screen_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        await Shell(ct, "screencap -p /sdcard/geely_screen.png");
        await Adb(ct, true, "pull", "/sdcard/geely_screen.png", file);
        await AdbQuiet(ct, "shell", "rm /sdcard/geely_screen.png");
        Log($"حُفظت في {file}");
    }

    private static string LocalMirror(string root, string remote) =>
        Path.Combine(root, remote.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    private static string Q(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
