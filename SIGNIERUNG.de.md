# Release-Signierung und Windows-Warnung

Die bisher veroeffentlichte ScreenDash-EXE ist nicht mit einer vertrauenswuerdigen Herausgeberidentitaet signiert. Windows kann deshalb beim ersten Start die Meldung „Windows hat Ihren PC geschuetzt“ beziehungsweise eine SmartScreen-Warnung anzeigen. Diese Meldung ist allein noch kein bestaetigter Virenfund. Falls Windows einen konkreten Bedrohungsnamen meldet, muss dieser gesondert untersucht werden.

Eine echte Codesignatur bestaetigt den Herausgeber und schuetzt die EXE vor unbemerkten Aenderungen. Sie garantiert jedoch keine sofortige SmartScreen-Reputation. Auch neue, korrekt signierte Versionen koennen anfangs noch gewarnt werden; EV-Zertifikate umgehen das nicht. Selbst ausgestellte Zertifikate sind fuer oeffentliche Downloads keine Loesung. Microsoft nennt ein ueber den Microsoft Store verteiltes MSIX als den verlaesslichsten Weg gegen SmartScreen-Downloadwarnungen. [Microsoft: SmartScreen-Reputation](https://learn.microsoft.com/de-de/windows/apps/package-and-deploy/smartscreen-reputation)

## Voraussetzungen

- Ein vorhandenes, aktuell gueltiges Codesignaturzertifikat einer vertrauenswuerdigen Zertifizierungsstelle mit verfuegbarem privatem Schluessel im persoenlichen Speicher `CurrentUser\My` oder `LocalMachine\My`. Bei einem Hardware-Token muss dessen Anbieter bereits eingerichtet sein. Ein moeglicher Token- oder PIN-Dialog gehoert zum jeweiligen Schluesselanbieter.
- Das Windows-SDK mit einer gueltig von Microsoft signierten `x64\signtool.exe`.
- Erreichbare Zertifikats-Sperrpruefung und ein HTTPS-Dienst fuer RFC-3161-Zeitstempel. Standard: `https://timestamp.digicert.com`. Fuer einen anderen Anbieter kann `-TimestampUrl` gesetzt werden.

Das Skript erstellt oder importiert keine Zertifikate, exportiert keine privaten Schluessel und veraendert keine Windows-Schutzfunktion. Es erwartet keinen PFX-Pfad und kein Passwort. Im Repository und in CI-Ausgaben sollen keine privaten Schluessel, PINs oder Zertifikatspasswoerter stehen.

## Lokaler Ablauf

Der Build kann die finale EXE nach allen Tests automatisch signieren. Den Platzhalter durch den 40-stelligen Fingerabdruck der vorhandenen Codesignaturidentitaet ersetzen:

```powershell
.\build.ps1 -SigningThumbprint '<40-stelliger Fingerabdruck>'
```

Bei Bedarf werden `-SignToolPath 'C:\Program Files (x86)\Windows Kits\10\bin\<SDK-Version>\x64\signtool.exe'`, `-TimestampUrl 'https://<Zeitstempeldienst>'` und `-StoreLocation LocalMachine` zusaetzlich angegeben. Ohne `SignToolPath` verwendet das Skript das neueste installierte x64-SignTool des Windows-10/11-SDK. Ohne `SigningThumbprint` wird wie bisher ein unsignierter Build erstellt.

Eine bereits fertig gebaute EXE kann separat signiert werden:

```powershell
.\scripts\Sign-Release.ps1 -FilePath .\dist\ScreenDash.exe -SigningThumbprint '<40-stelliger Fingerabdruck>'
```

Das Skript prueft EKU, Schluesselverfuegbarkeit, Gueltigkeitszeitraum, Zertifikatskette und Sperrstatus. Es lehnt selbst ausgestellte oder CA-Zertifikate ab. Danach signiert es mit SHA-256 und RFC-3161-Zeitstempel, prueft mit `signtool verify /pa /all /tw` und bestaetigt abschliessend den Authenticode-Status, den ausgewaehlten Signierer und einen vorhandenen Zeitstempel. Jeder Fehler bricht den Vorgang ab; ein zur Signierung vorgesehenes Release darf dann nicht als erfolgreich signiert veroeffentlicht werden. [Microsoft: SignTool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool)

Erst nach erfolgreicher Signierung das Windows-ZIP und dessen SHA-256-Pruefsummen erstellen. Die signierte EXE danach nicht erneut bauen oder veraendern, weil die Signatur sonst verloren geht oder ungueltig werden kann. Pruefsummen bestaetigen Dateiintegritaet und ersetzen keine Herausgebersignatur.

## Build- und CI-Integration

`build.ps1` ruft das Signierskript bei angegebenem `SigningThumbprint` unmittelbar nach dem erfolgreichen `dotnet publish` auf. Die ZIP-Erstellung und die Berechnung der Release-Pruefsummen erfolgen danach. Ist eine Signieridentitaet angegeben, bricht ein Fehler den gesamten Build beziehungsweise Release-Job ab. Ohne Signieridentitaet bleibt die Ausgabe offen als unsignierter Build gekennzeichnet.

Fuer GitHub Actions ist Microsoft Artifact Signing eine Alternative zum lokalen Zertifikatspeicher. Dafuer sind eine eigene verifizierte Herausgeberidentitaet, ein eingerichtetes Signing-Konto und eng begrenzte CI-Berechtigungen erforderlich. Dieser Dienst wird durch das lokale SignTool-Skript nicht eingerichtet oder aktiviert. [Microsoft: Artifact-Signing-Integration](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-signing-integrations)

Aktuell ist in der Arbeitsumgebung keine nutzbare Codesignaturidentitaet vorhanden. Deshalb ist nur die sichere Vorbereitung des Signiervorgangs moeglich; ein signiertes Release und das Verschwinden der Windows-Warnung werden nicht behauptet.

## Pruefung ohne Signieridentitaet

```powershell
.\scripts\Test-Signing.ps1
```

Diese fuenf Pruefungen bestaetigen die PowerShell-Syntax sowie den harten Abbruch bei einem ungueltigen Fingerabdruck, einer fehlenden Signieridentitaet, einem HTTP-Zeitstempeldienst und Zugangsdaten in der Zeitstempel-URL. Sie verwenden ausschliesslich eine isolierte temporaere Dummy-EXE, die nicht ausgefuehrt wird; deren SHA-256 muss in jedem Fall unveraendert bleiben. Die Testdatei wird danach entfernt. Es werden keine Zertifikate angelegt, importiert oder geaendert und keine Produktdateien signiert. Das ersetzt keine echte Signier- und Zeitstempelpruefung mit einer spaeter eingerichteten Identitaet.
