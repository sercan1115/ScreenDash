# ScreenDash 1.7.1 – 19. Oktober 2026

## Klarere Qualitätsanzeigen

- Das Dashboard zeigt die native Auflösung, gewünschte FPS und Zielbitrate der gespeicherten Auswahl. Bildschirm, Ton und Dateimodus bleiben über die Quellenanzeige zugänglich.
- Die große FPS-Zahl während der Aufnahme verwendet die jüngsten Bildzuwächse statt des Durchschnitts seit Aufnahmestart. Die gewünschte Bildrate steht separat dabei. Dafür läuft kein zusätzlicher Timer und es wird keine Videovorschau erzeugt.
- Nach **Geräte aktualisieren** folgen Qualitäts- und Speicherprognosen sofort der aktuellen Monitorauflösung. Ist der gewählte Monitor getrennt, verschwinden alte Prognosen. Ungespeicherte Einstellungen bleiben erhalten.
- Änderungen am optionalen Mikrofon werden bereits im Entwurf in den Speicherbedarf eingerechnet.

## Zuverlässiger und schneller starten

- Die Spielerkennung prüft das Spiel nach der Encoderprüfung unmittelbar vor Aufnahmebeginn erneut. Wurde das Spiel während der Prüfung geschlossen, in den Hintergrund gewechselt oder lässt sich sein Zustand nicht zuverlässig prüfen, beginnt keine automatische Aufnahme. Die Automatik kann beim nächsten passenden Spielzustand erneut starten.
- Wird der automatische Erststart vor dem ersten Aufnahmebild durch Sperre, Standby oder manuelle Pause unterbrochen, bleibt die erneute Spielprüfung auch beim Fortsetzen erforderlich. Stoppen wartet nicht auf eine langsame Spielabfrage. Die Bildschirmzuordnung wird nach der letzten Spielprüfung erneut validiert.
- Manuelles Starten und Fortsetzen sowie Alt-Tab während einer bereits gestarteten Aufnahme behalten ihr bisheriges Verhalten. Manueller Stopp bleibt vorrangig.
- Der zuletzt funktionierende Hardware-Encoder wird während derselben App-Sitzung bei gleicher Konfiguration zuerst erneut geprüft. Das spart wiederholte fehlgeschlagene Prüfungen anderer Encoder, besonders beim Fortsetzen einer Aufnahme. Eine frische Funktionsprüfung bleibt immer erhalten.
- Änderungen an FFmpeg, Monitor oder Qualitätswerten machen die bevorzugte Auswahl ungültig. Funktioniert der bevorzugte Encoder nicht mehr, wird die normale Auswahl geprüft. CPU-Kodierung bleibt eine ausdrückliche Wahl.

Die sehr hohe Qualität aus Version 1.7.0 bleibt erhalten: 60 FPS, native Auflösung und automatische Bitrate. Einstellungen, Aufnahmeprofile, Speicherregeln und vorhandene Videos werden durch dieses Update nicht ersetzt.

## Update

Die alte App über das Menü neben der Windows-Uhr **Beenden**, die ZIP vollständig entpacken und die neue EXE öffnen. Die eigene Desktop-Verknüpfung wird aktualisiert. Alternativ **Werkzeuge → Updates** verwenden. FFmpeg bleibt separat erforderlich. Die EXE ist weiterhin unsigniert; Hinweise dazu stehen in `SIGNIERUNG.de.md`.

591 automatische Prüfungen bestanden. Sie verwenden künstliche Fortschritts-, Geräte- und Spielzustände. Es wird dafür kein Bildschirm oder Ton aufgenommen. Die jüngsten FPS sind eine Durchsatzanzeige, keine Messung von Frame-Timing oder Gaming-FPS. Einzelheiten stehen in `TESTBERICHT.de.md`.
