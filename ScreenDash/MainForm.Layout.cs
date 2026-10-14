namespace ScreenDash;

public sealed partial class MainForm
{
    private void BuildShell()
    {
        var shell = Grid(2, 1); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 204)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); Controls.Add(shell);
        var sidebar = new SidebarSurface { Dock = DockStyle.Fill, BackColor = Sidebar, Padding = new Padding(18, 25, 18, 19), Margin = Padding.Empty };
        var sideGrid = Grid(1, 4); sideGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 126)); sideGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 237)); sideGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); sideGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 144)); sidebar.Controls.Add(sideGrid);
        var brand = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }; brand.Controls.Add(new BrandMark { Location = Point.Empty, Size = new Size(44, 44) });
        var name = TextLabel("ScreenDash", 17, Ink, FontStyle.Bold); name.Dock = DockStyle.None; name.SetBounds(0, 53, 170, 31); brand.Controls.Add(name);
        var tagline = TextLabel("DEIN LOKALER RÜCKBLICK", 7.5f, Muted, FontStyle.Bold); tagline.Dock = DockStyle.None; tagline.SetBounds(1, 86, 171, 21); brand.Controls.Add(tagline); sideGrid.Controls.Add(brand, 0, 0);
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        string[] titles = ["Aufnahme", "Aufnahmen", "Einstellungen", "Werkzeuge"]; int[] pages = [0, 2, 1, 3]; AppGlyph[] icons = [AppGlyph.Record, AppGlyph.Archive, AppGlyph.Settings, AppGlyph.Tools];
        for (int i = 0; i < titles.Length; i++)
        {
            int page = pages[i]; var button = (ThemeButton)ActionButton(titles[i]); button.Size = new Size(168, 47); button.Margin = new Padding(0, 0, 0, 9); button.LeftAligned = true; button.IconKind = icons[i]; button.Font = new Font("Segoe UI", 10);
            button.Click += (_, _) => ShowPage(page); _nav[page] = button; nav.Controls.Add(button);
        }
        sideGrid.Controls.Add(nav, 0, 1);
        var bottom = Grid(1, 4); bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 37)); bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 37)); bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var folder = (ThemeButton)ActionButton("Ordner öffnen"); folder.IconKind = AppGlyph.Folder; folder.LeftAligned = true; folder.Dock = DockStyle.Fill; folder.Margin = new Padding(0, 0, 0, 6); folder.Font = new Font("Segoe UI", 9); folder.Click += (_, _) => RunSync(_controller.OpenRecordingFolder); bottom.Controls.Add(folder, 0, 0);
        var logs = (ThemeButton)ActionButton("Diagnose"); logs.IconKind = AppGlyph.Activity; logs.LeftAligned = true; logs.Dock = DockStyle.Fill; logs.Margin = new Padding(0, 0, 0, 6); logs.Font = new Font("Segoe UI", 9); logs.Click += (_, _) => RunSync(_controller.OpenLogs); bottom.Controls.Add(logs, 0, 1);
        var privacy = new StatusPill { Text = "LOKAL GESPEICHERT", ForeColor = Cyan, Font = new Font("Segoe UI", 7.4f, FontStyle.Bold), Dock = DockStyle.Fill, Margin = new Padding(0, 5, 0, 7) }; bottom.Controls.Add(privacy, 0, 2); bottom.Controls.Add(TextLabel("Version 1.4.2  ·  Windows", 8, Muted), 0, 3); sideGrid.Controls.Add(bottom, 0, 3); shell.Controls.Add(sidebar, 0, 0);
        var main = Grid(1, 3); main.Padding = new Padding(28, 23, 28, 12); main.RowStyles.Add(new RowStyle(SizeType.Absolute, 84)); main.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); main.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        var header = Grid(2, 2); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 184)); header.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); header.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        header.Controls.Add(_pageTitle, 0, 0); header.Controls.Add(_pageSubtitle, 0, 1); header.Controls.Add(_headerState, 1, 0); var keys = TextLabel("Strg + Alt + F8  ·  Start / Stopp", 8, Muted); keys.TextAlign = ContentAlignment.MiddleRight; header.Controls.Add(keys, 1, 1);
        _footer.Text = "Deine Videos bleiben auf diesem PC."; main.Controls.Add(header, 0, 0); main.Controls.Add(_content, 0, 1); main.Controls.Add(_footer, 0, 2); shell.Controls.Add(main, 1, 0);
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
        heroGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); heroGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); heroGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 37)); heroGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 27)); heroGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 29)); heroGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heroGrid.Controls.Add(TextLabel("AUFNAHME IM HINTERGRUND", 8, Cyan, FontStyle.Bold), 0, 0); var timerHeading = TextLabel("SITZUNGSDAUER", 8, Muted, FontStyle.Bold); timerHeading.TextAlign = ContentAlignment.MiddleRight; heroGrid.Controls.Add(timerHeading, 1, 0);
        _recordingState.Font = new Font("Segoe UI", 20, FontStyle.Bold); _elapsed.Font = new Font("Segoe UI", 27, FontStyle.Bold); _elapsed.ForeColor = Cyan; _elapsed.TextAlign = ContentAlignment.MiddleRight;
        heroGrid.Controls.Add(_recordingState, 0, 1); heroGrid.Controls.Add(_elapsed, 1, 1); heroGrid.Controls.Add(_recordingMessage, 0, 2); heroGrid.SetColumnSpan(_recordingMessage, 2); heroGrid.Controls.Add(_source, 0, 3); heroGrid.SetColumnSpan(_source, 2);
        heroGrid.Controls.Add(_sound, 0, 4); _autoStopCountdown.TextAlign = ContentAlignment.MiddleRight; heroGrid.Controls.Add(_autoStopCountdown, 1, 4);
        var actions = Grid(4, 1); float[] weights = [31, 18, 27, 24]; Button[] buttons = [_toggle, _pause, _protect, _bookmark];
        for (int i = 0; i < buttons.Length; i++) { actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, weights[i])); buttons[i].Dock = DockStyle.Fill; buttons[i].Margin = new Padding(0, 7, i == buttons.Length - 1 ? 0 : 8, 0); buttons[i].Font = new Font("Segoe UI", i == 0 ? 9.5f : 9f, i == 0 ? FontStyle.Bold : FontStyle.Regular); actions.Controls.Add(buttons[i], i, 0); }
        ((ThemeButton)_toggle).IconKind = AppGlyph.Record; ((ThemeButton)_pause).IconKind = AppGlyph.Pause; ((ThemeButton)_protect).IconKind = AppGlyph.Shield; ((ThemeButton)_bookmark).IconKind = AppGlyph.Bookmark; _bookmark.Text = "Lesezeichen";
        _toggle.Click += async (_, _) => await ToggleRecordingActionAsync(); _pause.Click += async (_, _) => await RunActionAsync(TogglePauseAsync); _protect.Click += async (_, _) => await RunActionAsync(_controller.ProtectRecentAsync); _bookmark.Click += async (_, _) => await AddBookmarkAsync();
        heroGrid.Controls.Add(actions, 0, 5); heroGrid.SetColumnSpan(actions, 2); hero.Controls.Add(heroGrid); grid.Controls.Add(hero, 0, 0);
        _tips.SetToolTip(_toggle, "Aufnahme starten oder Sitzung vollständig stoppen · Strg + Alt + F8"); _tips.SetToolTip(_pause, "Manuell pausieren und fortsetzen · Strg + Alt + F10"); _tips.SetToolTip(_protect, "Wichtige Aufnahmen vor automatischem Löschen schützen · Strg + Alt + F9"); _tips.SetToolTip(_bookmark, "Moment markieren und gesamte Datei schützen · Strg + Alt + F11");
        var metrics = Grid(4, 1); for (int i = 0; i < 4; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        metrics.Controls.Add(Metric("KODIERUNG", _encoder, "Hardware · H.264", false), 0, 0); metrics.Controls.Add(Metric("CPU", _cpu, "App + Encoder", false), 1, 0); metrics.Controls.Add(Metric("ARBEITSSPEICHER", _memory, "App + Encoder", false), 2, 0); metrics.Controls.Add(Metric("BILDRATE", _fps, "Bilder pro Sekunde", true), 3, 0); grid.Controls.Add(metrics, 0, 1);
        var storage = Surface(new Padding(22, 11, 22, 12)); var storageGrid = Grid(1, 5); storageGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20)); storageGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 29)); storageGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 13)); storageGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); storageGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        storageGrid.Controls.Add(TextLabel("SPEICHER & AUFBEWAHRUNG", 8, Muted, FontStyle.Bold), 0, 0); storageGrid.Controls.Add(_storage, 0, 1); storageGrid.Controls.Add(_storageMeter, 0, 2); storageGrid.Controls.Add(_storageDetail, 0, 3); storageGrid.Controls.Add(_storageEstimate, 0, 4); storage.Controls.Add(storageGrid); grid.Controls.Add(storage, 0, 2);
        var shortcuts = Grid(2, 1); shortcuts.Padding = new Padding(2, 5, 2, 0); shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62)); shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        var help = TextLabel("Ein Moment, den du behalten möchtest?\nLesezeichen markieren ihn und sichern die ganze Datei.", 9, Muted); help.TextAlign = ContentAlignment.TopLeft; shortcuts.Controls.Add(help, 0, 0);
        var keyHints = TextLabel("Mit Strg + Alt:\nF9 Sichern · F10 Pause · F11 Lesezeichen", 9, Muted); keyHints.TextAlign = ContentAlignment.TopRight; shortcuts.Controls.Add(keyHints, 1, 0); grid.Controls.Add(shortcuts, 0, 3);
    }
}
