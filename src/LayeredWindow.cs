using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Tomee
{
    /// <summary>
    /// Fenêtre « layered » à alpha par pixel, rendue dans une DIB 32 bits prémultipliée.
    /// Seules les zones modifiées sont envoyées au compositeur (UpdateLayeredWindowIndirect + prcDirty).
    /// </summary>
    internal unsafe class LayeredWindow : NativeWindow, IDisposable
    {
        public Rectangle Bounds { get; private set; }
        public Bitmap Canvas { get; private set; }
        public Graphics G { get; private set; }
        public bool Visible { get; private set; }

        IntPtr memDc, dib, oldBmp;
        bool presented, disposed;

        public LayeredWindow(Rectangle bounds, bool clickThrough)
        {
            Bounds = bounds;
            var cp = new CreateParams();
            cp.Caption = AppInfo.Name;
            cp.X = bounds.X; cp.Y = bounds.Y; cp.Width = bounds.Width; cp.Height = bounds.Height;
            cp.Style = Native.WS_POPUP;
            cp.ExStyle = Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
            if (clickThrough) cp.ExStyle |= Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE;
            CreateHandle(cp);

            var bih = new Native.BITMAPINFOHEADER();
            bih.biSize = (uint)sizeof(Native.BITMAPINFOHEADER);
            bih.biWidth = bounds.Width;
            bih.biHeight = -bounds.Height;
            bih.biPlanes = 1;
            bih.biBitCount = 32;
            memDc = Native.CreateCompatibleDC(IntPtr.Zero);
            IntPtr bits;
            dib = Native.CreateDIBSection(memDc, ref bih, 0, out bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) throw new Win32Exception();
            oldBmp = Native.SelectObject(memDc, dib);
            Canvas = new Bitmap(bounds.Width, bounds.Height, bounds.Width * 4, PixelFormat.Format32bppPArgb, bits);
            G = Graphics.FromImage(Canvas);
            G.PixelOffsetMode = PixelOffsetMode.Half;
        }

        public Rectangle LocalBounds { get { return new Rectangle(0, 0, Bounds.Width, Bounds.Height); } }

        /// <summary>Envoie au compositeur la zone <paramref name="dirty"/> (coordonnées locales).</summary>
        public void Present(Rectangle dirty)
        {
            dirty.Intersect(LocalBounds);
            if (presented && dirty.IsEmpty) return;
            G.Flush(FlushIntention.Sync);
            Native.GdiFlush();

            var dst = new Native.POINT { X = Bounds.X, Y = Bounds.Y };
            var size = new Native.SIZE { cx = Bounds.Width, cy = Bounds.Height };
            var src = new Native.POINT();
            var blend = new Native.BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = 1 };
            var rc = new Native.RECT { Left = dirty.Left, Top = dirty.Top, Right = dirty.Right, Bottom = dirty.Bottom };
            var info = new Native.UPDATELAYEREDWINDOWINFO();
            info.cbSize = (uint)sizeof(Native.UPDATELAYEREDWINDOWINFO);
            info.pptDst = &dst;
            info.psize = &size;
            info.hdcSrc = memDc;
            info.pptSrc = &src;
            info.pblend = &blend;
            info.dwFlags = Native.ULW_ALPHA;
            info.prcDirty = presented ? &rc : null;
            Native.UpdateLayeredWindowIndirect(Handle, &info);
            presented = true;
        }

        public void Show(bool activate)
        {
            if (!presented) Present(LocalBounds);
            Native.ShowWindow(Handle, activate ? Native.SW_SHOW : Native.SW_SHOWNOACTIVATE);
            BringToTop();
            Visible = true;
        }

        public void Hide()
        {
            Native.ShowWindow(Handle, Native.SW_HIDE);
            Visible = false;
        }

        public void BringToTop()
        {
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public virtual void Dispose()
        {
            if (disposed) return;
            disposed = true;
            G.Dispose();
            Canvas.Dispose();
            Native.SelectObject(memDc, oldBmp);
            Native.DeleteObject(dib);
            Native.DeleteDC(memDc);
            DestroyHandle();
        }
    }
}
