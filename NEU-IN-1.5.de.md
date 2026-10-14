# ScreenDash 1.5

## Gestaltung wie bei Nordpoint Systems

Die Oberfläche greift die Gestaltung der Nordpoint-Systems-Seite auf: warme Papier- und Steinfarben, eine dunkle Navigation, Indigo-Akzente, schmale Linien, nummerierte Bereiche und kursive Serifenschrift für die großen Überschriften. Aufnahme, Archiv, Einstellungen, Werkzeuge und Dialoge verwenden dieselbe Gestaltung. Die Sitzungsdauer ist klar hervorgehoben.

Die kurzen Hover- und Seitenübergänge bleiben erhalten. Sie enden nach der Bedienung sowie beim Minimieren. Die geometrischen Formen werden direkt mit Windows-Zeichenfunktionen gezeichnet; es gibt keine laufende Hintergrundanimation oder Videovorschau.

## Klarere Bedienung

- **Tonquellen richtig anzeigen:** das Dashboard unterscheidet Systemton, Mikrofon, beide Quellen und eine Aufnahme ohne Ton. Mikrofonaufnahmen werden nicht mehr fälschlich als „Ohne Ton“ bezeichnet.
- **Kodierung nachvollziehen:** die Anzeige unterscheidet Hardware- und Software-Kodierung. Der feste Hardware-Hinweis erscheint nicht mehr bei einer Aufnahme mit CPU-Encoding.
- **Ungespeicherte Änderungen erkennen:** Navigation und das ruhende Dashboard zeigen einen offenen Einstellungsentwurf. Ein Aufnahmestart nutzt die gespeicherten Einstellungen; Änderungen werden mit **Speichern** übernommen oder mit **Verwerfen** zurückgenommen.
- **Archivaktionen verstehen:** bei einer noch laufenden Datei erklären die Hinweise, warum Wiedergabe oder Export noch nicht verfügbar sind. Bei mehreren ausgewählten Dateien ist klar, welche Aktionen eine einzelne Aufnahme benötigen.
- **Bessere Tastatur- und Screenreader-Bedienung:** beschriftete Eingabefelder und Einheiten erhalten passende zugängliche Namen und Beschreibungen, auch bei verschachtelten Zahlenfeldern.
- **Updatewechsel verständlich:** **Jetzt wechseln** startet die neue Version automatisch. Der Versionsordner bleibt für spätere Starts verfügbar. Die eigene Desktop-Verknüpfung wird beim neuen Start angepasst.

Aufnahmeformat, Audioverarbeitung, Hardware-Encoding und Speicherregeln bleiben erhalten. Es wird kein zusätzlicher Hintergrunddienst gestartet. Eine konkrete Spieleleistung oder eine lastfreie Aufnahme wird weiterhin nicht zugesichert.

Die EXE ist weiterhin unsigniert, da noch keine verifizierte Signieridentität vorhanden ist. Windows-SmartScreen kann warnen. Der echte Signierweg ist in [SIGNIERUNG.de.md](SIGNIERUNG.de.md) vorbereitet.

Zum Wechsel die alte Version über **Beenden** im Infobereich schließen, die neue ZIP vollständig entpacken und `ScreenDash.exe` starten. Einstellungen und Aufnahmen bleiben erhalten. FFmpeg und FFprobe sind weiterhin separat erforderlich.
