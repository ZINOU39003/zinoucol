using System.Text;

namespace GeelyOpenTool;

public sealed partial class MainForm
{
    private const string SteeringAppDefault = "com.salat.gbinder";
    private const string NoApkPackage = "(بدون حزمة تطبيقات)";

    private TextBox? flashSystemBox;
    private readonly ComboBox flashApkBox = new() { Width = 480, DropDownStyle = ComboBoxStyle.DropDownList };

    private sealed record RegionalSettings(string TimeZone, string Locale, string SteeringApp, bool SkipOobe, bool SelinuxPermissive);

    private sealed record FlashOptions(bool DeploySystem, string ManualSystemDir, PackageEntry? Apps, bool OverrideCheck);

    private sealed class PreflightReport
    {
        public FirmwareInfo Car { get; set; } = new();
        public PackageEntry? System { get; set; }
        public PackageEntry? Apps { get; set; }
        public List<string> Blocking { get; } = new();
        public List<string> Warnings { get; } = new();
        public StringBuilder Text { get; } = new();
        public bool Ok => Blocking.Count == 0;
    }

    private Control FullFlashPage()
    {
        var systemDir = Txt(480);
        flashSystemBox = systemDir;
        var deploySystem = Check("نشر حزمة نظام (SYSTEM_PACKAGE)", true);
        var overrideCheck = Check("تجاوز عدم تطابق البصمة (خطر — للمطوّر فقط)", false);
        var timezone = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDown, RightToLeft = RightToLeft.No };
        timezone.Items.AddRange(new object[] { "Africa/Algiers", "Africa/Casablanca", "Africa/Tunis", "Africa/Cairo", "Asia/Riyadh", "Asia/Dubai", "Europe/Paris" });
        timezone.SelectedIndex = 0;
        var locale = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDown, RightToLeft = RightToLeft.No };
        locale.Items.AddRange(new object[] { "ar-DZ", "ar-SA", "fr-FR", "en-US" });
        locale.SelectedIndex = 0;
        var steeringApp = Txt(220, SteeringAppDefault);
        var skipOobe = Check("تخطي معالج الإعداد الأول (oobe_phase=2)", true);
        var selinux = Check("SELinux Permissive (مؤقت حتى إعادة التشغيل — يُضعف الحماية)", false);
        var compile = Check("تحسين الأداء بعد التثبيت (compile speed)", false);
        ReloadApkPackageChoices(LoadPackages());

        var settings = () => new RegionalSettings(
            timezone.Text.Trim(), locale.Text.Trim(), steeringApp.Text.Trim(), skipOobe.Checked, selinux.Checked);
        var options = () => new FlashOptions(deploySystem.Checked, systemDir.Text.Trim(), flashApkBox.SelectedItem as PackageEntry, overrideCheck.Checked);

        return Page(
            Group("مسار العمل",
                Row(Lbl("S0 فحص قدرات النظام (قراءة فقط) ← S1 نسخ احتياطي ← S2 نشر حزمة النظام ← S3 حزمة التطبيقات ← S4 الإعدادات")),
                Row(Lbl("إذا وجد المدقق أي عدم تطابق أو مانع: STOP — لا يُكتب أي شيء على السيارة."))),
            Group("الحزم",
                Row(deploySystem),
                Row(Lbl("حزمة النظام:"), systemDir, Plain("...", () => PickFolder(systemDir)), Plain("تلقائي", () => systemDir.Clear()),
                    Plain("المكتبة", () => selectPage?.Invoke(PackagesPageIndex))),
                Row(Lbl("اتركه فارغاً (موصى به): المدقق يختار من المكتبة الحزمة المطابقة لبصمة السيارة تلقائياً.")),
                Row(Lbl("حزمة التطبيقات:"), flashApkBox, Plain("تحديث", () => ReloadApkPackageChoices(LoadPackages()))),
                Row(overrideCheck)),
            Group("S4 الإعدادات الإقليمية والإصلاحات",
                Row(Lbl("المنطقة الزمنية:"), timezone, Lbl("اللغة:"), locale, Lbl("تطبيق أزرار المقود:"), steeringApp),
                Row(skipOobe, selinux, compile),
                Row(Btn("تطبيق S4 فقط", () => Run("S4 الإعدادات الإقليمية", ct => ApplyRegional(ct, settings()))),
                    Btn("تعطيل فحص التطبيقات (Play Protect)", () => Run("تعطيل verifier", DisableVerifier)))),
            Group("التنفيذ",
                Row(Btn("فحص فقط — S0 (بدون أي كتابة)", () => Run("فحص قدرات النظام", async ct =>
                    {
                        var report = await Preflight(ct, options());
                        if (!report.Ok) throw new InvalidOperationException("STOP — لا يمكن الفلاش على هذه السيارة بهذه الحزم.");
                    })),
                    Btn("▶ تشغيل الفلاش (S0 → S4)", () =>
                    {
                        var o = options();
                        if (!o.DeploySystem && o.Apps is null) { Warn("لا شيء للتنفيذ: فعّل حزمة النظام أو اختر حزمة تطبيقات."); return; }
                        if (!ConfirmDanger("سيُنفَّذ: فحص ← نسخ احتياطي ← نشر.\nإن فشل الفحص يتوقف البرنامج قبل أي كتابة.")) return;
                        Run("الفلاش الكامل", ct => FullFlash(ct, o, settings(), compile.Checked));
                    }),
                    Btn("إعادة تشغيل الوحدة", () => Run("reboot", ct => Adb(ct, true, "reboot")))),
                Row(Lbl("للتراجع: صفحة «نشر النظام» ← الاستعادة، باختيار مجلد deploy_... الذي يظهر في السجل."))));
    }

    private static CheckBox Check(string text, bool value) =>
        new() { Text = text, Checked = value, AutoSize = true, Margin = new Padding(3, 7, 12, 3) };

    private void ReloadApkPackageChoices(List<PackageEntry> all)
    {
        var current = (flashApkBox.SelectedItem as PackageEntry)?.Dir;
        flashApkBox.Items.Clear();
        flashApkBox.Items.Add(NoApkPackage);
        foreach (var p in all.Where(p => !p.Manifest.IsSystem)) flashApkBox.Items.Add(p);
        var match = flashApkBox.Items.OfType<PackageEntry>().FirstOrDefault(p => p.Dir == current);
        flashApkBox.SelectedItem = match ?? (object)NoApkPackage;
    }

    // ---------------- S0: system capability check (read-only) ----------------

    private async Task<PreflightReport> Preflight(CancellationToken ct, FlashOptions o)
    {
        var rep = new PreflightReport();
        void Note(string line) { Log(line); rep.Text.AppendLine(line); }
        void Block(string why) { rep.Blocking.Add(why); Note("  ✖ " + why); }
        void WarnLine(string why) { rep.Warnings.Add(why); Note("  تنبيه: " + why); }
        const int steps = 8;

        Note($"[1/{steps}] adb devices");
        var devices = await ListDevices(ct);
        var serial = await TargetSerial(ct);
        var state = devices.FirstOrDefault(d => d.Serial == serial).State;
        if (state != "device") { Block(serial.Length == 0 ? "لا توجد سيارة متصلة." : $"حالة الجهاز {serial}: {state ?? "غير موجود"}"); return Finish(rep, Note); }
        Note($"  ✔ {serial}");

        Note($"[2/{steps}] getprop");
        var car = rep.Car = await ReadFirmware(ct);
        if (car.IsEmpty) { Block("تعذر قراءة خصائص النظام."); return Finish(rep, Note); }
        Note($"  model={car.Model}  device={car.Device}  platform={car.Platform}  android={car.Android}  type={car.BuildType}");

        Note($"[3/{steps}] fingerprint / build ID");
        Note($"  buildId={car.BuildId}  incremental={car.Incremental}");
        Note($"  fingerprint={car.Fingerprint}");
        if (o.DeploySystem)
        {
            if (o.ManualSystemDir.Length > 0)
            {
                var m = LoadManifest(o.ManualSystemDir);
                rep.System = m is null ? null : new PackageEntry(o.ManualSystemDir, m);
                if (m is null) Block($"المجلد ليس حزمة Zinou (لا يوجد {PackageManifestFile}).");
                else if (!m.IsSystem) Block("الحزمة المختارة ليست SYSTEM_PACKAGE.");
            }
            else
            {
                rep.System = FindSystemPackage(car);
                if (rep.System is null) Block("لا توجد في المكتبة SYSTEM_PACKAGE مطابقة لبصمة هذه السيارة — ابنِ حزمة لهذا الإصدار.");
            }

            if (rep.System is { } sys && sys.Manifest.IsSystem)
            {
                var result = Compare(sys.Manifest, car);
                Note($"  الحزمة: {sys.Manifest.Name} v{sys.Manifest.PackageVersion} ← buildId={sys.Manifest.BuildId}");
                if (result == Compatibility.Match) Note("  ✔ MATCH");
                else if (o.OverrideCheck) WarnLine($"{result} — تم التجاوز بطلب المستخدم.");
                else Block(result == Compatibility.Mismatch ? "MISMATCH — الحزمة مبنية لإصدار نظام آخر." : "الحزمة بدون بصمة — لا يمكن التحقق.");
                if (sys.Manifest.Platform.Length > 0 && !$"{car.Model} {car.Device} {car.Platform} {car.BuildId}".Contains(sys.Manifest.Platform, StringComparison.OrdinalIgnoreCase))
                    WarnLine($"المنصة {sys.Manifest.Platform} لا تظهر في خصائص السيارة (معلومة فقط).");
                foreach (var e in ValidatePackageFiles(sys.Dir, sys.Manifest)) Block(e);
            }
        }
        if (o.Apps is { } apps)
        {
            rep.Apps = apps;
            var result = Compare(apps.Manifest, car);
            Note($"  حزمة التطبيقات: {apps.Manifest.Name} (Android {apps.Manifest.Android}+) ← {result}");
            if (result != Compatibility.Match) Block($"حزمة التطبيقات تحتاج Android {apps.Manifest.Android}+.");
            foreach (var e in ValidatePackageFiles(apps.Dir, apps.Manifest)) Block(e);
        }

        var needsRoot = rep.System?.Manifest is { IsSystem: true, RequiresRoot: true };
        Note($"[4/{steps}] root availability");
        if (needsRoot)
        {
            var root = await AdbQuiet(ct, "root");
            await AdbQuiet(ct, "wait-for-device");
            var uid = (await AdbQuiet(ct, "shell", "id -u")).Output.Trim();
            if (uid == "0") Note("  ✔ adb root متاح (uid=0)");
            else Block($"adb root غير متاح ({root.Output.Trim()}) — نظام {car.BuildType}. حزم النظام غير ممكنة على هذه الوحدة.");
        }
        else Note("  غير مطلوب (لا توجد حزمة نظام).");

        var partitions = rep.System is null ? new List<string>() : PackagePartitions(rep.System.Dir);
        Note($"[5/{steps}] mount state");
        var mounts = (await AdbQuiet(ct, "shell", "cat /proc/mounts")).Output.Replace("\r", "").Split('\n')
            .Select(l => l.Split(' ')).Where(p => p.Length >= 4).ToList();
        var readOnly = new List<string>();
        foreach (var part in partitions)
        {
            var exists = (await AdbQuiet(ct, "shell", $"[ -d /{part} ] && echo Y")).Output.Trim() == "Y";
            if (!exists) { Block($"الحزمة تحتاج /{part} وهو غير موجود في هذه الوحدة."); continue; }
            var entry = mounts.LastOrDefault(m => m[1] == "/" + part) ?? (part == "system" ? mounts.LastOrDefault(m => m[1] == "/") : null);
            var opts = entry?[3].Split(',') ?? Array.Empty<string>();
            var mode = opts.Contains("rw") ? "rw" : opts.Contains("ro") ? "ro" : "?";
            Note($"  /{part}: {(entry is null ? "جزء من قسم آخر" : $"{entry[0]} {entry[2]} {mode}")}");
            if (mode != "rw") readOnly.Add(part);
        }

        Note($"[6/{steps}] writable partitions");
        if (readOnly.Count == 0) Note(partitions.Count == 0 ? "  لا توجد أقسام مطلوبة." : "  ✔ كل الأقسام المطلوبة rw.");
        else if (car.Verity == "enforcing")
            Block($"dm-verity مفعّل و {string.Join(", ", readOnly.Select(p => "/" + p))} للقراءة فقط — remount سيفشل. نفّذ «تعطيل dm-verity» من صفحة نشر النظام ثم أعد التشغيل.");
        else Note($"  {string.Join(", ", readOnly.Select(p => "/" + p))} ro — سيُعاد تركيبها rw في S2 (verity={(car.Verity.Length == 0 ? "غير مفعّل" : car.Verity)}).");
        foreach (var part in partitions)
        {
            var bytes = Directory.Exists(Path.Combine(rep.System!.Dir, part))
                ? Directory.GetFiles(Path.Combine(rep.System.Dir, part), "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
                : 0;
            var df = (await AdbQuiet(ct, "shell", $"df -k /{part} | tail -1")).Output.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (df.Length >= 4 && long.TryParse(df[3], out var freeKb))
            {
                Note($"  /{part}: متاح {freeKb / 1024} MB — الحزمة {bytes / 1024 / 1024} MB");
                if (bytes / 1024 > freeKb) WarnLine($"/{part}: المساحة الحرة قد لا تكفي (تُستبدل ملفات موجودة فقد يكفي).");
            }
        }

        Note($"[7/{steps}] package / signature compatibility");
        if (rep.System is { } s) await CheckSystemSignatures(ct, s.Dir, Note, WarnLine);
        if (rep.Apps is { } a) await CheckAppSignatures(ct, a.Dir, Note, WarnLine);
        if (rep.System is null && rep.Apps is null) Note("  لا توجد حزم.");

        Note($"[8/{steps}] النتيجة");
        return Finish(rep, Note);
    }

    private static PreflightReport Finish(PreflightReport rep, Action<string> note)
    {
        note(rep.Ok
            ? $"✔ PASS — يمكن المتابعة ({rep.Warnings.Count} تنبيه)."
            : $"✖ STOP — {rep.Blocking.Count} مانع. لم يُكتب أي شيء على السيارة.");
        return rep;
    }

    private static List<string> PackagePartitions(string dir) =>
        SystemPackageRoots.Where(p => Directory.Exists(Path.Combine(dir, p))
                                      && Directory.EnumerateFiles(Path.Combine(dir, p), "*", SearchOption.AllDirectories).Any()).ToList();

    private async Task CheckSystemSignatures(CancellationToken ct, string dir, Action<string> note, Action<string> warn)
    {
        foreach (var apk in Directory.GetFiles(dir, "*.apk", SearchOption.AllDirectories))
        {
            var remote = "/" + Path.GetRelativePath(dir, apk).Replace('\\', '/');
            var local = ApkCertHashes(apk);
            if (local.Count == 0) { warn($"{remote}: لا يوجد توقيع v1 قابل للقراءة (قد يكون v2 فقط)."); continue; }
            var exists = (await AdbQuiet(ct, "shell", $"[ -f {Q(remote)} ] && echo Y")).Output.Trim() == "Y";
            if (!exists) { note($"  {remote}: جديد"); continue; }
            var existing = await RemoteCertHashes(ct, remote);
            if (existing.Count == 0) note($"  {remote}: تعذر قراءة توقيع الأصلي");
            else if (existing.Intersect(local).Any()) note($"  ✔ {remote}: نفس التوقيع");
            else warn($"{remote}: توقيع مختلف عن الأصلي — ستُمسح بيانات التطبيق، وإن كان يستعمل sharedUserId=system فلن يعمل إلا بمفتاح المنصة.");
        }
    }

    private async Task CheckAppSignatures(CancellationToken ct, string dir, Action<string> note, Action<string> warn)
    {
        var plan = LoadJson<InstallPlan>(Path.Combine(dir, "install.json"));
        foreach (var item in plan?.Apps.Where(i => !string.IsNullOrEmpty(i.Package)) ?? Enumerable.Empty<InstallItem>())
        {
            var path = (await AdbQuiet(ct, "shell", $"pm path {item.Package}")).Output.Split('\n')
                .Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("package:"))?["package:".Length..];
            if (path is null) { note($"  {item.Package}: غير مثبت — تثبيت جديد"); continue; }
            var local = ApkCertHashes(Path.Combine(dir, "apks", item.File));
            var existing = await RemoteCertHashes(ct, path);
            if (local.Count == 0 || existing.Count == 0) note($"  {item.Package}: تعذر مقارنة التوقيع");
            else if (existing.Intersect(local).Any()) note($"  ✔ {item.Package}: نفس التوقيع — تحديث عادي");
            else warn($"{item.Package}: توقيع مختلف عن المثبت — التحديث سيفشل؛ ألغِ تثبيت النسخة القديمة أولاً.");
        }
    }

    private async Task<List<string>> RemoteCertHashes(CancellationToken ct, string remote)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"zinou_sig_{Guid.NewGuid():N}.apk");
        try
        {
            var r = await AdbQuiet(ct, "pull", remote, tmp);
            return r.Code == 0 && File.Exists(tmp) ? ApkCertHashes(tmp) : new List<string>();
        }
        finally { try { File.Delete(tmp); } catch { } }
    }

    // ---------------- full flash pipeline ----------------

    private async Task FullFlash(CancellationToken ct, FlashOptions o, RegionalSettings settings, bool compile)
    {
        var started = DateTime.Now;
        var summary = new List<string>();
        var stages = compile ? 6 : 5;

        Stage(1, stages, "S0 الفحص");
        Log("── S0 فحص قدرات النظام (قراءة فقط) ──");
        var rep = await Preflight(ct, o);
        if (!rep.Ok)
            throw new InvalidOperationException("STOP — " + string.Join(" | ", rep.Blocking));

        Stage(2, stages, "S1 النسخ الاحتياطي");
        Log("── S1 النسخ الاحتياطي ──");
        var backup = await SaveRecoveryInfo(ct, "flash");
        File.WriteAllText(Path.Combine(backup, "preflight.txt"), rep.Text.ToString());
        summary.Add($"النسخ الاحتياطي: {backup}");

        Stage(3, stages, "S2 حزمة النظام");
        Log("── S2 نشر حزمة النظام ──");
        if (rep.System is { } sys)
        {
            await Deploy(ct, sys.Dir, systemOnly: true);
            summary.Add($"حزمة النظام: {sys.Manifest.Name} v{sys.Manifest.PackageVersion}");
        }
        else Log("لا توجد حزمة نظام — تخطي.");

        Stage(4, stages, "S3 التطبيقات");
        Log("── S3 حزمة التطبيقات ──");
        if (rep.Apps is { } apps) summary.AddRange(await InstallApkPackage(ct, apps.Dir));
        else Log("لا توجد حزمة تطبيقات — تخطي.");

        Stage(5, stages, "S4 الإعدادات");
        Log("── S4 الإعدادات الإقليمية ──");
        summary.AddRange(await ApplyRegional(ct, settings));

        if (compile)
        {
            Stage(6, stages, "تحسين الأداء");
            Log("── تحسين الأداء (قد يستغرق دقائق) ──");
            await Shell(ct, "cmd package compile -m speed -f -a");
            summary.Add("تحسين الأداء: تم");
        }

        Log("════════ ملخص العملية ════════");
        foreach (var line in summary) Log("✔ " + line);
        Log($"المدة: {DateTime.Now - started:mm\\:ss} — أعد تشغيل الوحدة لتطبيق كل التغييرات.");
    }

    private async Task<List<string>> InstallApkPackage(CancellationToken ct, string dir)
    {
        var summary = new List<string>();
        var plan = LoadJson<InstallPlan>(Path.Combine(dir, "install.json")) ?? new InstallPlan();
        var items = plan.Apps.Count > 0
            ? plan.Apps
            : Directory.GetFiles(Path.Combine(dir, "apks"), "*.apk").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(f => new InstallItem { File = Path.GetFileName(f) }).ToList();

        await DisableVerifier(ct);
        var failed = new List<string>();
        for (var i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = items[i];
            Log($"[{i + 1}/{items.Count}] {item.File}");
            var args = new List<string> { "install", "-r" };
            if (item.GrantAll) args.Add("-g");
            if (item.AllowDowngrade) args.Add("-d");
            args.Add(Path.Combine(dir, "apks", item.File));
            var r = await Adb(ct, false, args.ToArray());
            if (r.Code != 0 || r.Output.Contains("Failure", StringComparison.OrdinalIgnoreCase)) failed.Add(item.File);
        }
        summary.Add($"التطبيقات المثبتة: {items.Count - failed.Count}/{items.Count}");
        if (failed.Count > 0) summary.Add("فشل: " + string.Join(", ", failed));

        var permissions = LoadJson<Dictionary<string, PermissionSpec>>(Path.Combine(dir, "permissions.json")) ?? new();
        foreach (var (pkg, spec) in permissions)
        {
            if (!(await AdbQuiet(ct, "shell", $"pm path {pkg}")).Output.Contains("package:")) continue;
            foreach (var perm in spec.Grant) await AdbQuiet(ct, "shell", $"pm grant {pkg} {perm}");
            foreach (var (op, mode) in spec.Appops) await AdbQuiet(ct, "shell", $"appops set {pkg} {op} {mode}");
            Log($"✔ صلاحيات {pkg}: {spec.Grant.Count} grant, {spec.Appops.Count} appops");
        }
        return summary;
    }

    private async Task DisableVerifier(CancellationToken ct)
    {
        await Shell(ct, "settings put global verifier_verify_adb_installs 0");
        await Shell(ct, "settings put global package_verifier_enable 0");
    }

    private async Task<List<string>> ApplyRegional(CancellationToken ct, RegionalSettings s)
    {
        var summary = new List<string>();
        await AdbQuiet(ct, "root");
        await Adb(ct, false, "wait-for-device");

        if (s.TimeZone.Length > 0)
        {
            await Shell(ct, "settings put global auto_time_zone 0");
            await Shell(ct, $"setprop persist.sys.timezone {Q(s.TimeZone)}");
            summary.Add($"المنطقة الزمنية: {s.TimeZone}");
        }
        if (s.Locale.Length > 0)
        {
            await Shell(ct, $"setprop persist.sys.locale {Q(s.Locale)}");
            await AdbQuiet(ct, "shell", $"pm grant {MoreLocalePkg} android.permission.CHANGE_CONFIGURATION");
            summary.Add($"اللغة: {s.Locale} (بعد إعادة التشغيل)");
        }
        if (s.SteeringApp.Length > 0)
        {
            var installed = (await AdbQuiet(ct, "shell", $"pm path {s.SteeringApp}")).Output.Contains("package:");
            if (installed)
            {
                await Shell(ct, $"appops set {s.SteeringApp} GET_USAGE_STATS allow");
                await AdbQuiet(ct, "shell", $"appops set {s.SteeringApp} SYSTEM_ALERT_WINDOW allow");
                summary.Add($"أزرار المقود: GET_USAGE_STATS لـ {s.SteeringApp}");
            }
            else summary.Add($"أزرار المقود: {s.SteeringApp} غير مثبت — تم التخطي");
        }
        if (s.SkipOobe)
        {
            await Shell(ct, "settings put system oobe_phase 2");
            await AdbQuiet(ct, "shell", "settings put global device_provisioned 1; settings put secure user_setup_complete 1");
            summary.Add("OOBE: 2");
        }
        if (s.SelinuxPermissive)
        {
            await Shell(ct, "setenforce 0");
            summary.Add($"SELinux: {(await AdbQuiet(ct, "shell", "getenforce")).Output.Trim()}");
        }
        return summary;
    }
}
