namespace ScreenDash;

public sealed partial class MainForm
{
    private static Form FeatureDialog(string title, Size size) => new() { Text = title, ClientSize = size, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, BackColor = Canvas, ForeColor = Ink, Font = new Font("Segoe UI", 10), ShowInTaskbar = false, AutoScaleMode = AutoScaleMode.Dpi };
    private static string? PromptFeatureText(IWin32Window owner, string title, string hint, string current, int maximum = 120)
    {
        using var dialog = FeatureDialog(title, new Size(500, 195)); var grid = Grid(1, 3); grid.Padding = new Padding(22); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 43)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); grid.Controls.Add(TextLabel(hint, 9, Muted), 0, 0); var entry = Entry(); entry.Text = current; entry.MaxLength = maximum; grid.Controls.Add(entry, 0, 1); var save = ActionButton("Speichern", true); save.DialogResult = DialogResult.OK; var cancel = ActionButton("Abbrechen"); cancel.DialogResult = DialogResult.Cancel; grid.Controls.Add(FeatureActions(save, cancel), 0, 2); dialog.Controls.Add(grid); dialog.AcceptButton = save; dialog.CancelButton = cancel; dialog.Shown += (_, _) => { entry.Focus(); entry.SelectAll(); }; return dialog.ShowDialog(owner) == DialogResult.OK ? entry.Text.Trim() : null;
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
        private readonly ListBox _list = new() { Dock = DockStyle.Fill, BackColor = Input, ForeColor = Ink, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 11), IntegralHeight = false, ItemHeight = 30 };
        private readonly Button _rename = ActionButton("Benennen"), _deleteBookmark = ActionButton("Entfernen"), _exportMoment = ActionButton("Moment als Clip …", true), _close = ActionButton("Schließen");
        private readonly Label _hint = TextLabel("", 9, Muted);
        private bool _busy;
        public RecordingBookmark? SelectedBookmark => (_list.SelectedItem as BookmarkListItem)?.Bookmark;
        public BookmarkDialog(IAppController controller, RecordingInfo recording, IReadOnlyList<RecordingBookmark> bookmarks)
        {
            _controller = controller; _recording = recording; Text = "Lesezeichen · " + recording.FileName; ClientSize = new Size(690, 440); MinimumSize = new Size(570, 340); StartPosition = FormStartPosition.CenterParent; BackColor = Canvas; ForeColor = Ink; Font = new Font("Segoe UI", 10); ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
            var grid = Grid(1, 4); grid.Padding = new Padding(22); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); grid.Controls.Add(TextLabel("Deine markierten Momente", 16, Ink, FontStyle.Bold), 0, 0); _list.Margin = new Padding(0, 8, 0, 8); grid.Controls.Add(_list, 0, 1); grid.Controls.Add(_hint, 0, 2); grid.Controls.Add(FeatureActions(_exportMoment, _rename, _deleteBookmark, _close), 0, 3); Controls.Add(grid);
            _list.SelectedIndexChanged += (_, _) => UpdateActions(); _rename.Click += async (_, _) => await RenameAsync(); _deleteBookmark.Click += async (_, _) => await DeleteAsync(); _exportMoment.DialogResult = DialogResult.OK; _close.DialogResult = DialogResult.Cancel; CancelButton = _close; SetBookmarks(bookmarks); UpdateActions(); FormClosing += (_, e) => { if (_busy) e.Cancel = true; };
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
            Text = "Spiele / Programme auswählen"; ClientSize = new Size(540, 470); StartPosition = FormStartPosition.CenterParent; BackColor = Canvas; ForeColor = Ink; Font = new Font("Segoe UI", 10); ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
            var grid = Grid(1, 3); grid.Padding = new Padding(22); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 51)); grid.Controls.Add(TextLabel("Wähle den Prozess deines Spiels. Gespeicherte Namen bleiben sichtbar,\nauch wenn das Spiel gerade nicht läuft.", 9, Muted), 0, 0); grid.Controls.Add(_list, 0, 1);
            var selected = existing.ToHashSet(StringComparer.OrdinalIgnoreCase); foreach (var name in running.Concat(existing).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)) _list.Items.Add(name, selected.Contains(name)); var accept = ActionButton("Auswahl übernehmen", true); accept.DialogResult = DialogResult.OK; var cancel = ActionButton("Abbrechen"); cancel.DialogResult = DialogResult.Cancel; grid.Controls.Add(FeatureActions(accept, cancel), 0, 2); Controls.Add(grid); AcceptButton = accept; CancelButton = cancel;
        }
    }

    private sealed class TimeSearchDialog : Form
    {
        private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "dd.MM.yyyy", Dock = DockStyle.Fill };
        private readonly DateTimePicker _time = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm:ss", ShowUpDown = true, Dock = DockStyle.Fill };
        public DateTime SelectedTime => DateTime.SpecifyKind(_date.Value.Date + _time.Value.TimeOfDay, DateTimeKind.Local);
        public TimeSearchDialog()
        {
            Text = "Aufnahme nach Uhrzeit finden"; ClientSize = new Size(505, 240); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent; BackColor = Canvas; ForeColor = Ink; Font = new Font("Segoe UI", 10); ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
            var fields = Grid(2, 0); fields.Padding = new Padding(22); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); AddWide(fields, TextLabel("Wann ist der Moment passiert?", 15, Ink, FontStyle.Bold), 36); AddField(fields, "Datum", _date); AddField(fields, "Uhrzeit", _time); AddWide(fields, TextLabel("Lokale Windows-Zeit. Bei einem Treffer kannst du einen Clip exportieren.", 9, Muted), 35); var find = ActionButton("Moment suchen", true); find.DialogResult = DialogResult.OK; var cancel = ActionButton("Abbrechen"); cancel.DialogResult = DialogResult.Cancel; AddWide(fields, FeatureActions(find, cancel), 40); Controls.Add(fields); AcceptButton = find; CancelButton = cancel;
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
            Text = "Clip um deinen Moment"; ClientSize = new Size(565, 330); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; StartPosition = FormStartPosition.CenterParent; BackColor = Canvas; ForeColor = Ink; Font = new Font("Segoe UI", 10); ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
            var fields = Grid(2, 0); fields.Padding = new Padding(22); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); AddWide(fields, TextLabel(string.IsNullOrWhiteSpace(label) ? "Markierter Moment" : label, 15, Ink, FontStyle.Bold), 36); AddWide(fields, TextLabel($"Position {FormatDuration(_offset)}  ·  Aufnahme {FormatDuration(total)}", 9, Muted), 32); AddField(fields, "Davor", WithUnit(_before, "Sekunden Vorlauf")); AddField(fields, "Danach", WithUnit(_after, "Sekunden Nachlauf")); AddWide(fields, _summary, 35); AddWide(fields, TextLabel("Grenzen werden an die Aufnahme angepasst. Kopieren ohne Neukodierung;\nder Schnitt kann wegen Schlüsselbildern etwas abweichen.", 9, Muted), 47); _accept.DialogResult = DialogResult.OK; var cancel = ActionButton("Abbrechen"); cancel.DialogResult = DialogResult.Cancel; AddWide(fields, FeatureActions(_accept, cancel), 44); Controls.Add(fields); AcceptButton = _accept; CancelButton = cancel;
            _before.Value = 10; _after.Value = 20; _before.ValueChanged += (_, _) => UpdateSummary(); _after.ValueChanged += (_, _) => UpdateSummary(); UpdateSummary();
        }
        private void UpdateSummary() { var range = SelectedRange; _accept.Enabled = range.Duration > TimeSpan.Zero; _summary.Text = _accept.Enabled ? $"{FormatDuration(range.Start)} → {FormatDuration(range.Start + range.Duration)}  ·  {range.Duration.TotalSeconds:0.#} Sekunden" : "Bitte einen Vorlauf oder Nachlauf größer als null wählen."; }
    }
}
