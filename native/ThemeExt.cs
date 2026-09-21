// Theme 的扩展部分（partial）：把界面里各处硬编码的浅色换算成深色。
//
// 为什么用「颜色换算」而不是把几百处 Color.FromArgb 挨个改掉：
//   1) 改动面小、风险低 —— 哪个浅色对应哪个深色，全在这一张表里；
//   2) 双向且幂等 —— 认得的颜色在两套之间互换，认不得的原样返回，
//      所以「浅色 → 深色 → 浅色」来回切不会越切越花。
//
// 表在每次换肤时现建（不是静态字段）：调色板 Bg/Text 这些是另一个 partial 文件里的静态字段，
// 跨文件的静态字段初始化顺序没有保证，写成静态表可能拿到默认的全黑。
//
// 本文件必须保持 C# 2.0 语法（XP 版是用 v2.0.50727 的 csc 编的）：
// 不用 var / lambda / 自动属性 / 扩展方法。
using System;
using System.Drawing;
using System.Windows.Forms;

namespace FileDiffTool
{
    internal static partial class Theme
    {
        private static Color[] P(Color light, Color dark)
        {
            return new Color[] { light, dark };
        }

        /// <summary>浅色 ↔ 深色成对表。要调配色只动这里。</summary>
        private static Color[][] BuildPairs()
        {
            return new Color[][]
            {
                // ── 背景 ──
                P(Color.White, Bg),
                P(Color.FromArgb(255, 243, 243), Color.FromArgb(58, 34, 34)),    // 计分卡：内容不同
                P(Color.FromArgb(236, 253, 245), Color.FromArgb(30, 52, 44)),    // 计分卡：仅在 B
                P(Color.FromArgb(254, 242, 242), Color.FromArgb(58, 34, 34)),    // 计分卡：仅在 A
                P(Color.FromArgb(243, 247, 252), Color.FromArgb(32, 42, 58)),    // 计分卡：完全相同

                // ── 文字 ──
                P(Color.FromArgb(38, 50, 66), Text),        // 标题、正文
                P(Color.FromArgb(70, 80, 92), SubText),     // 次要
                P(Color.FromArgb(100, 110, 122), SubText),
                P(Color.FromArgb(110, 120, 132), SubText),
                P(Color.FromArgb(120, 130, 142), Dim),      // 提示
                P(Color.FromArgb(90, 110, 140), SubText),
                P(Color.Black, Text),
                P(Color.Gray, SubText),

                // ── 强调色 ──
                P(Color.FromArgb(74, 108, 247), Link),                            // 链接蓝
                P(Color.FromArgb(190, 60, 60), Color.FromArgb(240, 110, 110)),    // 警告红
                P(Color.FromArgb(16, 150, 100), Color.FromArgb(80, 200, 150)),    // 成功绿
                P(Color.FromArgb(200, 80, 60), Color.FromArgb(240, 140, 110))     // 橙红
            };
        }

        private static bool Same(Color a, Color b)
        {
            return a.ToArgb() == b.ToArgb();
        }

        private static bool Known(Color c, Color[][] pairs)
        {
            for (int i = 0; i < pairs.Length; i++)
                if (Same(c, pairs[i][0]) || Same(c, pairs[i][1])) return true;
            return false;
        }

        /// <summary>
        /// 把颜色换算到当前主题那一侧：深色主题下"浅 → 深"，浅色主题下"深 → 浅"，认不得的原样返回。
        /// 注意必须看 IsLight —— 早先写成无条件双向，结果浅色主题下把浅色也换成了深色、
        /// 深色主题下又把刚染深的颜色换回白色，每刷一遍就白回来一遍。
        /// </summary>
        public static Color Map(Color c, Color[][] pairs)
        {
            for (int i = 0; i < pairs.Length; i++)
            {
                if (IsLight)
                {
                    if (Same(c, pairs[i][1])) return pairs[i][0];
                }
                else
                {
                    if (Same(c, pairs[i][0])) return pairs[i][1];
                }
            }
            return c;
        }

        /// <summary>
        /// 单参数版：界面上那些"刷新时现设颜色"的地方（UpdatePaths 之类）用它包一下，
        /// 否则换肤之后又被写回硬编码的浅色文字，深色界面上就成了一片看不清的暗字。
        /// </summary>
        public static Color Mix(Color c)
        {
            return Map(c, BuildPairs());
        }

        /// <summary>映射表认不出的背景（多半是系统默认色）按控件类型兜底。</summary>
        private static Color FallbackBack(Control c)
        {
            if (c is TextBox) return IsLight ? Color.White : Panel;
            if (c is ListView || c is TabControl || c is TabPage) return IsLight ? Color.White : Bg;
            if (c is Button || c is CheckBox || c is RadioButton) return IsLight ? SystemColors.Control : BtnFace;
            // 窗口本身还是系统底色；面板/布局容器用白色 —— 浅色下那份"白"原来是 Label 自己带的，
            // 现在 Label 统一透明了，就得由容器来提供，否则整个界面会发灰
            if (c is Form) return IsLight ? SystemColors.Control : Bg;
            return IsLight ? Color.White : Bg;
        }

        private static Color FallbackFore(Control c)
        {
            if (c is Button || c is CheckBox || c is RadioButton) return IsLight ? Color.Black : Text;
            return IsLight ? SystemColors.ControlText : Text;
        }

        /// <summary>是不是"浅底色"。深色主题下，凡是没登记进成对表的浅色都一律压深，
        /// 免得漏网的容器/标签留下一块白（计分卡那种彩色底在成对表里，走不到这里）。</summary>
        private static bool IsPale(Color c)
        {
            if (c.A == 0) return false;
            double lum = c.R * 0.299 + c.G * 0.587 + c.B * 0.114;
            return lum > 170;
        }

        /// <summary>上一次换肤访问/改动了多少个控件（自动化断言用：能分清"没遍历到"和"算了没设"）</summary>
        public static int LastVisited = 0;
        public static int LastChanged = 0;

        /// <summary>递归给整棵控件树换肤（深色染深、浅色还原）。建完界面和切主题时各调一次。</summary>
        public static void Apply(Control root)
        {
            if (root == null) return;
            LastVisited = 0;
            LastChanged = 0;
            Color[][] pairs = BuildPairs();
            ApplyCore(root, pairs);
            try { root.Invalidate(true); } catch (Exception) { }
        }

        private static void ApplyCore(Control c, Color[][] pairs)
        {
            LastVisited++;

            // 标签这类控件本来就该"透明贴在父容器上"：它构造时从父容器继承来的背景色会被
            // WinForms 当成显式颜色留下（浅色时是白），光靠"把浅色染深"认不出它 ——
            // 直接强制透明，让父容器的深色透上来，白块就没了。
            if (c is Label || c is CheckBox || c is RadioButton || c is LinkLabel)
            {
                if (c.BackColor.A != 0)
                {
                    c.BackColor = Color.Transparent;
                    LastChanged++;
                }
            }

            // 透明背景（Label、CheckBox 等）永远不碰：一旦染成不透明，底下那层颜色就没了
            bool keepBg = (c.BackColor.A == 0);

            Color wantBg = c.BackColor;
            if (!keepBg)
            {
                if (Known(c.BackColor, pairs)) wantBg = Map(c.BackColor, pairs);
                else if (!IsLight && IsPale(c.BackColor)) wantBg = Bg;
                else wantBg = FallbackBack(c);
            }
            if (!keepBg && wantBg != c.BackColor)
            {
                c.BackColor = wantBg;
                LastChanged++;
            }

            // 列表在深色下是自绘的，文字颜色由自绘代码给，这里别去动
            if (!(c is ListView))
            {
                Color wantFg = Known(c.ForeColor, pairs) ? Map(c.ForeColor, pairs) : FallbackFore(c);
                if (wantFg != c.ForeColor) c.ForeColor = wantFg;
            }

            // ── 按控件类型做定制 ──
            Button b = c as Button;
            if (b != null) StyleButton(b);

            ButtonBase bb = c as ButtonBase;
            if (bb != null && b == null) StyleChoice(bb);

            TextBox tb = c as TextBox;
            if (tb != null) StyleTextBox(tb);

            ListView lv = c as ListView;
            if (lv != null) StyleListView(lv);

            DataGridView grid = c as DataGridView;
            if (grid != null) StyleGrid(grid);

            TabControl tc = c as TabControl;
            if (tc != null) StyleTabControl(tc);

            if (c is TextBox || c is ListView || c is TreeView || c is TabControl || c is ProgressBar)
                ApplyScrollTheme(c);

            // GroupBox 的边框和标题是系统画的（uxtheme 压不动它），自己描一圈盖掉
            GroupBox gb = c as GroupBox;
            if (gb != null) StyleGroupBox(gb);

            Form f = c as Form;
            if (f != null && f.Handle != IntPtr.Zero) ApplyTitleBar(f.Handle, IsLight);

            for (int i = 0; i < c.Controls.Count; i++) ApplyCore(c.Controls[i], pairs);
        }

        // ============================================================ 列表自绘
        /// <summary>深色下 ListView 交给系统画就是白底黑字，改成自绘；浅色下还原成系统绘制。</summary>
        public static void StyleListView(ListView lv)
        {
            if (lv == null) return;
            if (IsLight)
            {
                lv.OwnerDraw = false;
                lv.BackColor = Color.White;
                lv.ForeColor = SystemColors.ControlText;
                return;
            }
            lv.BackColor = Bg;
            lv.ForeColor = Text;
            lv.OwnerDraw = true;
            // 挂之前先摘：换肤会被反复调用，不摘就叠起来（每切一次主题多画一遍）
            lv.DrawColumnHeader -= OnDrawColumnHeader;
            lv.DrawItem -= OnDrawListItem;
            lv.DrawSubItem -= OnDrawListSubItem;
            lv.DrawColumnHeader += OnDrawColumnHeader;
            lv.DrawItem += OnDrawListItem;
            lv.DrawSubItem += OnDrawListSubItem;
        }

        private static void OnDrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(PanelAlt))
                e.Graphics.FillRectangle(b, e.Bounds);
            using (Pen p = new Pen(Border))
            {
                e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top + S(4), e.Bounds.Right - 1, e.Bounds.Bottom - S(4));
            }
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            flags |= (e.Header.TextAlign == HorizontalAlignment.Right) ? TextFormatFlags.Right : TextFormatFlags.Left;
            Rectangle r = e.Bounds;
            r.Inflate(-S(6), 0);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Header.ListView.Font, r, SubText, flags);
        }

        private static void OnDrawListItem(object sender, DrawListViewItemEventArgs e)
        {
            // 先把整行铺满：最右侧空白区不属于任何列，不铺会留一条白边
            Color back = e.Item.Selected ? SelBg : (e.ItemIndex % 2 == 1 ? RowAlt : Bg);
            using (SolidBrush b = new SolidBrush(back))
                e.Graphics.FillRectangle(b, e.Bounds);
        }

        private static void OnDrawListSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            ListView lv = e.Item.ListView;
            Color back = e.Item.Selected ? SelBg : (e.ItemIndex % 2 == 1 ? RowAlt : Bg);
            using (SolidBrush b = new SolidBrush(back))
                e.Graphics.FillRectangle(b, e.Bounds);

            Color c = e.Item.Selected ? SelText : Text;
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            bool right = (e.ColumnIndex > 0 && e.ColumnIndex < lv.Columns.Count
                          && lv.Columns[e.ColumnIndex].TextAlign == HorizontalAlignment.Right);
            flags |= right ? TextFormatFlags.Right : TextFormatFlags.Left;
            Rectangle r = e.Bounds;
            r.Inflate(-S(6), 0);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, lv.Font, r, c, flags);
        }

        // ============================================================ 分组框
        /// <summary>
        /// GroupBox 的边框是系统（uxtheme）画的，改 BackColor 完全不生效，深色下就是一圈浅灰线。
        /// 做法：在它画完之后自己照原位再描一圈主题色的框，把系统那条盖掉。
        /// </summary>
        public static void StyleGroupBox(GroupBox g)
        {
            if (g == null) return;
            g.Paint -= OnPaintGroupBox;
            if (IsLight) return;
            g.ForeColor = SubText;          // 标题文字也是系统画的，颜色靠 ForeColor
            g.Paint += OnPaintGroupBox;
        }

        private static void OnPaintGroupBox(object sender, PaintEventArgs e)
        {
            GroupBox g = sender as GroupBox;
            if (g == null) return;
            // 系统那条框线大致落在标题文字的半高处；画粗一点确保盖住（差 1px 会露出双线）
            int top = g.Font.Height / 2;
            Rectangle r = new Rectangle(0, top, g.Width - 1, g.Height - top - 1);
            using (Pen p = new Pen(Border, 2f))
                e.Graphics.DrawRectangle(p, r);
        }

        // ============================================================ 表格
        /// <summary>
        /// DataGridView 有自己一套颜色属性，光设 BackColor 不够：表头、单元格、网格线、选中色各有各的；
        /// 而且不关掉 EnableHeadersVisualStyles，表头永远是系统浅色。
        /// </summary>
        public static void StyleGrid(DataGridView g)
        {
            if (g == null) return;
            if (IsLight)
            {
                g.EnableHeadersVisualStyles = true;      // 交回系统画，跟以前一模一样
                g.BackgroundColor = SystemColors.AppWorkspace;
                g.DefaultCellStyle.BackColor = Color.White;
                g.DefaultCellStyle.ForeColor = SystemColors.ControlText;
                g.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
                g.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
                g.GridColor = SystemColors.ControlDark;
            }
            else
            {
                g.EnableHeadersVisualStyles = false;     // 不关掉的话表头还是浅色
                g.BackgroundColor = Bg;
                g.DefaultCellStyle.BackColor = Bg;
                g.DefaultCellStyle.ForeColor = Text;
                g.DefaultCellStyle.SelectionBackColor = SelBg;
                g.DefaultCellStyle.SelectionForeColor = SelText;
                g.ColumnHeadersDefaultCellStyle.BackColor = PanelAlt;
                g.ColumnHeadersDefaultCellStyle.ForeColor = SubText;
                g.ColumnHeadersDefaultCellStyle.SelectionBackColor = PanelAlt;
                g.RowHeadersDefaultCellStyle.BackColor = PanelAlt;
                g.RowHeadersDefaultCellStyle.ForeColor = SubText;
                g.GridColor = Border;
            }
            g.BorderStyle = BorderStyle.FixedSingle;
        }

        // ============================================================ 标签页
        /// <summary>系统画的标签头在深色下是白底，改成自绘（浅色保持系统原生）。</summary>
        public static void StyleTabControl(TabControl tc)
        {
            if (tc == null) return;
            if (IsLight)
            {
                tc.DrawMode = TabDrawMode.Normal;
                tc.BackColor = SystemColors.Control;
                tc.ForeColor = SystemColors.ControlText;
                return;
            }
            tc.DrawMode = TabDrawMode.OwnerDrawFixed;
            tc.BackColor = PanelAlt;
            tc.ForeColor = Text;
            tc.DrawItem -= OnDrawTabItem;
            tc.DrawItem += OnDrawTabItem;
            tc.Paint -= OnPaintTabFrame;
            tc.Paint += OnPaintTabFrame;      // 标签带和外框那几条线也是系统画的，自己描一遍
        }

        /// <summary>
        /// TabControl 的标签带上下边线、以及它整个外框，都是系统按浅色画的。
        /// 标签本身由 OnDrawTabItem 自绘，这里只补那几条线（画在标签带边缘，不压文字）。
        /// </summary>
        private static void OnPaintTabFrame(object sender, PaintEventArgs e)
        {
            TabControl tc = sender as TabControl;
            if (tc == null || IsLight) return;
            Rectangle d = tc.DisplayRectangle;
            using (Pen p = new Pen(Border))
            {
                e.Graphics.DrawLine(p, 0, 1, tc.Width, 1);
                e.Graphics.DrawLine(p, 0, d.Top - 1, tc.Width, d.Top - 1);
                e.Graphics.DrawRectangle(p, 0, 0, tc.Width - 1, tc.Height - 1);
                e.Graphics.DrawRectangle(p, d.Left, d.Top, d.Width - 1, d.Height - 1);
            }
        }

        private static void OnDrawTabItem(object sender, DrawItemEventArgs e)
        {
            TabControl tc = sender as TabControl;
            if (tc == null || e.Index < 0 || e.Index >= tc.TabPages.Count) return;
            bool active = (e.Index == tc.SelectedIndex);
            Rectangle r = e.Bounds;
            using (SolidBrush b = new SolidBrush(active ? Bg : PanelAlt))
                e.Graphics.FillRectangle(b, r);
            using (Pen p = new Pen(Border))
            {
                if (active) e.Graphics.DrawLine(p, r.Left, r.Top, r.Right, r.Top);
                e.Graphics.DrawLine(p, r.Right - 1, r.Top + S(3), r.Right - 1, r.Bottom);
            }
            string t = tc.TabPages[e.Index].Text;
            TextRenderer.DrawText(e.Graphics, t, tc.Font, r, active ? Text : SubText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // 最后一个标签右边到控件右边缘那一截，系统仍会画成浅色 —— 自己按主题补上
            if (e.Index == tc.TabCount - 1 && r.Right < tc.Width)
            {
                Rectangle rest = new Rectangle(r.Right, r.Top, tc.Width - r.Right, r.Height);
                using (SolidBrush b2 = new SolidBrush(PanelAlt))
                    e.Graphics.FillRectangle(b2, rest);
                using (Pen p2 = new Pen(Border))
                    e.Graphics.DrawLine(p2, rest.Left, rest.Bottom - 1, rest.Right, rest.Bottom - 1);
            }
        }
    }
}
