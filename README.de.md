# ScreenDash 1.7.0

Die Neuerungen und Hinweise zum Update stehen in `NEU-IN-1.7.0.de.md`.

Eine lokale Bildschirm-Dashcam für Windows 10/11 (64 Bit). Sie nimmt **einen ausgewählten Monitor und den Ton eines Windows-Wiedergabegeräts** auf. Alle Videos bleiben auf deinem PC.

## Starten

1. Falls du die ZIP-Datei verwendest: vollständig entpacken.
   Bei einem Update zuerst die alte App über das Tray-Menü **Beenden** schließen. Vorhandene Aufnahmen und Einstellungen bleiben erhalten.
2. `ScreenDash.exe` doppelt anklicken. Das Fenster erscheint und eine eigene Desktop-Verknüpfung wird angelegt. Ein .NET-Download ist nicht nötig.
3. Unter **Einstellungen** Bildschirm, Tonausgabe und Aufnahmeordner wählen. **Einstellungen speichern**, dann **Aufnahme starten**.

FFmpeg muss separat installiert sein. Die App sucht `ffmpeg.exe` neben der EXE, unter `tools/ffmpeg.exe` und im Windows-PATH. Installiere eine aktuelle Windows-Ausgabe mit `ddagrab` und Hardware-Encodern, etwa über `winget install --id Gyan.FFmpeg -e`. Anschließend ScreenDash neu öffnen; bei Bedarf `ffmpeg.exe` neben die App legen. FFmpeg ist nicht in diesem Paket enthalten.

Die veröffentlichte EXE ist derzeit **nicht digital signiert**. Windows-SmartScreen kann deshalb „Der Computer wurde durch Windows geschützt“ beziehungsweise einen unbekannten Herausgeber anzeigen. Eine echte Codesignatur benötigt eine geprüfte Herausgeberidentität; der Build-Weg ist in [SIGNIERUNG.de.md](SIGNIERUNG.de.md) vorbereitet. Auch neue signierte Dateien können zunächst warnen. [Microsoft: SmartScreen-Reputation](https://learn.microsoft.com/de-de/windows/apps/package-and-deploy/smartscreen-reputation)

Minimieren behält das Programm in der Taskleiste. Ein erneuter Doppelklick öffnet das laufende Fenster. Das eigene Desktop-Symbol wird nach einem Update auf die neue EXE angepasst und lässt sich im Menü neben der Windows-Uhr über **Desktop-Verknüpfung erstellen** wiederherstellen. Eine fremde oder beschädigte gleichnamige Verknüpfung wird nicht ersetzt; falls der Desktop nicht beschreibbar ist, bleibt die App über Taskleiste und Infobereich bedienbar. Die Startverknüpfung enthält keine Aufnahmeautomatik; dafür gelten deine gespeicherten Einstellungen.

## Enthaltene Funktionen

- **Sehr hohe Videoqualität:** unter **Einstellungen → Qualität** das empfohlene Profil mit 60 FPS auswählen und speichern. Native Auflösung, H.264 High und hochwertige Hardware-Presets erhalten mehr Details. Die automatische Bitrate richtet sich nach Monitorauflösung, Bildrate und Kodierqualität. Die Seite zeigt die Zielbitrate und den geschätzten Platzbedarf sofort an. Alternativ lassen sich FPS und bis zu 100.000 kbit/s manuell einstellen. Bestehende manuelle Bitraten werden beim Update erhalten; Aufnahmeprofile speichern die neue Qualitätsauswahl mit.
- **Bewegtes Schwarz und Silber:** gefüllte silberne Bänder mit wandernden Lichtkanten und kleinen Lichtpunkten in Seitenleiste, Kopfbereich und Dashboard, dunkle Karten, metallische Schaltflächen und Serifenschrift. Unter **Einstellungen → Automatik** den Hintergrund sofort an- oder ausschalten und speichern. Im Menü neben der Windows-Uhr lässt sich die Bewegung auch während einer Aufnahme umschalten; zum Behalten anschließend die Einstellungen speichern. Beim Minimieren oder Verstecken sowie bei abgeschalteten Windows-Animationen oder hohem Kontrast stoppt der Hintergrundtimer. Hover und Fokus gehen weich ineinander über, Schaltflächen reagieren mit sanftem Druckeffekt. Seiten und Reiter erscheinen mit einem kurzen Überblenden und gleiten leicht an ihren Platz; ihre Klickflächen bleiben stabil. Alle Bedienanimationen enden nach höchstens 240 ms. Das Dashboard passt bei 1024 × 720 ohne vertikales Scrollen; kleinere Fenster können scrollen.
- **Klare Anzeigen und Eingaben:** das Dashboard benennt Systemton, Mikrofon und den tatsächlichen Encoder korrekt. Ungespeicherte Einstellungsänderungen sind auch nach dem Seitenwechsel sichtbar. Ein Aufnahmestart verwendet die gespeicherten Werte. Beschriftete Eingabefelder besitzen passende zugängliche Namen und Einheiten.
- **Lesezeichen:** während der Aufnahme mit **Strg + Alt + F11** einen Moment markieren. Die ganze zugehörige Datei wird geschützt. Unter **Aufnahmen → Lesezeichen** benennen, entfernen oder nach dem Stoppen/Pausieren einen Clip mit wählbarer Zeit vor und nach dem Moment exportieren. Lesezeichen liegen in einer `.bookmarks.json` neben der MKV; beim Verschieben mitnehmen. Entfernen eines Lesezeichens hebt den Dateischutz nicht auf.
- **Spielerkennung:** unter **Werkzeuge → Spielerkennung** genaue Programmnamen per Fensterauswahl oder **Spiel-EXE hinzufügen** wählen und ausdrücklich einschalten. Ein neuer Start erfordert die ausgewählte EXE mit sichtbarem Fenster im Vordergrund in deiner Windows-Sitzung; bekannte Launcher und Hilfsprogramme sind ausgeschlossen. Eine vorhandene Einstellung **Aufnahme beim Programmstart** wird bei aktivierter Spielerkennung ausgeschaltet. Die Statuszeile erklärt, worauf die Automatik wartet. Prüfung etwa alle sechs Sekunden. Alt-Tab oder Minimieren beendet eine laufende Aufnahme nicht. Nach Spielende wird eine von der Automatik gestartete Aufnahme nach mindestens zehn Sekunden Wartezeit geschlossen. Manuell gestartete Aufnahmen werden durch Spielende nicht gestoppt. Nach manuellem Stopp startet die Automatik erst wieder, wenn alle ausgewählten Spiele beendet waren. PC-Sperre, Standby, Speichergrenzen und Timer gelten weiterhin.
- **Eigene Aufnahmeprofile:** unter **Werkzeuge → Aufnahmeprofile** Bildschirm, Audio, Mikrofon, Encoder und Qualität speichern. Anwenden setzt die Aufnahme-Einstellungen bei gestoppter Aufnahme; Speicherregeln, Autostart und Spielerkennung bleiben erhalten.
- **Tonwarnungen:** bei längerer Stille erscheint ein Hinweis auf das Wiedergabegerät. Wartezeit 10–600 Sekunden, standardmäßig 30. Stille ist kein Nachweis eines Fehlers; bei Bedarf ausschalten. Gerätefehler werden gesondert angezeigt.
- **Optionales Mikrofon:** ausdrücklich in den Audio-Einstellungen einschalten. Spielton und Mikrofon lassen sich einzeln auf 0–200 % stellen und werden in einer gemeinsamen AAC-Tonspur gespeichert. Mikrofon allein ist ebenfalls möglich. Windows muss Mikrofonzugriff für Desktop-Apps erlauben; ein Limiter begrenzt Übersteuerung.
- **Updates:** unter **Werkzeuge → Updates** manuell bei GitHub prüfen, herunterladen, abbrechen und nach Prüfsummenprüfung zur neuen Version wechseln. Eine laufende Aufnahme wird zuvor geschlossen, ein Export abgebrochen. Die neue portable EXE startet aus einem eigenen Ordner unter `%LOCALAPPDATA%\ScreenDash\updates`; Einstellungen und Videos bleiben erhalten. Aktivierter Windows-Autostart und die eigene Desktop-Verknüpfung werden angepasst. Andere manuelle Verknüpfungen zeigen weiter auf die alte EXE. Die App prüft nicht automatisch im Hintergrund auf Updates.
- **Datum und Uhrzeit:** unter **Aufnahmen → Zeitpunkt** einen Moment eingeben, die passende Datei finden und einen Clip vorbereiten. Es zählen lokale Uhrzeit und der gespeicherte Aufnahmebeginn; Zeitsprünge oder manuell veränderte Dateizeiten können die Zuordnung verschieben. Für Medienmetadaten ist FFprobe erforderlich.

- **Clip-Werkzeug:** abgeschlossene Aufnahmen als Ausschnitt kopieren; Start und Länge wählen oder die letzten 30 Sekunden, eine Minute oder fünf Minuten verwenden. Bild und Ton werden ohne Neukodierung exportiert. Schlüsselbilder können den Schnitt etwas verschieben. FFprobe aus dem vollständigen FFmpeg-Paket ist dafür erforderlich.
- **Aufnahme-Timer:** optional nach 1 bis 1440 aktiven Aufnahmeminuten stoppen. Pausen zählen nicht. Der sichtbare Countdown wird alle zwei Sekunden geprüft. Standardmäßig ausgeschaltet.
- **Archivseiten:** 200 Dateien pro Seite; Suche und Filter finden auch Dateien auf anderen Seiten. Mehrfachauswahl gilt auf der sichtbaren Seite. Stopp/Pause warten nicht auf den eigentlichen Archivscan oder die laufende regelmäßige Speicherprüfung.

- **Durchgehende Datei:** standardmäßig eine MKV pro ununterbrochener Aufnahme, ohne Wechsel nach einer Minute. Stoppen oder Pausieren schließt sie ab; Fortsetzen erzeugt eine neue Datei. Optional kann die bisherige Segmentierung eingeschaltet werden. Bei einem Absturz kann die letzte Datei unvollständig sein.
- **Automatische Bereinigung:** abgeschlossene ungeschützte Aufnahmen werden sieben Tage nach ihrem letzten Schreiben gelöscht. Bei erreichtem Speicherlimit werden ältere Aufnahmen auch früher gelöscht. Zeitraum und Limit sind einstellbar; eine volle Woche ist bei kleinem Limit nicht garantiert.
- **Speichergrenzen:** standardmäßig 50 GiB insgesamt, einschließlich geschützter Videos, und mindestens 2 GiB freier Laufwerksspeicher. Prüfung alle zehn Sekunden mit zusätzlichem Puffer. Beim Erreichen der Grenzen werden ältere ungeschützte Dateien entfernt; reicht das nicht, stoppt die Aufnahme. Das Limit ist wegen der Prüfintervalle und variabler Bitrate kein exaktes Byte-Limit.
- **Letzte Minuten sichern:** Aufnahmen aus den letzten zehn Minuten vor automatischem Löschen schützen. Bei durchgehender Aufnahme wird die gesamte aktuelle Datei gesichert. Das schließt überlappende Segmente und die gerade geschriebene Datei ein. Der Schutz gilt für ganze Segmente und kann daher einen längeren Zeitraum umfassen. Neben einer geschützten MKV liegt eine `.keep`-Datei; bei manuellem Verschieben beide Dateien zusammen verschieben. Geschützte Dateien werden niemals automatisch gelöscht.
- **Aufnahmeliste:** Dateien abspielen, schützen/freigeben und abgeschlossene Segmente ohne Neukodierung als MP4 exportieren. Exporte liegen außerhalb des Ringpuffers und werden nicht automatisch gelöscht. Die originale MKV bleibt erhalten. Ein Export kann abgebrochen werden; Aufnahme starten, stoppen und pausieren bleibt währenddessen verfügbar.
- **Archivverwaltung:** nach Bezeichnung, Dateiname, Zeitraum und Schutzstatus filtern; mehrere Dateien gemeinsam sichern/freigeben oder nach Bestätigung löschen. Die laufende Datei kann nicht gelöscht werden. Aktualisieren lädt die Liste im Hintergrund; unveränderte Zeilen, Auswahl und Scrollposition bleiben erhalten. Spaltenüberschriften sortieren nach Zeitpunkt, tatsächlicher Größe oder Bezeichnung. Eigene Bezeichnungen liegen in einer zugehörigen `.note.json`-Datei.
- **Manuelle Pause:** pausieren und bewusst fortsetzen. Eine manuelle Pause bleibt auch nach dem Entsperren des PCs erhalten. Fortsetzen erzeugt einen neuen Abschnitt.
- **Taskleiste, Infobereich und Tastenkürzel:** bei minimiertem Fenster im Hintergrund bedienen. Schließen während einer Aufnahme oder eines Exports minimiert in die Taskleiste. Über **Beenden** im Infobereich wird die Aufnahme beendet und die App geschlossen. Animationen und die automatische Aktualisierung der Aufnahmeliste pausieren beim Minimieren.
- **PC-Sperre und Standby:** beim Sperren standardmäßig pausieren; nach dem Entsperren fortsetzen. Standby unterbricht die Aufnahme ebenfalls. In dieser Zeit gibt es keine Aufnahme.
- **Optionale Automatik:** Windows-Autostart und Aufnahme beim Programmstart getrennt einschalten. Beide sind anfangs ausgeschaltet. Bei aktiver Spielerkennung bleibt Aufnahme beim Programmstart ausgeschaltet, damit die App auf ein Spiel wartet.
- **Unterbrechungen:** bei einem unerwarteten Aufnahmefehler bis zu drei automatische Neustartversuche. Fehler sind im lokalen Protokoll nachvollziehbar.

| Tastenkürzel | Aktion |
| --- | --- |
| Strg + Alt + F8 | Aufnahme starten / stoppen |
| Strg + Alt + F9 | Letzte eingestellte Minuten schützen |
| Strg + Alt + F10 | Aufnahme pausieren / fortsetzen |
| Strg + Alt + F11 | Moment markieren und gesamte Datei schützen |

## Geringe Belastung

Neue Einstellungen verwenden **Sehr hohe Qualität mit 60 FPS** und automatischer Bitrate. Die Aufnahme bleibt in nativer Auflösung: ungefähr **33,5 Mbit/s bei 1080p60**, **55 Mbit/s bei 1440p60** und höchstens **100 Mbit/s bei 4K60**. Zusammen mit Ton und Containerreserve entspricht das grob **14,5 / 23,8 / 43,2 GiB pro Stunde**. Das Standardlimit von 50 GiB reicht dafür ungefähr 3,4 / 2,1 / 1,2 Stunden. Bewegung und Encoder verändern die tatsächliche Dateigröße. Für längere Aufbewahrung das Speicherlimit erhöhen, ein Sparprofil wählen oder nur beim Spielen aufnehmen. Sieben Tage Aufbewahrung garantieren keine sieben Tage Daueraufnahme.

Beim Update bleiben vorhandene manuelle Qualitätswerte erhalten. Für die neue Empfehlung **Einstellungen → Qualität → Sehr hohe Qualität · 60 FPS** wählen und speichern. Die automatische Berechnung verwendet den ausgewählten Monitor auch nach einem Auflösungswechsel; die Speicherreserve berücksichtigt zusätzlich die mögliche Bitratenspitze. Eigene Profile aus älteren Versionen behalten ihre manuelle Bitrate.

Desktop Duplication liest den ausgewählten Bildschirm über DirectX aus. Die App prüft einen funktionierenden Hardware-Encoder: AMD AMF, NVIDIA NVENC oder Intel Quick Sync. **CPU-Encoding ist standardmäßig deaktiviert.** Wenn kein Hardware-Encoder funktioniert, zeigt die App einen Fehler. Du kannst CPU-Encoding ausdrücklich erlauben; es kann Spiele und andere Programme deutlich stärker belasten. Der Modus **Software** ist ebenfalls eine bewusste Wahl für CPU-Encoding.

Es gibt keine laufende Videovorschau. Die Aufnahmeanzeigen aktualisieren sich alle zwei Sekunden. Der sichtbare Hintergrund zeichnet nur freie dekorative Flächen mit höchstens 25 Bildern pro Sekunde; er ist abschaltbar und pausiert bei minimiertem oder verstecktem Fenster. Audio verwendet einen begrenzten Speicherpuffer, und der Encoder läuft mit niedriger Prozesspriorität. Dennoch benötigen Bildschirmaufnahme, Encoder und Dateischreiben Rechenleistung, GPU-Ressourcen und Speicherbandbreite. Die Anzeige misst die CPU-Last von App und Encoder gemeinsam sowie deren Arbeitsspeicher. Die GPU-Last wird nicht gemessen. Eine bestimmte Gaming-FPS oder völlig lastfreie Aufnahme kann nicht zugesichert werden.

## Ton und Grenzen

Der Systemton wird über WASAPI vom ausgewählten Wiedergabegerät aufgenommen, beispielsweise Lautsprecher oder HDMI/DisplayPort-Audio. Ein Monitor ohne eigenes Audiogerät liefert keinen separaten Ton. **Automatisch · aktive Audioausgabe** bevorzugt beim Start ein Gerät mit laufendem Ton, sonst den Windows-Standard. Starte das Spiel vorher oder wähle dessen Headset/Lautsprecher ausdrücklich. **Ton testen** prüft fünf Sekunden lang die gewählte Ausgabe; **Mixer öffnen** hilft, die Spielausgabe zu finden. Pegel und tatsächliche Quelle erscheinen während der Aufnahme im Dashboard. Die separate Option **Windows-Standardausgabe** wird beim Aufnahmestart aufgelöst; wechselst du später das Standardgerät, Aufnahme stoppen und erneut starten oder das gewünschte Gerät ausdrücklich wählen. Das optionale Mikrofon ist anfangs ausgeschaltet. Es werden nicht mehrere Wiedergabeausgaben gemischt oder Geräte mitten in einer Aufnahme gewechselt. Exklusiver Audiomodus und geschützte Inhalte können Loopback-Ton verhindern.

Die Aufnahme erfolgt in der nativen Monitorauflösung. Sehr große oder mehrere GPU-Adapter können mehr Ressourcen beanspruchen. Monitorwechsel, entfernte Desktop-Sitzungen, exklusiv verwendete Audiogeräte oder Geräteunterbrechungen können die Aufnahme stoppen. Geschützte Video-Inhalte und der Windows-Sicherheitsbildschirm sind nicht zuverlässig aufnehmbar. Der Sparmodus ist für Dokumentation gedacht, nicht für flüssige 60-fps-Spielmitschnitte.

## Dateien

Standard-Aufnahmeordner: Windows-Videos-Ordner / `ScreenDash`.

Einstellungen: `%LOCALAPPDATA%\ScreenDash\settings.json`. Profile: `%LOCALAPPDATA%\ScreenDash\profiles.json`. Protokolle: `%LOCALAPPDATA%\ScreenDash\logs`. Die Bereinigung betrifft nur direkt im Aufnahmeordner liegende Dateien mit dem ScreenDash-Aufnahmenamen. Andere Dateien, Unterordner und Exporte werden nicht bereinigt. Verknüpfte Aufnahmeordner werden für eine eindeutige Speicherverwaltung abgewiesen.

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
