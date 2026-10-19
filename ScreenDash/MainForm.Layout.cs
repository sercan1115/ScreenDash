namespace ScreenDash;

public sealed partial class MainForm
{
    private void BuildShell()
    {
        var shell = Grid(2, 1); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 204)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); Controls.Add(shell);
        var sidebar = new SidebarSurface { Dock = DockStyle.Fill, BackColor = Sidebar, Padding = new Padding(18, 26, 18, 19), Margin = Padding.Empty };
        var sideGrid = Grid(1, 4); sideGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 139)); sideGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 230)); sideGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); sideGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 145)); sidebar.Controls.Add(sideGrid);
        var brand = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Sidebar };
        brand.Controls.Add(new BrandMark { Location = Point.Empty, Size = new Size(44, 44) });
        brand.Controls.Add(new BrandWordmark { Location = new Point(0, 59), Size = new Size(168, 34), AccessibleName = "ScreenDash" });
        var tagline = TextLabel("DEIN LOKALER RÜCKBLICK", 7.2f, Muted, FontStyle.Bold); tagline.Dock = DockStyle.None; tagline.SetBounds(1, 92, 168, 21); brand.Controls.Add(tagline);
        var brandRule = new Panel { Location = new Point(0, 125), Size = new Size(168, 1), BackColor = Line }; brand.Controls.Add(brandRule); sideGrid.Controls.Add(brand, 0, 0);
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        string[] titles = ["Aufnahme", "Aufnahmen", "Einstellungen", "Werkzeuge"]; int[] pages = [0, 2, 1, 3];
        for (int i = 0; i < titles.Length; i++)
        {
            int page = pages[i]; var button = (ThemeButton)ActionButton(titles[i]); button.Size = new Size(168, 48); button.Margin = new Padding(0, 0, 0, 7); button.LeftAligned = true; button.IndexLabel = $"{i + 1:00}"; button.Font = new Font("Segoe UI", 9.7f);
            button.Click += (_, _) => ShowPage(page); _nav[page] = button; nav.Controls.Add(button);
        }
        sideGrid.Controls.Add(nav, 0, 1);
        var sidebarArt = AmbientDecoration(Sidebar, true); sidebarArt.Dock = DockStyle.Fill; sidebarArt.Margin = new Padding(0, 3, 0, 13); sideGrid.Controls.Add(sidebarArt, 0, 2);
        var bottom = Grid(1, 4); bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 37)); bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 37)); bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var folder = SidebarAction("Ordner öffnen", AppGlyph.Folder); folder.Click += (_, _) => RunSync(_controller.OpenRecordingFolder); bottom.Controls.Add(folder, 0, 0);
        var logs = SidebarAction("Diagnose", AppGlyph.Activity); logs.Click += (_, _) => RunSync(_controller.OpenLogs); bottom.Controls.Add(logs, 0, 1);
        var privacy = new StatusPill { Text = "LOKAL GESPEICHERT", ForeColor = Cyan, Font = new Font("Segoe UI", 7.4f, FontStyle.Bold), Dock = DockStyle.Fill, Margin = new Padding(0, 5, 0, 7) }; bottom.Controls.Add(privacy, 0, 2); bottom.Controls.Add(TextLabel("Version 1.7.0  ·  Windows", 8, Muted), 0, 3); sideGrid.Controls.Add(bottom, 0, 3); shell.Controls.Add(sidebar, 0, 0);
        var mainFrame = new Panel { Dock = DockStyle.Fill, BackColor = Canvas, Padding = new Padding(28, 23, 28, 12), Margin = Padding.Empty };
        var main = Grid(1, 3); main.BackColor = Canvas; main.RowStyles.Add(new RowStyle(SizeType.Absolute, 84)); main.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); main.RowStyles.Add(new RowStyle(SizeType.Absolute, 29)); mainFrame.Controls.Add(main);
        var leftArt = AmbientDecoration(Canvas, false); var rightArt = AmbientDecoration(Canvas, false); var topArt = AmbientDecoration(Canvas, false); var bottomArt = AmbientDecoration(Canvas, false);
        mainFrame.Controls.AddRange([leftArt, rightArt, topArt, bottomArt]);
        void LayoutAmbientFrame()
        {
            int width = mainFrame.ClientSize.Width, height = mainFrame.ClientSize.Height;
            var padding = mainFrame.Padding;
            int left = Math.Min(width, padding.Left), right = Math.Min(Math.Max(0, width - left), padding.Right);
            int top = Math.Min(height, padding.Top), bottomHeight = Math.Min(Math.Max(0, height - top), padding.Bottom);
            leftArt.SetBounds(0, 0, left, height); rightArt.SetBounds(width - right, 0, right, height);
            topArt.SetBounds(left, 0, Math.Max(0, width - left - right), top); bottomArt.SetBounds(left, height - bottomHeight, Math.Max(0, width - left - right), bottomHeight);
            foreach (var art in new[] { leftArt, rightArt, topArt, bottomArt }) { art.CanvasOrigin = art.Location; art.CanvasSize = mainFrame.ClientSize; }
        }
        mainFrame.SizeChanged += (_, _) => LayoutAmbientFrame(); mainFrame.PaddingChanged += (_, _) => LayoutAmbientFrame(); LayoutAmbientFrame();
        var headerSurface = new EditorialHeader { Dock = DockStyle.Fill, BackColor = Canvas, Margin = new Padding(0, 0, 0, 10) };
        var header = Grid(3, 2); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 208)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 184)); header.RowStyles.Add(new RowStyle(SizeType.Absolute, 47)); header.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        _pageTitle.Font = new Font("Georgia", 29.5f, FontStyle.Italic); _pageTitle.ForeColor = Cyan;
        _pageSubtitle.Font = new Font("Segoe UI", 9.5f); _pageSubtitle.TextAlign = ContentAlignment.TopLeft;
        _headerState.Margin = new Padding(0, 7, 0, 7);
        header.Controls.Add(_pageTitle, 0, 0); header.Controls.Add(_pageSubtitle, 0, 1); header.Controls.Add(_headerState, 2, 0); var keys = TextLabel("Strg + Alt + F8  ·  Start / Stopp", 8, Muted); keys.TextAlign = ContentAlignment.MiddleRight; header.Controls.Add(keys, 2, 1);
        var headerArt = AmbientDecoration(Canvas, true); headerArt.Dock = DockStyle.Fill; headerArt.Margin = new Padding(10, 0, 12, 1); header.Controls.Add(headerArt, 1, 0); header.SetRowSpan(headerArt, 2);
        header.SizeChanged += (_, _) =>
        {
            bool showArt = header.ClientSize.Width >= 880 * DeviceDpi / 96;
            headerArt.Visible = showArt;
            float width = showArt ? 208 * DeviceDpi / 96f : 0;
            if (header.ColumnStyles[1].Width != width) header.ColumnStyles[1].Width = width;
        };
        headerSurface.Controls.Add(header);
        _footer.Text = "Deine Videos bleiben auf diesem PC."; _footer.Font = new Font("Segoe UI", 8.5f); main.Controls.Add(headerSurface, 0, 0); main.Controls.Add(_content, 0, 1); main.Controls.Add(_footer, 0, 2); shell.Controls.Add(mainFrame, 1, 0);
    }
    private static ThemeButton SidebarAction(string text, AppGlyph glyph)
    {
        var button = (ThemeButton)ActionButton(text);
        button.IconKind = glyph; button.LeftAligned = true; button.Dock = DockStyle.Fill;
        button.BackColor = Sidebar; button.ForeColor = Cyan;
        button.FlatAppearance.MouseOverBackColor = Mix(Sidebar, Color.White, .08); button.FlatAppearance.MouseDownBackColor = Mix(Sidebar, Color.White, .13);
        button.Margin = new Padding(0, 0, 0, 6); button.Font = new Font("Segoe UI", 9);
        return button;
    }
    private void BuildDashboard()
    {
        var grid = Grid(1, 4); grid.Dock = DockStyle.Top; grid.Height = 560; grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 254)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 120)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 143)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); _dashboard.Controls.Add(grid);
        _dashboard.SizeChanged += (_, _) =>
        {
            int minimum = 560 * DeviceDpi / 96;
            _dashboard.AutoScroll = _dashboard.ClientSize.Height < minimum;
            grid.Height = Math.Max(minimum, _dashboard.ClientSize.Height); _dashboard.AutoScrollMinSize = new Size(0, _dashboard.AutoScroll ? minimum : 0);
        };
        var hero = (RoundedPanel)Surface(new Padding(22, 16, 22, 17)); hero.Hero = true;
        var heroGrid = Grid(2, 6); heroGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heroGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        heroGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); heroGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); heroGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); heroGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 27)); heroGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); heroGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heroGrid.Controls.Add(TextLabel("01  /  AUFNAHME IM HINTERGRUND", 7.8f, Cyan, FontStyle.Bold), 0, 0);
        _recordingState.Font = new Font("Georgia", 21.5f, FontStyle.Italic); _recordingState.ForeColor = Ink;
        _recordingMessage.Font = new Font("Segoe UI", 9.5f); _source.Font = new Font("Segoe UI", 9.3f);
        var clock = new ClockSurface { Dock = DockStyle.Fill, BackColor = Cyan, Padding = new Padding(11, 6, 11, 6), Margin = new Padding(15, 0, 0, 2) };
        var clockGrid = Grid(1, 2); clockGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 17)); clockGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var timerHeading = TextLabel("SITZUNGSDAUER", 7.5f, Color.FromArgb(50, 55, 63), FontStyle.Bold); timerHeading.TextAlign = ContentAlignment.MiddleRight;
        _elapsed.Font = new Font("Segoe UI", 24, FontStyle.Bold); _elapsed.ForeColor = Color.FromArgb(18, 20, 24); _elapsed.TextAlign = ContentAlignment.MiddleRight;
        clockGrid.Controls.Add(timerHeading, 0, 0); clockGrid.Controls.Add(_elapsed, 0, 1); clock.Controls.Add(clockGrid);
        heroGrid.Controls.Add(clock, 1, 0); heroGrid.SetRowSpan(clock, 2);
        heroGrid.Controls.Add(_recordingState, 0, 1); heroGrid.Controls.Add(_recordingMessage, 0, 2); heroGrid.SetColumnSpan(_recordingMessage, 2); heroGrid.Controls.Add(_source, 0, 3); heroGrid.SetColumnSpan(_source, 2);
        heroGrid.Controls.Add(_sound, 0, 4); _autoStopCountdown.TextAlign = ContentAlignment.MiddleRight; heroGrid.Controls.Add(_autoStopCountdown, 1, 4);
        var actions = Grid(4, 1); float[] weights = [31, 18, 27, 24]; Button[] buttons = [_toggle, _pause, _protect, _bookmark];
        for (int i = 0; i < buttons.Length; i++) { actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, weights[i])); buttons[i].Dock = DockStyle.Fill; buttons[i].Margin = new Padding(0, 7, i == buttons.Length - 1 ? 0 : 8, 0); buttons[i].Font = new Font("Segoe UI", i == 0 ? 9.5f : 9f, i == 0 ? FontStyle.Bold : FontStyle.Regular); actions.Controls.Add(buttons[i], i, 0); }
        ((ThemeButton)_toggle).IconKind = AppGlyph.Record; ((ThemeButton)_pause).IconKind = AppGlyph.Pause; ((ThemeButton)_protect).IconKind = AppGlyph.Shield; ((ThemeButton)_bookmark).IconKind = AppGlyph.Bookmark; _bookmark.Text = "Lesezeichen";
        _toggle.Click += async (_, _) => await ToggleRecordingActionAsync(); _pause.Click += async (_, _) => await RunActionAsync(TogglePauseAsync); _protect.Click += async (_, _) => await RunActionAsync(_controller.ProtectRecentAsync); _bookmark.Click += async (_, _) => await AddBookmarkAsync();
        heroGrid.Controls.Add(actions, 0, 5); heroGrid.SetColumnSpan(actions, 2); hero.Controls.Add(heroGrid); grid.Controls.Add(hero, 0, 0);
        _tips.SetToolTip(_toggle, "Aufnahme starten oder Sitzung vollständig stoppen · Strg + Alt + F8"); _tips.SetToolTip(_pause, "Manuell pausieren und fortsetzen · Strg + Alt + F10"); _tips.SetToolTip(_protect, "Wichtige Aufnahmen vor automatischem Löschen schützen · Strg + Alt + F9"); _tips.SetToolTip(_bookmark, "Moment markieren und gesamte Datei schützen · Strg + Alt + F11");
        var metrics = Grid(4, 1); for (int i = 0; i < 4; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        _encoder.Font = new Font("Segoe UI", 16, FontStyle.Bold); _cpu.Font = new Font("Segoe UI", 24); _memory.Font = new Font("Segoe UI", 24); _fps.Font = new Font("Segoe UI", 24); _fps.ForeColor = Cyan;
        metrics.Controls.Add(Metric("KODIERUNG", _encoder, _encoderDetail, false), 0, 0); metrics.Controls.Add(Metric("CPU", _cpu, "App + Encoder", false), 1, 0); metrics.Controls.Add(Metric("ARBEITSSPEICHER", _memory, "App + Encoder", false), 2, 0); metrics.Controls.Add(Metric("BILDRATE", _fps, "Bilder pro Sekunde", true), 3, 0); grid.Controls.Add(metrics, 0, 1);
        var storage = Surface(new Padding(22, 11, 22, 12)); var storageGrid = Grid(1, 5); storageGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); storageGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 29)); storageGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 13)); storageGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); storageGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        storageGrid.Controls.Add(TextLabel("02  /  SPEICHER & AUFBEWAHRUNG", 7.8f, Cyan, FontStyle.Bold), 0, 0); storageGrid.Controls.Add(_storage, 0, 1); storageGrid.Controls.Add(_storageMeter, 0, 2); storageGrid.Controls.Add(_storageDetail, 0, 3); storageGrid.Controls.Add(_storageEstimate, 0, 4); storage.Controls.Add(storageGrid); grid.Controls.Add(storage, 0, 2);
        var shortcuts = Grid(2, 1); shortcuts.Padding = new Padding(2, 5, 2, 0); shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63)); shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        var help = TextLabel("Lesezeichen sichern die ganze Datei. Mit Strg + Alt:\nF9 Sichern · F10 Pause · F11 Lesezeichen", 8.8f, Muted); help.TextAlign = ContentAlignment.TopLeft; shortcuts.Controls.Add(help, 0, 0);
        var landscape = AmbientDecoration(Canvas, true); landscape.Dock = DockStyle.Fill; landscape.Margin = new Padding(14, 0, 0, 0); shortcuts.Controls.Add(landscape, 1, 0); grid.Controls.Add(shortcuts, 0, 3);
    }
}
