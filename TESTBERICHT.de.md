# ScreenDash – Prüfergebnis

## Version 1.4 – 14. Oktober 2026

- Die automatische Suite umfasst 177 Prüfungen ohne Aufnahme: 45 Speicher-/Einstellungs-/Lesezeichentests, 25 Zustands-/Planungsprüfungen, 32 Prozess-/Medienprüfungen, 7 Controller-Ablaufprüfungen, 34 Profil-/Spielerkennungs-/Updateprüfungen, 16 Audiopuffer-/Fehlerprüfungen und 18 Controller-Funktionsprüfungen. Die 45 Speichertests bestanden im uneingeschränkten Lauf. Beim späteren Lauf in der eingeschränkten Umgebung bestanden 42; drei Prüfungen konnten ihre isolierten NTFS-Junctions wegen verweigerter Berechtigungen nicht anlegen. Diese Tests bleiben für normale Windows-/CI-Läufe verpflichtend.
- 83 Oberfläche-Prüfungen mit künstlichen Daten bestanden: neue Werkzeugtabs, Profilnamen/-anwendung, Mikrofonentwurf/-gerätewahl, manuelle Updateprüfung, Downloadabbruch, neuer Versionsordner, Lesezeichenclips und Auswahl eines Zeittreffers auf späteren Archivseiten. Die Vorschauen enthalten ausschließlich Beispieldaten.
- 25 reale Controllerprüfungen bestanden: Systemtonaufnahme, Live-Lesezeichen mit gültigem Videoversatz, Schutz der ganzen Datei, Pause/Fortsetzen, unveränderte Pausenzeit, Notizen, MP4-/Clip-Export, Suche anhand lokaler Uhrzeit, Umbenennen eines Lesezeichens und Löschen eigener Begleitdateien. Einstellungen und Windows-Autostart wurden nicht verändert.
- Sechs zusätzliche FFmpeg-Prüfungen mit künstlichen Audiosignalen bestanden: Mono-Mikrofon wird in 48-kHz-Stereo gewandelt, stilles Mikrofon senkt den Spielton nicht, Null-Lautstärke erzeugt Stille und der Limiter hält laute gemischte Signale innerhalb der Grenzen. Dabei wurde kein tatsächliches Mikrofon geöffnet.
- Stoppen wartet weder auf einen blockierten Archivscan noch auf eine langsame Spielerkennungs-Abfrage. Veraltete Abfragen können manuelles Stoppen nicht rückgängig machen. Eine manuelle Pause bleibt beim laufenden Spiel bestehen; Spielende beendet ausschließlich die von der Automatik verwaltete Sitzung nach der Wartezeit.
- Updateprüfung/-vorbereitung wurden mit simulierten GitHub-Antworten und ZIP-Dateien geprüft: HTTPS-/Repositorygrenzen, Prüfsummen, Größen, Abbruch, Pfadgrenzen, Links und veränderte Programmdateien. Der Neustarthelper wurde mit echtem Windows PowerShell geparst, jedoch nicht ausgeführt. Keine fremde Installation oder Autostart-Einstellung wurde ersetzt.
- Die portable Windows-x64-Anwendung wurde lokal gebaut. Während der Offline-Wiederherstellung konnten aktuelle NuGet-Sicherheitsmeldungen wegen gesperrtem Netzwerk nicht abgerufen werden. Es gibt keine neuen NuGet-Abhängigkeiten.
- Die fertige EXE startet und erkennt Bildschirm-/Audiogeräte. Der abschließende Aufnahmeversuch innerhalb der später eingeschränkten Umgebung wurde von Windows mit „Desktop duplication access denied“ abgewiesen. Der zuvor erfolgreiche reale Controllerlauf fand noch in der uneingeschränkten Umgebung statt. Die Sandbox-Beschränkung wird nicht durch Software-Encoding umgangen.

Mikrofon-Hardware, physisches Abziehen von Geräten, alle konkreten Spiele sowie der Wechsel zu einer echten zukünftigen Updateversion wurden nicht als Hardware-/Installationstest ausgeführt. Ein neuer isolierter Spiele-/GPU-Benchmark und eine volle Woche Dauerbetrieb wurden nicht gemessen. Die bisherige sparsame Hardware-Aufnahme bleibt bestehen; eine lastfreie Aufnahme wird nicht zugesichert. Reale Testvideos bleiben im privaten Arbeitsordner und werden nicht mitgeliefert.

## Version 1.3 – 14. Oktober 2026

- 96 automatische Prüfungen ohne Aufnahme bestanden: 32 Speicher-/Einstellungstests, 25 Zustands-/Planungs-/Timerprüfungen, 32 Prozess-/Medienprüfungen und 7 Controller-Ablaufprüfungen.
- Die Ablaufprüfungen blockieren einen isolierten Archivordner absichtlich: Stopp wartet nicht auf Archivlesen oder Hintergrundbereinigung. Abbruch beendet einen wartenden Lesescan. Eine simulierte Sperrmeldung setzt die Aufnahmeabsicht und das Startup-Abbruchsignal trotz belegter Steuerungssperre. Eine veraltete Pause beeinflusst keine neue Sitzung. Dabei werden keine Bildschirme oder Audiogeräte aufgenommen.
- 52 UI-Prüfungen mit Beispieldaten bestanden: 1.201 Aufnahmen in Seiten mit höchstens 200 Zeilen, vollständige Suche/Filter, Auswahl und Seitengrenzen, keine Dateiscans beim Paging, Clipauswahl/-bereiche/-Presets, Countdown, Abbruch eines wartenden Exports und Stopp während eines wartenden Starts.
- 22 reale Controllerprüfungen bestanden: Aufnahme mit Systemton, Schutz, Pause/Fortsetzen und eingefrorene Pausenzeit, Notizen, MP4, Metadaten, Clip, Ablehnung ungültiger Bereiche, Auto-Stopp nach einer Minute aktiver Aufnahmezeit und manuelles Löschen. Timerprüfung wartet auf vollständig geschlossene Datei und beendetem Encoder. Durch Pause entstehen zwei unabhängige durchgehende Dateien.
- Medienexport mit vollständig künstlichem Bild/Ton getestet: 8,021 Sekunden H.264/AAC, Wunschbereich Beginn 2,375 Sekunden / Länge 2,25 Sekunden, MP4-Clip tatsächlich 2,666 Sekunden wegen Schlüsselbildausrichtung. Vollständige Dekodierung fehlerfrei, Original-Hash unverändert. Keine neue Kodierung im Clipwerkzeug.
- Normale und kompakte Oberfläche sowie Clipdialog visuell geprüft. Alle sieben Archivwerkzeuge passen bei 1024 × 720. Alle Vorschaudaten und Auslastungswerte sind künstlich.
- Aufnahme-/Encoderprofil weiterhin 15 FPS, etwa 3 Mbit/s und Hardware-Encoding. In dieser Version wurde kein neuer isolierter Spiele-/GPU-Benchmark durchgeführt. Medienmetadaten werden nur auf Anfrage gelesen; Archivseiten erzeugen keine zusätzlichen Dateiscans.

Die automatisierten Ablauftests ersetzen keinen physischen Sperr-/Standby-Langzeittest. Bereits laufende Dateisystemzugriffe in der Startprüfung sind nicht sofort abbrechbar. Clipgrenzen sind schlüsselbildorientiert; exklusiver Spielton, alle Hardwaretypen und Dauerbetrieb über eine volle Woche wurden nicht neu physisch getestet. Reale Aufnahme-Testdateien bleiben im Arbeitsordner und werden nicht veröffentlicht.

## Version 1.2 – 14. Oktober 2026

- 30/30 Speicher- und Einstellungstests bestanden, darunter Notizcache, externe Änderungen, gesperrte/entfernte Notizen, große Mehrfachauswahl und fehlende Schutz-Ziele.
- 22/22 Zustands-, Planungs- und Zeitprüfungen bestanden. Die neue monotone Uhr zählt Pause/Fortsetzen und wiederholte Statusereignisse korrekt.
- 17/17 Prozessprüfungen ohne Bildschirm-/Tonaufnahme bestanden: veränderte Bildschirmtopologie, begrenzte Fehlerpuffer, Probe-Abbruch, Export-Abbruch/Fehler/Erfolg, Schutz vorhandener Ziele und zeitlich begrenzter Stopp eines hängenden Encoders.
- 20/20 UI-Prüfungen bestanden: Auswahl und Zeilenidentität beim Aktualisieren, numerische Größensortierung, Suche/Filter ohne erneute Dateizugriffe, Änderungen anzeigen, durchgehende Datei als Standard, Aufnahmebuttons während Export verfügbar, langsamer Scan lädt im Hintergrund und Filter bleiben bedienbar.
- 18 reale Controllerprüfungen für Aufnahme, Schutz, Pause, Zeitmessung, Notizen, MP4-Export, Fortsetzen und manuelles Löschen bestanden.
- Reale durchgehende Aufnahme mit Systemton auf dem zweiten 1080p-Monitor: genau eine MKV mit etwa 66,4 Sekunden, einschließlich der bisherigen Minutengrenze. H.264 über AMD AMF; Videozeitstempel streng steigend. Leiser Testton wurde über die echte Windows-Wiedergabe gespielt, WASAPI-Pegel war positiv und dekodierter AAC-Ton im MP4-Export war nicht stumm. Keine Aufnahmen werden mitgeliefert.
- Über 64 Sekunden gemessene CPU für Testprozess plus Encoder etwa 2,4 % der gesamten CPU-Kapazität, Arbeitsspeicher etwa 189 MiB. Während dieses Integrationslaufs wurden auch Builds/UI-Prüfungen ausgeführt; dies ist kein isolierter Spiele- oder GPU-Benchmark. Die Probe umfasst Tonerzeugung im Testprozess. Die normale Oberfläche benötigt zusätzlichen Speicher.
- Normale und kompakte Oberfläche mit Beispieldaten gerendert und visuell geprüft. Die in den Vorschauen gezeigten Auslastungswerte sind Beispieldaten.

Automatisch in Build/CI: 69 Prüfungen ohne reale Aufnahme. UI- und Aufnahmeprüfungen sind separate lokale Tests. Konkrete Spiele, exklusive Audiogeräte, NVIDIA/Intel-Hardware und ein voller Wochenbetrieb sind nicht physisch auf dieser AMD-Maschine getestet.

## Version 1.1

- 24 von 24 Speicher-/Einstellungstests bestanden: zusätzlich Beschriftungen, Begrenzung beschädigter Metadaten, Mehrfachauswahl und Löschschutz für aktive/offene/geschützte Dateien.
- 20 von 20 Tests für Aufnahmezustände und Speicherplanung bestanden: manuelle Pause bleibt beim Sperren und Standby erhalten, veraltete Neustartversuche verlieren ihre Gültigkeit, wiederholtes Fortsetzen ändert keine laufende Aufnahme, Schätzungen berücksichtigen Bitrate, Ton, geschützte Videos und Mindestreserve.
- 18 Prüfungen mit echter Aufnahme bestanden: Aufnahme mit Systemton, Pause beendet den Encoder und schließt die Datei, während der Pause entstehen keine neuen Dateien, die Sitzungsdauer bleibt stehen, Fortsetzen nimmt in einer neuen Datei auf, Bezeichnung/Schutz/MP4-Export und ausgewähltes Löschen einschließlich eigener Begleitdatei funktionieren zusammen. Vorhandene Exporte bleiben erhalten.
- Neue Oberfläche in normaler und kompakter Fenstergröße sowie alle vier Einstellungsbereiche mit Beispieldaten gerendert und visuell geprüft. Die Vorschau ist eine Darstellung mit Beispieldaten; ihre CPU-/Speicheranzeigen sind keine Messwerte.
- 11 Prüfungen der Bedienoberfläche bestanden: Auswahl bleibt beim Aktualisieren erhalten, Suche findet Bezeichnungen, Suche/Filter verwenden den lokalen Zwischenspeicher, Schutz-/Zeitraumfilter funktionieren, Ultrasparprofil und individuelle Qualität bleiben erhalten, Pause/Fortsetzen zeigt die richtige Aktion.
- Integrationstests verwendeten eigene Aufnahmeordner und änderten weder bestehende Einstellungen noch den Windows-Autostart.
- Abschließender Test der veröffentlichten EXE erfolgreich: AMD AMF, 1080p, 15 FPS, H.264 mit Systemton und MP4-Export. Gemessene CPU für Testprozess plus Encoder über zwölf Sekunden etwa **0,8 %** der gesamten CPU-Kapazität, Arbeitsspeicher etwa **166 MiB**. Der Test lief ohne geöffnete neue Benutzeroberfläche; dies ist kein Benchmark für Spiele oder GPU-Last.

## Grundlage: Version 1.0

Erstellt und geprüft am 13. Oktober 2026 auf Windows mit AMD Radeon Graphics, zwei angeschlossenen 1920×1080-Monitoren und FFmpeg 8.1.2.

- Build: erfolgreich, keine Fehler oder Warnungen.
- Speicher- und Einstellungstests: 13 von 13 bestanden. Geprüft wurden unter anderem die Sieben-Tage-Frist, UTC-Grenze, geschützte und aktive Dateien, offene Dateien, Speicherlimit, freie Mindestreserve, fremde Dateien, Pfadgrenzen/Verknüpfungen und beschädigte Einstellungsdateien.
- Reale Aufnahme: Hardware-Encoder AMD AMF, 1920×1080, 15 Bilder/s, etwa dreizehn Sekunden Videodatei mit H.264 und nicht stummer AAC-Systemtonspur.
- Videozeitstempel: alle 198 Videopakete mit streng steigenden PTS/DTS.
- MP4-Export: ohne Neukodierung erfolgreich; Videospur und Audiospur vorhanden. Unterschied der Spurendauer etwa 28 ms. Dies prüft die Kontinuität, nicht die exakte Synchronität von einzelnen sichtbaren und hörbaren Ereignissen.
- CPU: etwa **0,9 % der gesamten CPU-Kapazität** im zwölf Sekunden langen Messfenster für Testprozess und FFmpeg gemeinsam. Arbeitsspeicher zusammen etwa **163 MiB**. Kein Spiel lief als kontrollierte Testlast. Diese Werte gelten für diese kurze Messung; sie messen keine GPU-Last und sind keine Garantie für andere Hardware oder Spieleleistung. Die normale Benutzeroberfläche benötigt zusätzlichen Speicher.
- Oberfläche: drei Ansichten als Bilder gerendert und visuell geprüft; deutsche Bedienung, Einstellungen, Status und Aufnahmeliste.
- Segmentwechsel/zweiter Monitor: reale Aufnahme des zweiten Monitors für über eine Minute, ohne Ton. Genau zwei unabhängige MKVs entstanden (etwa 60,2 und 6,1 Sekunden), beide 1920×1080, ohne Audiospur; alle 991 Videopakete mit streng steigenden PTS/DTS. Aufnahme sauber beendet.
- Programminitialisierung mit echten Einstellungen und Geräteerkennung: erfolgreich; zwei Monitore erkannt, kein unbeabsichtigter Aufnahmestart.
- Zusätzlicher Test der Encoder-Zeitbasis: Aufnahme des zweiten Monitors mit Ton und MP4-Export bestanden. Die Filter-Zeitbasis vermeidet Warnungen durch gerundete Encoder-Zeitstempel.
- Absturztest: Testprogramm nach acht Sekunden absichtlich beendet. Windows beendete den zugehörigen Encoder automatisch; kein Encoder blieb zurück. Durch regelmäßiges Flushen blieb die teilweise geschriebene MKV mit 90 Videoframes abspielbar. Das garantiert keine vollständig erhaltene letzte Datei bei jedem denkbaren Absturz.

NVIDIA- und Intel-Encoding werden mit derselben ausgewählten Bildschirmquelle beim Aufnahmestart geprüft; sie konnten auf diesem AMD-PC nicht physisch getestet werden. Autostart wurde im Test nicht aktiviert. Sperren/Standby und eine volle Woche Dauerbetrieb wurden nicht als Langzeittest ausgeführt.

Die für echte Aufnahmetests verwendeten Bildschirmvideos bleiben im Arbeitsordner und sind nicht Teil des Auslieferungspakets.
