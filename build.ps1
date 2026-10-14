param(
    [string]$Dotnet = 'dotnet',
    [string]$SigningThumbprint,
    [string]$SignToolPath,
    [string]$TimestampUrl = 'https://timestamp.digicert.com',
    [ValidateSet('CurrentUser', 'LocalMachine')][string]$StoreLocation = 'CurrentUser'
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Push-Location $PSScriptRoot
try {
    & $Dotnet run --project ScreenDash.Tests\ScreenDash.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests fehlgeschlagen.' }
    & $Dotnet run --project ScreenDash.ControllerTests\ScreenDash.ControllerTests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Zustands- und Planungstests fehlgeschlagen.' }
    & $Dotnet run --project ScreenDash.RecorderTests\ScreenDash.RecorderTests.csproj -c Release -- --process-checks
    if ($LASTEXITCODE -ne 0) { throw 'Prozess- und Exporttests fehlgeschlagen.' }
    & $Dotnet run --project ScreenDash.ControllerCheck\ScreenDash.ControllerCheck.csproj -c Release -- --scheduling-checks
    if ($LASTEXITCODE -ne 0) { throw 'Controller-Ablauftests fehlgeschlagen.' }
    & $Dotnet run --project ScreenDash.FeatureTests\ScreenDash.FeatureTests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Profil-, Spielerkennungs- und Updatetests fehlgeschlagen.' }
    & $Dotnet run --project ScreenDash.RecorderTests\ScreenDash.RecorderTests.csproj -c Release -- --audio-checks
    if ($LASTEXITCODE -ne 0) { throw 'Audiopuffer- und Mikrofontests fehlgeschlagen.' }
    & $Dotnet run --project ScreenDash.ControllerCheck\ScreenDash.ControllerCheck.csproj -c Release -- --feature-checks
    if ($LASTEXITCODE -ne 0) { throw 'Funktions-Controller-Tests fehlgeschlagen.' }
    & $Dotnet run --project ScreenDash.Preview\ScreenDash.Preview.csproj -c Release -- work\ui-checks --checks-only
    if ($LASTEXITCODE -ne 0) { throw 'Oberflächen- und Animationsprüfungen fehlgeschlagen.' }
    & $Dotnet run --project ScreenDash.WindowsTests\ScreenDash.WindowsTests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Windows-Verknüpfungsprüfungen fehlgeschlagen.' }
    & (Join-Path $PSScriptRoot 'scripts\Test-Signing.ps1')
    & $Dotnet publish ScreenDash\ScreenDash.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o dist
    if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }
    if ($SigningThumbprint) {
        & "$PSScriptRoot\scripts\Sign-Release.ps1" -FilePath "$PSScriptRoot\dist\ScreenDash.exe" -SigningThumbprint $SigningThumbprint -SignToolPath $SignToolPath -TimestampUrl $TimestampUrl -StoreLocation $StoreLocation
    }
    else { Write-Warning 'Die erzeugte EXE ist nicht digital signiert. Windows-SmartScreen kann warnen.' }
    Copy-Item -LiteralPath README.de.md -Destination dist\README.de.md
    if (Test-Path LICENSE) { Copy-Item -LiteralPath LICENSE -Destination dist\LICENSE }
    if (Test-Path NEU-IN-1.5.1.de.md) { Copy-Item -LiteralPath NEU-IN-1.5.1.de.md -Destination dist\NEU-IN-1.5.1.de.md }
    if (Test-Path SIGNIERUNG.de.md) { Copy-Item -LiteralPath SIGNIERUNG.de.md -Destination dist\SIGNIERUNG.de.md }
    if (Test-Path TESTBERICHT.de.md) { Copy-Item -LiteralPath TESTBERICHT.de.md -Destination dist\TESTBERICHT.de.md }
    if (Test-Path Drittanbieter-Lizenzen.txt) { Copy-Item -LiteralPath Drittanbieter-Lizenzen.txt -Destination dist\Drittanbieter-Lizenzen.txt }
    Write-Output 'Fertig: dist\ScreenDash.exe'
} finally { Pop-Location }
