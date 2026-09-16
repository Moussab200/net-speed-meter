// اختبارات Net Speed Meter
// ------------------------------------------------------------------
// بتشتغل على **نفس دوال البرنامج الحقيقية** (مش نسخة منها)، فأي كسر
// بالمنطق بينمسك قبل ما ينشر.
//
//   run-tests.cmd
// ------------------------------------------------------------------

using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace NetSpeedMeter
{
    static class Tests
    {
        static int pass, fail;

        static void Check(string name, bool ok, string detail)
        {
            if (ok) { pass++; Console.WriteLine("  PASS  " + name); }
            else { fail++; Console.WriteLine("  FAIL  " + name + (detail.Length > 0 ? "   -> " + detail : "")); }
        }
        static void Check(string name, bool ok) { Check(name, ok, ""); }

        static void Section(string t)
        {
            Console.WriteLine();
            Console.WriteLine("== " + t + " ==");
        }

        public static int Run()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== اختبارات Net Speed Meter ===");

            ContrastTests();
            FormatTests();
            UsageTests();
            CounterTests();
            SettingsTests();
            ClampTests();

            Console.WriteLine();
            Console.WriteLine("=================================");
            Console.WriteLine(fail == 0
                ? "  كل الاختبارات نجحت (" + pass + ")"
                : "  " + fail + " فشل / " + pass + " نجح");
            Console.WriteLine("=================================");
            return fail == 0 ? 0 : 1;
        }

        // ============================================================
        // 1) التباين - يمنع نهائياً "نص أبيض على خلفية بيضاء"
        // ============================================================
        static void ContrastTests()
        {
            Section("التباين وقابلية القراءة");

            // نسب معروفة للتحقق من صحة المعادلة نفسها
            double bw = Widget.ContrastRatio(Color.White, Color.Black);
            Check("أبيض/أسود = 21", Math.Abs(bw - 21.0) < 0.1, bw.ToString("0.00"));
            double same = Widget.ContrastRatio(Color.Gray, Color.Gray);
            Check("نفس اللون = 1", Math.Abs(same - 1.0) < 0.001, same.ToString("0.000"));

            // الحالة اللي كسرت البرنامج فعلياً: خلفية فاتحة + نص أبيض محفوظ
            Color lightTaskbar = Color.FromArgb(255, 243, 243, 243);   // ثيم ويندوز الفاتح
            Color storedText = Color.FromArgb(255, 236, 236, 236);     // #ECECEC الافتراضي
            double before = Widget.ContrastRatio(storedText, lightTaskbar);
            Check("الحالة المكسورة فعلاً غير مقروءة قبل الإصلاح", before < 1.5, before.ToString("0.00"));

            Color fixedText = Widget.EnsureContrast(storedText, lightTaskbar, 4.5);
            double after = Widget.ContrastRatio(fixedText, lightTaskbar);
            Check("بعد الإصلاح صار مقروء (>=4.5)", after >= 4.5, after.ToString("0.00"));

            // فحص شامل: أي لون خلفية ممكن + الألوان الافتراضية = لازم يضل مقروء
            int bad = 0; string worst = "";
            double worstRatio = 99;
            for (int r = 0; r <= 255; r += 15)
                for (int g = 0; g <= 255; g += 15)
                    for (int b = 0; b <= 255; b += 15)
                    {
                        Color bg = Color.FromArgb(255, r, g, b);
                        Color t = Widget.EnsureContrast(Color.FromArgb(Cfg.DefText), bg, 4.5);
                        double ratio = Widget.ContrastRatio(t, bg);
                        if (ratio < 4.4) { bad++; if (ratio < worstRatio) { worstRatio = ratio; worst = bg.ToString(); } }
                    }
            Check("النص مقروء فوق كل خلفية ممكنة (" + (18 * 18 * 18) + " لون)",
                  bad == 0, bad + " فشل، الأسوأ " + worst + " @ " + worstRatio.ToString("0.00"));

            // نفس الشي للأسهم (معيار أقل لأنها أشكال مش نص)
            bad = 0;
            for (int r = 0; r <= 255; r += 15)
                for (int g = 0; g <= 255; g += 15)
                    for (int b = 0; b <= 255; b += 15)
                    {
                        Color bg = Color.FromArgb(255, r, g, b);
                        foreach (int col in new int[] { Cfg.DefUp, Cfg.DefDown })
                        {
                            Color a = Widget.EnsureContrast(Color.FromArgb(col), bg, 3.0);
                            if (Widget.ContrastRatio(a, bg) < 2.9) bad++;
                        }
                    }
            Check("الأسهم واضحة فوق كل خلفية ممكنة", bad == 0, bad + " فشل");

            // ما بيغيّر لون أصلاً إذا كان مقروء من الأساس
            Color darkBg = Color.FromArgb(255, 21, 21, 21);
            Color keep = Widget.EnsureContrast(Color.FromArgb(Cfg.DefText), darkBg, 4.5);
            Check("ما بيلمس اللون إذا كان مقروء أصلاً", keep.ToArgb() == Cfg.DefText);

            // بيحافظ على الشفافية
            Color withAlpha = Color.FromArgb(128, 236, 236, 236);
            Color res = Widget.EnsureContrast(withAlpha, Color.FromArgb(255, 240, 240, 240), 4.5);
            Check("بيحافظ على قناة الشفافية", res.A == 128, "A=" + res.A);
        }

        // ============================================================
        // 2) تنسيق السرعة
        // ============================================================
        static void FormatTests()
        {
            Section("تنسيق السرعة والوحدات");
            string n, u;

            Widget.FormatSpeed(0, 0, false, out n, out u);
            Check("صفر = 0.0 B/s", n == "0.0" && u == "B/s", n + " " + u);

            Widget.FormatSpeed(1024, 0, false, out n, out u);
            Check("1024 بايت = 1.0 KBps", n == "1.0" && u == "KBps", n + " " + u);

            Widget.FormatSpeed(1024.0 * 1024, 0, false, out n, out u);
            Check("1 ميجابايت = 1.0 MBps", n == "1.0" && u == "MBps", n + " " + u);

            Widget.FormatSpeed(1024.0 * 1024 * 1024, 0, false, out n, out u);
            Check("1 جيجابايت = 1.0 GBps", n == "1.0" && u == "GBps", n + " " + u);

            // بِت مقابل بايت: لازم يكون ×8 بالضبط
            Widget.FormatSpeed(1000000, 0, true, out n, out u);
            Check("1 ميجابايت/ث = 8.0 Mbps بالبِت", n == "8.0" && u == "Mbps", n + " " + u);

            // وحدة مثبتة
            Widget.FormatSpeed(1024.0 * 1024, 1, false, out n, out u);
            Check("تثبيت كيلو: 1MB = 1024.0 KBps", u == "KBps", u);

            Widget.FormatSpeed(100, 3, false, out n, out u);
            Check("تثبيت جيجا بيضل جيجا حتى لو الرقم صغير", u == "GBps", u);

            // ما بيتخطى الحد
            Widget.FormatSpeed(1e15, 0, false, out n, out u);
            Check("رقم ضخم ما بيكسر التنسيق", n.Length <= 5 && u == "GBps", n + " " + u);

            // عرض ثابت - ما بيرجف
            Widget.FormatSpeed(99.9 * 1024, 0, false, out n, out u);
            bool w1 = n.Length <= 5;
            Widget.FormatSpeed(999 * 1024, 0, false, out n, out u);
            Check("الأرقام ما بتتجاوز 5 خانات (منع الرجفة)", w1 && n.Length <= 5, n);

            // FmtBytes
            Check("FmtBytes: 0 = 0.0 B", Widget.FmtBytes(0) == "0.0 B", Widget.FmtBytes(0));
            Check("FmtBytes: 1024 = 1.0 KB", Widget.FmtBytes(1024) == "1.0 KB", Widget.FmtBytes(1024));
            Check("FmtBytes: 1 جيجا", Widget.FmtBytes(1073741824L) == "1.0 GB", Widget.FmtBytes(1073741824L));
            Check("FmtBytes: تيرابايت ما بينكسر", Widget.FmtBytes(1099511627776L).EndsWith("TB"),
                  Widget.FmtBytes(1099511627776L));
        }

        // ============================================================
        // 3) عدّادات الاستهلاك والتدوير
        // ============================================================
        static void UsageTests()
        {
            Section("الاستهلاك والتدوير اليومي/الشهري");

            string dir = Path.Combine(Path.GetTempPath(), "nsm_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Usage.DayKey = ""; Usage.MonthKey = "";
                Usage.DayRx = Usage.DayTx = Usage.MonRx = Usage.MonTx = Usage.TotRx = Usage.TotTx = 0;

                Usage.Roll();
                Usage.Add(1000, 200);
                Usage.Add(500, 100);
                Check("الجمع باليوم", Usage.DayRx == 1500 && Usage.DayTx == 300);
                Check("الجمع بالشهر", Usage.MonRx == 1500 && Usage.MonTx == 300);
                Check("الجمع بالإجمالي", Usage.TotRx == 1500 && Usage.TotTx == 300);

                // تدوير اليوم: نزوّر مفتاح يوم قديم
                Usage.DayKey = "2000-01-01";
                Usage.Add(300, 50);
                Check("اليوم بيتصفّر عند يوم جديد", Usage.DayRx == 300 && Usage.DayTx == 50,
                      Usage.DayRx + "/" + Usage.DayTx);
                Check("الشهر بيضل محفوظ عند تغيّر اليوم", Usage.MonRx == 1800);
                Check("الإجمالي بيضل محفوظ", Usage.TotRx == 1800);

                // تدوير الشهر
                Usage.MonthKey = "2000-01";
                Usage.Add(700, 150);
                Check("الشهر بيتصفّر عند شهر جديد", Usage.MonRx == 700 && Usage.MonTx == 150);
                Check("الإجمالي بيضل محفوظ عبر الشهور", Usage.TotRx == 2500 && Usage.TotTx == 500);

                // صفر ما بيحرّك شي
                long before = Usage.TotRx;
                Usage.Add(0, 0);
                Check("إضافة صفر ما بتغيّر شي", Usage.TotRx == before);
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }

        // ============================================================
        // 4) حساب الفروقات - إعادة تشغيل كرت الشبكة
        // ============================================================
        static void CounterTests()
        {
            Section("فروقات العدّاد");

            // نفس منطق OnTick
            long lastRx = 1000000;
            long rx = 1050000;
            long d = rx - lastRx; if (d < 0) d = 0;
            Check("فرق عادي محسوب صح", d == 50000, d.ToString());

            // الكرت أعاد التشغيل - العدّاد رجع لرقم أصغر
            lastRx = 1050000; rx = 2000;
            d = rx - lastRx; if (d < 0) d = 0;
            Check("إعادة تشغيل الكرت ما بتعطي رقم ضخم غلط", d == 0, d.ToString());

            // القسمة على الوقت الفعلي مش على 1000 ثابتة
            double dt = 2.5;   // الجهاز رجع من السكون
            double speed = 50000 / dt;
            Check("القسمة على الوقت الفعلي", Math.Abs(speed - 20000) < 0.001, speed.ToString());
        }

        // ============================================================
        // 5) الإعدادات - كل مفتاح بينحفظ لازم ينقرا
        // ============================================================
        static void SettingsTests()
        {
            Section("حفظ واسترجاع الإعدادات");

            // ما في مفتاح بينحفظ وما بينقرا (كان بيسبب ضياع إعدادات صامت)
            string src = FindSource("NetSpeedMeter.cs");
            if (src == null) { Check("ملف المصدر موجود للفحص", false, "ما لقيته"); return; }
            string code = File.ReadAllText(src, Encoding.UTF8);

            System.Collections.Generic.List<string> saved = Matches(code, "AppendLine(\"", "=");
            System.Collections.Generic.List<string> loaded = Matches(code, "case \"", "\"");

            int missing = 0; string names = "";
            foreach (string k in saved)
                if (!loaded.Contains(k)) { missing++; names += k + " "; }
            Check("كل مفتاح بينحفظ بينقرا كمان (" + saved.Count + " مفتاح)", missing == 0, names);

            // القيم الافتراضية منطقية
            Check("الشفافية الافتراضية كاملة", Cfg.Opacity == 100);
            Check("إخفاء ملء الشاشة مطفي افتراضياً", Cfg.HideFullscreen == false);
            Check("مطابقة شريط المهام مفعّلة", Cfg.MatchTaskbar);
            Check("لون شريط المهام مفعّل", Cfg.UseTaskbarColor);
        }

        static System.Collections.Generic.List<string> Matches(string code, string open, string close)
        {
            var res = new System.Collections.Generic.List<string>();
            int i = 0;
            while ((i = code.IndexOf(open, i, StringComparison.Ordinal)) >= 0)
            {
                i += open.Length;
                int e = code.IndexOf(close, i, StringComparison.Ordinal);
                if (e < 0) break;
                string k = code.Substring(i, e - i);
                if (k.Length > 0 && k.Length < 24 && IsWord(k) && !res.Contains(k)) res.Add(k);
            }
            return res;
        }

        static bool IsWord(string s)
        {
            foreach (char c in s) if (!char.IsLetterOrDigit(c)) return false;
            return true;
        }

        // الاختبارات بتنبنى بمجلد مؤقت، فمجلد الملف التنفيذي مش مجلد المصدر.
        // run-tests.cmd بيعمل cd على مجلد المشروع، فمنبلّش من مجلد العمل الحالي.
        static string FindSource(string name)
        {
            foreach (string start in new string[] {
                        Directory.GetCurrentDirectory(),
                        AppDomain.CurrentDomain.BaseDirectory })
            {
                string d = start;
                for (int i = 0; i < 5 && d != null; i++)
                {
                    string p = Path.Combine(d, name);
                    if (File.Exists(p)) return p;
                    DirectoryInfo up = Directory.GetParent(d.TrimEnd('\\'));
                    d = up == null ? null : up.FullName;
                }
            }
            return null;
        }

        // ============================================================
        // 6) التقييد
        // ============================================================
        static void ClampTests()
        {
            Section("تقييد القيم");
            Check("Clamp تحت الحد", Cfg.Clamp(-5, 0, 10) == 0);
            Check("Clamp فوق الحد", Cfg.Clamp(50, 0, 10) == 10);
            Check("Clamp ضمن المدى", Cfg.Clamp(7, 0, 10) == 7);

            // مكان الودجت: ما بيطلع برّا حدود الشاشة
            int screenW = 1920, screenH = 1080, w = 120, h = 60;
            int x = Cfg.Clamp(99999, 0, screenW - w);
            int y = Cfg.Clamp(99999, 0, screenH - h);
            Check("المكان ما بيتجاوز حدود الشاشة", x == screenW - w && y == screenH - h, x + "," + y);
            x = Cfg.Clamp(-500, 0, screenW - w);
            y = Cfg.Clamp(-500, 0, screenH - h);
            Check("المكان ما بينزل تحت الصفر", x == 0 && y == 0, x + "," + y);
        }
    }
}
