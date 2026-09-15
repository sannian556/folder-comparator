// 历史记录面板（原生版）：列出「导出成功」的快照，可还原、可按差异重新对比、可清理。
// 与主窗口一致：AutoScaleMode.None + 手动按 DPI 缩放（自动缩放对本程序无效）。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FileDiffTool
{
    internal class HistoryForm : Form
    {
        private DataGridView _grid;
        private Button _btnRestore;
        private Button _btnClean;
        private Button _btnClose;
        private Label _info;

        private float _uiScale = 1f;
        private List<HistoryEntry> _list = new List<HistoryEntry>();

        /// <summary>用户点「还原」/ 双击后要还原的那一条；取消时为 null。</summary>
        public HistoryEntry RestoreTarget;

        public HistoryForm()
        {
            _uiScale = DetectUiScale();
            Font = TextUtil.PickUiFont(9f, false);
            AutoScaleMode = AutoScaleMode.None;

            Text = "历史记录";
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(S(720), S(420));
            MinimumSize = new Size(S(560), S(320));

            Label intro = new Label();
            intro.Text = "每跑完一次对比就存一条（不用导出）；双击某条可还原那次的对比结果。";
            intro.AutoSize = true;
            intro.Location = new Point(S(14), S(12));
            intro.ForeColor = Color.FromArgb(90, 100, 114);
            Controls.Add(intro);

            _grid = new DataGridView();
            _grid.Location = new Point(S(14), S(40));
            _grid.Size = new Size(S(692), S(300));
            _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.BackgroundColor = Color.White;
            _grid.BorderStyle = BorderStyle.FixedSingle;
            _grid.Font = TextUtil.PickUiFont(9f, false);
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _grid.ColumnHeadersHeight = S(28);
            _grid.RowTemplate.Height = S(26);

            DataGridViewTextBoxColumn cTime = new DataGridViewTextBoxColumn();
            cTime.HeaderText = "时间";
            cTime.Width = S(132);
            cTime.ReadOnly = true;
            cTime.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(cTime);

            DataGridViewTextBoxColumn cDirs = new DataGridViewTextBoxColumn();
            cDirs.HeaderText = "文件夹 A ⇄ B";
            cDirs.Width = S(238);
            cDirs.ReadOnly = true;
            cDirs.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(cDirs);

            DataGridViewTextBoxColumn cDiff = new DataGridViewTextBoxColumn();
            cDiff.HeaderText = "差异概要";
            cDiff.Width = S(178);
            cDiff.ReadOnly = true;
            cDiff.SortMode = DataGridViewColumnSortMode.NotSortable;
            _grid.Columns.Add(cDiff);

            DataGridViewButtonColumn cRe = new DataGridViewButtonColumn();
            cRe.HeaderText = "重跑";
            cRe.Width = S(78);
            cRe.Text = "重新对比";
            cRe.UseColumnTextForButtonValue = true;
            cRe.FlatStyle = FlatStyle.Standard;
            cRe.SortMode = DataGridViewColumnSortMode.NotSortable;   // 排序会让行序和 _list 错位
            _grid.Columns.Add(cRe);

            DataGridViewCheckBoxColumn cChk = new DataGridViewCheckBoxColumn();
            cChk.HeaderText = "清理";
            cChk.Width = S(56);
            cChk.ThreeState = false;
            cChk.SortMode = DataGridViewColumnSortMode.NotSortable;
            cChk.HeaderCell.ToolTipText = "点这一列的任意位置即可打勾；点列表头 = 全选 / 全不选";
            _grid.Columns.Add(cChk);

            _grid.CellContentClick += OnCellContentClick;
            _grid.CellMouseDown += OnCellMouseDown;
            _grid.CellValueChanged += OnCellValueChanged;
            _grid.ColumnHeaderMouseClick += OnColumnHeaderClick;
            _grid.CellDoubleClick += OnCellDoubleClick;
            _grid.CurrentCellDirtyStateChanged += OnCellDirty;
            Controls.Add(_grid);

            _info = new Label();
            _info.AutoSize = true;
            _info.Location = new Point(S(14), S(350));
            _info.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            _info.ForeColor = Color.FromArgb(90, 100, 114);
            Controls.Add(_info);

            _btnRestore = MakeButton("还原", 14, 376, 96, 30, OnRestore);
            _btnRestore.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;

            _btnClean = MakeButton("一键清理历史记录", 118, 376, 190, 30, OnClean);
            _btnClean.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;

            _btnClose = MakeButton("关闭", 596, 376, 110, 30, null);
            _btnClose.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            _btnClose.DialogResult = DialogResult.Cancel;

            CancelButton = _btnClose;

            Load += OnLoaded;
            Fill();
        }

        // ── DPI ─────────────────────────────────────────────
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

        private void OnLoaded(object sender, EventArgs e)
        {
            if (_grid.Rows.Count > 0)
            {
                _grid.Rows[0].Selected = true;
                _grid.CurrentCell = _grid.Rows[0].Cells[0];
            }
        }

        // ── 填充列表 ─────────────────────────────────────────
        private void Fill()
        {
            _list = HistoryStore.Load();

            _grid.Rows.Clear();
            for (int i = 0; i < _list.Count; i++)
            {
                HistoryEntry e = _list[i];
                int idx = _grid.Rows.Add(e.TimeText, e.DirsText, e.DiffText, "重新对比", false);

                DataGridViewRow row = _grid.Rows[idx];
                row.Cells[0].ToolTipText = e.TimeFull;
                string tip = "A：" + e.DirA + "\r\nB：" + e.DirB + "\r\n导出到："
                    + (e.ZipPath != null && e.ZipPath.Length > 0 ? e.ZipPath : "（这条还没导出过）");
                if (e.IgnoreSummary != null && e.IgnoreSummary.Length > 0)
                    tip += "\r\n当时的忽略规则：" + e.IgnoreSummary;
                row.Cells[1].ToolTipText = tip;
                row.Cells[2].ToolTipText = e.DiffText + "\r\n（清单：" + e.Items.Count + " 条）";
            }

            UpdateInfo();
            UpdateCleanButton();
            bool has = _list.Count > 0;
            _btnRestore.Enabled = has;
            _btnClean.Enabled = has;
        }

        private void UpdateInfo()
        {
            long bytes = 0;
            try
            {
                System.IO.FileInfo fi = new System.IO.FileInfo(HistoryStore.FilePath);
                if (fi.Exists) bytes = fi.Length;
            }
            catch (Exception) { }

            if (_list.Count == 0)
                _info.Text = "还没有历史记录 —— 跑完一次对比就会自动生成一条。";
            else
                _info.Text = "共 " + _list.Count + " 条 · 占用 " + TextUtil.FormatSize(bytes)
                    + "　（最多 " + HistoryStore.MaxEntries + " 条 / "
                    + TextUtil.FormatSize(HistoryStore.MaxBytes) + "，超了自动删最旧的）";
        }

        private int CheckedCount()
        {
            int n = 0;
            for (int i = 0; i < _grid.Rows.Count; i++)
            {
                object v = _grid.Rows[i].Cells[4].Value;
                if (v != null && v is bool && (bool)v) n++;
            }
            return n;
        }

        private void UpdateCleanButton()
        {
            int n = CheckedCount();
            if (n == 0) _btnClean.Text = "一键清理历史记录";
            else _btnClean.Text = "清理选中的 " + n + " 条";
        }

        private void OnCellDirty(object sender, EventArgs e)
        {
            // 勾选框立刻提交，好让按钮文案实时变
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            UpdateCleanButton();
        }

        /// <summary>
        /// 点「清理」列任意位置都能打勾。
        /// 坑：DataGridView 只在点中「小方框本身」时才把它当内容点击（ContentBounds），
        /// 点在这一格的其他位置（70px 宽的格子，方框只占中间十几像素）完全没反应；
        /// 而点中方框时框架自己会切换值，再手动切一次就会互相抵消（这就是之前"打勾没反应"的根因）。
        /// 所以：点方框 → 交给框架；点格子其余位置 → 自己切换。两条路各切一次，正好一下一勾。
        /// </summary>
        private void OnCellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 4) return;
            if (e.RowIndex >= _grid.Rows.Count) return;

            DataGridViewCell cell = _grid.Rows[e.RowIndex].Cells[4];
            Rectangle glyph = cell.ContentBounds;
            if (glyph.Width > 0 && glyph.Height > 0 && glyph.Contains(e.X, e.Y)) return;   // 交给框架

            _grid.EndEdit();
            object v = cell.Value;
            bool cur = (v != null && v is bool) && (bool)v;
            cell.Value = !cur;
            UpdateCleanButton();
        }

        /// <summary>点「清理」列表头 = 全选 / 全不选。</summary>
        private void OnColumnHeaderClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex != 4 || _grid.Rows.Count == 0) return;
            bool all = CheckedCount() == _grid.Rows.Count;
            _grid.EndEdit();
            for (int i = 0; i < _grid.Rows.Count; i++) _grid.Rows[i].Cells[4].Value = !all;
            UpdateCleanButton();
        }

        /// <summary>空格键切换、程序化赋值等也要让按钮文案跟上。</summary>
        private void OnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 4) UpdateCleanButton();
        }

        private void OnCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex == 4)
            {
                // 值已经被框架（点方框）或 OnCellMouseDown（点格子其他位置）切好了，这里只刷新文案
                UpdateCleanButton();
            }
            else if (e.ColumnIndex == 3)
            {
                OnRecompare(e.RowIndex);
            }
        }

        private void OnCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex == 3 || e.ColumnIndex == 4) return;   // 按钮/勾选框各自处理
            DoRestore(e.RowIndex);
        }

        private void OnRestore(object sender, EventArgs e)
        {
            if (_grid.CurrentRow == null) return;
            DoRestore(_grid.CurrentRow.Index);
        }

        private void DoRestore(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _list.Count) return;
            RestoreTarget = _list[rowIndex];
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnRecompare(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _list.Count) return;
            HistoryEntry e = _list[rowIndex];

            List<string> missing = new List<string>();
            if (!System.IO.Directory.Exists(e.DirA)) missing.Add("A：" + e.DirA);
            if (!System.IO.Directory.Exists(e.DirB)) missing.Add("B：" + e.DirB);
            if (missing.Count > 0)
            {
                MessageBox.Show(this,
                    "这些文件夹已经不在原来的位置了，没法重跑：\r\n\r\n" + string.Join("\r\n", missing.ToArray()),
                    "文件比较器", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 交给主窗口去重新对比（把两个路径填好并开跑）
            RestoreTarget = e;
            RecompareRequested = true;
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>true 表示用户要的是「按当前内容重新对比」，而不是还原快照。</summary>
        public bool RecompareRequested;

        private void OnClean(object sender, EventArgs e)
        {
            int n = CheckedCount();
            string ask;
            if (n == 0)
                ask = "确定要清空全部历史记录吗？共 " + _list.Count + " 条。\r\n\r\n删除后无法恢复。";
            else
                ask = "确定要清理选中的 " + n + " 条历史记录吗？\r\n\r\n删除后无法恢复。";

            DialogResult r = MessageBox.Show(this, ask, "文件比较器",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (r != DialogResult.OK) return;

            List<HistoryEntry> kept = new List<HistoryEntry>();
            for (int i = 0; i < _list.Count; i++)
            {
                bool marked = false;
                if (i < _grid.Rows.Count)
                {
                    object v = _grid.Rows[i].Cells[4].Value;
                    if (v != null && v is bool) marked = (bool)v;
                }
                if (n == 0) continue;          // 一条没勾 = 清空全部
                if (!marked) kept.Add(_list[i]);
            }
            if (n > 0 && kept.Count == _list.Count) return;

            HistoryStore.Trim(kept);
            HistoryStore.Save(kept);
            Fill();
        }
    }
}
