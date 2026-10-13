# ScreenDash 1.2

- **Eine durchgehende Datei:** Standardmäßig wird während der laufenden Aufnahme eine einzelne MKV geschrieben. Die Minutengrenze erzeugt keine neuen Dateien mehr. Stoppen, Pause, PC-Sperre oder ein Neustart nach Unterbrechung schließt die aktuelle Datei; Fortsetzen beginnt eine neue. Die bisherige Segmentierung bleibt als optionale Einstellung verfügbar.
- **Flüssiger aktualisieren:** Die Aufnahmeliste lädt im Hintergrund und behält unveränderte Zeilen, Auswahl und Scrollposition. Mehrfaches Aktualisieren wird zusammengefasst. Suche und Filter funktionieren während des Ladens weiter; ein Lesefehler wird im Fenster angezeigt und verwirft die vorhandene Liste nicht.
- **Spielton besser finden:** Die automatische Tonwahl bevorzugt beim Start eine Audioausgabe mit laufendem Ton, beispielsweise ein Headset statt einer stummen HDMI-Ausgabe. Die tatsächlich gewählte Ausgabe und ein einfacher Tonpegel stehen im Dashboard. Ein Fünf-Sekunden-Tontest und der direkte Zugang zum Windows-Lautstärkemixer helfen bei der richtigen Auswahl.
- **Mehr Kontrolle im Archiv:** Sortieren nach Zeitpunkt, Dateigröße, Bezeichnung, Schutz und Status. Filter zurücksetzen, präzisere Kontextmenüs und Tastenkürzel für Suche, Benennung, Sicherung und Export.
- **Abbrüchbarer Export:** MP4-Export lässt die Aufnahmebuttons nutzbar. Abbrechen beendet den Exportprozess und entfernt dessen temporäre Datei; die Originalaufnahme bleibt erhalten. Vorhandene Exportdateien werden nicht überschrieben.
- **Klarere Einstellungen:** Ungespeicherte Änderungen sind sichtbar. Speichern/Verwerfen wird nur bei Änderungen angeboten; Strg + S speichert. Nicht mehr verfügbare Geräte werden angezeigt, ohne stillschweigend eine andere Quelle auszuwählen.
- **Weniger wiederholte Dateizugriffe:** Archivscans nutzen die Metadaten einer Verzeichnisabfrage; unveränderte Notizen werden zwischengespeichert. Schutz und Löschentscheidungen bleiben frisch geprüft.
- **Zuverlässiger Aufnahmebeginn und Stopp:** Nach der Encoderprüfung wird der Bildschirm nochmals geprüft. Die Aufnahmedauer folgt einer monotonen Uhr. Prozess- und Exportabbrüche sind zeitlich begrenzt und gegen hängenbleibende Encoder geprüft.

## Update

Die alte App über das Tray-Menü **Beenden** schließen. Die neue ZIP vollständig in einen neuen Ordner entpacken und `ScreenDash.exe` starten. Bestehende Aufnahmen, Schutzmarkierungen und Einstellungen werden weiter verwendet. Auch ältere Einstellungen ohne Dateimodus verwenden ab Version 1.2 die durchgehende Datei.

Die bisherige automatische/Standard-Tonwahl wird als **Automatisch · aktive Audioausgabe** geführt. Explizit gewählte Geräte bleiben ausgewählt. Wer nur den Windows-Standard aufnehmen möchte, wählt die separate Option **Windows-Standardausgabe**.

Beim Ton gilt: Starte das Spiel vor dem Aufnahmestart oder wähle dessen Audioausgabe ausdrücklich. Automatische Wahl mischt nicht mehrere Geräte und wechselt die Tonquelle nicht mitten in einer Aufnahme. Ändert das Spiel sein Ausgabegerät, Aufnahme stoppen, Gerät neu wählen und wieder starten. Mikrofone und exklusive Audiostreams werden nicht aufgenommen.

Bei durchgehender Aufnahme sichert die Schaltfläche die **gesamte Datei**. Automatische Bereinigung löscht ausschließlich geschlossene, ungeschützte Dateien. Eine noch laufende große Datei wird nicht zerteilt oder gelöscht; bei erreichtem Speicherlimit stoppt die Aufnahme. Für dauerhaftes Aufzeichnen mit rollendem Platzmanagement kann die optionale Segmentierung sinnvoll sein.
