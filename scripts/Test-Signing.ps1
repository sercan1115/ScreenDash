[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$signingScript = Join-Path $PSScriptRoot 'Sign-Release.ps1'
$signingTokens = $null
$signingParseErrors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile(
    $signingScript, [ref]$signingTokens, [ref]$signingParseErrors)
if ($signingParseErrors.Count -ne 0) {
    throw ('Sign-Release.ps1 enthaelt Parserfehler: ' +
        (($signingParseErrors | ForEach-Object { $_.Message }) -join '; '))
}
Write-Output 'PASS: Signierskript ohne PowerShell-Parserfehler.'

$signingTempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$signingTestDirectory = Join-Path $signingTempBase ('ScreenDash-SigningTests-' + [Guid]::NewGuid().ToString('N'))
$signingDummy = Join-Path $signingTestDirectory 'unsigned-test.exe'
[void][IO.Directory]::CreateDirectory($signingTestDirectory)
try {
    # This file is never run, and every case must fail before invoking SignTool.
    [IO.File]::WriteAllBytes($signingDummy, [byte[]](0x4D, 0x5A, 0x00, 0x00))
    $signingOriginalHash = (Get-FileHash -LiteralPath $signingDummy -Algorithm SHA256).Hash
    $signingCases = @(
        @{ Name = 'ungueltiger Fingerabdruck'; Thumbprint = 'invalid';
            Timestamp = 'https://timestamp.digicert.com'; Expected = '40-stellige' },
        @{ Name = 'fehlende Signieridentitaet'; Thumbprint = ('0' * 40);
            Timestamp = 'https://timestamp.digicert.com'; Expected = 'nicht eindeutig gefunden' },
        @{ Name = 'HTTP-Zeitstempel abgewiesen'; Thumbprint = ('0' * 40);
            Timestamp = 'http://timestamp.digicert.com'; Expected = 'HTTPS-Adresse' },
        @{ Name = 'Zugangsdaten in Zeitstempel-URL abgewiesen'; Thumbprint = ('0' * 40);
            Timestamp = 'https://user:example@timestamp.digicert.com'; Expected = 'HTTPS-Adresse' }
    )
    foreach ($signingCase in $signingCases) {
        $signingRejected = $false
        try {
            # Explicitly empty SignToolPath also covers build.ps1's optional argument.
            & $signingScript -FilePath $signingDummy -SigningThumbprint $signingCase.Thumbprint `
                -SignToolPath '' -TimestampUrl $signingCase.Timestamp -StoreLocation CurrentUser
        } catch {
            if ($_.Exception.Message -notlike ('*' + $signingCase.Expected + '*')) {
                throw ('Unerwarteter Fehler bei ' + $signingCase.Name + ': ' + $_.Exception.Message)
            }
            $signingRejected = $true
        }
        if (-not $signingRejected) {
            throw ('Negativtest wurde nicht abgewiesen: ' + $signingCase.Name)
        }
        if ((Get-FileHash -LiteralPath $signingDummy -Algorithm SHA256).Hash -ne $signingOriginalHash) {
            throw 'Die isolierte Dummy-EXE wurde veraendert.'
        }
        Write-Output ('PASS: ' + $signingCase.Name + '; Dummy-EXE unveraendert.')
    }
    Write-Output '5 Signierpruefungen bestanden. Keine EXE signiert und keine Zertifikate veraendert.'
} finally {
    # Remove only the known test file and its empty directory; never delete recursively.
    $signingResolvedDirectory = [IO.Path]::GetFullPath($signingTestDirectory)
    $signingBasePrefix = $signingTempBase.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    if (-not $signingResolvedDirectory.StartsWith($signingBasePrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($signingResolvedDirectory) -notmatch '\AScreenDash-SigningTests-[0-9a-f]{32}\z') {
        throw 'Unerwarteter temporaerer Testpfad; automatische Bereinigung abgebrochen.'
    }
    if (Test-Path -LiteralPath $signingDummy -PathType Leaf) {
        Remove-Item -LiteralPath $signingDummy -Force
    }
    if (Test-Path -LiteralPath $signingResolvedDirectory -PathType Container) {
        Remove-Item -LiteralPath $signingResolvedDirectory
    }
}
