// Net Speed Meter - ودجت سرعة الإنترنت والاستهلاك
// C# 5 متوافق مع csc.exe المدمج بويندوز (.NET Framework 4)
// بلا أي اعتمادية خارجية.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NetSpeedMeter
{
    // ============================================================
    // نداءات ويندوز الأصلية
    // ============================================================
    static class Native
    {
        public const int WS_EX_LAYERED     = 0x00080000;
        public const int WS_EX_TOOLWINDOW  = 0x00000080;
        public const int WS_EX_NOACTIVATE  = 0x08000000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int GWL_EXSTYLE = -20;
        public const int ULW_ALPHA   = 2;
        public const byte AC_SRC_OVER  = 0;
        public const byte AC_SRC_ALPHA = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx, cy; public SIZE(int a, int b) { cx = a; cy = b; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey,
            ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")]  public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")]  public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")]  public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")]  public static extern bool DeleteObject(IntPtr h);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int i);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int i, int v);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindow(string cls, string win);
        [DllImport("gdi32.dll")] public static extern uint GetPixel(IntPtr hdc, int x, int y);

        public const int SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, int flags);

        // خطّاف يبلّغنا **فوراً** أي ما نافذة تانية تصير بالمقدمة - بدون هالخطّاف
        // كنا نعتمد بس على تثبيت "فوق الكل" كل ثانية بالنبضة، فأي برنامج بيفتح
        // ويسرق القمة كان يغطّي الودجت لحد ثانية كاملة قبل ما نرجّعه فوق -
        // وهاد بالضبط اللي كان يبين كأنه "يختفي ويرجع".
        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd,
            int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
            IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc,
            uint idProcess, uint idThread, uint dwFlags);
        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        // حالة "هل المستخدم بوضع ملء شاشة حقيقي أو عرض تقديمي؟" - نفس ما يستعمله شريط المهام
        public enum QUNS
        {
            NOT_PRESENT = 1, BUSY = 2, RUNNING_D3D_FULL_SCREEN = 3,
            PRESENTATION_MODE = 4, ACCEPTS_NOTIFICATIONS = 5, QUIET_TIME = 6, APP = 7
        }
        [DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState(out QUNS state);

        // رسالة عامة بين النسخ - النسخة التانية بتبعتها للنسخة الشغالة
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern uint RegisterWindowMessage(string lpString);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        // حصر البحث عن المكتبات بمجلد النظام - يمنع زرع DLL بمجلد البرنامج
        public const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetDefaultDllDirectories(uint directoryFlags);
    }

    // ============================================================
    // كتابة ملفات آمنة - مشتركة بين الإعدادات والاستهلاك
    // ============================================================
    static class Io
    {
        // البرنامج مفروض يضل شغّال 24 ساعة، وممكن يتقفل بالغلط (قطع كهرباء،
        // Task Manager) بمنتصف الكتابة. File.WriteAllText مباشرة على الملف
        // الأصلي بتعرّضه لنص ملف تالف لو صار هيك بالضبط. الحل القياسي: نكتب
        // بملف مؤقت جنبه، وبعدين نستبدل بعملية واحدة ذرّية - إما الملف القديم
        // كامل أو الجديد كامل، ما في احتمال نص كتابة.
        public static void WriteAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, Encoding.UTF8);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        // بيمنع error.log من التضخم بدون حد - لو صار خطأ متكرر كل ثانية
        // (استثناء بحلقة الرسم مثلاً) ما يظل يكبر لأشهر بدون توقف.
        // القص على البايتات مباشرة مش على عدد الأسطر - سطر واحد بتتبّع كامل
        // (exception + stack trace) ممكن يكون آلاف البايتات لحاله، فالقص على
        // "عدد الأسطر" ما بيضمن حد فعلي للحجم.
        public static void AppendCapped(string path, string line, int capBytes)
        {
            try
            {
                if (File.Exists(path))
                {
                    byte[] data = File.ReadAllBytes(path);
                    if (data.Length > capBytes)
                    {
                        // نرجع لنص الحد بدل الحد بالضبط - حتى ما نعيد القص بكل
                        // نبضة تالية طول ما الملف عم يكبر شوي شوي فوق الحد
                        int start = data.Length - capBytes / 2;
                        int nl = Array.IndexOf(data, (byte)'\n', start);
                        if (nl >= 0 && nl + 1 < data.Length) start = nl + 1;   // بداية سطر كامل
                        byte[] tail = new byte[data.Length - start];
                        Array.Copy(data, start, tail, 0, tail.Length);
                        File.WriteAllBytes(path, tail);
                    }
                }
            }
            catch { }
            File.AppendAllText(path, line, Encoding.UTF8);
        }
    }

    // ============================================================
    // اللغات والنصوص
    // ============================================================
    public enum Lang
    {
        English,
        Arabic
    }

    public enum TrId
    {
        TrayToday,
        MenuPlace,
        MenuLock,
        MenuSnap,
        MenuCornerTR,
        MenuCornerTL,
        MenuCornerBR,
        MenuCornerBL,
        MenuMoveToScreen,
        MenuScreen,
        MenuPrimary,
        MenuAppearance,
        MenuTopMost,
        MenuOpacity,
        MenuFontSize,
        MenuFontSmall,
        MenuFontMedium,
        MenuFontLarge,
        MenuOrientation,
        MenuVertical,
        MenuHorizontal,
        MenuMatchTbHeight,
        MenuMatchTbColor,
        MenuColors,
        ColorDefault,
        ColorNeon,
        ColorSunset,
        ColorMonochrome,
        ColorFrost,
        ColorPickUp,
        ColorPickDown,
        ColorPickText,
        ColorPickBg,
        ColorReset,
        MenuMeasure,
        MenuFormat,
        FormatAuto,
        FormatFixedKB,
        FormatFixedMB,
        FormatFixedGB,
        MenuUnit,
        UnitBytes,
        UnitBits,
        MenuAdapter,
        AdapterAuto,
        AdapterDisconnected,
        MenuSpeedTest,
        MenuLastTest,
        MenuUsage,
        UsageToday,
        UsageMonth,
        UsageTotal,
        UsageReset,
        MenuBehavior,
        MenuClickThrough,
        MenuHideFullscreen,
        MenuStartWithWindows,
        MenuStartShortcut,
        MenuResetDefaults,
        MenuLanguage,
        LangEnglish,
        LangArabic,
        MenuExit,
        SpeedTestTitle,
        SpeedTestPing,
        SpeedTestDown,
        SpeedTestUp,
        SpeedTestReady,
        SpeedTestStart,
        SpeedTestClose,
        SpeedTestLastPrefix,
        SpeedTestMeasuringPing,
        SpeedTestMeasuringDown,
        SpeedTestMeasuringUp,
        SpeedTestPingFailed,
        SpeedTestAborted,
        SpeedTestFailed,
        SpeedTestCompleted,
        SpeedTestErrTimeout,
        SpeedTestErrResolution,
        SpeedTestErrConnect,
        SpeedTestErrTls,
        SpeedTestErrCert,
        SpeedTestErrGeneral
    }

    public static class Tr
    {
        public static string Get(TrId id)
        {
            return Get(id, Cfg.Language);
        }

        public static string Get(TrId id, Lang lang)
        {
            bool ar = lang == Lang.Arabic;
            switch (id)
            {
                case TrId.TrayToday: return ar ? "اليوم: " : "Today: ";
                case TrId.MenuPlace: return ar ? "المكان" : "Position";
                case TrId.MenuLock: return ar ? "اقفل المكان" : "Lock Position";
                case TrId.MenuSnap: return ar ? "الالتصاق بحواف الشاشة" : "Snap to Screen Edges";
                case TrId.MenuCornerTR: return ar ? "أعلى اليمين" : "Top Right";
                case TrId.MenuCornerTL: return ar ? "أعلى اليسار" : "Top Left";
                case TrId.MenuCornerBR: return ar ? "أسفل اليمين" : "Bottom Right";
                case TrId.MenuCornerBL: return ar ? "أسفل اليسار" : "Bottom Left";
                case TrId.MenuMoveToScreen: return ar ? "انقله لشاشة" : "Move to Screen";
                case TrId.MenuScreen: return ar ? "شاشة " : "Screen ";
                case TrId.MenuPrimary: return ar ? " (الرئيسية)" : " (Primary)";
                case TrId.MenuAppearance: return ar ? "المظهر" : "Appearance";
                case TrId.MenuTopMost: return ar ? "دائماً بالمقدمة" : "Always on Top";
                case TrId.MenuOpacity: return ar ? "الشفافية" : "Opacity";
                case TrId.MenuFontSize: return ar ? "حجم الخط" : "Font Size";
                case TrId.MenuFontSmall: return ar ? "صغير" : "Small";
                case TrId.MenuFontMedium: return ar ? "متوسط" : "Medium";
                case TrId.MenuFontLarge: return ar ? "كبير" : "Large";
                case TrId.MenuOrientation: return ar ? "الاتجاه" : "Orientation";
                case TrId.MenuVertical: return ar ? "عمودي" : "Vertical";
                case TrId.MenuHorizontal: return ar ? "أفقي" : "Horizontal";
                case TrId.MenuMatchTbHeight: return ar ? "بنفس ارتفاع شريط المهام" : "Match Taskbar Height";
                case TrId.MenuMatchTbColor: return ar ? "بنفس لون شريط المهام" : "Match Taskbar Color";
                case TrId.MenuColors: return ar ? "الألوان" : "Colors";
                case TrId.ColorDefault: return ar ? "الافتراضي" : "Default";
                case TrId.ColorNeon: return ar ? "نيون" : "Neon";
                case TrId.ColorSunset: return ar ? "غروب" : "Sunset";
                case TrId.ColorMonochrome: return ar ? "أبيض وأسود" : "Monochrome";
                case TrId.ColorFrost: return ar ? "ثلجي" : "Frost";
                case TrId.ColorPickUp: return ar ? "لون سهم الرفع…" : "Upload Arrow Color…";
                case TrId.ColorPickDown: return ar ? "لون سهم التنزيل…" : "Download Arrow Color…";
                case TrId.ColorPickText: return ar ? "لون الأرقام…" : "Text Color…";
                case TrId.ColorPickBg: return ar ? "لون الخلفية…" : "Background Color…";
                case TrId.ColorReset: return ar ? "استرجاع الألوان الافتراضية" : "Reset Default Colors";
                case TrId.MenuMeasure: return ar ? "القياس" : "Measurement";
                case TrId.MenuFormat: return ar ? "الصيغة" : "Format";
                case TrId.FormatAuto: return ar ? "تلقائي (بيبدّل لحاله)" : "Auto (dynamic)";
                case TrId.FormatFixedKB: return ar ? "كيلو ثابت" : "Fixed KB";
                case TrId.FormatFixedMB: return ar ? "ميجا ثابت" : "Fixed MB";
                case TrId.FormatFixedGB: return ar ? "جيجا ثابت" : "Fixed GB";
                case TrId.MenuUnit: return ar ? "الوحدة" : "Unit";
                case TrId.UnitBytes: return ar ? "بايت  ‏(MBps)" : "Bytes (MBps)";
                case TrId.UnitBits: return ar ? "بِت  ‏(Mbps)" : "Bits (Mbps)";
                case TrId.MenuAdapter: return ar ? "كرت الشبكة" : "Network Adapter";
                case TrId.AdapterAuto: return ar ? "تلقائي" : "Auto";
                case TrId.AdapterDisconnected: return ar ? "  (مفصول)" : "  (Disconnected)";
                case TrId.MenuSpeedTest: return ar ? "اختبار سرعة الإنترنت…" : "Speed Test…";
                case TrId.MenuLastTest: return ar ? "      آخر اختبار:  " : "      Last test:  ";
                case TrId.MenuUsage: return ar ? "الاستهلاك" : "Data Usage";
                case TrId.UsageToday: return ar ? "اليوم:        ↓ " : "Today:        ↓ ";
                case TrId.UsageMonth: return ar ? "هذا الشهر:  ↓ " : "This Month:   ↓ ";
                case TrId.UsageTotal: return ar ? "الإجمالي:     ↓ " : "Total:        ↓ ";
                case TrId.UsageReset: return ar ? "تصفير العدّادات" : "Reset Counters";
                case TrId.MenuBehavior: return ar ? "السلوك" : "Behavior";
                case TrId.MenuClickThrough: return ar ? "تمرير النقر (النقرة بتعدّي من خلاله)" : "Click-through (transparent to clicks)";
                case TrId.MenuHideFullscreen: return ar ? "إخفاء عند ملء الشاشة" : "Hide in Fullscreen";
                case TrId.MenuStartWithWindows: return ar ? "يبدأ مع ويندوز" : "Start with Windows";
                case TrId.MenuStartShortcut: return ar ? "أنشئ اختصار في قائمة ابدأ (حتى يلاقيه بحث ويندوز)" : "Create Start Menu shortcut (for Windows search)";
                case TrId.MenuResetDefaults: return ar ? "استرجاع الإعدادات الافتراضية" : "Reset All Settings";
                case TrId.MenuLanguage: return ar ? "اللغة" : "Language";
                case TrId.LangEnglish: return "English";
                case TrId.LangArabic: return "عربي";
                case TrId.MenuExit: return ar ? "خروج" : "Exit";
                case TrId.SpeedTestTitle: return ar ? "اختبار سرعة الإنترنت" : "Internet Speed Test";
                case TrId.SpeedTestPing: return ar ? "زمن الاستجابة" : "Latency";
                case TrId.SpeedTestDown: return ar ? "التنزيل  ↓" : "Download  ↓";
                case TrId.SpeedTestUp: return ar ? "الرفع  ↑" : "Upload  ↑";
                case TrId.SpeedTestReady: return ar ? "جاهز للاختبار" : "Ready to test";
                case TrId.SpeedTestStart: return ar ? "ابدأ الاختبار" : "Start Test";
                case TrId.SpeedTestClose: return ar ? "إغلاق" : "Close";
                case TrId.SpeedTestLastPrefix: return ar ? "آخر اختبار: " : "Last test: ";
                case TrId.SpeedTestMeasuringPing: return ar ? "قياس زمن الاستجابة…" : "Measuring latency…";
                case TrId.SpeedTestMeasuringDown: return ar ? "قياس سرعة التنزيل…" : "Measuring download speed…";
                case TrId.SpeedTestMeasuringUp: return ar ? "قياس سرعة الرفع…" : "Measuring upload speed…";
                case TrId.SpeedTestPingFailed: return ar ? "تعذّر" : "Failed";
                case TrId.SpeedTestAborted: return ar ? "انلغى الاختبار" : "Test cancelled";
                case TrId.SpeedTestFailed: return ar ? "تعذّر الاتصال بخادم القياس" : "Could not connect to speed test server";
                case TrId.SpeedTestCompleted: return ar ? "خلص الاختبار" : "Test completed";
                case TrId.SpeedTestErrTimeout: return ar ? "انتهت مهلة الاتصال" : "Connection timed out";
                case TrId.SpeedTestErrResolution: return ar ? "تعذّر الوصول للخادم - تحقق من الإنترنت" : "Could not reach server - check your internet connection";
                case TrId.SpeedTestErrConnect: return ar ? "تعذّر الاتصال - ممكن يكون جدار الحماية مانع البرنامج" : "Connection failed - firewall may be blocking the app";
                case TrId.SpeedTestErrTls: return ar ? "مشكلة بالاتصال الآمن (TLS)" : "Secure connection error (TLS)";
                case TrId.SpeedTestErrCert: return ar ? "مشكلة بشهادة الأمان" : "Security certificate error";
                case TrId.SpeedTestErrGeneral: return ar ? "تعذّر الاتصال" : "Connection failed";
                default: return "";
            }
        }
    }

    // ============================================================
    // ملف الإعدادات
    // ============================================================
    static class Cfg
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetSpeedMeter");

        static string SettingsPath { get { return Path.Combine(Dir, "settings.ini"); } }

        // الافتراضيات - مطابقة للصورة المرجعية
        public const int DefUp   = unchecked((int)0xFF4C8DF6); // أزرق للرفع
        public const int DefDown = unchecked((int)0xFF35C75A); // أخضر للتنزيل
        public const int DefText = unchecked((int)0xFFECECEC); // أبيض مائل للرمادي
        public const int DefBg   = unchecked((int)0xEB2D2D30); // رمادي داكن نصف شفاف

        public static int  X = -1, Y = -1;
        public static bool Locked;
        public static bool Snap = true;
        public static bool TopMost = true;
        public static int  Opacity = 100;   // 50..100
        public static int  FontIdx = 1;     // 0 صغير / 1 متوسط / 2 كبير
        public static bool Vertical = true;
        public static int  UnitMode = 0;    // 0 تلقائي / 1 كيلو / 2 ميجا / 3 جيجا
        public static bool Bits;            // false = بايت
        public static string Adapter = "";  // "" = تلقائي، غير هيك Id الكرت
        public static int  ColUp = DefUp, ColDown = DefDown, ColText = DefText, ColBg = DefBg;
        public static bool ClickThrough;
        // مطفية افتراضياً: المستخدم بدو الودجت ظاهر دائماً. لما كانت مفعّلة،
        // أي تطبيق بياخد الشاشة كاملة (فيديو، لعبة، حتى بعض النوافذ العادية)
        // كان بيخفي الودجت فجأة ويرجّعه - وهاد بيبان عشوائي ومزعج.
        public static bool HideFullscreen;

        // يطابق ارتفاع الودجت لارتفاع شريط المهام، وياخد لون الشريط الحقيقي بالعيّنة
        public static bool MatchTaskbar = true;
        public static bool UseTaskbarColor = true;

        public static Lang Language = Lang.English;

        // آخر نتيجة اختبار سرعة - تنعرض بالقائمة لحد ما ينعاد الاختبار
        public static int LastPing;
        public static double LastDown, LastUp;
        public static string LastTest = "";

        // هل انعمل اختصار قائمة ابدأ من قبل؟ (حتى بحث ويندوز يلاقي البرنامج)
        // مرة وحدة بس - إذا المستخدم حذفه يدوياً منحترم قراره وما منعيده لحاله
        public static bool ShortcutDone;

        public static void Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                foreach (string line in File.ReadAllLines(SettingsPath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    switch (k)
                    {
                        case "x":         X = ParseInt(v, X); break;
                        case "y":         Y = ParseInt(v, Y); break;
                        case "locked":    Locked = v == "1"; break;
                        case "snap":      Snap = v == "1"; break;
                        case "topmost":   TopMost = v == "1"; break;
                        case "opacity":   Opacity = Clamp(ParseInt(v, 100), 20, 100); break;
                        case "font":      FontIdx = Clamp(ParseInt(v, 1), 0, 2); break;
                        case "vertical":  Vertical = v == "1"; break;
                        case "unitMode":  UnitMode = Clamp(ParseInt(v, 0), 0, 3); break;
                        case "bits":      Bits = v == "1"; break;
                        case "adapter":   Adapter = v; break;
                        case "colUp":     ColUp = ParseHex(v, DefUp); break;
                        case "colDown":   ColDown = ParseHex(v, DefDown); break;
                        case "colText":   ColText = ParseHex(v, DefText); break;
                        case "colBg":     ColBg = ParseHex(v, DefBg); break;
                        case "through":   ClickThrough = v == "1"; break;
                        case "hideFs":    HideFullscreen = v == "1"; break;
                        case "lastPing":  LastPing = ParseInt(v, 0); break;
                        case "lastDown":  LastDown = ParseDbl(v); break;
                        case "lastUp":    LastUp = ParseDbl(v); break;
                        case "lastTest":  LastTest = v; break;
                        case "shortcut":  ShortcutDone = v == "1"; break;
                        case "matchTb":   MatchTaskbar = v == "1"; break;
                        case "tbColor":   UseTaskbarColor = v == "1"; break;
                        case "language":  Language = v == "ar" ? Lang.Arabic : Lang.English; break;
                    }
                }
            }
            catch { /* ملف تالف = نرجع للافتراضيات بصمت */ }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("x=" + X);
                sb.AppendLine("y=" + Y);
                sb.AppendLine("locked=" + B(Locked));
                sb.AppendLine("snap=" + B(Snap));
                sb.AppendLine("topmost=" + B(TopMost));
                sb.AppendLine("opacity=" + Opacity);
                sb.AppendLine("font=" + FontIdx);
                sb.AppendLine("vertical=" + B(Vertical));
                sb.AppendLine("unitMode=" + UnitMode);
                sb.AppendLine("bits=" + B(Bits));
                sb.AppendLine("adapter=" + Adapter);
                sb.AppendLine("colUp=" + H(ColUp));
                sb.AppendLine("colDown=" + H(ColDown));
                sb.AppendLine("colText=" + H(ColText));
                sb.AppendLine("colBg=" + H(ColBg));
                sb.AppendLine("through=" + B(ClickThrough));
                sb.AppendLine("hideFs=" + B(HideFullscreen));
                sb.AppendLine("lastPing=" + LastPing);
                sb.AppendLine("lastDown=" + LastDown.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("lastUp=" + LastUp.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("lastTest=" + LastTest);
                sb.AppendLine("shortcut=" + B(ShortcutDone));
                sb.AppendLine("matchTb=" + B(MatchTaskbar));
                sb.AppendLine("tbColor=" + B(UseTaskbarColor));
                sb.AppendLine("language=" + (Language == Lang.Arabic ? "ar" : "en"));
                Io.WriteAtomic(SettingsPath, sb.ToString());
            }
            catch { }
        }

        static string B(bool b) { return b ? "1" : "0"; }
        static string H(int c) { return c.ToString("X8", CultureInfo.InvariantCulture); }

        public static int ParseInt(string s, int def)
        {
            int r;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : def;
        }

        static double ParseDbl(string s)
        {
            double r;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : 0;
        }

        static int ParseHex(string s, int def)
        {
            uint r;
            return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
                ? unchecked((int)r) : def;
        }

        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        // بترجّع الشكل والسلوك للافتراضي - بس **ما بتلمس** عدّادات الاستهلاك
        // ولا نتيجة آخر اختبار ولا حالة الاختصار، لأنها بيانات مش إعدادات
        public static void ResetDefaults()
        {
            X = -1; Y = -1;
            Locked = false; Snap = true; TopMost = true;
            Opacity = 100; FontIdx = 1; Vertical = true;
            UnitMode = 0; Bits = false; Adapter = "";
            ColUp = DefUp; ColDown = DefDown; ColText = DefText; ColBg = DefBg;
            ClickThrough = false; HideFullscreen = false;
            MatchTaskbar = true; UseTaskbarColor = true;
            Language = Lang.English;
        }
    }

    // ============================================================
    // عدّادات الاستهلاك التراكمي
    // ============================================================
    static class Usage
    {
        static string Path_ { get { return Path.Combine(Cfg.Dir, "usage.ini"); } }

        public static string DayKey = "", MonthKey = "";
        public static long DayRx, DayTx, MonRx, MonTx, TotRx, TotTx;
        static bool dirty;

        public static void Load()
        {
            try
            {
                if (File.Exists(Path_))
                {
                    foreach (string line in File.ReadAllLines(Path_, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim();
                        string v = line.Substring(eq + 1).Trim();
                        switch (k)
                        {
                            case "dayKey":   DayKey = v; break;
                            case "monthKey": MonthKey = v; break;
                            case "dayRx":    DayRx = L(v); break;
                            case "dayTx":    DayTx = L(v); break;
                            case "monRx":    MonRx = L(v); break;
                            case "monTx":    MonTx = L(v); break;
                            case "totRx":    TotRx = L(v); break;
                            case "totTx":    TotTx = L(v); break;
                        }
                    }
                }
            }
            catch { }
            Roll();
        }

        static long L(string s)
        {
            long r;
            return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : 0;
        }

        // تدوير تلقائي عند تغيّر اليوم أو الشهر
        public static void Roll()
        {
            string d = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string m = DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            if (DayKey != d)   { DayKey = d;   DayRx = 0; DayTx = 0; dirty = true; }
            if (MonthKey != m) { MonthKey = m; MonRx = 0; MonTx = 0; dirty = true; }
        }

        public static void Add(long rx, long tx)
        {
            if (rx == 0 && tx == 0) return;
            Roll();
            DayRx += rx; DayTx += tx;
            MonRx += rx; MonTx += tx;
            TotRx += rx; TotTx += tx;
            dirty = true;
        }

        public static void Reset()
        {
            DayRx = DayTx = MonRx = MonTx = TotRx = TotTx = 0;
            dirty = true;
            Save(true);
        }

        public static void Save(bool force)
        {
            if (!dirty && !force) return;
            try
            {
                Directory.CreateDirectory(Cfg.Dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("dayKey=" + DayKey);
                sb.AppendLine("monthKey=" + MonthKey);
                sb.AppendLine("dayRx=" + DayRx);
                sb.AppendLine("dayTx=" + DayTx);
                sb.AppendLine("monRx=" + MonRx);
                sb.AppendLine("monTx=" + MonTx);
                sb.AppendLine("totRx=" + TotRx);
                sb.AppendLine("totTx=" + TotTx);
                Io.WriteAtomic(Path_, sb.ToString());
                dirty = false;
            }
            catch { }
        }
    }

    // ============================================================
    // مظهر مودرن داكن لقائمة الكلك اليمين - بديل قائمة ويندوز الرمادية الافتراضية
    // ============================================================
    class ModernMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(30, 30, 34); } }
        public override Color MenuBorder { get { return Color.FromArgb(52, 55, 60); } }
        public override Color MenuItemBorder { get { return Color.Transparent; } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(30, 30, 34); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(30, 30, 34); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(30, 30, 34); } }
        public override Color SeparatorDark { get { return Color.FromArgb(50, 53, 58); } }
        public override Color SeparatorLight { get { return Color.FromArgb(0, 0, 0, 0); } }
    }

    class ModernMenuRenderer : ToolStripProfessionalRenderer
    {
        public ModernMenuRenderer() : base(new ModernMenuColors()) { }

        static GraphicsPath RR(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Color.FromArgb(30, 30, 34));
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen p = new Pen(Color.FromArgb(52, 55, 60)))
                e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!(e.Item.Selected || e.Item.Pressed) || !e.Item.Enabled) return;
            Rectangle r = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = RR(r, 5))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(60, 76, 141, 246)))
                e.Graphics.FillPath(b, p);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Color.FromArgb(232, 232, 235) : Color.FromArgb(128, 128, 132);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Rectangle b = e.Item.ContentRectangle;
            using (Pen p = new Pen(Color.FromArgb(50, 53, 58)))
                e.Graphics.DrawLine(p, b.Left + 6, b.Height / 2, b.Right - 6, b.Height / 2);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Enabled ? Color.FromArgb(210, 210, 214) : Color.FromArgb(100, 100, 104);
            base.OnRenderArrow(e);
        }

        // علامة صح حقيقية (خطّين) بدل نقطة - أوضح بكتير إن الخيار مفعّل
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            float cx = r.X + r.Width / 2f;
            float cy = r.Y + r.Height / 2f;
            float s = Math.Min(r.Width, r.Height) * 0.34f;

            using (Pen p = new Pen(Color.FromArgb(96, 165, 250), 2f))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                e.Graphics.DrawLines(p, new PointF[] {
                    new PointF(cx - s,        cy + s * 0.10f),
                    new PointF(cx - s * 0.25f, cy + s * 0.75f),
                    new PointF(cx + s,        cy - s * 0.75f) });
            }
        }
    }

    // ============================================================
    // الودجت
    // ============================================================
    public class Widget : Form
    {
        // --- القياس ---
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        NetworkInterface nic;
        long lastMs, lastRx, lastTx;
        bool haveLast;
        int nicAge = 99, saveAge;
        double upBps, dnBps;

        // --- النص المعروض ---
        string upNum = "0.0", upUnit = "MBps", dnNum = "0.0", dnUnit = "MBps";

        // --- الرسم ---
        Bitmap surf;
        readonly Bitmap scratch = new Bitmap(1, 1);
        Graphics gm;
        Font fNum, fUnit;
        float scale = 1f;
        int padX, padY, arrowW, arrowH, gapAT, gapNU, rowGap, colGap, rowH, radius, shadowM;
        int numW, unitW, cellW, cardW, cardH;
        StringFormat sfRight, sfLeft;

        // --- التفاعل ---
        NotifyIcon tray;
        ContextMenuStrip menu;
        bool dragging;
        Point dragOrigin, dragStartLoc;
        bool hiddenByFullscreen;
        Color tbColor = Color.Empty;   // لون شريط المهام المأخوذ بالعيّنة (مخزّن)
        int lastTbThick = -1;
        Native.WinEventProc fgHookProc;   // لازم يضل حي طول عمر النموذج
        IntPtr fgHook = IntPtr.Zero;
        string trayTip = "";

        static readonly string[] ByteUnits = { "B/s", "KBps", "MBps", "GBps" };
        static readonly string[] BitUnits  = { "bps", "Kbps", "Mbps", "Gbps" };
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public Widget()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = Cfg.TopMost;
            Text = "Net Speed Meter";

            // كل القياسات محسوبة يدوياً بمعامل scale تبعنا. لو تركنا التحجيم
            // التلقائي شغّال، ويندوز بيضرب المقاسات مرة تانية فوق حسابنا
            // (تحجيم مزدوج) - فيطلع الحجم والمكان غلط على شاشة بتكبير 125%.
            AutoScaleMode = AutoScaleMode.None;

            gm = Graphics.FromImage(scratch);
            gm.TextRenderingHint = TextRenderingHint.AntiAlias;

            // مقياس DPI - حتى ما يطلع الودجت صغير على شاشة عالية الدقة
            try { scale = gm.DpiX / 96f; } catch { scale = 1f; }
            if (scale < 1f) scale = 1f;

            sfRight = (StringFormat)StringFormat.GenericTypographic.Clone();
            sfRight.Alignment = StringAlignment.Far;
            sfRight.LineAlignment = StringAlignment.Center;
            sfRight.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;

            sfLeft = (StringFormat)StringFormat.GenericTypographic.Clone();
            sfLeft.Alignment = StringAlignment.Near;
            sfLeft.LineAlignment = StringAlignment.Center;
            sfLeft.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;

            ToolStripManager.Renderer = new ModernMenuRenderer();

            BuildTray();

            timer.Interval = 1000;
            timer.Tick += OnTick;

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            // (كان في تأثير "بيضوء لما الماوس فوقه" هون - انشال: SetWindowPos
            // يلي بيثبّت الودجت فوق كل شي كل ثانية كان عم يخلي ويندوز يعيد
            // تقييم دخول/خروج الماوس بشكل زائف، فيصير الإطار يومض لحاله كل
            // ثانية وكأنه مضغوط - حتى بدون ما المستخدم يحرّك الماوس فعلياً)
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                if (Cfg.ClickThrough) cp.ExStyle |= Native.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        const int WM_DPICHANGED = 0x02E0;

        // رسائل ويندوز لما يتغيّر الثيم أو لون النظام فعلياً - هدول الإشارة
        // الموثوقة الوحيدة إن اللون لازم ينعاد. أي فحص دوري غيرهم بيخلي اللون
        // يتغيّر لحاله بلا سبب حقيقي.
        const int WM_THEMECHANGED = 0x031A;
        const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
        const int WM_SYSCOLORCHANGE = 0x0015;

        // اسم الرسالة اللي بتبعتها النسخة التانية لما المستخدم يضغط الأيقونة مرة تانية
        public const string ShowRequestMessage = "NetSpeedMeter_ShowRequest";
        static readonly uint WM_SHOW_REQUEST = Native.RegisterWindowMessage(ShowRequestMessage);

        // إنقاذ: بترجّع الودجت ظاهر وبمكان صحيح مهما كانت حالته - مخفي، أو
        // مسحوب برّا الشاشة، أو ضايع ورا شريط المهام
        void RescueToView()
        {
            hiddenByFullscreen = false;
            lastTbThick = -1;
            if (!Visible) Show();
            KeepOnScreen();
            if (Cfg.TopMost)
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Render();
        }

        // بتنبعت لما الودجت ينسحب لشاشة تانية بدقة (DPI) مختلفة - لابتوب + شاشة
        // خارجية مثلاً. بدونها الودجت بيضل يرسم بمقياس الشاشة القديمة وويندوز
        // بيمطّط البتمابة تمطيط بدائي، فيطلع مموّه بدل واضح.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SHOW_REQUEST && IsHandleCreated)
            {
                RescueToView();
                return;
            }

            // ويندوز بلّغنا إن الثيم/لون النظام تغيّر فعلاً - هون بس منعيد العيّنة
            if ((m.Msg == WM_THEMECHANGED || m.Msg == WM_DWMCOLORIZATIONCOLORCHANGED
                 || m.Msg == WM_SYSCOLORCHANGE) && IsHandleCreated && Cfg.UseTaskbarColor)
            {
                Color c = SampleTaskbarNow();
                if (c != tbColor) { tbColor = c; Render(); }
            }

            if (m.Msg == WM_DPICHANGED && IsHandleCreated)
            {
                int newDpi = (int)((long)m.WParam & 0xFFFF);   // LOWORD = X-axis DPI
                float newScale = newDpi / 96f;
                if (newScale < 1f) newScale = 1f;

                if (Math.Abs(newScale - scale) > 0.01f)
                {
                    scale = newScale;
                    Relayout();
                }

                // ويندوز بيقترح مستطيل جديد يحافظ على مكانها النسبي تحت المؤشر -
                // منحترم الموقع المقترح، بس منخلي القياس من حسابنا احنا (Relayout)
                try
                {
                    Native.RECT r = (Native.RECT)Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));
                    Location = new Point(r.Left, r.Top);
                    Cfg.X = r.Left; Cfg.Y = r.Top; Cfg.Save();
                }
                catch { }

                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            // إغلاق ويندوز ما بيضمن استدعاء OnFormClosing - بدون هالسطر
            // بيضيع استهلاك آخر لحد دقيقة عند كل إطفاء للجهاز
            SystemEvents.SessionEnding += OnSessionEnding;

            // منحافظ على مرجع الدالة حياً - وإلا الـ GC ممكن يجمعها وويندوز
            // بيستدعي دالة راحت، وهاد بيسبّب كراش
            fgHookProc = OnForegroundChanged;
            fgHook = Native.SetWinEventHook(
                Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, fgHookProc, 0, 0, Native.WINEVENT_OUTOFCONTEXT);

            Relayout();
            PlaceAtStart();

            // أخذ عيّنة اللون **بعد** ما ينضبط الحجم والمكان - لأن العيّنة بدها
            // تعرف مستطيل الودجت حتى تتجاهل بكسلاته وما تصوّر حالها
            if (Cfg.UseTaskbarColor)
            {
                Color c = SampleTaskbarNow();
                if (c != tbColor) { tbColor = c; Render(); }
            }

            lastMs = clock.ElapsedMilliseconds;
            timer.Start();
        }

        // ========================================================
        // التخطيط والرسم
        // ========================================================
        void BuildFonts()
        {
            float[] sizes = { 8.0f, 9.5f, 12.0f };
            float s = sizes[Cfg.FontIdx] * scale;
            if (fNum != null) fNum.Dispose();
            if (fUnit != null) fUnit.Dispose();
            fNum = new Font("Segoe UI", s, FontStyle.Bold, GraphicsUnit.Point);
            fUnit = new Font("Segoe UI", s * 0.92f, FontStyle.Regular, GraphicsUnit.Point);
        }

        int M(float px) { return (int)Math.Round(px * scale); }

        // ========================================================
        // شريط المهام: سماكته ولونه الحقيقي
        // ========================================================

        // سماكة الشريط = الفرق بين حدود الشاشة ومساحة العمل
        static int TaskbarThickness()
        {
            try
            {
                Rectangle b = Screen.PrimaryScreen.Bounds;
                Rectangle w = Screen.PrimaryScreen.WorkingArea;
                int t = Math.Max(b.Height - w.Height, b.Width - w.Width);
                // شريط مخفي تلقائياً بيعطي صفر - منرجع مقاس ويندوز 11 الافتراضي
                return (t >= 24 && t <= 120) ? t : 48;
            }
            catch { return 48; }
        }

        // ناخد اللون بالعيّنة من بكسلات الشريط نفسه بدل ما نخمّنه من إعدادات الثيم -
        // هيك بيطلع مطابق تماماً لِلّي شايفه المستخدم، بما فيه تأثير الشفافية والضبابية.
        // منسحب عدة نقاط ومناخد اللون الأكثر تكراراً حتى ما تخربطنا أيقونات الشريط.
        // selfRect: مستطيل الودجت نفسه - لازم نتجاهل أي نقطة واقعة جوّاته، وإلا
        // بيصوّر حاله (الودجت قاعد فوق الشريط) فيصير اللون يتغذّى على نفسه ويرقص.
        static Color SampleTaskbarColor(Color fallback, Rectangle selfRect)
        {
            IntPtr dc = IntPtr.Zero;
            try
            {
                IntPtr tb = Native.FindWindow("Shell_TrayWnd", null);
                if (tb == IntPtr.Zero) return fallback;
                Native.RECT r;
                if (!Native.GetWindowRect(tb, out r)) return fallback;

                int w = r.Right - r.Left, h = r.Bottom - r.Top;
                if (w <= 4 || h <= 4) return fallback;

                dc = Native.GetDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) return fallback;

                Dictionary<int, int> hits = new Dictionary<int, int>();
                int taken = 0;
                int y = r.Top + h / 2;
                for (int i = 1; i <= 24; i++)
                {
                    int x = r.Left + (w * i) / 25;
                    if (selfRect.Width > 0 && selfRect.Contains(x, y)) continue;  // تجاهل نفسه
                    uint c = Native.GetPixel(dc, x, y);
                    if (c == 0xFFFFFFFF) continue;             // CLR_INVALID
                    int key = (int)(c & 0x00FFFFFF);
                    int n;
                    hits[key] = hits.TryGetValue(key, out n) ? n + 1 : 1;
                    taken++;
                }
                if (taken < 6) return fallback;                // عيّنة ضعيفة = ما منغيّر

                int best = 0, bestN = -1;
                foreach (KeyValuePair<int, int> kv in hits)
                    if (kv.Value > bestN) { bestN = kv.Value; best = kv.Key; }

                // لازم أغلبية واضحة (نص العيّنة على الأقل) تمثّل خلفية الشريط فعلاً.
                // بدون هالشرط، أيقونة أو تحويم ماوس بيقدروا يفوزوا بالتصويت ويغيّروا اللون.
                if (bestN * 2 < taken) return fallback;

                // COLORREF بترتيب 0x00BBGGRR
                int rr = best & 0xFF, gg = (best >> 8) & 0xFF, bb = (best >> 16) & 0xFF;
                return Color.FromArgb(fallback.A, rr, gg, bb);
            }
            catch { return fallback; }
            finally { if (dc != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, dc); }
        }

        // غلاف بيمرّر مستطيل الودجت الحالي تلقائياً
        Color SampleTaskbarNow()
        {
            Rectangle self = IsHandleCreated ? new Rectangle(Location, Size) : Rectangle.Empty;
            return SampleTaskbarColor(Color.FromArgb(Cfg.ColBg), self);
        }

        // نحتار أكبر خط بيضبط ضمن ارتفاع الصف المطلوب
        void PickFontForRow(int wantRowH)
        {
            float chosen = 5.5f;
            for (float s = 12f; s >= 5.5f; s -= 0.25f)
            {
                using (Font f = new Font("Segoe UI", s * scale, FontStyle.Bold, GraphicsUnit.Point))
                {
                    if (f.GetHeight(gm) <= wantRowH) { chosen = s; break; }
                }
            }
            if (fNum != null) fNum.Dispose();
            if (fUnit != null) fUnit.Dispose();
            fNum = new Font("Segoe UI", chosen * scale, FontStyle.Bold, GraphicsUnit.Point);
            fUnit = new Font("Segoe UI", chosen * scale * 0.88f, FontStyle.Regular, GraphicsUnit.Point);
        }

        float TextW(string s, Font f)
        {
            return gm.MeasureString(s, f, new SizeF(2000, 200), StringFormat.GenericTypographic).Width;
        }

        // يُستدعى فقط لما تتغيّر إعدادة تخصّ الشكل - مش كل ثانية
        void Relayout()
        {
            bool compact = Cfg.MatchTaskbar;
            int rows = Cfg.Vertical ? 2 : 1;

            if (compact)
            {
                // مقاسات ضيّقة حتى نلحق ارتفاع شريط المهام
                padX = M(8); padY = M(3);
                gapAT = M(4); gapNU = M(3); rowGap = M(2); colGap = M(10);
                radius = M(6); shadowM = M(3);

                // نوزّع الارتفاع المتاح على الصفوف، وناخد أكبر خط بيضبط فيه
                int targetH = TaskbarThickness();
                int overhead = shadowM * 2 + padY * 2 + (Cfg.Vertical ? rowGap : 0);
                int avail = targetH - overhead;
                rowH = Math.Max(M(9), avail / rows);
                PickFontForRow(rowH);
                arrowW = M(6);
                arrowH = (int)Math.Round(rowH * 0.80f);
            }
            else
            {
                BuildFonts();
                padX = M(12); padY = M(9);
                gapAT = M(7); gapNU = M(5); rowGap = M(7); colGap = M(16);
                radius = M(11); shadowM = M(6);
                arrowW = M(8); arrowH = (int)Math.Round(fNum.GetHeight(gm) * 0.80f);
                rowH = Math.Max((int)Math.Ceiling(fNum.GetHeight(gm)), arrowH);
            }

            // عرض ثابت للأرقام - حتى ما ترجف الأرقام وهي عم تتغيّر
            numW = (int)Math.Ceiling(TextW("888.8", fNum));

            // أعرض وحدة ممكنة بالوضع الحالي
            string[] units = Cfg.Bits ? BitUnits : ByteUnits;
            float w = 0;
            if (Cfg.UnitMode == 0)
            {
                foreach (string u in units) { float t = TextW(u, fUnit); if (t > w) w = t; }
            }
            else w = TextW(units[Cfg.UnitMode], fUnit);
            unitW = (int)Math.Ceiling(w);

            cellW = arrowW + gapAT + numW + gapNU + unitW;

            if (Cfg.Vertical)
            {
                cardW = padX * 2 + cellW;
                cardH = padY * 2 + rowH * 2 + rowGap;
            }
            else
            {
                cardW = padX * 2 + cellW * 2 + colGap;
                cardH = padY * 2 + rowH;
            }

            int wTotal = cardW + shadowM * 2;
            int hTotal = cardH + shadowM * 2;

            if (Size.Width != wTotal || Size.Height != hTotal)
            {
                Size = new Size(wTotal, hTotal);
                if (surf != null) { surf.Dispose(); surf = null; }
            }
            if (surf == null) surf = new Bitmap(wTotal, hTotal, PixelFormat.Format32bppArgb);

            Render();
        }

        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // سهم مودرن: رأس مثلث ناعم + ذيل بأطراف دائرية بدل مستطيل حاد
        static void DrawArrow(Graphics g, Brush b, float x, float y, float w, float h, bool up)
        {
            float headH = h * 0.50f;
            float stemW = w * 0.30f;
            // نسبة من ارتفاع السهم نفسه، مش رقم بكسلات ثابت - وإلا على شاشة عالية
            // الدقة (DPI أكبر) بيصير الرأس والذيل أكبر بكتير من هالفجوة الثابتة
            // وبيبين خط رفيع فاصل بينهم بدل ما يلتحموا بسلاسة
            float seam = Math.Max(1f, h * 0.035f);
            float stemH = h - headH + seam;
            float sx = x + (w - stemW) / 2f;

            using (GraphicsPath p = new GraphicsPath())
            {
                if (up)
                {
                    p.AddPolygon(new PointF[] {
                        new PointF(x + w / 2f, y),
                        new PointF(x + w,      y + headH),
                        new PointF(x,          y + headH) });
                    g.FillPath(b, p);
                }
                else
                {
                    p.AddPolygon(new PointF[] {
                        new PointF(x + w / 2f, y + h),
                        new PointF(x + w,      y + h - headH),
                        new PointF(x,          y + h - headH) });
                    g.FillPath(b, p);
                }

                float stemY = up ? y + headH - seam : y;
                RectangleF stemRect = new RectangleF(sx, stemY, stemW, Math.Max(1f, stemH));
                using (GraphicsPath stem = RoundRect(stemRect, stemW / 2.2f))
                    g.FillPath(b, stem);
            }
        }

        void DrawCell(Graphics g, float x, float y, Brush arrow, Brush text, Brush unitBrush,
                      bool up, string num, string unit)
        {
            DrawArrow(g, arrow, x, y + (rowH - arrowH) / 2f, arrowW, arrowH, up);
            float tx = x + arrowW + gapAT;
            g.DrawString(num, fNum, text, new RectangleF(tx, y, numW, rowH), sfRight);
            g.DrawString(unit, fUnit, unitBrush,
                new RectangleF(tx + numW + gapNU, y, unitW + M(4), rowH), sfLeft);
        }

        void Render()
        {
            if (!IsHandleCreated || surf == null) return;

            Color bg   = Color.FromArgb(Cfg.ColBg);
            if (Cfg.UseTaskbarColor && tbColor != Color.Empty) bg = tbColor;
            Color cUp  = Color.FromArgb(Cfg.ColUp);
            Color cDn  = Color.FromArgb(Cfg.ColDown);
            Color cTxt = Color.FromArgb(Cfg.ColText);

            // ضمان القراءة: الخلفية بتتبع لون شريط المهام وبتقدر تصير فاتحة
            // (ثيم فاتح) بينما ألوان النص محفوظة ثابتة. بدون هالسطور بيصير
            // نص أبيض على خلفية بيضاء = الودجت بيختفي. منعدّل ألوان **العرض**
            // فقط - اختيار المستخدم المحفوظ بـ Cfg ما بينمسّ.
            cTxt = EnsureContrast(cTxt, bg, 4.5);   // نص: معيار WCAG AA
            cUp  = EnsureContrast(cUp,  bg, 3.0);   // أسهم: معيار العناصر الرسومية
            cDn  = EnsureContrast(cDn,  bg, 3.0);

            bool darkBg = Luminance(bg) < 0.25;

            using (Graphics g = Graphics.FromImage(surf))
            {
                g.CompositingMode = CompositingMode.SourceOver;
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;

                // لما الودجت ياخد لون شريط المهام، بدنا نفس اللمسة الفعلية: تعبئة
                // مسطّحة بلا ظل ولا تدرّج ولا لمعان، وإطار من نفس درجة اللون نفسه
                // مش أبيض/أسود عام - هيك بيبين وكأنه جزء من الشريط فعلاً مش بطاقة
                // عايمة فوقه بهوية بصرية مختلفة.
                bool flat = Cfg.UseTaskbarColor && tbColor != Color.Empty;

                // ظل ناعم متدرّج خلف البطاقة - يعطيها عمق بدل الشكل المسطّح.
                // لما يكون بلون الشريط، الودجت غالباً قاعد فوق نفس لون الشريط
                // بالضبط، فالظل بيبين كحلقة غامقة غريبة بدل ما يختفي - بنشيله
                // ونخلي الاندماج كامل.
                if (!flat)
                {
                    for (int i = 4; i >= 1; i--)
                    {
                        float grow = i * (shadowM / 4f);
                        RectangleF sr = new RectangleF(shadowM - grow, shadowM - grow + M(1.5f),
                            cardW + grow * 2, cardH + grow * 2);
                        using (GraphicsPath sp = RoundRect(sr, radius + grow * 0.5f))
                        using (SolidBrush sb = new SolidBrush(Color.FromArgb(9, 0, 0, 0)))
                            g.FillPath(sb, sp);
                    }
                }

                RectangleF box = new RectangleF(shadowM + 0.5f, shadowM + 0.5f, cardW - 1f, cardH - 1f);
                using (GraphicsPath p = RoundRect(box, radius))
                {
                    if (flat)
                    {
                        using (SolidBrush fb = new SolidBrush(bg))
                            g.FillPath(fb, p);
                    }
                    else
                    {
                        // تدرّج لوني خفيف من الأعلى للأسفل - يكسر التسطيح ويعطي إحساس زجاجي
                        Color top = darkBg ? Lift(bg, 0.10f) : Lift(bg, -0.06f);
                        using (LinearGradientBrush gb = new LinearGradientBrush(
                            new RectangleF(box.X, box.Y, box.Width, box.Height + 1),
                            top, bg, LinearGradientMode.Vertical))
                            g.FillPath(gb, p);
                    }

                    // حدّ خارجي رفيع وثابت - نفس درجة اللون نفسه لما يكون بلون
                    // الشريط (بالكاد محسوس، بس كافي يفصل الزوايا الدائرية)،
                    // وإلا حدّ عام أفتح/أغمق من الخلفية حسب سطوعها
                    Color edgeCol = flat ? Lift(bg, darkBg ? 0.16f : -0.16f)
                                          : Color.FromArgb(255, darkBg ? 255 : 0, darkBg ? 255 : 0, darkBg ? 255 : 0);
                    int edgeA = flat ? 90 : Math.Min((int)bg.A, 60);
                    using (Pen edge = new Pen(Color.FromArgb(edgeA, edgeCol.R, edgeCol.G, edgeCol.B), 1f))
                        g.DrawPath(edge, p);
                }

                if (!flat)
                {
                    // خط لمعان رفيع أعلى البطاقة - حافة "زجاجية" مودرن (بس لما تكون
                    // بطاقة عايمة بألوان مخصّصة؛ شريط المهام نفسه مسطّح بلا لمعان)
                    using (Pen hl = new Pen(Color.FromArgb(darkBg ? 26 : 40, 255, 255, 255), 1f))
                        g.DrawLine(hl, box.X + radius * 0.5f, box.Y + 1f, box.Right - radius * 0.5f, box.Y + 1f);
                }

                using (SolidBrush bUp = new SolidBrush(cUp))
                using (SolidBrush bDn = new SolidBrush(cDn))
                using (SolidBrush bTx = new SolidBrush(cTxt))
                using (SolidBrush bUn = new SolidBrush(Color.FromArgb(
                        (int)(cTxt.A * 0.62f), cTxt.R, cTxt.G, cTxt.B)))
                {
                    int ox = shadowM, oy = shadowM;
                    if (Cfg.Vertical)
                    {
                        DrawCell(g, ox + padX, oy + padY, bUp, bTx, bUn, true, upNum, upUnit);
                        DrawCell(g, ox + padX, oy + padY + rowH + rowGap, bDn, bTx, bUn, false, dnNum, dnUnit);

                        // فاصل رفيع بين الصفّين - يرتّب البطاقة كصفّي إحصاء واضحين
                        using (Pen sep = new Pen(Color.FromArgb(darkBg ? 16 : 22, cTxt.R, cTxt.G, cTxt.B), 1f))
                            g.DrawLine(sep, ox + padX, oy + padY + rowH + rowGap / 2f,
                                            ox + cardW - padX, oy + padY + rowH + rowGap / 2f);
                    }
                    else
                    {
                        DrawCell(g, ox + padX, oy + padY, bUp, bTx, bUn, true, upNum, upUnit);
                        DrawCell(g, ox + padX + cellW + colGap, oy + padY, bDn, bTx, bUn, false, dnNum, dnUnit);

                        using (Pen sep = new Pen(Color.FromArgb(darkBg ? 16 : 22, cTxt.R, cTxt.G, cTxt.B), 1f))
                            g.DrawLine(sep, ox + padX + cellW + colGap / 2f, oy + padY + M(3),
                                            ox + padX + cellW + colGap / 2f, oy + cardH - padY - M(3));
                    }
                }
            }

            Push();
        }

        static Color Lift(Color c, float f)
        {
            int r = Cfg.Clamp((int)(c.R + 255 * f), 0, 255);
            int g = Cfg.Clamp((int)(c.G + 255 * f), 0, 255);
            int b = Cfg.Clamp((int)(c.B + 255 * f), 0, 255);
            return Color.FromArgb(c.A, r, g, b);
        }

        // ================================================================
        // ضمان قابلية القراءة - يمنع نهائياً حالة "نص أبيض على خلفية بيضاء"
        // ================================================================
        // الخلفية بتتغيّر مع لون شريط المهام (فاتح بالثيم الفاتح، غامق بالداكن)،
        // بينما لون النص مخزّن ثابت. بدون هالفحص، الثيم الفاتح كان بيخلي نص
        // أبيض فوق خلفية بيضاء = الودجت بيختفي تماماً. هون منقيس التباين بمعادلة
        // WCAG المعيارية، ولو كان ضعيف منعدّل لون العرض (بدون ما نلمس اختيار
        // المستخدم المحفوظ) لحدّ ما يصير مقروء.

        static double SrgbChannel(int v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        public static double Luminance(Color c)
        {
            return 0.2126 * SrgbChannel(c.R) + 0.7152 * SrgbChannel(c.G) + 0.0722 * SrgbChannel(c.B);
        }

        // نسبة التباين حسب WCAG: من 1 (نفس اللون) لـ 21 (أسود مقابل أبيض)
        public static double ContrastRatio(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
            return (hi + 0.05) / (lo + 0.05);
        }

        static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(a.A,
                Cfg.Clamp((int)Math.Round(a.R + (b.R - a.R) * t), 0, 255),
                Cfg.Clamp((int)Math.Round(a.G + (b.G - a.G) * t), 0, 255),
                Cfg.Clamp((int)Math.Round(a.B + (b.B - a.B) * t), 0, 255));
        }

        // بترجّع اللون نفسه إذا كان مقروء، وإلا بتدفعه نحو الأسود أو الأبيض
        // (حسب سطوع الخلفية) بأصغر مقدار بيحقق النسبة المطلوبة - هيك بنحافظ
        // على درجة اللون الأصلية قدر الإمكان بدل ما نستبدله بالكامل.
        public static Color EnsureContrast(Color fg, Color bg, double minRatio)
        {
            if (ContrastRatio(fg, bg) >= minRatio) return fg;

            // مهم: منجرّب **الاتجاهين** (نحو الأسود ونحو الأبيض) ومناخد الأفضل.
            // اختيار الاتجاه بعتبة ثابتة كان بيفشل مع الخلفيات متوسطة السطوع
            // (أزرق/أخضر متوسط): بيدفع نحو الأبيض ويوصل 3.0 بس، بينما الأسود
            // كان بيعطي تباين أعلى بكتير. الاختبارات مسكت هالحالة على 1113 لون.
            Color bestOverall = fg;
            double bestRatio = ContrastRatio(fg, bg);

            Color[] targets = new Color[] { Color.Black, Color.White };
            foreach (Color target in targets)
            {
                for (int i = 1; i <= 24; i++)
                {
                    Color cand = Mix(fg, target, i / 24.0);
                    double ratio = ContrastRatio(cand, bg);

                    // أول ما نحقق المطلوب منرجّعه فوراً - أقل انحراف عن اللون الأصلي
                    if (ratio >= minRatio) return cand;

                    if (ratio > bestRatio) { bestRatio = ratio; bestOverall = cand; }
                }
            }
            // ما في اتجاه وصل للمطلوب (خلفية متوسطة جداً) - مناخد أعلى تباين متاح
            return bestOverall;
        }

        void Push()
        {
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBmp = IntPtr.Zero, hOld = IntPtr.Zero;
            try
            {
                hBmp = surf.GetHbitmap(Color.FromArgb(0));
                hOld = Native.SelectObject(memDc, hBmp);

                Native.SIZE size = new Native.SIZE(surf.Width, surf.Height);
                Native.POINT src = new Native.POINT(0, 0);
                Native.BLENDFUNCTION bf = new Native.BLENDFUNCTION();
                bf.BlendOp = Native.AC_SRC_OVER;
                bf.BlendFlags = 0;
                bf.SourceConstantAlpha = (byte)Cfg.Clamp(Cfg.Opacity * 255 / 100, 20, 255);
                bf.AlphaFormat = Native.AC_SRC_ALPHA;

                Native.UpdateLayeredWindow(Handle, screenDc, IntPtr.Zero, ref size,
                    memDc, ref src, 0, ref bf, Native.ULW_ALPHA);
            }
            catch { }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, screenDc);
                if (hBmp != IntPtr.Zero) { Native.SelectObject(memDc, hOld); Native.DeleteObject(hBmp); }
                Native.DeleteDC(memDc);
            }
        }

        // ========================================================
        // القياس كل ثانية
        // ========================================================
        void OnTick(object sender, EventArgs e)
        {
            // بعض النوافذ (شريط المهام، نوافذ ألعاب) بتقدر تسرق القمة حتى من نافذة TopMost
            // فبنعيد تثبيته فوق كل شي كل ثانية - عملية رخيصة جداً على المعالج
            if (Cfg.TopMost && IsHandleCreated)
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

            long now = clock.ElapsedMilliseconds;
            double dt = (now - lastMs) / 1000.0;
            lastMs = now;
            if (dt <= 0.05) dt = 1.0;

            // شريط المهام بيقدر يتغيّر سماكته بدون تغيير دقة الشاشة (تصغير الأزرار،
            // الإخفاء التلقائي) - فمنراقبه ومنعيد التخطيط لما يتغيّر فعلاً فقط
            if (Cfg.MatchTaskbar)
            {
                int t = TaskbarThickness();
                if (t != lastTbThick) { lastTbThick = t; Relayout(); }
            }

            // ملاحظة: ما عاد في إعادة أخذ عيّنة للّون كل كم ثانية. كانت بتخلي
            // اللون "يرقص" لحاله لأن الودجت قاعد فوق الشريط فبيصوّر حاله وبيصوّر
            // الأيقونات المتحرّكة. صار أخذ العيّنة يصير مرة عند الإقلاع، وبعدين
            // بس لما ويندوز يبلّغنا فعلياً إنه الثيم أو لون النظام تغيّر
            // (WM_DWMCOLORIZATIONCOLORCHANGED / WM_THEMECHANGED) - شوف WndProc.

            // نعيد اختيار الكرت كل 10 ثواني فقط - مش كل نبضة
            if (++nicAge >= 10 || nic == null)
            {
                nicAge = 0;
                NetworkInterface pick = PickNic();
                if (pick == null || nic == null || pick.Id != nic.Id) { nic = pick; haveLast = false; }
            }

            long rx = 0, tx = 0; bool ok = false;
            if (nic != null)
            {
                try
                {
                    IPv4InterfaceStatistics st = nic.GetIPv4Statistics();
                    rx = st.BytesReceived; tx = st.BytesSent; ok = true;
                }
                catch { nic = null; haveLast = false; }
            }

            if (ok && haveLast)
            {
                long drx = rx - lastRx; if (drx < 0) drx = 0;   // العدّاد بيتصفّر لما يعيد الكرت التشغيل
                long dtx = tx - lastTx; if (dtx < 0) dtx = 0;
                dnBps = drx / dt;
                upBps = tx >= lastTx ? dtx / dt : 0;
                Usage.Add(drx, dtx);
            }
            else { dnBps = 0; upBps = 0; }

            if (ok) { lastRx = rx; lastTx = tx; haveLast = true; }

            string un, uu, dn2, du;
            FormatSpeed(upBps, out un, out uu);
            FormatSpeed(dnBps, out dn2, out du);

            bool changed = un != upNum || uu != upUnit || dn2 != dnNum || du != dnUnit;
            upNum = un; upUnit = uu; dnNum = dn2; dnUnit = du;

            ApplyFullscreenRule();

            // نعيد الرسم فقط لما يتغيّر النص فعلاً
            if (changed && Visible) Render();

            // تلميح أيقونة التراي - بيفيد خصوصاً لما يكون الودجت مخفي
            if (changed && tray != null)
            {
                string tip = "↓ " + dnNum + " " + dnUnit + "   ↑ " + upNum + " " + upUnit
                           + "\n" + Tr.Get(TrId.TrayToday) + FmtBytes(Usage.DayRx + Usage.DayTx);
                if (tip.Length > 62) tip = tip.Substring(0, 62);   // ويندوز بيرفض أطول من 63
                if (tip != trayTip) { trayTip = tip; try { tray.Text = tip; } catch { } }
            }

            if (++saveAge >= 60) { saveAge = 0; Usage.Save(false); }
        }

        NetworkInterface PickNic()
        {
            NetworkInterface best = null;
            long bestBytes = -1;
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    if (Cfg.Adapter.Length > 0)
                    {
                        if (ni.Id == Cfg.Adapter) return ni;
                        continue;
                    }
                    long b;
                    try
                    {
                        IPv4InterfaceStatistics s = ni.GetIPv4Statistics();
                        b = s.BytesReceived + s.BytesSent;
                    }
                    catch { continue; }
                    if (b > bestBytes) { bestBytes = b; best = ni; }
                }
            }
            catch { }
            return best;
        }

        void FormatSpeed(double bytesPerSec, out string num, out string unit)
        {
            FormatSpeed(bytesPerSec, Cfg.UnitMode, Cfg.Bits, out num, out unit);
        }

        // نسخة نقية (بلا اعتماد على حالة البرنامج) حتى تنفحص آلياً بالاختبارات
        public static void FormatSpeed(double bytesPerSec, int unitMode, bool bits,
                                       out string num, out string unit)
        {
            double v = bits ? bytesPerSec * 8.0 : bytesPerSec;
            double base_ = bits ? 1000.0 : 1024.0;   // البِتات عشرية، البايتات ثنائية
            string[] units = bits ? BitUnits : ByteUnits;

            int idx;
            if (unitMode == 0)
            {
                idx = 0;
                while (v >= base_ && idx < 3) { v /= base_; idx++; }
            }
            else
            {
                idx = Cfg.Clamp(unitMode, 0, 3);
                for (int i = 0; i < idx; i++) v /= base_;
            }
            unit = units[idx];
            if (v >= 1000) v = 999.9;
            num = (v >= 100)
                ? v.ToString("0", CultureInfo.InvariantCulture)
                : v.ToString("0.0", CultureInfo.InvariantCulture);
        }

        public static string FmtBytes(long b)
        {
            double v = b;
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (v >= 1024 && i < 4) { v /= 1024; i++; }
            return (v >= 100 ? v.ToString("0", CultureInfo.InvariantCulture)
                             : v.ToString("0.0", CultureInfo.InvariantCulture)) + " " + u[i];
        }

        // ========================================================
        // ما يأثر على أي تطبيق آخر
        // ========================================================
        void ApplyFullscreenRule()
        {
            bool fs = Cfg.HideFullscreen && IsFullscreenForeground();
            if (fs && Visible) { hiddenByFullscreen = true; Hide(); }
            else if (!fs && hiddenByFullscreen) { hiddenByFullscreen = false; Show(); Render(); }

            // شبكة أمان: ما في طريقة يختفي فيها الودجت غير قاعدة ملء الشاشة
            // (وهي مطفية افتراضياً). فلو صار مخفي لأي سبب تاني - تطبيق تاني،
            // خلل عابر بالنظام، أي شي - منرجّعه لحاله خلال ثانية.
            if (!hiddenByFullscreen && !Visible)
            {
                Show();
                Render();
            }
        }

        // نفس الاستفسار يلي بيستعمله شريط المهام نفسه ليقرّر يخبّي الإشعارات أو لأ.
        // أدق بكتير من مقارنة أبعاد النافذة بأبعاد الشاشة يدوياً - هيك ما بينخدع بنافذة
        // مكبّرة عادية (IDE، متصفح) بيصادف إنها تغطي الشاشة كلها بدون ما تكون فعلاً
        // لعبة أو فيديو بوضع ملء الشاشة الحقيقي.
        bool IsFullscreenForeground()
        {
            try
            {
                Native.QUNS state;
                if (Native.SHQueryUserNotificationState(out state) != 0) return false;
                return state == Native.QUNS.RUNNING_D3D_FULL_SCREEN || state == Native.QUNS.PRESENTATION_MODE;
            }
            catch { return false; }
        }

        void ApplyClickThrough()
        {
            if (!IsHandleCreated) return;
            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            if (Cfg.ClickThrough) ex |= Native.WS_EX_TRANSPARENT;
            else ex &= ~Native.WS_EX_TRANSPARENT;
            Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
        }

        // ========================================================
        // المكان والسحب
        // ========================================================
        void PlaceAtStart()
        {
            if (Cfg.X < 0 || Cfg.Y < 0 || !OnAnyScreen(new Point(Cfg.X, Cfg.Y)))
            {
                MoveToCorner(2); // أسفل اليمين افتراضياً
                return;
            }
            Location = new Point(Cfg.X, Cfg.Y);

            // ما بيكفي نتأكد إنه "لامس الشاشة" - لازم نصحّح المكان كمان. بدون
            // هالسطر، ودجت محفوظ نصّه برّا الشاشة أو مدفون ورا شريط المهام
            // بيضل هيك للأبد، وإذا كان المكان مقفول كمان بيصير مستحيل توصله.
            KeepOnScreen();
        }

        bool OnAnyScreen(Point p)
        {
            Rectangle probe = new Rectangle(p, new Size(Math.Max(Width, 10), Math.Max(Height, 10)));
            foreach (Screen s in Screen.AllScreens)
                if (s.Bounds.IntersectsWith(probe)) return true;   // Bounds مش WorkingArea:
            return false;                                          // منطقة الشريط محسوبة شاشة
        }

        // تغيّرت دقة الشاشة أو انفصلت شاشة - لازم نتأكد إن الودجت لسا ظاهر
        // ونعيد التخطيط لأن سماكة شريط المهام بتتغيّر مع الدقة
        void OnDisplayChanged(object sender, EventArgs e)
        {
            if (!IsHandleCreated || IsDisposed) return;
            try
            {
                lastTbThick = -1;              // يجبر إعادة التخطيط بالنبضة الجاية
                if (Cfg.MatchTaskbar) Relayout();
                KeepOnScreen();
            }
            catch { }
        }

        // بينستدعى فوراً أي ما نافذة تانية تصير بالمقدمة - منثبّت الودجت فوقها
        // مباشرة بدل ما ننتظر نبضة الثانية الجاية. هون سبب اختفاء الودجت
        // المؤقت لما تفتح برنامج جديد.
        void OnForegroundChanged(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild,
                                  uint thread, uint time)
        {
            if (!Cfg.TopMost || !IsHandleCreated || IsDisposed) return;
            try
            {
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
            catch { }
        }

        void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            try { Usage.Save(true); Cfg.Save(); } catch { }
        }

        // بتتأكد بس إن الودجت مش ضايع برّا الشاشة. **بتستعمل حدود الشاشة الكاملة
        // مش مساحة العمل** - لأن وضعه فوق شريط المهام مقصود من المستخدم، فما
        // بيصح نشدّه لفوق كل مرة يشتغل فيها البرنامج.
        void KeepOnScreen()
        {
            if (!OnAnyScreen(Location)) { MoveToCorner(2); return; }
            Rectangle b = Screen.FromRectangle(new Rectangle(Location, Size)).Bounds;
            int x = Cfg.Clamp(Location.X, b.Left, Math.Max(b.Left, b.Right - Width));
            int y = Cfg.Clamp(Location.Y, b.Top, Math.Max(b.Top, b.Bottom - Height));
            if (x != Location.X || y != Location.Y)
            {
                Location = new Point(x, y);
                Cfg.X = x; Cfg.Y = y; Cfg.Save();
            }
        }

        // 0 أعلى يسار / 1 أعلى يمين / 2 أسفل يمين / 3 أسفل يسار
        void MoveToCorner(int corner) { MoveToCorner(corner, Screen.PrimaryScreen); }

        void MoveToCorner(int corner, Screen sc)
        {
            Rectangle a = sc.WorkingArea;
            int m = M(12);
            int x = (corner == 0 || corner == 3) ? a.Left + m : a.Right - Width - m;
            int y = (corner == 0 || corner == 1) ? a.Top + m : a.Bottom - Height - m;
            Location = new Point(x, y);
            Cfg.X = x; Cfg.Y = y; Cfg.Save();
        }

        void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || Cfg.Locked) return;
            dragging = true;
            dragOrigin = Cursor.Position;
            dragStartLoc = Location;
        }

        void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            Point c = Cursor.Position;
            int nx = dragStartLoc.X + (c.X - dragOrigin.X);
            int ny = dragStartLoc.Y + (c.Y - dragOrigin.Y);

            Screen scr = Screen.FromPoint(c);
            Rectangle a = scr.WorkingArea;

            if (Cfg.Snap)
            {
                int t = M(14), m = M(12);
                if (Math.Abs(nx - a.Left) < t) nx = a.Left + m;
                if (Math.Abs(a.Right - (nx + Width)) < t) nx = a.Right - Width - m;
                if (Math.Abs(ny - a.Top) < t) ny = a.Top + m;
                if (Math.Abs(a.Bottom - (ny + Height)) < t) ny = a.Bottom - Height - m;
            }

            // التقييد على حدود الشاشة الكاملة (Bounds) مش على مساحة العمل -
            // مساحة العمل بتستثني شريط المهام، والمستخدم بيحط الودجت فوق الشريط
            // عن قصد (ولهيك هو أصلاً بنفس ارتفاع الشريط ولونه). التقييد هون
            // بس بيمنع ضياعه بالكامل برّا الشاشة، مش بيمنع وضعه على الشريط.
            Rectangle b = scr.Bounds;
            nx = Cfg.Clamp(nx, b.Left, Math.Max(b.Left, b.Right - Width));
            ny = Cfg.Clamp(ny, b.Top, Math.Max(b.Top, b.Bottom - Height));

            Location = new Point(nx, ny);
        }

        void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && dragging)
            {
                dragging = false;
                Cfg.X = Location.X; Cfg.Y = Location.Y;
                Cfg.Save();
                return;
            }
            if (e.Button == MouseButtons.Right) ShowMenuAt(Cursor.Position);
        }

        // ========================================================
        // أيقونة التراي
        // ========================================================
        void BuildTray()
        {
            tray = new NotifyIcon();
            tray.Text = "Net Speed Meter";
            tray.Icon = MakeIcon();
            tray.Visible = true;
            tray.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Right) ShowMenuAt(Cursor.Position);
                else RescueToView();   // كلك شمال = تأكيد إنه ظاهر بمكان صحيح
            };
        }

        Icon MakeIcon()
        {
            using (Bitmap bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (SolidBrush a = new SolidBrush(Color.FromArgb(Cfg.ColUp)))
                        DrawArrow(g, a, 1f, 1f, 6f, 14f, true);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(Cfg.ColDown)))
                        DrawArrow(g, b, 9f, 1f, 6f, 14f, false);
                }
                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { Native.DestroyIcon(h); }
            }
        }

        void RefreshTrayIcon()
        {
            Icon old = tray.Icon;
            tray.Icon = MakeIcon();
            if (old != null) old.Dispose();
        }

        // بترجّع كل الإعدادات للافتراضي وبتطبّقها فوراً (بدون إعادة تشغيل)
        void ResetAll()
        {
            Cfg.ResetDefaults();
            Cfg.Save();
            trayTip = "";
            upNum = ""; dnNum = "";

            TopMost = Cfg.TopMost;
            ApplyClickThrough();
            hiddenByFullscreen = false;
            if (!Visible) Show();

            tbColor = Cfg.UseTaskbarColor
                ? SampleTaskbarNow() : Color.Empty;
            lastTbThick = -1;

            nic = null; nicAge = 99; haveLast = false;   // رجعنا لاختيار الكرت التلقائي
            Relayout();
            MoveToCorner(2);
            RefreshTrayIcon();
        }

        // ========================================================
        // قائمة الكلك اليمين
        // ========================================================
        static ToolStripMenuItem MI(string text, EventHandler h)
        {
            ToolStripMenuItem i = new ToolStripMenuItem(text);
            if (h != null) i.Click += h;
            return i;
        }

        static ToolStripMenuItem MIC(string text, bool chk, EventHandler h)
        {
            ToolStripMenuItem i = MI(text, h);
            i.Checked = chk;
            return i;
        }

        // القائمة دايماً بتفتح لفوق - أبداً تحت أو فوق الودجت مباشرة حتى ما تغطيه
        void ShowMenuAt(Point screenPt)
        {
            if (menu != null) { menu.Dispose(); menu = null; }
            menu = BuildMenu();
            StyleMenu(menu);
            Native.SetForegroundWindow(Handle);

            Rectangle wa = Screen.FromPoint(screenPt).WorkingArea;
            bool roomRight = screenPt.X + 260 < wa.Right;
            ToolStripDropDownDirection dir = roomRight
                ? ToolStripDropDownDirection.AboveRight
                : ToolStripDropDownDirection.AboveLeft;

            Point rel = PointToClient(screenPt);
            menu.Show(this, rel, dir);
        }

        static void StyleMenu(ToolStripDropDown dd)
        {
            dd.RightToLeft = Cfg.Language == Lang.Arabic ? RightToLeft.Yes : RightToLeft.No;
            dd.BackColor = Color.FromArgb(30, 30, 34);
            dd.ForeColor = Color.FromArgb(232, 232, 235);
            dd.Font = new Font("Segoe UI", 9.25f);
            ToolStripDropDownMenu ddm = dd as ToolStripDropDownMenu;
            // ShowCheckMargin لازم يكون true وإلا علامة الصح ما بتنرسم إطلاقاً
            // حتى لو Checked=true - المنطقة نفسها بتكون ملغية
            if (ddm != null) { ddm.ShowImageMargin = false; ddm.ShowCheckMargin = true; }
            foreach (ToolStripItem it in dd.Items)
            {
                ToolStripMenuItem mi = it as ToolStripMenuItem;
                if (mi != null && mi.HasDropDownItems) StyleMenu(mi.DropDown);
            }
        }

        void SetLanguage(Lang lang)
        {
            if (Cfg.Language == lang) return;
            Cfg.Language = lang;
            Cfg.Save();
            trayTip = "";
            upNum = ""; dnNum = "";
            BeginInvoke((MethodInvoker)delegate
            {
                ShowMenuAt(Cursor.Position);
            });
        }

        ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.RightToLeft = Cfg.Language == Lang.Arabic ? RightToLeft.Yes : RightToLeft.No;
            m.ShowImageMargin = false;
            m.ShowCheckMargin = true;   // بدونها ما بتبيّن علامة الصح للخيارات المفعّلة

            // ---------- المكان ----------
            ToolStripMenuItem place = new ToolStripMenuItem(Tr.Get(TrId.MenuPlace));
            place.DropDownItems.Add(MIC(Tr.Get(TrId.MenuLock), Cfg.Locked,
                delegate { Cfg.Locked = !Cfg.Locked; Cfg.Save(); }));
            place.DropDownItems.Add(MIC(Tr.Get(TrId.MenuSnap), Cfg.Snap,
                delegate { Cfg.Snap = !Cfg.Snap; Cfg.Save(); }));
            place.DropDownItems.Add(new ToolStripSeparator());
            place.DropDownItems.Add(MI(Tr.Get(TrId.MenuCornerTR),  delegate { MoveToCorner(1); }));
            place.DropDownItems.Add(MI(Tr.Get(TrId.MenuCornerTL),  delegate { MoveToCorner(0); }));
            place.DropDownItems.Add(MI(Tr.Get(TrId.MenuCornerBR),  delegate { MoveToCorner(2); }));
            place.DropDownItems.Add(MI(Tr.Get(TrId.MenuCornerBL),  delegate { MoveToCorner(3); }));

            if (Screen.AllScreens.Length > 1)
            {
                place.DropDownItems.Add(new ToolStripSeparator());
                ToolStripMenuItem mon = new ToolStripMenuItem(Tr.Get(TrId.MenuMoveToScreen));
                Screen[] all = Screen.AllScreens;
                for (int i = 0; i < all.Length; i++)
                {
                    Screen sc = all[i];
                    string label = Tr.Get(TrId.MenuScreen) + (i + 1) + (sc.Primary ? Tr.Get(TrId.MenuPrimary) : "");
                    mon.DropDownItems.Add(MI(label, delegate { MoveToCorner(2, sc); }));
                }
                place.DropDownItems.Add(mon);
            }
            m.Items.Add(place);

            // ---------- المظهر ----------
            ToolStripMenuItem look = new ToolStripMenuItem(Tr.Get(TrId.MenuAppearance));
            look.DropDownItems.Add(MIC(Tr.Get(TrId.MenuTopMost), Cfg.TopMost,
                delegate { Cfg.TopMost = !Cfg.TopMost; TopMost = Cfg.TopMost; Cfg.Save(); }));
            look.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem op = new ToolStripMenuItem(Tr.Get(TrId.MenuOpacity));
            int[] ops = { 50, 70, 85, 100 };
            foreach (int o in ops)
            {
                int oo = o;
                op.DropDownItems.Add(MIC(o + "%", Cfg.Opacity == o,
                    delegate { Cfg.Opacity = oo; Cfg.Save(); Render(); }));
            }
            look.DropDownItems.Add(op);

            ToolStripMenuItem fs = new ToolStripMenuItem(Tr.Get(TrId.MenuFontSize));
            TrId[] fids = { TrId.MenuFontSmall, TrId.MenuFontMedium, TrId.MenuFontLarge };
            for (int i = 0; i < 3; i++)
            {
                int ii = i;
                fs.DropDownItems.Add(MIC(Tr.Get(fids[i]), Cfg.FontIdx == i,
                    delegate { Cfg.FontIdx = ii; Cfg.Save(); Relayout(); }));
            }
            look.DropDownItems.Add(fs);

            ToolStripMenuItem ori = new ToolStripMenuItem(Tr.Get(TrId.MenuOrientation));
            ori.DropDownItems.Add(MIC(Tr.Get(TrId.MenuVertical), Cfg.Vertical,
                delegate { Cfg.Vertical = true; Cfg.Save(); Relayout(); }));
            ori.DropDownItems.Add(MIC(Tr.Get(TrId.MenuHorizontal), !Cfg.Vertical,
                delegate { Cfg.Vertical = false; Cfg.Save(); Relayout(); }));
            look.DropDownItems.Add(ori);

            look.DropDownItems.Add(new ToolStripSeparator());
            look.DropDownItems.Add(MIC(Tr.Get(TrId.MenuMatchTbHeight), Cfg.MatchTaskbar,
                delegate { Cfg.MatchTaskbar = !Cfg.MatchTaskbar; Cfg.Save(); Relayout(); }));
            look.DropDownItems.Add(MIC(Tr.Get(TrId.MenuMatchTbColor), Cfg.UseTaskbarColor,
                delegate
                {
                    Cfg.UseTaskbarColor = !Cfg.UseTaskbarColor;
                    Cfg.Save();
                    tbColor = Cfg.UseTaskbarColor
                        ? SampleTaskbarNow() : Color.Empty;
                    Render();
                }));

            look.DropDownItems.Add(new ToolStripSeparator());
            look.DropDownItems.Add(BuildColorsMenu());
            m.Items.Add(look);

            // ---------- القياس ----------
            ToolStripMenuItem meas = new ToolStripMenuItem(Tr.Get(TrId.MenuMeasure));
            ToolStripMenuItem fmt = new ToolStripMenuItem(Tr.Get(TrId.MenuFormat));
            TrId[] fmtIds = { TrId.FormatAuto, TrId.FormatFixedKB, TrId.FormatFixedMB, TrId.FormatFixedGB };
            for (int i = 0; i < 4; i++)
            {
                int ii = i;
                fmt.DropDownItems.Add(MIC(Tr.Get(fmtIds[i]), Cfg.UnitMode == i,
                    delegate { Cfg.UnitMode = ii; Cfg.Save(); Relayout(); }));
            }
            meas.DropDownItems.Add(fmt);

            ToolStripMenuItem un = new ToolStripMenuItem(Tr.Get(TrId.MenuUnit));
            un.DropDownItems.Add(MIC(Tr.Get(TrId.UnitBytes), !Cfg.Bits,
                delegate { Cfg.Bits = false; Cfg.Save(); Relayout(); }));
            un.DropDownItems.Add(MIC(Tr.Get(TrId.UnitBits), Cfg.Bits,
                delegate { Cfg.Bits = true; Cfg.Save(); Relayout(); }));
            meas.DropDownItems.Add(un);

            meas.DropDownItems.Add(new ToolStripSeparator());
            meas.DropDownItems.Add(BuildAdaptersMenu());
            m.Items.Add(meas);

            // ---------- اختبار السرعة ----------
            m.Items.Add(MI(Tr.Get(TrId.MenuSpeedTest), delegate { OpenSpeedTest(); }));
            if (Cfg.LastTest.Length > 0)
                m.Items.Add(Info(Tr.Get(TrId.MenuLastTest) + "↓ " + FmtMbps(Cfg.LastDown)
                                 + "   ↑ " + FmtMbps(Cfg.LastUp) + "   (" + Cfg.LastTest + ")"));

            // ---------- الاستهلاك ----------
            Usage.Roll();
            ToolStripMenuItem us = new ToolStripMenuItem(Tr.Get(TrId.MenuUsage));
            us.DropDownItems.Add(Info(Tr.Get(TrId.UsageToday) + FmtBytes(Usage.DayRx) + "   ↑ " + FmtBytes(Usage.DayTx)));
            us.DropDownItems.Add(Info(Tr.Get(TrId.UsageMonth) + FmtBytes(Usage.MonRx) + "   ↑ " + FmtBytes(Usage.MonTx)));
            us.DropDownItems.Add(Info(Tr.Get(TrId.UsageTotal) + FmtBytes(Usage.TotRx) + "   ↑ " + FmtBytes(Usage.TotTx)));
            us.DropDownItems.Add(new ToolStripSeparator());
            us.DropDownItems.Add(MI(Tr.Get(TrId.UsageReset), delegate { Usage.Reset(); }));
            m.Items.Add(us);

            // ---------- السلوك ----------
            ToolStripMenuItem beh = new ToolStripMenuItem(Tr.Get(TrId.MenuBehavior));
            beh.DropDownItems.Add(MIC(Tr.Get(TrId.MenuClickThrough), Cfg.ClickThrough,
                delegate { Cfg.ClickThrough = !Cfg.ClickThrough; Cfg.Save(); ApplyClickThrough(); }));
            beh.DropDownItems.Add(MIC(Tr.Get(TrId.MenuHideFullscreen), Cfg.HideFullscreen,
                delegate { Cfg.HideFullscreen = !Cfg.HideFullscreen; Cfg.Save(); }));
            beh.DropDownItems.Add(MIC(Tr.Get(TrId.MenuStartWithWindows), StartupOn(),
                delegate { SetStartup(!StartupOn()); }));
            beh.DropDownItems.Add(new ToolStripSeparator());
            beh.DropDownItems.Add(MI(Tr.Get(TrId.MenuStartShortcut),
                delegate { Program.EnsureStartMenuShortcut(); }));
            beh.DropDownItems.Add(new ToolStripSeparator());
            beh.DropDownItems.Add(MI(Tr.Get(TrId.MenuResetDefaults), delegate { ResetAll(); }));
            m.Items.Add(beh);

            // ---------- اللغة ----------
            ToolStripMenuItem langMenu = new ToolStripMenuItem(Tr.Get(TrId.MenuLanguage));
            langMenu.DropDownItems.Add(MIC(Tr.Get(TrId.LangEnglish), Cfg.Language == Lang.English,
                delegate { SetLanguage(Lang.English); }));
            langMenu.DropDownItems.Add(MIC(Tr.Get(TrId.LangArabic), Cfg.Language == Lang.Arabic,
                delegate { SetLanguage(Lang.Arabic); }));
            m.Items.Add(langMenu);

            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(MI(Tr.Get(TrId.MenuExit), delegate { Quit(); }));
            return m;
        }

        SpeedTestForm testForm;

        void OpenSpeedTest()
        {
            if (testForm != null && !testForm.IsDisposed) { testForm.Activate(); return; }
            testForm = new SpeedTestForm(scale,
                (Cfg.UseTaskbarColor && tbColor != Color.Empty) ? tbColor : Color.FromArgb(Cfg.ColBg));
            testForm.Show();
        }

        static string FmtMbps(double m)
        {
            if (m <= 0) return "—";
            return (m >= 100 ? m.ToString("0", CultureInfo.InvariantCulture)
                             : m.ToString("0.0", CultureInfo.InvariantCulture)) + " Mbps";
        }

        static ToolStripMenuItem Info(string text)
        {
            ToolStripMenuItem i = new ToolStripMenuItem(text);
            i.Enabled = false;
            return i;
        }

        ToolStripMenuItem BuildColorsMenu()
        {
            ToolStripMenuItem col = new ToolStripMenuItem(Tr.Get(TrId.MenuColors));

            // أطقم جاهزة: اسم، رفع، تنزيل، نص، خلفية
            object[][] presets = new object[][] {
                new object[]{ TrId.ColorDefault,    Cfg.DefUp, Cfg.DefDown, Cfg.DefText, Cfg.DefBg },
                new object[]{ TrId.ColorNeon,       unchecked((int)0xFF00E5FF), unchecked((int)0xFF39FF6A), unchecked((int)0xFFFFFFFF), unchecked((int)0xF00A0A12) },
                new object[]{ TrId.ColorSunset,     unchecked((int)0xFFFF8A3D), unchecked((int)0xFF4FC3F7), unchecked((int)0xFFFFF3E6), unchecked((int)0xEB2A1E22) },
                new object[]{ TrId.ColorMonochrome, unchecked((int)0xFFDDDDDD), unchecked((int)0xFF999999), unchecked((int)0xFFFFFFFF), unchecked((int)0xF0141414) },
                new object[]{ TrId.ColorFrost,      unchecked((int)0xFF2563EB), unchecked((int)0xFF059669), unchecked((int)0xFF1A1A1A), unchecked((int)0xEBF2F4F8) }
            };

            foreach (object[] p in presets)
            {
                object[] pp = p;
                bool active = Cfg.ColUp == (int)pp[1] && Cfg.ColDown == (int)pp[2]
                           && Cfg.ColText == (int)pp[3] && Cfg.ColBg == (int)pp[4];
                col.DropDownItems.Add(MIC(Tr.Get((TrId)pp[0]), active, delegate
                {
                    Cfg.ColUp = (int)pp[1]; Cfg.ColDown = (int)pp[2];
                    Cfg.ColText = (int)pp[3]; Cfg.ColBg = (int)pp[4];
                    Cfg.Save(); Render(); RefreshTrayIcon();
                }));
            }

            col.DropDownItems.Add(new ToolStripSeparator());
            col.DropDownItems.Add(MI(Tr.Get(TrId.ColorPickUp),   delegate { PickColor(0); }));
            col.DropDownItems.Add(MI(Tr.Get(TrId.ColorPickDown), delegate { PickColor(1); }));
            col.DropDownItems.Add(MI(Tr.Get(TrId.ColorPickText), delegate { PickColor(2); }));
            col.DropDownItems.Add(MI(Tr.Get(TrId.ColorPickBg),   delegate { PickColor(3); }));
            col.DropDownItems.Add(new ToolStripSeparator());
            col.DropDownItems.Add(MI(Tr.Get(TrId.ColorReset), delegate
            {
                Cfg.ColUp = Cfg.DefUp; Cfg.ColDown = Cfg.DefDown;
                Cfg.ColText = Cfg.DefText; Cfg.ColBg = Cfg.DefBg;
                Cfg.Save(); Render(); RefreshTrayIcon();
            }));
            return col;
        }

        void PickColor(int which)
        {
            int cur = which == 0 ? Cfg.ColUp : which == 1 ? Cfg.ColDown
                    : which == 2 ? Cfg.ColText : Cfg.ColBg;
            Color start = Color.FromArgb(cur);

            using (ColorDialog dlg = new ColorDialog())
            {
                dlg.Color = Color.FromArgb(255, start.R, start.G, start.B);
                dlg.FullOpen = true;
                dlg.AnyColor = true;
                if (dlg.ShowDialog() != DialogResult.OK) return;

                // منتقي ويندوز ما بيعطي شفافية - فبنحافظ على شفافية اللون الأصلي
                Color c = Color.FromArgb(start.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
                int v = c.ToArgb();
                if (which == 0) Cfg.ColUp = v;
                else if (which == 1) Cfg.ColDown = v;
                else if (which == 2) Cfg.ColText = v;
                else Cfg.ColBg = v;
                Cfg.Save();
                Render();
                if (which <= 1) RefreshTrayIcon();
            }
        }

        ToolStripMenuItem BuildAdaptersMenu()
        {
            ToolStripMenuItem ad = new ToolStripMenuItem(Tr.Get(TrId.MenuAdapter));
            string activeName = nic != null ? nic.Name : "—";
            ad.DropDownItems.Add(MIC(Tr.Get(TrId.AdapterAuto) + "  (" + activeName + ")", Cfg.Adapter.Length == 0,
                delegate { Cfg.Adapter = ""; Cfg.Save(); nic = null; nicAge = 99; haveLast = false; }));
            ad.DropDownItems.Add(new ToolStripSeparator());
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    string id = ni.Id;
                    string label = ni.Name + (ni.OperationalStatus == OperationalStatus.Up ? "" : Tr.Get(TrId.AdapterDisconnected));
                    ad.DropDownItems.Add(MIC(label, Cfg.Adapter == id, delegate
                    {
                        Cfg.Adapter = id; Cfg.Save();
                        nic = null; nicAge = 99; haveLast = false;
                    }));
                }
            }
            catch { }
            return ad;
        }

        // ========================================================
        // يبدأ مع ويندوز
        // ========================================================
        static bool StartupOn()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    return k != null && k.GetValue("NetSpeedMeter") != null;
            }
            catch { return false; }
        }

        static void SetStartup(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue("NetSpeedMeter", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("NetSpeedMeter", false);
                }
            }
            catch { }
        }

        // ========================================================
        void Quit()
        {
            timer.Stop();
            Usage.Save(true);
            Cfg.Save();
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Usage.Save(true);
            Cfg.Save();
            if (tray != null) { tray.Visible = false; }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // لازم نفكّ الاشتراك - SystemEvents بيمسك مرجع ثابت، وبدونه
                // بيضل يستدعي نموذج ميت بعد الإغلاق
                try { SystemEvents.DisplaySettingsChanged -= OnDisplayChanged; } catch { }
                try { SystemEvents.SessionEnding -= OnSessionEnding; } catch { }
                if (fgHook != IntPtr.Zero) { try { Native.UnhookWinEvent(fgHook); } catch { } fgHook = IntPtr.Zero; }
                timer.Dispose();
                if (menu != null) menu.Dispose();
                if (tray != null) tray.Dispose();
                if (surf != null) surf.Dispose();
                if (gm != null) gm.Dispose();
                scratch.Dispose();
                if (fNum != null) fNum.Dispose();
                if (fUnit != null) fUnit.Dispose();
                if (sfRight != null) sfRight.Dispose();
                if (sfLeft != null) sfLeft.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // ============================================================
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // تقوية مسار تحميل المكتبات: نحصر البحث عن أي DLL بمجلد النظام فقط،
            // فما ينفع حدا يزرع DLL خبيثة بمجلد البرنامج ويخليها تنتحل مكتبة نظام.
            // دفاع بالعمق - كل استدعاءات P/Invoke هون أصلاً لمكتبات ويندوز
            // المعروفة، بس هالسطر بيقفل الباب نهائياً وتكلفته صفر عملياً.
            try { Native.SetDefaultDllDirectories(Native.LOAD_LIBRARY_SEARCH_SYSTEM32); }
            catch { }   // متوفر من ويندوز 8 وطالع - الفشل غير مؤذٍ

            bool created;
            using (Mutex mtx = new Mutex(true, "NetSpeedMeter_SingleInstance", out created))
            {
                if (!created)
                {
                    // في نسخة شغالة أصلاً. الخروج بصمت كان بيخلي المستخدم يضغط
                    // على الأيقونة ومحصلش شي - وإذا الودجت كان مخفي أو ضايع ورا
                    // شريط المهام، بيبان وكأن البرنامج "مش شغال" أو "خربان".
                    // بدل هيك منبعت إشارة للنسخة الشغالة ترجّع تظهر بمكان صحيح.
                    Native.PostMessage(Native.HWND_BROADCAST,
                        Native.RegisterWindowMessage(Widget.ShowRequestMessage),
                        IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                // ولا نافذة خطأ بتطلع بوجه المستخدم - بتنكتب بملف بدلها
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { LogErr(e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                { LogErr(e.ExceptionObject as Exception); };

                Cfg.Load();
                Usage.Load();
                if (!Cfg.ShortcutDone) { EnsureStartMenuShortcut(); Cfg.ShortcutDone = true; Cfg.Save(); }
                Application.Run(new Widget());
                GC.KeepAlive(mtx);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, int cchBuffer);

        // بيعمل اختصار بقائمة ابدأ بصمت - بدونه بحث ويندوز ما بيلاقي البرنامج إطلاقاً
        // لأنه بيفهرس اختصارات قائمة ابدأ، مش أي exe قاعد بأي مجلد.
        // بدون أي مكتبة COM مرجعية - استدعاء متأخر (late binding) عبر Reflection على
        // WScript.Shell المدمج بويندوز.
        //
        // مصيدة حقيقية: واجهة WScript.Shell بترمي استثناء أو بتخرّب المسار إذا كان فيه
        // حروف غير لاتينية (متل مجلد سطح المكتب "سطح المكتب" تبع OneDrive). فمنجرّب
        // المسار الأصلي، وإذا فشل منجرّب صيغة 8.3 المختصرة، وبالنهاية **منتحقق** إن
        // الاختصار المحفوظ بيأشّر على ملف موجود فعلاً - وإلا منمسحه بدل ما نترك
        // اختصار مكسور بقائمة ابدأ.
        public static bool EnsureStartMenuShortcut()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
            string link = Path.Combine(dir, "Net Speed Meter.lnk");
            string exe = Application.ExecutablePath;

            if (TryWriteShortcut(dir, link, exe)) return true;

            // خطة بديلة: صيغة 8.3 المختصرة (بتتجنّب الحروف غير اللاتينية بالمسار)
            StringBuilder sb = new StringBuilder(600);
            if (GetShortPathName(exe, sb, sb.Capacity) > 0)
            {
                string shortExe = sb.ToString();
                if (shortExe.Length > 0 && TryWriteShortcut(dir, link, shortExe)) return true;
            }

            try { if (File.Exists(link)) File.Delete(link); } catch { }
            return false;
        }

        static bool TryWriteShortcut(string dir, string link, string exe)
        {
            try
            {
                Directory.CreateDirectory(dir);
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                    null, shell, new object[] { link });
                Type scType = sc.GetType();

                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { exe });
                scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc,
                    new object[] { Path.GetDirectoryName(exe) });
                scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc,
                    new object[] { exe + ",0" });
                scType.InvokeMember("Description", BindingFlags.SetProperty, null, sc,
                    new object[] { "Net Speed Meter - internet speed and data usage" });
                scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);

                // التحقق: نعيد قراءة الاختصار ونتأكد إن هدفه ملف موجود فعلاً
                object chk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                    null, shell, new object[] { link });
                string saved = (string)chk.GetType().InvokeMember("TargetPath",
                    BindingFlags.GetProperty, null, chk, null);
                return !string.IsNullOrEmpty(saved) && File.Exists(saved);
            }
            catch { return false; }
        }

        static void LogErr(Exception ex)
        {
            try
            {
                if (ex == null) return;
                Directory.CreateDirectory(Cfg.Dir);
                string line = DateTime.Now.ToString("s", CultureInfo.InvariantCulture) + "  " + ex + Environment.NewLine;
                Io.AppendCapped(Path.Combine(Cfg.Dir, "error.log"), line, 512 * 1024);
            }
            catch { }
        }
    }
}
