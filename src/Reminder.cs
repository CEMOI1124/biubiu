using System;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Biubiu
{
    public static class Reminder
    {
        const int FlashAll = 3;

        static byte[] clip;
        static MemoryStream playing;
        static SoundPlayer player;

        public static void Play(int volume)
        {
            volume = UiSettings.ClampVolume(volume);
            if (volume <= 0)
                return;
            byte[] source = Clip();
            if (source == null)
                return;
            byte[] wav = volume >= 100 ? source : Scale(source, volume);
            if (player != null)
            {
                player.Stop();
                player.Dispose();
                player = null;
            }
            if (playing != null)
            {
                playing.Dispose();
                playing = null;
            }
            playing = new MemoryStream(wav);
            player = new SoundPlayer(playing);
            player.Play();
        }

        public static void Flash(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return;
            FlashInfo info = new FlashInfo();
            info.cbSize = Marshal.SizeOf(typeof(FlashInfo));
            info.hwnd = hwnd;
            info.dwFlags = FlashAll;
            info.uCount = 4;
            info.dwTimeout = 0;
            FlashWindowEx(ref info);
        }

        static byte[] Clip()
        {
            if (clip != null)
                return clip;
            Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Biubiu.remind.wav");
            if (source == null)
                return null;
            using (source)
            {
                MemoryStream copy = new MemoryStream();
                source.CopyTo(copy);
                clip = copy.ToArray();
            }
            return clip;
        }

        static byte[] Scale(byte[] wav, int volume)
        {
            int data = FindChunk(wav, "data");
            if (data < 0 || data + 8 > wav.Length)
                return wav;
            int length = BitConverter.ToInt32(wav, data + 4);
            if (length < 0)
                return wav;
            byte[] copy = new byte[wav.Length];
            Buffer.BlockCopy(wav, 0, copy, 0, wav.Length);
            int start = data + 8;
            int end = start + length;
            if (end > copy.Length)
                end = copy.Length;
            double gain = volume / 100d;
            for (int i = start; i + 1 < end; i += 2)
            {
                short sample = BitConverter.ToInt16(copy, i);
                int scaled = (int)Math.Round(sample * gain);
                if (scaled > short.MaxValue)
                    scaled = short.MaxValue;
                if (scaled < short.MinValue)
                    scaled = short.MinValue;
                copy[i] = (byte)(scaled & 255);
                copy[i + 1] = (byte)((scaled >> 8) & 255);
            }
            return copy;
        }

        static int FindChunk(byte[] wav, string name)
        {
            int offset = 12;
            while (offset + 8 <= wav.Length)
            {
                string id = Encoding.ASCII.GetString(wav, offset, 4);
                int size = BitConverter.ToInt32(wav, offset + 4);
                if (id == name)
                    return offset;
                if (size < 0)
                    return -1;
                offset += 8 + size;
                if ((size & 1) == 1)
                    offset++;
            }
            return -1;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct FlashInfo
        {
            public int cbSize;
            public IntPtr hwnd;
            public int dwFlags;
            public int uCount;
            public int dwTimeout;
        }

        [DllImport("user32.dll")]
        static extern bool FlashWindowEx(ref FlashInfo info);
    }
}
