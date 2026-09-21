// 设置窗体：外观三档（跟随系统 / 浅色 / 深色）+ 关于。
// 主界面右下角「设置」按钮打开它；切换外观后立刻应用到主窗口并写盘。
//
// 本文件必须保持 C# 2.0 语法（XP 版用 v2.0.50727 的 csc 编译）。
using System;
using System.Drawing;
using System.Windows.Forms;

namespace FileDiffTool
{
    internal class SettingsForm : Form
    {
        private ThemeRadio _rAuto, _rLight, _rDark;
        private Label _hint;
        private Control _ownerForRefresh;
        private bool _syncing;

        public SettingsForm(Control ownerForRefresh)
        {
            _ownerForRefresh = ownerForRefresh;

            Text = "设置 — 文件比较器";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = TextUtil.PickUiFont(9f, false);
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(420), Theme.S(330));

            BuildUi();
            SyncFromTheme();

            Theme.Apply(this);
            BackColor = Theme.Bg;
        }

        private void BuildUi()
        {
            int x = Theme.S(24), y = Theme.S(20), w = Theme.S(372);

            Label title = new Label();
            title.Text = "设置";
            title.Font = TextUtil.PickUiFont(11f, true);
            title.AutoSize = true;
            title.Location = new Point(x, y);
            Controls.Add(title);

            Label sec1 = new Label();
            sec1.Text = "外观";
            sec1.AutoSize = true;
            sec1.Location = new Point(x, y + Theme.S(38));
            Controls.Add(sec1);

            _rAuto = NewRadio("跟随系统", x, y + Theme.S(64), w);
            _rLight = NewRadio("浅色", x, y + Theme.S(92), w);
            _rDark = NewRadio("深色", x, y + Theme.S(120), w);

            _hint = new Label();
            _hint.AutoSize = false;
            _hint.Size = new Size(w, Theme.S(34));
            _hint.Location = new Point(x, y + Theme.S(148));
            _hint.Text = "当前系统没有「深色模式」这个设置（Windows 10 1809 以上才有），"
                       + "所以「跟随系统」用不了，已按浅色显示；下面两档随时可用。";
            _hint.ForeColor = Theme.Dim;
            _hint.Visible = !Theme.SystemSupportsTheme;
            Controls.Add(_hint);

            Label about = new Label();
            about.Text = "关于";
            about.AutoSize = true;
            about.Location = new Point(x, y + Theme.S(190));
            Controls.Add(about);

            string ver = "?";
            try { ver = Application.ProductVersion; } catch (Exception) { }
            // 程序集版本是四段式 1.0.0.0，界面上只留前两段
            string[] seg = ver.Split('.');
            if (seg.Length > 2) ver = seg[0] + "." + seg[1];

            Label info = new Label();
            info.AutoSize = false;
            info.Size = new Size(w, Theme.S(52));
            info.Location = new Point(x, y + Theme.S(212));
            info.Text = "文件比较器　版本 " + ver
                      + "\r\n外观设置存在：" + Theme.SettingsPath
                      + "\r\n（删掉那个文件就恢复默认：跟随系统）";
            info.ForeColor = Theme.SubText;
            Controls.Add(info);

            Button close = new Button();
            close.Text = "关闭";
            close.Size = new Size(Theme.S(96), Theme.S(30));
            close.Location = new Point(ClientSize.Width - Theme.S(96) - Theme.S(24),
                                       ClientSize.Height - Theme.S(30) - Theme.S(20));
            close.Click += delegate { Close(); };
            Controls.Add(close);
            CancelButton = close;
        }

        private ThemeRadio NewRadio(string text, int x, int y, int w)
        {
            ThemeRadio r = new ThemeRadio();
            r.Text = text;
            r.AutoSize = false;
            r.Size = new Size(w, Theme.S(24));
            r.Location = new Point(x, y);
            r.CheckedChanged += OnRadioChanged;
            Controls.Add(r);
            return r;
        }

        /// <summary>把当前主题状态刷到三个单选项上（不要反过来触发保存）</summary>
        private void SyncFromTheme()
        {
            _syncing = true;
            try
            {
                _rAuto.Checked = (Theme.Current == Theme.Mode.Auto);
                _rLight.Checked = (Theme.Current == Theme.Mode.Light);
                _rDark.Checked = (Theme.Current == Theme.Mode.Dark);
                // 系统不支持深色时，「跟随系统」这一档用不了 —— 灰掉并说明原因
                _rAuto.Enabled = Theme.SystemSupportsTheme;
                _hint.Visible = !Theme.SystemSupportsTheme;
            }
            finally { _syncing = false; }
        }

        private void OnRadioChanged(object sender, EventArgs e)
        {
            if (_syncing) return;
            ThemeRadio r = sender as ThemeRadio;
            if (r == null || !r.Checked) return;

            Theme.Mode m = Theme.Mode.Auto;
            if (r == _rLight) m = Theme.Mode.Light;
            else if (r == _rDark) m = Theme.Mode.Dark;

            Theme.Current = m;
            Theme.Refresh();
            Theme.Save();

            Theme.Apply(this);
            BackColor = Theme.Bg;
            if (_ownerForRefresh != null) Theme.Apply(_ownerForRefresh);
            Invalidate(true);
        }
    }
}
