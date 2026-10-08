using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeelyOpenTool;

public sealed partial class MainForm
{
    private const string PackageManifestFile = "zinou-package.json";
    private const string SystemPackageType = "SYSTEM_PACKAGE";
    private const string ApkPackageType = "APK_PACKAGE";

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string[] SystemPackageRoots = { "system", "vendor", "product", "odm" };
    private static readonly string[] Components = { "launcher", "steering", "arabic_fonts", "overlays", "apps", "config" };

    /// <summary>CAR_SPECIFIC data: never allowed in any package (identity, calibration, keys).</summary>
    private static readonly Regex CarSpecific = new(
        @"(^|[\\/_.\-])(nvram|nvdata|nvcfg|proinfo|seccfg|protect1|protect2|protect_f|protect_s|persist|calibration|calib|keybox|attestation|serialno|imei)([\\/_.\-]|$)|\.img$",
        RegexOptions.IgnoreCase);

    private sealed class PackageManifest
    {
        public string PackageVersion { get; set; } = "1.0.0";
        public string Type { get; set; } = SystemPackageType;
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Created { get; set; } = "";
        public string Platform { get; set; } = "IHU624G";
        public string Android { get; set; } = "9";
        public string BuildId { get; set; } = "";
        public string Incremental { get; set; } = "";
        public string Fingerprint { get; set; } = "";
        public string Model { get; set; } = "";
        public bool RequiresRoot { get; set; } = true;
        public bool RequiresRemount { get; set; } = true;
        public List<string> Components { get; set; } = new();

        public bool IsSystem => Type == SystemPackageType;
        public bool HasFirmware => BuildId.Length > 0 || Fingerprint.Length > 0;
    }

    private sealed class FirmwareInfo
    {
        public string BuildId { get; set; } = "";
        public string Incremental { get; set; } = "";
        public string Fingerprint { get; set; } = "";
        public string Model { get; set; } = "";
        public string Device { get; set; } = "";
        public string Android { get; set; } = "";
        public string BuildType { get; set; } = "";
        public string Verity { get; set; } = "";
        public string BootState { get; set; } = "";
        public string Platform { get; set; } = "";
        public string SerialNo { get; set; } = "";

        public bool IsEmpty => BuildId.Length == 0 && Fingerprint.Length == 0;
    }

    private sealed record PackageEntry(string Dir, PackageManifest Manifest)
    {
        public override string ToString()
        {
            var m = Manifest;
            var kind = m.IsSystem ? "[نظام]" : "[تطبيقات]";
            var fw = m.IsSystem ? (m.HasFirmware ? m.BuildId : "بدون بصمة") : $"Android {m.Android}+";
            return $"{kind}  {m.Name}  v{m.PackageVersion}  —  {fw}";
        }
    }

    private enum Compatibility { Match, Mismatch, Unknown }

    private sealed class InstallPlan { public List<InstallItem> Apps { get; set; } = new(); }

    private sealed class InstallItem
    {
        public string File { get; set; } = "";
        public string? Package { get; set; }
        public bool GrantAll { get; set; } = true;
        public bool AllowDowngrade { get; set; }
    }

    private sealed class PermissionSpec
    {
        public List<string> Grant { get; set; } = new();
        public Dictionary<string, string> Appops { get; set; } = new();
    }

    private readonly ComboBox packageList = new() { Width = 560, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label packageDetails = new() { AutoSize = true, Margin = new Padding(4, 8, 4, 3) };

    private Control PackagesPage()
    {
        var type = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
        type.Items.AddRange(new object[] { "SYSTEM_PACKAGE — ملفات النظام (يحتاج root)", "APK_PACKAGE — تطبيقات فقط (بدون تعديل النظام)" });
        type.SelectedIndex = 0;
        var name = Txt(220, "Coolray-AR");
        var version = Txt(80, "1.0.0");
        var platform = Txt(110, "IHU624G");
        var description = Txt(420, "تعريب + لانشر + أزرار المقود");
        var componentChecks = Components.Select(c => Check(c, c is "launcher" or "arabic_fonts" or "overlays")).ToArray();
        var file = Txt(420);
        var remote = Txt(420, "/system/priv-app/");
        var snapshot = Txt(420);

        packageList.SelectedIndexChanged += (_, _) => ShowPackageDetails();
        ReloadPackages();

        return Page(
            Group("أنواع الحزم",
                Row(Lbl("SYSTEM_PACKAGE: ملفات مرتبطة بإصدار النظام (system / vendor / product / odm) — مقفلة على buildId و fingerprint و incremental و model.")),
                Row(Lbl("APK_PACKAGE: تطبيقات تُثبَّت بدون تعديل النظام — apks\\ + install.json + permissions.json.")),
                Row(Lbl("CAR_SPECIFIC (nvram, nvdata, proinfo, seccfg, persist, calibration, keys, serial) لا يدخل أي حزمة — المدقق يرفضه تلقائياً."))),
            Group("مكتبة الحزم",
                Row(Lbl("الحزمة:"), packageList, Plain("تحديث القائمة", ReloadPackages)),
                Row(packageDetails),
                Row(Btn("فحص محتوى الحزمة (Validator)", () => Run("فحص الحزمة", _ =>
                    {
                        var p = SelectedPackage();
                        var errors = ValidatePackageFiles(p.Dir, p.Manifest);
                        ListPackage(p);
                        foreach (var e in errors) Log("✖ " + e);
                        if (errors.Count > 0) throw new InvalidOperationException($"الحزمة غير صالحة ({errors.Count} خطأ).");
                        Log("✔ بنية الحزمة سليمة.");
                        return Task.CompletedTask;
                    })),
                    Plain("استعمال في الفلاش", UseSelectedPackageForFlash),
                    Plain("فتح مجلد الحزمة", () => Guarded(() => OpenFolder(SelectedPackage().Dir))),
                    Plain("فتح مجلد المكتبة", () => OpenFolder(Paths.Packages)))),
            Group("1) إنشاء حزمة جديدة",
                Row(Lbl("النوع:"), type, Lbl("الاسم:"), name, Lbl("الإصدار:"), version, Lbl("المنصة:"), platform),
                Row(Lbl("الوصف:"), description),
                Row(new Control[] { Lbl("المكونات:") }.Concat(componentChecks).ToArray()),
                Row(Btn("إنشاء الحزمة (تُقرأ بصمة السيارة المتصلة)", () => Run("إنشاء حزمة", ct =>
                    CreatePackage(ct, type.SelectedIndex == 0 ? SystemPackageType : ApkPackageType, Need(name, "الاسم"),
                        version.Text.Trim(), platform.Text.Trim(), description.Text.Trim(),
                        componentChecks.Where(c => c.Checked).Select(c => c.Text).ToList())))),
                Row(Lbl("SYSTEM_PACKAGE يجب أن تُنشأ والسيارة المرجعية متصلة حتى تُحفظ بصمتها؛ وإلا يرفضها المدقق عند الفلاش."))),
            Group("2) إضافة ملفات إلى الحزمة المختارة",
                Row(Lbl("ملف:"), file, Plain("...", () => PickFile(file, "All|*.*")),
                    Lbl("مساره في الوحدة:"), remote,
                    Plain("إضافة إلى المسار", () => Guarded(() => AddFileToPackage(file.Text.Trim(), remote.Text.Trim())))),
                Row(Plain("إضافة تطبيقات إلى apks\\", () => Guarded(() => CopyPickedFiles("APK|*.apk", "apks", ApkPackageType))),
                    Plain("إضافة خطوط إلى system\\fonts", () => Guarded(() => CopyPickedFiles("Fonts|*.ttf;*.otf;*.ttc", @"system\fonts", SystemPackageType)))),
                Row(Lbl("لقطة نظام (من backups):"), snapshot, Plain("...", () => PickFolder(snapshot)),
                    Plain("نسخ ملفات من اللقطة", () => Guarded(() => CopyFromSnapshot(snapshot.Text.Trim(), folders: false))),
                    Plain("نسخ مجلد تطبيق من اللقطة", () => Guarded(() => CopyFromSnapshot(snapshot.Text.Trim(), folders: true)))),
                Row(Lbl("install.json (اختياري): ترتيب التثبيت وخيارات كل تطبيق. permissions.json (اختياري): صلاحيات و appops بعد التثبيت."))),
            Group("3) تحديث البصمة",
                Row(Btn("تحديث بصمة الحزمة من السيارة المتصلة", () => Run("تحديث البصمة", async ct =>
                    {
                        var p = SelectedPackage();
                        var car = await ReadFirmware(ct);
                        if (car.IsEmpty) throw new InvalidOperationException("تعذر قراءة خصائص السيارة.");
                        StampFirmware(p.Manifest, car);
                        SaveManifest(p.Dir, p.Manifest);
                        Log($"✔ البصمة: {p.Manifest.BuildId} / {p.Manifest.Fingerprint}");
                        ReloadPackages(p.Dir);
                    }))),
                Row(Lbl("استعملها فقط على السيارة المرجعية التي اختبرت عليها الحزمة."))));
    }

    // ---------------- library ----------------

    private static List<PackageEntry> LoadPackages()
    {
        Directory.CreateDirectory(Paths.Packages);
        return Directory.GetDirectories(Paths.Packages)
            .Select(dir => new PackageEntry(dir, LoadManifest(dir) ?? new PackageManifest { Name = Path.GetFileName(dir) }))
            .OrderBy(p => p.Manifest.Type).ThenBy(p => p.Manifest.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static PackageManifest? LoadManifest(string dir)
    {
        var path = Path.Combine(dir, PackageManifestFile);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(path), ManifestJson); }
        catch (JsonException) { return null; }
    }

    private static void SaveManifest(string dir, PackageManifest manifest) =>
        File.WriteAllText(Path.Combine(dir, PackageManifestFile), JsonSerializer.Serialize(manifest, ManifestJson));

    private static T? LoadJson<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), ManifestJson); }
        catch (JsonException ex) { throw new InvalidOperationException($"{Path.GetFileName(path)} غير صالح: {ex.Message}"); }
    }

    private void ReloadPackages() => ReloadPackages(null);

    private void ReloadPackages(string? select)
    {
        if (InvokeRequired) { BeginInvoke(() => ReloadPackages(select)); return; }
        var all = LoadPackages();
        var current = select ?? (packageList.SelectedItem as PackageEntry)?.Dir;
        packageList.Items.Clear();
        foreach (var p in all) packageList.Items.Add(p);
        var index = all.FindIndex(p => p.Dir == current);
        packageList.SelectedIndex = index >= 0 ? index : all.Count > 0 ? 0 : -1;
        ShowPackageDetails();
        ReloadApkPackageChoices(all);
    }

    private void ShowPackageDetails()
    {
        if (packageList.SelectedItem is not PackageEntry p)
        {
            packageDetails.Text = "المكتبة فارغة — أنشئ أول حزمة من البطاقة «1) إنشاء حزمة جديدة».";
            return;
        }
        var m = p.Manifest;
        var files = Directory.GetFiles(p.Dir, "*", SearchOption.AllDirectories).Count(f => Path.GetFileName(f) != PackageManifestFile);
        packageDetails.Text = $"{m.Type}  •  {m.Description}\n" +
                              (m.IsSystem
                                  ? $"buildId: {(m.HasFirmware ? m.BuildId : "غير محدد")}  •  incremental: {m.Incremental}  •  model: {m.Model}  •  root: {m.RequiresRoot}  •  remount: {m.RequiresRemount}\n"
                                  : $"Android {m.Android}+  •  بدون root\n") +
                              $"المكونات: {string.Join(", ", m.Components)}  •  الملفات: {files}  •  أُنشئت: {m.Created}";
    }

    private PackageEntry SelectedPackage() =>
        packageList.SelectedItem as PackageEntry ?? throw new InvalidOperationException("اختر حزمة من المكتبة أولاً.");

    private void UseSelectedPackageForFlash()
    {
        if (packageList.SelectedItem is not PackageEntry p) { Warn("اختر حزمة من المكتبة أولاً."); return; }
        if (p.Manifest.IsSystem) { if (flashSystemBox != null) flashSystemBox.Text = p.Dir; }
        else flashApkBox.SelectedItem = flashApkBox.Items.Cast<object>().OfType<PackageEntry>().FirstOrDefault(e => e.Dir == p.Dir);
        selectPage?.Invoke(FullFlashPageIndex);
        Log($"حزمة الفلاش: {p.Manifest.Name} ({p.Manifest.Type})");
    }

    // ---------------- building ----------------

    private async Task CreatePackage(CancellationToken ct, string type, string name, string version, string platform, string description, List<string> components)
    {
        var safe = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_'));
        var dir = Path.Combine(Paths.Packages, safe);
        if (Directory.Exists(dir)) throw new InvalidOperationException($"توجد حزمة بنفس الاسم: {dir}");

        var manifest = new PackageManifest
        {
            Type = type,
            Name = name,
            PackageVersion = version.Length > 0 ? version : "1.0.0",
            Platform = platform,
            Description = description,
            Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Components = components,
            RequiresRoot = type == SystemPackageType,
            RequiresRemount = type == SystemPackageType,
        };

        var connected = (await ListDevices(ct)).Any(d => d.State == "device");
        if (connected)
        {
            var car = await ReadFirmware(ct);
            if (type == SystemPackageType) StampFirmware(manifest, car);
            else manifest.Android = car.Android.Split('.')[0];
        }
        else if (type == SystemPackageType)
            Log("تنبيه: لا توجد سيارة متصلة — الحزمة بدون بصمة، وسيرفضها المدقق حتى تضغط «تحديث بصمة الحزمة».");

        if (type == SystemPackageType)
            foreach (var sub in new[] { @"system\priv-app", @"system\app", @"system\fonts", @"system\etc", @"system\overlay" })
                Directory.CreateDirectory(Path.Combine(dir, sub));
        else
        {
            Directory.CreateDirectory(Path.Combine(dir, "apks"));
            File.WriteAllText(Path.Combine(dir, "install.json"), JsonSerializer.Serialize(new InstallPlan(), ManifestJson));
            File.WriteAllText(Path.Combine(dir, "permissions.json"), JsonSerializer.Serialize(new Dictionary<string, PermissionSpec>
            {
                ["com.example.app"] = new() { Grant = { "android.permission.ACCESS_FINE_LOCATION" }, Appops = { ["SYSTEM_ALERT_WINDOW"] = "allow" } },
            }, ManifestJson));
        }
        SaveManifest(dir, manifest);
        Log($"✔ أُنشئت الحزمة {type}: {dir}");
        ReloadPackages(dir);
        OpenFolder(dir);
    }

    private static void StampFirmware(PackageManifest m, FirmwareInfo car)
    {
        m.BuildId = car.BuildId;
        m.Incremental = car.Incremental;
        m.Fingerprint = car.Fingerprint;
        m.Model = car.Model;
        m.Android = car.Android;
    }

    private void Guarded(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Log($"✖ {ex.Message}");
            Warn(ex.Message);
        }
    }

    private PackageEntry SelectedPackageOfType(string type)
    {
        var p = SelectedPackage();
        if (p.Manifest.Type != type)
            throw new InvalidOperationException(type == SystemPackageType
                ? "هذه العملية لحزم النظام (SYSTEM_PACKAGE) فقط."
                : "هذه العملية لحزم التطبيقات (APK_PACKAGE) فقط.");
        return p;
    }

    private void AddFileToPackage(string file, string remote)
    {
        var p = SelectedPackageOfType(SystemPackageType);
        if (!File.Exists(file)) throw new InvalidOperationException("اختر ملفاً.");
        if (remote.EndsWith('/')) remote += Path.GetFileName(file);
        if (!DeployRoots.Any(remote.StartsWith) || remote.Contains(".."))
            throw new InvalidOperationException("المسار يجب أن يبدأ بـ /system/ أو /vendor/ أو /product/ أو /odm/.");
        if (CarSpecific.IsMatch(remote)) throw new InvalidOperationException($"مرفوض — بيانات خاصة بالسيارة: {remote}");
        var target = LocalMirror(p.Dir, remote);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target, overwrite: true);
        Log($"✔ أُضيف: {remote}");
        ShowPackageDetails();
    }

    private void CopyPickedFiles(string filter, string subdir, string type)
    {
        var p = SelectedPackageOfType(type);
        using var dlg = new OpenFileDialog { Filter = filter, Multiselect = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var dest = Path.Combine(p.Dir, subdir);
        Directory.CreateDirectory(dest);
        foreach (var f in dlg.FileNames)
        {
            File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), overwrite: true);
            Log($"✔ أُضيف: {subdir}\\{Path.GetFileName(f)}");
        }
        ShowPackageDetails();
    }

    private void CopyFromSnapshot(string snapshot, bool folders)
    {
        var p = SelectedPackageOfType(SystemPackageType);
        if (!Directory.Exists(snapshot)) throw new InvalidOperationException("اختر مجلد لقطة نظام (snapshot_...) من backups.");
        var root = Path.GetFullPath(snapshot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        List<string> picked;
        if (folders)
        {
            using var dlg = new FolderBrowserDialog { InitialDirectory = Path.Combine(snapshot, "system", "priv-app") };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            picked = new List<string> { dlg.SelectedPath };
        }
        else
        {
            using var dlg = new OpenFileDialog { InitialDirectory = snapshot, Multiselect = true, Filter = "All|*.*" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            picked = dlg.FileNames.ToList();
        }

        foreach (var source in picked)
        {
            var full = Path.GetFullPath(source);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("الملف ليس داخل مجلد اللقطة المختار.");
            var files = folders ? Directory.GetFiles(full, "*", SearchOption.AllDirectories) : new[] { full };
            foreach (var f in files)
            {
                var rel = Path.GetRelativePath(root, f);
                var remotePath = "/" + rel.Replace('\\', '/');
                if (!DeployRoots.Any(remotePath.StartsWith)) throw new InvalidOperationException($"مسار خارج النظام: {rel}");
                if (CarSpecific.IsMatch(remotePath)) { Log($"تخطي (خاص بالسيارة): {rel}"); continue; }
                if (rel.Split(Path.DirectorySeparatorChar).Contains("oat")) continue;
                var target = Path.Combine(p.Dir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(f, target, overwrite: true);
            }
            Log($"✔ نُسخ{(folders ? " المجلد (بدون oat)" : "")}: {full[root.Length..]}");
        }
        ShowPackageDetails();
    }

    private void ListPackage(PackageEntry p)
    {
        Log($"الحزمة: {p.Manifest.Name} v{p.Manifest.PackageVersion} ({p.Manifest.Type})");
        foreach (var f in Directory.GetFiles(p.Dir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(p.Dir, f);
            if (rel != PackageManifestFile) Log($"  {rel}  ({new FileInfo(f).Length / 1024} KB)");
        }
    }

    // ---------------- validation (static, on the PC) ----------------

    private static List<string> ValidatePackageFiles(string dir, PackageManifest? m)
    {
        var errors = new List<string>();
        if (m is null) { errors.Add($"لا يوجد {PackageManifestFile} — ليست حزمة Zinou."); return errors; }
        if (m.Type is not (SystemPackageType or ApkPackageType)) errors.Add($"نوع غير معروف: {m.Type}");

        foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(dir, f);
            if (rel == PackageManifestFile) continue;
            var unix = "/" + rel.Replace('\\', '/');
            var top = rel.Split(Path.DirectorySeparatorChar)[0];

            if (CarSpecific.IsMatch(unix)) errors.Add($"CAR_SPECIFIC ممنوع في الحزم: {rel}");
            if (m.IsSystem && !SystemPackageRoots.Contains(top, StringComparer.OrdinalIgnoreCase))
                errors.Add($"خارج بنية SYSTEM_PACKAGE (system/vendor/product/odm): {rel}");
            if (!m.IsSystem && !(top.Equals("apks", StringComparison.OrdinalIgnoreCase) || rel is "install.json" or "permissions.json"))
                errors.Add($"APK_PACKAGE لا يعدّل النظام — ملف غير مسموح: {rel}");
        }
        if (m.IsSystem && !m.HasFirmware) errors.Add("SYSTEM_PACKAGE بدون بصمة إصدار (buildId / fingerprint).");
        if (!m.IsSystem)
        {
            var plan = LoadJson<InstallPlan>(Path.Combine(dir, "install.json"));
            foreach (var item in plan?.Apps ?? new())
                if (!File.Exists(Path.Combine(dir, "apks", item.File))) errors.Add($"install.json يذكر ملفاً غير موجود: apks\\{item.File}");
            LoadJson<Dictionary<string, PermissionSpec>>(Path.Combine(dir, "permissions.json"));
        }
        return errors;
    }

    // ---------------- car properties ----------------

    private static readonly string[] FirmwareProps =
    {
        "ro.build.display.id", "ro.build.version.incremental", "ro.build.fingerprint", "ro.product.model", "ro.product.device",
        "ro.build.version.release", "ro.build.type", "ro.boot.veritymode", "ro.boot.verifiedbootstate", "ro.board.platform", "ro.serialno",
    };

    private async Task<FirmwareInfo> ReadFirmware(CancellationToken ct)
    {
        var r = await AdbQuiet(ct, "shell", string.Join("; ", FirmwareProps.Select(p => $"echo \"$(getprop {p})\"")));
        var v = r.Output.Replace("\r", "").Split('\n');
        string At(int i) => i < v.Length ? v[i].Trim() : "";
        return new FirmwareInfo
        {
            BuildId = At(0), Incremental = At(1), Fingerprint = At(2), Model = At(3), Device = At(4), Android = At(5),
            BuildType = At(6), Verity = At(7), BootState = At(8), Platform = At(9), SerialNo = At(10),
        };
    }

    private static Compatibility Compare(PackageManifest m, FirmwareInfo car)
    {
        if (car.IsEmpty) return Compatibility.Unknown;
        if (!m.IsSystem)
            return int.TryParse(m.Android.Split('.')[0], out var need) && int.TryParse(car.Android.Split('.')[0], out var have) && have < need
                ? Compatibility.Mismatch
                : Compatibility.Match;
        if (!m.HasFirmware) return Compatibility.Unknown;
        if (m.Fingerprint.Length > 0 && car.Fingerprint.Length > 0)
            return m.Fingerprint == car.Fingerprint ? Compatibility.Match : Compatibility.Mismatch;
        return m.BuildId == car.BuildId && m.Incremental == car.Incremental && (m.Model.Length == 0 || m.Model == car.Model)
            ? Compatibility.Match
            : Compatibility.Mismatch;
    }

    /// <summary>Picks the newest SYSTEM_PACKAGE whose fingerprint matches the car.</summary>
    private static PackageEntry? FindSystemPackage(FirmwareInfo car) =>
        LoadPackages()
            .Where(p => p.Manifest.IsSystem && Compare(p.Manifest, car) == Compatibility.Match)
            .OrderByDescending(p => Version.TryParse(p.Manifest.PackageVersion, out var v) ? v : new Version(0, 0))
            .ThenByDescending(p => p.Manifest.Created, StringComparer.Ordinal)
            .FirstOrDefault();
}
