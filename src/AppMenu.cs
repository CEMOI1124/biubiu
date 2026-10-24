using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Biubiu
{
    public class AppMenuForm : Form
    {
        const int ItemHeight = 42;
        const int MenuWidth = 196;
        readonly string[] items = new string[] { "设置", "关于" };
        int hover = -1;
        bool closed;

        public event Action SettingsChosen;
        public event Action AboutChosen;

        public AppMenuForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 10.5f);
            ClientSize = new Size(MenuWidth, ItemHeight * items.Length);
            DoubleBuffered = true;
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                    Choose(null);
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            using (GraphicsPath path = CardPainter.RoundRect(new Rectangle(0, 0, Width, Height), 14))
                Region = new Region(path);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Choose(null);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int next = -1;
            if (e.Y >= 0 && e.Y < ItemHeight * items.Length)
                next = e.Y / ItemHeight;
            if (next == hover)
                return;
            hover = next;
            Cursor = next >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = -1;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
                return;
            if (e.Y >= 0 && e.Y < ItemHeight)
                Choose(SettingsChosen);
            else if (e.Y >= ItemHeight && e.Y < ItemHeight * items.Length)
                Choose(AboutChosen);
        }

        void Choose(Action action)
        {
            if (closed)
                return;
            closed = true;
            Close();
            if (action != null)
                action();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.White);
            for (int i = 0; i < items.Length; i++)
            {
                Rectangle row = new Rectangle(0, i * ItemHeight, Width, ItemHeight);
                if (i == hover)
                {
                    using (Brush brush = new SolidBrush(CardPainter.ChipFill))
                        g.FillRectangle(brush, row);
                }
                Rectangle text = new Rectangle(18, row.Y, Width - 36, row.Height);
                TextRenderer.DrawText(g, items[i], Font, text, CardPainter.TitleColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            using (GraphicsPath path = CardPainter.RoundRect(new Rectangle(1, 1, Width - 3, Height - 3), 12))
            using (Pen pen = new Pen(CardPainter.IdleBorder))
                g.DrawPath(pen, path);
        }
    }
}
