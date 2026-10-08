namespace GeelyOpenTool;

public partial class MainForm
{
    private const int DevicePageIndex = 1, BackupPageIndex = 2, PackagesPageIndex = 3, FullFlashPageIndex = 4,
        SystemPageIndex = 5, MtkPageIndex = 6, LanguagePageIndex = 8;

    /// <summary>One guide step: the button to press (null for a manual action) and what it does.</summary>
    private sealed record GuideStep(string? Button, string Note, ButtonKind Kind = ButtonKind.Primary);

    private Control GuidePage()
    {
        var legend = Group("كيف تقرأ هذا الدليل",
            Row(Lbl("المرحلة 3 تُنفَّذ مرة واحدة لكل إصدار نظام على سيارة مرجعية. المراحل 1 و 2 و 4 تُنفَّذ لكل سيارة.")),
            Row(new KeyChip("زر عادي", ButtonKind.Primary), Lbl("عملية آمنة"),
                new KeyChip("زر أحمر", ButtonKind.Danger), Lbl("يكتب على النظام — يطلب تأكيداً مزدوجاً"),
                new KeyChip("زر ثانوي", ButtonKind.Secondary), Lbl("اختيار ملف/مجلد أو فتح مجلد")),
            Row(Lbl("أثناء أي عملية: الدائرة والخط الذهبي أسفل النافذة يعرضان نسبة الإكمال. زر «إيقاف العملية» يلغيها بأمان.")));

        return Page(
            legend,
            GuideCard(1, "الاتصال بالوحدة", "الجهاز والاتصال", DevicePageIndex,
                new("تنزيل/تحديث platform-tools (adb) من Google", "مرة واحدة فقط عند أول تشغيل للبرنامج."),
                new(null, "على شاشة السيارة: فعّل خيارات المطوّر ← تصحيح USB (أو التصحيح اللاسلكي)."),
                new("تحديث قائمة الأجهزة", "عند التوصيل بكابل USB — يختار البرنامج الوحدة تلقائياً."),
                new("اتصال", "للاتصال اللاسلكي: اكتب IP:Port ثم اضغط. لأندرويد 11+ استعمل «إقران» أولاً."),
                new("معلومات الوحدة", "تأكد أن المؤشر أعلى النافذة أصبح أخضر «متصل».")),
            GuideCard(2, "النسخ الاحتياطي لهذه السيارة (BACKUP)", "النسخ الاحتياطي", BackupPageIndex,
                new("حفظ معلومات الاسترداد", "getprop، الحزم، الأقسام، build.prop — بدون أي كتابة."),
                new("أخذ لقطة النظام (priv-app, app, etc, fonts, vendor/etc, build.prop)", "الملفات الأصلية لهذا الإصدار."),
                new("نسخ الأقسام الخاصة بالسيارة", "اختياري عبر MTK BROM: nvram / nvdata / proinfo... للسيارة نفسها فقط."),
                new("فتح مجلد النسخ", "انسخ مجلد car_... إلى مكان آمن.", ButtonKind.Secondary)),
            GuideCard(3, "بناء الحزم (مرة واحدة لكل إصدار نظام)", "صانع ومكتبة الحزم", PackagesPageIndex,
                new("إنشاء الحزمة (تُقرأ بصمة السيارة المتصلة)", "SYSTEM_PACKAGE على السيارة المرجعية — تُحفظ buildId و fingerprint."),
                new("نسخ مجلد تطبيق من اللقطة", "خذ التطبيق الأصلي ثم استبدل الـ APK بنسختك.", ButtonKind.Secondary),
                new("إضافة إلى المسار", "ملفاتك المعدّلة في مسارها داخل /system أو /vendor.", ButtonKind.Secondary),
                new("إضافة خطوط إلى system\\fonts", "خطوط عربية.", ButtonKind.Secondary),
                new(null, "أنشئ APK_PACKAGE منفصلة للتطبيقات العادية (apks + install.json + permissions.json)."),
                new("فحص محتوى الحزمة (Validator)", "يرفض أي ملف CAR_SPECIFIC أو خارج البنية.")),
            GuideCard(4, "الفلاش لكل سيارة (S0 → S4)", "الفلاش (S0 → S4)", FullFlashPageIndex,
                new(null, "اترك «حزمة النظام» فارغة: المدقق يختار الحزمة المطابقة لبصمة السيارة تلقائياً. اختر حزمة التطبيقات إن وُجدت."),
                new("فحص فقط — S0 (بدون أي كتابة)", "devices ← getprop ← fingerprint ← root ← mounts ← الكتابة ← التواقيع."),
                new("تعطيل dm-verity", "فقط إذا قال الفحص إن verity يمنع remount — من صفحة «نشر النظام»، ثم أعد التشغيل وافحص مجدداً.", ButtonKind.Danger),
                new("▶ تشغيل الفلاش (S0 → S4)", "PASS ← نسخ احتياطي ← نشر. MISMATCH ← STOP بدون أي كتابة."),
                new("إعادة تشغيل الوحدة", "لتطبيق كل التغييرات.")),
            GuideCard(5, "بعد الفلاش: السرعة والتعريب", "التعريب و Overlay", LanguagePageIndex,
                new("تحسين كل التطبيقات (compile -m speed -f -a)", "من صفحة «نشر النظام» إذا لم تفعّل خيار التحسين."),
                new("فحص خطوط العربية في /system/fonts", "يتأكد من وجود خط عربي."),
                new("تعيين اللغة عبر root", "يضبط لغة الواجهة (مثل ar-DZ)."),
                new("3) بناء + تثبيت + تفعيل", "Overlay عربي لتطبيقات جيلي بعد استخراج النصوص وترجمتها.")),
            GuideCard(6, "عند حدوث مشكلة", "نشر النظام (root)", SystemPageIndex,
                new("استعادة", "اختر مجلد deploy_... — يُرجع الملفات الأصلية كما كانت.", ButtonKind.Danger),
                new("إرجاع الوضع الافتراضي (--reset)", "إذا أصبح النظام بطيئاً بعد التحسين.", ButtonKind.Danger),
                new("كتابة القسم", "آخر حل من صفحة MTK — من نسخة نفس السيارة فقط.", ButtonKind.Danger),
                new("حفظ السجل", "احفظ السجل وأرسله لطلب المساعدة.", ButtonKind.Secondary)),
            GuideCard(7, "التحكم في السيارة (تمهيد)", "نشر النظام (root)", SystemPageIndex,
                new("فحص مفتاح المنصة (AOSP test-key)", "يحدد إمكانية بناء تطبيق تحكم بصلاحيات النظام."),
                new("جمع معلومات واجهة التحكم من الوحدة", "خدمات السيارة (الإضاءة، المكيف، الزجاج)."),
                new("مراقبة زر الصوت في المقود (30 ث)", "اضغط زر الصوت في المقود أثناء المراقبة.")));
    }

    private Control GuideCard(int number, string title, string pageName, int pageIndex, params GuideStep[] steps)
    {
        var rows = new List<Control>
        {
            Row(Lbl($"الصفحة: {pageName}"), Plain("اذهب إلى الصفحة", () => selectPage?.Invoke(pageIndex))),
        };
        for (var i = 0; i < steps.Length; i++)
        {
            var s = steps[i];
            Control action = s.Button is null
                ? new Label { Text = "يدوي", AutoSize = true, Tag = "manual", ForeColor = Theme.Sand, BackColor = Color.Transparent, Font = Theme.Bold, Margin = new Padding(8, 10, 8, 3) }
                : new KeyChip(s.Button, s.Kind);
            rows.Add(Row(new StepBadge(i + 1), action, Lbl(s.Note)));
        }
        return Group($"المرحلة {number} — {title}", rows.ToArray());
    }
}
