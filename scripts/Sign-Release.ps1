[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,

    [Parameter(Mandatory = $true)]
    [string]$SigningThumbprint,

    [string]$SignToolPath,

    [string]$TimestampUrl = 'https://timestamp.digicert.com',

    [ValidateSet('CurrentUser', 'LocalMachine')]
    [string]$StoreLocation = 'CurrentUser'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Select an existing signing identity; this script never creates or imports one.
$releaseThumbprint = ($SigningThumbprint -replace '\s', '').ToUpperInvariant()
if ($releaseThumbprint -notmatch '\A[0-9A-F]{40}\z') {
    throw 'SigningThumbprint muss der 40-stellige hexadezimale Fingerabdruck eines vorhandenen Codesignaturzertifikats sein.'
}

$releaseTimestampUri = $null
if (-not [Uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$releaseTimestampUri) -or
    $releaseTimestampUri.Scheme -ne 'https' -or
    -not [string]::IsNullOrEmpty($releaseTimestampUri.UserInfo) -or
    -not [string]::IsNullOrEmpty($releaseTimestampUri.Fragment)) {
    throw 'TimestampUrl muss eine absolute HTTPS-Adresse eines RFC-3161-Zeitstempeldienstes ohne Zugangsdaten oder Fragment sein.'
}

$releaseTarget = Get-Item -LiteralPath $FilePath -ErrorAction Stop
if ($releaseTarget -isnot [System.IO.FileInfo] -or $releaseTarget.Extension -ine '.exe' -or
    ($releaseTarget.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw 'FilePath muss auf eine vorhandene EXE-Datei zeigen; Verknuepfungen und Reparse Points werden nicht signiert.'
}
if (($releaseTarget.Attributes -band [IO.FileAttributes]::ReadOnly) -ne 0) {
    throw 'Die zu signierende EXE ist schreibgeschuetzt.'
}
$releaseParent = $releaseTarget.Directory
while ($null -ne $releaseParent) {
    if (($releaseParent.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Der Pfad zur EXE darf keine Reparse Points enthalten.'
    }
    $releaseParent = $releaseParent.Parent
}

$releaseStore = [System.Security.Cryptography.X509Certificates.X509Store]::new(
    [System.Security.Cryptography.X509Certificates.StoreName]::My,
    [System.Security.Cryptography.X509Certificates.StoreLocation]::$StoreLocation)
$releaseCertificate = $null
$releaseChain = $null
try {
    try {
        $releaseStore.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly -bor
            [System.Security.Cryptography.X509Certificates.OpenFlags]::OpenExistingOnly)
    } catch [System.Security.Cryptography.CryptographicException] {
        throw 'Der angegebene persoenliche Zertifikatspeicher ist nicht verfuegbar; das gewaehlte Codesignaturzertifikat wurde nicht eindeutig gefunden.'
    }
    $releaseMatches = $releaseStore.Certificates.Find(
        [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
        $releaseThumbprint, $false)
    if ($releaseMatches.Count -ne 1) {
        throw 'Das gewaehlte Codesignaturzertifikat wurde im angegebenen persoenlichen Zertifikatspeicher nicht eindeutig gefunden.'
    }
    $releaseCertificate = $releaseMatches[0]
    if (-not $releaseCertificate.HasPrivateKey) {
        throw 'Das gewaehlte Zertifikat besitzt keinen verfuegbaren privaten Schluessel.'
    }
    $releaseNow = [DateTime]::UtcNow
    if ($releaseCertificate.NotBefore.ToUniversalTime() -gt $releaseNow -or
        $releaseCertificate.NotAfter.ToUniversalTime() -le $releaseNow) {
        throw 'Das gewaehlte Zertifikat ist derzeit nicht gueltig.'
    }
    if ($releaseCertificate.Subject -eq $releaseCertificate.Issuer) {
        throw 'Selbst ausgestellte Zertifikate werden fuer eine oeffentliche Release-Signatur nicht akzeptiert.'
    }
    $releaseHasCodeSigning = $false
    foreach ($releaseExtension in $releaseCertificate.Extensions) {
        if ($releaseExtension -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]) {
            foreach ($releaseUsage in $releaseExtension.EnhancedKeyUsages) {
                if ($releaseUsage.Value -eq '1.3.6.1.5.5.7.3.3') { $releaseHasCodeSigning = $true }
            }
        }
        if ($releaseExtension -is [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension] -and
            $releaseExtension.CertificateAuthority) {
            throw 'Ein CA-Zertifikat darf nicht als Release-Signieridentitaet verwendet werden.'
        }
    }
    if (-not $releaseHasCodeSigning) {
        throw 'Das gewaehlte Zertifikat enthaelt keine Codesignatur-EKU.'
    }

    $releaseChain = [System.Security.Cryptography.X509Certificates.X509Chain]::new()
    [void]$releaseChain.ChainPolicy.ApplicationPolicy.Add([System.Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
    $releaseChain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::Online
    $releaseChain.ChainPolicy.RevocationFlag = [System.Security.Cryptography.X509Certificates.X509RevocationFlag]::ExcludeRoot
    $releaseChain.ChainPolicy.VerificationFlags = [System.Security.Cryptography.X509Certificates.X509VerificationFlags]::NoFlag
    $releaseChain.ChainPolicy.UrlRetrievalTimeout = [TimeSpan]::FromSeconds(20)
    if (-not $releaseChain.Build($releaseCertificate)) {
        # Status codes are sufficient to diagnose trust, without exposing certificate subjects.
        $releaseChainStatus = ($releaseChain.ChainStatus | ForEach-Object { $_.Status.ToString() }) -join ', '
        throw "Die Zertifikatskette oder Sperrpruefung ist nicht gueltig: $releaseChainStatus."
    }

    if ([string]::IsNullOrWhiteSpace($SignToolPath)) {
        $releaseToolCandidates = @()
        $releaseSdkBase = ${env:ProgramFiles(x86)}
        if (-not [string]::IsNullOrWhiteSpace($releaseSdkBase)) {
            $releaseSdkBin = Join-Path $releaseSdkBase 'Windows Kits\10\bin'
            if (Test-Path -LiteralPath $releaseSdkBin -PathType Container) {
                foreach ($releaseSdkVersion in Get-ChildItem -LiteralPath $releaseSdkBin -Directory) {
                    $releaseVersion = $null
                    if ([Version]::TryParse($releaseSdkVersion.Name, [ref]$releaseVersion)) {
                        $releaseCandidate = Join-Path $releaseSdkVersion.FullName 'x64\signtool.exe'
                        if (Test-Path -LiteralPath $releaseCandidate -PathType Leaf) {
                            $releaseToolCandidates += [pscustomobject]@{ Version = $releaseVersion; Path = $releaseCandidate }
                        }
                    }
                }
            }
        }
        if ($releaseToolCandidates.Count -gt 0) {
            $SignToolPath = ($releaseToolCandidates | Sort-Object Version -Descending | Select-Object -First 1).Path
        } else {
            throw 'SignTool wurde nicht im x64-Verzeichnis des Windows-10/11-SDK gefunden. Installiere das Windows-SDK oder gib SignToolPath explizit an.'
        }
    }
    $releaseTool = Get-Item -LiteralPath $SignToolPath -ErrorAction Stop
    if ($releaseTool -isnot [System.IO.FileInfo] -or $releaseTool.Name -ine 'signtool.exe' -or
        ($releaseTool.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'SignToolPath muss auf die vorhandene signtool.exe des Windows-SDK zeigen.'
    }
    $releaseToolSignature = Get-AuthenticodeSignature -LiteralPath $releaseTool.FullName
    if ($releaseToolSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $releaseToolSignature.SignerCertificate -or
        $releaseToolSignature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {
        throw 'Die ausgewaehlte SignTool-Datei besitzt keine gueltige Microsoft-Signatur.'
    }

    $releaseSignArguments = @('sign', '/sha1', $releaseThumbprint, '/s', 'My',
        '/fd', 'SHA256', '/tr', $releaseTimestampUri.AbsoluteUri, '/td', 'SHA256', '/d', 'ScreenDash')
    if ($StoreLocation -eq 'LocalMachine') { $releaseSignArguments += '/sm' }
    $releaseSignArguments += $releaseTarget.FullName
    & $releaseTool.FullName @releaseSignArguments
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool hat die Signierung oder Zeitstempelung nicht fehlerfrei abgeschlossen (Exitcode $LASTEXITCODE). Das Release darf nicht veroeffentlicht werden."
    }
    & $releaseTool.FullName verify /pa /all /tw $releaseTarget.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool hat die Signatur nicht fehlerfrei verifiziert (Exitcode $LASTEXITCODE). Das Release darf nicht veroeffentlicht werden."
    }
    $releaseSignature = Get-AuthenticodeSignature -LiteralPath $releaseTarget.FullName
    if ($releaseSignature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $releaseSignature.SignerCertificate -or
        $releaseSignature.SignerCertificate.Thumbprint -ne $releaseThumbprint -or
        $null -eq $releaseSignature.TimeStamperCertificate) {
        throw 'Die abschliessende Authenticode-Pruefung bestaetigt keine gueltige Signatur mit dem ausgewaehlten Zertifikat und Zeitstempel. Das Release darf nicht veroeffentlicht werden.'
    }
    Write-Output 'Release-EXE erfolgreich mit vorhandener Signieridentitaet und Zeitstempel signiert und verifiziert. ZIP und SHA-256-Pruefsummen erst jetzt erstellen.'
} finally {
    if ($null -ne $releaseChain) { $releaseChain.Dispose() }
    if ($null -ne $releaseCertificate) { $releaseCertificate.Dispose() }
    $releaseStore.Dispose()
}
