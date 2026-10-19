# ScreenDash 1.7.0 – 19. Oktober 2026

## Sehr hohe Videoqualität

- Neues empfohlenes Profil: **60 FPS, native Monitorauflösung und hochwertige GPU-Kodierung mit H.264 High**.
- Automatische Bitrate passend zu Auflösung, FPS und Qualitätsstufe: ungefähr **33,5 Mbit/s bei 1080p60**, **55 Mbit/s bei 1440p60**, bis zu **100 Mbit/s bei 4K60**. Keine zusätzliche Verkleinerung oder künstliche Schärfung.
- Qualitätsseite mit sichtbarer Zielbitrate und geschätztem Speicherbedarf. Manuelle Bitrate bis 100.000 kbit/s und Sparprofile bleiben verfügbar.
- Hardware-Presets für NVIDIA NVENC, AMD AMF und Intel Quick Sync. CPU-Kodierung bleibt eine ausdrückliche Wahl. Keine laufende Videovorschau und keine zusätzlichen Analysepuffer für den Qualitätsmodus.
- Qualitätsauswahl wird in eigenen Aufnahmeprofilen gespeichert. Alte Einstellungen und Profile behalten ihre bisherige manuelle Bitrate. Für die neue Qualität das empfohlene Profil unter **Einstellungen → Qualität** wählen und speichern.
- Speicherplanung berücksichtigt die tatsächlich verwendete Auflösung und die mögliche Bitratenspitze. Sehr hohe Qualität benötigt grob 14,5 / 23,8 / 43,2 GiB pro Stunde bei 1080p60 / 1440p60 / 4K60. Das Speicherlimit bleibt erhalten und kann Aufnahmen vor Ablauf der sieben Tage bereinigen.

## Spielerkennung wartet auf das tatsächliche Spiel

- Automatischer Start erst mit der ausgewählten Spiel-EXE und deren sichtbarem Fenster im Vordergrund. Bekannte Launcher und Hilfsprogramme lösen keinen Start aus.
- **Aufnahme beim Programmstart** wird bei aktivierter Spielerkennung deaktiviert. Dadurch startet ScreenDash nicht allein beim Öffnen der App mit einer Aufnahme.
- Spiel-EXE direkt auswählen oder sichtbares Programm übernehmen. Ein Status erklärt, worauf die Erkennung wartet. Die gewählte Datei wird bei der Auswahl nicht ausgeführt.
- Alt-Tab und Minimieren unterbrechen eine laufende Aufnahme nicht. Manuelles Stoppen und Pausieren behalten Vorrang; unzuverlässige Prozessabfragen werden nicht als Spielende gewertet.

## Bewegte Silberlandschaft

- Dunkle, gefüllte Silberbänder mit Lichtkanten und wandernden Lichtpunkten in Seitenleiste, Kopfbereich und Dashboard.
- Wiederverwendete Zeichenressourcen und höchstens 25 geplante Bilder pro Sekunde auf kleinen dekorativen Flächen. Keine neue laufende Videovorschau.
- Minimiert, versteckt, bei hohem Kontrast oder abgeschalteten Windows-Animationen ruht der Hintergrund. Der Schalter unter Automatik und im Infobereich bleibt verfügbar. Weiche Seiten- und Bedienanimationen bleiben erhalten.

## Update und Prüfung

Die alte App über das Menü neben der Windows-Uhr **Beenden**, die ZIP vollständig entpacken und die neue EXE öffnen. Einstellungen und Aufnahmen bleiben erhalten; die eigene Desktop-Verknüpfung wird aktualisiert. Alternativ **Werkzeuge → Updates** verwenden. Die EXE bleibt mangels verifizierter Herausgeberidentität unsigniert; weitere Hinweise stehen in `SIGNIERUNG.de.md`.

509 automatisierte Prüfungen ohne Bildschirm- oder Tonaufnahme bestanden. Ein zusätzlicher künstlicher 1080p60-Vergleich bestätigt höhere Bildtreue gegenüber 3 und 10 Mbit/s. Das ist ein Software-Referenzvergleich und kein Spiele- oder GPU-Leistungstest. Details und Messgrenzen stehen in `TESTBERICHT.de.md`.
