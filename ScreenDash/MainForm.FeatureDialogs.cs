namespace ScreenDash;

public sealed partial class MainForm
{
    private static Form FeatureDialog(string title, Size size)
    {
        var form = new Form(); ConfigureFeatureDialog(form, title, size); form.FormBorderStyle = FormBorderStyle.FixedDialog; form.MaximizeBox = false; return form;
    }
    private static void ConfigureFeatureDialog(Form form, string title, Size size)
    {
        form.Text = title; form.ClientSize = size; form.StartPosition = FormStartPosition.CenterParent; form.BackColor = Canvas; form.ForeColor = Ink; form.Font = new Font("Segoe UI", 10); form.ShowInTaskbar = false; form.MinimizeBox = false; form.AutoScaleMode = AutoScaleMode.Dpi; ConfigureDarkTitleBar(form);
    }
    private static Control FeatureDialogHeader(string title, string description)
    {
        var header = Grid(1, 2); header.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); header.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); header.Controls.Add(TextLabel(title, 18, Ink, FontStyle.Bold), 0, 0); var help = TextLabel(description, 9, Muted); help.TextAlign = ContentAlignment.TopLeft; help.Padding = new Padding(0, 4, 0, 0); header.Controls.Add(help, 0, 1); return header;
    }
    private static Control FeatureDialogFooter(params Button[] buttons)
    {
        var actions = FeatureActions(buttons); actions.FlowDirection = FlowDirection.RightToLeft; actions.Padding = new Padding(0, 9, 0, 0); return actions;
    }
    private static string? PromptFeatureText(IWin32Window owner, string title, string hint, string current, int maximum = 120)
    {
        using var dialog = FeatureDialog(title, new Size(530, 236)); var grid = Grid(1, 3); grid.Padding = new Padding(26, 22, 26, 20); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 77)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); grid.Controls.Add(FeatureDialogHeader(title, hint), 0, 0); var entry = Entry(); entry.Text = current; entry.MaxLength = maximum; entry.Margin = new Padding(0, 4, 0, 7); grid.Controls.Add(entry, 0, 1); var save = ActionButton("Speichern", true); save.DialogResult = DialogResult.OK; var cancel = ActionButton("Abbrechen"); cancel.DialogResult = DialogResult.Cancel; grid.Controls.Add(FeatureDialogFooter(save, cancel), 0, 2); dialog.Controls.Add(grid); dialog.AcceptButton = save; dialog.CancelButton = cancel; dialog.Shown += (_, _) => { entry.Focus(); entry.SelectAll(); }; return dialog.ShowDialog(owner) == DialogResult.OK ? entry.Text.Trim() : null;
    }

    private sealed class BookmarkListItem(RecordingBookmark bookmark)
    {
        public RecordingBookmark Bookmark { get; } = bookmark;
        public override string ToString() => $"{FormatDuration(Bookmark.Offset)}   ·   {(string.IsNullOrWhiteSpace(Bookmark.Label) ? "Markierter Moment" : Bookmark.Label)}";
    }
    private sealed class BookmarkDialog : Form
    {
        private readonly IAppController _controller;
        private readonly RecordingInfo _recording;
        private readonly ListBox _list = new() { Dock = DockStyle.Fill, BackColor = Card, ForeColor = Ink, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10.5f), IntegralHeight = false, ItemHeight = 64, DrawMode = DrawMode.OwnerDrawFixed };
        private readonly Button _rename = ActionButton("Benennen"), _deleteBookmark = ActionButton("Entfernen"), _exportMoment = ActionButton("Moment als Clip …", true), _close = ActionButton("Schließen");
        private readonly Label _hint = TextLabel("", 9, Muted);
        private bool _busy;
        public RecordingBookmark? SelectedBookmark => (_list.SelectedItem as BookmarkListItem)?.Bookmark;
        public BookmarkDialog(IAppController controller, RecordingInfo recording, IReadOnlyList<RecordingBookmark> bookmarks)
        {
            _controller = controller; _recording = recording; ConfigureFeatureDialog(this, "Lesezeichen · " + recording.FileName, new Size(720, 510)); MinimumSize = new Size(660, 410);
            var grid = Grid(1, 4); grid.Padding = new Padding(26, 22, 26, 22); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 83)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 66)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 49)); grid.Controls.Add(FeatureDialogHeader("Deine markierten Momente", "Benenne deinen Rückblick oder exportiere einen Moment als Clip."), 0, 0); var listCard = Surface(new Padding(10)); listCard.Margin = new Padding(0, 0, 0, 6); listCard.Controls.Add(_list); grid.Controls.Add(listCard, 0, 1); grid.Controls.Add(_hint, 0, 2); grid.Controls.Add(FeatureDialogFooter(_exportMoment, _rename, _deleteBookmark, _close), 0, 3); Controls.Add(grid); _list.DrawItem += DrawBookmark; Shown += (_, _) => _list.ItemHeight = 64 * DeviceDpi / 96;
            _list.SelectedIndexChanged += (_, _) => UpdateActions(); _rename.Click += async (_, _) => await RenameAsync(); _deleteBookmark.Click += async (_, _) => await DeleteAsync(); _exportMoment.DialogResult = DialogResult.OK; _close.DialogResult = DialogResult.Cancel; CancelButton = _close; SetBookmarks(bookmarks); UpdateActions(); FormClosing += (_, e) => { if (_busy) e.Cancel = true; };
        }
        private void DrawBookmark(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _list.Items.Count) return;
            var bookmark = ((BookmarkListItem)_list.Items[e.Index]).Bookmark; bool selected = (e.State & DrawItemState.Selected) != 0; float scale = DeviceDpi / 96f; int px(float value) => (int)Math.Round(value * scale); int gap = px(5), inset = px(12), stampWidth = px(112);
            using var background = new SolidBrush(Card); e.Graphics.FillRectangle(background, e.Bounds); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; var area = Rectangle.Inflate(e.Bounds, -gap, -gap); using var shape = RoundPath(area, 10 * scale); using var fill = new SolidBrush(selected ? Color.FromArgb(31, 51, 61) : Input); using var border = new Pen(selected ? Color.FromArgb(65, 125, 122) : Line); e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(border, shape);
            var stamp = new Rectangle(area.X + inset, area.Y, stampWidth, area.Height); TextRenderer.DrawText(e.Graphics, FormatDuration(bookmark.Offset), _list.Font, stamp, selected ? Cyan : Violet, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            var text = new Rectangle(stamp.Right + px(7), area.Top + px(6), Math.Max(0, area.Right - stamp.Right - px(20)), area.Height / 2); TextRenderer.DrawText(e.Graphics, string.IsNullOrWhiteSpace(bookmark.Label) ? "Markierter Moment" : bookmark.Label, _list.Font, text, Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using var detailFont = new Font("Segoe UI", 8.5f); text.Y = area.Top + area.Height / 2; text.Height = area.Height / 2 - px(4); TextRenderer.DrawText(e.Graphics, bookmark.CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy · HH:mm:ss"), detailFont, text, Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(area, -3, -3), Cyan, selected ? Color.FromArgb(31, 51, 61) : Input);
        }
        private void SetBookmarks(IReadOnlyList<RecordingBookmark> bookmarks, string? selection = null)
        {
            _list.Items.Clear(); foreach (var bookmark in bookmarks.OrderBy(b => b.Offset)) _list.Items.Add(new BookmarkListItem(bookmark)); if (_list.Items.Count > 0) { _list.SelectedIndex = Enumerable.Range(0, _list.Items.Count).FirstOrDefault(i => ((BookmarkListItem)_list.Items[i]).Bookmark.Id == selection); }
        }
        private void UpdateActions()
        {
            _rename.Enabled = _deleteBookmark.Enabled = !_busy && SelectedBookmark is not null; _exportMoment.Enabled = !_busy && !_recording.Active && SelectedBookmark is not null; _close.Enabled = !_busy;
            _hint.Text = _busy ? "Lesezeichen wird gespeichert …" : _list.Items.Count == 0 ? "Noch keine Lesezeichen. Während der Aufnahme Strg + Alt + F11 drücken." : _recording.Active ? "Die Datei läuft noch. Benennen ist möglich; für Clips zuerst pausieren oder stoppen." : "Beim Markieren wird die ganze Datei gesichert. Entfernen eines Lesezeichens hebt diesen Schutz nicht auf.";
        }
        private async Task RenameAsync()
        {
            if (SelectedBookmark is not { } bookmark || _busy) return; string? name = PromptFeatureText(this, "Lesezeichen benennen", "Ein kurzer Name hilft dir, diesen Moment wiederzufinden.", bookmark.Label); if (name is null) return;
            await ChangeAsync(() => _controller.RenameBookmarkAsync(_recording.Path, bookmark.Id, name), bookmark.Id);
        }
        private async Task DeleteAsync()
        {
            if (SelectedBookmark is not { } bookmark || _busy) return;
            if (MessageBox.Show(this, "Dieses Lesezeichen entfernen? Die Videoaufnahme bleibt erhalten.", "Lesezeichen entfernen", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            await ChangeAsync(() => _controller.DeleteBookmarkAsync(_recording.Path, bookmark.Id), null);
        }
        private async Task ChangeAsync(Func<Task> change, string? selected)
        {
            _busy = true; UpdateActions(); try { await change(); SetBookmarks(await _controller.GetBookmarksAsync(_recording.Path), selected); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Lesezeichen", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { _busy = false; UpdateActions(); }
        }
    }

    private sealed class GamePickerDialog : Form
    {
        private readonly CheckedListBox _list = new() { Dock = DockStyle.Fill, BackColor = Input, ForeColor = Ink, BorderStyle = BorderStyle.None, CheckOnClick = true, IntegralHeight = false, Font = new Font("Segoe UI", 10.5f) };
        public IReadOnlyList<string> SelectedNames => _list.CheckedItems.Cast<string>().ToArray();
        public GamePickerDialog(IReadOnlyList<string> running, IReadOnlyList<string> existing)
        {
            ConfigureFeatureDialog(this, "Spiele / Programme auswählen", new Size(570, 510)); MinimumSize = new Size(510, 390);
            var grid = Grid(1, 3); grid.Padding = new Padding(26, 22, 26, 22); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 91)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 54)); grid.Controls.Add(FeatureDialogHeader("Was möchtest du aufnehmen?", "Wähle die echte Spiel-EXE statt des Launchers. Es erscheinen sichtbare\nFenster; gespeicherte Namen bleiben auch bei geschlossenem Spiel sichtbar."), 0, 0); var card = Surface(new Padding(15)); card.Margin = Padding.Empty; card.BackColor = Input; card.Controls.Add(_list); grid.Controls.Add(card, 0, 1);
            var selected = existing.ToHashSet(StringComparer.OrdinalIgnoreCase); foreach (var name in running.Concat(existing).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)) _list.Items.Add(name, selected.Contains(name)); var accept = ActionButton("Auswahl übernehmen", true); accept.DialogResult = DialogResult.OK; var cancel = ActionButton("Abbrechen"); cancel.DialogResult = DialogResult.Cancel; grid.Controls.Add(FeatureDialogFooter(accept, cancel), 0, 2); Controls.Add(grid); AcceptButton = accept; CancelButton = cancel;
        }
    }

    private sealed class TimeSearchDialog : Form
    {
        private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd.MM.yyyy", Dock = DockStyle.Fill };
        private readonly DateTimePicker _time = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm:ss", ShowUpDown = true, Dock = DockStyle.Fill };
        public DateTime SelectedTime => DateTime.SpecifyKind(_date.Value.Date + _time.Value.TimeOfDay, DateTimeKind.Local);
        public TimeSearchDialog()
        {
            ConfigureFeatureDialog(this, "Aufnahme nach Uhrzeit finden", new Size(530, 300)); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
            var fields = Grid(2, 0); fields.Padding = new Padding(26, 22, 26, 22); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); AddWide(fields, FeatureDialogHeader("Wann war dein Moment?", "Wähle Datum und Uhrzeit aus deinem Rückblick."), 81); AddField(fields, "Datum", _date, 45); AddField(fields, "Uhrzeit", _time, 45); AddWide(fields, TextLabel("Lokale Windows-Zeit · Clips können nach einem Treffer exportiert werden.", 8.5f, Muted), 35); var find = ActionButton("Moment suchen", true); find.DialogResult = DialogResult.OK; var cancel = ActionButton("Abbrechen"); cancel.DialogResult = DialogResult.Cancel; AddWide(fields, FeatureDialogFooter(find, cancel), 50); Controls.Add(fields); AcceptButton = find; CancelButton = cancel;
        }
    }

    private sealed class MomentClipDialog : Form
    {
        private readonly NumericUpDown _before = Number(0, 3600), _after = Number(0, 3600);
        private readonly Label _summary = TextLabel("", 10, Cyan);
        private readonly Button _accept = ActionButton("Clip speichern …", true);
        private readonly TimeSpan _offset, _total;
        public ClipRange SelectedRange { get { var start = _offset - TimeSpan.FromSeconds((double)_before.Value); if (start < TimeSpan.Zero) start = TimeSpan.Zero; var end = _offset + TimeSpan.FromSeconds((double)_after.Value); if (end > _total) end = _total; return new(start, end - start); } }
        public MomentClipDialog(string label, TimeSpan offset, TimeSpan total)
        {
            if (total <= TimeSpan.Zero) throw new InvalidOperationException("Die Aufnahmedauer konnte nicht ermittelt werden."); _total = total; _offset = offset < TimeSpan.Zero ? TimeSpan.Zero : offset > total ? total : offset;
            ConfigureFeatureDialog(this, "Clip um deinen Moment", new Size(580, 378)); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
            var fields = Grid(2, 0); fields.Padding = new Padding(26, 22, 26, 22); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); AddWide(fields, FeatureDialogHeader(string.IsNullOrWhiteSpace(label) ? "Markierter Moment" : label, $"Position {FormatDuration(_offset)}  ·  Aufnahme {FormatDuration(total)}"), 81); AddField(fields, "Davor", WithUnit(_before, "Sekunden Vorlauf"), 50); AddField(fields, "Danach", WithUnit(_after, "Sekunden Nachlauf"), 50); var summaryCard = Surface(new Padding(14, 8, 14, 8)); summaryCard.BackColor = Input; summaryCard.Margin = new Padding(0, 4, 0, 4); summaryCard.Controls.Add(_summary); AddWide(fields, summaryCard, 51); AddWide(fields, TextLabel("Die Clip-Grenzen werden an die Aufnahme angepasst.\nDer Schnitt kann wegen Schlüsselbildern etwas abweichen.", 9, Muted), 47); _accept.DialogResult = DialogResult.OK; var cancel = ActionButton("Abbrechen"); cancel.DialogResult = DialogResult.Cancel; AddWide(fields, FeatureDialogFooter(_accept, cancel), 53); Controls.Add(fields); AcceptButton = _accept; CancelButton = cancel;
            _before.Value = 10; _after.Value = 20; _before.ValueChanged += (_, _) => UpdateSummary(); _after.ValueChanged += (_, _) => UpdateSummary(); UpdateSummary();
        }
        private void UpdateSummary() { var range = SelectedRange; _accept.Enabled = range.Duration > TimeSpan.Zero; _summary.Text = _accept.Enabled ? $"{FormatDuration(range.Start)} → {FormatDuration(range.Start + range.Duration)}  ·  {range.Duration.TotalSeconds:0.#} Sekunden" : "Bitte einen Vorlauf oder Nachlauf größer als null wählen."; }
    }
}
