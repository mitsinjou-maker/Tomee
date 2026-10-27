using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Tomee
{
    /// <summary>Petit « clic » synthétisé en mémoire et joué de façon asynchrone.</summary>
    internal static class ClickSound
    {
        const uint SND_ASYNC = 0x1, SND_NODEFAULT = 0x2, SND_MEMORY = 0x4;

        static GCHandle left, right;
        static int builtVolume = -1;

        public static void Play(bool rightButton, int volume)
        {
            if (volume != builtVolume) Build(volume);
            var h = rightButton ? right : left;
            Native.PlaySound(h.AddrOfPinnedObject(), IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
        }

        static void Build(int volume)
        {
            Native.PlaySound(IntPtr.Zero, IntPtr.Zero, 0); // stoppe un son en cours avant de libérer son tampon
            if (left.IsAllocated) left.Free();
            if (right.IsAllocated) right.Free();
            double amp = Math.Max(0, Math.Min(100, volume)) / 100.0;
            left = GCHandle.Alloc(MakeWav(2400, amp), GCHandleType.Pinned);
            right = GCHandle.Alloc(MakeWav(1500, amp), GCHandleType.Pinned);
            builtVolume = volume;
        }

        static byte[] MakeWav(double freq, double amp)
        {
            const int rate = 44100;
            int n = rate * 40 / 1000;
            var rnd = new Random(7);
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + n * 2);
                w.Write(new[] { 'W', 'A', 'V', 'E' }); w.Write(new[] { 'f', 'm', 't', ' ' });
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2);
                w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(n * 2);
                for (int i = 0; i < n; i++)
                {
                    double t = (double)i / rate;
                    double env = Math.Exp(-t * 160);
                    double s = (Math.Sin(2 * Math.PI * freq * t) * 0.65 + (rnd.NextDouble() * 2 - 1) * 0.35) * env;
                    w.Write((short)(s * amp * 30000));
                }
                return ms.ToArray();
            }
        }
    }
}
