# ScreenDash 1.4

## Neue Werkzeuge

- Momente während der Aufnahme markieren: Strg + Alt + F11 setzt ein Lesezeichen und schützt die gesamte Datei. Lesezeichen später benennen und Clips mit Zeit vor/nach dem Moment exportieren.
- Spielerkennung mit exakten .exe-Namen: auf Wunsch mit ausgewählten Spielen starten und nach Spielende stoppen. Ein manuelles Stoppen wird bis zum nächsten Spielstart respektiert.
- Eigene Aufnahmeprofile für Bildschirm, Ton und Qualität speichern und bei gestoppter Aufnahme anwenden.
- Auf fehlenden Spielton hinweisen; stille Phasen und Gerätefehler werden angezeigt. Stille-Warnung einstellbar oder abschaltbar.
- Optional ein Mikrofon aufnehmen; Spielton und Stimme getrennt lauter/leiser stellen. Beide Quellen teilen eine Zeitbasis und werden als eine Tonspur gespeichert.
- Updates manuell prüfen, mit Fortschritt laden, abbrechen und nach SHA256-Prüfung zur neuen Version wechseln. Keine automatische Netzwerkprüfung.
- Aufnahme anhand eines Datums und einer Uhrzeit finden und einen passenden Clip vorbereiten.

## Bedienung und Belastung

Die Oberfläche hat jetzt einen Bereich **Werkzeuge** mit Profilen, Spielerkennung und Updates. Aufnahme/Pause/Stopp bleiben beim Laden des Archivs und während Downloads verfügbar. Es gibt weiterhin keine laufende Videovorschau. Die Audioquellen haben jeweils einen festen Zwei-Sekunden-Puffer; die Spielerkennung prüft nur bei Aktivierung etwa alle sechs Sekunden. Qualität und Hardware-Encoding bleiben standardmäßig sparsam. Die neuen Funktionen benötigen dennoch Ressourcen; es wurde kein neuer isolierter Spiele-Benchmark durchgeführt.

## Umsteigen

Die neue ZIP vollständig entpacken. Eine noch laufende ältere ScreenDash-Version über das Tray-Menü **Beenden** schließen, dann die neue EXE öffnen. Globale Einstellungen und Videos bleiben erhalten. Mikrofon und Spielerkennung sind ohne deine Aktivierung ausgeschaltet.

Der eingebaute Updatewechsel startet künftig eine neue portable EXE aus einem eigenen Versionsordner unter `%LOCALAPPDATA%\ScreenDash\updates`. Der alte Programmordner bleibt erhalten; manuelle Desktop-Verknüpfungen bei Bedarf auf den neuen Pfad ändern. Aktivierter Windows-Autostart wird beim Wechsel angepasst. FFmpeg/FFprobe bleiben externe Voraussetzungen; lokale Tools aus dem bisherigen Programmordner werden beim Update übernommen.

Eine ununterbrochene Aufnahme bleibt eine MKV. Pausieren, Stoppen oder ein Gerätefehler schließt sie; Fortsetzen oder automatisches Wiederanlaufen erzeugt eine neue Datei. Lesezeichen schützen die ganze zugehörige Datei und verbrauchen dadurch weiter Speicher. MP4-Clips schneiden ohne Neukodierung; Schlüsselbilder können den tatsächlichen Anfang und die Länge verschieben.
