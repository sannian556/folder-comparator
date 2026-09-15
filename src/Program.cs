// 文件比较器 —— 把单文件网页封装成原生 Windows 桌面程序
// 宿主：WinForms + WebView2（Chromium 内核，与 Edge 同源）
// 全部前端资源与依赖 DLL 内嵌在 exe 里，首次运行释放到 %LOCALAPPDATA%\FolderDiff
// 编译：csc.exe /target:winexe /platform:x64 /langversion:5 /codepage:65001

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// 程序集信息（版本信息）—— 会写进 exe 的"属性 → 详细信息"里。
// 没有版本信息的 exe 在杀毒软件的启发式/云查杀里会被当成"来路不明的程序"。
// 想换成自己的名字/网名，改 AssemblyCompany 和 AssemblyCopyright 两行即可。
[assembly: AssemblyTitle("文件比较器 — 文件夹对比工具")]
[assembly: AssemblyDescription("比较两个文件夹，找出内容不同 / 仅在 A 中 / 仅在 B 中 / 完全相同的文件，支持逐行查看差异并导出 ZIP")]
[assembly: AssemblyProduct("文件比较器")]
[assembly: AssemblyCompany("文件比较器")]
[assembly: AssemblyCopyright("Copyright (C) 2026")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]
[assembly: ComVisible(false)]
[assembly: Guid("3d9b6e07-5a41-4c8f-b2d6-9e1c4a7f8b52")]

namespace FolderDiffApp
{
    internal static class Program
    {
        internal const string AppTitle = "文件比较器";
        internal const string WindowTitle = "文件比较器 — 文件夹对比工具";
        internal const string HostName = "folderdiff.local";
        internal const string ResVersion = "1.0.0";

        internal static string BaseDir;
        internal static string RuntimeDir;
        internal static string WebDir;

        private static Mutex _single;

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [STAThread]
        private static void Main()
        {
            // Per-Monitor V2 高 DPI，老系统上失败就忽略
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (Exception) { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 单实例：重复双击时把已有窗口唤到前台，不再开第二个
            bool createdNew;
            _single = new Mutex(true, @"Local\FolderDiffApp.SingleInstance", out createdNew);
            if (!createdNew)
            {
                ActivateExistingWindow();
                return;
            }

            // 必须在任何 WebView2 类型被 JIT 解析之前挂上
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            try
            {
                BaseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FolderDiff");
                RuntimeDir = Path.Combine(BaseDir, "runtime", ResVersion);
                WebDir = Path.Combine(BaseDir, "web");
                ExtractPayload();
            }
            catch (Exception ex)
            {
                MessageBox.Show("准备运行环境失败：\r\n\r\n" + ex.Message,
                    AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 让 P/Invoke 能找到释放出来的原生 WebView2Loader.dll
            try { SetDllDirectory(RuntimeDir); }
            catch (Exception) { }
            try { LoadLibrary(Path.Combine(RuntimeDir, "WebView2Loader.dll")); }
            catch (Exception) { }

            AppHost.Run();
        }

        private static void ActivateExistingWindow()
        {
            try
            {
                Process me = Process.GetCurrentProcess();
                Process[] all = Process.GetProcessesByName(me.ProcessName);
                for (int i = 0; i < all.Length; i++)
                {
                    IntPtr h = all[i].MainWindowHandle;
                    if (h != IntPtr.Zero && all[i].Id != me.Id)
                    {
                        ShowWindow(h, 9); // SW_RESTORE
                        SetForegroundWindow(h);
                        break;
                    }
                }
            }
            catch (Exception) { }
        }

        // ---- 内嵌资源释放 -------------------------------------------------
        private static void ExtractPayload()
        {
            Directory.CreateDirectory(RuntimeDir);
            Directory.CreateDirectory(WebDir);

            Assembly asm = Assembly.GetExecutingAssembly();
            string[] names = asm.GetManifestResourceNames();
            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];
                string target = null;
                if (n.StartsWith("web.", StringComparison.Ordinal))
                    target = Path.Combine(WebDir, n.Substring(4));
                else if (n.StartsWith("lib.", StringComparison.Ordinal))
                    target = Path.Combine(RuntimeDir, n.Substring(4));
                if (target == null) continue;
                ExtractOne(asm, n, target);
            }
        }

        private static void ExtractOne(Assembly asm, string resName, string target)
        {
            Stream src = asm.GetManifestResourceStream(resName);
            if (src == null) return;
            try
            {
                FileInfo fi = new FileInfo(target);
                if (fi.Exists && fi.Length == src.Length) return; // 已是最新
                using (FileStream fs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buf = new byte[65536];
                    int read;
                    while ((read = src.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, read);
                }
            }
            finally { src.Dispose(); }
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            if (RuntimeDir == null) return null;
            try
            {
                string simple = new AssemblyName(args.Name).Name;
                string path = Path.Combine(RuntimeDir, simple + ".dll");
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            catch (Exception) { }
            return null;
        }

        internal static Icon LoadAppIcon()
        {
            try
            {
                Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico");
                if (s == null) return null;
                return new Icon(s); // Icon 生命周期内需要保持流打开，故刻意不 Dispose
            }
            catch (Exception) { return null; }
        }
    }

    /// <summary>
    /// 网页版历史记录存储：条目对宿主来说是不透明文本（页面自己编的一行 base64），
    /// 宿主只做 追加 / 读取 / 删除 / 按 20 条与 5MB 裁剪，落在 %LOCALAPPDATA%\FolderDiff\历史.dat。
    /// </summary>
    internal static class HistoryStore
    {
        public const int MaxEntries = 20;
        public const long MaxBytes = 5L * 1024 * 1024;

        public static string FilePath { get { return Path.Combine(Program.BaseDir, "历史.dat"); } }
        public static string BakPath { get { return Path.Combine(Program.BaseDir, "历史.bak"); } }
        public static string TmpPath { get { return Path.Combine(Program.BaseDir, "历史.tmp"); } }

        public static List<string> Load()
        {
            List<string> list = new List<string>();
            try
            {
                if (!File.Exists(FilePath)) return list;
                byte[] raw = File.ReadAllBytes(FilePath);
                if (raw.Length == 0) return list;
                using (MemoryStream ms = new MemoryStream(raw))
                using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
                using (StreamReader sr = new StreamReader(ds, Encoding.UTF8))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.Length > 0) list.Add(line);
                    }
                }
            }
            catch (Exception)
            {
                // 坏了就当没有（原文件留一份 .bad 便于排查），绝不让程序崩
                try
                {
                    if (File.Exists(FilePath) && !File.Exists(FilePath + ".bad"))
                        File.Copy(FilePath, FilePath + ".bad", true);
                }
                catch (Exception) { }
                return new List<string>();
            }
            return list;
        }

        public static long CurrentBytes()
        {
            try
            {
                FileInfo fi = new FileInfo(FilePath);
                if (fi.Exists) return fi.Length;
            }
            catch (Exception) { }
            return 0;
        }

        public static void TrimAndSave(List<string> list)
        {
            while (list.Count > MaxEntries) list.RemoveAt(list.Count - 1);
            for (int guard = 0; guard < 50 && list.Count > 1; guard++)
            {
                if (BytesOf(list) <= MaxBytes) break;
                list.RemoveAt(list.Count - 1);
            }
            Save(list);
        }

        /// <summary>按当前内容压缩一遍算出实际字节数（不落盘）。</summary>
        public static long BytesOf(List<string> list)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress, true))
                    using (StreamWriter sw = new StreamWriter(ds, new UTF8Encoding(true)))
                    {
                        for (int i = 0; i < list.Count; i++) sw.WriteLine(list[i]);
                    }
                    return ms.Length;
                }
            }
            catch (Exception) { return 0; }
        }

        public static void Save(List<string> list)
        {
            try
            {
                Directory.CreateDirectory(Program.BaseDir);
                byte[] data;
                using (MemoryStream ms = new MemoryStream())
                {
                    using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress, true))
                    using (StreamWriter sw = new StreamWriter(ds, new UTF8Encoding(true)))
                    {
                        for (int i = 0; i < list.Count; i++) sw.WriteLine(list[i]);
                    }
                    data = ms.ToArray();
                }

                File.WriteAllBytes(TmpPath, data);
                if (File.Exists(FilePath))
                {
                    try
                    {
                        if (File.Exists(BakPath)) File.Delete(BakPath);
                        File.Move(FilePath, BakPath);
                    }
                    catch (Exception) { }
                }
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(TmpPath, FilePath);
            }
            catch (Exception) { }
        }
    }

    internal static class AppHost
    {        private static Form _form;
        private static WebView2 _webView;
        private static bool _fallbackDone;

        internal static void Run()
        {
            _form = new Form();
            _form.Text = Program.WindowTitle;

            // 按屏幕工作区自适应，避免在小屏上超出桌面
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int w = Math.Min(1280, (int)(wa.Width * 0.92));
            int h = Math.Min(840, (int)(wa.Height * 0.92));
            _form.ClientSize = new Size(w, h);
            _form.MinimumSize = new Size(Math.Min(860, w), Math.Min(560, h));
            _form.StartPosition = FormStartPosition.CenterScreen;
            _form.BackColor = Color.FromArgb(245, 246, 248);
            try
            {
                Icon ic = Program.LoadAppIcon();
                if (ic != null) _form.Icon = ic;
            }
            catch (Exception) { }
            RestoreBounds(_form);

            _webView = new WebView2();
            _webView.Dock = DockStyle.Fill;
            _webView.DefaultBackgroundColor = Color.FromArgb(245, 246, 248);
            _form.Controls.Add(_webView);

            _form.Shown += OnShown;
            _form.FormClosing += OnFormClosing;

            Application.Run(_form);
        }

        private static async void OnShown(object sender, EventArgs e)
        {
            await InitAsync();
        }

        private static async Task InitAsync()
        {
            try
            {
                CoreWebView2CreationProperties props = new CoreWebView2CreationProperties();
                props.UserDataFolder = Path.Combine(Program.BaseDir, "WebView2");

                // 仅在显式设置环境变量时打开远程调试端口（用于自动化验证）
                string extra = "--no-first-run";
                string dbgPort = Environment.GetEnvironmentVariable("FOLDERDIFF_DEBUG_PORT");
                if (!string.IsNullOrEmpty(dbgPort)) extra += " --remote-debugging-port=" + dbgPort;
                props.AdditionalBrowserArguments = extra;

                _webView.CreationProperties = props;
                await _webView.EnsureCoreWebView2Async(null);
            }
            catch (Exception ex)
            {
                FallbackToBrowser(ex);
                return;
            }

            CoreWebView2 core = _webView.CoreWebView2;
            try
            {
                CoreWebView2Settings st = core.Settings;
                st.IsStatusBarEnabled = false;
                st.AreDevToolsEnabled = false;
                st.IsZoomControlEnabled = true;
                st.AreBrowserAcceleratorKeysEnabled = false;
                st.IsPasswordAutosaveEnabled = false;
                st.IsGeneralAutofillEnabled = false;
                st.IsSwipeNavigationEnabled = false;
            }
            catch (Exception) { }

            core.SetVirtualHostNameToFolderMapping(
                Program.HostName, Program.WebDir, CoreWebView2HostResourceAccessKind.Allow);
            core.DownloadStarting += OnDownloadStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.ProcessFailed += OnProcessFailed;
            core.WebMessageReceived += OnWebMessage;

            // 往页面里注入右上角工具条：深/浅色主题开关 + 窗口置顶开关
            // （不改动原网页文件，页面样式变量照样生效）
            try { await core.AddScriptToExecuteOnDocumentCreatedAsync(UiScript); }
            catch (Exception) { }

            core.Navigate("https://" + Program.HostName + "/index.html");
        }

        // ---- 注入脚本：主题开关 + 窗口置顶开关 -----------------------------
        // 页面自带的深色样式是 @media (prefers-color-scheme: dark) 触发的；这里把同一批
        // 变量与几处零散规则复制成 html[data-fd-theme=...] 作用域，属性选择器优先级更高，
        // 于是"手动选的主题"能压过系统设置。选择存在 localStorage（WebView2 用户数据目录里）。
        private const string UiScript = @"
(function () {
    var KEY = 'fd-theme';

    // 同一段脚本既跑在 exe（WebView2 宿主）里，也跑在官网网页上。
    // 没有宿主时只有两处不同：① 不出现「窗口置顶」（网页没有窗口可置顶）；
    // ② 历史记录改存 localStorage（浏览器里没有宿主文件可写）。
    var HOST = (window.chrome && window.chrome.webview && window.chrome.webview.postMessage)
        ? window.chrome.webview : null;

    var CSS =
        'html[data-fd-theme=light]{--bg:#f5f6f8;--card-bg:#ffffff;--text:#2c3e50;--text-secondary:#5a6c7d;' +
        '--border:#e2e6ea;--primary:#4a6cf7;--primary-hover:#3651d5;--primary-light:#eef1fe;--success:#10b981;' +
        '--success-bg:#ecfdf5;--warning:#f59e0b;--warning-bg:#fffbeb;--danger:#ef4444;--danger-bg:#fef2f2;' +
        '--info:#6366f1;--info-bg:#eef2ff;--export:#8b5cf6;--export-hover:#7c3aed;--export-bg:#f5f3ff;' +
        '--shadow-sm:0 1px 3px rgba(0,0,0,.06),0 1px 2px rgba(0,0,0,.04);' +
        '--shadow:0 4px 16px rgba(0,0,0,.08),0 2px 4px rgba(0,0,0,.04);' +
        '--shadow-lg:0 12px 32px rgba(0,0,0,.1),0 4px 8px rgba(0,0,0,.05)}' +
        'html[data-fd-theme=dark]{--bg:#1a1d23;--card-bg:#21252b;--text:#e1e4e8;--text-secondary:#9aa0ab;' +
        '--border:#30363d;--primary:#6d8aff;--primary-hover:#8ba3ff;--primary-light:#1c2440;--success:#34d399;' +
        '--success-bg:#0a2e1f;--warning:#fbbf24;--warning-bg:#2d1f06;--danger:#f87171;--danger-bg:#2d0a0a;' +
        '--info:#818cf8;--info-bg:#1a1e3a;--export:#a78bfa;--export-hover:#c4b5fd;--export-bg:#1e1b4b;' +
        '--shadow-sm:0 1px 3px rgba(0,0,0,.3);--shadow:0 4px 16px rgba(0,0,0,.35);' +
        '--shadow-lg:0 12px 32px rgba(0,0,0,.45)}' +
        'html[data-fd-theme=light] .badge-modified{color:#b45309}' +
        'html[data-fd-theme=dark] .badge-modified{color:#fbbf24}' +
        'html[data-fd-theme=light] .badge-added{color:#065f46}' +
        'html[data-fd-theme=dark] .badge-added{color:#34d399}' +
        'html[data-fd-theme=light] .badge-removed{color:#991b1b}' +
        'html[data-fd-theme=dark] .badge-removed{color:#f87171}' +
        'html[data-fd-theme=light] .badge-same{color:#3730a3}' +
        'html[data-fd-theme=dark] .badge-same{color:#a5b4fc}' +
        'html[data-fd-theme=light] .diff-table .diff-added{background:#d4fcdc}' +
        'html[data-fd-theme=dark] .diff-table .diff-added{background:#0a3d1f}' +
        'html[data-fd-theme=light] .diff-table .diff-removed{background:#ffe0e0}' +
        'html[data-fd-theme=dark] .diff-table .diff-removed{background:#3d0a0a}' +
        'html[data-fd-theme=light] .toast{background:#1e293b;color:#ffffff}' +
        'html[data-fd-theme=dark] .toast{background:#e2e8f0;color:#1e293b}' +
        'html[data-fd-theme=light] ::-webkit-scrollbar-thumb{background:#c8cdd4}' +
        'html[data-fd-theme=dark] ::-webkit-scrollbar-thumb{background:#4a5568}' +
        'html[data-fd-theme=light] .export-info-banner{color:var(--export)}' +
        'html[data-fd-theme=dark] .export-info-banner{color:#c4b5fd}';

    // ── 灵动岛式动效（4 个胶囊通用）────────────────────────────────
    // 胶囊：悬浮微抬 + 按下回弹 + 点击高光扫过；文字换值像翻牌一样上下滚动；
    // 面板：从被点的那颗胶囊「长」出来（缩放 + 圆角从 999px 融回 12px + 模糊消散），
    //       收起时反向缩回胶囊里。全部纯 CSS 关键帧，无依赖。
    var ANIM_CSS =
        ':root{--fd-ring:rgba(74,108,247,.26)}' +
        'html[data-fd-theme=dark]{--fd-ring:rgba(109,138,255,.32)}' +
        '@media (prefers-color-scheme: dark){html:not([data-fd-theme=light]){--fd-ring:rgba(109,138,255,.32)}}' +

        '.fd-pill{display:inline-flex !important;align-items:center;justify-content:center;position:relative;' +
        'overflow:hidden;will-change:transform;transform:translateZ(0);box-shadow:var(--shadow-sm);' +
        'transition:transform .34s cubic-bezier(.34,1.56,.64,1),background-color .22s ease,color .22s ease,' +
        'border-color .22s ease,box-shadow .22s ease}' +
        '.fd-pill:hover{transform:translateY(-1.5px) scale(1.045);box-shadow:0 8px 18px rgba(0,0,0,.16)}' +
        '.fd-pill:active{transform:scale(.92)}' +
        '.fd-pill.fd-open{box-shadow:0 0 0 3px var(--fd-ring),0 8px 20px rgba(0,0,0,.18)}' +

        '.fd-wrap{position:relative;display:inline-block;white-space:nowrap}' +
        '.fd-label{display:inline-block;white-space:nowrap}' +
        '.fd-ghost{position:absolute;left:0;top:0;width:100%;height:100%;display:flex;align-items:center;' +
        'justify-content:center;pointer-events:none}' +

        '.fd-roll{animation:fdRoll .42s cubic-bezier(.22,1.3,.36,1) both}' +
        '@keyframes fdRoll{0%{transform:translateY(118%) scale(.86);opacity:0;filter:blur(3px)}' +
        '58%{transform:translateY(-7%) scale(1.05);opacity:1;filter:blur(0)}' +
        '100%{transform:translateY(0) scale(1);opacity:1}}' +
        '.fd-roll-out{animation:fdRollOut .34s cubic-bezier(.5,0,.75,0) both}' +
        '@keyframes fdRollOut{0%{transform:translateY(0) scale(1);opacity:1}' +
        '100%{transform:translateY(-118%) scale(.78);opacity:0;filter:blur(3px)}}' +

        '.fd-pill::after{content:\'\';position:absolute;left:0;top:0;width:100%;height:100%;border-radius:inherit;' +
        'pointer-events:none;opacity:0;transform:translateX(-130%);' +
        'background:linear-gradient(100deg,transparent 22%,rgba(255,255,255,.55) 50%,transparent 78%)}' +
        '.fd-sweep::after{animation:fdSweep .62s cubic-bezier(.4,0,.2,1)}' +
        '@keyframes fdSweep{0%{transform:translateX(-130%);opacity:0}' +
        '22%{opacity:.85}100%{transform:translateX(130%);opacity:0}}' +

        '.fd-pop{animation:fdPop .5s cubic-bezier(.34,1.56,.64,1)}' +
        '@keyframes fdPop{0%{transform:scale(.9)}38%{transform:scale(1.09)}66%{transform:scale(.98)}' +
        '100%{transform:scale(1)}}' +

        '.fd-island{transform-origin:100% 0;will-change:transform,opacity,filter}' +
        '.fd-island-in{animation:fdIslandIn .46s cubic-bezier(.22,1.28,.36,1) both}' +
        '@keyframes fdIslandIn{0%{opacity:0;border-radius:999px;transform:scale(.34,.14) translateY(-10px);' +
        'filter:blur(8px)}54%{opacity:1;border-radius:16px;transform:scale(1.035,1.012) translateY(0);' +
        'filter:blur(0)}78%{border-radius:11px;transform:scale(.995,.998)}' +
        '100%{opacity:1;border-radius:12px;transform:scale(1);filter:blur(0)}}' +
        '.fd-island-out{animation:fdIslandOut .27s cubic-bezier(.5,0,.75,0) both}' +
        '@keyframes fdIslandOut{0%{opacity:1;border-radius:12px;transform:scale(1)}' +
        '100%{opacity:0;border-radius:999px;transform:scale(.3,.12) translateY(-12px);filter:blur(6px)}}' +
        '.fd-island-in>.fd-rise{animation:fdRise .44s cubic-bezier(.22,1.2,.36,1) both}' +
        '.fd-island-in>.fd-rise:nth-of-type(2){animation-delay:.03s}' +
        '.fd-island-in>.fd-rise:nth-of-type(3){animation-delay:.06s}' +
        '.fd-island-in>.fd-rise:nth-of-type(4){animation-delay:.09s}' +
        '@keyframes fdRise{0%{opacity:0;transform:translateY(7px)}100%{opacity:1;transform:translateY(0)}}' +

        '@media (prefers-reduced-motion: reduce){' +
        '.fd-pill,.fd-label,.fd-island{animation:none !important}' +
        '.fd-pill{transition:none !important}.fd-pill:hover,.fd-pill:active{transform:none}}';

    function ensureStyle() {
        if (document.getElementById('__fdThemeCss')) return;
        var s = document.createElement('style');
        s.id = '__fdThemeCss';
        s.textContent = CSS + ANIM_CSS;
        (document.head || document.documentElement).appendChild(s);
    }
    function stored() { try { return localStorage.getItem(KEY); } catch (e) { return null; } }
    function isDark() {
        var a = document.documentElement.getAttribute('data-fd-theme');
        if (a) return a === 'dark';
        return !!(window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
    }
    function setTheme(t) {
        ensureStyle();
        document.documentElement.setAttribute('data-fd-theme', t);
        try { localStorage.setItem(KEY, t); } catch (e) { }
    }
    // 尽早套用上次的选择，避免先闪一下系统主题
    (function early() {
        var t = stored();
        if (!t) return;
        if (document.documentElement) setTheme(t);
        else setTimeout(early, 0);
    })();

    // ── 忽略规则（与原生版语义完全一致）──────────────────────
    var IGN_KEY = 'fd-ignore';
    var PRESETS = [
        ['vcs', '版本控制目录　.git/　.svn/　.hg/　CVS/', ['.git/', '.svn/', '.hg/', '.bzr/', '_darcs/', 'CVS/']],
        ['temp', '系统与临时文件　Thumbs.db　desktop.ini　~$*　*.tmp　*.swp',
            ['Thumbs.db', 'desktop.ini', '.DS_Store', '~$*', '*.tmp', '*.temp', '*.swp', '*.swo', '*~']],
        ['logs', '日志与备份　*.log　*.bak　*.old　*.orig', ['*.log', '*.bak', '*.old', '*.orig']]
    ];
    var rules = { vcs: false, temp: false, logs: false, custom: '' };
    var compiled = [];
    var ignoredCount = { inputFolderA: 0, inputFolderB: 0 };

    function loadRules() {
        try {
            var s = localStorage.getItem(IGN_KEY);
            if (!s) return;
            var o = JSON.parse(s);
            if (!o) return;
            rules.vcs = !!o.vcs; rules.temp = !!o.temp; rules.logs = !!o.logs;
            rules.custom = o.custom ? String(o.custom) : '';
        } catch (e) { }
    }
    function saveRules() {
        try { localStorage.setItem(IGN_KEY, JSON.stringify(rules)); } catch (e) { }
    }
    function parseRule(raw) {
        if (raw === null || raw === undefined) return null;
        var s = String(raw).replace(/^\s+|\s+$/g, '');
        if (!s) return null;
        if (s.charAt(0) === '#' || s.charAt(0) === ';') return null;
        var negate = false;
        if (s.charAt(0) === '!') { negate = true; s = s.substring(1).replace(/^\s+|\s+$/g, ''); }
        if (!s) return null;
        s = s.replace(/\\/g, '/');
        while (s.indexOf('./') === 0) s = s.substring(2);
        var dirOnly = false;
        if (s.charAt(s.length - 1) === '/') { dirOnly = true; s = s.replace(/\/+$/, ''); }
        if (!s) return null;
        s = s.toLowerCase();
        return { segs: s.split('/'), dirOnly: dirOnly, negate: negate, anchored: s.indexOf('/') >= 0 };
    }
    function compile() {
        var out = [], i, j, r;
        for (i = 0; i < PRESETS.length; i++) {
            if (!rules[PRESETS[i][0]]) continue;
            var list = PRESETS[i][2];
            for (j = 0; j < list.length; j++) { r = parseRule(list[j]); if (r) out.push(r); }
        }
        var lines = String(rules.custom || '').replace(/\r\n/g, '\n').replace(/\r/g, '\n').split('\n');
        for (i = 0; i < lines.length; i++) { r = parseRule(lines[i]); if (r) out.push(r); }
        compiled = out;
        return out;
    }
    function segMatch(pat, name) {
        var p = 0, n = 0, starP = -1, starN = 0;
        while (n < name.length) {
            if (p < pat.length && (pat.charAt(p) === '?' || pat.charAt(p) === name.charAt(n))) { p++; n++; }
            else if (p < pat.length && pat.charAt(p) === '*') { starP = p; starN = n; p++; }
            else if (starP >= 0) { p = starP + 1; starN++; n = starN; }
            else return false;
        }
        while (p < pat.length && pat.charAt(p) === '*') p++;
        return p === pat.length;
    }
    function matchSegs(pat, pi, path, xi) {
        while (pi < pat.length) {
            if (pat[pi] === '**') {
                if (pi === pat.length - 1) return true;
                for (var k = xi; k <= path.length; k++) { if (matchSegs(pat, pi + 1, path, k)) return true; }
                return false;
            }
            if (xi >= path.length) return false;
            if (!segMatch(pat[pi], path[xi])) return false;
            pi++; xi++;
        }
        return xi === path.length;
    }
    function matchesOne(r, segs, depth, isDir) {
        if (r.dirOnly && !isDir) return false;
        if (!r.anchored) return segMatch(r.segs[0], segs[depth - 1]);
        var sub = segs.slice(0, depth);
        return matchSegs(r.segs, 0, sub, 0);
    }
    function isIgnored(relPath, isDir) {
        if (!compiled.length) return false;
        var rel = String(relPath === null || relPath === undefined ? '' : relPath)
            .replace(/\\/g, '/').replace(/^\/+/, '').replace(/\/+$/, '').toLowerCase();
        if (!rel) return false;
        var segs = rel.split('/');
        var hit = false, i, depth;
        for (i = 0; i < compiled.length; i++) {
            var r = compiled[i], m = false;
            if (matchesOne(r, segs, segs.length, isDir)) m = true;
            else {
                for (depth = 1; depth < segs.length; depth++) {
                    if (matchesOne(r, segs, depth, true)) { m = true; break; }
                }
            }
            if (!m) continue;
            if (r.negate) return false;   // 例外：直接放行
            hit = true;
        }
        return hit;
    }
    function relOf(file) {
        var raw = file.webkitRelativePath || file.name || '';
        var p = String(raw).split('/');
        return p.length > 1 ? p.slice(1).join('/') : String(raw);
    }
    function filterList(list) {
        var kept = [], ignored = 0, i;
        for (i = 0; i < list.length; i++) {
            var f = list[i];
            if (compiled.length && isIgnored(relOf(f), false)) { ignored++; continue; }
            kept.push(f);
        }
        return { kept: kept, ignored: ignored };
    }

    // ── 面板小工具 ──────────────────────────────────────
    function mk(tag, css, text) {
        var e = document.createElement(tag);
        if (css) e.style.cssText = css;
        if (text) e.textContent = text;
        return e;
    }
    function panelButton(id, text, onClick) {
        var b = mk('button', 'font-family:inherit;font-size:13px;padding:6px 12px;cursor:pointer;' +
            'border:1px solid var(--border);border-radius:8px;background:var(--card-bg);color:var(--text)');
        b.id = id; b.type = 'button'; b.textContent = text;
        b.addEventListener('click', onClick);
        return b;
    }
    function buildPanel() {
        var p = mk('div', 'position:fixed;top:62px;right:16px;width:360px;z-index:2147483647;display:none;' +
            'background:var(--card-bg);border:1px solid var(--border);border-radius:12px;' +
            'box-shadow:var(--shadow-lg);padding:14px;font-family:inherit;color:var(--text);font-size:13px');
        p.id = '__fdIgnorePanel';
        p.classList.add('fd-island');
        p.appendChild(mk('div', 'font-weight:600;font-size:14px;margin-bottom:6px', '忽略规则'));
        p.appendChild(mk('div', 'color:var(--text-secondary);font-size:12px;margin-bottom:10px',
            '对比时跳过这些文件（A、B 两侧同时生效）'));

        for (var i = 0; i < PRESETS.length; i++) {
            var key = PRESETS[i][0];
            var lab = mk('label', 'display:block;margin:6px 0;cursor:pointer;font-size:13px');
            var cb = mk('input');
            cb.type = 'checkbox';
            cb.id = '__fdIg_' + key;
            cb.checked = !!rules[key];
            cb.style.marginRight = '6px';
            cb.addEventListener('change', onPanelEdit);
            lab.appendChild(cb);
            lab.appendChild(document.createTextNode(PRESETS[i][1]));
            p.appendChild(lab);
        }

        p.appendChild(mk('div', 'color:var(--text-secondary);font-size:12px;margin-top:10px', '自定义规则（每行一条）：'));
        var ta = mk('textarea', 'width:100%;height:96px;margin-top:6px;padding:6px 8px;box-sizing:border-box;' +
            'border:1px solid var(--border);border-radius:8px;background:var(--bg);color:var(--text);' +
            'font-family:inherit;font-size:12px;line-height:1.5;resize:vertical');
        ta.id = '__fdIgCustom';
        ta.spellcheck = false;
        ta.value = rules.custom || '';
        ta.addEventListener('input', onPanelEdit);
        p.appendChild(ta);

        p.appendChild(mk('div', 'color:var(--text-secondary);font-size:12px;line-height:1.6;margin-top:8px',
            '语法：* 任意字符，? 单个字符，/ 结尾只匹配目录，**/ 任意层级，行首 ! 是例外（放回来）。' +
            '例：node_modules/　build/**　*.log　!keep.log'));

        var info = mk('div', 'color:var(--primary);font-size:12px;line-height:1.6;margin-top:8px');
        info.id = '__fdIgInfo';
        p.appendChild(info);

        var row = mk('div', 'margin-top:12px;display:flex;gap:8px');
        row.appendChild(panelButton('__fdIgSave', '保存', onSave));
        row.appendChild(panelButton('__fdIgClear', '清空规则', onClearRules));
        row.appendChild(panelButton('__fdIgClose', '关闭', function () { hidePanel(); }));
        p.appendChild(row);

        riseChildren(p);
        document.body.appendChild(p);
        return p;
    }
    function panelEl() { return document.getElementById('__fdIgnorePanel'); }
    function showPanel() {
        hideHistPanel();          // 两个面板互斥：开一个就关掉另一个
        var p = panelEl() || buildPanel();
        syncPanel();
        updatePanelInfo('');
        islandOpen(p, 'ig');
    }
    function hidePanel(instant) { islandClose(panelEl(), 'ig', instant); }
    function togglePanel() {
        var p = panelEl();
        if (panelShown(p)) hidePanel(); else showPanel();
    }
    function syncPanel() {
        for (var i = 0; i < PRESETS.length; i++) {
            var key = PRESETS[i][0];
            var cb = document.getElementById('__fdIg_' + key);
            if (cb) cb.checked = !!rules[key];
        }
        var ta = document.getElementById('__fdIgCustom');
        if (ta) ta.value = rules.custom || '';
    }
    function onPanelEdit() {
        // 编辑中先按当前界面值试算条数，不落盘（点保存才存）
        updatePanelInfo('（尚未保存）');
    }
    function updatePanelInfo(msg) {
        var info = document.getElementById('__fdIgInfo');
        if (!info) return;
        var ign = (ignoredCount.inputFolderA || 0) + (ignoredCount.inputFolderB || 0);
        var s = compiled.length === 0 ? '当前：未启用忽略规则' : ('当前：' + compiled.length + ' 条规则生效');
        if (ign > 0) s += '　·　已忽略 ' + ign + ' 个文件';
        if (msg) s += '　·　' + msg;
        info.textContent = s;
    }
    function collectPanel() {
        for (var i = 0; i < PRESETS.length; i++) {
            var key = PRESETS[i][0];
            var cb = document.getElementById('__fdIg_' + key);
            if (cb) rules[key] = !!cb.checked;
        }
        var ta = document.getElementById('__fdIgCustom');
        if (ta) rules.custom = ta.value;
    }
    function onSave() {
        var before = JSON.stringify(rules);
        collectPanel();
        saveRules();
        compile();
        paintIgnorePill();
        var changed = before !== JSON.stringify(rules);
        updatePanelInfo(changed ? '已保存，重新选择文件夹后生效' : '已保存');
    }
    function onClearRules() {
        rules.vcs = false; rules.temp = false; rules.logs = false; rules.custom = '';
        saveRules();
        compile();
        syncPanel();
        paintIgnorePill();
        updatePanelInfo('已清空');
    }
    function paintIgnorePill(el) {
        // 初始化时元素还没插进 DOM，getElementById 查不到，所以允许直接把元素传进来
        var b = el || document.getElementById('__fdIgPill');
        if (!b) return;
        var n = compiled.length;
        setPillText(b, n === 0 ? '忽略规则' : ('忽略规则 (' + n + ')'));
        b.title = n === 0 ? '点开设置忽略规则（.git、node_modules、*.log 等）'
                          : '正在忽略 ' + n + ' 条规则命中的文件';
        b.style.background = n === 0 ? 'var(--card-bg)' : 'var(--primary)';
        b.style.color = n === 0 ? 'var(--text-secondary)' : '#ffffff';
        b.style.borderColor = n === 0 ? 'var(--border)' : 'var(--primary)';
    }

    // 在页面读 input.files 之前换掉它：命中的文件直接不出现在列表里
    function patchInputs() {
        var desc = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'files');
        if (!desc || !desc.get) return;
        var ids = ['inputFolderA', 'inputFolderB'];
        for (var i = 0; i < ids.length; i++) {
            var el = document.getElementById(ids[i]);
            if (!el || el.__fdPatched) continue;
            el.__fdPatched = true;
            (function (node, id) {
                Object.defineProperty(node, 'files', {
                    configurable: true,
                    get: function () {
                        var raw = desc.get.call(node);
                        // 空读（页面选完会把 value 清掉，之后再读就是空的）不要抹掉已统计的忽略数
                        if (!raw || !raw.length) return raw;
                        // 顺手记下文件夹名（页面只显示文件名，历史条目要它）
                        try {
                            var rel0 = String(raw[0].webkitRelativePath || raw[0].name || '');
                            var seg0 = rel0.split('/');
                            HIST_NAMES[id] = seg0.length > 1 ? seg0[0] : (seg0[0] || '');
                        } catch (e) { }
                        if (!compiled.length) { ignoredCount[id] = 0; return raw; }
                        var res = filterList(raw);
                        ignoredCount[id] = res.ignored;
                        updatePanelInfo('');
                        if (!res.ignored) return raw;
                        var out = res.kept;
                        out.item = function (k) { return this[k]; };
                        return out;
                    }
                });
            })(el, ids[i]);
        }
    }

    // ── 历史记录（R2，网页版）───────────────────────────────
    // 浏览器沙箱限制：页面拿不到文件夹的绝对路径，也不能重读磁盘。
    // 所以网页版的历史是「只读快照」：清单来自当时导出的 ZIP，能查看、能清理，
    // 但重新打包只能回 XP-Win7 原生版做。条目交给宿主存文件（%LOCALAPPDATA%\FolderDiff\历史.dat）。
    var HIST_ENTRIES = [];
    var HIST_CHECKED = {};
    var HIST_CLEAN_N = 0;
    var HIST_NAMES = { inputFolderA: '', inputFolderB: '' };
    var HIST_REPICK = 0;      // 1 = 刚重选了 A，接着自动弹 B

    function histB64(s) { try { return btoa(unescape(encodeURIComponent(s))); } catch (e) { return ''; } }
    function histUnb64(s) { try { return decodeURIComponent(escape(atob(s))); } catch (e) { return ''; } }
    function histPost(cmd) {
        if (HOST) { try { HOST.postMessage(cmd); } catch (e) { } return; }
        try { histLocal(cmd); } catch (e) { }
    }

    // ── 没有宿主时（官网网页版）的历史记录：存 localStorage ──────────────
    // 存的格式（每条一行 base64）和回包格式（history-ok <条数> <字节> <整体base64>）
    // 都照抄宿主，所以上面板那段代码一行都不用改。上限同样是 20 条 / 5MB。
    var HIST_LS = 'fd-history';
    function histUtf8Len(s) {
        var n = 0, i, c;
        for (i = 0; i < s.length; i++) {
            c = s.charCodeAt(i);
            if (c < 0x80) n += 1;
            else if (c < 0x800) n += 2;
            else if (c >= 0xD800 && c <= 0xDBFF) { n += 4; i++; }
            else n += 3;
        }
        return n;
    }
    function histLsLoad() {
        try {
            var arr = JSON.parse(localStorage.getItem(HIST_LS) || '[]'), out = [], i;
            if (Object.prototype.toString.call(arr) !== '[object Array]') return [];
            for (i = 0; i < arr.length; i++) if (typeof arr[i] === 'string' && arr[i]) out.push(arr[i]);
            return out;
        } catch (e) { return []; }
    }
    function histLsSize(list) {
        var n = 0, i;
        for (i = 0; i < list.length; i++) n += histUtf8Len(list[i]);
        return n;
    }
    function histLsSave(list) {
        var guard = 0;
        while (list.length > 20) list.pop();
        while (list.length > 1 && histLsSize(list) > 5 * 1024 * 1024 && guard++ < 200) list.pop();
        try { localStorage.setItem(HIST_LS, JSON.stringify(list)); } catch (e) { }
    }
    function histLsPayload(list) {
        var lines = [], i, s;
        for (i = 0; i < list.length; i++) { s = histUnb64(list[i]); lines.push(s || list[i]); }
        return histB64(lines.join('\n'));
    }
    function histLocal(cmd) {
        var f = cmd.split(' '), list = histLsLoad(), i, idx;
        if (f[0] === 'history-add') {
            var line = (f.length > 1 ? f[1] : '').replace(/^\s+|\s+$/g, '');
            if (line) list.unshift(line);
        } else if (f[0] === 'history-delete') {
            idx = [];
            try { idx = JSON.parse(histUnb64(f.length > 1 ? f[1] : '[]')); } catch (e) { }
            if (Object.prototype.toString.call(idx) !== '[object Array]') idx = [];
            idx.sort(function (a, b) { return b - a; });
            for (i = 0; i < idx.length; i++) {
                if (idx[i] >= 0 && idx[i] < list.length) list.splice(idx[i], 1);
            }
        } else if (f[0] === 'history-clear') {
            list = [];
        }
        histLsSave(list);
        if (f[0] === 'history-list') {
            histApplyReply('history-list ' + histLsPayload(list));
        } else {
            histApplyReply('history-ok ' + list.length + ' ' + histLsSize(list) + ' ' + histLsPayload(list));
        }
    }
    function histFmt(n) {
        if (!n || n < 1024) return (n || 0) + ' B';
        if (n < 1048576) return (n / 1024).toFixed(1) + ' KB';
        return (n / 1048576).toFixed(1) + ' MB';
    }
    function histNow() {
        var d = new Date();
        function p(x) { return (x < 10 ? '0' : '') + x; }
        return d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate()) + ' ' +
               p(d.getHours()) + ':' + p(d.getMinutes()) + ':' + p(d.getSeconds());
    }
    function histLines(text) {
        if (!text) return [];
        var raw = histUnb64(text), out = [], lines = raw.split('\n');
        for (var i = 0; i < lines.length; i++) {
            var L = lines[i].replace(/^\s+|\s+$/g, '');
            if (!L) continue;
            try { out.push(JSON.parse(L)); } catch (e) { }
        }
        return out;
    }
    function histApplyReply(msg) {
        var f = msg.split(' ');
        if (f[0] === 'history-list') {
            HIST_ENTRIES = histLines(f.length > 1 ? f[1] : '');
        } else if (f[0] === 'history-ok') {
            HIST_CLEAN_N = 0;
            HIST_ENTRIES = histLines(f.length > 3 ? f[3] : '');
            paintHistPill();
            updateHistInfo('共 ' + f[1] + ' 条 · 占用 ' + histFmt(parseInt(f[2] || '0', 10)));
        }
        paintHistPill();
        renderHistBody();
    }

    function paintHistPill(el) {
        var b = el || document.getElementById('__fdHistPill');
        if (!b) return;
        var n = HIST_ENTRIES.length;
        setPillText(b, n === 0 ? '历史记录' : ('历史记录 (' + n + ')'));
        b.title = '每跑完一次对比就记一条（不用导出），可查看当时的差异清单';
    }

    function histRow(e, idx) {
        var row = mk('div', 'display:flex;align-items:center;gap:8px;padding:7px 6px;' +
            'border-top:1px solid var(--border);font-size:12px;cursor:pointer');
        row.title = 'A：' + (e.a || '?') + '\nB：' + (e.b || '?') + '\n导出文件：' + (e.zip || '') +
                    (e.ign ? '\n' + e.ign : '');
        row.appendChild(mk('div', 'width:118px;color:var(--text-secondary);flex-shrink:0', e.t || ''));
        row.appendChild(mk('div', 'flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap',
            (e.a || '?') + ' ⇄ ' + (e.b || '?')));
        row.appendChild(mk('div', 'width:130px;color:var(--text-secondary);flex-shrink:0',
            (e.m || 0) + ' 不同 · ' + (e.ob || 0) + ' 增 · ' + (e.oa || 0) + ' 缺 · ' + (e.same || 0) + ' 同'));

        var view = mk('button', 'font-family:inherit;font-size:12px;padding:3px 9px;cursor:pointer;' +
            'border:1px solid var(--border);border-radius:6px;background:var(--card-bg);color:var(--text);flex-shrink:0', '查看');
        view.type = 'button';
        view.addEventListener('click', function (ev) { ev.stopPropagation(); showHistView(idx); });
        row.appendChild(view);

        var cb = mk('input');
        cb.type = 'checkbox';
        cb.style.width = '15px';
        cb.style.height = '15px';
        cb.style.margin = '0';
        cb.style.cursor = 'pointer';
        cb.checked = !!HIST_CHECKED[idx];
        cb.addEventListener('click', function (ev) { ev.stopPropagation(); });
        cb.addEventListener('change', function () {
            HIST_CHECKED[idx] = cb.checked;
            updateHistClean();
        });
        // 用 label 把小方框包起来放大点击区（否则只有十几像素的方框能点中，跟原生版同一个坑）
        var cwrap = mk('label', 'display:flex;align-items:center;justify-content:center;width:32px;height:26px;' +
            'margin-left:2px;flex-shrink:0;cursor:pointer;border-radius:6px');
        cwrap.title = '打勾后「一键清理」只清理勾上的这几条';
        cwrap.appendChild(cb);
        cwrap.addEventListener('click', function (ev) { ev.stopPropagation(); });
        row.appendChild(cwrap);

        row.addEventListener('click', function () { showHistView(idx); });
        return row;
    }

    function histItemList(items, kind) {
        var box = document.createElement('div');
        var n = 0;
        for (var i = 0; i < items.length; i++) {
            var it = items[i];
            if (it.k !== kind) continue;
            n++;
            var size = kind === 'M' ? (histFmt(it.sa) + ' → ' + histFmt(it.sb)) : histFmt(it.sa || it.sb);
            box.appendChild(mk('div', 'font-size:12px;padding:3px 0;color:var(--text);' +
                'border-top:1px solid var(--border)', it.r + '　　' + size));
        }
        if (n === 0) box.appendChild(mk('div', 'font-size:12px;color:var(--text-secondary);padding:3px 0', '（无）'));
        return box;
    }

    function showHistView(idx) {
        var e = HIST_ENTRIES[idx];
        if (!e) return;
        var body = document.getElementById('__fdHistBody');
        body.innerHTML = '';
        body.appendChild(mk('div', 'font-size:13px;font-weight:600;margin-bottom:6px', '历史快照 · ' + (e.t || '')));
        body.appendChild(mk('div', 'font-size:12px;color:var(--text-secondary);line-height:1.8',
            'A：' + (e.a || '?') + '　　B：' + (e.b || '?') + '\n导出文件：' + (e.zip || '') + '\n' +
            (e.m || 0) + ' 个内容不同 · ' + (e.oa || 0) + ' 个仅在 A · ' + (e.ob || 0) + ' 个仅在 B · ' +
            (e.same || 0) + ' 个完全相同' + (e.ign ? '\n' + e.ign : '')));
        body.appendChild(mk('div', 'font-size:12px;color:var(--warning);margin:10px 0',
            '网页版拿不到文件夹的绝对路径、也无法重读磁盘：下面是当时的快照。要重新打包请用 XP-Win7 原生版。'));

        var secs = [['内容不同', 'M'], ['仅在 A 中', 'A'], ['仅在 B 中', 'B']];
        for (var i = 0; i < secs.length; i++) {
            body.appendChild(mk('div', 'font-size:12px;font-weight:600;margin:10px 0 2px', secs[i][0]));
            body.appendChild(histItemList(e.items || [], secs[i][1]));
        }

        var row = mk('div', 'margin-top:12px;display:flex;gap:8px');
        var back = panelButton('__fdHistBack', '返回列表', function () { renderHistBody(); });
        row.appendChild(back);
        var repick = panelButton('__fdHistRepick', '重新选择文件夹并对比', function () { histStartRepick(); });
        row.appendChild(repick);
        body.appendChild(row);
    }

    function histStartRepick() {
        hideHistPanel(true);       // 马上要弹文件夹选择框，收起动画就不播了
        HIST_REPICK = 1;
        var a = document.getElementById('inputFolderA');
        if (a) a.click();       // 用户手势里弹出的文件夹选择框，选完 A 会自动接着弹 B
    }

    function renderHistBody() {
        var body = document.getElementById('__fdHistBody');
        if (!body) return;
        body.innerHTML = '';
        if (!HIST_ENTRIES.length) {
            body.appendChild(mk('div', 'color:var(--text-secondary);font-size:13px;padding:16px 4px',
                '还没有历史记录 —— 跑完一次对比就会自动生成一条。'));
            updateHistClean();
            updateHistInfo('');
            return;
        }
        for (var i = 0; i < HIST_ENTRIES.length; i++) body.appendChild(histRow(HIST_ENTRIES[i], i));
        updateHistClean();
    }

    function updateHistClean() {
        var n = 0, k;
        for (k in HIST_CHECKED) { if (HIST_CHECKED[k]) n++; }
        HIST_CLEAN_N = n;
        var b = document.getElementById('__fdHistClean');
        if (b) b.textContent = n === 0 ? '一键清理历史记录' : ('清理选中的 ' + n + ' 条');
        var c = document.getElementById('__fdHistConfirm');
        if (c) c.style.display = 'none';
    }

    function updateHistInfo(msg) {
        var i = document.getElementById('__fdHistInfo');
        if (!i) return;
        var base = HIST_ENTRIES.length === 0 ? '还没有历史记录'
            : ('共 ' + HIST_ENTRIES.length + ' 条');
        i.textContent = msg || base;
    }

    function onHistCleanClick() {
        if (!HIST_ENTRIES.length) return;
        var n = HIST_CLEAN_N;
        var c = document.getElementById('__fdHistConfirm');
        var t = document.getElementById('__fdHistConfirmText');
        if (t) {
            t.textContent = n === 0
                ? ('确定要清空全部历史记录吗？共 ' + HIST_ENTRIES.length + ' 条，删除后无法恢复。')
                : ('确定要清理选中的 ' + n + ' 条历史记录吗？删除后无法恢复。');
        }
        if (c) c.style.display = 'block';   // 二次确认（在面板里就地确认）
    }

    function histDoClean() {
        var n = HIST_CLEAN_N, idx = [], k;
        for (k in HIST_CHECKED) { if (HIST_CHECKED[k]) idx.push(parseInt(k, 10)); }
        if (n === 0) histPost('history-clear');
        else if (idx.length) histPost('history-delete ' + histB64(idx.join(',')));
        HIST_CHECKED = {};
        HIST_CLEAN_N = 0;
        var c = document.getElementById('__fdHistConfirm');
        if (c) c.style.display = 'none';
    }

    function buildHistPanel() {
        var p = mk('div', 'position:fixed;top:62px;right:16px;width:560px;max-width:92vw;z-index:2147483647;' +
            'display:none;background:var(--card-bg);border:1px solid var(--border);border-radius:12px;' +
            'box-shadow:var(--shadow-lg);padding:14px;font-family:inherit;color:var(--text);font-size:13px');
        p.id = '__fdHistPanel';
        p.classList.add('fd-island');
        p.appendChild(mk('div', 'font-weight:600;font-size:14px;margin-bottom:6px', '历史记录'));
        p.appendChild(mk('div', 'color:var(--text-secondary);font-size:12px;margin-bottom:8px',
            '每跑完一次对比就存一条（不用导出）；点某条看当时的差异清单。'));

        var body = mk('div', 'max-height:46vh;overflow-y:auto;border:1px solid var(--border);border-radius:8px');
        body.id = '__fdHistBody';
        p.appendChild(body);

        var info = mk('div', 'color:var(--text-secondary);font-size:12px;margin-top:8px');
        info.id = '__fdHistInfo';
        p.appendChild(info);

        var confirm = mk('div', 'display:none;margin-top:8px;padding:8px 10px;border:1px solid var(--warning);' +
            'border-radius:8px;background:var(--warning-bg);color:var(--text);font-size:12px');
        confirm.id = '__fdHistConfirm';
        var ct = mk('div', '', '');
        ct.id = '__fdHistConfirmText';
        confirm.appendChild(ct);
        var crow = mk('div', 'margin-top:8px;display:flex;gap:8px');
        var okBtn = panelButton('__fdHistCleanOk', '确定清理', function () { histDoClean(); });
        var noBtn = panelButton('__fdHistCleanNo', '取消', function () { confirm.style.display = 'none'; });
        crow.appendChild(okBtn);
        crow.appendChild(noBtn);
        confirm.appendChild(crow);
        p.appendChild(confirm);

        var row = mk('div', 'margin-top:12px;display:flex;gap:8px;align-items:center');
        var clean = panelButton('__fdHistClean', '一键清理历史记录', function () { onHistCleanClick(); });
        row.appendChild(clean);
        row.appendChild(panelButton('__fdHistRefresh', '刷新', function () { histPost('history-list'); }));
        row.appendChild(panelButton('__fdHistClose', '关闭', function () { hideHistPanel(); }));
        p.appendChild(row);

        riseChildren(p);
        document.body.appendChild(p);
        return p;
    }
    function histPanelEl() { return document.getElementById('__fdHistPanel'); }
    function showHistPanel() {
        hidePanel();              // 两个面板互斥：开一个就关掉另一个
        var p = histPanelEl() || buildHistPanel();
        HIST_CHECKED = {};                 // 勾选是临时的：每次打开都清空
        HIST_CLEAN_N = 0;
        renderHistBody();
        updateHistInfo('');
        histPost('history-list');
        islandOpen(p, 'hi');
    }
    function hideHistPanel(instant) { islandClose(histPanelEl(), 'hi', instant); }
    function toggleHistPanel() {
        var p = histPanelEl();
        if (panelShown(p)) hideHistPanel(); else showHistPanel();
    }

    // ── 历史记录的产生：比对完成就记一条（2026-09-15 用户要求，不再等导出差异文件）──
    // 页面自己的脚本包在 IIFE 里，拿不到它的 compareResults 变量，所以这里盯 DOM：
    //   · 一次比对开始 = #progressContainer 拿到 active（且不是导出模式）
    //   · 一次比对结束 = progressText 变成「对比完成！」且容器失去 active
    // 结束时把结果区已经渲染好的计数与清单刮下来。导出不再产生历史，只把导出文件名补记到那条上。
    var HIST_ARMED = false;      // 已经看到「开始比对」，就等着结束时记一条
    var HIST_CAP_TIMER = null;

    function hookCompare() {
        var pc = document.getElementById('progressContainer');
        var pb = document.getElementById('progressBar');
        var pt = document.getElementById('progressText');
        if (!pc || !pb || !pt) return false;
        try {
            var obs = new MutationObserver(function () {
                var active = pc.classList.contains('active');
                var exporting = pb.classList.contains('export-mode');
                var txt = pt.textContent || '';
                if (active) { HIST_ARMED = true; return; }        // 新一轮开始
                if (!HIST_ARMED) return;
                if (exporting) { HIST_ARMED = false; return; }    // 这次是导出，不算
                if (txt.indexOf('对比完成') < 0) return;           // 只有对比会写这句
                HIST_ARMED = false;
                if (HIST_CAP_TIMER) clearTimeout(HIST_CAP_TIMER);
                HIST_CAP_TIMER = setTimeout(captureCompare, 260); // 等页面把结果渲染完
            });
            obs.observe(pc, { attributes: true, attributeFilter: ['class'] });
            obs.observe(pb, { attributes: true, attributeFilter: ['class'] });
            obs.observe(pt, { childList: true, characterData: true, subtree: true });
            window.__fdHistLast = 'compare-hook-on';
            return true;
        } catch (e) { window.__fdHistLast = 'compare-hook-failed: ' + e.message; return false; }
    }

    function histNum(id) {
        var el = document.getElementById(id);
        if (!el) return 0;
        return parseInt((el.textContent || '0').replace(/[^\d]/g, ''), 10) || 0;
    }

    // 例：A: 1.2 KB → B: 3.4 KB · 大小不同 ／ 大小相同 (1.2 KB) · 内容不同 ／ 单文件就是 1.2 KB
    function histParseSizes(txt) {
        var re = /([\d.]+)\s*(KB|MB|GB|B)/gi, out = [], x;
        while ((x = re.exec(txt || '')) !== null) {
            var u = x[2].toUpperCase();
            var mul = u === 'B' ? 1 : (u === 'KB' ? 1024 : (u === 'MB' ? 1048576 : 1073741824));
            out.push(Math.round(parseFloat(x[1]) * mul));
        }
        return out;
    }

    function scrapeItems(bodyId, kind) {
        var box = document.getElementById(bodyId);
        var out = [];
        if (!box) return out;
        var rows = box.querySelectorAll('.file-item');
        for (var i = 0; i < rows.length; i++) {
            var rel = rows[i].getAttribute('data-path') || '';
            if (!rel) continue;
            var sz = rows[i].querySelector('.file-size');
            var nums = histParseSizes(sz ? sz.textContent : '');
            if (kind === 'M') out.push({ k: 'M', r: rel, sa: nums[0] || 0, sb: nums[1] || nums[0] || 0 });
            else if (kind === 'A') out.push({ k: 'A', r: rel, sa: nums[0] || 0 });
            else out.push({ k: 'B', r: rel, sb: nums[0] || 0 });
        }
        return out;
    }

    function captureCompare() {
        var detail = document.getElementById('resultsDetail');
        if (!detail || !detail.classList.contains('active')) { window.__fdHistLast = 'no-results'; return; }

        var m = histNum('countModified'), oa = histNum('countOnlyInA'),
            ob = histNum('countOnlyInB'), same = histNum('countSame');
        var items = scrapeItems('bodyModified', 'M')
            .concat(scrapeItems('bodyOnlyA', 'A'), scrapeItems('bodyOnlyB', 'B'));

        var entry = {
            t: histNow(),
            a: HIST_NAMES.inputFolderA || '?',
            b: HIST_NAMES.inputFolderB || '?',
            zip: '',
            m: m, oa: oa, ob: ob, same: same,
            ign: compiled.length ? ('忽略规则：' + compiled.length + ' 条生效') : '忽略规则：未启用',
            items: items
        };
        histPost('history-add ' + histB64(JSON.stringify(entry)));
        window.__fdHistLast = 'compare-added items=' + items.length + ' m=' + m + ' oa=' + oa +
            ' ob=' + ob + ' same=' + same;
    }

    // 导出成功：不再产生历史，只把导出文件名补记到最新那条快照上（重写那一条，时间/清单原样保留）
    function rememberExportName(name) {
        if (!name) return;
        if (!HIST_ENTRIES.length) return;
        var e = HIST_ENTRIES[0];
        if (!e || e.zip) return;
        e.zip = name;
        histPost('history-delete ' + histB64('0'));
        histPost('history-add ' + histB64(JSON.stringify(e)));
        window.__fdHistLast = 'export-name=' + name;
    }

    function hookSaveAs() {
        if (!window.saveAs || window.saveAs.__fdHooked) return;
        var orig = window.saveAs;
        var wrapped = function (blob, name) {
            try { rememberExportName(name); } catch (e) { }
            return orig.apply(this, arguments);
        };
        wrapped.__fdHooked = true;
        try { window.saveAs = wrapped; } catch (e) { }
    }

    // 供自动化验收调用（不参与界面逻辑）
    window.__fdHist = {
        count: function () { return HIST_ENTRIES.length; },
        raw: function () { return JSON.stringify(HIST_ENTRIES); },
        apply: function (msg) { histApplyReply(msg); return HIST_ENTRIES.length; },
        b64: function (s) { return histB64(s); },
        lines: function (s) { return JSON.stringify(histLines(s)); },
        scrape: function () { return JSON.stringify({ m: histNum('countModified'), oa: histNum('countOnlyInA'), ob: histNum('countOnlyInB'), same: histNum('countSame'), items: scrapeItems('bodyModified', 'M').concat(scrapeItems('bodyOnlyA', 'A'), scrapeItems('bodyOnlyB', 'B')) }); },
        capture: function () { captureCompare(); return HIST_ENTRIES.length; },
        build: function () { return histB64(JSON.stringify({ t: histNow(), a: 'A', b: 'B', zip: 'p.zip', m: 1, oa: 0, ob: 0, same: 0, ign: '', items: [{ k: 'M', r: 'x.txt', sa: 1, sb: 2 }] })); }
    };

    function pill(b) {
        b.type = 'button';
        b.className = 'fd-pill';
        b.style.cssText = 'font-family:inherit;font-size:13px;line-height:1;padding:8px 14px;' +
            'cursor:pointer;border:1px solid var(--border);border-radius:999px;' +
            'background:var(--card-bg);color:var(--text-secondary)';
        return b;
    }

    // ── 灵动岛动效小工具 ─────────────────────────────────────────
    var islSeq = { ig: 0, hi: 0 };
    function pillOf(key) { return document.getElementById(key === 'ig' ? '__fdIgPill' : '__fdHistPill'); }

    // 文字换值：旧值像翻牌一样从上面翻走，新值从下面弹进来
    function pillLabel(b) {
        var w = b.querySelector('.fd-wrap');
        if (!w) {
            b.textContent = '';
            w = document.createElement('span');
            w.className = 'fd-wrap';
            var s = document.createElement('span');
            s.className = 'fd-label';
            w.appendChild(s);
            b.appendChild(w);
        }
        return w.querySelector('.fd-label');
    }
    function setPillText(b, txt) {
        if (!b) return;
        var s = pillLabel(b);
        if (s.textContent === txt) return;
        var w = s.parentNode;
        var stale = w.querySelectorAll('.fd-ghost');    // 连点时不堆残影
        for (var i = 0; i < stale.length; i++) w.removeChild(stale[i]);
        var ghost = document.createElement('span');
        ghost.className = 'fd-label fd-ghost fd-roll-out';
        ghost.textContent = s.textContent;
        w.appendChild(ghost);
        s.textContent = txt;
        s.classList.remove('fd-roll');
        void s.offsetWidth;
        s.classList.add('fd-roll');
        setTimeout(function () { if (ghost.parentNode) ghost.parentNode.removeChild(ghost); }, 400);
    }

    // 点击反馈：高光扫过 + 弹性回弹
    function popPill(b) {
        if (!b) return;
        b.classList.remove('fd-sweep'); b.classList.remove('fd-pop');
        void b.offsetWidth;
        b.classList.add('fd-sweep'); b.classList.add('fd-pop');
        setTimeout(function () { b.classList.remove('fd-sweep'); b.classList.remove('fd-pop'); }, 720);
    }

    // 面板从被点的那颗胶囊「长」出来 / 缩回胶囊
    function islandOpen(p, key) {
        var seq = ++islSeq[key];
        p.style.display = 'block';
        p.classList.remove('fd-island-in');
        p.classList.remove('fd-island-out');
        p.style.transform = 'none';                       // 量位置前先去掉可能残留的缩放
        var pr = p.getBoundingClientRect();
        p.style.transform = '';
        var b = pillOf(key);
        if (b) {
            var br = b.getBoundingClientRect();
            p.style.transformOrigin = ((br.left + br.width / 2) - pr.left).toFixed(1) + 'px ' +
                                      ((br.top + br.height / 2) - pr.top).toFixed(1) + 'px';
            b.classList.add('fd-open');
        }
        void p.offsetWidth;
        p.classList.add('fd-island-in');
        return seq;
    }
    function islandClose(p, key, instant) {
        if (!p || p.style.display === 'none') return;
        var seq = ++islSeq[key];
        var b = pillOf(key);
        if (b) b.classList.remove('fd-open');
        p.classList.remove('fd-island-in');
        p.classList.remove('fd-island-out');
        if (instant) { p.style.display = 'none'; return; }
        void p.offsetWidth;
        p.classList.add('fd-island-out');
        setTimeout(function () {
            if (islSeq[key] !== seq) return;              // 期间又被点开了，别关
            p.classList.remove('fd-island-out');
            p.style.display = 'none';
        }, 280);
    }
    function panelShown(p) {
        return !!p && p.style.display === 'block' && p.className.indexOf('fd-island-out') < 0;
    }
    // 面板内容逐条浮现
    function riseChildren(p) {
        var kids = p.children;
        for (var i = 0; i < kids.length; i++) {
            kids[i].classList.add('fd-rise');
            kids[i].style.animationDelay = (i * 0.028).toFixed(3) + 's';
        }
    }
    function paintTheme(b) {
        setPillText(b, isDark() ? '深色主题' : '浅色主题');
        b.title = '点击切换深色 / 浅色主题';
    }
    function paintPin(on, b) {
        setPillText(b, on ? '已置顶' : '窗口置顶');
        b.title = on ? '窗口已始终显示在最前面（点击取消）' : '点击让窗口始终显示在最前面';
        b.style.background = on ? 'var(--primary)' : 'var(--card-bg)';
        b.style.color = on ? '#ffffff' : 'var(--text-secondary)';
        b.style.borderColor = on ? 'var(--primary)' : 'var(--border)';
    }
    function ready() {
        if (document.getElementById('__fdBar')) return;
        ensureStyle();
        loadRules();
        compile();

        var bar = document.createElement('div');
        bar.id = '__fdBar';
        bar.style.cssText = 'position:fixed;top:14px;right:16px;z-index:2147483647;display:flex;gap:8px';

        var ib = pill(document.createElement('button'));
        ib.id = '__fdIgPill';
        ib.addEventListener('click', function () { popPill(ib); togglePanel(); });
        paintIgnorePill(ib);          // 元素还没入 DOM，必须直接传进去（否则按钮没文字，只剩一个圆球）
        bar.appendChild(ib);

        var hb = pill(document.createElement('button'));
        hb.id = '__fdHistPill';
        hb.addEventListener('click', function () { popPill(hb); toggleHistPanel(); });
        paintHistPill(hb);
        bar.appendChild(hb);

        var tb = pill(document.createElement('button'));
        tb.id = '__fdTheme';
        tb.addEventListener('click', function () {
            popPill(tb);
            setTheme(isDark() ? 'light' : 'dark');
            paintTheme(tb);
        });
        paintTheme(tb);
        bar.appendChild(tb);

        // 「窗口置顶」只有 exe 里有意义（网页里没有窗口可置顶），所以没有宿主就不建这个胶囊
        if (HOST) {
            var pb = pill(document.createElement('button'));
            pb.id = '__fdTopMost';
            pb.addEventListener('click', function () {
                popPill(pb);
                HOST.postMessage('toggle-topmost');
            });
            HOST.addEventListener('message', function (ev) {
                var d = ev.data;
                if (typeof d === 'string' && (d.indexOf('history-list ') === 0 || d.indexOf('history-ok ') === 0)) {
                    histApplyReply(d);
                    return;
                }
                paintPin(d === 'topmost-on', pb);
            });
            paintPin(false, pb);
            bar.appendChild(pb);
        }

        document.body.appendChild(bar);
        patchInputs();
        hookSaveAs();
        hookCompare();      // 比对完成 → 记一条历史
        // 历史「重新选择文件夹并对比」：选完 A 自动接着弹 B
        var ia = document.getElementById('inputFolderA');
        if (ia) ia.addEventListener('change', function () {
            if (HIST_REPICK !== 1) return;
            HIST_REPICK = 2;
            var b2 = document.getElementById('inputFolderB');
            if (b2) setTimeout(function () { b2.click(); }, 300);
        });
        var ib2 = document.getElementById('inputFolderB');
        if (ib2) ib2.addEventListener('change', function () {
            if (HIST_REPICK === 2) HIST_REPICK = 0;
        });
        histPost('history-list');
        updatePanelInfo('');
        if (HOST) HOST.postMessage('get-topmost');
    }

    // 供自动化验收调用（不参与界面逻辑）
    window.__fdIgnore = {
        state: function () {
            return JSON.stringify({ rules: rules, count: compiled.length, ignored: ignoredCount });
        },
        set: function (json) {
            var o = JSON.parse(json);
            rules.vcs = !!o.vcs; rules.temp = !!o.temp; rules.logs = !!o.logs;
            rules.custom = o.custom ? String(o.custom) : '';
            saveRules(); compile(); syncPanel(); paintIgnorePill(); updatePanelInfo('');
            return compiled.length;
        },
        isIgnored: function (rel, isDir) { return isIgnored(rel, !!isDir); },
        filterNames: function (namesJson) {
            var names = JSON.parse(namesJson), arr = [], i;
            // 真的 webkitRelativePath 形如 '根目录/子路径'，这里补一个假根目录，好让过滤逻辑走到同一分支
            for (i = 0; i < names.length; i++) arr.push({ name: names[i], webkitRelativePath: 'root/' + names[i] });
            var res = filterList(arr), kept = [];
            for (i = 0; i < res.kept.length; i++) kept.push(res.kept[i].name);
            return JSON.stringify({ kept: kept, ignored: res.ignored });
        }
    };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', ready);
    else ready();
})();
";

        private static void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string msg = null;
            try { msg = e.TryGetWebMessageAsString(); }
            catch (Exception) { }
            if (msg == null) return;

            // 历史记录走"字符串命令 + base64 载荷"，宿主只做存取，不解析页面数据
            if (msg.StartsWith("history-", StringComparison.Ordinal))
            {
                HandleHistory(msg);
                return;
            }

            if (msg == "toggle-topmost")
            {
                try { _form.TopMost = !_form.TopMost; }
                catch (Exception) { }
                PostTopMostState();
            }
            else if (msg == "get-topmost")
            {
                PostTopMostState();
            }
        }

        // ---- 历史记录（网页版）-------------------------------------------
        // 页面把每条记录编成一行 base64 文本；宿主只负责存/取/删/裁剪，
        // 存在 %LOCALAPPDATA%\FolderDiff\历史.dat（Deflate 压缩，UTF-8 带 BOM）。
        private static void HandleHistory(string msg)
        {
            try
            {
                string[] parts = msg.Split(new char[] { ' ' }, 2);
                string cmd = parts[0];
                string arg = parts.Length > 1 ? parts[1] : string.Empty;

                if (cmd == "history-add")
                {
                    List<string> list = HistoryStore.Load();
                    list.Insert(0, arg.Trim());
                    HistoryStore.TrimAndSave(list);
                    Reply(list);
                }
                else if (cmd == "history-list")
                {
                    List<string> list = HistoryStore.Load();
                    ReplyList("history-list", list);
                }
                else if (cmd == "history-delete")
                {
                    string raw = FromBase64(arg);
                    List<string> list = HistoryStore.Load();
                    string[] idx = raw.Split(',');
                    List<int> kill = new List<int>();
                    for (int i = 0; i < idx.Length; i++)
                    {
                        int n;
                        if (int.TryParse(idx[i], out n) && n >= 0 && n < list.Count) kill.Add(n);
                    }
                    kill.Sort();
                    for (int i = kill.Count - 1; i >= 0; i--) list.RemoveAt(kill[i]);
                    HistoryStore.TrimAndSave(list);
                    Reply(list);
                }
                else if (cmd == "history-clear")
                {
                    List<string> list = new List<string>();
                    HistoryStore.TrimAndSave(list);
                    Reply(list);
                }
            }
            catch (Exception ex) { HistoryLog("error " + ex.Message); }
        }

        /// <summary>历史记录的小日志（只在出问题时用得上，最多留 32KB）。</summary>
        private static void HistoryLog(string line)
        {
            try
            {
                string p = Path.Combine(Program.BaseDir, "历史.log");
                FileInfo fi = new FileInfo(p);
                if (fi.Exists && fi.Length > 32 * 1024) File.Delete(p);
                File.AppendAllText(p, DateTime.Now.ToString("HH:mm:ss") + "  " + line + "\r\n");
            }
            catch (Exception) { }
        }

        private static void Reply(List<string> list)
        {
            ReplyList("history-ok " + list.Count + " " + HistoryStore.BytesOf(list), list);
        }

        /// <summary>
        /// 回给页面。注意：存进来的是"页面编好的每行 base64"，这里要先解回原文再整体 base64 一次，
        /// 否则页面会拿到 base64 的 base64 而解析失败（踩过一次）。
        /// </summary>
        private static void ReplyList(string prefix, List<string> list)
        {
            List<string> lines = new List<string>();
            for (int i = 0; i < list.Count; i++)
            {
                string s = FromBase64(list[i]);
                lines.Add(s.Length > 0 ? s : list[i]);
            }
            Post(prefix + " " + ToBase64(string.Join("\n", lines.ToArray())));
        }

        private static void Post(string text)
        {
            try
            {
                _webView.CoreWebView2.PostWebMessageAsString(text);
            }
            catch (Exception ex) { HistoryLog("post failed: " + ex.Message); }
        }

        private static string ToBase64(string s)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
        }

        private static string FromBase64(string s)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
            catch (Exception) { return string.Empty; }
        }

        internal static void PostTopMostState()
        {
            try
            {
                bool on = _form != null && _form.TopMost;
                _webView.CoreWebView2.PostWebMessageAsString(on ? "topmost-on" : "topmost-off");
            }
            catch (Exception) { }
        }

        // 导出 ZIP 时弹出「另存为」，而不是静默丢进下载目录
        private static void OnDownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            try
            {
                string name = "对比结果.zip";
                if (!string.IsNullOrEmpty(e.ResultFilePath))
                {
                    string n = Path.GetFileName(e.ResultFilePath);
                    if (!string.IsNullOrEmpty(n)) name = n;
                }

                using (SaveFileDialog dlg = new SaveFileDialog())
                {
                    dlg.Title = "保存导出文件";
                    dlg.FileName = name;
                    dlg.Filter = "ZIP 压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*";
                    dlg.OverwritePrompt = true;
                    dlg.RestoreDirectory = true;
                    if (dlg.ShowDialog(_form) == DialogResult.OK)
                    {
                        e.ResultFilePath = dlg.FileName;
                        e.Handled = true;
                    }
                    else
                    {
                        e.Cancel = true;
                        e.Handled = true;
                    }
                }
            }
            catch (Exception)
            {
                // 交给 WebView2 默认行为
            }
        }

        private static void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            try { System.Diagnostics.Process.Start(e.Uri); }
            catch (Exception) { }
        }

        private static void OnProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            try
            {
                if (_webView != null && _webView.CoreWebView2 != null) _webView.CoreWebView2.Reload();
            }
            catch (Exception) { }
        }

        // 没有 WebView2 运行时的机器上，退回系统默认浏览器，程序仍然可用
        private static void FallbackToBrowser(Exception ex)
        {
            if (_fallbackDone) return;
            _fallbackDone = true;

            string html = Path.Combine(Program.WebDir, "index.html");
            DialogResult r = MessageBox.Show(
                "无法启动内置浏览器内核（WebView2 Runtime）。\r\n\r\n" + ex.Message +
                "\r\n\r\n是否改用系统默认浏览器打开？",
                Program.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r == DialogResult.Yes)
            {
                try { System.Diagnostics.Process.Start(html); }
                catch (Exception) { }
            }
            try { if (_form != null) _form.Close(); }
            catch (Exception) { }
        }

        // ---- 窗口位置记忆 -------------------------------------------------

        private static string BoundsFile
        {
            get { return Path.Combine(Program.BaseDir, "window.txt"); }
        }

        private static void RestoreBounds(Form f)
        {
            try
            {
                string p = BoundsFile;
                if (!File.Exists(p)) return;
                string[] parts = File.ReadAllText(p).Split(',');
                if (parts.Length != 4) return;

                int x = int.Parse(parts[0], CultureInfo.InvariantCulture);
                int y = int.Parse(parts[1], CultureInfo.InvariantCulture);
                int w = int.Parse(parts[2], CultureInfo.InvariantCulture);
                int h = int.Parse(parts[3], CultureInfo.InvariantCulture);
                if (w < 400 || h < 300) return;

                Rectangle r = new Rectangle(x, y, w, h);
                bool onScreen = false;
                Screen[] screens = Screen.AllScreens;
                for (int i = 0; i < screens.Length; i++)
                {
                    Rectangle hit = Rectangle.Intersect(screens[i].WorkingArea, r);
                    if (hit.Width > 160 && hit.Height > 120) { onScreen = true; break; }
                }
                if (!onScreen) return;

                f.StartPosition = FormStartPosition.Manual;
                f.Bounds = r;
            }
            catch (Exception) { }
        }

        private static void SaveBounds(Form f)
        {
            try
            {
                Rectangle r = f.WindowState == FormWindowState.Normal ? f.Bounds : f.RestoreBounds;
                string s = r.X.ToString(CultureInfo.InvariantCulture) + "," +
                           r.Y.ToString(CultureInfo.InvariantCulture) + "," +
                           r.Width.ToString(CultureInfo.InvariantCulture) + "," +
                           r.Height.ToString(CultureInfo.InvariantCulture);
                File.WriteAllText(BoundsFile, s);
            }
            catch (Exception) { }
        }

        private static void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            SaveBounds(_form);
        }
    }
}
