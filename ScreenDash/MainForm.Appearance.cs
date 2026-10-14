using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ScreenDash;

public sealed partial class MainForm
{
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    private enum AppGlyph { None, Record, Stop, Play, Pause, Shield, Bookmark, Archive, Settings, Tools, Folder, Activity }
    private static Color Mix(Color a, Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb((int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));
    }
    private static void ConfigureDarkTitleBar(Form form)
    {
        form.HandleCreated += (_, _) =>
        {
            try { int dark = 1, corners = 2; DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)); DwmSetWindowAttribute(form.Handle, 33, ref corners, sizeof(int)); }
            catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        };
    }
    private static void DrawGlyph(Graphics g, AppGlyph glyph, RectangleF bounds, Color color)
    {
        if (glyph == AppGlyph.None || bounds.Width <= 0 || bounds.Height <= 0) return;
        var state = g.Save(); g.TranslateTransform(bounds.X, bounds.Y); g.ScaleTransform(bounds.Width / 24, bounds.Height / 24);
        using var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(color);
        switch (glyph)
        {
            case AppGlyph.Record: g.DrawEllipse(pen, 3, 3, 18, 18); g.FillEllipse(fill, 8, 8, 8, 8); break;
            case AppGlyph.Stop: using (var p = RoundPath(new RectangleF(5, 5, 14, 14), 3)) g.FillPath(fill, p); break;
            case AppGlyph.Play: g.DrawPolygon(pen, new PointF[] { new(7, 4), new(20, 12), new(7, 20) }); break;
            case AppGlyph.Pause: g.DrawLine(pen, 8, 5, 8, 19); g.DrawLine(pen, 16, 5, 16, 19); break;
            case AppGlyph.Shield: g.DrawPolygon(pen, new PointF[] { new(12, 2), new(21, 6), new(20, 15), new(12, 22), new(4, 15), new(3, 6) }); g.DrawLines(pen, new PointF[] { new(7, 12), new(10, 15), new(17, 8) }); break;
            case AppGlyph.Bookmark: g.DrawPolygon(pen, new PointF[] { new(6, 3), new(18, 3), new(18, 21), new(12, 17), new(6, 21) }); break;
            case AppGlyph.Archive: using (var p = RoundPath(new RectangleF(3, 5, 18, 15), 3)) g.DrawPath(pen, p); g.DrawLine(pen, 3, 9, 21, 9); g.DrawLine(pen, 9, 13, 15, 13); break;
            case AppGlyph.Settings: g.DrawLine(pen, 5, 3, 5, 21); g.DrawLine(pen, 12, 3, 12, 21); g.DrawLine(pen, 19, 3, 19, 21); g.FillEllipse(fill, 2, 6, 6, 6); g.FillEllipse(fill, 9, 13, 6, 6); g.FillEllipse(fill, 16, 5, 6, 6); break;
            case AppGlyph.Tools: g.DrawPolygon(pen, new PointF[] { new(12, 2), new(15, 9), new(22, 12), new(15, 15), new(12, 22), new(9, 15), new(2, 12), new(9, 9) }); break;
            case AppGlyph.Folder: g.DrawPolygon(pen, new PointF[] { new(3, 6), new(10, 6), new(12, 9), new(21, 9), new(21, 20), new(3, 20) }); break;
            case AppGlyph.Activity: g.DrawLines(pen, new PointF[] { new(2, 12), new(6, 12), new(9, 4), new(14, 20), new(18, 12), new(22, 12) }); break;
        }
        g.Restore(state);
    }
    private sealed class SidebarSurface : Panel
    {
        public SidebarSurface() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Width < 1 || Height < 1) return;
            using var fill = new LinearGradientBrush(ClientRectangle, Sidebar, Color.FromArgb(17, 21, 34), 80f); e.Graphics.FillRectangle(fill, ClientRectangle);
            using var edge = new Pen(Mix(Sidebar, Line, .55)); e.Graphics.DrawLine(edge, Width - 1, 0, Width - 1, Height);
        }
    }
    private sealed class RoundedPanel : Panel
    {
        public bool Hero { get; set; }
        public RoundedPanel() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(SurroundingColor(this)); if (Width < 2 || Height < 2) return;
            float scale = DeviceDpi / 96f; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundPath(new RectangleF(.5f, .5f, Width - 1.5f, Height - 1.5f), (Hero ? 19 : 15) * scale);
            using var fill = new LinearGradientBrush(ClientRectangle, Hero ? Color.FromArgb(27, 40, 48) : BackColor, Mix(BackColor, Violet, Hero ? .055 : .014), 30f);
            e.Graphics.FillPath(fill, path);
            using var border = new Pen(Hero ? Color.FromArgb(64, 98, 105) : Mix(Line, BackColor, .20)); e.Graphics.DrawPath(border, path);
            if (Hero)
            {
                using var accent = new Pen(Mix(Cyan, Card, .45), 2 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                e.Graphics.DrawLine(accent, 26 * scale, 1.5f * scale, Math.Min(Width - 28 * scale, 122 * scale), 1.5f * scale);
            }
        }
    }
    private sealed class StatusPill : Label
    {
        public StatusPill() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(SurroundingColor(this)); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = DeviceDpi / 96f; using var path = RoundPath(new RectangleF(.5f, .5f, Width - 1.5f, Height - 1.5f), Height / 2f);
            using var fill = new SolidBrush(Mix(Card, ForeColor, .085)); using var border = new Pen(Mix(Line, ForeColor, .2));
            e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
            using var dot = new SolidBrush(ForeColor); e.Graphics.FillEllipse(dot, 12 * scale, Height / 2f - 3 * scale, 6 * scale, 6 * scale);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle((int)(25 * scale), 0, Math.Max(0, Width - (int)(30 * scale)), Height), ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
    private sealed class ThemeButton : Button
    {
        private bool _hover, _pressed;
        private float _hoverAmount, _from, _target;
        private long _started;
        private System.Windows.Forms.Timer? _animation;
        public bool LeftAligned { get; set; }
        public bool AccentBorder { get; set; }
        public bool Active { get; set; }
        public bool Primary { get; set; }
        public AppGlyph IconKind { get; set; }
        public bool AnimationRunning => _animation?.Enabled == true;
        public ThemeButton() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        private bool CanAnimate => Visible && Enabled && FindForm() is { Visible: true, WindowState: not FormWindowState.Minimized } owner && SystemInformation.IsMenuAnimationEnabled && !SystemInformation.HighContrast && (owner is not MainForm main || main._motionEnabled);
        private void AnimateHover()
        {
            _target = _hover ? 1 : 0;
            if (!CanAnimate) { _animation?.Stop(); _hoverAmount = _target; Invalidate(); return; }
            _from = _hoverAmount; _started = Stopwatch.GetTimestamp();
            if (_animation is null) { _animation = new() { Interval = 32 }; _animation.Tick += (_, _) => AdvanceHover(); }
            _animation.Start(); Invalidate();
        }
        private void AdvanceHover()
        {
            if (!CanAnimate) { _animation?.Stop(); _hoverAmount = _hover ? 1 : 0; return; }
            double t = Math.Clamp(Stopwatch.GetElapsedTime(_started).TotalMilliseconds / 160, 0, 1);
            _hoverAmount = (float)(_from + (_target - _from) * (1 - Math.Pow(1 - t, 3))); Invalidate();
            if (t >= 1) _animation?.Stop();
        }
        public void FinishAnimation() { _animation?.Stop(); _hoverAmount = _hover && Enabled ? 1 : 0; Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; AnimateHover(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; AnimateHover(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { FinishAnimation(); base.OnEnabledChanged(e); }
        protected override void OnVisibleChanged(EventArgs e) { if (!Visible) { _hover = _pressed = false; FinishAnimation(); } base.OnVisibleChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(SurroundingColor(this)); if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; float scale = DeviceDpi / 96f;
            var color = !Enabled ? Mix(Card, Input, .25) : _pressed ? FlatAppearance.MouseDownBackColor : Mix(BackColor, FlatAppearance.MouseOverBackColor, _hoverAmount);
            using var path = RoundPath(new RectangleF(.5f, .5f, Width - 1.5f, Height - 1.5f), 10 * scale);
            using var fill = new LinearGradientBrush(ClientRectangle, color, Primary && Enabled ? Mix(color, Color.White, .07) : color, 12f); e.Graphics.FillPath(fill, path);
            using var border = new Pen(AccentBorder ? Mix(Line, Cyan, Active ? .46 : .32) : Mix(Line, color, .55)); if (!Primary || !Enabled) e.Graphics.DrawPath(border, path);
            if (Active && LeftAligned) { using var accent = new Pen(Cyan, 2.4f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round }; e.Graphics.DrawLine(accent, 2 * scale, Height * .33f, 2 * scale, Height * .67f); }
            Color textColor = Enabled ? ForeColor : Color.FromArgb(128, 142, 164);
            var area = Rectangle.Inflate(ClientRectangle, -(int)(9 * scale), -(int)(2 * scale));
            int iconSize = (int)(18 * scale), gap = (int)(8 * scale);
            if (IconKind != AppGlyph.None)
            {
                int textWidth = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                int contentWidth = Math.Min(area.Width, textWidth + iconSize + gap);
                int x = LeftAligned ? area.Left + (int)(4 * scale) : area.Left + Math.Max(0, (area.Width - contentWidth) / 2);
                DrawGlyph(e.Graphics, IconKind, new RectangleF(x, (Height - iconSize) / 2f, iconSize, iconSize), textColor);
                area.X = x + iconSize + gap; area.Width = Math.Max(0, ClientRectangle.Right - (int)(9 * scale) - area.X);
            }
            else if (LeftAligned) { area.X += (int)(7 * scale); area.Width -= (int)(7 * scale); }
            TextRenderer.DrawText(e.Graphics, Text, Font, area, textColor, (LeftAligned || IconKind != AppGlyph.None ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues) { using var focus = new Pen(Mix(textColor, color, .28), 1.5f); using var focusPath = RoundPath(new RectangleF(3 * scale, 3 * scale, Width - 6 * scale, Height - 6 * scale), 7 * scale); e.Graphics.DrawPath(focus, focusPath); }
        }
        protected override void Dispose(bool disposing) { if (disposing) _animation?.Dispose(); base.Dispose(disposing); }
    }
    private sealed class BrandMark : Control
    {
        public BrandMark() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(SurroundingColor(this)); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = RoundPath(new RectangleF(.5f, .5f, Width - 1.5f, Height - 1.5f), Width * .27f);
            using var fill = new LinearGradientBrush(ClientRectangle, Color.FromArgb(33, 72, 78), Color.FromArgb(38, 37, 64), 45f);
            using var border = new Pen(Color.FromArgb(73, 111, 125)); e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
            float s = Width / 44f; var state = e.Graphics.Save(); e.Graphics.ScaleTransform(s, s);
            using var stroke = new Pen(Cyan, 1.8f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var display = RoundPath(new RectangleF(10, 11, 24, 18), 3); e.Graphics.DrawPath(stroke, display); e.Graphics.DrawLine(stroke, 17, 34, 27, 34); e.Graphics.DrawLine(stroke, 22, 29, 22, 34);
            using var dot = new SolidBrush(Cyan); e.Graphics.FillEllipse(dot, 18, 16, 8, 8); e.Graphics.Restore(state);
        }
    }
}
