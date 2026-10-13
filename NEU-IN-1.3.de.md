# ScreenDash 1.3

- **Momente als Clip speichern:** Eine abgeschlossene Aufnahme auswählen und **Clip exportieren** anklicken. Gesamtdauer, Auflösung und vorhandene Tonspur werden angezeigt. Beginn und Länge können in Sekunden gewählt werden; Schnelltasten wählen die letzten 30 Sekunden, eine Minute oder fünf Minuten.
- **Schneller Export:** Video und Ton werden kopiert, ohne erneut kodiert zu werden. Die Originalaufnahme bleibt erhalten. Ein Schnitt orientiert sich an Schlüsselbildern; Beginn und tatsächliche Cliplänge können etwas vom eingegebenen Bereich abweichen. Exporte bleiben abbrechbar und überschreiben keine vorhandenen Dateien. Die neue Clipprüfung benötigt `ffprobe.exe` aus dem FFmpeg-Paket.
- **Optionaler Aufnahme-Timer:** Unter **Automatik** eine Dauer von 1 bis 1440 Minuten einstellen. Danach stoppt die Aufnahme automatisch. Der Countdown zählt ausschließlich aktive Aufnahmezeit; eine Pause hält ihn an. Standardmäßig ist der Timer ausgeschaltet. Prüfung alle zwei Sekunden, anschließend normales Schließen der Datei.
- **Große Archive:** Höchstens 200 Aufnahmen pro Seite. Suche und Filter beziehen sich auf alle geladenen Aufnahmen. Seitenwechsel und Sortieren verwenden den Zwischenspeicher. Mehrfachauswahl gilt für die sichtbare Seite. Nach Löschen oder Filtern wird die Seitennummer angepasst.
- **Reagierende Aufnahmebedienung:** Archivlesen und regelmäßige Speicherprüfungen halten die Aufnahmebedienung nicht mehr während des Scans fest. Ein wartender Aufnahmestart lässt sich über Stopp, Tray oder Tastenkürzel abbrechen. Laufende Dateisystemzugriffe im Startcheck können noch bis zu ihrem Abschluss benötigen.
- **Besser bei Sperren und Standby:** Die Aufnahmeabsicht wird sofort gesperrt, und ein laufender Encoderstart erhält sofort ein Abbruchsignal. Alte Pauseanforderungen können keine später neu gestartete Sitzung stoppen.
- **Flüssiger Programmstart:** Die erste Speicherprüfung erfolgt im Hintergrund mit sichtbarem Prüfstatus. Einstellungen und automatische Löschregeln werden mit laufender Bereinigung abgestimmt; veraltete Prüfergebnisse stoppen keine neue Sitzung.

## Update und Nutzung

Alte ScreenDash-App über das Tray-Menü **Beenden** schließen. Neue ZIP vollständig in einen neuen Ordner entpacken und `ScreenDash.exe` starten. Aufnahmen, Schutzmarkierungen und Einstellungen bleiben erhalten. Die durchgehende Datei bleibt Standard; Pause oder Stoppen schließt sie, Fortsetzen beginnt eine neue Datei.

FFmpeg und FFprobe werden weiterhin separat installiert. Bei der üblichen vollständigen FFmpeg-Installation sind beide enthalten. Für die Bildschirmaufnahme genügt FFmpeg; fehlt FFprobe, erklärt die App beim Clipwerkzeug, wie es ergänzt wird.

Neu im Archiv: **Strg + Umschalt + E** öffnet den Clip-Export für eine einzelne abgeschlossene Aufnahme. **Strg + E** exportiert weiterhin die komplette Aufnahme als MP4. Der Clip-Dialog erzeugt keine laufende Videovorschau und lädt Metadaten nur auf Anfrage.

Bei vielen Dateien bleiben Dateisystem und Laufwerk Teil der Belastung. Die Aufnahmeeinstellungen, Hardware-Encoding und fehlende laufende Videovorschau behalten ihr sparsames Verhalten. Ein exakt bildgenauer Schnitt würde neue Kodierung erfordern und ist in diesem schnellen Clipwerkzeug nicht enthalten.
