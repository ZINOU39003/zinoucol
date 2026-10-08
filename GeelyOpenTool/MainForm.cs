using System.ComponentModel;
using System.IO.Compression;

namespace GeelyOpenTool;

public sealed partial class MainForm : Form
{
    private const string PlatformToolsUrl = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";
    private const string MtkClientRepo = "https://github.com/bkerler/mtkclient";
    private const string MoreLocalePkg = "jp.co.c_lis.ccl.morelocale";

    private readonly RichTextBox log = new()
    {
        ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
        Font = Theme.Mono, RightToLeft = RightToLeft.No, BackColor = Theme.Terminal,
        ForeColor = Color.FromArgb(232, 222, 200), BorderStyle = BorderStyle.None, DetectUrls = false,
    };
    private readonly List<Button> actionButtons = new();
    private readonly Button cancelButton = new NeonButton(ButtonKind.Danger) { Text = "إيقاف العملية", Enabled = false };
    private readonly Label status = new() { AutoSize = true, Margin = new Padding(12, 12, 8, 3), Tag = "status", Font = Theme.Bold, ForeColor = Theme.Success, BackColor = Color.Transparent };
    private static readonly Image? backdrop = Theme.Image("background.jpg");
    private readonly HeaderBar header = new(Theme.Image("logo.png"), backdrop);
    private readonly ProgressRing progressRing = new();
    private readonly ProgressLine progressLine = new();
    private string runTitle = "", stageLabel = "";
    private int stageIndex, stageCount = 1;
    private double stageFraction = -1;
    private static readonly System.Text.RegularExpressions.Regex ItemCounter = new(@"^\[(\d+)/(\d+)\]");
    private static readonly System.Text.RegularExpressions.Regex PercentText = new(@"(\d{1,3}(?:\.\d+)?)\s?%");
    private Action<int>? selectPage;
    private readonly System.Windows.Forms.Timer devicePoll = new() { Interval = 3000 };
    private bool polling;
    private readonly ComboBox serialBox = new() { Width = 320, DropDownStyle = ComboBoxStyle.DropDown, RightToLeft = RightToLeft.No };
    private static readonly HashSet<string> HostCommands = new() { "devices", "connect", "disconnect", "pair", "mdns", "kill-server", "start-server", "reconnect", "version" };
    private readonly TextBox pythonBox = Txt(220, "python");
    private CancellationTokenSource? cts;

    public MainForm()
    {
        Text = "Zinou Coolray — Flash & Customization Suite";
        Icon = Theme.AppIcon() ?? Icon;
        RightToLeft = RightToLeft.Yes;
        Font = Theme.Body;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1440, 900);
        MinimumSize = new Size(Math.Min(1100, screen.Width), Math.Min(680, screen.Height));
        Size = new Size(Math.Min(1420, screen.Width - 20), Math.Min(900, screen.Height - 20));
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;

        var pages = new (string Glyph, string Title, Control Page)[]
        {
            ("\uE897", "طريقة العمل خطوة بخطوة", GuidePage()),
            ("\uE703", "الجهاز والاتصال", DevicePage()),
            ("\uE8F1", "النسخ الاحتياطي", BackupPage()),
            ("\uE7B8", "صانع ومكتبة الحزم", PackagesPage()),
            ("\uE945", "الفلاش (S0 → S4)", FullFlashPage()),
            ("\uE90F", "نشر النظام (root)", SystemPage()),
            ("\uE950", "تفليش MTK (BROM)", FlashPage()),
            ("\uE71D", "التطبيقات", AppsPage()),
            ("\uE774", "التعريب و Overlay", LanguagePage()),
            ("\uE756", "الطرفية", ConsolePage()),
        };

        var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        var sidebar = new Panel { Dock = DockStyle.Right, Width = 250, BackColor = Theme.Sidebar, Padding = new Padding(0, 14, 0, 0) };
        var navButtons = new List<NavButton>();
        foreach (var (glyph, title, page) in pages)
        {
            page.Visible = false;
            host.Controls.Add(page);
            var nav = new NavButton(glyph, title);
            nav.Click += (_, _) =>
            {
                foreach (var n in navButtons) n.Selected = n == nav;
                foreach (Control p in host.Controls) p.Visible = p == page;
            };
            navButtons.Add(nav);
        }
        for (var i = navButtons.Count - 1; i >= 0; i--) sidebar.Controls.Add(navButtons[i]);
        var footer = new Label
        {
            Dock = DockStyle.Bottom, Height = 54, Tag = "footer", ForeColor = Theme.Muted, BackColor = Theme.Sidebar,
            Text = $"{Theme.AppName}  {Theme.Version}\nADB • MTKClient • RRO", TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 8.5f),
        };
        sidebar.Controls.Add(footer);
        navButtons[0].Selected = true;
        pages[0].Page.Visible = true;
        selectPage = i =>
        {
            foreach (var n in navButtons) n.Selected = n == navButtons[i];
            foreach (Control p in host.Controls) p.Visible = p == pages[i].Page;
        };

        var terminalBar = new SmoothFlow { Dock = DockStyle.Top, Height = 50, BackColor = Color.FromArgb(20, 18, 26), Padding = new Padding(8, 6, 8, 0), WrapContents = false };
        var termTitle = new Label
        {
            Text = "●  ●  ●     ZINOU TERMINAL — ADB", AutoSize = true, Tag = "term", Font = Theme.Bold,
            ForeColor = Theme.Gold, BackColor = Color.Transparent, Margin = new Padding(12, 12, 12, 3), RightToLeft = RightToLeft.No,
        };
        terminalBar.Controls.AddRange(new Control[] { termTitle, progressRing, status, cancelButton, Plain("مسح السجل", () => log.Clear()), Plain("حفظ السجل", SaveLog) });
        cancelButton.Click += (_, _) => cts?.Cancel();

        var terminal = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Terminal, Padding = new Padding(10, 6, 10, 6) };
        terminal.Controls.Add(log);
        var terminalFrame = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Terminal };
        terminalFrame.Controls.Add(terminal);
        terminalFrame.Controls.Add(progressLine);
        terminalFrame.Controls.Add(terminalBar);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Color.FromArgb(120, 78, 30),
            SplitterWidth = 3, FixedPanel = FixedPanel.Panel2,
        };
        split.Panel1.BackColor = Theme.Bg;
        split.Panel1.Controls.Add(host);
        split.Panel2.Controls.Add(terminalFrame);

        Controls.Add(split);
        Controls.Add(sidebar);
        Controls.Add(header);

        Theme.Apply(this);

        devicePoll.Tick += async (_, _) => await PollDevice();
        Shown += async (_, _) =>
        {
            split.SplitterDistance = Math.Max(300, ClientSize.Height - header.Height - 220);
            Log($"{Theme.AppName} {Theme.Version} — جاهز");
            Log($"المجلد: {Paths.Base}");
            Log($"adb: {Paths.Adb}");
            Log("تنبيه: خذ نسخة احتياطية قبل أي كتابة على النظام أو الأقسام.");
            SetBusy(false);
            await PollDevice();
            devicePoll.Start();
        };
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var dark = 1;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
    }

    private async Task PollDevice()
    {
        if (polling || cts != null) return;
        polling = true;
        try
        {
            var devices = await ListDevices(CancellationToken.None);
            var online = devices.Where(d => d.State == "device").ToList();
            if (online.Count > 0)
            {
                var name = online.Count == 1 ? online[0].Serial : $"{online.Count} أجهزة";
                if (name.Length > 28) name = name[..26] + "…";
                header.SetDevice($"متصل — {name}", Theme.Success);
            }
            else if (devices.Count > 0)
                header.SetDevice($"{devices[0].State} — {devices[0].Serial}", Theme.Warning);
            else
                header.SetDevice("غير متصل", Theme.Danger);
        }
        catch (Win32Exception)
        {
            header.SetDevice("adb غير مثبت — نزّله من تبويب الجهاز", Theme.Warning);
        }
        catch
        {
        }
        finally
        {
            polling = false;
        }
    }

    // ---------------- Pages ----------------

    private Control DevicePage()
    {
        var ip = Txt(180, "192.168.1.100:5555");
        var pairAddr = Txt(180);
        var pairCode = Txt(90);
        return Page(
            Group("الأدوات",
                Row(Btn("تنزيل/تحديث platform-tools (adb) من Google", () => Run("تنزيل platform-tools", DownloadPlatformTools))),
                Row(Lbl("مسار adb المستعمل:"), Lbl(Paths.Adb))),
            Group("الاتصال",
                Row(Lbl("الجهاز المستهدف (يُختار تلقائياً إن تُرك فارغاً):"), serialBox,
                    Btn("تحديث قائمة الأجهزة", () => Run("adb devices", async ct =>
                    {
                        await Adb(ct, false, "devices", "-l");
                        await RefreshDevices(ct);
                    }))),
                Row(Lbl("IP:Port للاتصال اللاسلكي:"), ip,
                    Btn("اتصال", () => Run("adb connect", ct => Connect(ct, Need(ip, "IP")))),
                    Btn("قطع", () => Run("adb disconnect", ct => Adb(ct, false, "disconnect", Need(ip, "IP"))))),
                Row(Lbl("إقران Android 11+ — IP:Port الإقران:"), pairAddr, Lbl("الرمز:"), pairCode,
                    Btn("إقران", () => Run("adb pair", async ct =>
                    {
                        var r = await Adb(ct, false, "pair", Need(pairAddr, "عنوان الإقران"), Need(pairCode, "رمز الإقران"));
                        if (!r.Output.Contains("Successfully paired", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("فشل الإقران — تأكد أن نافذة «إقران الجهاز برمز» ما زالت مفتوحة في الهاتف وأن العنوان والرمز منها.");
                        Log("تم الإقران. الآن اكتب IP:Port الظاهر في الشاشة الرئيسية لـ «تصحيح الأخطاء اللاسلكي» (ليس منفذ الإقران) واضغط اتصال.");
                    }))),
                Row(Lbl("الهاتف: خيارات المطور ← تصحيح الأخطاء اللاسلكي ← إقران الجهاز برمز إقران. منفذ الإقران يختلف عن منفذ الاتصال ويتغيران كل مرة.")),
                Row(Btn("بحث تلقائي عن الأجهزة (mDNS)", () => Run("adb mdns", ct => Adb(ct, false, "mdns", "services"))),
                    Btn("تفعيل اللاسلكي على المنفذ 5555 (الجهاز موصول بـ USB)", () => Run("adb tcpip", async ct =>
                    {
                        await Adb(ct, true, "tcpip", "5555");
                        var r = await AdbQuiet(ct, "shell", "ip -f inet addr show wlan0");
                        var m = System.Text.RegularExpressions.Regex.Match(r.Output, @"inet (\d+\.\d+\.\d+\.\d+)");
                        if (m.Success) { ip.Text = $"{m.Groups[1].Value}:5555"; Log($"عنوان الجهاز: {ip.Text} — افصل الكابل واضغط اتصال."); }
                    })),
                    Btn("إعادة تشغيل خادم adb", () => Run("restart adb server", async ct =>
                    {
                        await Adb(ct, false, "kill-server");
                        await Adb(ct, false, "start-server");
                        await Adb(ct, false, "reconnect", "offline");
                        await Adb(ct, false, "devices", "-l");
                    })))),
            Group("معلومات وإعادة تشغيل",
                Row(Btn("معلومات الوحدة", () => Run("معلومات الوحدة", DeviceInfo)),
                    Btn("لقطة شاشة", () => Run("لقطة شاشة", Screenshot)),
                    Btn("إعادة تشغيل", () => Run("reboot", ct => Adb(ct, true, "reboot"))),
                    Btn("Recovery", () => Run("reboot recovery", ct => Adb(ct, true, "reboot", "recovery"))))));
    }

    private Control FlashPage()
    {
        var skipUserdata = new CheckBox { Text = "تخطي userdata (أسرع وأصغر)", Checked = true, AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
        var part = Txt(160, "boot");
        var partFile = Txt(420);
        var writeDir = Txt(420);

        return Page(
            Group("تثبيت mtkclient (bkerler/mtkclient)",
                Row(Lbl("مفسّر Python:"), pythonBox,
                    Btn("تثبيت/تحديث mtkclient", () => Run("تثبيت mtkclient", SetupMtkClient)),
                    Plain("فتح واجهة mtkclient الرسومية", OpenMtkGui)),
                Row(Lbl("على Windows يلزم تعريف UsbDk: https://github.com/daynix/UsbDk/releases"))),
            Group("قراءة (آمنة) — ضع الوحدة في وضع BROM عند الطلب",
                Row(Btn("عرض جدول الأقسام (GPT)", () => Run("printgpt", ct => Mtk(ct, "printgpt"))),
                    Btn("نسخة كاملة لكل الأقسام (Full Dump)", () => Run("Full Dump", ct =>
                    {
                        var dir = Path.Combine(Paths.Backups, $"dump_{DateTime.Now:yyyyMMdd_HHmmss}");
                        Directory.CreateDirectory(dir);
                        Log($"الحفظ في: {dir}");
                        return skipUserdata.Checked
                            ? Mtk(ct, "rl", dir, "--skip", "userdata")
                            : Mtk(ct, "rl", dir);
                    })), skipUserdata),
                Row(Lbl("قسم:"), part,
                    Btn("قراءة القسم إلى ملف", () => Run("قراءة قسم", ct =>
                    {
                        var name = Need(part, "اسم القسم");
                        Directory.CreateDirectory(Paths.Backups);
                        var file = Path.Combine(Paths.Backups, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.img");
                        Log($"الحفظ في: {file}");
                        return Mtk(ct, "r", name, file);
                    })),
                    Plain("فتح مجلد النسخ", () => OpenFolder(Paths.Backups)))),
            Group("كتابة (خطيرة) — تأكيد مزدوج",
                Row(Lbl("قسم:"), Lbl("(نفس الحقل أعلاه)"), Lbl("ملف الصورة:"), partFile,
                    Plain("...", () => PickFile(partFile, "Images|*.img;*.bin|All|*.*")),
                    Btn("كتابة القسم", () =>
                    {
                        var name = part.Text.Trim();
                        var file = partFile.Text.Trim();
                        if (name.Length == 0 || !File.Exists(file)) { Warn("حدد اسم القسم وملفاً موجوداً."); return; }
                        if (!ConfirmDanger($"سيتم كتابة الملف:\n{file}\nعلى القسم: {name}")) return;
                        Run($"كتابة {name}", ct => Mtk(ct, "w", name, file));
                    })),
                Row(Lbl("مجلد صور (أسماء الملفات = أسماء الأقسام):"), writeDir,
                    Plain("...", () => PickFolder(writeDir)),
                    Btn("تفليش كامل من مجلد (wl)", () =>
                    {
                        var dir = writeDir.Text.Trim();
                        if (!Directory.Exists(dir)) { Warn("حدد مجلداً موجوداً."); return; }
                        var files = Directory.GetFiles(dir).Select(Path.GetFileName);
                        if (!ConfirmDanger($"سيتم تفليش كل الأقسام الموجودة في:\n{dir}\n\n{string.Join(", ", files)}")) return;
                        Run("تفليش كامل", ct => Mtk(ct, "wl", dir));
                    })),
                Row(Btn("إعادة تشغيل عبر mtkclient", () => Run("mtk reset", ct => Mtk(ct, "reset"))))));
    }

    private Control AppsPage()
    {
        var apk = Txt(480);
        var folder = Txt(480);
        var pkg = Txt(320);
        var home = Txt(420, "com.example.launcher/.MainActivity");

        return Page(
            Group("تثبيت APK واحد (لاونشر، صوت Kiki، ...)",
                Row(apk, Plain("...", () => PickFile(apk, "APK|*.apk")),
                    Btn("تثبيت", () => Run("تثبيت APK", async ct =>
                    {
                        var f = Need(apk, "ملف APK");
                        await InstallApk(ct, f);
                    })))),
            Group("تثبيت دفعي لمجلد APK (بالترتيب الأبجدي: 01_..., 02_...)",
                Row(folder, Plain("...", () => PickFolder(folder)),
                    Btn("تثبيت الكل", () => Run("تثبيت دفعي", ct => BatchInstall(ct, Need(folder, "المجلد")))))),
            Group("إدارة الحزم",
                Row(Lbl("الحزمة:"), pkg,
                    Btn("تعطيل", () => Run("pm disable-user", ct => Shell(ct, $"pm disable-user --user 0 {Need(pkg, "الحزمة")}"))),
                    Btn("تفعيل", () => Run("pm enable", ct => Shell(ct, $"pm enable {Need(pkg, "الحزمة")}"))),
                    Btn("إلغاء التثبيت", () =>
                    {
                        if (!Confirm($"إلغاء تثبيت {pkg.Text}؟")) return;
                        Run("uninstall", ct => Adb(ct, true, "uninstall", Need(pkg, "الحزمة")));
                    })),
                Row(Btn("التطبيقات المثبتة من المستخدم", () => Run("pm list -3", ct => Shell(ct, "pm list packages -3"))),
                    Btn("كل الحزم", () => Run("pm list", ct => Shell(ct, "pm list packages"))),
                    Btn("الحزم المعطلة", () => Run("pm list -d", ct => Shell(ct, "pm list packages -d"))))),
            Group("S2 اللاونشر الافتراضي",
                Row(Lbl("package/activity:"), home,
                    Btn("تعيين كلاونشر افتراضي", () => Run("set-home-activity", ct => Shell(ct, $"cmd package set-home-activity {Need(home, "النشاط")}"))),
                    Btn("عرض نشاط HOME الحالي", () => Run("resolve home", ct =>
                        Shell(ct, "cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.HOME"))))));
    }

    private Control LanguagePage()
    {
        var locale = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDown, RightToLeft = RightToLeft.No };
        locale.Items.AddRange(new object[] { "ar-DZ", "ar-SA", "ar-EG", "ar", "fr-FR", "en-US" });
        locale.SelectedIndex = 0;
        var overlay = Txt(360);

        return Page(
            Group("فحص دعم العربية",
                Row(Btn("اللغة الحالية", () => Run("اللغة الحالية", async ct =>
                    {
                        await Shell(ct, "getprop persist.sys.locale; getprop ro.product.locale; settings get system system_locales");
                    })),
                    Btn("فحص خطوط العربية في /system/fonts", () => Run("فحص الخطوط", CheckArabicFonts)))),
            Group("المستوى 1: لغة النظام (MoreLocale2 أو root)",
                Row(Lbl("اللغة:"), locale,
                    Btn("منح صلاحية MoreLocale2", () => Run("grant MoreLocale", ct =>
                        Shell(ct, $"pm grant {MoreLocalePkg} android.permission.CHANGE_CONFIGURATION"))),
                    Btn("تعيين اللغة عبر root", () =>
                    {
                        var loc = locale.Text.Trim();
                        if (loc.Length == 0) return;
                        Run("setprop locale", async ct =>
                        {
                            var root = await AdbQuiet(ct, "root");
                            if (root.Output.Contains("cannot run as root", StringComparison.OrdinalIgnoreCase))
                            {
                                await Shell(ct, $"su -c 'setprop persist.sys.locale {loc}'");
                            }
                            else
                            {
                                await Adb(ct, false, "wait-for-device");
                                await Shell(ct, $"setprop persist.sys.locale {loc}");
                            }
                            Log("أعد تشغيل الوحدة لتطبيق اللغة.");
                        });
                    })),
                Row(Lbl("بدون root: ثبّت MoreLocale2 من تبويب التطبيقات، امنحه الصلاحية، ثم اختر اللغة من داخله."))),
            Group("المستوى 2: RRO Overlay (ترجمة واجهة Geely / PiP)",
                Row(Btn("عرض كل الـ Overlays", () => Run("overlay list", ct => Shell(ct, "cmd overlay list")))),
                Row(Lbl("حزمة الـ Overlay:"), overlay,
                    Btn("تفعيل", () => Run("overlay enable", ct => Shell(ct, $"cmd overlay enable {Need(overlay, "الحزمة")}"))),
                    Btn("تعطيل", () => Run("overlay disable", ct => Shell(ct, $"cmd overlay disable {Need(overlay, "الحزمة")}")))),
                Row(Lbl("ثبّت ملف Overlay APK من تبويب التطبيقات ثم فعّله هنا."))),
            OverlayBuilderGroup());
    }

    private Control OverlayBuilderGroup()
    {
        var target = Txt(320);
        var allLocales = new CheckBox { Text = "لكل اللغات (values بدل values-ar)", AutoSize = true, Margin = new Padding(3, 7, 3, 3) };

        return Group("صانع Overlay عربي (يتطلب Android SDK و JDK 11+ على هذا الحاسوب)",
            Row(Btn("عرض حزم Geely / ECARX", () => Run("حزم Geely", ct =>
                Shell(ct, "pm list packages | grep -iE 'geely|ecarx|flyme|lynk' || true")))),
            Row(Lbl("التطبيق المستهدف:"), target,
                Btn("1) سحب التطبيق واستخراج النصوص", () => Run("استخراج النصوص", ct =>
                    OverlayScript(ct, "extract-strings.ps1", "-Package", Need(target, "التطبيق المستهدف")))),
                Plain("2) فتح ملف الترجمة", () =>
                {
                    var file = Path.Combine(OverlayDir, "targets", target.Text.Trim(), "strings.xml");
                    if (!File.Exists(file)) { Warn("استخرج النصوص أولاً."); return; }
                    System.Diagnostics.Process.Start("notepad.exe", $"\"{file}\"");
                })),
            Row(allLocales,
                Btn("3) بناء + تثبيت + تفعيل", () => Run("بناء Overlay", ct =>
                {
                    var args = new List<string> { "-Package", Need(target, "التطبيق المستهدف"), "-Install" };
                    if (allLocales.Checked) args.Add("-AllLocales");
                    return OverlayScript(ct, "build-overlay.ps1", args.ToArray());
                })),
                Btn("بناء فقط", () => Run("بناء Overlay", ct =>
                {
                    var args = new List<string> { "-Package", Need(target, "التطبيق المستهدف") };
                    if (allLocales.Checked) args.Add("-AllLocales");
                    return OverlayScript(ct, "build-overlay.ps1", args.ToArray());
                })),
                Plain("فتح مجلد الـ Overlays", () => OpenFolder(Path.Combine(OverlayDir, "out")))),
            Row(Lbl("املأ النصوص الفارغة بالعربية فقط؛ الفارغة تُتجاهل. يظهر التعريب بعد ضبط لغة النظام على العربية.")));
    }

    private static string OverlayDir => Path.Combine(Paths.Base, "overlay-ar");

    private async Task<(int Code, string Output)> OverlayScript(CancellationToken ct, string script, params string[] args)
    {
        var path = Path.Combine(OverlayDir, script);
        if (!File.Exists(path)) throw new FileNotFoundException("سكربت الـ Overlay غير موجود", path);
        var full = new List<string> { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", path };
        full.AddRange(args);
        var serial = await TargetSerial(ct);
        if (serial.Length > 0) full.AddRange(new[] { "-Serial", serial });
        var r = await Proc.RunAsync("powershell.exe", full, OverlayDir, Log, ct);
        Check(r, script);
        return r;
    }

    private Control ConsolePage()
    {
        var cmd = Txt(620, "getprop ro.build.display.id");
        var remote = Txt(380, "/sdcard/");
        var local = Txt(380);
        Action runCmd = () => Run($"shell: {cmd.Text}", ct => Shell(ct, Need(cmd, "الأمر")));
        cmd.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter || cts != null) return;
            e.SuppressKeyPress = true;
            runCmd();
        };

        return Page(
            Group("adb shell (Enter للتنفيذ)",
                Row(cmd, Btn("تنفيذ", runCmd))),
            Group("نقل الملفات (مثلاً ملفات إعداد ecarx قبل/بعد الباتش)",
                Row(Lbl("المسار في الوحدة:"), remote),
                Row(Lbl("المسار على الحاسوب:"), local,
                    Plain("ملف...", () => PickFile(local, "All|*.*")),
                    Plain("مجلد...", () => PickFolder(local))),
                Row(Btn("سحب من الوحدة (pull)", () => Run("adb pull", ct =>
                    {
                        var dst = local.Text.Trim().Length > 0 ? local.Text.Trim() : Paths.Backups;
                        Directory.CreateDirectory(Directory.Exists(dst) || !Path.HasExtension(dst) ? dst : Path.GetDirectoryName(dst)!);
                        return Adb(ct, true, "pull", Need(remote, "المسار في الوحدة"), dst);
                    })),
                    Btn("دفع إلى الوحدة (push)", () => Run("adb push", ct =>
                        Adb(ct, true, "push", Need(local, "المسار على الحاسوب"), Need(remote, "المسار في الوحدة")))))));
    }

    // ---------------- Actions ----------------

    private async Task DownloadPlatformTools(CancellationToken ct)
    {
        var zip = Path.Combine(Path.GetTempPath(), "platform-tools-latest-windows.zip");
        Log($"تنزيل {PlatformToolsUrl}");
        Stage(1, 2, "تنزيل");
        using (var http = new HttpClient())
        using (var response = await http.GetAsync(PlatformToolsUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var s = await response.Content.ReadAsStreamAsync(ct);
            await using var f = File.Create(zip);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await s.ReadAsync(buffer, ct)) > 0)
            {
                await f.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) Fraction((double)done / total);
            }
        }
        Stage(2, 2, "تثبيت");

        var existing = Path.Combine(Paths.PlatformTools, "adb.exe");
        if (File.Exists(existing))
        {
            try { await Proc.RunAsync(existing, new[] { "kill-server" }, null, Log, ct); } catch (Win32Exception) { }
            Directory.Delete(Paths.PlatformTools, true);
        }
        Directory.CreateDirectory(Paths.ToolsDir);
        ZipFile.ExtractToDirectory(zip, Paths.ToolsDir);
        File.Delete(zip);
        Log($"تم التثبيت في {Paths.PlatformTools}");
        await Adb(ct, false, "version");
    }

    private async Task Connect(CancellationToken ct, string address)
    {
        var r = await Adb(ct, false, "connect", address);
        var output = r.Output.Trim();
        if (!output.Contains("connected to", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("cannot", StringComparison.OrdinalIgnoreCase))
        {
            Log("أسباب شائعة: هاتف Android 11+ يحتاج «إقران» أولاً؛ أو المنفذ تغيّر؛ أو الحاسوب والجهاز ليسا على نفس شبكة Wi-Fi؛ أو جدار الحماية.");
            throw new InvalidOperationException($"فشل الاتصال بـ {address}");
        }
        var state = (await Proc.RunAsync(Paths.Adb, new[] { "-s", address, "get-state" }, null, null, ct)).Output.Trim();
        Log($"الحالة: {state}");
        if (state == "device")
        {
            serialBox.Text = address;
            await RefreshDevices(ct);
        }
        else
            Log("الجهاز offline/unauthorized: اقبل نافذة «السماح بتصحيح الأخطاء» على الشاشة، أو اضغط «إعادة تشغيل خادم adb».");
    }

    private async Task DeviceInfo(CancellationToken ct)
    {
        await Shell(ct,
            "echo model=$(getprop ro.product.model); echo device=$(getprop ro.product.device); " +
            "echo build=$(getprop ro.build.display.id); echo android=$(getprop ro.build.version.release); " +
            "echo sdk=$(getprop ro.build.version.sdk); echo platform=$(getprop ro.board.platform); " +
            "echo hardware=$(getprop ro.hardware); echo locale=$(getprop persist.sys.locale)");
    }

    private async Task SetupMtkClient(CancellationToken ct)
    {
        Directory.CreateDirectory(Paths.ToolsDir);
        if (Directory.Exists(Path.Combine(Paths.MtkDir, ".git")))
            Check(await Proc.RunAsync("git", new[] { "-C", Paths.MtkDir, "pull", "--ff-only" }, null, Log, ct), "git pull");
        else
            Check(await Proc.RunAsync("git", new[] { "clone", "--depth", "1", MtkClientRepo, Paths.MtkDir }, null, Log, ct), "git clone");

        var venvPython = Path.Combine(Paths.MtkDir, ".venv", "Scripts", "python.exe");
        if (!File.Exists(venvPython))
            Check(await Proc.RunAsync(pythonBox.Text.Trim(), new[] { "-m", "venv", ".venv" }, Paths.MtkDir, Log, ct), "python -m venv");

        Check(await Proc.RunAsync(venvPython, new[] { "-m", "pip", "install", "--upgrade", "pip" }, Paths.MtkDir, Log, ct), "pip upgrade");
        Check(await Proc.RunAsync(venvPython, new[] { "-m", "pip", "install", "-r", "requirements.txt" }, Paths.MtkDir, Log, ct), "pip install");
        Log("mtkclient جاهز. لا تنسَ تعريف UsbDk على Windows.");
    }

    private void OpenMtkGui()
    {
        var gui = Path.Combine(Paths.MtkDir, "mtk_gui.py");
        if (!File.Exists(gui)) { Warn("mtkclient غير مثبت بعد."); return; }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Paths.MtkPython(pythonBox.Text.Trim()), "mtk_gui.py")
            {
                WorkingDirectory = Paths.MtkDir,
                UseShellExecute = false,
            });
        }
        catch (Exception ex) { Warn(ex.Message); }
    }

    private async Task<(int Code, string Output)> Mtk(CancellationToken ct, params string[] args)
    {
        if (!File.Exists(Path.Combine(Paths.MtkDir, "mtk.py")))
            throw new InvalidOperationException("mtkclient غير مثبت — اضغط «تثبيت/تحديث mtkclient» أولاً.");
        Log("بانتظار الوحدة في وضع BROM عبر USB...");
        var r = await Proc.RunAsync(Paths.MtkPython(pythonBox.Text.Trim()), new[] { "mtk.py" }.Concat(args), Paths.MtkDir, Log, ct);
        Check(r, "mtkclient");
        return r;
    }

    private async Task InstallApk(CancellationToken ct, string file)
    {
        if (!File.Exists(file)) throw new FileNotFoundException("الملف غير موجود", file);
        var r = await Adb(ct, false, "install", "-r", "-g", file);
        if (r.Code != 0 || r.Output.Contains("Failure", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"فشل تثبيت {Path.GetFileName(file)}");
    }

    private async Task BatchInstall(CancellationToken ct, string folder)
    {
        var files = Directory.GetFiles(folder, "*.apk").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0) throw new InvalidOperationException("لا توجد ملفات APK في المجلد.");
        var failed = new List<string>();
        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            Log($"[{i + 1}/{files.Count}] {Path.GetFileName(files[i])}");
            try { await InstallApk(ct, files[i]); }
            catch (InvalidOperationException ex) { Log(ex.Message); failed.Add(Path.GetFileName(files[i])); }
        }
        Log($"نجح {files.Count - failed.Count} من {files.Count}.");
        if (failed.Count > 0) throw new InvalidOperationException("فشل: " + string.Join(", ", failed));
    }

    private async Task CheckArabicFonts(CancellationToken ct)
    {
        var r = await Adb(ct, false, "shell", "ls /system/fonts");
        var arabic = r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Contains("Arab", StringComparison.OrdinalIgnoreCase)
                        || l.Contains("Naskh", StringComparison.OrdinalIgnoreCase)
                        || l.Contains("Kufi", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Log(arabic.Count > 0
            ? $"خطوط عربية موجودة ({arabic.Count}): {string.Join(", ", arabic)}"
            : "لا توجد خطوط عربية في /system/fonts — النص العربي سيظهر مربعات؛ يلزم إضافة خط (root/فلاش) قبل التعريب.");
    }

    // ---------------- adb helpers ----------------

    private async Task<List<(string Serial, string State)>> ListDevices(CancellationToken ct)
    {
        var r = await Proc.RunAsync(Paths.Adb, new[] { "devices" }, null, null, ct);
        return r.Output.Split('\n')
            .Select(l => l.Trim().Split('\t'))
            .Where(p => p.Length == 2)
            .Select(p => (p[0], p[1]))
            .ToList();
    }

    private async Task RefreshDevices(CancellationToken ct)
    {
        var devices = await ListDevices(ct);
        var current = serialBox.Text;
        serialBox.Items.Clear();
        foreach (var d in devices) serialBox.Items.Add(d.Serial);
        serialBox.Text = current;
        Log(devices.Count == 0 ? "لا توجد أجهزة." : string.Join("  |  ", devices.Select(d => $"{d.Serial} ({d.State})")));
    }

    private async Task<string> TargetSerial(CancellationToken ct)
    {
        var serial = serialBox.Text.Trim();
        var devices = await ListDevices(ct);
        if (serial.Length > 0 && devices.Any(d => d.Serial == serial)) return serial;

        var online = devices.Where(d => d.State == "device").Select(d => d.Serial).ToList();
        if (online.Count == 0) return serial;
        var pick = online.FirstOrDefault(s => s.Contains(':') && !s.Contains("._adb-tls")) ?? online[0];
        if (online.Count > 1 || serial.Length > 0) Log($"اختيار تلقائي للجهاز: {pick}");
        serialBox.Text = pick;
        return pick;
    }

    private async Task<(int Code, string Output)> Adb(CancellationToken ct, bool check, params string[] args)
    {
        var serial = HostCommands.Contains(args[0]) ? "" : await TargetSerial(ct);
        var full = serial.Length > 0 ? new[] { "-s", serial }.Concat(args) : args;
        var r = await Proc.RunAsync(Paths.Adb, full, null, Log, ct);
        if (check) Check(r, "adb");
        return r;
    }

    private Task<(int Code, string Output)> Shell(CancellationToken ct, string command) => Adb(ct, true, "shell", command);

    private async Task<(int Code, string Output)> AdbQuiet(CancellationToken ct, params string[] args)
    {
        var serial = HostCommands.Contains(args[0]) ? "" : await TargetSerial(ct);
        var full = serial.Length > 0 ? new[] { "-s", serial }.Concat(args) : args;
        return await Proc.RunAsync(Paths.Adb, full, null, null, ct);
    }

    private static void Check((int Code, string Output) r, string what)
    {
        if (r.Code != 0) throw new InvalidOperationException($"{what} انتهى برمز {r.Code}");
    }

    // ---------------- runner ----------------

    private async void Run(string title, Func<CancellationToken, Task> work)
    {
        if (cts != null) return;
        cts = new CancellationTokenSource();
        runTitle = title;
        stageLabel = "";
        stageIndex = 0;
        stageCount = 1;
        stageFraction = -1;
        SetBusy(true, title);
        UpdateProgress();
        Log($"==== {title} ====");
        var outcome = OpState.Failed;
        try
        {
            await work(cts.Token);
            outcome = OpState.Done;
            Log("✔ تم");
        }
        catch (OperationCanceledException) { outcome = OpState.Cancelled; Log("✖ أُلغيت العملية"); }
        catch (Win32Exception ex)
        {
            Log($"✖ تعذر تشغيل الأداة: {ex.Message}");
            Log("  تأكد من تنزيل platform-tools (تبويب الجهاز) أو تثبيت git/Python.");
        }
        catch (Exception ex) { Log($"✖ {ex.Message}"); }
        finally
        {
            cts.Dispose();
            cts = null;
            SetBusy(false);
            progressRing.Set(outcome, 1);
            progressLine.Set(outcome, 1);
            status.Text = outcome switch
            {
                OpState.Done => $"✔ اكتمل: {title}",
                OpState.Cancelled => $"■ أُلغيت: {title}",
                _ => $"✖ فشل: {title}",
            };
            status.ForeColor = outcome == OpState.Done ? Theme.Success : outcome == OpState.Cancelled ? Theme.Warning : Theme.Danger;
        }
    }

    private void SetBusy(bool busy, string? title = null)
    {
        foreach (var b in actionButtons) b.Enabled = !busy;
        cancelButton.Enabled = busy;
        status.Text = busy ? $"جارٍ: {title}" : "✔ جاهز";
        status.ForeColor = busy ? Theme.Gold : Theme.Success;
    }

    /// <summary>Starts stage <paramref name="number"/> of <paramref name="total"/> in a multi-stage operation.</summary>
    private void Stage(int number, int total, string label)
    {
        if (InvokeRequired) { BeginInvoke(() => Stage(number, total, label)); return; }
        stageIndex = number - 1;
        stageCount = Math.Max(1, total);
        stageFraction = 0;
        stageLabel = label;
        UpdateProgress();
    }

    /// <summary>Reports completion (0..1) of the current stage.</summary>
    private void Fraction(double value)
    {
        if (InvokeRequired) { BeginInvoke(() => Fraction(value)); return; }
        stageFraction = Math.Clamp(value, 0, 1);
        UpdateProgress();
    }

    private void TrackProgress(string line)
    {
        var item = ItemCounter.Match(line);
        if (item.Success && int.TryParse(item.Groups[2].Value, out var n) && n > 0)
            stageFraction = (int.Parse(item.Groups[1].Value) - 1) / (double)n;
        else
        {
            var pct = PercentText.Match(line);
            if (!pct.Success || !double.TryParse(pct.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) || v > 100) return;
            stageFraction = v / 100;
        }
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        var value = stageFraction < 0 && stageCount == 1 ? -1 : (stageIndex + Math.Max(0, stageFraction)) / stageCount;
        progressRing.Set(OpState.Running, value);
        progressLine.Set(OpState.Running, value);
        status.Text = stageLabel.Length > 0 ? $"جارٍ: {runTitle}  •  {stageLabel}" : $"جارٍ: {runTitle}";
    }

    private void Log(string line)
    {
        if (log.InvokeRequired) { log.BeginInvoke(new Action(() => Log(line))); return; }
        if (cts != null) TrackProgress(line);
        var color =
            line.StartsWith("✔") || line.Contains("Success", StringComparison.OrdinalIgnoreCase) || line.Contains("succeeded", StringComparison.OrdinalIgnoreCase) ? Theme.Success :
            line.StartsWith("✖") || line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("failed", StringComparison.OrdinalIgnoreCase) || line.Contains("Failure") ? Theme.Danger :
            line.StartsWith("====") || line.StartsWith("──") || line.StartsWith("════") ? Theme.Gold :
            line.StartsWith("تنبيه") || line.StartsWith("تحذير") ? Theme.Warning :
            log.ForeColor;
        log.SelectionStart = log.TextLength;
        log.SelectionLength = 0;
        log.SelectionColor = Theme.Muted;
        log.AppendText($"[{DateTime.Now:HH:mm:ss}] ");
        log.SelectionColor = color;
        log.AppendText(line + Environment.NewLine);
        log.ScrollToCaret();
    }

    private void SaveLog()
    {
        using var dlg = new SaveFileDialog { Filter = "Text|*.txt", FileName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt" };
        if (dlg.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dlg.FileName, log.Text);
    }

    // ---------------- dialogs ----------------

    private const MessageBoxOptions Rtl = MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign;

    private void Warn(string text) =>
        MessageBox.Show(this, text, "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, Rtl);

    private bool Confirm(string text) =>
        MessageBox.Show(this, text, "تأكيد", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2, Rtl) == DialogResult.Yes;

    private bool ConfirmDanger(string text)
    {
        if (MessageBox.Show(this, text + "\n\nهل أخذت نسخة احتياطية لهذه السيارة (صفحة النسخ الاحتياطي)؟ المتابعة؟", "عملية خطيرة",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2, Rtl) != DialogResult.Yes)
            return false;
        return MessageBox.Show(this, "تأكيد أخير: الكتابة على النظام أو الأقسام قد لا يمكن التراجع عنها إلا من النسخة الاحتياطية.", "تأكيد أخير",
            MessageBoxButtons.YesNo, MessageBoxIcon.Stop, MessageBoxDefaultButton.Button2, Rtl) == DialogResult.Yes;
    }

    private static void PickFile(TextBox target, string filter)
    {
        using var dlg = new OpenFileDialog { Filter = filter };
        if (dlg.ShowDialog() == DialogResult.OK) target.Text = dlg.FileName;
    }

    private static void PickFolder(TextBox target)
    {
        using var dlg = new FolderBrowserDialog();
        if (dlg.ShowDialog() == DialogResult.OK) target.Text = dlg.SelectedPath;
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start("explorer.exe", path);
    }

    private static string Need(Control box, string name)
    {
        var v = box.Text.Trim();
        if (v.Length == 0) throw new InvalidOperationException($"الحقل «{name}» فارغ.");
        return v;
    }

    // ---------------- layout helpers ----------------

    private Control Page(params Control[] groups)
    {
        var p = new BackdropPage(backdrop);
        p.Controls.AddRange(groups);
        return p;
    }

    private static Control Group(string title, params Control[] rows)
    {
        var card = new Card(title);
        card.Controls.AddRange(rows);
        return card;
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var r = new SmoothFlow
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            Margin = new Padding(0, 3, 0, 3),
        };
        r.Controls.AddRange(controls);
        return r;
    }

    private static readonly string[] DangerWords = { "كتابة", "تفليش كامل", "إلغاء التثبيت", "dm-verity", "استبدال", "إرجاع" };

    private Button Btn(string text, Action onClick)
    {
        var kind = DangerWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)) ? ButtonKind.Danger : ButtonKind.Primary;
        var b = new NeonButton(kind) { Text = text };
        b.Click += (_, _) => onClick();
        actionButtons.Add(b);
        return b;
    }

    private static Button Plain(string text, Action onClick)
    {
        var b = new NeonButton(ButtonKind.Secondary) { Text = text };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static Label Lbl(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(4, 11, 4, 3) };

    private static TextBox Txt(int width, string value = "") =>
        new() { Width = width, Text = value, RightToLeft = RightToLeft.No };
}
