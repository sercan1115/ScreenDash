# ScreenDash 1.4.2

- **Fenster und Taskleiste:** normaler Start zeigt das Fenster. Minimieren, minimierter Windows-Autostart und „Nach dem Aufnahmestart minimieren“ behalten den Taskleisteneintrag. Schließen während Aufnahme oder Export minimiert; **Beenden** im Infobereich schließt die App vollständig.
- **Desktop-Symbol:** beim Start wird eine eigene `ScreenDash.lnk` auf dem Windows-Desktop angelegt. Sie öffnet das normale Fenster. Nach einem Update wird diese eigene Verknüpfung auf die neue EXE angepasst. Über das Menü im Infobereich lässt sie sich erneut erstellen. Fremde oder beschädigte gleichnamige Verknüpfungen werden nicht überschrieben.
- **Erneut öffnen:** ein weiterer Doppelklick holt das bereits laufende ScreenDash-Fenster zurück. Es wird keine zweite Aufnahmeinstanz gestartet.
- **OneDrive-Desktop:** Windows-Cloud-Platzhalter werden unterstützt. Echte Ordnerumleitungen über Junctions oder symbolische Links werden weiterhin abgewiesen.
- **Geringe Hintergrundlast:** beim Minimieren stoppen Hover- und Seitenanimationen sofort. Die automatische Aktualisierung der sichtbaren Aufnahmeliste pausiert ebenfalls; die Aufnahme und Speicherbereinigung laufen weiter.
- **Signierung vorbereitet:** der Build kann eine ausdrücklich konfigurierte, vertrauenswürdige Codesignatur einschließlich SHA-256 und Zeitstempel anwenden und überprüfen. Angeforderte Signierung bricht bei Fehlern ab. Anleitung: [SIGNIERUNG.de.md](SIGNIERUNG.de.md).

## Windows-SmartScreen

Die veröffentlichte EXE ist derzeit **nicht digital signiert**. „Der Computer wurde durch Windows geschützt“ kann deshalb weiterhin erscheinen. Es steht noch keine geprüfte Herausgeberidentität für die Veröffentlichung bereit. Die Fenster- und Desktop-Änderungen beseitigen diese Warnung nicht. Auch eine neue gültig signierte Datei kann zunächst noch eine Warnung erhalten, bis sie Reputation aufgebaut hat. [Microsoft: SmartScreen-Reputation](https://learn.microsoft.com/de-de/windows/apps/package-and-deploy/smartscreen-reputation)

Zum Wechsel die alte Version über **Beenden** im Infobereich schließen, die neue ZIP vollständig entpacken und `ScreenDash.exe` starten. Einstellungen und Aufnahmen bleiben erhalten. FFmpeg und FFprobe bleiben externe Voraussetzungen.
