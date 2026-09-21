using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FileDiffTool
{
    /// <summary>
    /// 主题：浅色 / 深色两套配色 + 「跟随系统 / 浅色 / 深色」三态。
    ///
    /// 三条设计原则：
    ///   ① **只用 .NET 2.0 / XP 就有的 API**（本程序要靠系统自带的 csc 编出 XP~Win11 单 exe）；
    ///   ② DWM / uxtheme 那几处 Win32 调用**全部 try/catch 兜住** —— 老系统上"少两处美化"，功能照旧；
    ///   ③ **系统不支持深色时，「跟随系统」这一档要变灰**（方案 B）：判断依据不能只看版本号
    ///      （没清单的进程读 Environment.OSVersion 会被骗成 6.2），而是看
    ///      `AppsUseLightTheme` 这个键**在不在** —— 它只在 Win10 1809+ 才有。
    ///      读不到就等于"系统没有深色模式"，此时跟随一律按**浅色**算（不能按深色，那是反的）。
    /// </summary>
    internal static partial class Theme
    {
        // ============================================================ DPI
        /// <summary>界面缩放（由 MainForm 在构造时按屏幕 DPI 设进来；自绘控件也要跟着放大）</summary>
        public static float UiScale = 1f;

        public static int S(int px) { return (int)Math.Round(px * UiScale); }

        // ============================================================ 模式
        public enum Mode { Auto = 0, Light = 1, Dark = 2 }

        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string ValueName = "AppsUseLightTheme";

        /// <summary>用户选的三态（默认跟随系统）</summary>
        public static Mode Current = Mode.Auto;

        /// <summary>系统有没有"应用深色模式"这个设置（决定「跟随系统」是否可用）</summary>
        public static bool SystemSupportsTheme = false;

        /// <summary>当前实际是不是浅色（由 Current + 系统探测算出来）</summary>
        public static bool IsLight = true;

        // ---- 给自动化验证留的口子（命令行 --theme / --no-sys-theme）----
        public static bool ForceNoSystemSupport = false;   // 假装系统不支持，用来看"灰态"
        public static bool HasStartupOverride = false;     // 命令行指定了主题，就不再读设置文件
        public static Mode StartupOverride = Mode.Auto;

        // ============================================================ 调色板
        public static Color Bg, Panel, PanelAlt, Border, Text, SubText, Dim, Link, Accent;
        public static Color SelBg, SelText, BtnFace, BtnBorder, DropHover, RowAlt;

        private static void ApplyPalette(bool light)
        {
            IsLight = light;
            if (light)
            {
                Bg = Color.FromArgb(0xFF, 0xFF, 0xFF);
                Panel = Color.FromArgb(0xFA, 0xFB, 0xFC);
                PanelAlt = Color.FromArgb(0xF7, 0xF8, 0xFA);
                Border = Color.FromArgb(0xE0, 0xE3, 0xE8);
                Text = Color.FromArgb(0x19, 0x1E, 0x26);
                SubText = Color.FromArgb(0x64, 0x6A, 0x74);
                Dim = Color.FromArgb(0xAA, 0xAF, 0xB6);
                Link = Color.FromArgb(0x32, 0x64, 0xB9);
                Accent = Color.FromArgb(0x28, 0x5A, 0xAF);
                SelBg = Color.FromArgb(0xCC, 0xE4, 0xFF);
                SelText = Color.FromArgb(0x10, 0x14, 0x1A);
                BtnFace = Color.FromArgb(0xF3, 0xF4, 0xF6);
                BtnBorder = Color.FromArgb(0xC8, 0xCC, 0xD2);
                DropHover = Color.FromArgb(0xEC, 0xF4, 0xFF);
                RowAlt = Color.FromArgb(0xFA, 0xFB, 0xFD);
            }
            else
            {
                Bg = Color.FromArgb(0x14, 0x17, 0x1C);
                Panel = Color.FromArgb(0x1A, 0x1E, 0x25);
                PanelAlt = Color.FromArgb(0x16, 0x19, 0x1F);
                Border = Color.FromArgb(0x2C, 0x31, 0x3A);
                Text = Color.FromArgb(0xE6, 0xEA, 0xF0);
                SubText = Color.FromArgb(0x8A, 0x94, 0xA6);
                Dim = Color.FromArgb(0x5C, 0x64, 0x72);
                Link = Color.FromArgb(0x6F, 0xA8, 0xFF);
                Accent = Color.FromArgb(0x5B, 0x9B, 0xFF);
                SelBg = Color.FromArgb(0x26, 0x4F, 0x78);
                SelText = Color.FromArgb(0xFF, 0xFF, 0xFF);
                BtnFace = Color.FromArgb(0x22, 0x27, 0x30);
                BtnBorder = Color.FromArgb(0x3A, 0x41, 0x4C);
                DropHover = Color.FromArgb(0x1E, 0x2A, 0x3A);
                RowAlt = Color.FromArgb(0x18, 0x1C, 0x22);
            }
        }

        // ============================================================ 系统探测
        /// <summary>
        /// 系统是不是"浅色"（读不到一律按浅色 —— 老系统没有这个键，绝不能当成深色）。
        /// </summary>
        public static bool ReadSystemPrefersLight()
        {
            if (ForceNoSystemSupport) return true;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    if (k == null) return true;                       // 键都没有 -> 老系统 -> 浅色
                    object v = k.GetValue(ValueName);
                    if (v == null) return true;                       // 值没有 -> 同上
                    return Convert.ToInt32(v) != 0;                   // 1=浅色 0=深色
                }
            }
            catch { return true; }
        }

        /// <summary>
        /// 系统支不支持"深色模式"这个设置。判据（缺一不可）：
        ///   ① AppsUseLightTheme 键存在（Win10 1809+ 才有）
        ///   ② 不是高对比度模式（高对比度下别自作主张）
        /// </summary>
        public static bool DetectSystemSupport()
        {
            if (ForceNoSystemSupport) return false;
            try
            {
                if (SystemInformation.HighContrast) return false;
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    if (k == null) return false;
                    return k.GetValue(ValueName) != null;
                }
            }
            catch { return false; }
        }

        /// <summary>按当前模式算出"实际该不该用浅色"</summary>
        public static bool ResolveIsLight()
        {
            if (Current == Mode.Light) return true;
            if (Current == Mode.Dark) return false;
            // Auto：系统不支持就只能浅色（此时界面上「跟随系统」是灰的）
            if (!SystemSupportsTheme) return true;
            return ReadSystemPrefersLight();
        }

        /// <summary>重新探测系统 + 应用调色板；返回是否发生了变化</summary>
        public static bool Refresh()
        {
            SystemSupportsTheme = DetectSystemSupport();
            bool light = ResolveIsLight();
            bool changed = (light != IsLight) || (Bg == Color.Empty);
            ApplyPalette(light);
            return changed;
        }

        public static string Describe(Mode m)
        {
            if (m == Mode.Light) return "浅色";
            if (m == Mode.Dark) return "深色";
            return "跟随系统";
        }

        public static string CurrentDescription()
        {
            if (Current != Mode.Auto) return Describe(Current);
            if (!SystemSupportsTheme) return "跟随系统（本系统不支持，按浅色显示）";
            return "跟随系统（当前：" + (IsLight ? "浅色" : "深色") + "）";
        }

        // ============================================================ 设置持久化
        /// <summary>%APPDATA%\文件比较器\设置.txt（手写 key=value，跟忽略规则一个做法但分成两个文件，免得互相覆盖）</summary>
        public static string SettingsPath
        {
            get
            {
                try
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "文件比较器");
                    return Path.Combine(dir, "主题.txt");
                }
                catch { return null; }
            }
        }

        public static void Load()
        {
            if (HasStartupOverride) return;
            try
            {
                string p = SettingsPath;
                if (p == null || !File.Exists(p)) return;
                string[] lines = File.ReadAllLines(p);
                for (int i = 0; i < lines.Length; i++)
                {
                    string ln = lines[i].Trim();
                    if (ln.Length == 0 || ln.StartsWith("#")) continue;
                    int eq = ln.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = ln.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = ln.Substring(eq + 1).Trim().ToLowerInvariant();
                    if (key == "theme")
                    {
                        if (val == "light") Current = Mode.Light;
                        else if (val == "dark") Current = Mode.Dark;
                        else Current = Mode.Auto;
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                string p = SettingsPath;
                if (p == null) return;
                string dir = Path.GetDirectoryName(p);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string val = Current == Mode.Light ? "light" : (Current == Mode.Dark ? "dark" : "auto");
                File.WriteAllText(p, "# 文件比较器 主题设置\r\ntheme=" + val + "\r\n");
            }
            catch { }
        }

        // ============================================================ Win32 美化（老系统上静默失败）
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled(out bool enabled);

        /// <summary>
        /// 控件自带的滚动条是系统画的，深色界面下会是一条白杠。
        /// 用 uxtheme 的主题名把它压深（Win10 1809+ 有效；老系统上调用无害）。
        /// </summary>
        public static void ApplyScrollTheme(Control c)
        {
            if (c == null || !c.IsHandleCreated) return;
            // 一次只设一个名字：连着设好几个的话，最后一个会盖掉前面那个，
            // 而 TreeView 的滚动条只认 DarkMode_Explorer、ListView 只认 DarkMode_ItemsView
            string name;
            if (IsLight) name = "Explorer";
            else if (c is ListView) name = "DarkMode_ItemsView";
            else name = "DarkMode_Explorer";
            try { SetWindowTheme(c.Handle, name, null); }
            catch { }
        }

        /// <summary>标题栏跟随深色（Win10 1809+ 用属性 20，更早的 1809 用 19；老系统上调用无害）</summary>
        public static void ApplyTitleBar(IntPtr hwnd, bool light)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                bool composition = false;
                try { DwmIsCompositionEnabled(out composition); }
                catch { return; }                       // XP 上没有 dwmapi.dll -> 直接算了
                if (!composition) return;

                int dark = light ? 0 : 1;
                int hr = DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
                if (hr != 0) DwmSetWindowAttribute(hwnd, 19, ref dark, 4);
            }
            catch { }
        }

        /// <summary>按钮：浅色下保持系统原生外观（跟以前一模一样），深色下改成自绘扁平样式</summary>
        public static void StyleButton(Button b)
        {
            if (b == null) return;
            if (IsLight)
            {
                b.FlatStyle = FlatStyle.System;
            }
            else
            {
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = BtnBorder;
                b.FlatAppearance.MouseOverBackColor = DropHover;
                b.BackColor = BtnFace;
                b.ForeColor = Text;
                b.UseVisualStyleBackColor = false;
                // 禁用态系统会改用浅色系绘制，主题色全被忽略 → 挂个 Paint 补回来。
                // 切主题会重复调用这里，所以先摘再挂，别叠起来。
                b.Paint -= DarkDisabledPaint;
                b.Paint += DarkDisabledPaint;
            }
        }

        /// <summary>
        /// 深色下 Flat 按钮一旦 Enabled=false，WinForms 就换成系统色画（浅底 + 系统灰字），
        /// 在深色界面上几乎看不见（用户反馈"右下角的按钮不明显、文字是深色的"）。
        /// 系统画完之后自己按主题补一遍底、框、字。
        /// </summary>
        private static void DarkDisabledPaint(object sender, PaintEventArgs e)
        {
            Button b = sender as Button;
            if (b == null || b.Enabled || IsLight) return;
            using (SolidBrush bg = new SolidBrush(BtnFace))
                e.Graphics.FillRectangle(bg, b.ClientRectangle);
            using (Pen p = new Pen(BtnBorder))
                e.Graphics.DrawRectangle(p, 0, 0, b.ClientSize.Width - 1, b.ClientSize.Height - 1);
            TextRenderer.DrawText(e.Graphics, b.Text, b.Font, b.ClientRectangle, Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        /// <summary>检查框/单选框：深色下改自绘（浅色仍然系统原生）</summary>
        public static void StyleChoice(ButtonBase b)
        {
            if (b == null) return;
            if (IsLight)
            {
                b.FlatStyle = FlatStyle.System;
                b.BackColor = Color.Transparent;
                b.ForeColor = Color.Black;
            }
            else
            {
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = Color.Transparent;
                b.ForeColor = Text;
            }
        }

        public static void StyleTextBox(TextBox t)
        {
            if (t == null) return;
            if (IsLight)
            {
                t.BackColor = Color.White;
                t.ForeColor = Color.Black;
                t.BorderStyle = BorderStyle.Fixed3D;
            }
            else
            {
                t.BackColor = Color.FromArgb(0x1F, 0x23, 0x2B);
                t.ForeColor = Text;
                t.BorderStyle = BorderStyle.FixedSingle;
            }
        }
    }

    /// <summary>
    /// 自己画的单选按钮。
    /// 为什么不用系统那个：WinForms 的 RadioButton 在深色底上会把小圆点画成**黑色**（背景也是深色 -> 看不见），
    /// 而它又不支持自绘（没有 DrawItem）。所以整个自己画：圆环 + 选中时的实心点，颜色全走主题。
    /// 行为仍是标准 RadioButton（点击切换、CheckedChanged 事件都照旧）。
    /// </summary>
    internal class ThemeRadio : RadioButton
    {
        public ThemeRadio()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            // 背景自己刷（用父控件的底色）—— 不依赖 WinForms 那套"透明背景"模拟，
            // 否则在某些绘制顺序下会在文字后面留一个黑方块。
            Color back = (Parent != null && Parent.BackColor != Color.Empty) ? Parent.BackColor : Theme.Bg;
            using (SolidBrush brush = new SolidBrush(back)) g.FillRectangle(brush, ClientRectangle);

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int d = Theme.S(13);                                  // 圆环直径
            int top = (Height - d) / 2;
            Rectangle ring = new Rectangle(Theme.S(1), top, d, d);

            Color c = Enabled ? (Checked ? Theme.Accent : Theme.Border) : Theme.Dim;
            using (Pen p = new Pen(c, Math.Max(1.2f, Theme.S(1) * 1.2f))) g.DrawEllipse(p, ring);
            if (Checked)
            {
                int inset = Math.Max(3, d / 4);
                using (SolidBrush b = new SolidBrush(c))
                    g.FillEllipse(b, ring.X + inset, ring.Y + inset, d - inset * 2, d - inset * 2);
            }

            Color txt = Enabled ? ForeColor : Theme.Dim;
            Rectangle tr = new Rectangle(ring.Right + Theme.S(5), 0, Width - ring.Right - Theme.S(6), Height);
            TextRenderer.DrawText(g, Text, Font, tr, txt,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>
    /// 深色工具栏渲染器（只在深色模式下挂上去；浅色模式仍然用系统原生渲染，保持老样子）。
    /// .NET 2.0 就有 ToolStripProfessionalRenderer，够用。
    /// </summary>
    internal class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        public DarkToolStripRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Theme.Panel))
                e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen p = new Pen(Theme.Border))
            {
                e.Graphics.DrawLine(p, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // 面包屑自己设了 ForeColor（当前层/上层链接色）；没设过的就用主题正文色
            Color c = e.Item.ForeColor;
            if (c == Control.DefaultForeColor || c.IsEmpty) c = Theme.Text;
            if (!e.Item.Enabled) c = Theme.Dim;
            e.TextColor = c;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            ToolStripItem it = e.Item;
            bool hot = it.Selected || it.Pressed;
            if (!hot) return;
            Rectangle r = new Rectangle(0, 0, it.Width, it.Height);
            using (SolidBrush b = new SolidBrush(it.Pressed ? Theme.SelBg : Theme.DropHover))
                e.Graphics.FillRectangle(b, r);
            if (it.Pressed)
            {
                using (Pen p = new Pen(Theme.Border))
                    e.Graphics.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (Pen p = new Pen(Theme.Border))
            {
                int y = e.Item.Height / 2;
                e.Graphics.DrawLine(p, 3, y, e.Item.Width - 4, y);
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected && !e.Item.Pressed) return;
            Rectangle r = new Rectangle(0, 0, e.Item.Width, e.Item.Height);
            using (SolidBrush b = new SolidBrush(e.Item.Pressed ? Theme.SelBg : Theme.DropHover))
                e.Graphics.FillRectangle(b, r);
        }
    }

    internal class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin { get { return Theme.Panel; } }
        public override Color ToolStripGradientMiddle { get { return Theme.Panel; } }
        public override Color ToolStripGradientEnd { get { return Theme.Panel; } }
        public override Color ToolStripBorder { get { return Theme.Border; } }
        public override Color ToolStripDropDownBackground { get { return Theme.Panel; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Panel; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Panel; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Panel; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Border; } }
        public override Color MenuItemSelected { get { return Theme.SelBg; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.SelBg; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.SelBg; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.PanelAlt; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.PanelAlt; } }
        public override Color ButtonSelectedHighlight { get { return Theme.DropHover; } }
        public override Color ButtonSelectedBorder { get { return Theme.Border; } }
        public override Color ButtonPressedHighlight { get { return Theme.SelBg; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Border; } }
    }
}
