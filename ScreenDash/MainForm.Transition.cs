using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ScreenDash;

public sealed partial class MainForm
{
    private PageTransitionOverlay? _pageTransition;
    private TransitionInputFilter? _transitionInputFilter;
    private int _pageTransitionCaptureCount;

    private void BeginVisualTransition(Control host)
    {
        CompletePageTransition();
        if (!CanRunPageMotion() || !host.Visible || !host.IsHandleCreated || host.IsDisposed) return;
        Size size = host.ClientSize;
        // At most one 16 MB image lives for 240 ms. Very large windows navigate immediately.
        if (size.Width <= 0 || size.Height <= 0 || (long)size.Width * size.Height > 4_000_000) return;

        Bitmap? snapshot = null;
        try
        {
            host.PerformLayout();
            snapshot = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            // Draw only ScreenDash controls; this never reads the desktop, game or recording frames.
            host.DrawToBitmap(snapshot, new Rectangle(Point.Empty, size));
            var overlay = new PageTransitionOverlay(snapshot, Canvas, 12 * DeviceDpi / 96d)
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                TabStop = false,
                AccessibleRole = AccessibleRole.None
            };
            snapshot = null; // The overlay now owns the sole cached image.
            _pageTransition = overlay;
            host.Controls.Add(overlay);
            overlay.BringToFront();
            _pageTransitionCaptureCount++;
            _motionStarted = Stopwatch.GetTimestamp();
            _pageMotionTimer.Start();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ExternalException or OutOfMemoryException)
        {
            // Unsupported native controls or unavailable drawing resources must not delay navigation.
            CompletePageTransition();
        }
        finally { snapshot?.Dispose(); }
    }

    // The synthetic preview uses a fixed phase without involving the desktop or the recorder.
    private void SetPageTransitionPreviewProgress(double progress)
    {
        if (_pageTransition is null || !double.IsFinite(progress)) return;
        _pageMotionTimer.Stop();
        if (progress >= 1) CompletePageTransition();
        else _pageTransition.SetProgress(Math.Clamp(progress, 0, 1));
    }

    private sealed class TransitionInputFilter(MainForm owner) : IMessageFilter
    {
        public bool PreFilterMessage(ref Message message)
        {
            if (owner._pageTransition is null) return false;
            int type = message.Msg;
            bool keyboard = type is >= 0x100 and <= 0x109 or 0x312; // Include registered capture hotkeys.
            bool mouse = type is 0x201 or 0x202 or 0x203 or 0x204 or 0x205 or 0x206 or
                0x207 or 0x208 or 0x209 or 0x20A or 0x20B or 0x20C or 0x20D or 0x20E or 0x246;
            if ((keyboard || mouse) && Control.FromChildHandle(message.HWnd)?.FindForm() == owner)
                owner.CompletePageTransition();
            return false; // The real control receives the same input and acts immediately.
        }
    }

    private sealed class PageTransitionOverlay : Control
    {
        private readonly Bitmap _snapshot;
        private readonly ImageAttributes _attributes = new();
        private readonly ColorMatrix _matrix = new();
        private readonly Color _background;
        private readonly double _distance;
        private float _opacity;
        private int _offset;

        public PageTransitionOverlay(Bitmap snapshot, Color background, double distance)
        {
            _snapshot = snapshot;
            _background = background;
            _distance = distance;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            SetStyle(ControlStyles.Selectable, false);
            SetProgress(0);
        }

        public void SetProgress(double progress)
        {
            double eased = 1 - Math.Pow(1 - Math.Clamp(progress, 0, 1), 3);
            _opacity = (float)eased;
            _offset = (int)Math.Round(_distance * (1 - eased));
            Invalidate(false);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(_background);
            if (_opacity <= 0) return;
            _matrix.Matrix33 = _opacity;
            _attributes.SetColorMatrix(_matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            e.Graphics.DrawImage(_snapshot, new Rectangle(0, _offset, Width, Height),
                0, 0, _snapshot.Width, _snapshot.Height, GraphicsUnit.Pixel, _attributes);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84) { message.Result = new IntPtr(-1); return; } // HTTRANSPARENT: hit-test the native control underneath.
            base.WndProc(ref message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _snapshot.Dispose(); _attributes.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
