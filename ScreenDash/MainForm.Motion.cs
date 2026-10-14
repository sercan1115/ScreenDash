using System.Diagnostics;

namespace ScreenDash;

public sealed partial class MainForm
{
    private readonly System.Windows.Forms.Timer _pageMotionTimer = new() { Interval = 32 };
    private bool _motionEnabled = SystemInformation.IsMenuAnimationEnabled && !SystemInformation.HighContrast;
    private bool _motionRequested = true;
    private long _motionStarted;
    private int _motionFrameCount;
    private bool _motionDisposed;
    private void InitializeMotion()
    {
        _pageMotionTimer.Tick += (_, _) =>
        {
            if (!_motionEnabled || !Visible || WindowState == FormWindowState.Minimized) { CompletePageTransition(); return; }
            double t = Math.Clamp(Stopwatch.GetElapsedTime(_motionStarted).TotalMilliseconds / 190, 0, 1);
            int offset = (int)Math.Round(8 * DeviceDpi / 96d * Math.Pow(1 - t, 3));
            _content.Padding = new Padding(0, offset, 0, 0); _motionFrameCount++;
            if (t >= 1) CompletePageTransition();
        };
        VisibleChanged += MotionVisibilityChanged;
        Resize += MotionVisibilityChanged;
        InitializeAmbient();
    }
    private void SetMotionEnabled(bool enabled) { _motionRequested = enabled; RefreshMotionPolicy(); }
    private void BeginPageTransition()
    {
        CompletePageTransition();
        if (!_motionEnabled || !Visible || WindowState == FormWindowState.Minimized || IsDisposed || Disposing || _motionDisposed || !SystemInformation.IsMenuAnimationEnabled || SystemInformation.HighContrast) return;
        _motionStarted = Stopwatch.GetTimestamp(); _content.Padding = new Padding(0, 8 * DeviceDpi / 96, 0, 0); _pageMotionTimer.Start();
    }
    private void CompletePageTransition()
    {
        if (_motionDisposed) return;
        _pageMotionTimer.Stop();
        if (!_content.IsDisposed && _content.Padding != Padding.Empty) _content.Padding = Padding.Empty;
    }
    private void MotionVisibilityChanged(object? sender, EventArgs e)
    {
        RefreshAmbientMotion();
        if (Visible && WindowState != FormWindowState.Minimized) return;
        CompletePageTransition();
        FinishButtonMotion(this);
    }
    private static void FinishButtonMotion(Control parent)
    {
        foreach (Control child in parent.Controls) { if (child is ThemeButton button) button.FinishAnimation(); if (child.HasChildren) FinishButtonMotion(child); }
    }
    private void DisposeMotion() { if (_motionDisposed) return; DisposeAmbient(); VisibleChanged -= MotionVisibilityChanged; Resize -= MotionVisibilityChanged; CompletePageTransition(); _pageMotionTimer.Dispose(); _motionDisposed = true; }
}
