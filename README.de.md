# ScreenDash 1.1

Die Neuerungen und Hinweise zum Update stehen in `NEU-IN-1.1.de.md`.

Eine lokale Bildschirm-Dashcam für Windows 10/11 (64 Bit). Sie nimmt **einen ausgewählten Monitor und den Ton eines Windows-Wiedergabegeräts** auf. Alle Videos bleiben auf deinem PC.

## Starten

1. Falls du die ZIP-Datei verwendest: vollständig entpacken.
   Bei einem Update zuerst die alte App über das Tray-Menü **Beenden** schließen. Vorhandene Aufnahmen und Einstellungen bleiben erhalten.
2. `ScreenDash.exe` doppelt anklicken. Ein .NET-Download ist nicht nötig.
3. Unter **Einstellungen** Bildschirm, Tonausgabe und Aufnahmeordner wählen. **Einstellungen speichern**, dann **Aufnahme starten**.

FFmpeg muss separat installiert sein. Die App sucht `ffmpeg.exe` neben der EXE, unter `tools/ffmpeg.exe` und im Windows-PATH. Installiere eine aktuelle Windows-Ausgabe mit `ddagrab` und Hardware-Encodern, etwa über `winget install --id Gyan.FFmpeg -e`. Anschließend ScreenDash neu öffnen; bei Bedarf `ffmpeg.exe` neben die App legen. FFmpeg ist nicht in diesem Paket enthalten.

## Enthaltene Funktionen

- **Ringpuffer:** fortlaufende MKV-Segmente, standardmäßig fünf Minuten. Bereits abgeschlossene Segmente bleiben unabhängig von späteren Aufnahmen verwendbar. Bei einem Absturz kann das letzte Segment unvollständig sein.
- **Automatische Bereinigung:** ungeschützte Aufnahmen werden nach sieben Tagen gelöscht. Bei erreichtem Speicherlimit werden ältere Aufnahmen auch früher gelöscht. Zeitraum und Limit sind einstellbar; eine volle Woche ist bei kleinem Limit nicht garantiert.
- **Speichergrenzen:** standardmäßig 50 GiB insgesamt, einschließlich geschützter Videos, und mindestens 2 GiB freier Laufwerksspeicher. Prüfung alle zehn Sekunden mit zusätzlichem Puffer. Beim Erreichen der Grenzen werden ältere ungeschützte Dateien entfernt; reicht das nicht, stoppt die Aufnahme. Das Limit ist wegen der Prüfintervalle und variabler Bitrate kein exaktes Byte-Limit.
- **Letzte Minuten sichern:** standardmäßig die letzten zehn Minuten vor automatischem Löschen schützen. Das schließt überlappende Segmente und die gerade geschriebene Datei ein. Der Schutz gilt für ganze Segmente und kann daher einen längeren Zeitraum umfassen. Neben einer geschützten MKV liegt eine `.keep`-Datei; bei manuellem Verschieben beide Dateien zusammen verschieben. Geschützte Dateien werden niemals automatisch gelöscht.
- **Aufnahmeliste:** Dateien abspielen, schützen/freigeben und abgeschlossene Segmente ohne Neukodierung als MP4 exportieren. Exporte liegen außerhalb des Ringpuffers und werden nicht automatisch gelöscht. Die originale MKV bleibt erhalten.
- **Archivverwaltung:** nach Bezeichnung, Dateiname, Zeitraum und Schutzstatus filtern; mehrere Dateien gemeinsam sichern/freigeben oder nach Bestätigung löschen. Die laufende Datei kann nicht gelöscht werden. Eigene Bezeichnungen liegen in einer zugehörigen `.note.json`-Datei.
- **Manuelle Pause:** pausieren und bewusst fortsetzen. Eine manuelle Pause bleibt auch nach dem Entsperren des PCs erhalten. Fortsetzen erzeugt einen neuen Abschnitt.
- **Tray und Tastenkürzel:** im Hintergrund bedienen, ohne ein großes Fenster offen zu lassen. Schließen während einer Aufnahme minimiert in den Tray. Über **Beenden** im Tray wird die Aufnahme beendet und die App geschlossen.
- **PC-Sperre und Standby:** beim Sperren standardmäßig pausieren; nach dem Entsperren fortsetzen. Standby unterbricht die Aufnahme ebenfalls. In dieser Zeit gibt es keine Aufnahme.
- **Optionale Automatik:** Windows-Autostart und Aufnahme beim Programmstart getrennt einschalten. Beide sind anfangs ausgeschaltet.
- **Unterbrechungen:** bei einem unerwarteten Aufnahmefehler bis zu drei automatische Neustartversuche. Fehler sind im lokalen Protokoll nachvollziehbar.

| Tastenkürzel | Aktion |
| --- | --- |
| Strg + Alt + F8 | Aufnahme starten / stoppen |
| Strg + Alt + F9 | Letzte eingestellte Minuten schützen |
| Strg + Alt + F10 | Aufnahme pausieren / fortsetzen |

## Geringe Belastung

Die Standardeinstellung **Sparsam** verwendet 15 Bilder/s und ungefähr 3 Mbit/s Video. Das ergibt zusammen mit Ton grob **1,4 GB pro Stunde**; tatsächliche Dateigrößen schwanken. Bei Daueraufnahme reicht das Standardlimit damit ungefähr 35–38 Stunden. Für sieben volle Tage werden bei diesem Profil grob 240 GB benötigt. Passe das Limit entsprechend an oder zeichne nur während deiner Nutzung auf.

Desktop Duplication liest den ausgewählten Bildschirm über DirectX aus. Die App prüft einen funktionierenden Hardware-Encoder: AMD AMF, NVIDIA NVENC oder Intel Quick Sync. **CPU-Encoding ist standardmäßig deaktiviert.** Wenn kein Hardware-Encoder funktioniert, zeigt die App einen Fehler. Du kannst CPU-Encoding ausdrücklich erlauben; es kann Spiele und andere Programme deutlich stärker belasten. Der Modus **Software** ist ebenfalls eine bewusste Wahl für CPU-Encoding.

Es gibt keine laufende Videovorschau. Die Oberfläche aktualisiert sich alle zwei Sekunden, Audio verwendet einen begrenzten Speicherpuffer, und der Encoder läuft mit niedriger Prozesspriorität. Dennoch benötigen Bildschirmaufnahme, Encoder und Dateischreiben Rechenleistung, GPU-Ressourcen und Speicherbandbreite. Die Anzeige misst die CPU-Last von App und Encoder gemeinsam sowie deren Arbeitsspeicher. Die GPU-Last wird nicht gemessen. Eine bestimmte Gaming-FPS oder völlig lastfreie Aufnahme kann nicht zugesichert werden.

## Ton und Grenzen

Der Systemton wird über WASAPI vom ausgewählten Wiedergabegerät aufgenommen, beispielsweise Lautsprecher oder HDMI/DisplayPort-Audio. Ein Monitor ohne eigenes Audiogerät liefert keinen separaten Ton. **Windows-Standardausgabe** wird beim Aufnahmestart aufgelöst; wechselst du später das Standardgerät, Aufnahme stoppen und erneut starten oder das gewünschte Gerät ausdrücklich wählen. Ein Mikrofon wird nicht aufgenommen.

Die Aufnahme erfolgt in der nativen Monitorauflösung. Sehr große oder mehrere GPU-Adapter können mehr Ressourcen beanspruchen. Monitorwechsel, entfernte Desktop-Sitzungen, exklusiv verwendete Audiogeräte oder Geräteunterbrechungen können die Aufnahme stoppen. Geschützte Video-Inhalte und der Windows-Sicherheitsbildschirm sind nicht zuverlässig aufnehmbar. Der Sparmodus ist für Dokumentation gedacht, nicht für flüssige 60-fps-Spielmitschnitte.

## Dateien

Standard-Aufnahmeordner: Windows-Videos-Ordner / `ScreenDash`.

Einstellungen: `%LOCALAPPDATA%\ScreenDash\settings.json`. Protokolle: `%LOCALAPPDATA%\ScreenDash\logs`. Die Bereinigung betrifft nur direkt im Aufnahmeordner liegende Dateien mit dem ScreenDash-Aufnahmenamen. Andere Dateien, Unterordner und Exporte werden nicht bereinigt. Verknüpfte Aufnahmeordner werden für eine eindeutige Speicherverwaltung abgewiesen.

## Quellcode bauen

Das separate Quellcode-Paket enthält C#, die Tests und `build.ps1`. Benötigt werden ein .NET-8-SDK, Internetzugang für die NuGet-Abhängigkeiten und FFmpeg zum Aufnehmen. In PowerShell im entpackten Quellcode-Ordner:

```powershell
.\build.ps1
```

Die portable EXE entsteht unter `dist`. Tests prüfen die Löschregeln, Schutzmarkierungen, Speicherreserve, Pfadgrenzen, Pausenzustände und Speicherplanung ohne das Laufwerk zu füllen. Für einen kurzen realen Bildschirm-/Ton-/Exporttest:

```powershell
.\dist\ScreenDash.exe --smoke-test "$PWD\smoke-test"
```

Dieser Test nimmt den ersten Bildschirm samt Standard-Systemton ungefähr zwölf Sekunden auf und schreibt das Ergebnis in den angegebenen Ordner. Diese Testdateien werden nicht automatisch gelöscht.

Technische Grundlagen: [FFmpeg Desktop Duplication](https://ffmpeg.org/ffmpeg-filters.html#ddagrab), [FFmpeg Segmentierung](https://ffmpeg.org/ffmpeg-formats.html#segment), [Microsoft WASAPI-Loopback](https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording). NAudio und .NET stehen unter eigenen Lizenzen; siehe `Drittanbieter-Lizenzen.txt`.
