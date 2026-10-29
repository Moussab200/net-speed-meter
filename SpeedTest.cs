// اختبار سرعة الإنترنت - نافذة صغيرة بتنفتح من قائمة الكلك اليمين فقط.
// بيستعمل نقاط قياس Cloudflare المجانية، بعدة اتصالات متوازية حتى يوصل
// لسرعة الخط الحقيقية (اتصال واحد بيعطي رقم أقل من الواقع على الخطوط السريعة).

using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;

namespace NetSpeedMeter
{
    public class SpeedTestForm : Form
    {
        // 25MB لكل طلب لأن كلاودفلير صار يرجع 403 على الـ 100MB، واللوب بيكرر الطلب فالسرعة ما بتأثر
        const string DownUrl = "https://speed.cloudflare.com/__down?bytes=25000000";
        const string UpUrl   = "https://speed.cloudflare.com/__up";
        const string PingHost = "1.1.1.1";

        const int DownThreads = 4;
        const int UpThreads   = 2;
        const int WarmupMs    = 1200;   // نتجاهل البداية البطيئة (TCP slow start)
        const int MeasureMs   = 8000;

        readonly float sc;
        readonly Color bgBase;
        Label lblTitle, lblPingV, lblDownV, lblUpV, lblStatus;
        Label lblPingK, lblDownK, lblUpK;
        Panel bar, barFill;
        Button btnRun, btnClose;

        Thread worker;
        volatile bool cancelAll;
        volatile PhaseState currentPhase;
        volatile string lastError = "";   // آخر سبب فشل فعلي - حتى نعرضه بدل ما نسكت

        // حالة مرحلة قياس مستقلة (تنزيل أو رفع) لمنع تداخل الخيوط المتأخرة
        sealed class PhaseState
        {
            public volatile bool Stop;
            long totalBytes;
            int liveRunning;
            readonly object reqLock = new object();
            readonly System.Collections.Generic.List<HttpWebRequest> activeReqs =
                new System.Collections.Generic.List<HttpWebRequest>();

            public PhaseState(int threadCount)
            {
                Interlocked.Exchange(ref liveRunning, threadCount);
                Interlocked.Exchange(ref totalBytes, 0);
            }

            public long ReadBytes()
            {
                return Interlocked.Read(ref totalBytes);
            }

            public void AddBytes(long bytes)
            {
                Interlocked.Add(ref totalBytes, bytes);
            }

            public int ReadLive()
            {
                return Thread.VolatileRead(ref liveRunning);
            }

            public void DecrementLive()
            {
                Interlocked.Decrement(ref liveRunning);
            }

            public bool RegisterReq(HttpWebRequest req)
            {
                lock (reqLock)
                {
                    if (Stop)
                    {
                        try { req.Abort(); } catch { }
                        return false;
                    }
                    activeReqs.Add(req);
                    return true;
                }
            }

            public void UnregisterReq(HttpWebRequest req)
            {
                lock (reqLock)
                {
                    activeReqs.Remove(req);
                }
            }

            public void AbortAll()
            {
                Stop = true;
                HttpWebRequest[] arr;
                lock (reqLock)
                {
                    arr = activeReqs.ToArray();
                    activeReqs.Clear();
                }
                for (int i = 0; i < arr.Length; i++)
                {
                    try { arr[i].Abort(); } catch { }
                }
            }
        }

        // bgOverride: اللون الفعلي المعروض بالودجت (ممكن يكون مأخوذ من شريط المهام)
        // حتى نافذة الاختبار تطلع بنفس الهوية مش بلون مختلف
        public SpeedTestForm(float scale, Color bgOverride)
        {
            sc = scale < 1f ? 1f : scale;
            bgBase = bgOverride.A == 0 ? Color.FromArgb(Cfg.ColBg) : bgOverride;

            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            bool isAr = Cfg.Language == Lang.Arabic;
            RightToLeft = isAr ? RightToLeft.Yes : RightToLeft.No;
            RightToLeftLayout = isAr;
            Text = Tr.Get(TrId.SpeedTestTitle);
            ClientSize = new Size(P(360), P(268));

            Color bg = Opaque(bgBase);
            Color fg = Color.FromArgb(Cfg.ColText);
            bool darkBg = (bg.R + bg.G + bg.B) / 3 < 128;
            Color dim = darkBg ? Lighten(bg, 0.45f) : Darken(bg, 0.45f);

            BackColor = bg;
            ForeColor = fg;

            lblTitle = Mk(Tr.Get(TrId.SpeedTestTitle), 12f, FontStyle.Bold, fg, P(18), P(16), P(324), P(28));
            lblTitle.TextAlign = isAr ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;

            int y = P(58);
            lblPingK = Mk(Tr.Get(TrId.SpeedTestPing), 9.5f, FontStyle.Regular, dim, P(18), y, P(150), P(26));
            lblPingV = Mk("—", 15f, FontStyle.Bold, fg, P(170), y - P(4), P(172), P(32));
            lblPingV.TextAlign = isAr ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleRight;

            y += P(44);
            lblDownK = Mk(Tr.Get(TrId.SpeedTestDown), 9.5f, FontStyle.Regular, dim, P(18), y, P(150), P(26));
            lblDownV = Mk("—", 15f, FontStyle.Bold, Color.FromArgb(Cfg.ColDown), P(170), y - P(4), P(172), P(32));
            lblDownV.TextAlign = isAr ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleRight;

            y += P(44);
            lblUpK = Mk(Tr.Get(TrId.SpeedTestUp), 9.5f, FontStyle.Regular, dim, P(18), y, P(150), P(26));
            lblUpV = Mk("—", 15f, FontStyle.Bold, Color.FromArgb(Cfg.ColUp), P(170), y - P(4), P(172), P(32));
            lblUpV.TextAlign = isAr ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleRight;

            y += P(48);
            bar = new Panel();
            bar.Location = new Point(P(18), y);
            bar.Size = new Size(P(324), P(5));
            bar.BackColor = darkBg ? Lighten(bg, 0.14f) : Darken(bg, 0.14f);
            Controls.Add(bar);

            barFill = new Panel();
            barFill.Location = new Point(0, 0);
            barFill.Size = new Size(0, P(5));
            barFill.BackColor = Color.FromArgb(Cfg.ColDown);
            bar.Controls.Add(barFill);

            lblStatus = Mk(Tr.Get(TrId.SpeedTestReady), 9f, FontStyle.Regular, dim, P(18), y + P(12), P(324), P(24));
            lblStatus.TextAlign = isAr ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;

            btnRun = MkBtn(Tr.Get(TrId.SpeedTestStart), P(198), P(222), P(144), P(32));
            btnRun.Click += delegate { Start(); };

            btnClose = MkBtn(Tr.Get(TrId.SpeedTestClose), P(18), P(222), P(100), P(32));
            btnClose.Click += delegate { Close(); };

            // إذا في نتيجة سابقة محفوظة، بنعرضها لحد ما يبلّش اختبار جديد
            if (Cfg.LastTest.Length > 0)
            {
                lblPingV.Text = Cfg.LastPing > 0 ? Cfg.LastPing + " ms" : "—";
                lblDownV.Text = Fmt(Cfg.LastDown);
                lblUpV.Text = Fmt(Cfg.LastUp);
                lblStatus.Text = Tr.Get(TrId.SpeedTestLastPrefix) + Cfg.LastTest;
            }
        }

        int P(float v) { return (int)Math.Round(v * sc); }

        static Color Opaque(Color c) { return Color.FromArgb(255, c.R, c.G, c.B); }

        static Color Lighten(Color c, float f)
        {
            return Color.FromArgb(255,
                (int)(c.R + (255 - c.R) * f), (int)(c.G + (255 - c.G) * f), (int)(c.B + (255 - c.B) * f));
        }

        static Color Darken(Color c, float f)
        {
            return Color.FromArgb(255, (int)(c.R * (1 - f)), (int)(c.G * (1 - f)), (int)(c.B * (1 - f)));
        }

        Label Mk(string text, float size, FontStyle st, Color col, int x, int y, int w, int h)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = new Font("Segoe UI", size, st);
            l.ForeColor = col;
            l.BackColor = Color.Transparent;
            l.Location = new Point(x, y);
            l.Size = new Size(w, h);
            l.TextAlign = Cfg.Language == Lang.Arabic ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft;
            Controls.Add(l);
            return l;
        }

        Button MkBtn(string text, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = text;
            b.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Lighten(BackColor, 0.22f);
            b.BackColor = Lighten(BackColor, 0.10f);
            b.ForeColor = ForeColor;
            b.UseVisualStyleBackColor = false;
            Controls.Add(b);
            return b;
        }

        // ====================================================
        void Start()
        {
            if (worker != null && worker.IsAlive) return;
            cancelAll = false;
            btnRun.Enabled = false;
            lblPingV.Text = "—"; lblDownV.Text = "—"; lblUpV.Text = "—";
            barFill.Width = 0;
            worker = new Thread(Run);
            worker.IsBackground = true;
            worker.Start();
        }

        void UI(ThreadStart a)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(a); } catch { }
        }

        void Status(string s) { UI(delegate { if (!IsDisposed) lblStatus.Text = s; }); }

        void Progress(int pct)
        {
            UI(delegate
            {
                if (IsDisposed) return;
                int w = bar.ClientSize.Width * Cfg.Clamp(pct, 0, 100) / 100;
                barFill.Width = w;
            });
        }

        static string Fmt(double mbps)
        {
            if (mbps <= 0) return "—";
            string n = mbps >= 100 ? mbps.ToString("0", CultureInfo.InvariantCulture)
                     : mbps >= 10 ? mbps.ToString("0.0", CultureInfo.InvariantCulture)
                                  : mbps.ToString("0.00", CultureInfo.InvariantCulture);
            return n + " Mbps";
        }

        void Run()
        {
            lastError = "";
            long ping = -1;
            double down = 0, up = 0;
            bool doneCalled = false;

            try
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                    ServicePointManager.DefaultConnectionLimit = 32;
                    ServicePointManager.Expect100Continue = false;
                }
                catch { }

                // ---------- 1. زمن الاستجابة ----------
                Status(Tr.Get(TrId.SpeedTestMeasuringPing));
                Progress(4);
                ping = MeasurePing();
                long pingShow = ping;
                UI(delegate { if (!IsDisposed) lblPingV.Text = pingShow >= 0 ? pingShow + " ms" : Tr.Get(TrId.SpeedTestPingFailed); });

                if (cancelAll) { doneCalled = true; Done(ping, 0, 0, true); return; }

                // ---------- 2. التنزيل ----------
                Status(Tr.Get(TrId.SpeedTestMeasuringDown));
                down = Measure(true);
                double dShow = down;
                UI(delegate { if (!IsDisposed) lblDownV.Text = Fmt(dShow); });

                if (cancelAll) { doneCalled = true; Done(ping, down, 0, true); return; }

                // ---------- 3. الرفع ----------
                Status(Tr.Get(TrId.SpeedTestMeasuringUp));
                up = Measure(false);
                double uShow = up;
                UI(delegate { if (!IsDisposed) lblUpV.Text = Fmt(uShow); });

                doneCalled = true;
                Done(ping, down, up, false);
            }
            catch (Exception ex)
            {
                lastError = DescribeError(ex);
            }
            finally
            {
                if (!doneCalled) Done(ping, down, up, cancelAll);
            }
        }

        void Done(long ping, double down, double up, bool aborted)
        {
            Progress(100);
            bool ok = down > 0 || up > 0;
            if (!aborted && ok)
            {
                if (ping >= 0) Cfg.LastPing = (int)ping;
                if (down > 0) Cfg.LastDown = down;
                if (up > 0) Cfg.LastUp = up;
                Cfg.LastTest = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                Cfg.Save();
            }

            string msg;
            if (aborted) msg = Tr.Get(TrId.SpeedTestAborted);
            else if (!ok) msg = lastError.Length > 0 ? lastError : Tr.Get(TrId.SpeedTestFailed);
            else if ((down <= 0 || up <= 0) && lastError.Length > 0) msg = lastError;
            else msg = Tr.Get(TrId.SpeedTestCompleted);
            Status(msg);
            UI(delegate { if (!IsDisposed) btnRun.Enabled = true; });
        }

        long MeasurePing()
        {
            long best = -1;
            try
            {
                using (Ping p = new Ping())
                {
                    int consecutiveFails = 0;
                    for (int i = 0; i < 4 && !cancelAll; i++)
                    {
                        try
                        {
                            // مهلة 1 ثانية ونوقف بعد فشلين متتاليين حتى ما نعلّق 8 ثواني لو ICMP محجوب
                            PingReply r = p.Send(PingHost, 1000);
                            if (r != null && r.Status == IPStatus.Success)
                            {
                                consecutiveFails = 0;
                                if (best < 0 || r.RoundtripTime < best) best = r.RoundtripTime;
                            }
                            else
                            {
                                consecutiveFails++;
                                if (consecutiveFails >= 2 && best < 0) break;
                            }
                        }
                        catch
                        {
                            consecutiveFails++;
                            if (consecutiveFails >= 2 && best < 0) break;
                        }
                    }
                }
            }
            catch { }
            return best;
        }

        // القياس الفعلي: نشغّل عدة اتصالات، ننتظر فترة إحماء، بعدين نقيس
        double Measure(bool download)
        {
            int n = download ? DownThreads : UpThreads;
            PhaseState phase = new PhaseState(n);
            currentPhase = phase;

            Thread[] th = new Thread[n];
            for (int i = 0; i < n; i++)
            {
                th[i] = new Thread(download
                    ? (ThreadStart)delegate { DownWorker(phase); }
                    : (ThreadStart)delegate { UpWorker(phase); });
                th[i].IsBackground = true;
                th[i].Start();
            }

            // إحماء - بيتجاهل بطء بداية الاتصال حتى الرقم يطلع صحيح
            int waited = 0;
            while (waited < WarmupMs && !cancelAll) { Thread.Sleep(100); waited += 100; }

            long baseBytes = phase.ReadBytes();
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            double mbps = 0;

            while (sw.ElapsedMilliseconds < MeasureMs && !cancelAll && phase.ReadLive() > 0)
            {
                Thread.Sleep(200);
                double secs = sw.Elapsed.TotalSeconds;
                if (secs <= 0) continue;
                long b = phase.ReadBytes() - baseBytes;
                mbps = b * 8.0 / 1000000.0 / secs;

                double live = mbps;
                UI(delegate
                {
                    if (IsDisposed) return;
                    if (download) lblDownV.Text = Fmt(live); else lblUpV.Text = Fmt(live);
                });

                int basePct = download ? 10 : 55;
                Progress(basePct + (int)(sw.ElapsedMilliseconds * 45.0 / MeasureMs));
            }

            phase.AbortAll();
            for (int i = 0; i < n; i++) { try { th[i].Join(1500); } catch { } }
            if (currentPhase == phase) currentPhase = null;
            return mbps;
        }

        // مصيدة كانت هون: try/catch وحدة لف كل الحلقة، فأول طلب يفشل (هبّة شبكة
        // مؤقتة، تدخّل جدار ناري، أي شي) كان يقتل الخيط كامل ونهائياً بلا إعادة
        // محاولة ولا أي رسالة - فيطلع التنزيل "0" فوراً وكأنه ما اشتغل إطلاقاً.
        // هلق كل طلب محاط بمحاولته الخاصة، وفشل واحد ما بيوقف الباقي.
        void DownWorker(PhaseState phase)
        {
            byte[] buf = new byte[65536];
            while (!phase.Stop && !cancelAll)
            {
                HttpWebRequest req = null;
                try
                {
                    req = (HttpWebRequest)WebRequest.Create(DownUrl);
                    req.Method = "GET";
                    req.Timeout = 15000;
                    req.ReadWriteTimeout = 15000;
                    req.KeepAlive = true;
                    req.AutomaticDecompression = DecompressionMethods.None;
                    req.UserAgent = "NetSpeedMeter/1.0";
                    req.CachePolicy = new System.Net.Cache.RequestCachePolicy(
                        System.Net.Cache.RequestCacheLevel.NoCacheNoStore);

                    if (!phase.RegisterReq(req)) break;

                    using (WebResponse res = req.GetResponse())
                    using (Stream st = res.GetResponseStream())
                    {
                        int r;
                        while (!phase.Stop && !cancelAll && st != null && (r = st.Read(buf, 0, buf.Length)) > 0)
                            phase.AddBytes(r);
                    }
                }
                catch (Exception ex)
                {
                    if (!phase.Stop && !cancelAll)
                    {
                        lastError = DescribeError(ex);
                        if (!phase.Stop && !cancelAll) Thread.Sleep(400);   // تهدئة قبل إعادة المحاولة
                    }
                }
                finally
                {
                    if (req != null) phase.UnregisterReq(req);
                }
            }
            phase.DecrementLive();
        }

        void UpWorker(PhaseState phase)
        {
            byte[] buf = new byte[65536];
            Random rnd = new Random();
            rnd.NextBytes(buf);   // بيانات عشوائية حتى ما ينضغط الرفع ويعطي رقم مغلوط
            while (!phase.Stop && !cancelAll)
            {
                HttpWebRequest req = null;
                try
                {
                    req = (HttpWebRequest)WebRequest.Create(UpUrl);
                    req.Method = "POST";
                    req.ContentType = "application/octet-stream";
                    req.AllowWriteStreamBuffering = false;
                    req.SendChunked = true;
                    req.Timeout = 15000;
                    req.ReadWriteTimeout = 20000;
                    req.KeepAlive = true;
                    req.UserAgent = "NetSpeedMeter/1.0";

                    if (!phase.RegisterReq(req)) break;

                    using (Stream s = req.GetRequestStream())
                    {
                        while (!phase.Stop && !cancelAll)
                        {
                            s.Write(buf, 0, buf.Length);
                            phase.AddBytes(buf.Length);
                        }
                    }

                    // ما مننتظر الاستجابة لو توقف الاختبار
                    if (!phase.Stop && !cancelAll)
                    {
                        try { using (WebResponse r = req.GetResponse()) { } } catch { }
                    }
                    else
                    {
                        try { req.Abort(); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    if (!phase.Stop && !cancelAll)
                    {
                        lastError = DescribeError(ex);
                        if (!phase.Stop && !cancelAll) Thread.Sleep(400);
                    }
                }
                finally
                {
                    if (req != null) phase.UnregisterReq(req);
                }
            }
            phase.DecrementLive();
        }

        // بيرجّع سبب مفهوم بدل اسم صنف الاستثناء التقني - المستخدم مش لازم
        // يعرف شو هو WebException، بدو يعرف "تعذّر الاتصال" أو "انتهت المهلة"
        static string DescribeError(Exception ex)
        {
            WebException we = ex as WebException;
            if (we != null)
            {
                switch (we.Status)
                {
                    case WebExceptionStatus.Timeout: return Tr.Get(TrId.SpeedTestErrTimeout);
                    case WebExceptionStatus.NameResolutionFailure: return Tr.Get(TrId.SpeedTestErrResolution);
                    case WebExceptionStatus.ConnectFailure: return Tr.Get(TrId.SpeedTestErrConnect);
                    case WebExceptionStatus.SecureChannelFailure: return Tr.Get(TrId.SpeedTestErrTls);
                    case WebExceptionStatus.TrustFailure: return Tr.Get(TrId.SpeedTestErrCert);
                    case WebExceptionStatus.ProtocolError:
                        HttpWebResponse resp = we.Response as HttpWebResponse;
                        if (resp != null)
                            return Tr.Get(TrId.SpeedTestErrGeneral) + " (HTTP " + (int)resp.StatusCode + ")";
                        return Tr.Get(TrId.SpeedTestErrGeneral) + " (" + we.Status + ")";
                    default: return Tr.Get(TrId.SpeedTestErrGeneral) + " (" + we.Status + ")";
                }
            }
            return ex.Message;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            cancelAll = true;
            PhaseState ps = currentPhase;
            if (ps != null) ps.AbortAll();
            base.OnFormClosing(e);
        }
    }
}
