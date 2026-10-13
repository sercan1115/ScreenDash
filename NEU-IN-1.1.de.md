# ScreenDash 1.1

## Oberfläche

Neue Seitenleiste, abgerundete Karten, klare Aufnahmeaktionen und ein eigenes Windows-App-Symbol. Quelle, Qualität, Speicher und Automatik sind getrennte Einstellungsbereiche. Hinweise zur geplanten Speicherdauer zeigen früh, wenn das Limit keine ganze Woche Aufnahme abdeckt.

## Neue Funktionen

- **Pausieren und fortsetzen:** ohne die Aufnahmesitzung aufzugeben. Während der Pause schreibt der Encoder keine Videodatei. Fortsetzen beginnt einen neuen Abschnitt. Eine manuelle Pause bleibt beim Sperren/Entsperren und bei Standby erhalten.
- **Aufnahme-Archiv:** Suche, Zeitraum und Schutzstatus filtern. Mehrere Dateien auswählen und gemeinsam sichern, freigeben oder bewusst löschen. Die aktuelle Datei lässt sich nicht löschen; geschützte Aufnahmen erfordern eine zusätzliche Bestätigung.
- **Eigene Bezeichnungen:** wichtige Ereignisse mit bis zu 120 Zeichen beschriften. Der technische Dateiname bleibt erhalten, damit Ringpuffer und Löschregeln weiterhin zuverlässig funktionieren. Bezeichnungen liegen neben der MKV in einer `.note.json`-Datei; bei manuellem Verschieben mitnehmen.
- **Aufnahmeprofile:** zusätzlicher Ultrasparmodus mit zehn Bildern/s und 1,5 Mbit/s; eigene Bildrate und Bitrate einstellbar.
- **Speicherplanung:** ungefährer Verbrauch pro Stunde, mögliche Pufferdauer und Speicherbedarf für die gewünschte Aufbewahrung. Das sind Schätzungen aus der eingestellten Bitrate einschließlich Ton und kleiner Containerreserve; echte Dateigrößen schwanken. Geschützte Videos und freie Laufwerksreserve werden berücksichtigt.
- **Bedienung:** optional beim Aufnahmestart minimieren und Tray-Hinweise ausschalten. Strg + Alt + F10 pausiert oder setzt fort; F8 startet/stoppt und F9 sichert die letzten Minuten wie bisher.

## Technische Verbesserungen

Alte Neustartversuche können eine spätere neue Aufnahmesitzung nicht mehr unterbrechen. Wiederholtes Fortsetzen bei laufender Aufnahme bleibt ohne Nebenwirkung. CPU-Messwerte setzen den Encoder-Anteil bei einem Prozesswechsel neu an. Während der Aufnahme wird der Speicher weiterhin alle zehn Sekunden geprüft; im Leerlauf nur einmal pro Minute. Archivfilter arbeiten mit der bereits geladenen Liste.

## Aktualisieren

1. Die alte Version über das Tray-Menü **Beenden** schließen.
2. Die neue ZIP vollständig entpacken und `ScreenDash.exe` starten.
3. Bildschirm, Ton und Aufnahmeordner in den Einstellungen prüfen.

Vorhandene Einstellungen und Aufnahmen werden weiterverwendet. Die neuen Optionen erhalten Standardwerte. Wenn Windows-Autostart eingeschaltet war, in der neuen Version einmal **Einstellungen speichern**, damit der Startpfad auf die neue EXE zeigt. Das Paket verändert den Autostart nicht selbstständig.
