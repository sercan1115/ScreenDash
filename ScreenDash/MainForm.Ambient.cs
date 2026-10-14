using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ScreenDash;

public sealed partial class MainForm
{
    // Only small, childless decorations repaint. The recording controls and video pipeline
    // never participate in this timer, and no image buffers are allocated on a tick.
    private readonly System.Windows.Forms.Timer _ambientTimer = new() { Interval = 40 };
    private readonly List<AmbientPanel> _ambientSurfaces = [];
    private bool _ambientBackgroundEnabled = true, _ambientInitialized, _ambientDisposed;
    private int _ambientFrameCount, _ambientPaintCount;
    private double _ambientPhase;
    private long _ambientLastTick;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AmbientSystemParametersInfo(uint action, uint parameter, out int enabled, uint flags);

    private static bool SystemMotionAllowed()
    {
        if (SystemInformation.HighContrast || !SystemInformation.IsMenuAnimationEnabled) return false;
        // Windows Settings > Accessibility > Visual effects controls client-area animation.
        // A failed query falls back to the existing managed Windows animation preference.
        return !AmbientSystemParametersInfo(0x1042, 0, out int enabled, 0) || enabled != 0;
    }

    private void InitializeAmbient()
    {
        if (_ambientInitialized || _ambientDisposed) return;
        _ambientInitialized = true;
        _ambientTimer.Tick += AmbientTick;
        SystemEvents.UserPreferenceChanged += AmbientPreferencesChanged;
        RefreshMotionPolicy();
    }

    private void ConfigureAmbientBackground(bool enabled)
    {
        if (_ambientDisposed) return;
        _ambientBackgroundEnabled = enabled;
        RefreshAmbientMotion();
    }

    private void RefreshMotionPolicy()
    {
        if (_ambientDisposed || _motionDisposed) return;
        _motionEnabled = _motionRequested && SystemMotionAllowed();
        if (!_motionEnabled)
        {
            CompletePageTransition();
            FinishButtonMotion(this);
        }
        foreach (var surface in _ambientSurfaces) surface.HighContrast = SystemInformation.HighContrast;
        RefreshAmbientMotion();
    }

    private void RefreshAmbientMotion()
    {
        if (_ambientDisposed) return;
        bool canRun = _ambientInitialized && _ambientBackgroundEnabled && _motionEnabled &&
            Visible && WindowState != FormWindowState.Minimized && !IsDisposed && !Disposing && SystemMotionAllowed();
        if (canRun)
        {
            if (!_ambientTimer.Enabled)
            {
                _ambientLastTick = Stopwatch.GetTimestamp();
                _ambientTimer.Start();
            }
        }
        else
        {
            _ambientTimer.Stop();
            _ambientLastTick = 0;
        }
    }

    private void AmbientTick(object? sender, EventArgs e)
    {
        if (_ambientDisposed) return;
        RefreshAmbientMotion();
        if (!_ambientTimer.Enabled) return;
        long now = Stopwatch.GetTimestamp();
        double elapsed = _ambientLastTick == 0 ? 0 : Math.Clamp(Stopwatch.GetElapsedTime(_ambientLastTick, now).TotalSeconds, 0, .25);
        _ambientLastTick = now;
        _ambientPhase = (_ambientPhase + elapsed * Math.Tau / 36) % Math.Tau;
        _ambientFrameCount++;
        ApplyAmbientPhase();
    }

    private void SetAmbientPreviewPhase(double phase)
    {
        if (_ambientDisposed || !double.IsFinite(phase)) return;
        _ambientPhase = (phase % Math.Tau + Math.Tau) % Math.Tau;
        _ambientLastTick = Stopwatch.GetTimestamp();
        ApplyAmbientPhase();
    }

    private void ApplyAmbientPhase()
    {
        foreach (var surface in _ambientSurfaces)
        {
            if (surface.IsDisposed) continue;
            surface.Phase = _ambientPhase;
            if (surface.Visible && surface.Width > 0 && surface.Height > 0) surface.Invalidate(false);
        }
    }

    private void AmbientPreferencesChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_ambientDisposed || IsDisposed || Disposing || !IsHandleCreated) return;
        try
        {
            BeginInvoke((Action)(() =>
            {
                if (_ambientDisposed || IsDisposed || Disposing) return;
                RefreshMotionPolicy();
                ApplyAmbientPhase();
            }));
        }
        catch (InvalidOperationException) { } // A queued system event can race window disposal.
    }

    private void DisposeAmbient()
    {
        if (_ambientDisposed) return;
        _ambientDisposed = true;
        _ambientTimer.Stop();
        SystemEvents.UserPreferenceChanged -= AmbientPreferencesChanged;
        _ambientTimer.Tick -= AmbientTick;
        _ambientTimer.Dispose();
    }

    private AmbientPanel AmbientDecoration(Color background, bool sculpture)
    {
        var surface = new AmbientPanel(this, sculpture) { BackColor = background, Margin = Padding.Empty, TabStop = false, AccessibleRole = AccessibleRole.None };
        _ambientSurfaces.Add(surface);
        return surface;
    }

    private sealed class AmbientPanel : Panel
    {
        private readonly MainForm _owner;
        private readonly bool _sculpture;
        public double Phase { get; set; }
        public Point CanvasOrigin { get; set; }
        public Size CanvasSize { get; set; }
        public bool HighContrast { get; set; }

        public AmbientPanel(MainForm owner, bool sculpture)
        {
            _owner = owner;
            _sculpture = sculpture;
            SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            _owner._ambientPaintCount++;
            if (Width < 2 || Height < 2 || HighContrast) return;
            var state = e.Graphics.Save();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float scale = DeviceDpi / 96f;
            using var shadow = new Pen(Color.FromArgb(_sculpture ? 20 : 14, 210, 218, 228), 5 * scale);
            using var edge = new Pen(Color.FromArgb(_sculpture ? 78 : 57, 210, 218, 228), .7f * scale);
            using var glint = new Pen(Color.FromArgb(_sculpture ? 142 : 82, 235, 240, 245), .7f * scale);
            if (_sculpture) DrawSculpture(e.Graphics, ClientSize, Phase, shadow, edge, glint);
            else
            {
                e.Graphics.TranslateTransform(-CanvasOrigin.X, -CanvasOrigin.Y);
                DrawCanvas(e.Graphics, CanvasSize, Phase, shadow, edge, glint);
            }
            e.Graphics.Restore(state);
        }

        private static void DrawSculpture(Graphics graphics, Size size, double phase, Pen shadow, Pen edge, Pen glint)
        {
            float width = size.Width, height = size.Height;
            if (width < 2 || height < 2) return;
            float drift = (float)Math.Sin(phase) * .08f;
            float breathe = (float)Math.Cos(phase) * .06f;
            for (int i = 0; i < 9; i++)
            {
                float t = i / 8f;
                var a = new PointF(-width * .12f, height * (.16f + t * .11f));
                var b = new PointF(width * (.80f + drift), height * (-.17f + t * .17f));
                var c = new PointF(width * (1.23f + breathe), height * (.52f + t * .17f));
                var d = new PointF(width * (.22f + t * .13f + drift), height * 1.12f);
                graphics.DrawBezier(shadow, a, b, c, d);
                graphics.DrawBezier(i == 3 || i == 4 ? glint : edge, a, b, c, d);
            }
        }

        private static void DrawCanvas(Graphics graphics, Size size, double phase, Pen shadow, Pen edge, Pen glint)
        {
            float width = size.Width, height = size.Height;
            if (width < 2 || height < 2) return;
            float drift = (float)Math.Sin(phase) * .055f;
            float breathe = (float)Math.Cos(phase) * .045f;
            for (int i = 0; i < 9; i++)
            {
                float t = i / 8f;
                var a = new PointF(-width * .08f, height * (.10f + t * .17f + drift));
                var b = new PointF(width * (.21f + breathe), height * (.05f + t * .08f));
                var c = new PointF(width * (.71f - breathe), height * (.66f + t * .10f + drift));
                var d = new PointF(width * 1.08f, height * (.56f + t * .13f));
                graphics.DrawBezier(shadow, a, b, c, d);
                graphics.DrawBezier(i == 4 ? glint : edge, a, b, c, d);
            }
        }
    }
}
