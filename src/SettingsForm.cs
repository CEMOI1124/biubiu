using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Biubiu
{
    public class SettingsForm : Form
    {
        readonly TrackBar slider = new TrackBar();
        readonly Label percentLabel = new Label();
        readonly SizePreview preview = new SizePreview();
        readonly int requestedPercent;
        bool live;

        public event Action<float> ScaleChanged;

        public SettingsForm(float scale)
        {
            Text = "设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = CardPainter.Canvas;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(400, 392);
            AppIcon.Apply(this);

            Label heading = new Label();
            heading.Text = "界面大小";
            heading.ForeColor = CardPainter.TitleColor;
            heading.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            heading.AutoSize = true;
            heading.Location = new Point(24, 24);

            percentLabel.ForeColor = CardPainter.MutedColor;
            percentLabel.Font = new Font("Segoe UI", 10f);
            percentLabel.TextAlign = ContentAlignment.MiddleRight;
            percentLabel.Size = new Size(72, 24);
            percentLabel.Location = new Point(304, 24);

            slider.Minimum = (int)Math.Round(UiSettings.MinScale * 100f);
            slider.Maximum = (int)Math.Round(UiSettings.MaxScale * 100f);
            slider.TickFrequency = 10;
            slider.SmallChange = 1;
            slider.LargeChange = 5;
            slider.AutoSize = false;
            slider.Size = new Size(352, 40);
            slider.Location = new Point(24, 58);
            slider.BackColor = CardPainter.Canvas;
            int percent = (int)Math.Round(UiSettings.ClampScale(scale) * 100f);
            if (percent < slider.Minimum)
                percent = slider.Minimum;
            if (percent > slider.Maximum)
                percent = slider.Maximum;
            requestedPercent = percent;
            slider.Value = percent;
            slider.ValueChanged += delegate
            {
                if (live)
                    Publish();
            };
            Shown += delegate { ArmSlider(); };

            Label hint = new Label();
            hint.Text = "标题、内容和备注一起变化，备注按同一比例。";
            hint.ForeColor = CardPainter.MutedColor;
            hint.AutoSize = false;
            hint.Size = new Size(352, 36);
            hint.Location = new Point(24, 108);

            preview.SampleScale = slider.Value / 100f;
            preview.Location = new Point(24, 150);
            preview.Size = new Size(352, 168);

            Button done = new Button();
            done.Text = "完成";
            done.FlatStyle = FlatStyle.Flat;
            done.FlatAppearance.BorderSize = 0;
            done.BackColor = CardPainter.TitleColor;
            done.ForeColor = Color.White;
            done.Font = new Font("Segoe UI", 9.5f);
            done.Size = new Size(72, 32);
            done.Location = new Point(304, 336);
            done.Cursor = Cursors.Hand;
            done.DialogResult = DialogResult.OK;

            percentLabel.Text = slider.Value.ToString() + "%";
            AcceptButton = done;
            CancelButton = done;
            Controls.Add(heading);
            Controls.Add(percentLabel);
            Controls.Add(slider);
            Controls.Add(hint);
            Controls.Add(preview);
            Controls.Add(done);
        }

        void ArmSlider()
        {
            live = false;
            if (slider.Value != requestedPercent)
                slider.Value = requestedPercent;
            percentLabel.Text = requestedPercent.ToString() + "%";
            preview.SampleScale = requestedPercent / 100f;
            preview.Invalidate();
            live = true;
        }

        void Publish()
        {
            float scale = slider.Value / 100f;
            percentLabel.Text = slider.Value.ToString() + "%";
            preview.SampleScale = scale;
            preview.Invalidate();
            if (ScaleChanged != null)
                ScaleChanged(scale);
        }
    }

    class SizePreview : Control
    {
        public float SampleScale = 1f;

        public SizePreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = CardPainter.Canvas;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(CardPainter.Canvas);
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = CardPainter.RoundRect(bounds, 14))
            using (Brush fill = new SolidBrush(Color.White))
            using (Pen pen = new Pen(CardPainter.IdleBorder))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
                g.SetClip(path);
            }

            float scale = SampleScale;
            if (scale < UiSettings.MinScale)
                scale = UiSettings.MinScale;
            using (Font titleFont = new Font("Segoe UI", 11f * scale, FontStyle.Bold))
            using (Font bodyFont = new Font("Segoe UI", 9f * scale))
            {
                Size titleSize = TextRenderer.MeasureText(g, "标题", titleFont, new Size(Width, Height), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                Size noteSize = TextRenderer.MeasureText(g, "备注", bodyFont, new Size(Width, Height), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                int pad = Math.Max(6, (int)Math.Round(8 * scale));
                int row = Math.Max(titleSize.Height, noteSize.Height + pad);
                int top = 16 + Math.Max(0, (row - titleSize.Height) / 2);
                TextRenderer.DrawText(g, "标题", titleFont, new Rectangle(16, top, titleSize.Width + 4, titleSize.Height), CardPainter.TitleColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                int chipHeight = noteSize.Height + pad;
                Rectangle chip = new Rectangle(16 + titleSize.Width + pad, 16 + (row - chipHeight) / 2, noteSize.Width + pad * 2, chipHeight);
                using (GraphicsPath chipPath = CardPainter.RoundRect(chip, Math.Max(6, pad)))
                using (Brush chipFill = new SolidBrush(CardPainter.ChipFill))
                using (Pen chipPen = new Pen(CardPainter.IdleBorder))
                {
                    g.FillPath(chipFill, chipPath);
                    g.DrawPath(chipPen, chipPath);
                }
                TextRenderer.DrawText(g, "备注", bodyFont, chip, CardPainter.MutedColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                int contentTop = 16 + row + 12;
                Rectangle content = new Rectangle(16, contentTop, Math.Max(1, Width - 32), Math.Max(1, Height - contentTop - 16));
                TextRenderer.DrawText(g, "这里是任务内容，会和标题、备注一起变化。", bodyFont, content, CardPainter.MutedColor, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
            g.ResetClip();
        }
    }
}
