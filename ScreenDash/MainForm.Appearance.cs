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
        form.HandleCreated += (_, _) => ApplyTitleBar(form);
    }
    private static void ApplyTitleBar(Form form)
    {
        try
        {
            int dark = form.BackColor.GetBrightness() < .45f ? 1 : 0, corners = 2;
            int caption = form.BackColor.R | form.BackColor.G << 8 | form.BackColor.B << 16;
            Color text = dark == 1 ? Color.White : Ink;
            int captionText = text.R | text.G << 8 | text.B << 16;
            DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 33, ref corners, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 36, ref captionText, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
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
            e.Graphics.Clear(Sidebar);
            using var edge = new Pen(Mix(Sidebar, Color.White, .12)); e.Graphics.DrawLine(edge, Width - 1, 0, Width - 1, Height);
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
            using var path = RoundPath(new RectangleF(.5f, .5f, Width - 1.5f, Height - 1.5f), 3 * scale);
            using var fill = new SolidBrush(BackColor);
            e.Graphics.FillPath(fill, path);
            using var border = new Pen(Hero ? Mix(Line, Ink, .18) : Line); e.Graphics.DrawPath(border, path);
            if (Hero)
            {
                using var accent = new Pen(Cyan, 2 * scale);
                e.Graphics.DrawLine(accent, 22 * scale, 1 * scale, Math.Min(Width - 24 * scale, 84 * scale), 1 * scale);
            }
        }
    }
    private sealed class StatusPill : Label
    {
        public StatusPill() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(SurroundingColor(this)); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (Width < 2 || Height < 2) return;
            float scale = DeviceDpi / 96f; using var path = RoundPath(new RectangleF(.5f, .5f, Width - 1.5f, Height - 1.5f), 3 * scale);
            var background = SurroundingColor(this);
            using var fill = new SolidBrush(Mix(background, ForeColor, .07)); using var border = new Pen(Mix(background, ForeColor, .25));
            e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
            using var dot = new SolidBrush(ForeColor); e.Graphics.FillEllipse(dot, 12 * scale, Height / 2f - 3 * scale, 6 * scale, 6 * scale);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle((int)(25 * scale), 0, Math.Max(0, Width - (int)(30 * scale)), Height), ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
    private sealed class ThemeButton : Button
    {
        private bool _hover, _pressed, _mouseDown, _keyboardDown, _active;
        private float _hoverAmount, _pressAmount, _focusAmount, _activeAmount;
        private float _hoverFrom, _pressFrom, _focusFrom, _activeFrom;
        private float _hoverTarget, _pressTarget, _focusTarget, _activeTarget;
        private double _pressDuration = 160;
        private long _started;
        private System.Windows.Forms.Timer? _animation;
        public bool LeftAligned { get; set; }
        public bool AccentBorder { get; set; }
        public bool Active
        {
            get => _active;
            set { if (_active == value) return; _active = value; AnimateInteraction(); }
        }
        public bool Primary { get; set; }
        public AppGlyph IconKind { get; set; }
        public string? IndexLabel { get; set; }
        public bool AnimationRunning => _animation?.Enabled == true;
        public ThemeButton() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        private bool CanAnimate => Visible && Enabled && FindForm() is { Visible: true, WindowState: not FormWindowState.Minimized } owner && SystemInformation.IsMenuAnimationEnabled && !SystemInformation.HighContrast && (owner is not MainForm main || main._motionEnabled);
        private bool InteractionVisible => Visible && Enabled && FindForm() is { Visible: true, WindowState: not FormWindowState.Minimized };
        private void AnimateInteraction()
        {
            // Rebase from the displayed position, including the time since the last tick.
            // A quick reversal therefore never jumps back to an old endpoint.
            if (_animation?.Enabled == true) SampleInteraction(Stopwatch.GetElapsedTime(_started).TotalMilliseconds);
            _hoverTarget = _hover && Enabled ? 1 : 0;
            _pressTarget = _pressed && Enabled ? 1 : 0;
            _focusTarget = Focused && ShowFocusCues && Enabled ? 1 : 0;
            _activeTarget = Active ? 1 : 0;
            if (!CanAnimate)
            {
                _animation?.Stop();
                _hoverAmount = _hoverTarget; _pressAmount = _pressTarget; _focusAmount = _focusTarget; _activeAmount = _activeTarget;
                Invalidate(); return;
            }
            _hoverFrom = _hoverAmount; _pressFrom = _pressAmount; _focusFrom = _focusAmount; _activeFrom = _activeAmount;
            _pressDuration = _pressed ? 90 : 160; _started = Stopwatch.GetTimestamp();
            if (InteractionSettled()) { _animation?.Stop(); return; }
            if (_animation is null) { _animation = new() { Interval = 15 }; _animation.Tick += (_, _) => AdvanceInteraction(); }
            _animation.Start(); Invalidate();
        }
        private static float EaseInteraction(float from, float target, double elapsedMilliseconds, double duration)
        {
            double t = Math.Clamp(elapsedMilliseconds / duration, 0, 1);
            return t >= 1 ? target : (float)(from + (target - from) * (1 - Math.Pow(1 - t, 3)));
        }
        private void SampleInteraction(double elapsedMilliseconds)
        {
            _hoverAmount = EaseInteraction(_hoverFrom, _hoverTarget, elapsedMilliseconds, 220);
            _pressAmount = EaseInteraction(_pressFrom, _pressTarget, elapsedMilliseconds, _pressDuration);
            _focusAmount = EaseInteraction(_focusFrom, _focusTarget, elapsedMilliseconds, 180);
            _activeAmount = EaseInteraction(_activeFrom, _activeTarget, elapsedMilliseconds, 220);
        }
        private bool InteractionSettled() => _hoverAmount == _hoverTarget && _pressAmount == _pressTarget && _focusAmount == _focusTarget && _activeAmount == _activeTarget;
        private void AdvanceInteraction()
        {
            if (!CanAnimate) { FinishAnimation(); return; }
            SampleInteraction(Stopwatch.GetElapsedTime(_started).TotalMilliseconds); Invalidate();
            if (InteractionSettled()) _animation?.Stop();
        }
        public void FinishAnimation()
        {
            _animation?.Stop(); _mouseDown = _keyboardDown = _pressed = false;
            if (!InteractionVisible) _hover = false;
            _hoverAmount = _hover && Enabled ? 1 : 0; _pressAmount = 0;
            _focusAmount = Focused && ShowFocusCues && Enabled ? 1 : 0; _activeAmount = Active ? 1 : 0;
            _hoverTarget = _hoverAmount; _pressTarget = _pressAmount; _focusTarget = _focusAmount; _activeTarget = _activeAmount;
            Invalidate();
        }
        private void UpdatePressedState() { _pressed = _keyboardDown || (_mouseDown && _hover); AnimateInteraction(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; UpdatePressedState(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; UpdatePressedState(); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_mouseDown && _hover != ClientRectangle.Contains(e.Location)) { _hover = !_hover; UpdatePressedState(); }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Enabled) { _mouseDown = _hover = true; UpdatePressedState(); }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _mouseDown = false; UpdatePressedState(); }
            base.OnMouseUp(e);
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && _mouseDown) { _mouseDown = false; UpdatePressedState(); }
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space && !e.Alt && !e.Control && Enabled && !_keyboardDown) { _keyboardDown = true; UpdatePressedState(); }
        }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && _keyboardDown) { _keyboardDown = false; UpdatePressedState(); }
            base.OnKeyUp(e);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); AnimateInteraction(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); _mouseDown = _keyboardDown = false; UpdatePressedState(); }
        protected override void OnChangeUICues(UICuesEventArgs e) { base.OnChangeUICues(e); AnimateInteraction(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); _hover = false; FinishAnimation(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) { _hover = false; FinishAnimation(); } }
        protected override void OnHandleDestroyed(EventArgs e) { FinishAnimation(); base.OnHandleDestroyed(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(SurroundingColor(this)); if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; float scale = DeviceDpi / 96f;
            var hoverColor = Mix(BackColor, FlatAppearance.MouseOverBackColor, Math.Max(_hoverAmount, _focusAmount * .4));
            var color = !Enabled ? Mix(BackColor, SurroundingColor(this), .35) : Mix(hoverColor, FlatAppearance.MouseDownBackColor, _pressAmount);
            float inset = Enabled ? Math.Min(_pressAmount * 1.2f * scale, Math.Max(0, (Math.Min(Width, Height) - 8 * scale) / 2)) : 0;
            using var path = RoundPath(new RectangleF(.5f + inset, .5f + inset, Width - 1.5f - inset * 2, Height - 1.5f - inset * 2), 3 * scale);
            if (Primary && Enabled)
            {
                using var fill = new LinearGradientBrush(ClientRectangle, Mix(color, Color.White, .07), Mix(color, Color.Black, .08), LinearGradientMode.Vertical);
                e.Graphics.FillPath(fill, path);
            }
            else { using var fill = new SolidBrush(color); e.Graphics.FillPath(fill, path); }
            bool darkSidebarButton = LeftAligned && BackColor.GetBrightness() < .25f;
            Color borderColor = darkSidebarButton ? Mix(Sidebar, Color.White, .1 + _activeAmount * .1) : AccentBorder ? Mix(Line, Cyan, .5) : Line;
            using var border = new Pen(borderColor); if (!Primary || !Enabled) e.Graphics.DrawPath(border, path);
            if (_activeAmount > .001f && LeftAligned) { using var accent = new Pen(Mix(color, ForeColor, _activeAmount * .8), 2.5f * scale); float half = (Height / 2f - 10 * scale) * _activeAmount; e.Graphics.DrawLine(accent, 1.5f * scale, Height / 2f - half, 1.5f * scale, Height / 2f + half); }
            Color textColor = Enabled ? ForeColor : Mix(ForeColor, color, .55);
            var area = Rectangle.Inflate(ClientRectangle, -(int)(9 * scale), -(int)(2 * scale));
            int contentOffset = (int)Math.Round(_pressAmount * scale); area.Offset(0, contentOffset);
            int iconSize = (int)(18 * scale), gap = (int)(8 * scale);
            if (IndexLabel is not null)
            {
                using var indexFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
                var indexArea = new Rectangle((int)(13 * scale), contentOffset, (int)(23 * scale), Height);
                TextRenderer.DrawText(e.Graphics, IndexLabel, indexFont, indexArea, Active ? Color.FromArgb(63, 69, 78) : Mix(textColor, color, .35), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                area.X = (int)(43 * scale); area.Width = Math.Max(0, Width - area.X - (int)(8 * scale));
            }
            else if (IconKind != AppGlyph.None)
            {
                int textWidth = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                int contentWidth = Math.Min(area.Width, textWidth + iconSize + gap);
                int x = LeftAligned ? area.Left + (int)(4 * scale) : area.Left + Math.Max(0, (area.Width - contentWidth) / 2);
                DrawGlyph(e.Graphics, IconKind, new RectangleF(x, (Height - iconSize) / 2f + contentOffset, iconSize, iconSize), textColor);
                area.X = x + iconSize + gap; area.Width = Math.Max(0, ClientRectangle.Right - (int)(9 * scale) - area.X);
            }
            else if (LeftAligned) { area.X += (int)(7 * scale); area.Width -= (int)(7 * scale); }
            TextRenderer.DrawText(e.Graphics, Text, Font, area, textColor, (LeftAligned || IconKind != AppGlyph.None ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (_focusAmount > .001f || Focused && ShowFocusCues)
            {
                // Keyboard focus is readable immediately, then softly settles.
                double strength = Focused && ShowFocusCues ? .65 + _focusAmount * .35 : _focusAmount;
                using var focus = new Pen(Mix(color, Mix(textColor, color, .18), strength), 1.5f * scale);
                using var focusPath = RoundPath(new RectangleF(3 * scale + inset, 3 * scale + inset, Width - 6 * scale - inset * 2, Height - 6 * scale - inset * 2), 1 * scale);
                e.Graphics.DrawPath(focus, focusPath);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) { _animation?.Stop(); _animation?.Dispose(); _animation = null; } base.Dispose(disposing); }
    }
    private sealed class BrandMark : Control
    {
        public BrandMark() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(SurroundingColor(this)); if (Width < 3 || Height < 3) return; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var fill = new LinearGradientBrush(ClientRectangle, Mix(Cyan, Color.White, .12), Mix(Cyan, Color.Black, .1), LinearGradientMode.Vertical); e.Graphics.FillRectangle(fill, 0, 0, Width - 4, Height - 4);
            using var outline = new Pen(Mix(Sidebar, Color.White, .25)); e.Graphics.DrawRectangle(outline, 4, 4, Width - 5, Height - 5);
            float s = Width / 44f; var state = e.Graphics.Save(); e.Graphics.ScaleTransform(s, s);
            using var stroke = new Pen(Color.FromArgb(18, 20, 24), 1.7f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var display = RoundPath(new RectangleF(8, 9, 24, 18), 2); e.Graphics.DrawPath(stroke, display); e.Graphics.DrawLine(stroke, 15, 32, 25, 32); e.Graphics.DrawLine(stroke, 20, 27, 20, 32);
            using var dot = new SolidBrush(Color.FromArgb(18, 20, 24)); e.Graphics.FillEllipse(dot, 16, 14, 8, 8); e.Graphics.Restore(state);
        }
    }
    private sealed class BrandWordmark : Control
    {
        public BrandWordmark() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(SurroundingColor(this));
            using var sans = new Font("Segoe UI", 16.5f, FontStyle.Bold);
            using var serif = new Font("Georgia", 18f, FontStyle.Italic);
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            int width = TextRenderer.MeasureText(e.Graphics, "Screen", sans, Size.Empty, flags).Width;
            TextRenderer.DrawText(e.Graphics, "Screen", sans, new Point(0, 2), Color.White, flags);
            TextRenderer.DrawText(e.Graphics, "Dash", serif, new Point(width, 1), Color.White, flags);
        }
    }
    private sealed class EditorialHeader : Panel
    {
        public EditorialHeader() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            using var line = new Pen(Mix(Line, Ink, .16));
            e.Graphics.DrawLine(line, 0, Height - 1, Width, Height - 1);
        }
    }
    private sealed class ClockSurface : Panel
    {
        public ClockSurface() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); if (Width < 2 || Height < 2) return;
            using var fill = new LinearGradientBrush(ClientRectangle, Mix(BackColor, Color.White, .12), Mix(BackColor, Color.Black, .08), LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(fill, ClientRectangle);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = DeviceDpi / 96f;
            using var circle = new Pen(Color.FromArgb(38, Color.Black), scale);
            e.Graphics.DrawEllipse(circle, Width - 50 * scale, Height - 51 * scale, 80 * scale, 80 * scale);
            using var line = new Pen(Color.FromArgb(38, Color.Black), scale);
            e.Graphics.DrawLine(line, Width - 28 * scale, 0, Width, 28 * scale);
        }
    }
}
