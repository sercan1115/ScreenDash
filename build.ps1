param([string]$Dotnet = 'dotnet')
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
    & $Dotnet publish ScreenDash\ScreenDash.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o dist
    if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }
    Copy-Item -LiteralPath README.de.md -Destination dist\README.de.md
    if (Test-Path LICENSE) { Copy-Item -LiteralPath LICENSE -Destination dist\LICENSE }
    if (Test-Path NEU-IN-1.4.de.md) { Copy-Item -LiteralPath NEU-IN-1.4.de.md -Destination dist\NEU-IN-1.4.de.md }
    if (Test-Path TESTBERICHT.de.md) { Copy-Item -LiteralPath TESTBERICHT.de.md -Destination dist\TESTBERICHT.de.md }
    if (Test-Path Drittanbieter-Lizenzen.txt) { Copy-Item -LiteralPath Drittanbieter-Lizenzen.txt -Destination dist\Drittanbieter-Lizenzen.txt }
    Write-Output 'Fertig: dist\ScreenDash.exe'
} finally { Pop-Location }
