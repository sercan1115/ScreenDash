# ScreenDash 1.6.1

Die Bedienung hat jetzt weichere Animationen. Schaltflächen blenden Hover und Tastaturfokus sanft ein und aus und reagieren beim Drücken mit einem kleinen visuellen Druckeffekt. Schnelle Richtungswechsel setzen am aktuellen Zustand an; die Klickflächen bleiben an ihrer Position. Die Auswahlmarkierung in der Navigation blendet sich ebenfalls sanft ein und aus.

Seiten und Reiter in Einstellungen und Werkzeugen erscheinen mit einem kurzen Überblenden und einer leichten Bewegung. Pro Übergang wird einmal der eigene Fensterinhalt zwischengespeichert und während eines kurzen Übergangs von 240 ms gezeichnet. Die Bedienelemente werden dafür nicht in jedem Bild neu angeordnet. Klicks, Tastaturbefehle und Aufnahmetastenkürzel beenden den Übergang sofort und werden an die echte Oberfläche weitergegeben. Große Fenster über vier Millionen Inhaltspixeln wechseln direkt, um den Zwischenspeicher zu begrenzen.

Der silberne Hintergrund bewegt sich mit bis zu 25 Bildern pro Sekunde flüssiger. Ausschalten friert seine aktuelle Position ein; Wiedereinschalten setzt dort fort. Minimieren, Verstecken und reduzierte Windows-Bewegung stoppen die Animationen. Seitenbilder werden auch bei Fenstergrößen- oder DPI-Wechsel sofort freigegeben. Es werden keine Spielbilder, Aufnahmebilder oder Desktopbilder für die Animationen gelesen.

Zum manuellen Wechsel die alte Version über **Beenden** im Infobereich schließen, die neue ZIP vollständig entpacken und `ScreenDash.exe` starten. Einstellungen und Videos bleiben erhalten. Die eigene Desktop-Verknüpfung folgt beim neuen Start. Die EXE bleibt unsigniert; der echte Signierweg benötigt weiterhin eine verifizierte Herausgeberidentität, siehe [SIGNIERUNG.de.md](SIGNIERUNG.de.md).
