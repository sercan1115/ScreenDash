using System.IO.Compression;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ScreenDash;

var checks = new List<(string Name, Func<Task> Run)>();
void Sync(string name, Action check) => checks.Add((name, () => { check(); return Task.CompletedTask; }));
void Async(string name, Func<Task> check) => checks.Add((name, check));
void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name);
}
async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name);
}
var scratch = Path.Combine(Path.GetTempPath(), "ScreenDash-FeatureTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
string Folder() => Path.Combine(scratch, Guid.NewGuid().ToString("N"));
AppSettings Settings() => new() { RecordingDirectory = Path.Combine(scratch, "recordings") };

Sync("Game names normalize exactly and remove duplicate aliases", () =>
{
    var names = GameDetection.NormalizeNames([" GAME.EXE ", "game", "other.exe"]);
    Require(names.SequenceEqual(["game.exe", "other.exe"]), "Names must normalize without partial matches.");
    Throws<ArgumentException>(() => GameDetection.NormalizeNames(["C:\\Games\\game.exe"]));
    Throws<ArgumentException>(() => GameDetection.NormalizeNames(["*.exe"]));
    Throws<ArgumentException>(() => GameDetection.NormalizeNames(Enumerable.Range(0, 31).Select(i => "game" + i)));
});
Sync("Game detection matches only exact names in the current session", () =>
{
    var detector = new GameDetection(() => [new("game_helper", 2), new("GAME", 3), new("game", 2)], 2);
    var result = detector.Evaluate(["game.exe"]);
    Require(result.ShouldStart && result.HasGames && result.RunningGames.SequenceEqual(["game.exe"]), "The selected session must match exactly.");
    var other = new GameDetection(() => [new("game_helper", 2), new("game", 3)], 2);
    Require(!other.Evaluate(["game"]).HasGames, "Another session and a similarly named process must be ignored.");
});
Sync("Disabled or unconfigured game detection takes no process snapshot", () =>
{
    int count = 0;
    var detector = new GameDetection(() => { count++; return [new("game", 2)]; }, 2);
    Require(!detector.Evaluate([]).ShouldStart && count == 0, "Empty detection must be free of process enumeration.");
});
Sync("Game detection stops only a recording that it started", () =>
{
    IReadOnlyList<GameProcess> snapshot = [new("game", 2)];
    var detector = new GameDetection(() => snapshot, 2);
    Require(detector.Evaluate(["game"]).ShouldStart, "Game launch must offer an automatic start.");
    detector.MarkRecordingStarted();
    Require(!detector.Evaluate(["game"]).ShouldStart, "An owned recording must not restart.");
    snapshot = [];
    Require(detector.Evaluate(["game"]).ShouldStop, "The last selected game exit must stop an owned recording.");
    detector.MarkRecordingStopped();
    Require(!detector.Evaluate(["game"]).ShouldStop, "A manual recording must not be stopped by detection.");
});
Sync("Manual stop suppresses game restart until all selected games exit", () =>
{
    IReadOnlyList<GameProcess> snapshot = [new("game", 2), new("other", 2)];
    var detector = new GameDetection(() => snapshot, 2);
    detector.MarkRecordingStarted();
    detector.SuppressUntilGamesExit();
    detector.MarkRecordingStopped();
    Require(!detector.Evaluate(["game", "other"]).ShouldStart, "Manual stop must persist with games running.");
    snapshot = [new("other", 2)];
    Require(!detector.Evaluate(["game", "other"]).ShouldStart, "One remaining game must keep suppression.");
    snapshot = [];
    detector.Evaluate(["game", "other"]);
    snapshot = [new("game", 2)];
    Require(detector.Evaluate(["game", "other"]).ShouldStart, "A new game session may start capture.");
});
Sync("Program picker lists normalized unique names from this session", () =>
{
    var detector = new GameDetection(() => [new("GAME", 2), new("game.exe", 2), new("other", 3), new("bad/name", 2)], 2);
    Require(detector.GetRunningPrograms().SequenceEqual(["game.exe"]), "The picker must not leak other session names or duplicates.");
});
Async("A slow game snapshot cannot block Stop or undo its suppression", async () =>
{
    using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
    bool block = true;
    var detector = new GameDetection(() => { if (block) { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); return []; } return [new("game", 2)]; }, 2);
    var snapshot = Task.Run(() => detector.Evaluate(["game"]));
    try
    {
        Require(entered.Wait(TimeSpan.FromSeconds(2)), "Snapshot must be running.");
        await Task.Run(() => { detector.MarkRecordingStopped(); detector.SuppressUntilGamesExit(); }).WaitAsync(TimeSpan.FromSeconds(1));
        release.Set(); await snapshot.WaitAsync(TimeSpan.FromSeconds(2)); block = false;
        Require(!detector.Evaluate(["game"]).ShouldStart, "An older empty snapshot must not clear manual Stop suppression.");
    }
    finally { release.Set(); await snapshot; }
});
Sync("Profile save reload update delete keeps a stable identity", () =>
{
    var library = new ProfileLibrary(Path.Combine(Folder(), "profiles.json"));
    var first = library.Save("  Spiel  ", Settings());
    Require(first.Name == "Spiel" && library.Load().Count == 1, "A profile must round-trip.");
    var updated = library.Save("Neuer Name", Settings(), first.Id);
    Require(first.Id == updated.Id && library.Load().Single().Name == "Neuer Name", "Renaming must preserve identity.");
    library.Delete(first.Id);
    Require(library.Load().Count == 0, "Deleting must persist.");
    Throws<ArgumentException>(() => library.Delete(first.Id));
    Throws<ArgumentException>(() => library.Save("", Settings()));
    Throws<ArgumentException>(() => library.Save(new string('x', 61), Settings()));
});
Sync("Profiles have a bounded count and reject unknown identities", () =>
{
    var library = new ProfileLibrary(Path.Combine(Folder(), "profiles.json"));
    for (int i = 0; i < 30; i++) library.Save("Profil " + i, Settings());
    Throws<InvalidOperationException>(() => library.Save("Profil 31", Settings()));
    Throws<ArgumentException>(() => library.Save("Unknown", Settings(), Guid.NewGuid().ToString("N")));
    Require(library.Load().Count == 30, "Rejected writes must retain the valid library.");
});
Sync("Applying a capture profile preserves storage and automation settings", () =>
{
    var current = Settings();
    current.RetentionDays = 20; current.MaxStorageGb = 77; current.AutoStopMinutes = 30;
    current.StartWithWindows = true; current.RecordOnLaunch = true; current.GameDetectionEnabled = true;
    current.GameExecutables = ["other.exe"];
    current.AnimatedBackground = false;
    var capture = Settings(); capture.Fps = 30; capture.VideoBitrateKbps = 6000; capture.DisplayId = "display";
    capture.AudioDeviceId = "headset"; capture.SingleFile = false; capture.SegmentMinutes = 15;
    capture.MicrophoneEnabled = true; capture.MicrophoneDeviceId = "mic"; capture.MicrophoneGainPercent = 150; capture.SystemGainPercent = 60;
    var result = ProfileLibrary.Apply(current, new(Guid.NewGuid().ToString("N"), "Preset", capture));
    Require(result.Fps == 30 && result.VideoBitrateKbps == 6000 && result.DisplayId == "display" && result.AudioDeviceId == "headset"
        && result.MicrophoneEnabled && result.MicrophoneGainPercent == 150 && result.SystemGainPercent == 60 && !result.SingleFile && result.SegmentMinutes == 15, "Capture fields must apply.");
    Require(result.RecordingDirectory == current.RecordingDirectory && result.RetentionDays == 20 && result.MaxStorageGb == 77
        && result.StartWithWindows && result.RecordOnLaunch && result.AutoStopMinutes == 30 && result.GameDetectionEnabled
        && result.GameExecutables.SequenceEqual(["other.exe"]) && !result.AnimatedBackground, "A preset must never replace storage, automation or appearance preferences.");
    result.GameExecutables.Add("extra.exe");
    Require(current.GameExecutables.Count == 1, "Applying must deep-clone mutable settings.");
});
Sync("Loaded profiles and returned profiles cannot mutate the stored library", () =>
{
    var library = new ProfileLibrary(Path.Combine(Folder(), "profiles.json"));
    var profile = library.Save("Game", Settings());
    profile.Settings.Fps = 60;
    var loaded = library.Load().Single(); loaded.Settings.GameExecutables.Add("game.exe");
    Require(library.Load().Single().Settings.Fps == 15 && library.Load().Single().Settings.GameExecutables.Count == 0, "Returned data must be independent.");
});
Sync("Malformed profile JSON is preserved without overwriting", () =>
{
    var folder = Folder(); Directory.CreateDirectory(folder);
    var path = Path.Combine(folder, "profiles.json");
    File.WriteAllText(path, "{broken}");
    var library = new ProfileLibrary(path);
    Throws<IOException>(() => library.Load());
    Throws<IOException>(() => library.Save("Game", Settings()));
    Require(File.ReadAllText(path) == "{broken}", "The corrupt original must remain recoverable.");
});
Sync("Oversized and duplicate-ID profile libraries are rejected", () =>
{
    var folder = Folder(); Directory.CreateDirectory(folder);
    var path = Path.Combine(folder, "profiles.json");
    File.WriteAllBytes(path, new byte[1024 * 1024 + 1]);
    var library = new ProfileLibrary(path);
    Throws<IOException>(() => library.Load());
    var profile = new RecordingProfile(Guid.NewGuid().ToString("N"), "Game", Settings());
    File.WriteAllText(path, JsonSerializer.Serialize(new[] { profile, profile }));
    Throws<IOException>(() => library.Load());
});

byte[] Zip(params (string Name, byte[] Data, int Attributes)[] entries)
{
    using var stream = new MemoryStream();
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        foreach (var (name, data, attributes) in entries)
        {
            var entry = archive.CreateEntry(name);
            if (attributes != 0) entry.ExternalAttributes = attributes;
            using var output = entry.Open(); output.Write(data);
        }
    return stream.ToArray();
}
byte[] StandardZip() => Zip(("ScreenDash-1.5.0/ScreenDash.exe", "MZ-FAKE-FOR-TEST-ONLY"u8.ToArray(), 0),
    ("ScreenDash-1.5.0/README.md", "Documentation"u8.ToArray(), 0));
UpdateInfo Info(byte[] package) => new("1.5.0", true, "https://github.com/sercan1115/ScreenDash/releases/tag/v1.5.0",
    "https://github.com/sercan1115/ScreenDash/releases/download/v1.5.0/ScreenDash-1.5.0-Windows.zip",
    "https://github.com/sercan1115/ScreenDash/releases/download/v1.5.0/ScreenDash-1.5.0-SHA256SUMS.txt", package.LongLength);
string Checksum(byte[] package) => Convert.ToHexString(SHA256.HashData(package)) + "  ScreenDash-1.5.0-Windows.zip\n";
byte[] Metadata(UpdateInfo info, bool draft = false, bool prerelease = false) => JsonSerializer.SerializeToUtf8Bytes(new
{
    tag_name = "v" + info.Version, html_url = info.ReleaseUrl, draft, prerelease,
    assets = new[] {
        new { name = "ScreenDash-" + info.Version + "-Windows.zip", state = "uploaded", size = info.Size, browser_download_url = info.DownloadUrl },
        new { name = "ScreenDash-" + info.Version + "-SHA256SUMS.txt", state = "uploaded", size = 95L, browser_download_url = info.ChecksumUrl }
    }
});
UpdateFixture Fixture(byte[]? zip = null, string? manifest = null)
{
    zip ??= StandardZip();
    var info = Info(zip);
    var handler = new FakeHandler();
    handler.Bytes["https://api.github.com/repos/sercan1115/ScreenDash/releases/latest"] = Metadata(info);
    handler.Bytes[info.DownloadUrl] = zip;
    handler.Bytes[info.ChecksumUrl] = Encoding.UTF8.GetBytes(manifest ?? Checksum(zip));
    return new(handler, new UpdateService(new HttpClient(handler)), info);
}
async Task RejectPackage(byte[] zip)
{
    var fixture = Fixture(zip); var root = Folder();
    await ThrowsAsync<IOException>(() => fixture.Service.PrepareAsync(fixture.Info, root));
    Require(!Directory.EnumerateFileSystemEntries(root).Any(), "A rejected archive must leave no executable behind.");
}

Async("Update check compares installed version and uses only the fixed repository API", async () =>
{
    var fixture = Fixture();
    var result = await fixture.Service.CheckAsync(new Version(1, 4, 0, 0));
    Require(result.IsNewer && result.Version == "1.5.0" && result.DownloadUrl == fixture.Info.DownloadUrl, "A later stable version must be offered.");
    Require(!(await fixture.Service.CheckAsync(new Version(1, 5, 0))).IsNewer, "The current version must not reinstall.");
    Require(!(await fixture.Service.CheckAsync(new Version(2, 0, 0))).IsNewer, "Older releases must not be installed.");
    Require(fixture.Handler.Requests.All(uri => uri == "https://api.github.com/repos/sercan1115/ScreenDash/releases/latest"), "Checking must not download any assets.");
});
Async("Update check rejects draft and prerelease metadata", async () =>
{
    var fixture = Fixture();
    var api = "https://api.github.com/repos/sercan1115/ScreenDash/releases/latest";
    fixture.Handler.Bytes[api] = Metadata(fixture.Info, draft: true);
    await ThrowsAsync<IOException>(() => fixture.Service.CheckAsync(new Version(1, 4, 0)));
    fixture.Handler.Bytes[api] = Metadata(fixture.Info, prerelease: true);
    await ThrowsAsync<IOException>(() => fixture.Service.CheckAsync(new Version(1, 4, 0)));
});
Async("Update check rejects foreign repository and unauthenticated HTTP URLs", async () =>
{
    var fixture = Fixture(); var api = "https://api.github.com/repos/sercan1115/ScreenDash/releases/latest";
    foreach (var bad in new[] { "https://github.com/other/ScreenDash/releases/download/v1.5.0/ScreenDash-1.5.0-Windows.zip",
        fixture.Info.DownloadUrl.Replace("https:", "http:"), fixture.Info.DownloadUrl + "?ignored=yes" })
    {
        fixture.Handler.Bytes[api] = Metadata(fixture.Info with { DownloadUrl = bad });
        await ThrowsAsync<IOException>(() => fixture.Service.CheckAsync(new Version(1, 4, 0)));
    }
});
Async("Update metadata is size bounded before JSON parsing", async () =>
{
    var fixture = Fixture();
    fixture.Handler.Bytes["https://api.github.com/repos/sercan1115/ScreenDash/releases/latest"] = new byte[1024 * 1024 + 1];
    await ThrowsAsync<IOException>(() => fixture.Service.CheckAsync(new Version(1, 4, 0)));
});
Async("Valid update prepares its verified EXE in a new private directory", async () =>
{
    var fixture = Fixture(); var root = Folder();
    var prepared = await fixture.Service.PrepareAsync(fixture.Info, root);
    Require(prepared.Version == "1.5.0" && File.Exists(prepared.ExecutablePath) && Path.GetDirectoryName(prepared.Directory) == root,
        "Preparation must use a new direct child directory.");
    Require(File.Exists(Path.Combine(prepared.Directory, "verified-update.json")) && !Directory.EnumerateFiles(prepared.Directory, "*.zip").Any(), "Verified receipt remains and package is removed.");
    var second = await fixture.Service.PrepareAsync(fixture.Info, root);
    Require(prepared.Directory != second.Directory && File.Exists(prepared.ExecutablePath), "A second download must never overwrite the first.");
});
Async("SHA mismatch prevents preparation and removes the rejected package", async () =>
{
    var fixture = Fixture(manifest: new string('0', 64) + "  ScreenDash-1.5.0-Windows.zip\n"); var root = Folder();
    await ThrowsAsync<IOException>(() => fixture.Service.PrepareAsync(fixture.Info, root));
    Require(!Directory.EnumerateFileSystemEntries(root).Any(), "No unverified update may remain launchable.");
});
Async("Checksum manifest requires one exact filename and one value", async () =>
{
    var hash = new string('a', 64);
    Require(UpdateService.ParseChecksum(hash + " *package.zip\n", "package.zip") == hash, "Binary sha256sum syntax must work.");
    await ThrowsAsync<IOException>(() => Task.FromResult(UpdateService.ParseChecksum(hash + "  other.zip\n", "package.zip")));
    await ThrowsAsync<IOException>(() => Task.FromResult(UpdateService.ParseChecksum(hash + "  package.zip\n" + hash + "  package.zip\n", "package.zip")));
    await ThrowsAsync<IOException>(() => Task.FromResult(UpdateService.ParseChecksum("invalid manifest", "package.zip")));
});
Async("Package size mismatch is rejected and cleaned up", async () =>
{
    var fixture = Fixture(); var root = Folder();
    await ThrowsAsync<IOException>(() => fixture.Service.PrepareAsync(fixture.Info with { Size = fixture.Info.Size + 1 }, root));
    Require(!Directory.EnumerateFileSystemEntries(root).Any(), "Incomplete downloads must be removed.");
});
Async("Untrusted or excessive updates are rejected before any download", async () =>
{
    var fixture = Fixture();
    await ThrowsAsync<IOException>(() => fixture.Service.PrepareAsync(fixture.Info with { DownloadUrl = "https://example.com/program.zip" }, Folder()));
    await ThrowsAsync<IOException>(() => fixture.Service.PrepareAsync(fixture.Info with { Size = 256L * 1024 * 1024 + 1 }, Folder()));
    await ThrowsAsync<IOException>(() => fixture.Service.PrepareAsync(fixture.Info with { IsNewer = false }, Folder()));
    Require(fixture.Handler.Requests.Count == 0, "Rejected update descriptions must not access the network.");
});
Async("User cancellation cleans its fresh folder and preserves existing update folders", async () =>
{
    var fixture = Fixture(); var root = Folder(); Directory.CreateDirectory(root);
    var retained = Path.Combine(root, "previous"); Directory.CreateDirectory(retained); File.WriteAllText(Path.Combine(retained, "keep.txt"), "keep");
    using var canceled = new CancellationTokenSource(); canceled.Cancel();
    await ThrowsAsync<OperationCanceledException>(() => fixture.Service.PrepareAsync(fixture.Info, root, cancellationToken: canceled.Token));
    Require(Directory.EnumerateDirectories(root).SequenceEqual([retained]), "Cancellation must not remove earlier verified files.");
});
Async("Archive rejects traversal and absolute entries", async () =>
{
    foreach (var name in new[] { "../outside.exe", "/outside.exe", "C:\\outside.exe", "inside/../../outside.exe" })
        await RejectPackage(Zip((name, "MZ-bad"u8.ToArray(), 0), ("ScreenDash.exe", "MZ-good"u8.ToArray(), 0)));
});
Async("Archive rejects ADS and ambiguous Windows reserved names", async () =>
{
    foreach (var name in new[] { "ScreenDash.exe:other", "CON.txt", "LPT1.txt", "folder./file.txt", "folder /file.txt" })
        await RejectPackage(Zip((name, "bad"u8.ToArray(), 0), ("ScreenDash.exe", "MZ-good"u8.ToArray(), 0)));
});
Async("Archive rejects symbolic links and reparse attributes", async () =>
{
    await RejectPackage(Zip(("link", "target"u8.ToArray(), unchecked((int)0xA1FF0000)), ("ScreenDash.exe", "MZ-good"u8.ToArray(), 0)));
    await RejectPackage(Zip(("link", "target"u8.ToArray(), (int)FileAttributes.ReparsePoint), ("ScreenDash.exe", "MZ-good"u8.ToArray(), 0)));
});
Async("Archive rejects duplicate paths including case-only aliases", async () =>
{
    await RejectPackage(Zip(("ScreenDash.exe", "MZ-one"u8.ToArray(), 0), ("screendash.EXE", "MZ-two"u8.ToArray(), 0)));
});
Async("Archive requires exactly one Windows program", async () =>
{
    await RejectPackage(Zip(("readme.txt", "No EXE"u8.ToArray(), 0)));
    await RejectPackage(Zip(("ScreenDash.exe", "not a Windows EXE"u8.ToArray(), 0)));
    await RejectPackage(Zip(("one/ScreenDash.exe", "MZ-one"u8.ToArray(), 0), ("two/ScreenDash.exe", "MZ-two"u8.ToArray(), 0)));
});
Async("Archive entry count is bounded", async () =>
{
    var entries = Enumerable.Range(0, 501).Select(i => ("file" + i + ".txt", Array.Empty<byte>(), 0)).ToArray();
    await RejectPackage(Zip(entries));
});
Async("Download redirect rejects an unrelated host before following it", async () =>
{
    var fixture = Fixture();
    fixture.Handler.Redirects[fixture.Info.ChecksumUrl] = "https://example.com/hashes.txt";
    await ThrowsAsync<IOException>(() => fixture.Service.PrepareAsync(fixture.Info, Folder()));
    Require(fixture.Handler.Requests.Count == 1, "An unrelated redirect must never be requested.");
});
Async("Download permits the explicit GitHub HTTPS asset host", async () =>
{
    var fixture = Fixture();
    var target = "https://release-assets.githubusercontent.com/github-production-release-asset/hashes.txt?signature=test";
    fixture.Handler.Redirects[fixture.Info.ChecksumUrl] = target;
    fixture.Handler.Bytes[target] = fixture.Handler.Bytes[fixture.Info.ChecksumUrl];
    var prepared = await fixture.Service.PrepareAsync(fixture.Info, Folder());
    Require(File.Exists(prepared.ExecutablePath) && fixture.Handler.Requests.Contains(target), "GitHub's asset redirect must work.");
});
Sync("Installer script quotes literal paths and verifies target before launch", () =>
{
    var exe = "C:\\Users\\O'Brien\\ScreenDash 1.5\\ScreenDash.exe";
    var script = UpdateService.BuildLauncherScript(exe, 1234, 9876, new string('a', 64), true, true);
    Require(script.Contains("O''Brien") && script.Contains("Get-Process -Id 1234") && script.Contains("Ticks -ne 9876"), "Paths and PID reuse checks must be explicit.");
    Require(script.IndexOf("Get-FileHash", StringComparison.Ordinal) < script.IndexOf("Start-Process", StringComparison.Ordinal)
        && script.Contains("-ArgumentList '--tray'") && script.Contains("Set-ItemProperty -LiteralPath"), "Hash validation must precede launch and preserve enabled startup.");
    Require(!script.Contains("Invoke-Expression") && !script.Contains("Remove-Item"), "The helper must not evaluate arbitrary input or remove existing apps.");
});
Async("Installer helper parses in Windows PowerShell without executing the helper", async () =>
{
    var scriptFile = Path.Combine(scratch, "quoted-installer.ps1");
    File.WriteAllText(scriptFile, UpdateService.BuildLauncherScript("C:\\Users\\O'Brien\\ScreenDash 1.5\\ScreenDash.exe", 1234, 638000000000000000,
        new string('a', 64), true, true));
    var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
    var info = new ProcessStartInfo(powershell) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
    info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive"); info.ArgumentList.Add("-Command");
    info.ArgumentList.Add("$tokens=$null;$parseErrors=$null;[System.Management.Automation.Language.Parser]::ParseFile('"
        + scriptFile.Replace("'", "''", StringComparison.Ordinal)
        + "',[ref]$tokens,[ref]$parseErrors)|Out-Null;if($parseErrors.Count -ne 0){$parseErrors|ForEach-Object {$_.Message};exit 1};exit 0");
    using var parser = Process.Start(info)!;
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    try { await parser.WaitForExitAsync(timeout.Token); }
    catch (OperationCanceledException) { if (!parser.HasExited) parser.Kill(true); throw; }
    Require(parser.ExitCode == 0, "PowerShell parser rejected the generated script: " + await parser.StandardOutput.ReadToEndAsync() + await parser.StandardError.ReadToEndAsync());
});
Async("Installer refuses unknown receipts and modified prepared executables without launching", async () =>
{
    var fixture = Fixture();
    Throws<IOException>(() => fixture.Service.LaunchInstaller(new("1.5.0", Folder(), Path.Combine(Folder(), "ScreenDash.exe")), Environment.ProcessId, false, false));
    var prepared = await fixture.Service.PrepareAsync(fixture.Info, Folder());
    File.AppendAllText(prepared.ExecutablePath, "tampered");
    Throws<IOException>(() => fixture.Service.LaunchInstaller(prepared, Environment.ProcessId, false, false));
});

int failures = 0;
try
{
    foreach (var (name, run) in checks)
    {
        try { await run(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex); }
    }
    Console.WriteLine($"Feature checks: {checks.Count - failures}/{checks.Count} passed.");
    return failures == 0 ? 0 : 1;
}
finally { Directory.Delete(scratch, true); }

sealed record UpdateFixture(FakeHandler Handler, UpdateService Service, UpdateInfo Info);
sealed class FakeHandler : HttpMessageHandler
{
    public Dictionary<string, byte[]> Bytes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Redirects { get; } = new(StringComparer.Ordinal);
    public List<string> Requests { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var url = request.RequestUri!.AbsoluteUri;
        Requests.Add(url);
        if (Redirects.TryGetValue(url, out var target))
        {
            var redirected = new HttpResponseMessage(HttpStatusCode.Redirect) { RequestMessage = request };
            redirected.Headers.Location = new Uri(target);
            return Task.FromResult(redirected);
        }
        return Task.FromResult(new HttpResponseMessage(Bytes.ContainsKey(url) ? HttpStatusCode.OK : HttpStatusCode.NotFound)
        { RequestMessage = request, Content = new ByteArrayContent(Bytes.GetValueOrDefault(url, [])) });
    }
}
