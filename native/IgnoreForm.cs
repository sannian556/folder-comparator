// 「忽略规则」设置对话框：三个预设 + 自定义规则文本框。
// 注意：与主窗口一致，这里同样用 AutoScaleMode.None + 手动按 DPI 缩放。
// （自动缩放对本程序无效：实测 AutoScaleDimensions 会被重置，控件位置不放大而字体变大，
//   结果就是 125% DPI 下文字被挤掉。见 AGENTS.md 里那段排查记录。）
using System;
using System.Drawing;
using System.Windows.Forms;

namespace FileDiffTool
{
    internal class IgnoreForm : Form
    {
        private CheckBox _vcs;
        private CheckBox _temp;
        private CheckBox _logs;
        private TextBox _custom;
        private Label _hint;
        private Label _count;

        private float _uiScale = 1f;

        /// <summary>点「保存」后的规则；取消时为 null。</summary>
        public IgnoreRules Result;

        public IgnoreForm(IgnoreRules current)
        {
            if (current == null) current = new IgnoreRules();

            _uiScale = DetectUiScale();
            Font = TextUtil.PickUiFont(9f, false);
            AutoScaleMode = AutoScaleMode.None;   // 缩放全部自己做

            Text = "忽略规则";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(S(536), S(452));

            Label intro = new Label();
            intro.Text = "对比时跳过这些文件（文件夹 A、B 两侧同时生效）：";
            intro.AutoSize = true;
            intro.Location = new Point(S(16), S(14));
            intro.ForeColor = Color.FromArgb(60, 70, 84);
            Controls.Add(intro);

            _vcs = MakeCheck("版本控制目录　.git/　.svn/　.hg/　.bzr/　CVS/", 16, 42, current.SkipVcs);
            _temp = MakeCheck("系统与临时文件　Thumbs.db　desktop.ini　.DS_Store　~$*　*.tmp　*.swp", 16, 68, current.SkipTemp);
            _logs = MakeCheck("日志与备份　*.log　*.bak　*.old　*.orig", 16, 94, current.SkipLogs);

            Label cl = new Label();
            cl.Text = "自定义规则（每行一条）：";
            cl.AutoSize = true;
            cl.Location = new Point(S(16), S(128));
            cl.ForeColor = Color.FromArgb(60, 70, 84);
            Controls.Add(cl);

            _custom = new TextBox();
            _custom.Multiline = true;
            _custom.ScrollBars = ScrollBars.Vertical;
            _custom.AcceptsReturn = true;
            _custom.WordWrap = false;
            _custom.Location = new Point(S(16), S(150));
            _custom.Size = new Size(S(504), S(150));
            _custom.Text = current.Custom == null ? string.Empty : current.Custom;
            _custom.TextChanged += OnAnyChange;
            Controls.Add(_custom);

            _hint = new Label();
            _hint.Text = "语法：* 任意字符，? 单个字符，/ 结尾只匹配目录，**/ 任意层级，行首 ! 表示例外（放回来）。\n" +
                         "例：node_modules/　build/**　*.log　!keep.log　　（以 # 开头的行是注释）";
            _hint.AutoSize = false;
            _hint.Location = new Point(S(16), S(308));
            _hint.Size = new Size(S(504), S(64));
            _hint.ForeColor = Color.FromArgb(120, 130, 142);
            Controls.Add(_hint);

            _count = new Label();
            _count.AutoSize = true;
            _count.Location = new Point(S(16), S(378));
            _count.ForeColor = Color.FromArgb(74, 108, 247);
            Controls.Add(_count);

            MakeButton("清空规则", 16, 406, 92, 30, OnClear);
            Button cancel = MakeButton("取消", 340, 406, 84, 30, null);
            cancel.DialogResult = DialogResult.Cancel;
            Button save = MakeButton("保存", 432, 406, 88, 30, OnSave);

            AcceptButton = save;
            CancelButton = cancel;

            UpdateCount();
        }

        // ── DPI 缩放 ────────────────────────────────────────
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

        private CheckBox MakeCheck(string text, int x, int y, bool on)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.AutoSize = true;
            c.Location = new Point(S(x), S(y));
            c.Checked = on;
            c.CheckedChanged += OnAnyChange;
            Controls.Add(c);
            return c;
        }

        private Button MakeButton(string text, int x, int y, int w, int h, EventHandler onClick)
        {
            Button b = new Button();
            b.Text = text;
            b.Location = new Point(S(x), S(y));
            b.Size = new Size(S(w), S(h));
            if (onClick != null) b.Click += onClick;
            Controls.Add(b);
            return b;
        }

        private IgnoreRules Collect()
        {
            IgnoreRules r = new IgnoreRules();
            r.SkipVcs = _vcs.Checked;
            r.SkipTemp = _temp.Checked;
            r.SkipLogs = _logs.Checked;
            r.Custom = _custom.Text;
            r.Compile();
            return r;
        }

        private void OnAnyChange(object sender, EventArgs e)
        {
            UpdateCount();
        }

        private void UpdateCount()
        {
            IgnoreRules r = Collect();
            if (r.RuleCount == 0) _count.Text = "当前：未启用忽略规则";
            else _count.Text = "当前：" + r.RuleCount + " 条规则生效";
        }

        private void OnClear(object sender, EventArgs e)
        {
            _vcs.Checked = false;
            _temp.Checked = false;
            _logs.Checked = false;
            _custom.Text = string.Empty;
            UpdateCount();
        }

        private void OnSave(object sender, EventArgs e)
        {
            IgnoreRules r = Collect();
            try { r.Save(); }
            catch (Exception) { }
            Result = r;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
