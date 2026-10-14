using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScreenDash;

/// <summary>Explicit update checks and verified portable downloads; no network activity occurs on construction.</summary>
public sealed class UpdateService
{
    public const string ReleasesUrl = "https://github.com/sercan1115/ScreenDash/releases";
    private const string LatestApi = "https://api.github.com/repos/sercan1115/ScreenDash/releases/latest";
    private const long MaximumPackageBytes = 256L * 1024 * 1024;
    private const long MaximumExtractedBytes = 512L * 1024 * 1024;
    private const int MaximumEntries = 500;
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _client;
    private readonly Dictionary<string, PreparedState> _prepared = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private sealed record PreparedState(PreparedUpdate Update, string Sha256);

    public UpdateService(HttpClient? client = null) => _client = client ?? SharedClient;

    public async Task<UpdateInfo> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var bytes = await ReadBoundedAsync(new Uri(LatestApi), 1024 * 1024, false, deadline.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
                throw new IOException("GitHub lieferte keine stabile Veröffentlichung.");
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            var version = ParseVersion(tag.StartsWith('v') ? tag[1..] : tag);
            if (tag != "v" + version) throw new IOException("Die Versionskennung der Veröffentlichung ist ungültig.");
            var versionText = version.ToString();
            var zipName = $"ScreenDash-{versionText}-Windows.zip";
            var checksumName = $"ScreenDash-{versionText}-SHA256SUMS.txt";
            var releaseUrl = root.GetProperty("html_url").GetString() ?? "";
            ValidateUrl(releaseUrl, $"/sercan1115/ScreenDash/releases/tag/{tag}");
            var zip = FindAsset(root.GetProperty("assets"), zipName);
            var checksum = FindAsset(root.GetProperty("assets"), checksumName);
            var size = zip.GetProperty("size").GetInt64();
            if (size <= 0 || size > MaximumPackageBytes || checksum.GetProperty("size").GetInt64() is <= 0 or > 65536)
                throw new IOException("Die Update-Dateien haben eine ungültige Größe.");
            var downloadUrl = zip.GetProperty("browser_download_url").GetString() ?? "";
            var checksumUrl = checksum.GetProperty("browser_download_url").GetString() ?? "";
            ValidateUrl(downloadUrl, $"/sercan1115/ScreenDash/releases/download/{tag}/{zipName}");
            ValidateUrl(checksumUrl, $"/sercan1115/ScreenDash/releases/download/{tag}/{checksumName}");
            var installed = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(currentVersion.Build, 0));
            return new(versionText, version > installed, releaseUrl, downloadUrl, checksumUrl, size);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new IOException("Die Updateprüfung hat zu lange gedauert. Bitte später erneut versuchen."); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { throw new IOException("Die GitHub-Veröffentlichung konnte nicht gelesen werden.", ex); }
    }

    public async Task<PreparedUpdate> PrepareAsync(UpdateInfo update, string downloadRoot,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var version = ParseVersion(update.Version).ToString();
        var zipName = $"ScreenDash-{version}-Windows.zip";
        var checksumName = $"ScreenDash-{version}-SHA256SUMS.txt";
        ValidateUrl(update.ReleaseUrl, $"/sercan1115/ScreenDash/releases/tag/v{version}");
        var packageUri = ValidateUrl(update.DownloadUrl, $"/sercan1115/ScreenDash/releases/download/v{version}/{zipName}");
        var checksumUri = ValidateUrl(update.ChecksumUrl, $"/sercan1115/ScreenDash/releases/download/v{version}/{checksumName}");
        if (!update.IsNewer || update.Size <= 0 || update.Size > MaximumPackageBytes)
            throw new IOException("Es ist kein gültiges neueres Update vorbereitet.");
        if (!Path.IsPathFullyQualified(downloadRoot) || downloadRoot.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Updates benötigen einen lokalen vollständigen Ordnerpfad.", nameof(downloadRoot));
        var root = Path.GetFullPath(downloadRoot);
        EnsureNoReparsePoints(root);
        Directory.CreateDirectory(root);
        EnsureNoReparsePoints(root);
        var directory = Path.Combine(root, "v" + version + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        EnsureNoReparsePoints(directory);
        var packagePath = Path.Combine(directory, zipName);
        bool ready = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            var checksumBytes = await ReadBoundedAsync(checksumUri, 65536, true, deadline.Token).ConfigureAwait(false);
            var expectedHash = ParseChecksum(Encoding.UTF8.GetString(checksumBytes), zipName);
            await DownloadAsync(packageUri, packagePath, update.Size, progress, deadline.Token).ConfigureAwait(false);
            var actualHash = await HashFileAsync(packagePath, deadline.Token).ConfigureAwait(false);
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Die Prüfsumme des Updates stimmt nicht. Die Datei wird nicht gestartet.");
            var extractionRoot = Path.Combine(directory, "app");
            var executable = await ExtractSafeAsync(packagePath, extractionRoot, deadline.Token).ConfigureAwait(false);
            await CopyLocalMediaToolsAsync(Path.GetDirectoryName(executable)!, deadline.Token).ConfigureAwait(false);
            var executableHash = await HashFileAsync(executable, deadline.Token).ConfigureAwait(false);
            // Retain a receipt for the user; launching still requires this service's in-memory verified state.
            var receipt = JsonSerializer.Serialize(new { Version = version, Executable = Path.GetRelativePath(directory, executable), Sha256 = executableHash });
            await File.WriteAllTextAsync(Path.Combine(directory, "verified-update.json"), receipt, deadline.Token).ConfigureAwait(false);
            File.Delete(packagePath);
            var prepared = new PreparedUpdate(version, directory, executable);
            lock (_gate) _prepared[executable] = new(prepared, executableHash);
            ready = true;
            progress?.Report(1);
            return prepared;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new IOException("Der Updatedownload hat zu lange gedauert. Bitte später erneut versuchen."); }
        finally
        {
            // Only our fresh direct child is removed, and never through a redirected directory.
            if (!ready)
            {
                try { DeleteFreshDirectory(directory, root); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write(ex); }
            }
        }
    }

    /// <summary>Called only after the user chooses Install. The UI must then exit normally.</summary>
    public void LaunchInstaller(PreparedUpdate update, int currentPid, bool autostart, bool startInTray)
    {
        ArgumentNullException.ThrowIfNull(update);
        PreparedState state;
        lock (_gate)
        {
            if (!_prepared.TryGetValue(update.ExecutablePath, out state!) || state.Update != update)
                throw new IOException("Das Update wurde in dieser Sitzung nicht geprüft. Bitte erneut herunterladen.");
        }
        var directory = Path.GetFullPath(update.Directory);
        var executable = Path.GetFullPath(update.ExecutablePath);
        if (!executable.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(executable).Equals("ScreenDash.exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Ungültiger Updatepfad.");
        EnsureNoReparsePoints(executable);
        using var current = Process.GetCurrentProcess();
        if (currentPid != current.Id) throw new ArgumentException("Der Updatewechsel muss vom laufenden ScreenDash-Prozess ausgehen.", nameof(currentPid));
        string hash;
        using (var stream = File.OpenRead(executable)) hash = Convert.ToHexString(SHA256.HashData(stream));
        if (!hash.Equals(state.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Die vorbereitete Programmdatei wurde verändert. Bitte erneut herunterladen.");
        var script = BuildLauncherScript(executable, currentPid, current.StartTime.ToUniversalTime().Ticks,
            state.Sha256, autostart, startInTray);
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell)) throw new FileNotFoundException("Windows PowerShell fehlt. Bitte das Update manuell über GitHub installieren.");
        var info = new ProcessStartInfo(powershell) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            info.ArgumentList.Add(argument);
        using var helper = Process.Start(info) ?? throw new IOException("Der Updatewechsel konnte nicht gestartet werden.");
    }

    internal static string BuildLauncherScript(string executable, int currentPid, long startTicks, string sha256, bool autostart, bool startInTray)
    {
        string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        var trayArguments = startInTray ? " -ArgumentList '--tray'" : "";
        var startupCommand = "\"" + executable + "\" --tray";
        return $$"""
            $ErrorActionPreference = 'Stop'
            try {
                $deadline = [DateTime]::UtcNow.AddMinutes(2)
                do {
                    $old = Get-Process -Id {{currentPid}} -ErrorAction SilentlyContinue
                    if ($null -eq $old -or $old.StartTime.ToUniversalTime().Ticks -ne {{startTicks}}) { break }
                    if ([DateTime]::UtcNow -gt $deadline) { exit 2 }
                    Start-Sleep -Milliseconds 200
                } while ($true)
                $target = {{Literal(executable)}}
                if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne {{Literal(sha256)}}) { exit 3 }
                $started = Start-Process -FilePath $target -WorkingDirectory {{Literal(Path.GetDirectoryName(executable)!)}}{{trayArguments}} -PassThru
                {{(autostart ? "New-Item -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run' -Force | Out-Null\n    Set-ItemProperty -LiteralPath 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run' -Name 'ScreenDash' -Value " + Literal(startupCommand) : "")}}
            } catch {
                Add-Content -LiteralPath {{Literal(Path.Combine(Path.GetDirectoryName(executable)!, "update-error.txt"))}} -Value $_.Exception.Message
                exit 1
            }
            """;
    }

    private async Task<byte[]> ReadBoundedAsync(Uri uri, int maximumBytes, bool asset, CancellationToken ct)
    {
        using var response = await SendAsync(uri, asset, ct).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is > 0 && response.Content.Headers.ContentLength > maximumBytes)
            throw new IOException("Die Updateantwort ist zu groß.");
        using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > maximumBytes) throw new IOException("Die Updateantwort ist zu groß.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private async Task DownloadAsync(Uri uri, string destination, long expectedBytes, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await SendAsync(uri, true, ct).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is { } length && length != expectedBytes)
            throw new IOException("Die Downloadgröße stimmt nicht mit der Veröffentlichung überein.");
        using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[81920];
        long copied = 0;
        var reportAt = Stopwatch.StartNew();
        int count;
        while ((count = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            copied += count;
            if (copied > expectedBytes || copied > MaximumPackageBytes) throw new IOException("Der Updatedownload überschreitet die erlaubte Größe.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
            if (reportAt.ElapsedMilliseconds >= 150)
            {
                progress?.Report(copied / (double)expectedBytes * .9);
                reportAt.Restart();
            }
        }
        if (copied != expectedBytes) throw new IOException("Der Updatedownload ist unvollständig.");
        await output.FlushAsync(ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, bool asset, CancellationToken ct)
    {
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("ScreenDash-Update/1.4");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(asset ? "application/octet-stream" : "application/vnd.github+json"));
            if (!asset) request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (!asset || location is null) throw new IOException("Die Updateantwort enthält eine unerwartete Weiterleitung.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (!IsTrustedDownloadHost(uri)) throw new IOException("Die Downloadweiterleitung führt zu einer nicht erlaubten Adresse.");
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode;
                response.Dispose();
                throw new IOException(status == HttpStatusCode.Forbidden || (int)status == 429
                    ? "GitHub erlaubt momentan keine weiteren Updateprüfungen. Bitte später erneut versuchen."
                    : "GitHub konnte nicht erreicht werden (HTTP " + (int)status + ").");
            }
            // An injected client may follow redirects itself. Never trust a response from an unrelated host.
            if (response.RequestMessage?.RequestUri is { } final && (asset ? !IsTrustedDownloadHost(final) : final.AbsoluteUri != LatestApi))
            {
                response.Dispose();
                throw new IOException("Die Updateantwort kommt nicht von der erlaubten Adresse.");
            }
            return response;
        }
        throw new IOException("Der Updatedownload enthält zu viele Weiterleitungen.");
    }

    private static bool IsTrustedDownloadHost(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && uri.Host is "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";

    private static JsonElement FindAsset(JsonElement assets, string name)
    {
        if (assets.ValueKind != JsonValueKind.Array) throw new IOException("Die Veröffentlichung enthält keine Update-Dateien.");
        var matches = assets.EnumerateArray().Where(asset => asset.GetProperty("name").GetString() == name
            && asset.GetProperty("state").GetString() == "uploaded").ToArray();
        return matches.Length == 1 ? matches[0] : throw new IOException("Zur Veröffentlichung fehlt eine eindeutige Windows-Datei oder Prüfsumme.");
    }

    private static Version ParseVersion(string value)
    {
        if (!Regex.IsMatch(value ?? "", @"^\d{1,4}\.\d{1,4}\.\d{1,4}$", RegexOptions.CultureInvariant)
            || !Version.TryParse(value, out var version)) throw new IOException("Ungültige Updateversion.");
        return version;
    }

    private static Uri ValidateUrl(string value, string path)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com"
            || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || uri.AbsolutePath != path)
            throw new IOException("Das Update verweist nicht auf die erlaubte ScreenDash-Veröffentlichung.");
        return uri;
    }

    internal static string ParseChecksum(string manifest, string fileName)
    {
        string? checksum = null;
        foreach (var raw in manifest.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.Length == 0) continue;
            var match = Regex.Match(line, @"^([0-9a-fA-F]{64})\s+\*?([^\r\n]+)$", RegexOptions.CultureInvariant);
            if (!match.Success) throw new IOException("Die Prüfsummendatei ist ungültig.");
            if (match.Groups[2].Value != fileName) continue;
            if (checksum is not null) throw new IOException("Die Prüfsummendatei enthält mehrere Werte für das Update.");
            checksum = match.Groups[1].Value;
        }
        return checksum ?? throw new IOException("Die Prüfsummendatei enthält keinen Wert für die Windows-Datei.");
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }

    internal static async Task<string> ExtractSafeAsync(string package, string extractionRoot, CancellationToken ct)
    {
        var root = Path.GetFullPath(extractionRoot);
        EnsureNoReparsePoints(root);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Der Updateordner existiert bereits.");
        Directory.CreateDirectory(root);
        using var archive = ZipFile.OpenRead(package);
        if (archive.Entries.Count is 0 or > MaximumEntries) throw new IOException("Das Updatearchiv enthält zu viele oder keine Dateien.");
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var executables = new List<string>();
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0 || unixType is not (0 or 0x8000 or 0x4000))
                throw new IOException("Das Updatearchiv enthält eine Verknüpfung oder einen unerlaubten Dateityp.");
            var name = entry.FullName.Replace('\\', '/');
            bool isDirectory = name.EndsWith('/');
            var parts = name.TrimEnd('/').Split('/');
            if (name.StartsWith('/') || parts.Any(InvalidPathPart)) throw new IOException("Das Updatearchiv enthält einen ungültigen Dateipfad.");
            var destination = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !destinations.Add(destination))
                throw new IOException("Das Updatearchiv enthält einen doppelten oder unerlaubten Dateipfad.");
            EnsureNoReparsePoints(destination);
            if (isDirectory)
            {
                if (entry.Length != 0) throw new IOException("Ein Updateordner enthält unerwartete Daten.");
                Directory.CreateDirectory(destination);
                continue;
            }
            if (entry.Length < 0 || entry.Length > MaximumPackageBytes || total + entry.Length > MaximumExtractedBytes)
                throw new IOException("Die entpackten Updatedateien sind zu groß.");
            total += entry.Length;
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            EnsureNoReparsePoints(destination);
            using var source = entry.Open();
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            long copied = 0;
            int count;
            while ((count = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
            {
                copied += count;
                if (copied > entry.Length) throw new IOException("Eine entpackte Updatedatei überschreitet ihre angegebene Größe.");
                await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
            }
            if (copied != entry.Length) throw new IOException("Eine entpackte Updatedatei ist unvollständig.");
            if (Path.GetFileName(destination).Equals("ScreenDash.exe", StringComparison.OrdinalIgnoreCase)) executables.Add(destination);
        }
        if (executables.Count != 1 || new FileInfo(executables[0]).Length < 2)
            throw new IOException("Das Update enthält keine eindeutige ScreenDash.exe.");
        using (var exe = File.OpenRead(executables[0]))
            if (exe.ReadByte() != 'M' || exe.ReadByte() != 'Z') throw new IOException("Die Updatedatei ist kein Windows-Programm.");
        return executables[0];
    }

    private static bool InvalidPathPart(string part)
    {
        if (part.Length is 0 or > 128 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
            || part.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0 || part.Any(char.IsControl)) return true;
        var stem = part.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || Regex.IsMatch(stem, @"^(COM|LPT)[1-9]$", RegexOptions.CultureInvariant);
    }

    private static void EnsureNoReparsePoints(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Der Updatepfad darf keine Dateiverknüpfungen oder umgeleiteten Ordner enthalten.");
            current = Path.GetDirectoryName(current);
        }
    }

    private static async Task CopyLocalMediaToolsAsync(string destination, CancellationToken ct)
    {
        foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" })
        {
            if (File.Exists(Path.Combine(destination, name))) continue;
            foreach (var directory in new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "tools") })
            {
                var source = Path.Combine(directory, name);
                if (!File.Exists(source)) continue;
                EnsureNoReparsePoints(source);
                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                if (input.Length > MaximumPackageBytes) throw new IOException("Die lokale FFmpeg-Datei ist zu groß für den Updatewechsel.");
                using var output = new FileStream(Path.Combine(destination, name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await input.CopyToAsync(output, ct).ConfigureAwait(false);
                break;
            }
        }
    }

    private static void DeleteFreshDirectory(string directory, string expectedParent)
    {
        if (!Path.GetDirectoryName(Path.GetFullPath(directory))!.Equals(Path.GetFullPath(expectedParent), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Ungültiger temporärer Updateordner.");
        EnsureNoReparsePoints(directory);
        if (!Directory.Exists(directory)) return;
        // Check every descendant before a recursive delete; extraction never creates links itself.
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count != 0)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                EnsureNoReparsePoints(entry);
                if (Directory.Exists(entry)) pending.Push(entry);
            }
        }
        Directory.Delete(directory, true);
    }
}
