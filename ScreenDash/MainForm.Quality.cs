namespace ScreenDash;

public sealed partial class MainForm
{
    private readonly CheckBox _autoVideoBitrate = Check("Bitrate automatisch an Auflösung und Bildrate anpassen");
    private readonly ComboBox _encodingQuality = Choice();

    private void BuildQualitySettings(TableLayoutPanel fields)
    {
        AddHeading(fields, "Sehr gute Bilder. Flüssige Bewegung.", "60 FPS, native Auflösung und hochwertige GPU-Kodierung für klare Spielaufnahmen.");
        _profile.Items.AddRange(["Ultra sparsam · 10 FPS · 2 Mbit/s", "Sparsam · 15 FPS · automatische Bitrate", "Gaming · 30 FPS · ausgewogen", "Sehr hohe Qualität · 60 FPS (empfohlen)", "Benutzerdefiniert"]);
        _encodingQuality.Items.AddRange(["Effizient · geringe Encoderlast", "Ausgewogen · Qualität und Leistung", "Sehr hohe Qualität · mehr Details"]);
        _encoderChoice.Items.AddRange(["Auto · GPU bevorzugt", "NVIDIA · NVENC", "AMD · AMF", "Intel · Quick Sync", "Software · CPU"]);
        AddField(fields, "Aufnahmeprofil", _profile);
        AddField(fields, "Bildrate", WithUnit(_customFps, "FPS · 60 für flüssige Spielbewegung"));
        AddField(fields, "Kodierqualität", _encodingQuality);
        AddOption(fields, _autoVideoBitrate, 42);
        AddField(fields, "Manuelle Bitrate", WithUnit(_customBitrate, "kbit/s · wird bei automatischer Bitrate nicht verwendet"));
        AddField(fields, "Encoder", _encoderChoice);
        AddOption(fields, _software);
        AddWide(fields, _qualityEstimate, 76);
        AddHelp(fields, "Sehr hohe Qualität benötigt mehr Speicher. Die GPU bleibt bevorzugt; CPU-Kodierung wird nur nach ausdrücklicher Freigabe verwendet.");
        _profile.SelectedIndexChanged += (_, _) => ApplyProfile();
        _customFps.ValueChanged += (_, _) => CustomQualityChanged();
        _customBitrate.ValueChanged += (_, _) => CustomQualityChanged();
        _encodingQuality.SelectedIndexChanged += (_, _) => CustomQualityChanged();
        _autoVideoBitrate.CheckedChanged += (_, _) => { _customBitrate.Enabled = !_autoVideoBitrate.Checked; CustomQualityChanged(); };
        _display.SelectedIndexChanged += (_, _) => UpdateEstimates();
        _audioEnabled.CheckedChanged += (_, _) => UpdateEstimates();
        _tips.SetToolTip(_autoVideoBitrate, "Verwendet die tatsächliche Monitorauflösung. Sehr hohe Qualität: ungefähr 33,5 Mbit/s bei 1080p60, 55 Mbit/s bei 1440p60 und höchstens 100 Mbit/s bei 4K60.");
    }

    private void LoadQualitySettings(AppSettings settings)
    {
        _autoVideoBitrate.Checked = settings.AutoVideoBitrate;
        _customBitrate.Enabled = !settings.AutoVideoBitrate;
        _encodingQuality.SelectedIndex = settings.EncodingQuality switch { "Efficiency" => 0, "Quality" => 2, _ => 1 };
        _profile.SelectedIndex = (settings.Fps, settings.AutoVideoBitrate, settings.EncodingQuality, settings.VideoBitrateKbps) switch
        {
            (10, false, "Efficiency", 2000) => 0,
            (15, true, "Efficiency", _) => 1,
            (30, true, "Balanced", _) => 2,
            (60, true, "Quality", _) => 3,
            _ => 4
        };
    }

    private void ApplyQualityPreset()
    {
        if (_loadingSettings || _profile.SelectedIndex is < 0 or > 3) return;
        _updatingProfile = true;
        try
        {
            int preset = _profile.SelectedIndex;
            SetNumber(_customFps, preset switch { 0 => 10, 1 => 15, 2 => 30, _ => 60 });
            _encodingQuality.SelectedIndex = preset switch { 0 or 1 => 0, 2 => 1, _ => 2 };
            _autoVideoBitrate.Checked = preset != 0;
            SetNumber(_customBitrate, preset == 0 ? 2000 : preset == 3 ? 33500 : 10000);
            _customBitrate.Enabled = !_autoVideoBitrate.Checked;
        }
        finally { _updatingProfile = false; }
        UpdateEstimates();
    }

    private void ReadQualitySettings(AppSettings settings)
    {
        settings.AutoVideoBitrate = _autoVideoBitrate.Checked;
        settings.EncodingQuality = _encodingQuality.SelectedIndex switch { 0 => "Efficiency", 2 => "Quality", _ => "Balanced" };
    }

    private void UpdateQualityEstimates()
    {
        if (_loadingSettings) return;
        var draft = _controller.Settings.Clone();
        draft.Fps = (int)_customFps.Value; draft.VideoBitrateKbps = (int)_customBitrate.Value;
        draft.AudioEnabled = _audioEnabled.Checked; draft.MaxStorageGb = (int)_maxStorage.Value; draft.RetentionDays = (int)_retention.Value;
        ReadQualitySettings(draft);
        var display = _display.SelectedItem as DisplaySource;
        if (display is { Width: > 0, Height: > 0 })
        {
            int bitrate = RecordingQuality.ResolveBitrateKbps(draft, display);
            var estimate = RecordingEstimates.Calculate(draft, display: display);
            _qualityEstimate.Text = $"{display.Width} × {display.Height} · {draft.Fps} FPS · {(draft.AutoVideoBitrate ? "automatisch" : "manuell")} {bitrate / 1000d:0.0} Mbit/s\nGeschätzt {estimate.GiBPerHour:0.0} GB pro Stunde; Bewegung und Encoder verändern die Dateigröße.";
            _quotaEstimate.Text = $"Das Limit reicht ungefähr für {FormatHours(estimate.HoursAtLimit)} Aufnahme.\n{draft.RetentionDays} Tage gelten zusätzlich; hohe Qualität füllt den Speicher früher.";
            _quotaEstimate.ForeColor = estimate.FullRetentionFits ? Cyan : Amber;
        }
        else { _qualityEstimate.Text = "Wähle einen verbundenen Bildschirm für die Qualitäts- und Speicherberechnung."; }
    }
}
