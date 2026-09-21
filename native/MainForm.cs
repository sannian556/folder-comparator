// 主窗口：文件夹选择 → 对比 → 结果浏览 → 导出 ZIP
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FileDiffTool
{
    internal class MainForm : Form
    {
        // ── 状态 ─────────────────────────────────────────────
        private Dictionary<string, FileEntry> _mapA;
        private Dictionary<string, FileEntry> _mapB;
        private string _dirA = string.Empty;
        private string _dirB = string.Empty;
        private CompareResult _result;
        private CancelFlag _cancel = new CancelFlag();
        private volatile bool _busy;
        private float _uiScale = 1f;

        // ── 控件 ─────────────────────────────────────────────
        private Label _lblPathA, _lblInfoA, _lblPathB, _lblInfoB;
        private Button _btnPickA, _btnPickB, _btnClearA, _btnClearB;
        private TextBox _txtPathA, _txtPathB;
        private Button _btnCompare, _btnExport, _btnClear, _btnCancel, _btnContact;
        private CheckBox _chkTop;
        private Button _btnIgnore;
        private Button _btnHistory;
        private HistoryEntry _restoredEntry;   // 非空表示当前界面是历史快照还原出来的
        private HistoryEntry _lastEntry;       // 本次会话里「比对完成」刚记下的那条（导出时补记 ZIP 路径用）
        private int _historyMismatch;          // 还原时统计出来的"与记录不一致"文件数
        private IgnoreRules _rules = new IgnoreRules();
        private int _rescanStage;   // 规则变更后重扫：0 空闲 / 1 等 A 扫完 / 2 等 B 扫完
        private ThemeProgress _progress;
        private Label _lblStatus;
        private Label[] _countLabels = new Label[4];
        private TabStrip _tabs;
        private Panel[] _pages = new Panel[4];
        private ListView[] _lists = new ListView[4];
        private ContextMenuStrip _menu;

        private const int TabModified = 0;
        private const int TabOnlyB = 1;
        private const int TabOnlyA = 2;
        private const int TabSame = 3;
        private const long DiffOpenWarnBytes = 8L * 1024 * 1024;

        private string _initialA, _initialB;
        private bool _autoComparePending;
        private string _autoExportPath;
        private bool _silentMode;
        private string _dumpUiPath;   // --dump-ui <文件>：把控件树（含实际字体）写出来，便于排查布局问题
        private string _dumpHistoryPath;   // --dump-history <文件>：把历史记录解析结果写出来（自动化验收用）
        private string _dumpThemePath;     // --dump-theme <文件>：把主题状态写出来（自动化断言用）
        private int _autoRestoreIndex = -1; // --restore-history <序号>：还原第 N 条历史（配合导出路径可无人值守重打包）
        private bool _autoOpenSettings;     // --settings：启动直接把设置窗体打开（自动化截图/验证用）

        public MainForm(string[] args)
        {
            Font = TextUtil.PickUiFont(9f, false);
            Text = "文件比较器 — 文件夹对比工具";
            StartPosition = FormStartPosition.CenterScreen;
            _uiScale = DetectUiScale();
            // 主题：自绘控件要跟界面用同一个缩放系数；命令行指定了外观就不再读设置文件
            Theme.UiScale = _uiScale;
            if (!Theme.HasStartupOverride) Theme.Load();
            Theme.Refresh();
            _rules = IgnoreRules.Load();   // 上次用过的忽略规则（%APPDATA%\文件比较器\设置.txt）
            // 先设模式、后设基准 —— 反过来会被 AutoScaleMode 的 setter 重置成"当前 DPI"，
            // 那样 AutoScaleFactor = 1.0（控件完全不缩放），而 125%/150% 下字体像素却变大了，
            // 文字就会撑破控件、被压扁截断。
            AutoScaleMode = AutoScaleMode.None;   // 缩放全部自己做，见 ApplyUiScale

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int w = Math.Min(1060, (int)(wa.Width / _uiScale * 0.94));
            int h = Math.Min(760, (int)(wa.Height / _uiScale * 0.94));
            ClientSize = new Size(S(w), S(h));
            MinimumSize = new Size(S(760), S(520));
            try { Icon = Program.LoadAppIcon(); }
            catch (Exception) { }

            BuildLayout();
            ApplyUiScale(this);   // 建好控件后统一按 DPI 放大
            Theme.Apply(this);    // 再统一换肤（深色染深、浅色保持原样）
            UpdateButtons();

            // 支持 文件比较器.exe "文件夹A" "文件夹B"，也支持把两个文件夹拖到 exe 或窗口上
            if (args != null && args.Length >= 2)
            {
                // 命令行参数也走同一套规整：支持带引号、%环境变量%、以及 .lnk 快捷方式
                string e1, e2;
                string d1 = NormalizeFolderPath(args[0], out e1);
                string d2 = NormalizeFolderPath(args[1], out e2);
                if (d1 != null && d2 != null)
                {
                    _initialA = d1;
                    _initialB = d2;
                    _autoComparePending = true;

                    // 第三个参数：对比完成后自动把差异导出到这个 zip（适合批处理 / 计划任务）
                    if (args.Length >= 3 && args[2].Length > 0 && args[2][0] != '-')
                        _autoExportPath = args[2];
                }
            }
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--silent", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(args[i], "-s", StringComparison.OrdinalIgnoreCase))
                        _silentMode = true;
                    else if (string.Equals(args[i], "--dump-ui", StringComparison.OrdinalIgnoreCase) &&
                             i + 1 < args.Length)
                        _dumpUiPath = args[i + 1];
                    else if (string.Equals(args[i], "--dump-history", StringComparison.OrdinalIgnoreCase) &&
                             i + 1 < args.Length)
                        _dumpHistoryPath = args[i + 1];
                    else if (string.Equals(args[i], "--restore-history", StringComparison.OrdinalIgnoreCase) &&
                             i + 1 < args.Length)
                    {
                        int n;
                        if (int.TryParse(args[i + 1], out n)) _autoRestoreIndex = n;
                    }
                    else if (string.Equals(args[i], "--export", StringComparison.OrdinalIgnoreCase) &&
                             i + 1 < args.Length)
                        _autoExportPath = args[i + 1];
                    // 下面三个是主题相关的自动化口子（正常双击用不到）
                    else if (string.Equals(args[i], "--theme", StringComparison.OrdinalIgnoreCase) &&
                             i + 1 < args.Length)
                        ApplyStartupTheme(args[i + 1]);          // auto / light / dark
                    else if (string.Equals(args[i], "--no-sys-theme", StringComparison.OrdinalIgnoreCase))
                        Theme.ForceNoSystemSupport = true;       // 假装系统不支持深色，用来看灰态
                    else if (string.Equals(args[i], "--dump-theme", StringComparison.OrdinalIgnoreCase) &&
                             i + 1 < args.Length)
                        _dumpThemePath = args[i + 1];            // 把主题状态写出来
                    else if (string.Equals(args[i], "--settings", StringComparison.OrdinalIgnoreCase))
                        _autoOpenSettings = true;                // 启动就打开设置窗体（自动化截图用）
                }
            }
            // 上面改过主题设置的话要重算一次并重新刷界面（构造开头那会儿还没解析参数）
            if (Theme.HasStartupOverride || Theme.ForceNoSystemSupport)
            {
                Theme.Refresh();
                Theme.Apply(this);
            }
            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            // 与原网页一致的快捷键：Ctrl+Enter 开始对比，Ctrl+E 导出
            KeyPreview = true;
            KeyDown += OnMainKeyDown;

            // 自动化：把主题状态写出来就退出（放最后，顺便验证上面那通换肤没把构造搞崩）
            if (_dumpThemePath != null)
            {
                DumpThemeState(_dumpThemePath);
                Environment.Exit(0);
            }

            // 自动化：启动就把设置窗体弹出来（要等窗口显示后再弹，模态窗体才有正确的父窗口位置）
            if (_autoOpenSettings)
                Shown += delegate { OnOpenSettings(this, EventArgs.Empty); };
        }

        private void OnMainKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Enter && _btnCompare.Enabled)
            {
                OnCompare(null, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.E && _btnExport.Enabled)
            {
                OnExport(null, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F5 && !_busy && _mapA != null && _mapB != null)
            {
                OnCompare(null, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.T)
            {
                // Ctrl+T：窗口置顶开关（勾选框状态变化会自己应用）
                _chkTop.Checked = !_chkTop.Checked;
                e.Handled = true;
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (_busy) return;
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (_busy) return;
            string[] items = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (items == null) return;

            List<string> dirs = new List<string>();
            for (int i = 0; i < items.Length; i++)
                if (Directory.Exists(items[i])) dirs.Add(items[i]);

            if (dirs.Count >= 2)
            {
                SetBusy(true, "正在扫描拖入的文件夹…");
                StartScan(dirs[0], true, true);
                StartScan(dirs[1], false, true);
            }
            else if (dirs.Count == 1)
            {
                bool isA = (_mapA == null);
                StartScan(dirs[0], isA, false);
            }
            else
            {
                MessageBox.Show(this, "请拖入两个文件夹。", "文件比较器",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 窗口真显示出来之后再刷一次主题：构造阶段控件还没句柄，有些样式（按钮的 FlatStyle 之类）
            // 改了要等建句柄时才真正生效，不补这一下会留下一批系统配色的白底控件
            Theme.Apply(this);
            if (_dumpUiPath != null)
            {
                DumpUiTo(_dumpUiPath);
                // 附一行主题状态：dump 出来的颜色必须能对上"当时到底是浅色还是深色"，
                // 不然很容易拿着浅色的 dump 去解释深色的截图（已经白排查过一次）
                try
                {
                    File.AppendAllText(_dumpUiPath, "--- theme=" + Theme.Describe(Theme.Current)
                        + " effective=" + (Theme.IsLight ? "light" : "dark")
                        + " bg=" + HexOf(Theme.Bg) + " panel=" + HexOf(Theme.Panel)
                        + " btnFace=" + HexOf(Theme.BtnFace)
                        + " visited=" + Theme.LastVisited + " changed=" + Theme.LastChanged + "\r\n",
                        new UTF8Encoding(false));
                }
                catch (Exception) { }
                Close();
                return;
            }
            if (_dumpHistoryPath != null)
            {
                DumpHistoryTo(_dumpHistoryPath);
                Close();
                return;
            }
            if (_autoRestoreIndex >= 0)
            {
                // 自动化/无人值守：还原第 N 条历史；给了导出路径就顺手重新打包
                List<HistoryEntry> all = HistoryStore.Load();
                if (_autoRestoreIndex < all.Count)
                {
                    RestoreHistory(all[_autoRestoreIndex]);
                    if (_autoExportPath != null)
                    {
                        StartExport(_autoExportPath);
                        return;
                    }
                }
                if (_silentMode) Close();
                return;
            }
            if (_autoComparePending)
            {
                _autoComparePending = false;
                SetBusy(true, "正在扫描两个文件夹…");
                StartScan(_initialA, true, true);
                StartScan(_initialB, false, true);
            }
        }

        /// <summary>把控件树（类型 / 位置尺寸 / 实际生效的字体）写到文件，用于排查渲染问题。</summary>
        internal void DumpUiTo(string path)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("uiScale=" + _uiScale.ToString("0.###") +
                          "  form.Font=" + Font.Name + " " + Font.SizeInPoints.ToString("0.##") + "pt" +
                          "  ClientSize=" + ClientSize.Width + "x" + ClientSize.Height);
            DumpUi(this, sb, 0);
            System.IO.File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
        }

        /// <summary>把历史记录解析结果写成文本（自动化验收 / 排查用）。</summary>
        internal void DumpHistoryTo(string path)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            List<HistoryEntry> all = HistoryStore.Load();
            sb.AppendLine("历史文件 = " + HistoryStore.FilePath);
            sb.AppendLine("条数 = " + all.Count + "   文件大小 = " + HistoryStore.CurrentBytes() + " 字节");
            for (int i = 0; i < all.Count; i++)
            {
                HistoryEntry e = all[i];
                sb.AppendLine("[" + i + "] " + e.TimeFull);
                sb.AppendLine("    A = " + e.DirA);
                sb.AppendLine("    B = " + e.DirB);
                sb.AppendLine("    ZIP = " + (e.ZipPath != null && e.ZipPath.Length > 0 ? e.ZipPath : "(未导出)"));
                sb.AppendLine("    计数 = " + e.CountModified + "," + e.CountOnlyB + "," + e.CountOnlyA + "," + e.CountSame);
                sb.AppendLine("    忽略规则 = " + e.IgnoreSummary);
                sb.AppendLine("    清单 = " + e.Items.Count + " 条");
                for (int j = 0; j < e.Items.Count; j++)
                {
                    HistoryItem it = e.Items[j];
                    sb.AppendLine("      " + it.Kind + "  " + it.Rel + "  " + it.SizeA + "  " + it.SizeB);
                }
            }
            System.IO.File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
        }

        /// <summary>颜色写成十六进制；全透明单独标出来（Color.Transparent 的 R/G/B 也是 FF/FF/FF，
        /// 只取 RGB 会显示成"白色"，曾据此误判"深色下这片还是白底"）</summary>
        private static string HexOf(Color c)
        {
            if (c.A == 0) return "透明";
            return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        /// <summary>命令行 --theme：指定启动外观，不再读设置文件</summary>
        private static void ApplyStartupTheme(string name)
        {
            Theme.HasStartupOverride = true;
            Theme.Mode m = Theme.Mode.Auto;
            if (string.Equals(name, "light", StringComparison.OrdinalIgnoreCase))
                m = Theme.Mode.Light;
            else if (string.Equals(name, "dark", StringComparison.OrdinalIgnoreCase))
                m = Theme.Mode.Dark;
            // Current 和 StartupOverride 都设上：解析实际明暗和界面读的可能不是同一个字段
            Theme.StartupOverride = m;
            Theme.Current = m;
        }

        /// <summary>命令行 --dump-theme：把主题状态写出来（自动化断言用）</summary>
        private static void DumpThemeState(string path)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("mode=" + Theme.Describe(Theme.Current));
            sb.AppendLine("effective=" + (Theme.IsLight ? "light" : "dark"));
            sb.AppendLine("sysSupport=" + (Theme.SystemSupportsTheme ? "yes" : "no"));
            sb.AppendLine("sysPrefersLight=" + (Theme.ReadSystemPrefersLight() ? "yes" : "no"));
            sb.AppendLine("autoEnabled=" + (Theme.SystemSupportsTheme ? "yes" : "no"));
            sb.AppendLine("bg=" + HexOf(Theme.Bg));
            sb.AppendLine("panel=" + HexOf(Theme.Panel));
            sb.AppendLine("text=" + HexOf(Theme.Text));
            sb.AppendLine("settings=" + Theme.SettingsPath);
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private void DumpUi(Control c, System.Text.StringBuilder sb, int depth)
        {
            sb.Append(new string(' ', depth * 2));
            sb.Append(c.GetType().Name);
            sb.Append("  ").Append(c.Left).Append(',').Append(c.Top).Append(' ')
              .Append(c.Width).Append('x').Append(c.Height);
            Font f = c.Font;
            sb.Append("  font=").Append(f.Name).Append(' ').Append(f.SizeInPoints.ToString("0.##"))
              .Append("pt h=").Append(f.Height);
            sb.Append("  client=").Append(c.ClientSize.Width).Append('x').Append(c.ClientSize.Height);
            sb.Append("  back=").Append(HexOf(c.BackColor)).Append(" fore=").Append(HexOf(c.ForeColor));
            if (c is ListView) sb.Append(" ownerDraw=").Append(((ListView)c).OwnerDraw);
            if (c is Button) sb.Append(" flat=").Append(((Button)c).FlatStyle);
            if (!c.Enabled) sb.Append(" DISABLED");
            Label lb = c as Label;
            if (lb != null) sb.Append("  autosize=").Append(lb.AutoSize)
                             .Append(" ellipsis=").Append(lb.AutoEllipsis)
                             .Append(" pref=").Append(lb.GetPreferredSize(new Size(10000, 10000)).Height);
            string t = c.Text;
            if (t == null) t = "";
            t = t.Replace("\r", " ").Replace("\n", " ");
            if (t.Length > 34) t = t.Substring(0, 34) + "…";
            sb.Append("  text=\"").Append(t).Append('"');
            sb.AppendLine();
            for (int i = 0; i < c.Controls.Count; i++) DumpUi(c.Controls[i], sb, depth + 1);
        }

        // ── 界面搭建 ─────────────────────────────────────────
        /// <summary>
        /// 行高缩放系数：设计基准是 9pt 微软雅黑（96 DPI 下行高约 15px）。
        /// WinForms 的 AutoScaleMode.Font 会缩放控件尺寸，但 TableLayoutPanel 的
        /// Absolute 行高不参与缩放 —— 高 DPI 或系统字体偏大时，控件被放大而行高不变，
        /// 文字就会被压扁/截断（已在 125% DPI 等价的 11pt 环境下复现）。
        /// 这里按实际字体行高换算，让行高与控件同步。
        /// </summary>
        /// <summary>按系统 DPI 计算界面缩放系数（96 DPI = 1.0）。</summary>
        private static float DetectUiScale()
        {
            float s = 1f;
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) s = g.DpiY / 96f; }
            catch (Exception) { }
            if (s < 1f) s = 1f;
            if (s > 4f) s = 4f;
            return s;
        }

        private int S(int px) { return (int)Math.Round(px * _uiScale); }
        private float Sf(float px) { return px * _uiScale; }

        /// <summary>
        /// 递归把固定尺寸的控件按 DPI 放大（Dock/锚定 Right 的控件由布局负责，不重复缩放）。
        /// </summary>
        private void ApplyUiScale(Control parent)
        {
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control c = parent.Controls[i];
                if (c.Dock == DockStyle.None)
                {
                    c.Location = new Point(S(c.Left), S(c.Top));
                    c.Height = S(c.Height);
                    if ((c.Anchor & AnchorStyles.Right) == 0) c.Width = S(c.Width);
                }
                else if (c.Dock == DockStyle.Top || c.Dock == DockStyle.Bottom)
                {
                    c.Height = S(c.Height);
                }
                else if (c.Dock == DockStyle.Left || c.Dock == DockStyle.Right)
                {
                    c.Width = S(c.Width);
                }
                if (c.Controls.Count > 0) ApplyUiScale(c);
            }
        }

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.Padding = new Padding(12, 10, 12, 10);
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowCount = 7;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f * _uiScale));   // 标题
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 156f * _uiScale));  // 文件夹
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f * _uiScale));   // 按钮
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f * _uiScale));   // 进度（内容需 48px，见 BuildProgressArea）
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f * _uiScale));   // 汇总
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // 结果
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f * _uiScale));   // 右下角：联系作者

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildFolderArea(), 0, 1);
            root.Controls.Add(BuildActionBar(), 0, 2);
            root.Controls.Add(BuildProgressArea(), 0, 3);
            root.Controls.Add(BuildSummaryArea(), 0, 4);

            // 分页区：不用原生 TabControl（它的标签带边线和外框是系统按浅色画的，改不动），
            // 换成"自绘标签条 + 内容面板"，这样标签、边线、背景全归主题管。
            _tabs = new TabStrip();
            _tabs.Dock = DockStyle.Top;
            _tabs.Height = S(28);
            string[] titles = new string[] { "内容不同", "仅在 B 中存在", "仅在 A 中存在", "完全相同" };
            _tabs.Titles = titles;

            Panel content = new Panel();
            content.Dock = DockStyle.Fill;
            for (int i = 0; i < 4; i++)
            {
                _pages[i] = new Panel();
                _pages[i].Dock = DockStyle.Fill;
                _pages[i].Visible = (i == 0);
                _lists[i] = BuildListView(i);
                _pages[i].Controls.Add(_lists[i]);
                content.Controls.Add(_pages[i]);
            }
            _tabs.SelectedIndexChanged += delegate
            {
                for (int i = 0; i < _pages.Length; i++)
                    _pages[i].Visible = (i == _tabs.SelectedIndex);
            };

            Panel tabHost = new Panel();
            tabHost.Dock = DockStyle.Fill;
            tabHost.Controls.Add(content);      // 先加内容（Fill），后加标签条（Top）—— Dock 顺序才对
            tabHost.Controls.Add(_tabs);
            root.Controls.Add(tabHost, 0, 5);
            root.Controls.Add(BuildContactBar(), 0, 6);

            Controls.Add(root);

            _menu = new ContextMenuStrip();
            _menu.Items.Add("查看差异", null, OnMenuDiff);
            _menu.Items.Add("在资源管理器中显示", null, OnMenuReveal);
            _menu.Items.Add("打开 A 版本", null, OnMenuOpenA);
            _menu.Items.Add("打开 B 版本", null, OnMenuOpenB);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("复制相对路径", null, OnMenuCopyPath);
        }

        // ── 右下角「联系作者」────────────────────────────────
        private const string AuthorUrl = "https://b23.tv/7ojZmWb";

        /// <summary>
        /// 窗口右下角常驻的小按钮：点了用默认浏览器打开作者主页。
        /// 用 FlowLayoutPanel（RightToLeft）靠右对齐，窗口拉宽拉窄都贴着右下角。
        /// </summary>
        private Control BuildContactBar()
        {
            FlowLayoutPanel f = new FlowLayoutPanel();
            f.Dock = DockStyle.Fill;
            f.FlowDirection = FlowDirection.RightToLeft;
            f.WrapContents = false;
            f.Margin = new Padding(0, 2, 0, 0);

            Button b = new Button();
            b.Text = "联系作者";
            b.Size = new Size(96, 28);
            b.Cursor = Cursors.Hand;
            b.Click += OnContactAuthor;
            f.Controls.Add(b);
            _btnContact = b;

            ToolTip tip = new ToolTip();
            tip.SetToolTip(b, "用浏览器打开作者的 B 站主页");

            // 「设置」（外观三档等）：跟「联系作者」并排，一起贴着右下角。
            // FlowDirection 是 RightToLeft，后加的会排在更左边。
            Button st = new Button();
            st.Text = "设置";
            st.Size = new Size(96, 28);
            st.Cursor = Cursors.Hand;
            st.Click += OnOpenSettings;
            f.Controls.Add(st);
            tip.SetToolTip(st, "外观（跟随系统 / 浅色 / 深色）");

            return f;
        }

        private void OnOpenSettings(object sender, EventArgs e)
        {
            using (SettingsForm dlg = new SettingsForm(this))
            {
                dlg.ShowDialog(this);
            }
            Theme.Apply(this);     // 设置里可能改过外观，回来再刷一遍
            UpdateButtons();
        }

        private void OnContactAuthor(object sender, EventArgs e)
        {
            try { System.Diagnostics.Process.Start(AuthorUrl); }
            catch (Exception ex)
            {
                MessageBox.Show("打开浏览器失败：\r\n\r\n" + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private Control BuildHeader()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Fill;

            Label title = new Label();
            title.Text = "文件夹对比工具";
            title.Font = TextUtil.PickUiFont(15f, true);
            title.ForeColor = Theme.Mix(Color.FromArgb(38, 50, 66));
            title.AutoSize = true;
            title.Location = new Point(2, 2);
            p.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "选择两个文件夹，快速找出文件差异 · 支持文本内容逐行对比 · 可导出差异文件为 ZIP";
            sub.ForeColor = Theme.Mix(Color.FromArgb(110, 120, 132));
            sub.AutoSize = true;
            sub.Location = new Point(4, 34);
            p.Controls.Add(sub);

            return p;
        }

        private Control BuildFolderArea()
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 2;
            t.RowCount = 1;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            t.Controls.Add(BuildFolderBox(true), 0, 0);
            t.Controls.Add(BuildFolderBox(false), 1, 0);
            return t;
        }

        private Control BuildFolderBox(bool isA)
        {
            GroupBox box = new GroupBox();
            box.Dock = DockStyle.Fill;
            box.Text = isA ? "  文件夹 A（源）  " : "  文件夹 B（目标）  ";
            box.Padding = new Padding(8);
            box.Margin = new Padding(isA ? 0 : 5, 0, isA ? 5 : 0, 0);

            Label path = new Label();
            path.Text = "尚未选择文件夹";
            path.ForeColor = Theme.Mix(Color.Gray);
            path.AutoEllipsis = true;
            path.Location = new Point(12, 26);
            path.Size = new Size(420, 18);
            path.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            box.Controls.Add(path);

            Label info = new Label();
            info.Text = "";
            info.ForeColor = Theme.Mix(Color.FromArgb(74, 108, 247));
            info.AutoEllipsis = true;
            info.Location = new Point(12, 46);
            info.Size = new Size(420, 18);
            info.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            box.Controls.Add(info);

            Button pick = new Button();
            pick.Text = "选择文件夹…";
            pick.Location = new Point(12, 72);
            pick.Size = new Size(108, 26);
            pick.Click += isA ? new EventHandler(OnPickA) : new EventHandler(OnPickB);
            box.Controls.Add(pick);

            Button clr = new Button();
            clr.Text = "清除";
            clr.Location = new Point(126, 72);
            clr.Size = new Size(60, 26);
            clr.Enabled = false;
            clr.Click += isA ? new EventHandler(OnClearA) : new EventHandler(OnClearB);
            box.Controls.Add(clr);

            // 手动输入 / 粘贴路径，也可以把文件夹直接拖到这个框里
            // （也用于绕开"浏览文件夹"对话框无法选中 .lnk 快捷方式的限制）
            Label hint = new Label();
            hint.Text = "文件拖拽或路径粘贴";
            hint.ForeColor = Theme.Mix(Color.FromArgb(120, 130, 142));
            hint.Location = new Point(12, 104);
            hint.Size = new Size(180, 16);
            hint.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            box.Controls.Add(hint);

            TextBox txt = new TextBox();
            txt.Location = new Point(12, 124);
            txt.Size = new Size(286, 22);
            txt.Anchor = AnchorStyles.Top | AnchorStyles.Left;   // 不锚 Right：否则会以控件的默认父宽算出错误边距而溢出
            txt.AllowDrop = true;
            txt.KeyDown += new KeyEventHandler(OnPathBoxKeyDown);
            txt.DragEnter += new DragEventHandler(OnPathBoxDragEnter);
            txt.DragDrop += new DragEventHandler(OnPathBoxDragDrop);
            box.Controls.Add(txt);

            Button go = new Button();
            go.Text = "确定";
            go.Location = new Point(304, 123);
            go.Size = new Size(52, 24);
            go.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            go.Click += isA ? new EventHandler(OnPathGoA) : new EventHandler(OnPathGoB);
            box.Controls.Add(go);

            if (isA)
            {
                _lblPathA = path; _lblInfoA = info; _btnPickA = pick; _btnClearA = clr; _txtPathA = txt;
            }
            else
            {
                _lblPathB = path; _lblInfoB = info; _btnPickB = pick; _btnClearB = clr; _txtPathB = txt;
            }
            return box;
        }

        private Control BuildActionBar()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Fill;

            _btnCompare = new Button();
            _btnCompare.Text = "开始对比";
            _btnCompare.Size = new Size(120, 32);
            _btnCompare.Location = new Point(0, 8);
            _btnCompare.Font = TextUtil.PickUiFont(9f, true);
            _btnCompare.Click += OnCompare;
            p.Controls.Add(_btnCompare);

            _btnExport = new Button();
            _btnExport.Text = "导出差异文件 (ZIP)";
            _btnExport.Size = new Size(156, 32);
            _btnExport.Location = new Point(128, 8);
            _btnExport.Enabled = false;
            _btnExport.Click += OnExport;
            p.Controls.Add(_btnExport);

            _btnClear = new Button();
            _btnClear.Text = "清除结果";
            _btnClear.Size = new Size(88, 32);
            _btnClear.Location = new Point(292, 8);
            _btnClear.Enabled = false;
            _btnClear.Click += OnClearResults;
            p.Controls.Add(_btnClear);

            _btnCancel = new Button();
            _btnCancel.Text = "取消";
            _btnCancel.Size = new Size(72, 32);
            _btnCancel.Location = new Point(388, 8);
            _btnCancel.Visible = false;
            _btnCancel.Click += OnCancel;
            p.Controls.Add(_btnCancel);

            // 窗口置顶开关（也可以按 Ctrl+T）
            _chkTop = new CheckBox();
            _chkTop.Text = "窗口置顶";
            _chkTop.AutoSize = false;
            _chkTop.Size = new Size(100, 24);
            _chkTop.Location = new Point(476, 12);
            _chkTop.ForeColor = Theme.Mix(Color.FromArgb(70, 80, 92));
            _chkTop.CheckedChanged += OnToggleTopMost;
            p.Controls.Add(_chkTop);

            // 忽略规则（.git、node_modules、*.log 之类不参与对比）
            _btnIgnore = new Button();
            _btnIgnore.Size = new Size(142, 24);
            _btnIgnore.Location = new Point(586, 12);
            _btnIgnore.Click += OnIgnoreRules;
            p.Controls.Add(_btnIgnore);
            UpdateIgnoreButton();

            // 历史记录（每次导出成功存一条，可还原快照、可重新打包）
            _btnHistory = new Button();
            _btnHistory.Text = "历史记录…";
            _btnHistory.Size = new Size(118, 24);
            _btnHistory.Location = new Point(736, 12);
            _btnHistory.Click += OnHistory;
            p.Controls.Add(_btnHistory);

            return p;
        }

        // ── 历史记录 ─────────────────────────────────────────
        private void OnHistory(object sender, EventArgs e)
        {
            if (_busy) return;

            HistoryForm f = new HistoryForm();
            DialogResult dr = f.ShowDialog(this);
            HistoryEntry target = f.RestoreTarget;
            bool recompare = f.RecompareRequested;
            f.Dispose();
            if (dr != DialogResult.OK || target == null) return;

            if (recompare)
            {
                // 「重新对比」：把两个路径填好，按当前内容重新扫、重新比
                _restoredEntry = null;
                _historyMismatch = 0;
                StartScan(target.DirA, true, true);
                StartScan(target.DirB, false, true);
                return;
            }
            RestoreHistory(target);
        }

        /// <summary>把历史快照还原到界面上（清单照快照，文件路径指向当前磁盘）。</summary>
        private void RestoreHistory(HistoryEntry h)
        {
            bool hasA = Directory.Exists(h.DirA);
            bool hasB = Directory.Exists(h.DirB);

            CompareResult res = new CompareResult();
            res.NameA = HistoryEntry.FolderName(h.DirA);
            res.NameB = HistoryEntry.FolderName(h.DirB);
            res.Same.Clear();

            int mismatch = 0;
            for (int i = 0; i < h.Items.Count; i++)
            {
                HistoryItem hi = h.Items[i];
                CompareItem it = new CompareItem();
                it.RelPath = hi.Rel;

                if (hi.Kind == 'M' || hi.Kind == 'A')
                {
                    it.A = MakeSnapshotEntry(h.DirA, hi.Rel, hi.SizeA);
                    if (it.A != null && it.A.Size != hi.SizeA) mismatch++;
                }
                if (hi.Kind == 'M' || hi.Kind == 'B')
                {
                    it.B = MakeSnapshotEntry(h.DirB, hi.Rel, hi.SizeB);
                    if (it.B != null && it.B.Size != hi.SizeB) mismatch++;
                }
                if ((hi.Kind == 'M' || hi.Kind == 'A') && it.A == null) mismatch++;
                if ((hi.Kind == 'M' || hi.Kind == 'B') && it.B == null) mismatch++;

                it.SizeA = it.A != null ? it.A.Size : hi.SizeA;
                it.SizeB = it.B != null ? it.B.Size : hi.SizeB;
                if (hi.Kind == 'A') it.Note = "仅在 A";
                else if (hi.Kind == 'B') it.Note = "仅在 B";

                if (hi.Kind == 'M') res.Modified.Add(it);
                else if (hi.Kind == 'A') res.OnlyInA.Add(it);
                else res.OnlyInB.Add(it);
            }
            // 「完全相同」当时只存了计数，这里补一个占位行说明
            for (int i = 0; i < h.CountSame; i++)
            {
                CompareItem it = new CompareItem();
                it.RelPath = "（历史记录未保存该清单）";
                it.Note = "完全相同";
                res.Same.Add(it);
            }

            _restoredEntry = h;
            _historyMismatch = mismatch;
            _mapA = null;
            _mapB = null;
            _dirA = h.DirA;
            _dirB = h.DirB;

            _lblPathA.Text = h.DirA + (hasA ? "" : "（已被删除或已被移除）");
            _lblPathA.ForeColor = hasA ? Theme.Mix(Color.FromArgb(38, 50, 66)) : Theme.Mix(Color.FromArgb(190, 60, 60));
            _lblPathB.Text = h.DirB + (hasB ? "" : "（已被删除或已被移除）");
            _lblPathB.ForeColor = hasB ? Theme.Mix(Color.FromArgb(38, 50, 66)) : Theme.Mix(Color.FromArgb(190, 60, 60));

            string infoA = h.CountModified + h.CountOnlyA + h.CountOnlyB + h.CountSame + " 个文件（历史快照）";
            _lblInfoA.Text = infoA;
            _lblInfoB.Text = infoA;
            _btnClearA.Enabled = true;
            _btnClearB.Enabled = true;

            _result = res;
            RenderResults(res);
            _progress.Value = 100;

            string msg = "历史记录 " + h.TimeFull + " 那次对比：" + h.CountModified + " 个内容不同，"
                + h.CountOnlyB + " 个新增，" + h.CountOnlyA + " 个缺失，" + h.CountSame + " 个相同。";
            if (!hasA) msg += "　A 文件夹已被删除或已被移除 —— 打包不可用（清单仍可查看）。";
            if (!hasB) msg += "　B 文件夹已被删除或已被移除 —— 打包不可用（清单仍可查看）。";
            if (hasA && hasB && mismatch > 0) msg += "　其中 " + mismatch + " 个文件与记录不一致。";
            SetStatus(msg);
            UpdateButtons();
        }

        private static FileEntry MakeSnapshotEntry(string root, string rel, long size)
        {
            try
            {
                string full = Path.Combine(root, rel);
                if (!File.Exists(full)) return null;
                FileEntry e = new FileEntry();
                e.FullPath = full;
                e.RelPath = rel;
                e.Size = new FileInfo(full).Length;
                e.Modified = File.GetLastWriteTime(full);
                return e;
            }
            catch (Exception) { return null; }
        }

        /// <summary>按钮上显示当前规则条数，一眼能看出过滤有没有开着。</summary>
        private void UpdateIgnoreButton()
        {
            if (_btnIgnore == null) return;
            int n = _rules == null ? 0 : _rules.RuleCount;
            _btnIgnore.Text = n == 0 ? "忽略规则…" : ("忽略规则 (" + n + ")");
            _btnIgnore.ForeColor = n == 0 ? Theme.Mix(Color.FromArgb(70, 80, 92)) : Theme.Mix(Color.FromArgb(190, 60, 60));
        }

        /// <summary>打开忽略规则设置；规则变了就把已载入的文件夹按新规则重扫一遍。</summary>
        private void OnIgnoreRules(object sender, EventArgs e)
        {
            if (_busy) return;

            IgnoreForm f = new IgnoreForm(_rules);
            DialogResult dr = f.ShowDialog(this);
            IgnoreRules next = f.Result;
            f.Dispose();
            if (dr != DialogResult.OK || next == null) return;

            bool changed = !_rules.SameAs(next);
            _rules = next;
            UpdateIgnoreButton();
            if (!changed) return;

            if (_dirA.Length == 0 && _dirB.Length == 0)
            {
                SetStatus("忽略规则已保存：" + _rules.Summary() + "（选择文件夹后生效）");
                return;
            }
            SetStatus("忽略规则已更新：" + _rules.Summary() + "，正在按新规则重新扫描…");
            StartRescan();
        }

        /// <summary>规则变更后按 A→B 顺序重扫，扫完自动重新对比。</summary>
        private void StartRescan()
        {
            _cancel.Reset();
            if (_dirA.Length > 0)
            {
                _rescanStage = 1;
                StartScan(_dirA, true, false);
            }
            else
            {
                _rescanStage = 2;
                if (_dirB.Length > 0) StartScan(_dirB, false, false);
                else _rescanStage = 0;
            }
        }

        /// <summary>「窗口置顶」开关：勾上后窗口始终显示在最前面（Ctrl+T 同效）。</summary>
        private void OnToggleTopMost(object sender, EventArgs e)
        {
            TopMost = _chkTop.Checked;
        }

        private Control BuildProgressArea()
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Fill;
            // 关键：TableLayoutPanel 会把子控件默认的 Margin(3,3,3,3) 从行高里扣掉。
            // 本面板的内容是"进度条 8..24 + 状态文字 26..48"（设计像素，共 48px），
            // 若留着默认外边距，面板实得高度只有行高-6px，状态文字的下半截会被父容器裁掉
            // —— 125% DPI 下实测文字底部少了 4 行像素（"文字显示不全"的真正原因）。
            p.Margin = new Padding(0);

            _progress = new ThemeProgress();
            _progress.Location = new Point(2, 8);
            _progress.Size = new Size(560, 16);
            _progress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            p.Controls.Add(_progress);

            _lblStatus = new Label();
            _lblStatus.Text = "请选择两个文件夹后开始对比（Ctrl+Enter 开始，也可直接把文件夹拖进来）";
            _lblStatus.ForeColor = Theme.Mix(Color.FromArgb(110, 120, 132));
            _lblStatus.AutoEllipsis = true;
            _lblStatus.Location = new Point(2, 26);
            _lblStatus.Size = new Size(560, 22);
            _lblStatus.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            p.Controls.Add(_lblStatus);

            return p;
        }

        private Control BuildSummaryArea()
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.ColumnCount = 4;
            t.RowCount = 1;
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 4; i++) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            Color[] back = new Color[] {
                Color.FromArgb(255, 243, 243), Color.FromArgb(236, 253, 245),
                Color.FromArgb(254, 242, 242), Color.FromArgb(243, 247, 252)
            };
            Color[] fore = new Color[] {
                Color.FromArgb(190, 60, 60), Color.FromArgb(16, 150, 100),
                Color.FromArgb(200, 80, 60), Color.FromArgb(90, 110, 140)
            };
            string[] names = new string[] { "内容不同", "仅在 B 中存在", "仅在 A 中存在", "完全相同" };

            for (int i = 0; i < 4; i++)
            {
                Panel box = new Panel();
                box.Dock = DockStyle.Fill;
                box.BackColor = back[i];
                box.Margin = new Padding(3, 2, 3, 2);

                Label num = new Label();
                num.Text = "0";
                num.Font = TextUtil.PickUiFont(14f, true);
                num.ForeColor = fore[i];
                num.TextAlign = ContentAlignment.MiddleCenter;
                num.Dock = DockStyle.Top;
                num.Height = (int)(26 * _uiScale);
                box.Controls.Add(num);

                Label cap = new Label();
                cap.Text = names[i];
                cap.ForeColor = Theme.Mix(Color.FromArgb(100, 110, 122));
                cap.TextAlign = ContentAlignment.MiddleCenter;
                cap.Dock = DockStyle.Fill;
                box.Controls.Add(cap);
                cap.BringToFront();

                _countLabels[i] = num;
                t.Controls.Add(box, i, 0);
            }
            return t;
        }

        private ListView BuildListView(int kind)
        {
            ListView lv = new ListView();
            lv.Dock = DockStyle.Fill;
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.GridLines = false;
            lv.HideSelection = false;
            lv.MultiSelect = true;
            lv.Columns.Add("文件（相对路径）", 380, HorizontalAlignment.Left);
            if (kind == TabModified)
            {
                lv.Columns.Add("大小", 130, HorizontalAlignment.Left);
                lv.Columns.Add("说明", 300, HorizontalAlignment.Left);
            }
            else
            {
                lv.Columns.Add("大小", 110, HorizontalAlignment.Left);
            }
            lv.Tag = kind;
            lv.DoubleClick += OnListDoubleClick;
            lv.MouseUp += OnListMouseUp;
            lv.Resize += OnListResize;
            lv.KeyDown += OnListKeyDown;
            return lv;
        }

        private void OnListResize(object sender, EventArgs e)
        {
            ListView lv = (ListView)sender;
            if (lv.Columns.Count == 0) return;
            int others = 0;
            for (int i = 1; i < lv.Columns.Count; i++) others += lv.Columns[i].Width;
            int first = lv.ClientSize.Width - others - 4;
            if (first < 160) first = 160;
            lv.Columns[0].Width = first;
        }

        // ── 文件夹选择 ───────────────────────────────────────
        // ── 手动输入 / 粘贴路径 ───────────────────────────────
        private void OnPathGoA(object sender, EventArgs e) { ApplyTypedPath(true); }
        private void OnPathGoB(object sender, EventArgs e) { ApplyTypedPath(false); }

        private void OnPathBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;   // 防"叮"声，也避免冒泡到主窗口的快捷键
            ApplyTypedPath(sender == _txtPathA);
        }

        /// <summary>把文件夹直接拖到路径框上：等于填入该路径并点「确定」。</summary>
        private void OnPathBoxDragEnter(object sender, DragEventArgs e)
        {
            if (_busy) return;
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void OnPathBoxDragDrop(object sender, DragEventArgs e)
        {
            if (_busy) return;
            string[] items = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (items == null || items.Length == 0) return;
            TextBox box = sender as TextBox;
            if (box == null) return;
            box.Text = items[0];
            ApplyTypedPath(box == _txtPathA);
        }

        private void ApplyTypedPath(bool isA)
        {
            if (_busy) return;
            TextBox txt = isA ? _txtPathA : _txtPathB;
            string raw = txt.Text;
            string err;
            string dir = NormalizeFolderPath(raw, out err);
            if (dir == null)
            {
                MessageBox.Show(this, err, "文件比较器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txt.Focus();
                txt.SelectAll();
                return;
            }
            txt.Clear();
            StartScan(dir, isA);
        }

        /// <summary>
        /// 把输入/粘贴的文本规整成可用的文件夹路径。
        /// 支持：资源管理器"复制路径"带的首尾双引号、%环境变量%、以及 .lnk 快捷方式（自动解析到目标）。
        /// </summary>
        private static string NormalizeFolderPath(string input, out string error)
        {
            error = null;
            string s = input == null ? string.Empty : input.Trim();
            if (s.Length == 0)
            {
                error = "请先输入或粘贴一个文件夹路径。";
                return null;
            }

            // 资源管理器「复制路径」给出的字符串带首尾双引号
            // 去掉首尾引号（资源管理器「复制路径」和命令行参数都可能带，甚至带多层）
            while (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                s = s.Substring(1, s.Length - 2).Trim();

            try { s = Environment.ExpandEnvironmentVariables(s); }
            catch (Exception) { }

            // .lnk 快捷方式：解析它指向的目标（"浏览文件夹"对话框选不中快捷方式，这里是替代路径）
            if (s.Length > 4 && s.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string target = ResolveShortcutTarget(s);
                if (string.IsNullOrEmpty(target))
                {
                    error = "无法解析这个快捷方式：\r\n" + s +
                            "\r\n\r\n可以改成粘贴快捷方式指向的真实文件夹路径。";
                    return null;
                }
                s = target;
            }

            s = s.TrimEnd('\\', '/');
            if (s.Length == 0)
            {
                error = "路径无效。";
                return null;
            }
            if (File.Exists(s))
            {
                error = "这是一个文件，不是文件夹：\r\n" + s;
                return null;
            }
            if (!Directory.Exists(s))
            {
                error = "找不到这个文件夹：\r\n" + s;
                return null;
            }
            return s;
        }

        // ── 解析 .lnk：直接用 shell32 的 IShellLink 接口 ──────────────────
        // 以前走的是 Type.GetTypeFromProgID("WScript.Shell") + Activator.CreateInstance
        // + InvokeMember 反射调用 —— 那正是脚本木马最爱的组合，杀软的启发式（如 360 的 QVM）
        // 会因此把小工具判成可疑程序。这里改成直接声明 COM 接口调用 shell32，
        // 二进制里不再出现 "WScript.Shell" 这类脚本宿主字符串。
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkCoClass { }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            // 只需 GetPath：vtable 顺序必须从第 0 个方法开始，后面的用不到就不声明
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
                         int cch, IntPtr pfd, int fFlags);
        }

        [ComImport, Guid("0000010b-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        }

        /// <summary>解析 .lnk 指向的目标（IShellLink，Windows 98 起就自带）。</summary>
        private static string ResolveShortcutTarget(string lnkPath)
        {
            object link = null;
            try
            {
                link = new ShellLinkCoClass();
                ((IPersistFile)link).Load(lnkPath, 0);          // 0 = STGM_READ
                StringBuilder sb = new StringBuilder(1040);     // MAX_PATH 有余量
                ((IShellLinkW)link).GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                string s = sb.ToString();
                return s.Length > 0 ? s : null;
            }
            catch (Exception) { return null; }
            finally
            {
                if (link != null) { try { Marshal.ReleaseComObject(link); } catch (Exception) { } }
            }
        }

        // ── 文件夹选择 ───────────────────────────────────────
        private void OnPickA(object sender, EventArgs e) { PickFolder(true); }
        private void OnPickB(object sender, EventArgs e) { PickFolder(false); }

        private void PickFolder(bool isA)
        {
            if (_busy) return;
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = isA ? "选择文件夹 A（源文件夹）" : "选择文件夹 B（目标文件夹）";
                dlg.ShowNewFolderButton = false;
                string current = isA ? _dirA : _dirB;
                if (current.Length > 0) dlg.SelectedPath = current;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                StartScan(dlg.SelectedPath, isA);
            }
        }

        private void StartScan(string dir, bool isA)
        {
            StartScan(dir, isA, false);
        }

        private void StartScan(string dir, bool isA, bool autoCompare)
        {
            SetBusy(true, "正在扫描文件夹…");
            _cancel.Reset();

            string target = dir;
            bool targetIsA = isA;
            bool targetAuto = autoCompare;
            IgnoreRules targetRules = _rules;   // 线程里不要再读字段，先抓一份
            Thread th = new Thread(new ThreadStart(delegate
            {
                ScanStats st = null;
                string error = null;
                try
                {
                    st = FolderScanner.Scan(target, targetRules);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                ScanStats fSt = st;
                string fError = error;
                try
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        FinishScan(targetIsA, target, fSt, fError, targetAuto);
                    }));
                }
                catch (Exception) { }
            }));
            th.IsBackground = true;
            th.Start();
        }

        private void FinishScan(bool isA, string dir, ScanStats st, string error, bool autoCompare)
        {
            SetBusy(false, null);
            if (error != null)
            {
                MessageBox.Show(this, "无法读取该文件夹：\r\n" + error, "文件比较器",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (st == null || st.Map == null) return;

            Dictionary<string, FileEntry> map = st.Map;
            long total = st.TotalSize;
            int skipped = st.SkippedDirs;

            if (isA)
            {
                _mapA = map; _dirA = dir;
                _lblPathA.Text = dir;
                _lblPathA.ForeColor = Theme.Mix(Color.FromArgb(38, 50, 66));
                _btnClearA.Enabled = true;
            }
            else
            {
                _mapB = map; _dirB = dir;
                _lblPathB.Text = dir;
                _lblPathB.ForeColor = Theme.Mix(Color.FromArgb(38, 50, 66));
                _btnClearB.Enabled = true;
            }

            string info = map.Count + " 个文件 · " + TextUtil.FormatSize(total);
            if (skipped > 0) info += "（" + skipped + " 个目录无权限已跳过）";
            if (st.IgnoredFiles > 0 || st.IgnoredDirs > 0)
            {
                info += "（已忽略 ";
                if (st.IgnoredFiles > 0) info += st.IgnoredFiles + " 个文件";
                if (st.IgnoredFiles > 0 && st.IgnoredDirs > 0) info += "、";
                if (st.IgnoredDirs > 0) info += st.IgnoredDirs + " 个目录";
                info += "）";
            }
            if (isA) _lblInfoA.Text = info; else _lblInfoB.Text = info;

            ClearResultsInternal();
            SetStatus("已载入 " + (isA ? "文件夹 A" : "文件夹 B") + "：" + info);
            UpdateButtons();

            // 忽略规则变更后的串行重扫：A 完了扫 B，B 完了自动重新对比
            if (_rescanStage == 1)
            {
                _rescanStage = 2;
                if (_dirB.Length > 0) { StartScan(_dirB, false, false); return; }
            }
            if (_rescanStage == 2)
            {
                _rescanStage = 0;
                if (_mapA != null && _mapB != null) { OnCompare(null, EventArgs.Empty); return; }
            }

            if (autoCompare && _mapA != null && _mapB != null)
                OnCompare(null, EventArgs.Empty);
        }

        private void OnClearA(object sender, EventArgs e)
        {
            _mapA = null; _dirA = string.Empty;
            _lblPathA.Text = "尚未选择文件夹";
            _lblPathA.ForeColor = Theme.Mix(Color.Gray);
            _lblInfoA.Text = "";
            _btnClearA.Enabled = false;
            ClearResultsInternal();
            UpdateButtons();
        }

        private void OnClearB(object sender, EventArgs e)
        {
            _mapB = null; _dirB = string.Empty;
            _lblPathB.Text = "尚未选择文件夹";
            _lblPathB.ForeColor = Theme.Mix(Color.Gray);
            _lblInfoB.Text = "";
            _btnClearB.Enabled = false;
            ClearResultsInternal();
            UpdateButtons();
        }

        // ── 对比 ─────────────────────────────────────────────
        private void OnCompare(object sender, EventArgs e)
        {
            if (_busy || _mapA == null || _mapB == null) return;

            _cancel.Reset();
            SetBusy(true, "正在分析文件列表…");
            _progress.Value = 0;

            Dictionary<string, FileEntry> a = _mapA;
            Dictionary<string, FileEntry> b = _mapB;
            string nameA = FolderNameOf(_dirA);
            string nameB = FolderNameOf(_dirB);

            Thread th = new Thread(new ThreadStart(delegate
            {
                CompareResult res = null;
                string error = null;
                try
                {
                    res = FileComparer.CompareFolders(a, b, nameA, nameB, _cancel,
                        new ProgressReport(OnCompareProgress));
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                CompareResult fRes = res;
                string fErr = error;
                bool cancelled = _cancel.Cancelled;
                try
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        FinishCompare(fRes, fErr, cancelled);
                    }));
                }
                catch (Exception) { }
            }));
            th.IsBackground = true;
            th.Start();
        }

        private DateTime _lastProgress = DateTime.MinValue;

        private void OnCompareProgress(int done, int total, string current)
        {
            DateTime now = DateTime.Now;
            bool force = (done == total) || (done <= 2);
            if (!force && (now - _lastProgress).TotalMilliseconds < 60) return;
            _lastProgress = now;

            int pct = total > 0 ? (int)((long)done * 100 / total) : 100;
            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    if (IsDisposed) return;
                    _progress.Value = pct < 0 ? 0 : (pct > 100 ? 100 : pct);
                    _lblStatus.Text = "正在对比：" + done + " / " + total + "（" + pct + "%）  " + Shorten(current, 70);
                }));
            }
            catch (Exception) { }
        }

        private static string Shorten(string s, int max)
        {
            if (s == null) return string.Empty;
            return s.Length <= max ? s : "…" + s.Substring(s.Length - max);
        }

        private void FinishCompare(CompareResult res, string error, bool cancelled)
        {
            SetBusy(false, null);
            if (error != null)
            {
                MessageBox.Show(this, "对比过程出错：\r\n" + error, "文件比较器",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (cancelled)
            {
                SetStatus("已取消对比。");
                return;
            }

            _result = res;
            RenderResults(res);

            _progress.Value = 100;
            string msg = "对比完成：" + res.Modified.Count + " 个内容不同，"
                + res.OnlyInB.Count + " 个新增，"
                + res.OnlyInA.Count + " 个缺失，"
                + res.Same.Count + " 个相同。";
            if (res.Errors.Count > 0) msg += "（" + res.Errors.Count + " 个文件读取失败）";
            SetStatus(msg);
            if (!res.HasDiff) SetStatus("🎉 两个文件夹完全一致 —— " + msg);
            UpdateButtons();

            // 历史记录改成「比对完成就记一条」（2026-09-15 用户要求：不再等导出差异文件）
            if (!_silentMode) SaveHistoryEntry();

            if (_autoExportPath != null && res.HasDiff)
                StartExport(_autoExportPath);
            else if (_autoExportPath != null && _silentMode)
                Close();
        }

        private void RenderResults(CompareResult res)
        {
            _countLabels[TabModified].Text = res.Modified.Count.ToString();
            _countLabels[TabOnlyB].Text = res.OnlyInB.Count.ToString();
            _countLabels[TabOnlyA].Text = res.OnlyInA.Count.ToString();
            _countLabels[TabSame].Text = res.Same.Count.ToString();

            FillList(_lists[TabModified], res.Modified, TabModified);
            FillList(_lists[TabOnlyB], res.OnlyInB, TabOnlyB);
            FillList(_lists[TabOnlyA], res.OnlyInA, TabOnlyA);
            FillList(_lists[TabSame], res.Same, TabSame);

            if (res.Modified.Count > 0) _tabs.SelectedIndex = TabModified;
            else if (res.OnlyInB.Count > 0) _tabs.SelectedIndex = TabOnlyB;
            else if (res.OnlyInA.Count > 0) _tabs.SelectedIndex = TabOnlyA;
            else _tabs.SelectedIndex = TabSame;
        }

        private void FillList(ListView lv, List<CompareItem> items, int kind)
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                CompareItem it = items[i];
                ListViewItem row = new ListViewItem(it.RelPath);
                row.Tag = it;

                if (kind == TabModified)
                {
                    string sizeText;
                    string note;
                    if (it.SizeDiff)
                    {
                        sizeText = TextUtil.FormatSize(it.SizeA) + " → " + TextUtil.FormatSize(it.SizeB);
                        note = "大小不同";
                    }
                    else
                    {
                        sizeText = TextUtil.FormatSize(it.SizeA);
                        note = "大小相同 · 内容不同";
                    }
                    row.SubItems.Add(sizeText);
                    row.SubItems.Add(note);
                }
                else
                {
                    row.SubItems.Add(TextUtil.FormatSize(it.DisplaySize));
                }

                lv.Items.Add(row);
            }
            lv.EndUpdate();
        }

        // ── 结果操作 ─────────────────────────────────────────
        private void OnClearResults(object sender, EventArgs e)
        {
            if (_busy) return;
            ClearResultsInternal();
            UpdateButtons();
            SetStatus("结果已清除。");
        }

        private void ClearResultsInternal()
        {
            _result = null;
            for (int i = 0; i < 4; i++)
            {
                _lists[i].Items.Clear();
                _countLabels[i].Text = "0";
            }
            _progress.Value = 0;
        }

        private void UpdateButtons()
        {
            bool ready = (_mapA != null && _mapB != null);
            _btnCompare.Enabled = ready && !_busy;
            // 历史还原出来的结果：清单照快照，但重新打包要读当前文件 ——
            // 任一文件夹被删/被移走时打包不可用（清单仍可看）
            bool exportOk = _result != null && _result.HasDiff;
            if (exportOk && _restoredEntry != null &&
                (!Directory.Exists(_restoredEntry.DirA) || !Directory.Exists(_restoredEntry.DirB)))
                exportOk = false;
            _btnExport.Enabled = exportOk && !_busy;
            _btnClear.Enabled = (_result != null) && !_busy;
            _btnPickA.Enabled = !_busy;
            _btnPickB.Enabled = !_busy;
            _btnClearA.Enabled = (_mapA != null) && !_busy;
            _btnClearB.Enabled = (_mapB != null) && !_busy;
            _btnCancel.Visible = _busy;
            if (_btnHistory != null) _btnHistory.Enabled = !_busy;
        }

        private void SetBusy(bool busy, string status)
        {
            _busy = busy;
            if (status != null) SetStatus(status);
            Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            UpdateButtons();
        }

        private void SetStatus(string text)
        {
            _lblStatus.Text = text;
        }

        private void OnCancel(object sender, EventArgs e)
        {
            _cancel.Cancelled = true;
            SetStatus("正在取消…");
        }

        private static string FolderNameOf(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return string.Empty;
            string s = dir.TrimEnd('\\', '/');
            int i = s.LastIndexOfAny(new char[] { '\\', '/' });
            if (i >= 0 && i < s.Length - 1) return s.Substring(i + 1);
            return s;
        }

        // ── 列表交互 ─────────────────────────────────────────
        private CompareItem ItemAt(ListView lv, int index)
        {
            if (index < 0 || index >= lv.Items.Count) return null;
            return lv.Items[index].Tag as CompareItem;
        }

        private void OnListDoubleClick(object sender, EventArgs e)
        {
            ListView lv = (ListView)sender;
            if (lv.SelectedIndices.Count == 0) return;
            CompareItem it = ItemAt(lv, lv.SelectedIndices[0]);
            if (it == null) return;

            if (it.A != null && it.B != null && TextUtil.IsTextFile(it.RelPath))
                OpenDiff(it);
            else if (it.A != null || it.B != null)
                RevealInExplorer((it.A != null ? it.A : it.B).FullPath);
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                OnListDoubleClick(sender, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void OnListMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            ListView lv = (ListView)sender;
            ListViewItem hit = lv.GetItemAt(e.X, e.Y);
            if (hit == null) return;
            if (!hit.Selected)
            {
                lv.SelectedItems.Clear();
                hit.Selected = true;
            }
            CompareItem it = hit.Tag as CompareItem;
            bool isText = it != null && it.A != null && it.B != null && TextUtil.IsTextFile(it.RelPath);
            _menu.Items[0].Enabled = isText;
            _menu.Items[2].Enabled = it != null && it.A != null;
            _menu.Items[3].Enabled = it != null && it.B != null;
            _menu.Show(lv, e.Location);
        }

        private CompareItem FirstSelected()
        {
            for (int i = 0; i < 4; i++)
            {
                if (_lists[i].SelectedItems.Count > 0)
                    return _lists[i].SelectedItems[0].Tag as CompareItem;
            }
            return null;
        }

        private void OnMenuDiff(object sender, EventArgs e)
        {
            CompareItem it = FirstSelected();
            if (it != null && it.A != null && it.B != null) OpenDiff(it);
        }

        private void OnMenuReveal(object sender, EventArgs e)
        {
            CompareItem it = FirstSelected();
            if (it == null) return;
            FileEntry target = it.B != null ? it.B : it.A;
            if (target != null) RevealInExplorer(target.FullPath);
        }

        private void OnMenuOpenA(object sender, EventArgs e)
        {
            CompareItem it = FirstSelected();
            if (it != null && it.A != null) OpenWithShell(it.A.FullPath);
        }

        private void OnMenuOpenB(object sender, EventArgs e)
        {
            CompareItem it = FirstSelected();
            if (it != null && it.B != null) OpenWithShell(it.B.FullPath);
        }

        private void OnMenuCopyPath(object sender, EventArgs e)
        {
            CompareItem it = FirstSelected();
            if (it == null) return;
            try { Clipboard.SetText(it.RelPath); SetStatus("已复制相对路径：" + it.RelPath); }
            catch (Exception) { }
        }

        private static void RevealInExplorer(string path)
        {
            try
            {
                if (File.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else
                    System.Diagnostics.Process.Start("explorer.exe", "/e,\"" + Path.GetDirectoryName(path) + "\"");
            }
            catch (Exception) { }
        }

        private static void OpenWithShell(string path)
        {
            try { System.Diagnostics.Process.Start(path); }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开文件：\r\n" + ex.Message, "文件比较器",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenDiff(CompareItem item)
        {
            long size = item.SizeA > item.SizeB ? item.SizeA : item.SizeB;
            if (size > DiffOpenWarnBytes)
            {
                DialogResult r = MessageBox.Show(this,
                    "该文件约 " + TextUtil.FormatSize(size) + "，逐行差异视图可能较慢。要继续吗？",
                    "文件比较器", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
            }

            string pathA = item.A.FullPath;
            string pathB = item.B.FullPath;
            string rel = item.RelPath;
            Cursor = Cursors.WaitCursor;
            try
            {
                string textA = TextUtil.ReadAllTextAuto(pathA);
                string textB = TextUtil.ReadAllTextAuto(pathB);
                Cursor = Cursors.Default;
                using (DiffForm f = new DiffForm(rel, textA, textB))
                {
                    f.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                MessageBox.Show(this, "无法读取文件内容进行文本对比：\r\n" + ex.Message,
                    "文件比较器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ── 导出 ZIP ─────────────────────────────────────────
        private void OnExport(object sender, EventArgs e)
        {
            if (_busy || _result == null) return;
            CompareResult res = _result;
            if (res.ExportFileCount == 0)
            {
                MessageBox.Show(this, "没有可导出的差异文件。", "文件比较器",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string defaultName = "diff_export_" + (res.NameA.Length > 0 ? res.NameA : "A")
                + "_vs_" + (res.NameB.Length > 0 ? res.NameB : "B")
                + "_" + TextUtil.TimeStamp() + ".zip";

            string target;
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "导出差异文件";
                dlg.Filter = "ZIP 压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*";
                dlg.FileName = SanitizeFileName(defaultName);
                dlg.OverwritePrompt = true;
                dlg.RestoreDirectory = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                target = dlg.FileName;
            }

            StartExport(target);
        }

        private void StartExport(string target)
        {
            CompareResult res = _result;
            if (res == null || res.ExportFileCount == 0) return;

            _cancel.Reset();
            SetBusy(true, "正在准备导出差异文件…");
            _progress.Value = 0;

            Thread th = new Thread(new ThreadStart(delegate
            {
                string error = null;
                int packed = 0;
                int skipped = 0;
                int total = res.ExportFileCount;
                long bytes = 0;
                try
                {
                    using (FileStream fs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024))
                    {
                        using (ZipWriter zip = new ZipWriter(fs))
                        {
                            for (int i = 0; i < res.OnlyInA.Count; i++)
                            {
                                if (_cancel.Cancelled) break;
                                CompareItem it = res.OnlyInA[i];
                                AddOne(zip, "only_in_A/" + TextUtil.ToZipPath(it.RelPath), it.A, ref bytes, ref skipped);
                                ReportExport(++packed, total, bytes);
                            }
                            for (int i = 0; i < res.OnlyInB.Count; i++)
                            {
                                if (_cancel.Cancelled) break;
                                CompareItem it = res.OnlyInB[i];
                                AddOne(zip, "only_in_B/" + TextUtil.ToZipPath(it.RelPath), it.B, ref bytes, ref skipped);
                                ReportExport(++packed, total, bytes);
                            }
                            for (int i = 0; i < res.Modified.Count; i++)
                            {
                                if (_cancel.Cancelled) break;
                                CompareItem it = res.Modified[i];
                                if (it.A != null)
                                {
                                    AddOne(zip, "modified/version_A/" + TextUtil.ToZipPath(it.RelPath), it.A, ref bytes, ref skipped);
                                }
                                ReportExport(++packed, total, bytes);
                                if (it.B != null)
                                {
                                    AddOne(zip, "modified/version_B/" + TextUtil.ToZipPath(it.RelPath), it.B, ref bytes, ref skipped);
                                }
                                ReportExport(++packed, total, bytes);
                            }

                            zip.AddText("对比说明.txt", BuildReport(res, skipped, _restoredEntry));
                            zip.Finish();
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                string fErr = error;
                bool cancelled = _cancel.Cancelled;
                string fTarget = target;
                int fPacked = packed;
                int fSkipped = skipped;
                try
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        FinishExport(fErr, cancelled, fTarget, fPacked, fSkipped);
                    }));
                }
                catch (Exception) { }
            }));
            th.IsBackground = true;
            th.Start();
        }

        /// <summary>打包一个文件；文件不在原处/读不了就跳过并计数（历史重打包时文件可能已被删）。</summary>
        private static bool AddOne(ZipWriter zip, string entryName, FileEntry fe, ref long bytes, ref int skipped)
        {
            try
            {
                if (fe == null || fe.FullPath == null || !File.Exists(fe.FullPath)) { skipped++; return false; }
                zip.AddFile(entryName, fe.FullPath, fe.Modified);
                bytes += fe.Size;
                return true;
            }
            catch (Exception)
            {
                skipped++;
                return false;
            }
        }

        private DateTime _lastExportReport = DateTime.MinValue;

        private void ReportExport(int done, int total, long bytes)
        {
            DateTime now = DateTime.Now;
            if (done < total && (now - _lastExportReport).TotalMilliseconds < 60) return;
            _lastExportReport = now;
            int pct = total > 0 ? (int)((long)done * 100 / total) : 100;
            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    if (IsDisposed) return;
                    _progress.Value = pct < 0 ? 0 : (pct > 100 ? 100 : pct);
                    _lblStatus.Text = "正在打包：" + done + " / " + total + "（" + pct + "%）· 已处理 "
                        + TextUtil.FormatSize(bytes);
                }));
            }
            catch (Exception) { }
        }

        private void FinishExport(string error, bool cancelled, string target, int packed, int skipped)
        {
            SetBusy(false, null);
            if (cancelled)
            {
                try { if (File.Exists(target)) File.Delete(target); } catch (Exception) { }
                SetStatus("已取消导出，未保留文件。");
                return;
            }
            if (error != null)
            {
                SetStatus("导出失败：" + error);
                MessageBox.Show(this, "导出失败：\r\n" + error, "文件比较器",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            long size = 0;
            try { size = new FileInfo(target).Length; }
            catch (Exception) { }
            _progress.Value = 100;

            string skipNote = skipped > 0 ? "（" + skipped + " 个文件已不在原处，未打包）" : "";
            SetStatus("导出完成：" + target + "（" + TextUtil.FormatSize(size) + "）" + skipNote);

            // 历史条目在「比对完成」时就生成了；这里只把导出文件名补记到那一条上
            if (!_silentMode) RememberExportPath(target);

            if (_silentMode)
            {
                Close();
                return;
            }

            DialogResult r = MessageBox.Show(this,
                "导出完成！\r\n\r\n文件：" + target + "\r\n大小：" + TextUtil.FormatSize(size)
                + "\r\n共打包 " + packed + " 个文件。" + (skipped > 0 ? "\r\n" + skipped + " 个文件已不在原处，已跳过。" : "")
                + "\r\n\r\n是否打开所在文件夹？",
                "文件比较器", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r == DialogResult.Yes)
            {
                try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + target + "\""); }
                catch (Exception) { }
            }
        }

        /// <summary>把当前这次对比结果存进历史（快照：三类完整清单 + 完全相同仅计数）。</summary>
        private void SaveHistoryEntry()
        {
            try
            {
                CompareResult res = _result;
                if (res == null) return;

                HistoryEntry h = new HistoryEntry();
                h.Time = DateTime.Now;
                h.DirA = _dirA;
                h.DirB = _dirB;
                h.ZipPath = string.Empty;      // 此刻可能还没导出，导出成功后再补记
                h.IgnoreSummary = _rules == null ? string.Empty : _rules.Summary();
                h.CountModified = res.Modified.Count;
                h.CountOnlyB = res.OnlyInB.Count;
                h.CountOnlyA = res.OnlyInA.Count;
                h.CountSame = res.Same.Count;

                int i;
                for (i = 0; i < res.Modified.Count; i++)
                {
                    CompareItem it = res.Modified[i];
                    HistoryItem hi = new HistoryItem();
                    hi.Kind = 'M'; hi.Rel = it.RelPath; hi.SizeA = it.SizeA; hi.SizeB = it.SizeB;
                    h.Items.Add(hi);
                }
                for (i = 0; i < res.OnlyInA.Count; i++)
                {
                    HistoryItem hi = new HistoryItem();
                    hi.Kind = 'A'; hi.Rel = res.OnlyInA[i].RelPath; hi.SizeA = res.OnlyInA[i].SizeA;
                    h.Items.Add(hi);
                }
                for (i = 0; i < res.OnlyInB.Count; i++)
                {
                    HistoryItem hi = new HistoryItem();
                    hi.Kind = 'B'; hi.Rel = res.OnlyInB[i].RelPath; hi.SizeB = res.OnlyInB[i].SizeB;
                    h.Items.Add(hi);
                }

                HistoryStore.Add(h);
                _lastEntry = h;
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 导出成功后，把 ZIP 路径补记到「当前界面这条」历史快照上（比对时还不知导出到哪）。
        /// 只改这一个字段，其它内容一字不动；按 目录A+目录B+时间 找到磁盘上那条再写回。
        /// </summary>
        private void RememberExportPath(string zipPath)
        {
            try
            {
                HistoryEntry e = _restoredEntry != null ? _restoredEntry : _lastEntry;
                if (e == null || zipPath == null || zipPath.Length == 0) return;
                if (e.ZipPath == zipPath) return;

                e.ZipPath = zipPath;
                // 注意：磁盘上的时间只存到秒，直接比 DateTime 会因为毫秒不等而匹配不上（踩过），
                // 所以按 "yyyy-MM-dd HH:mm:ss" 字符串比。
                string key = e.Time.ToString("yyyy-MM-dd HH:mm:ss");
                List<HistoryEntry> all = HistoryStore.Load();
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].DirA == e.DirA && all[i].DirB == e.DirB
                        && all[i].Time.ToString("yyyy-MM-dd HH:mm:ss") == key)
                    {
                        all[i].ZipPath = zipPath;
                        HistoryStore.Save(all);
                        break;
                    }
                }
            }
            catch (Exception) { }
        }

        private static string BuildReport(CompareResult res, int skipped, HistoryEntry restored)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("文件夹对比报告");
            sb.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            if (restored != null)
            {
                sb.AppendLine("※ 本包由历史记录（" + restored.TimeFull + " 的那次对比）重新打包，");
                sb.AppendLine("  清单是当时的快照，文件内容取自当前磁盘。");
            }
            if (skipped > 0)
                sb.AppendLine("※ 有 " + skipped + " 个文件已不在原处，未打进包里。");
            sb.AppendLine("文件夹 A：" + res.NameA);
            sb.AppendLine("文件夹 B：" + res.NameB);
            sb.AppendLine();
            sb.AppendLine("内容不同：" + res.Modified.Count);
            sb.AppendLine("仅在 A 中：" + res.OnlyInA.Count);
            sb.AppendLine("仅在 B 中：" + res.OnlyInB.Count);
            sb.AppendLine("完全相同：" + res.Same.Count);
            sb.AppendLine();
            sb.AppendLine("ZIP 目录结构：");
            sb.AppendLine("  only_in_A/            仅在 A 中存在的文件");
            sb.AppendLine("  only_in_B/            仅在 B 中存在的文件");
            sb.AppendLine("  modified/version_A/   内容不同文件的 A 版本");
            sb.AppendLine("  modified/version_B/   内容不同文件的 B 版本");
            sb.AppendLine();

            if (res.Modified.Count > 0)
            {
                sb.AppendLine("── 内容不同的文件 ──");
                for (int i = 0; i < res.Modified.Count; i++)
                {
                    CompareItem it = res.Modified[i];
                    sb.AppendLine("  " + it.RelPath + "  ("
                        + TextUtil.FormatSize(it.SizeA) + " → " + TextUtil.FormatSize(it.SizeB) + ")");
                }
                sb.AppendLine();
            }
            if (res.OnlyInA.Count > 0)
            {
                sb.AppendLine("── 仅在 A 中 ──");
                for (int i = 0; i < res.OnlyInA.Count; i++)
                    sb.AppendLine("  " + res.OnlyInA[i].RelPath);
                sb.AppendLine();
            }
            if (res.OnlyInB.Count > 0)
            {
                sb.AppendLine("── 仅在 B 中 ──");
                for (int i = 0; i < res.OnlyInB.Count; i++)
                    sb.AppendLine("  " + res.OnlyInB[i].RelPath);
                sb.AppendLine();
            }
            if (res.Errors.Count > 0)
            {
                sb.AppendLine("── 读取失败 ──");
                for (int i = 0; i < res.Errors.Count; i++) sb.AppendLine("  " + res.Errors[i]);
            }
            return sb.ToString();
        }

        private static string SanitizeFileName(string name)
        {
            char[] bad = Path.GetInvalidFileNameChars();
            for (int i = 0; i < bad.Length; i++) name = name.Replace(bad[i], '_');
            return name;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy)
            {
                DialogResult r = MessageBox.Show(this, "当前任务尚未完成，确定要退出吗？",
                    "文件比较器", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) { e.Cancel = true; return; }
                _cancel.Cancelled = true;
            }
            base.OnFormClosing(e);
        }
    }
}
