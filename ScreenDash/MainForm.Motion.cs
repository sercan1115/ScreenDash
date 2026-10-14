using System.Diagnostics;

namespace ScreenDash;

public sealed partial class MainForm
{
    private readonly System.Windows.Forms.Timer _pageMotionTimer = new() { Interval = 15 };
    private bool _motionEnabled = SystemInformation.IsMenuAnimationEnabled && !SystemInformation.HighContrast;
    private bool _motionRequested = true;
    private long _motionStarted;
    private int _motionFrameCount;
    private bool _motionDisposed;
    private void InitializeMotion()
    {
        _pageMotionTimer.Tick += PageMotionTick;
        VisibleChanged += MotionVisibilityChanged;
        Resize += MotionVisibilityChanged;
        DpiChanged += MotionDpiChanged;
        _content.SizeChanged += TransitionHostResized;
        _settingsFields.SizeChanged += TransitionHostResized;
        _toolFields.SizeChanged += TransitionHostResized;
        _transitionInputFilter = new TransitionInputFilter(this);
        Application.AddMessageFilter(_transitionInputFilter);
        InitializeAmbient();
    }
    private void SetMotionEnabled(bool enabled) { _motionRequested = enabled; RefreshMotionPolicy(); }
    private void BeginPageTransition() => BeginVisualTransition(_content);
    private void BeginSettingTabTransition() => BeginVisualTransition(_settingsFields);
    private void BeginToolTabTransition() => BeginVisualTransition(_toolFields);

    private void PageMotionTick(object? sender, EventArgs e)
    {
        if (!CanRunPageMotion() || _pageTransition is null) { CompletePageTransition(); return; }
        double progress = Math.Clamp(Stopwatch.GetElapsedTime(_motionStarted).TotalMilliseconds / 240, 0, 1);
        _motionFrameCount++;
        if (progress >= 1) CompletePageTransition();
        else _pageTransition.SetProgress(progress);
    }

    private bool CanRunPageMotion() => _motionEnabled && Visible && WindowState != FormWindowState.Minimized &&
        !IsDisposed && !Disposing && !_motionDisposed && SystemMotionAllowed();
    private void CompletePageTransition()
    {
        if (_motionDisposed) return;
        _pageMotionTimer.Stop();
        var overlay = _pageTransition;
        _pageTransition = null;
        if (overlay is not null && !overlay.IsDisposed)
        {
            overlay.Visible = false;
            overlay.Parent?.Controls.Remove(overlay);
            overlay.Dispose();
        }
        if (!_content.IsDisposed && _content.Padding != Padding.Empty) _content.Padding = Padding.Empty;
    }

    private void TransitionHostResized(object? sender, EventArgs e) => CompletePageTransition();
    private void MotionDpiChanged(object? sender, DpiChangedEventArgs e) => CompletePageTransition();

    private void MotionVisibilityChanged(object? sender, EventArgs e)
    {
        // A cached page cannot remain aligned after a resize. Hiding/minimizing also releases it immediately.
        CompletePageTransition();
        RefreshAmbientMotion();
        if (Visible && WindowState != FormWindowState.Minimized) return;
        FinishButtonMotion(this);
    }
    private static void FinishButtonMotion(Control parent)
    {
        foreach (Control child in parent.Controls) { if (child is ThemeButton button) button.FinishAnimation(); if (child.HasChildren) FinishButtonMotion(child); }
    }
    private void DisposeMotion()
    {
        if (_motionDisposed) return;
        DisposeAmbient();
        VisibleChanged -= MotionVisibilityChanged;
        Resize -= MotionVisibilityChanged;
        DpiChanged -= MotionDpiChanged;
        _content.SizeChanged -= TransitionHostResized;
        _settingsFields.SizeChanged -= TransitionHostResized;
        _toolFields.SizeChanged -= TransitionHostResized;
        if (_transitionInputFilter is not null) { Application.RemoveMessageFilter(_transitionInputFilter); _transitionInputFilter = null; }
        CompletePageTransition();
        _pageMotionTimer.Tick -= PageMotionTick;
        _pageMotionTimer.Dispose();
        _motionDisposed = true;
    }
}
