using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Biubiu
{
    public static class CardPainter
    {
        public static readonly Color Canvas = Color.FromArgb(255, 247, 246, 244);
        public static readonly Color CardFill = Color.White;
        public static readonly Color IdleBorder = Color.FromArgb(255, 231, 229, 228);
        public static readonly Color CompletedBorder = Color.FromArgb(255, 22, 128, 74);
        public static readonly Color CompletedFill = Color.FromArgb(255, 220, 242, 228);
        public static readonly Color WarningBorder = Color.FromArgb(255, 161, 98, 7);
        public static readonly Color WarningFill = Color.FromArgb(255, 255, 244, 214);
        public static readonly Color ErrorBorder = Color.FromArgb(255, 185, 28, 28);
        public static readonly Color ErrorFill = Color.FromArgb(255, 253, 226, 226);
        public static readonly Color Track = Color.FromArgb(255, 231, 229, 228);
        public static readonly Color RunningHighlight = Color.FromArgb(255, 28, 25, 23);
        public static readonly Color TitleColor = Color.FromArgb(255, 28, 25, 23);
        public static readonly Color MutedColor = Color.FromArgb(255, 120, 113, 108);
        public static readonly Color ChipFill = Color.FromArgb(255, 245, 245, 244);
        public static readonly Color NoteHint = Color.FromArgb(255, 168, 162, 158);
        public const int Radius = 16;
        static float scale = 1f;
        static Bitmap comet;
        static int cometWidth;
        static int cometHeight;

        public static float Scale
        {
            get { return scale; }
        }

        public static int CardWidth
        {
            get { return Px(320); }
        }

        public static int CardHeight
        {
            get { return Px(156); }
        }

        public static void SetScale(float value)
        {
            scale = UiSettings.ClampScale(value);
        }

        public static int Px(int value)
        {
            return Math.Max(1, (int)Math.Round(value * scale));
        }

        public static Font CreateTitleFont()
        {
            return new Font("Segoe UI", 11f * scale, FontStyle.Bold);
        }

        public static Font CreateBodyFont()
        {
            return new Font("Segoe UI", 9f * scale);
        }

        public static Font CreateElapsedFont()
        {
            return new Font("Segoe UI", 8.5f * scale);
        }

        public static Rectangle ProgressBar(Rectangle bounds)
        {
            return new Rectangle(Px(18), bounds.Height - Px(28), bounds.Width - Px(36), Px(8));
        }

        public static Rectangle CheckBounds(Rectangle bounds)
        {
            return new Rectangle(bounds.Width - Px(32), Px(14), Px(16), Px(16));
        }

        public static Rectangle ElapsedBounds(Rectangle bounds, string text)
        {
            if (string.IsNullOrEmpty(text))
                return Rectangle.Empty;
            using (Font font = CreateElapsedFont())
            {
                Size size = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, Px(24)), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                Rectangle check = CheckBounds(bounds);
                int width = size.Width + Px(2);
                int height = Math.Max(size.Height, Px(16));
                int right = check.Left - Px(8);
                int top = check.Top + (check.Height - height) / 2;
                return new Rectangle(right - width, top, width, height);
            }
        }

        static int ElapsedReserve(Rectangle bounds)
        {
            using (Font font = CreateElapsedFont())
            {
                Size size = TextRenderer.MeasureText("88:88:88", font, new Size(int.MaxValue, Px(24)), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                return CheckBounds(bounds).Width + Px(32) + size.Width + Px(8);
            }
        }

        public static Rectangle TitleBounds(Rectangle bounds, string project)
        {
            return TitleBounds(bounds, project, false);
        }

        public static Rectangle TitleBounds(Rectangle bounds, string project, bool reserveElapsed)
        {
            string title = string.IsNullOrEmpty(project) ? "未命名" : project;
            int reserved = reserveElapsed ? ElapsedReserve(bounds) + Px(70) : Px(124);
            int max = bounds.Width - reserved;
            if (max < Px(48))
                max = Px(48);
            using (Font font = CreateTitleFont())
            {
                Size size = TextRenderer.MeasureText(title, font, new Size(max, Px(24)), TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                int width = size.Width;
                if (width > max)
                    width = max;
                if (width < 1)
                    width = 1;
                return new Rectangle(Px(18), Px(16), width, Px(24));
            }
        }

        public static Rectangle NoteBounds(Rectangle bounds, string project, string note, bool editing)
        {
            return NoteBounds(bounds, project, note, editing, false);
        }

        public static Rectangle NoteBounds(Rectangle bounds, string project, string note, bool editing, bool reserveElapsed)
        {
            Rectangle title = TitleBounds(bounds, project, reserveElapsed);
            int left = title.Right + Px(6);
            int rightGap = reserveElapsed ? ElapsedReserve(bounds) : Px(36);
            int right = bounds.Width - rightGap;
            int max = right - left;
            if (max < Px(24))
                max = Px(24);
            int width = max;
            if (!editing)
            {
                string shown = string.IsNullOrEmpty(note) ? "\u5907\u6ce8" : note;
                using (Font font = CreateBodyFont())
                {
                    Size size = TextRenderer.MeasureText(shown, font, new Size(max, Px(24)), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    width = size.Width + Px(18);
                }
                if (width < Px(56))
                    width = Px(56);
                if (width > max)
                    width = max;
            }
            return new Rectangle(left, title.Y, width, title.Height);
        }

        public static void Paint(Graphics graphics, Rectangle bounds, ProjectCard card, float phase)
        {
            Paint(graphics, bounds, card, phase, false);
        }

        public static void Paint(Graphics graphics, Rectangle bounds, ProjectCard card, float phase, bool editingNote)
        {
            if (graphics == null || card == null)
                return;
            if (phase < 0f)
                phase = 0f;
            if (phase > 1f)
                phase = 1f;

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Canvas);
            string status = card.Status ?? "";
            bool completed = status == "completed";
            bool running = status == "running";
            bool aborted = status == "aborted";
            bool waiting = status == "confirm";
            bool failed = status == "error";
            Color border = IdleBorder;
            Color fillColor = CardFill;
            int thickness = 1;
            if (completed)
            {
                border = CompletedBorder;
                fillColor = CompletedFill;
                thickness = 2;
            }
            else if (failed)
            {
                border = ErrorBorder;
                fillColor = ErrorFill;
                thickness = 2;
            }
            else if (aborted || waiting)
            {
                border = WarningBorder;
                fillColor = WarningFill;
                thickness = 2;
            }

            int radius = Px(Radius);
            Rectangle shadowBounds = bounds;
            shadowBounds.Offset(0, Px(1));
            using (GraphicsPath shadow = RoundRect(shadowBounds, radius))
            using (Brush shadowBrush = new SolidBrush(Color.FromArgb(16, 28, 25, 23)))
                graphics.FillPath(shadowBrush, shadow);

            using (GraphicsPath outer = RoundRect(bounds, radius))
            using (Brush fill = new SolidBrush(border))
                graphics.FillPath(fill, outer);

            Rectangle inner = new Rectangle(bounds.X + thickness, bounds.Y + thickness, bounds.Width - thickness * 2, bounds.Height - thickness * 2);
            int innerRadius = radius - thickness;
            if (innerRadius < 1)
                innerRadius = 1;
            using (GraphicsPath innerPath = RoundRect(inner, innerRadius))
            using (Brush fill = new SolidBrush(fillColor))
                graphics.FillPath(fill, innerPath);

            if (running)
                DrawRunningBar(graphics, bounds, phase);

            if (completed)
                DrawCheck(graphics, CheckBounds(bounds));

            string elapsed = RunClocks.Text(card.StartedAt, card.EndedAt, DateTimeOffset.Now);
            bool reserveElapsed = elapsed.Length > 0;
            if (reserveElapsed)
            {
                using (Font elapsedFont = CreateElapsedFont())
                    TextRenderer.DrawText(graphics, elapsed, elapsedFont, ElapsedBounds(bounds, elapsed), TitleColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }

            string project = string.IsNullOrEmpty(card.Project) ? "未命名" : card.Project;
            string summary = SummaryText(card);
            Color summaryColor = MutedColor;
            if (status == "completed")
                summaryColor = CompletedBorder;
            else if (failed)
                summaryColor = ErrorBorder;
            else if (aborted || waiting)
                summaryColor = WarningBorder;
            else if (status != "idle")
                summaryColor = TitleColor;
            using (Font titleFont = CreateTitleFont())
            using (Font bodyFont = CreateBodyFont())
            {
                TextRenderer.DrawText(graphics, project, titleFont, TitleBounds(bounds, card.Project, reserveElapsed), TitleColor, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                Rectangle note = NoteBounds(bounds, card.Project, card.Note, editingNote, reserveElapsed);
                using (GraphicsPath notePath = RoundRect(note, Px(8)))
                using (Brush noteBrush = new SolidBrush(ChipFill))
                using (Pen notePen = new Pen(IdleBorder))
                {
                    graphics.FillPath(noteBrush, notePath);
                    graphics.DrawPath(notePen, notePath);
                }
                if (!editingNote)
                {
                    string noteText = string.IsNullOrEmpty(card.Note) ? "\u5907\u6ce8" : card.Note;
                    Color noteColor = string.IsNullOrEmpty(card.Note) ? NoteHint : MutedColor;
                    Rectangle noteTextBounds = new Rectangle(note.X + Px(8), note.Y, Math.Max(1, note.Width - Px(16)), note.Height);
                    TextRenderer.DrawText(graphics, noteText, bodyFont, noteTextBounds, noteColor, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                }
                TextRenderer.DrawText(graphics, summary, bodyFont, new Rectangle(Px(18), Px(56), Math.Max(1, bounds.Width - Px(36)), Px(64)), summaryColor, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }

        static string SummaryText(ProjectCard card)
        {
            string status = card.Status ?? "";
            if (status == "idle")
                return "暂无任务";
            if (status == "confirm")
                return "\u8bf7\u8fd4\u56de Cursor \u786e\u8ba4\u6216\u6267\u884c\u6307\u4ee4";
            if (status == "aborted")
                return Prefix("⏹已中止", card.Summary);
            if (status == "error")
                return Prefix("❌出错了", card.Summary);
            if (status == "unknown")
                return Prefix("❓状态未知", card.Summary);
            return card.Summary ?? "";
        }

        static string Prefix(string label, string summary)
        {
            if (string.IsNullOrEmpty(summary))
                return label;
            return label + "  " + summary;
        }

        static void DrawRunningBar(Graphics graphics, Rectangle bounds, float phase)
        {
            Rectangle bar = ProgressBar(bounds);
            if (bar.Width < 2 || bar.Height < 2)
                return;
            int core = bar.Height;
            int tail = Px(156);
            float center = bar.Left - core / 2f + phase * (bar.Width + core + tail);
            float headLeft = center - core / 2f - bar.Left;
            Bitmap stamp = CometStamp(bar.Width, bar.Height, headLeft, tail, core, RunningHighlight, Track);
            GraphicsState state = graphics.Save();
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.None;
            graphics.DrawImageUnscaled(stamp, bar.Left, bar.Top);
            graphics.Restore(state);
        }

        static Bitmap CometStamp(int width, int height, float headLeft, int tail, int core, Color color, Color track)
        {
            if (comet == null || cometWidth != width || cometHeight != height)
            {
                if (comet != null)
                    comet.Dispose();
                comet = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                cometWidth = width;
                cometHeight = height;
            }
            BitmapData bits = comet.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            int stride = bits.Stride;
            byte[] pixels = new byte[stride * height];
            float fadeStart = headLeft - tail;
            float fadeWidth = tail + core / 2f;
            float headCenter = headLeft + core / 2f;
            float radius = height / 2f;
            for (int y = 0; y < height; y++)
            {
                float py = y + 0.5f;
                for (int x = 0; x < width; x++)
                {
                    float px = x + 0.5f;
                    float capsule = Capsule(px, py, width, radius);
                    if (capsule <= 0f)
                        continue;
                    float ink = 0f;
                    if (fadeWidth > 1f)
                    {
                        float along = (px - fadeStart) / fadeWidth;
                        if (along > 0f && along < 1f)
                            ink = along * along * along;
                    }
                    float dx = px - headCenter;
                    float dy = py - radius;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    float head = radius + 0.5f - dist;
                    if (head > ink)
                        ink = head > 1f ? 1f : (head > 0f ? head : 0f);
                    if (ink > 1f)
                        ink = 1f;
                    int alpha = (int)Math.Round(255f * capsule);
                    if (alpha <= 0)
                        continue;
                    if (alpha > 255)
                        alpha = 255;
                    int i = y * stride + x * 4;
                    pixels[i] = (byte)Math.Round(color.B * ink + track.B * (1f - ink));
                    pixels[i + 1] = (byte)Math.Round(color.G * ink + track.G * (1f - ink));
                    pixels[i + 2] = (byte)Math.Round(color.R * ink + track.R * (1f - ink));
                    pixels[i + 3] = (byte)alpha;
                }
            }
            Marshal.Copy(pixels, 0, bits.Scan0, pixels.Length);
            comet.UnlockBits(bits);
            return comet;
        }

        static float Capsule(float x, float y, int width, float radius)
        {
            float cx = x;
            if (cx < radius)
                cx = radius;
            else if (cx > width - radius)
                cx = width - radius;
            float dx = x - cx;
            float dy = y - radius;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            float cover = radius + 0.5f - dist;
            if (cover >= 1f)
                return 1f;
            if (cover <= 0f)
                return 0f;
            return cover;
        }

        static void DrawCheck(Graphics graphics, Rectangle bounds)
        {
            using (Brush brush = new SolidBrush(CompletedBorder))
                graphics.FillEllipse(brush, bounds);
            float unit = bounds.Width / 16f;
            float x = bounds.Left + 4f * unit;
            float y = bounds.Top + 8f * unit;
            using (Pen pen = new Pen(Color.White, 1.6f * unit))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                graphics.DrawLine(pen, x, y, x + 3f * unit, y + 3f * unit);
                graphics.DrawLine(pen, x + 3f * unit, y + 3f * unit, x + 8f * unit, y - 2f * unit);
            }
        }

        public static GraphicsPath RoundRect(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            if (diameter > bounds.Width)
                diameter = bounds.Width;
            if (diameter > bounds.Height)
                diameter = bounds.Height;
            if (diameter < 2)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
