// 自绘的标签条。
//
// 为什么不用原生 TabControl：它的标签带边线、背景、整个外框都是系统按浅色画的，
// 属于非客户区 —— 设 BackColor、挂 Paint 事件都压不下去（试过，扫描像素证明没变）。
// 干脆自己画一条：标签、hover、选中态、边线全走主题色。
// 对外只暴露 Titles / SelectedIndex / SelectedIndexChanged，调用方当 TabControl 用就行。
//
// 本文件保持 C# 2.0 语法（XP 版用 v2.0.50727 的 csc 编译）：不用 var / lambda / 自动属性。
using System;
using System.Drawing;
using System.Windows.Forms;

namespace FileDiffTool
{
    internal class TabStrip : Control
    {
        private string[] _titles = new string[0];
        private int _selected;
        private int _hot = -1;

        public event EventHandler SelectedIndexChanged;

        public TabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = TextUtil.PickUiFont(9f, false);
            Cursor = Cursors.Hand;
        }

        public string[] Titles
        {
            get { return _titles; }
            set { _titles = (value == null) ? new string[0] : value; Invalidate(); }
        }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int v = value;
                if (v < 0) v = 0;
                if (_titles.Length > 0 && v > _titles.Length - 1) v = _titles.Length - 1;
                if (v == _selected) return;
                _selected = v;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        private int TabWidth(int i)
        {
            int w = 0;
            try { w = TextRenderer.MeasureText(_titles[i], Font).Width; }
            catch (Exception) { }
            return w + Theme.S(30);
        }

        private int HitTest(int x)
        {
            int left = 0;
            for (int i = 0; i < _titles.Length; i++)
            {
                int w = TabWidth(i);
                if (x >= left && x < left + w) return i;
                left += w;
            }
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Theme.PanelAlt))
                e.Graphics.FillRectangle(b, ClientRectangle);

            int x = 0;
            for (int i = 0; i < _titles.Length; i++)
            {
                int w = TabWidth(i);
                Rectangle r = new Rectangle(x, 0, w, Height);
                bool sel = (i == _selected);
                Color back = sel ? Theme.Bg : (_hot == i ? Theme.DropHover : Theme.PanelAlt);
                using (SolidBrush b = new SolidBrush(back))
                    e.Graphics.FillRectangle(b, r);
                using (Pen p = new Pen(Theme.Border))
                {
                    e.Graphics.DrawLine(p, r.Right - 1, 0, r.Right - 1, Height - 1);
                    if (sel) e.Graphics.DrawLine(p, r.Left, 0, r.Right - 1, 0);
                }
                TextRenderer.DrawText(e.Graphics, _titles[i], Font, r, sel ? Theme.Text : Theme.SubText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                x += w;
            }

            // 标签右边剩下的空白 + 整条标签带的底线，都按主题画（原生控件就是这几条线发白）
            if (x < Width)
            {
                using (SolidBrush b = new SolidBrush(Theme.PanelAlt))
                    e.Graphics.FillRectangle(b, new Rectangle(x, 0, Width - x, Height));
            }
            using (Pen p2 = new Pen(Theme.Border))
                e.Graphics.DrawLine(p2, 0, Height - 1, Width, Height - 1);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int i = HitTest(e.X);
            if (i != _hot) { _hot = i; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hot != -1) { _hot = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int i = HitTest(e.X);
            if (i >= 0) SelectedIndex = i;
            base.OnMouseDown(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left && _selected > 0) SelectedIndex = _selected - 1;
            else if (e.KeyCode == Keys.Right && _selected < _titles.Length - 1) SelectedIndex = _selected + 1;
            base.OnKeyDown(e);
        }
    }

    /// <summary>
    /// 自绘进度条：系统 ProgressBar 的轨道在深色下永远是浅色，BackColor / uxtheme 都压不下去。
    /// 对外只用 Minimum / Maximum / Value（和 ProgressBar 一致的用法）。
    /// </summary>
    internal class ThemeProgress : Control
    {
        private int _value;
        private int _min;
        private int _max = 100;

        public ThemeProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public int Minimum
        {
            get { return _min; }
            set { _min = value; if (_max <= _min) _max = _min + 1; Invalidate(); }
        }

        public int Maximum
        {
            get { return _max; }
            set { _max = (value <= _min) ? _min + 1 : value; Invalidate(); }
        }

        public int Value
        {
            get { return _value; }
            set { _value = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle r = ClientRectangle;
            using (SolidBrush b = new SolidBrush(Theme.PanelAlt))
                e.Graphics.FillRectangle(b, r);

            double f = (double)(_value - _min) / (_max - _min);
            if (f < 0) f = 0;
            if (f > 1) f = 1;
            int w = (int)Math.Round((r.Width - 4) * f);
            if (w > 0)
            {
                using (SolidBrush b2 = new SolidBrush(Theme.Accent))
                    e.Graphics.FillRectangle(b2, new Rectangle(2, 2, w, r.Height - 4));
            }
            using (Pen p = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
        }
    }
}
