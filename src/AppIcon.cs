using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Biubiu
{
    public static class AppIcon
    {
        static readonly List<Form> applied = new List<Form>();
        static readonly List<IntPtr> kept = new List<IntPtr>();
        static MemoryStream held;
        static Bitmap image;

        public static Bitmap Image
        {
            get
            {
                if (image != null)
                    return image;
                Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Biubiu.icon.png");
                if (source == null)
                    return null;
                using (source)
                {
                    held = new MemoryStream();
                    source.CopyTo(held);
                    held.Position = 0;
                    image = new Bitmap(held);
                }
                return image;
            }
        }

        public static void Apply(Form form)
        {
            if (form == null || Image == null || applied.Contains(form))
                return;
            applied.Add(form);
            form.ShowIcon = true;
            form.Icon = CloneIcon(32);
            if (form.IsHandleCreated)
                SendIcons(form);
            else
                form.HandleCreated += delegate { SendIcons(form); };
        }

        static void SendIcons(Form form)
        {
            if (form.IsDisposed || !form.IsHandleCreated)
                return;
            IntPtr big = NativeIcon(256);
            IntPtr small = NativeIcon(16);
            SendMessage(form.Handle, 0x80, (IntPtr)1, big);
            SendMessage(form.Handle, 0x80, IntPtr.Zero, small);
            kept.Add(big);
            kept.Add(small);
        }

        static Icon CloneIcon(int size)
        {
            IntPtr handle = NativeIcon(size);
            try
            {
                using (Icon temp = Icon.FromHandle(handle))
                    return (Icon)temp.Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }

        static IntPtr NativeIcon(int size)
        {
            using (Bitmap sized = Fit(Image, size))
                return sized.GetHicon();
        }

        static Bitmap Fit(Bitmap source, int size)
        {
            Bitmap dest = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(dest))
            {
                graphics.Clear(Color.Transparent);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float fit = Math.Min((float)size / source.Width, (float)size / source.Height);
                int width = Math.Max(1, (int)Math.Round(source.Width * fit));
                int height = Math.Max(1, (int)Math.Round(source.Height * fit));
                graphics.DrawImage(source, (size - width) / 2, (size - height) / 2, width, height);
            }
            return dest;
        }

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);
    }
}
