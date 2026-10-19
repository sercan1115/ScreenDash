using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ScreenDash;

public sealed partial class MainForm
{
    // The silver landscape is drawn only on childless background strips. Recording controls
    // and the video pipeline never participate in this timer; ticks allocate no image buffers.
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
        private RibbonInk? _ink;
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
            Size canvas = _sculpture ? ClientSize : CanvasSize;
            if (canvas.Width < 2 || canvas.Height < 2) return;
            float scale = DeviceDpi / 96f;
            bool vertical = _sculpture && Width < Height * 1.8f;
            if (_ink is null || !_ink.Matches(canvas, vertical, !_sculpture, scale))
            {
                _ink?.Dispose();
                _ink = new RibbonInk(canvas, vertical, !_sculpture, scale);
            }
            var state = e.Graphics.Save();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (_sculpture) DrawSculpture(e.Graphics, ClientSize, Phase, scale, _ink);
            else
            {
                e.Graphics.TranslateTransform(-CanvasOrigin.X, -CanvasOrigin.Y);
                DrawCanvas(e.Graphics, CanvasSize, Phase, scale, _ink);
            }
            e.Graphics.Restore(state);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _ink?.Dispose(); _ink = null; }
            base.Dispose(disposing);
        }

        private static void DrawSculpture(Graphics graphics, Size size, double phase, float scale, RibbonInk ink)
        {
            float width = size.Width, height = size.Height;
            if (width < 2 || height < 2) return;
            if (width >= height * 1.8f)
            {
                DrawHorizon(graphics, size, phase, scale, ink);
                return;
            }
            float drift = (float)Math.Sin(phase) * .16f;
            float fold = (float)Math.Cos(phase) * .14f;
            for (int layer = 0; layer < 3; layer++)
            {
                float offset = layer * .17f;
                var a = new PointF(width * (.32f + offset + drift), -height * .10f);
                var b = new PointF(width * (1.27f + offset + fold), height * .34f);
                var c = new PointF(width * (-.58f + offset - drift), height * .72f);
                var d = new PointF(width * (.72f + offset - fold), height * 1.12f);
                DrawRibbon(graphics, a, b, c, d, new PointF(width * .34f, 0), phase, layer, ink);
            }
            DrawShimmer(graphics, size, phase, true, scale, ink);
        }

        private static void DrawHorizon(Graphics graphics, Size size, double phase, float scale, RibbonInk ink)
        {
            float width = size.Width, height = size.Height;
            float drift = (float)Math.Sin(phase) * .23f;
            float fold = (float)Math.Cos(phase) * .18f;
            for (int layer = 0; layer < 3; layer++)
            {
                float y = .22f + layer * .23f;
                var a = new PointF(-width * .12f, height * (y + drift));
                var b = new PointF(width * .24f, height * (1.19f + fold + layer * .05f));
                var c = new PointF(width * .66f, height * (-.84f + drift + layer * .09f));
                var d = new PointF(width * 1.10f, height * (.48f + layer * .19f - fold));
                DrawRibbon(graphics, a, b, c, d, new PointF(0, height * .34f), phase, layer, ink);
            }
            DrawShimmer(graphics, size, phase, false, scale, ink);
        }

        private static void DrawCanvas(Graphics graphics, Size size, double phase, float scale, RibbonInk ink)
        {
            float width = size.Width, height = size.Height;
            if (width < 2 || height < 2) return;
            float drift = (float)Math.Sin(phase) * .10f;
            float fold = (float)Math.Cos(phase) * .08f;
            for (int layer = 0; layer < 3; layer++)
            {
                float y = .04f + layer * .35f;
                var a = new PointF(-width * .13f, height * (y + drift));
                var b = new PointF(width * (.25f + fold), height * (y - .32f));
                var c = new PointF(width * (.70f - fold), height * (y + .40f - drift));
                var d = new PointF(width * 1.12f, height * (y + .21f));
                DrawRibbon(graphics, a, b, c, d, new PointF(0, height * .13f), phase, layer, ink);
            }
        }

        private static void DrawRibbon(Graphics graphics, PointF a, PointF b, PointF c, PointF d,
            PointF thickness, double phase, int layer, RibbonInk ink)
        {
            static PointF Add(PointF point, PointF offset) => new(point.X + offset.X, point.Y + offset.Y);
            var body = ink.Body;
            body.Reset();
            body.AddBezier(a, b, c, d);
            body.AddLine(d, Add(d, thickness));
            body.AddBezier(Add(d, thickness), Add(c, thickness), Add(b, thickness), Add(a, thickness));
            body.CloseFigure();
            graphics.FillPath(ink.Fills[layer], body);
            double light = .5 + .5 * Math.Sin(phase + layer * 1.7);
            ink.Edge.Color = Color.FromArgb(ink.Subdued ? 53 : 94 + (int)(light * 45), 224, 232, 244);
            graphics.DrawBezier(ink.Glow, a, b, c, d);
            graphics.DrawBezier(ink.Edge, a, b, c, d);
            for (int line = 1; line <= 3; line++)
            {
                var offset = new PointF(thickness.X * line / 4, thickness.Y * line / 4);
                graphics.DrawBezier(ink.Contour, Add(a, offset), Add(b, offset), Add(c, offset), Add(d, offset));
            }
        }

        private static void DrawShimmer(Graphics graphics, Size size, double phase, bool vertical, float scale, RibbonInk ink)
        {
            // Four deterministic lights drift with the same 36-second cycle, without random flicker.
            for (int light = 0; light < 4; light++)
            {
                double angle = phase + light * 1.67;
                float x = size.Width * (.20f + light * .17f + (float)Math.Sin(angle) * .055f);
                float y = size.Height * ((vertical ? .15f : .40f) + light * (vertical ? .20f : .07f) + (float)Math.Cos(angle) * .09f);
                int opacity = 40 + (int)((.5 + .5 * Math.Sin(angle)) * 80);
                ink.Aura.Color = Color.FromArgb(opacity / 9, 225, 235, 249);
                ink.Light.Color = Color.FromArgb(opacity, 225, 235, 249);
                graphics.FillEllipse(ink.Aura, x - 4 * scale, y - 4 * scale, 8 * scale, 8 * scale);
                graphics.FillEllipse(ink.Light, x - .9f * scale, y - .9f * scale, 1.8f * scale, 1.8f * scale);
            }
        }

        private sealed class RibbonInk : IDisposable
        {
            private readonly Size _size;
            private readonly bool _vertical;
            private readonly float _scale;
            public bool Subdued { get; }
            public LinearGradientBrush[] Fills { get; } = new LinearGradientBrush[3];
            public GraphicsPath Body { get; } = new();
            public Pen Glow { get; }
            public Pen Edge { get; }
            public Pen Contour { get; }
            public SolidBrush Aura { get; } = new(Color.Transparent);
            public SolidBrush Light { get; } = new(Color.Transparent);

            public RibbonInk(Size size, bool vertical, bool subdued, float scale)
            {
                _size = size; _vertical = vertical; Subdued = subdued; _scale = scale;
                Glow = new Pen(Color.FromArgb(subdued ? 9 : 15, 218, 227, 240), 5 * scale);
                Edge = new Pen(Color.FromArgb(100, 224, 232, 244), .8f * scale);
                Contour = new Pen(Color.FromArgb(subdued ? 20 : 32, 185, 201, 225), .6f * scale);
                for (int layer = 0; layer < Fills.Length; layer++)
                {
                    int brightness = subdued ? 24 : 52 + layer * 12;
                    var fill = new LinearGradientBrush(new RectangleF(0, 0, size.Width, size.Height), Color.Transparent,
                        Color.Transparent, vertical ? LinearGradientMode.Horizontal : LinearGradientMode.Vertical);
                    fill.InterpolationColors = new ColorBlend
                    {
                        Positions = [0, .28f, .53f, .72f, 1],
                        Colors = [Color.FromArgb(0, 220, 226, 235), Color.FromArgb(brightness / 4, 190, 201, 216),
                            Color.FromArgb(brightness, 215, 223, 234), Color.FromArgb(brightness / 3, 151, 166, 187),
                            Color.FromArgb(0, 210, 220, 235)]
                    };
                    Fills[layer] = fill;
                }
            }

            public bool Matches(Size size, bool vertical, bool subdued, float scale) =>
                _size == size && _vertical == vertical && Subdued == subdued && _scale == scale;

            public void Dispose()
            {
                foreach (var fill in Fills) fill.Dispose();
                Body.Dispose(); Glow.Dispose(); Edge.Dispose(); Contour.Dispose(); Aura.Dispose(); Light.Dispose();
            }
        }
    }
}
