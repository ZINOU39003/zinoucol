namespace GeelyOpenTool;

public sealed partial class MainForm
{
    private const string DeviceSpecificDefault = "nvram,nvdata,nvcfg,proinfo,seccfg,protect1,protect2,persist";

    private Control BackupPage()
    {
        var partitions = Txt(520, DeviceSpecificDefault);

        return Page(
            Group("النسخ الاحتياطي منفصل تماماً عن الفلاش",
                Row(Lbl("BACKUP = بيانات هذه السيارة وحدها (هوية، معايرة، مفاتيح، ملفات النظام الأصلية). لا تُنشر أبداً على سيارة أخرى.")),
                Row(Lbl("DEPLOYMENT PACKAGE = تطبيقات مفتوحة + تعديلاتك + خطوط + Overlays + إعدادات — تُبنى في «صانع ومكتبة الحزم».")),
                Row(Lbl("كل نسخة تُحفظ في backups\\car_<الرقم التسلسلي>\\<التاريخ>_<النوع>."))),
            Group("1) معلومات الاسترداد (recovery information) — adb، بدون كتابة",
                Row(Btn("حفظ معلومات الاسترداد", () => Run("معلومات الاسترداد", async ct => await SaveRecoveryInfo(ct, "recovery-info"))),
                    Plain("فتح مجلد النسخ", () => OpenFolder(Paths.Backups))),
                Row(Lbl("getprop، قائمة الحزم ومساراتها، الأقسام المركّبة، المساحة، جدول by-name، build.prop."))),
            Group("2) لقطة النظام (system snapshot)",
                Row(Btn("أخذ لقطة النظام (priv-app, app, etc, fonts, vendor/etc, build.prop)", () => Run("لقطة النظام", SystemSnapshot))),
                Row(Lbl("الملفات الأصلية لهذا الإصدار — مصدر بناء حزمة SYSTEM_PACKAGE ونقطة الرجوع."))),
            Group("3) بيانات خاصة بالسيارة (device-specific) — MTK BROM",
                Row(Lbl("الأقسام:"), partitions, Plain("عرض جدول الأقسام (GPT)", () => Run("printgpt", ct => Mtk(ct, "printgpt")))),
                Row(Btn("نسخ الأقسام الخاصة بالسيارة", () => Run("نسخ بيانات السيارة", ct => DeviceSpecificBackup(ct, partitions.Text)))),
                Row(Lbl("اعرض جدول الأقسام أولاً وعدّل الأسماء حسب وحدتك. هذه الملفات للاسترجاع على نفس السيارة فقط."))),
            Group("4) نسخة أرشيفية كاملة (Full Dump)",
                Row(Plain("الذهاب إلى صفحة MTK", () => selectPage?.Invoke(MtkPageIndex))),
                Row(Lbl("نسخة كاملة لكل الأقسام للأرشيف. ليست جزءاً من الفلاش ولا تُكتب على سيارة أخرى."))));
    }

    private async Task<string> CarBackupDir(CancellationToken ct, string kind)
    {
        var id = (await AdbQuiet(ct, "shell", "getprop ro.serialno")).Output.Trim();
        if (id.Length == 0) id = await TargetSerial(ct);
        var safe = string.Concat(id.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        var dir = Path.Combine(Paths.Backups, $"car_{safe}", $"{DateTime.Now:yyyyMMdd_HHmmss}_{kind}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private async Task<string> SaveRecoveryInfo(CancellationToken ct, string kind)
    {
        if ((await TargetSerial(ct)).Length == 0) throw new InvalidOperationException("لا توجد سيارة متصلة.");
        var dir = await CarBackupDir(ct, kind);
        Log($"الحفظ في: {dir}");
        var items = new (string File, string Command)[]
        {
            ("getprop.txt", "getprop"),
            ("packages.txt", "pm list packages -f"),
            ("packages_disabled.txt", "pm list packages -d"),
            ("mounts.txt", "cat /proc/mounts"),
            ("df.txt", "df -k"),
            ("partitions_by-name.txt", "ls -l /dev/block/by-name /dev/block/platform/*/by-name /dev/block/platform/*/*/by-name 2>/dev/null"),
            ("system_build.prop", "cat /system/build.prop"),
            ("vendor_build.prop", "cat /vendor/build.prop"),
            ("settings_global.txt", "settings list global"),
            ("settings_system.txt", "settings list system"),
        };
        for (var i = 0; i < items.Length; i++)
        {
            var (file, command) = items[i];
            Log($"[{i + 1}/{items.Length}] {file}");
            File.WriteAllText(Path.Combine(dir, file), (await AdbQuiet(ct, "shell", command)).Output);
        }
        Log("✔ معلومات الاسترداد محفوظة.");
        return dir;
    }

    private async Task DeviceSpecificBackup(CancellationToken ct, string list)
    {
        var names = list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => n.All(c => char.IsLetterOrDigit(c) || c is '_' or '-')).ToList();
        if (names.Count == 0) throw new InvalidOperationException("اكتب أسماء الأقسام مفصولة بفواصل.");
        var dir = Path.Combine(Paths.Backups, "car_mtk", $"{DateTime.Now:yyyyMMdd_HHmmss}_device-specific");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "README.txt"),
            "CAR_SPECIFIC — بيانات هوية ومعايرة هذه السيارة فقط.\nلا تكتبها أبداً على سيارة أخرى ولا تضعها في أي حزمة.\n");
        Log($"الحفظ في: {dir}");
        await Mtk(ct, "r", string.Join(",", names), string.Join(",", names.Select(n => Path.Combine(dir, n + ".bin"))));
        Log("✔ اكتمل نسخ البيانات الخاصة بالسيارة.");
    }
}
