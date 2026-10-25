using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Biubiu
{
    public class AboutForm : Form
    {
        const string GithubUrl = "https://github.com/CEMOI1124/biubiu";
        const int ContentWidth = 452;

        public AboutForm()
        {
            Text = "关于";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = CardPainter.Canvas;
            Font = new Font("Segoe UI", 9.5f);
            AppIcon.Apply(this);

            Panel mark = new Panel();
            mark.Location = new Point(24, 22);
            mark.Size = new Size(64, 64);
            mark.BackColor = CardPainter.Canvas;
            mark.Paint += PaintMark;
            Controls.Add(mark);

            Label title = new Label();
            title.Text = "biubiu";
            title.Font = new Font("Segoe UI", 18f, FontStyle.Bold);
            title.ForeColor = CardPainter.TitleColor;
            title.AutoSize = true;
            title.Location = new Point(104, 26);
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "Cursor 多窗口任务进度监测工具";
            subtitle.Font = new Font("Segoe UI", 9.5f);
            subtitle.ForeColor = CardPainter.MutedColor;
            subtitle.AutoSize = true;
            subtitle.Location = new Point(106, 60);
            Controls.Add(subtitle);

            int y = 112;
            AddHeading(ref y, "\u2728 功能简介");
            AddBody(ref y, "专门面向 Cursor 编辑器用户开发的桌面托盘工具：\n\n自动识别打开的多个 Cursor 窗口，抓取正在执行的任务信息；\n\n卡片式列表展示每个任务的实时进度，任务结束后高亮绿色标记完成状态。\n\n常驻后台、低资源占用，不用来回切换窗口查看 AI 任务进度。");
            y += 8;
            AddHeading(ref y, "\U0001F4A1 说明");
            AddBody(ref y, "第一次运行时会自动在 Cursor 安装 hook 插件用于检测。\n\n仅本地监测，不会上传你的代码、对话内容到第三方服务器。");
            y += 12;
            AddMeta(ref y, "作者", "CEMOI1124", false);
            AddMeta(ref y, "Github", GithubUrl, true);
            ClientSize = new Size(24 + ContentWidth + 24, y + 8);
        }

        void AddHeading(ref int y, string text)
        {
            Label heading = new Label();
            heading.Text = text;
            heading.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            heading.ForeColor = CardPainter.TitleColor;
            heading.BackColor = CardPainter.Canvas;
            heading.AutoSize = false;
            heading.UseMnemonic = false;
            heading.Location = new Point(24, y);
            heading.Size = new Size(ContentWidth, 26);
            Controls.Add(heading);
            y += 30;
        }

        void AddBody(ref int y, string text)
        {
            Font font = new Font("Segoe UI", 9.5f);
            Size measured = TextRenderer.MeasureText(text, font, new Size(ContentWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            Label body = new Label();
            body.Text = text;
            body.Font = font;
            body.ForeColor = CardPainter.MutedColor;
            body.BackColor = CardPainter.Canvas;
            body.AutoSize = false;
            body.UseMnemonic = false;
            body.Location = new Point(24, y);
            body.Size = new Size(ContentWidth, measured.Height + 2);
            Controls.Add(body);
            y += body.Height + 8;
        }

        void AddMeta(ref int y, string caption, string value, bool link)
        {
            Label name = new Label();
            name.Text = caption;
            name.ForeColor = CardPainter.MutedColor;
            name.BackColor = CardPainter.Canvas;
            name.AutoSize = true;
            name.Location = new Point(24, y);
            Controls.Add(name);

            if (link)
            {
                LinkLabel url = new LinkLabel();
                url.Text = value;
                url.AutoSize = true;
                url.Location = new Point(96, y);
                url.LinkColor = CardPainter.TitleColor;
                url.ActiveLinkColor = CardPainter.CompletedBorder;
                url.VisitedLinkColor = CardPainter.TitleColor;
                url.LinkBehavior = LinkBehavior.HoverUnderline;
                url.LinkClicked += OpenGithub;
                Controls.Add(url);
                y += 28;
                return;
            }

            Label text = new Label();
            text.Text = value;
            text.ForeColor = CardPainter.TitleColor;
            text.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            text.BackColor = CardPainter.Canvas;
            text.AutoSize = true;
            text.Location = new Point(96, y);
            Controls.Add(text);
            y += 28;
        }

        static void PaintMark(object sender, PaintEventArgs e)
        {
            Bitmap bitmap = AppIcon.Image;
            if (bitmap == null)
                return;
            Control box = (Control)sender;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(bitmap, new Rectangle(0, 0, box.Width, box.Height));
        }

        void OpenGithub(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                Process.Start(GithubUrl);
            }
            catch (Exception)
            {
                MessageBox.Show(this, GithubUrl, "Biubiu");
            }
        }
    }
}
