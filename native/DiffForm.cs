// 逐行差异查看窗口：左侧文件夹 A 的行，右侧文件夹 B 的行
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FileDiffTool
{
    /// <summary>开启双缓冲的 DataGridView，滚动和重绘更平滑。</summary>
    internal class SmoothGrid : DataGridView
    {
        public SmoothGrid()
        {
            this.DoubleBuffered = true;
        }
    }

    internal class DiffForm : Form
    {
        private List<DiffLine> _rows;
        private DataGridView _grid;
        private ToolStripStatusLabel _summary;
        private List<int> _changeRowIndexes = new List<int>();
        private int _navCursor = -1;
        private string _textA;
        private string _textB;

        private static readonly Color ColorRemoved = Color.FromArgb(255, 236, 236);
        private static readonly Color ColorAdded = Color.FromArgb(232, 250, 236);
        private static readonly Color ColorSkip = Color.FromArgb(246, 247, 249);
        private static readonly Color ColorRemovedText = Color.FromArgb(170, 30, 30);
        private static readonly Color ColorAddedText = Color.FromArgb(20, 120, 60);
        private static readonly Color ColorLineNo = Color.FromArgb(150, 150, 150);

        public DiffForm(string title, string textA, string textB)
        {
            _textA = textA;
            _textB = textB;

            string[] linesA = TextUtil.SplitLines(textA);
            string[] linesB = TextUtil.SplitLines(textB);
            List<DiffLine> diff = DiffEngine.Compute(linesA, linesB);
            _rows = DiffEngine.Collapse(diff, 2);

            int added, removed;
            DiffEngine.CountChanges(diff, out added, out removed);

            this.Text = "差异对比 — " + title;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(1080, 700);
            this.MinimumSize = new Size(640, 400);
            this.ShowInTaskbar = false;

            ToolStrip strip = new ToolStrip();
            strip.GripStyle = ToolStripGripStyle.Hidden;
            strip.RenderMode = ToolStripRenderMode.System;

            ToolStripLabel nameLabel = new ToolStripLabel(title);
            nameLabel.Font = TextUtil.PickUiFont(9f, true);
            strip.Items.Add(nameLabel);
            strip.Items.Add(new ToolStripSeparator());

            ToolStripLabel statA = new ToolStripLabel("A 共 " + linesA.Length + " 行");
            ToolStripLabel statB = new ToolStripLabel("B 共 " + linesB.Length + " 行");
            ToolStripLabel statDiff = new ToolStripLabel("差异：+" + added + " / -" + removed);
            statDiff.ForeColor = ColorAddedText;
            strip.Items.Add(statA);
            strip.Items.Add(new ToolStripSeparator());
            strip.Items.Add(statB);
            strip.Items.Add(new ToolStripSeparator());
            strip.Items.Add(statDiff);
            strip.Items.Add(new ToolStripSeparator());

            ToolStripButton prevBtn = new ToolStripButton("上一处差异");
            prevBtn.Click += OnPrevDiff;
            ToolStripButton nextBtn = new ToolStripButton("下一处差异");
            nextBtn.Click += OnNextDiff;
            strip.Items.Add(prevBtn);
            strip.Items.Add(nextBtn);

            _grid = new SmoothGrid();
            _grid.Dock = DockStyle.Fill;
            _grid.VirtualMode = true;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = true;
            _grid.CellBorderStyle = DataGridViewCellBorderStyle.None;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _grid.BackgroundColor = Color.White;
            _grid.EnableHeadersVisualStyles = false;

            Font mono = PickMonoFont();

            DataGridViewTextBoxColumn colNoA = new DataGridViewTextBoxColumn();
            colNoA.HeaderText = "行";
            colNoA.Width = 52;
            colNoA.SortMode = DataGridViewColumnSortMode.NotSortable;
            colNoA.DefaultCellStyle.ForeColor = ColorLineNo;
            colNoA.DefaultCellStyle.Alignment = DataGridViewContentAlignment.TopRight;
            colNoA.DefaultCellStyle.Font = mono;

            DataGridViewTextBoxColumn colA = new DataGridViewTextBoxColumn();
            colA.HeaderText = "文件夹 A";
            colA.Width = 460;
            colA.SortMode = DataGridViewColumnSortMode.NotSortable;
            colA.DefaultCellStyle.Font = mono;

            DataGridViewTextBoxColumn colNoB = new DataGridViewTextBoxColumn();
            colNoB.HeaderText = "行";
            colNoB.Width = 52;
            colNoB.SortMode = DataGridViewColumnSortMode.NotSortable;
            colNoB.DefaultCellStyle.ForeColor = ColorLineNo;
            colNoB.DefaultCellStyle.Alignment = DataGridViewContentAlignment.TopRight;
            colNoB.DefaultCellStyle.Font = mono;

            DataGridViewTextBoxColumn colB = new DataGridViewTextBoxColumn();
            colB.HeaderText = "文件夹 B";
            colB.Width = 460;
            colB.SortMode = DataGridViewColumnSortMode.NotSortable;
            colB.DefaultCellStyle.Font = mono;

            _grid.Columns.Add(colNoA);
            _grid.Columns.Add(colA);
            _grid.Columns.Add(colNoB);
            _grid.Columns.Add(colB);
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            colA.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            colB.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            colA.FillWeight = 50;
            colB.FillWeight = 50;

            _grid.RowCount = _rows.Count;
            _grid.CellValueNeeded += OnCellValueNeeded;
            _grid.CellFormatting += OnCellFormatting;
            _grid.RowTemplate.Height = 19;

            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Kind != DiffLine.Equal) _changeRowIndexes.Add(i);
            }

            StatusStrip status = new StatusStrip();
            status.SizingGrip = false;
            _summary = new ToolStripStatusLabel();
            _summary.Text = BuildSummary(added, removed, linesA.Length, linesB.Length);
            status.Items.Add(_summary);

            this.Controls.Add(_grid);
            this.Controls.Add(status);
            this.Controls.Add(strip);

            this.KeyPreview = true;
            this.KeyDown += OnKeyDown;

            Theme.Apply(this);   // 差异窗体的表格/状态栏也要跟着主题走
        }

        private static Font PickMonoFont()
        {
            string[] names = new string[] { "Consolas", "Courier New", "Lucida Console", "宋体" };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    Font f = new Font(names[i], 9f);
                    if (string.Equals(f.Name, names[i], StringComparison.OrdinalIgnoreCase)) return f;
                    f.Dispose();
                }
                catch (Exception) { }
            }
            return new Font(FontFamily.GenericMonospace, 9f);
        }

        private static string BuildSummary(int added, int removed, int linesA, int linesB)
        {
            if (added == 0 && removed == 0)
                return "两个文件的文本内容逐行一致（可能仅换行符或编码不同）。";
            return "共 " + added + " 行新增、" + removed + " 行删除。相同内容已折叠，只显示差异附近 2 行。";
        }

        private void OnCellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
            DiffLine row = _rows[e.RowIndex];

            if (row.Kind == DiffLine.Skip)
            {
                if (e.ColumnIndex == 0) e.Value = "···";
                else if (e.ColumnIndex == 1) e.Value = "（省略了 " + row.SkippedCount + " 行相同内容）";
                else e.Value = "";
                return;
            }

            switch (e.ColumnIndex)
            {
                case 0:
                    e.Value = row.IndexA >= 0 ? (row.IndexA + 1).ToString() : "";
                    break;
                case 1:
                    e.Value = row.TextA == null ? "" : row.TextA;
                    break;
                case 2:
                    e.Value = row.IndexB >= 0 ? (row.IndexB + 1).ToString() : "";
                    break;
                case 3:
                    e.Value = row.TextB == null ? "" : row.TextB;
                    break;
            }
        }

        private void OnCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _rows.Count) return;
            DiffLine row = _rows[e.RowIndex];

            if (row.Kind == DiffLine.Skip)
            {
                e.CellStyle.BackColor = ColorSkip;
                e.CellStyle.ForeColor = Color.Gray;
                e.CellStyle.Font = new Font(e.CellStyle.Font, FontStyle.Italic);
                if (e.ColumnIndex == 1) e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                return;
            }

            if (row.Kind == DiffLine.Removed && (e.ColumnIndex == 0 || e.ColumnIndex == 1))
            {
                e.CellStyle.BackColor = ColorRemoved;
                if (e.ColumnIndex == 0) e.CellStyle.ForeColor = ColorRemovedText;
            }
            else if (row.Kind == DiffLine.Added && (e.ColumnIndex == 2 || e.ColumnIndex == 3))
            {
                e.CellStyle.BackColor = ColorAdded;
                if (e.ColumnIndex == 2) e.CellStyle.ForeColor = ColorAddedText;
            }
            else if (row.Kind == DiffLine.Equal)
            {
                e.CellStyle.BackColor = Color.White;
            }
        }

        private void OnNextDiff(object sender, EventArgs e)
        {
            if (_changeRowIndexes.Count == 0) return;
            _navCursor++;
            if (_navCursor >= _changeRowIndexes.Count) _navCursor = 0;
            GoToChange(_changeRowIndexes[_navCursor]);
        }

        private void OnPrevDiff(object sender, EventArgs e)
        {
            if (_changeRowIndexes.Count == 0) return;
            _navCursor--;
            if (_navCursor < 0) _navCursor = _changeRowIndexes.Count - 1;
            GoToChange(_changeRowIndexes[_navCursor]);
        }

        private void GoToChange(int rowIndex)
        {
            try
            {
                _grid.ClearSelection();
                _grid.FirstDisplayedScrollingRowIndex = rowIndex;
                _grid.Rows[rowIndex].Selected = true;
                _summary.Text = "第 " + (_navCursor + 1) + " / " + _changeRowIndexes.Count + " 处差异（行 " + (rowIndex + 1) + "）";
            }
            catch (Exception) { }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { this.Close(); return; }
            if (e.KeyCode == Keys.F3 || (e.KeyCode == Keys.N && e.Control)) { OnNextDiff(null, null); e.Handled = true; }
            if (e.KeyCode == Keys.F4 || (e.KeyCode == Keys.P && e.Control)) { OnPrevDiff(null, null); e.Handled = true; }
        }
    }
}
