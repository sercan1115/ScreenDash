using System.Diagnostics;
using System.Runtime.InteropServices;
using ScreenDash;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Every desktopDirectory is explicitly a private scratch folder. These tests never
        // touch the user's Desktop, registry, settings, capture devices, or taskbar pins.
        var tests = new (string Name, Action Run)[]
        {
            ("native shortcut roundtrip preserves all launch properties", NativeRoundtrip),
            ("spaces and Unicode in the portable folder are preserved", UnicodeRoundtrip),
            ("unchanged native shortcut is not rewritten", UnchangedShortcut),
            ("owned shortcut follows the new portable version folder", UpdateOwnedShortcut),
            ("owned shortcut remains updatable after its old target was removed", RemovedOldTarget),
            ("unrelated native shortcut remains byte-identical", ForeignShortcut),
            ("matching description without app identity does not claim ownership", MatchingDescriptionIsForeign),
            ("malformed existing shortcut remains byte-identical", MalformedShortcut),
            ("shortcut directory and its contents remain intact", ShortcutDirectory),
            ("shortcut junction is never followed or replaced", ShortcutJunction),
            ("desktop junction is never traversed", DesktopJunction),
            ("target parent junction is never traversed", TargetParentJunction),
            ("missing executable cannot create a shortcut", MissingTarget),
            ("directory posing as executable cannot create a shortcut", DirectoryTarget),
            ("unrelated executable name cannot create a shortcut", WrongExecutableName),
            ("missing desktop folder is not created", MissingDesktop),
            ("malformed shortcut cannot be read as trusted details", ReadMalformedShortcut),
            ("locked owned shortcut survives failed update with no temporary file", LockedShortcut),
            ("read-only owned shortcut survives failed update with no temporary file", ReadOnlyShortcut),
            ("native shortcut reads are non-mutating", ReadDoesNotWrite)
        };
        int failures = 0;
        foreach (var test in tests)
        {
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex); }
        }
        Console.WriteLine($"{tests.Length - failures}/{tests.Length} Windows integration tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void NativeRoundtrip()
    {
        using var scratch = new Scratch();
        string executable = scratch.Executable("version-1");
        string shortcut = WindowsIntegration.EnsureDesktopShortcut(executable, scratch.Desktop);
        Check(shortcut == Path.Combine(scratch.Desktop, "ScreenDash.lnk"), "The app should use a single predictable desktop link.");
        AssertDetails(shortcut, executable);
        AssertNativeShellDetails(shortcut, executable);
        AssertOnlyShortcut(scratch.Desktop);
        Check(File.ReadAllText(executable) == Scratch.DummyExecutable, "Creating a link must not alter or run the target.");
    }

    private static void UnicodeRoundtrip()
    {
        using var scratch = new Scratch();
        string executable = scratch.Executable("ScreenDash 1.4.2 – Test ä 日本語");
        string shortcut = WindowsIntegration.EnsureDesktopShortcut(executable, scratch.Desktop);
        AssertDetails(shortcut, executable);
        AssertUnicodeLinkInfo(shortcut, executable);
        AssertOnlyShortcut(scratch.Desktop);
    }

    private static void AssertUnicodeLinkInfo(string shortcut, string executable)
    {
        // Independently inspect Microsoft's MS-SHLLINK LinkInfo fields. WScript.Shell
        // reads the ANSI fallback on this Windows host, losing Japanese characters.
        // https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-shllink/6813269d-0cc8-4be2-933f-e96e8e3412dc
        byte[] bytes = File.ReadAllBytes(shortcut);
        Check(bytes.Length >= 76 && BitConverter.ToUInt32(bytes, 0) == 76, "The native ShellLinkHeader must be complete.");
        uint flags = BitConverter.ToUInt32(bytes, 20);
        int infoOffset = 76;
        if ((flags & 1) != 0) infoOffset = checked(infoOffset + 2 + BitConverter.ToUInt16(bytes, infoOffset));
        Check((flags & 2) != 0 && infoOffset + 36 <= bytes.Length, "The saved Unicode shortcut must contain LinkInfo.");
        int size = checked((int)BitConverter.ToUInt32(bytes, infoOffset));
        int headerSize = checked((int)BitConverter.ToUInt32(bytes, infoOffset + 4));
        Check(headerSize >= 36 && size >= headerSize && infoOffset + size <= bytes.Length, "Unicode LinkInfo offsets must fit within the native section.");
        string ReadUnicodeField(int offsetField)
        {
            int relative = checked((int)BitConverter.ToUInt32(bytes, infoOffset + offsetField));
            Check(relative >= headerSize && relative < size, "The UTF-16 target field must be inside LinkInfo.");
            int start = infoOffset + relative, end = start;
            while (end + 1 < infoOffset + size && BitConverter.ToUInt16(bytes, end) != 0) end += 2;
            Check(end + 1 < infoOffset + size, "The native UTF-16 path must be terminated.");
            return System.Text.Encoding.Unicode.GetString(bytes, start, end - start);
        }
        string target = ReadUnicodeField(28) + ReadUnicodeField(32);
        Check(SamePath(target, executable), "The independent persisted UTF-16 target must preserve every Unicode character: " + target);
    }

    private static void UnchangedShortcut()
    {
        using var scratch = new Scratch();
        string executable = scratch.Executable("version-1");
        string shortcut = WindowsIntegration.EnsureDesktopShortcut(executable, scratch.Desktop);
        byte[] previous = File.ReadAllBytes(shortcut);
        var stamp = new DateTime(2026, 10, 14, 11, 22, 33, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(shortcut, stamp);
        string again = WindowsIntegration.EnsureDesktopShortcut(executable, scratch.Desktop);
        Check(again == shortcut && previous.SequenceEqual(File.ReadAllBytes(shortcut)), "A repeated launch must reuse the same native shortcut.");
        Check(File.GetLastWriteTimeUtc(shortcut) == stamp, "An unchanged link must not produce an extra desktop write.");
        AssertOnlyShortcut(scratch.Desktop);
    }

    private static void UpdateOwnedShortcut()
    {
        using var scratch = new Scratch();
        string oldTarget = scratch.Executable("version-1");
        string newTarget = scratch.Executable("version-2");
        string shortcut = WindowsIntegration.EnsureDesktopShortcut(oldTarget, scratch.Desktop);
        Check(WindowsIntegration.EnsureDesktopShortcut(newTarget, scratch.Desktop) == shortcut, "An update must replace the existing app-owned link, not add another icon.");
        AssertDetails(shortcut, newTarget);
        AssertNativeShellDetails(shortcut, newTarget);
        Check(File.ReadAllText(oldTarget) == Scratch.DummyExecutable, "Updating a link must preserve the older portable application.");
        AssertOnlyShortcut(scratch.Desktop);
    }

    private static void RemovedOldTarget()
    {
        using var scratch = new Scratch();
        string oldTarget = scratch.Executable("version-1");
        string shortcut = WindowsIntegration.EnsureDesktopShortcut(oldTarget, scratch.Desktop);
        File.Delete(oldTarget);
        string newTarget = scratch.Executable("version-2");
        WindowsIntegration.EnsureDesktopShortcut(newTarget, scratch.Desktop);
        AssertDetails(shortcut, newTarget);
        Check(!File.Exists(oldTarget), "A missing old executable must not be restored or resolved.");
        AssertOnlyShortcut(scratch.Desktop);
    }

    private static void ForeignShortcut() => PreserveForeignShortcut("Unrelated application");
    private static void MatchingDescriptionIsForeign() => PreserveForeignShortcut(WindowsIntegration.ShortcutDescription);
    private static void PreserveForeignShortcut(string description)
    {
        using var scratch = new Scratch();
        string executable = scratch.Executable("version-1");
        string foreign = scratch.Executable("foreign");
        string shortcut = Path.Combine(scratch.Desktop, "ScreenDash.lnk");
        WithShellShortcut(shortcut, link =>
        {
            link.TargetPath = foreign; link.WorkingDirectory = Path.GetDirectoryName(foreign);
            link.Arguments = "--foreign-argument"; link.Description = description;
            link.WindowStyle = 3; link.IconLocation = foreign + ",0"; link.Save();
        });
        byte[] previous = File.ReadAllBytes(shortcut);
        DateTime stamp = File.GetLastWriteTimeUtc(shortcut);
        Throws<IOException>(() => WindowsIntegration.EnsureDesktopShortcut(executable, scratch.Desktop));
        Check(previous.SequenceEqual(File.ReadAllBytes(shortcut)) && File.GetLastWriteTimeUtc(shortcut) == stamp, "An unowned link must remain unchanged even when its file name or description matches.");
        AssertOnlyShortcut(scratch.Desktop);
    }

    private static void MalformedShortcut()
    {
        using var scratch = new Scratch();
        string executable = scratch.Executable("version-1");
        string shortcut = Path.Combine(scratch.Desktop, "ScreenDash.lnk");
        byte[] malformed = [1, 2, 3, 4, 5, 6];
        File.WriteAllBytes(shortcut, malformed);
        Throws<IOException>(() => WindowsIntegration.EnsureDesktopShortcut(executable, scratch.Desktop));
        Check(malformed.SequenceEqual(File.ReadAllBytes(shortcut)), "Unreadable links cannot be treated as owned or silently overwritten.");
        AssertOnlyShortcut(scratch.Desktop);
    }

    private static void ShortcutDirectory()
    {
        using var scratch = new Scratch();
        string executable = scratch.Executable("version-1");
        string shortcut = Path.Combine(scratch.Desktop, "ScreenDash.lnk");
        Directory.CreateDirectory(shortcut);
        string sentinel = Path.Combine(shortcut, "preserve.txt");
        File.WriteAllText(sentinel, "preserve directory");
        Throws<IOException>(() => WindowsIntegration.EnsureDesktopShortcut(executable, scratch.Desktop));
        Check(Directory.Exists(shortcut) && File.ReadAllText(sentinel) == "preserve directory", "A colliding directory must never be replaced or removed.");
        Check(Directory.GetFileSystemEntries(scratch.Desktop).Length == 1, "Rejected collisions must not leave temporary shortcut files.");
    }

    private static void ShortcutJunction()
    {
        using var scratch = new Scratch();
        string executable = scratch.Executable("version-1");
        string target = scratch.Directory("junction-target");
        string sentinel = Path.Combine(target, "preserve.txt"); File.WriteAllText(sentinel, "preserve junction target");
        string shortcut = Path.Combine(scratch.Desktop, "ScreenDash.lnk");
        CreateJunction(shortcut, target);
        Throws<IOException>(() => WindowsIntegration.EnsureDesktopShortcut(executable, scratch.Desktop));
        Check((File.GetAttributes(shortcut) & FileAttributes.ReparsePoint) != 0 && File.ReadAllText(sentinel) == "preserve junction target", "Existing reparse links and their targets must remain intact.");
        Check(Directory.GetFileSystemEntries(scratch.Desktop).Length == 1, "A refused reparse link must not leave temporary files.");
    }

    private static void DesktopJunction()
    {
        using var scratch = new Scratch();
        string executable = scratch.Executable("version-1");
        string target = scratch.Directory("external-desktop");
        string link = Path.Combine(scratch.Root, "linked-desktop"); CreateJunction(link, target);
        Throws<IOException>(() => WindowsIntegration.EnsureDesktopShortcut(executable, link));
        Check(Directory.GetFileSystemEntries(target).Length == 0 && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "A reparse desktop directory must not be traversed or modified.");
    }

    private static void TargetParentJunction()
    {
        using var scratch = new Scratch();
        string target = scratch.Executable("target-real");
        string link = Path.Combine(scratch.Root, "target-link"); CreateJunction(link, Path.GetDirectoryName(target)!);
        RejectTarget(() => WindowsIntegration.EnsureDesktopShortcut(Path.Combine(link, "ScreenDash.exe"), scratch.Desktop));
        Check(File.ReadAllText(target) == Scratch.DummyExecutable && Directory.GetFileSystemEntries(scratch.Desktop).Length == 0, "A target under a reparse parent must not create a shortcut or modify its target.");
    }

    private static void MissingTarget()
    {
        using var scratch = new Scratch();
        RejectTarget(() => WindowsIntegration.EnsureDesktopShortcut(Path.Combine(scratch.Root, "ScreenDash.exe"), scratch.Desktop));
        Check(Directory.GetFileSystemEntries(scratch.Desktop).Length == 0, "A missing executable must not produce an orphan desktop icon.");
    }

    private static void DirectoryTarget()
    {
        using var scratch = new Scratch();
        string target = scratch.Directory("ScreenDash.exe");
        RejectTarget(() => WindowsIntegration.EnsureDesktopShortcut(target, scratch.Desktop));
        Check(Directory.Exists(target) && Directory.GetFileSystemEntries(scratch.Desktop).Length == 0, "A directory cannot be accepted as a launch target.");
    }

    private static void WrongExecutableName()
    {
        using var scratch = new Scratch();
        string target = Path.Combine(scratch.Root, "OtherApp.exe"); File.WriteAllText(target, Scratch.DummyExecutable);
        RejectTarget(() => WindowsIntegration.EnsureDesktopShortcut(target, scratch.Desktop));
        Check(File.ReadAllText(target) == Scratch.DummyExecutable && Directory.GetFileSystemEntries(scratch.Desktop).Length == 0, "An unrelated executable cannot become the ScreenDash desktop target.");
    }

    private static void MissingDesktop()
    {
        using var scratch = new Scratch();
        string executable = scratch.Executable("version-1");
        string missing = Path.Combine(scratch.Root, "does-not-exist");
        Throws<IOException>(() => WindowsIntegration.EnsureDesktopShortcut(executable, missing));
        Check(!Directory.Exists(missing), "Invalid desktop locations must not be created implicitly.");
    }

    private static void ReadMalformedShortcut()
    {
        using var scratch = new Scratch();
        string path = Path.Combine(scratch.Root, "malformed.lnk"); File.WriteAllText(path, "this is not a Windows shell link");
        Throws<IOException>(() => WindowsIntegration.ReadShortcut(path));
        Check(File.ReadAllText(path) == "this is not a Windows shell link", "Reading malformed data must be non-mutating.");
    }

    private static void LockedShortcut()
    {
        using var scratch = new Scratch();
        string executable = scratch.Executable("version-1");
        string replacement = scratch.Executable("version-2");
        string shortcut = WindowsIntegration.EnsureDesktopShortcut(executable, scratch.Desktop);
        byte[] previous = File.ReadAllBytes(shortcut);
        using (var locked = new FileStream(shortcut, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Throws<IOException>(() => WindowsIntegration.EnsureDesktopShortcut(replacement, scratch.Desktop));
            Check(previous.SequenceEqual(File.ReadAllBytes(shortcut)), "A failed replacement must preserve the complete existing link.");
            AssertOnlyShortcut(scratch.Desktop);
        }
        AssertDetails(shortcut, executable);
        WindowsIntegration.EnsureDesktopShortcut(replacement, scratch.Desktop);
        AssertDetails(shortcut, replacement);
        AssertOnlyShortcut(scratch.Desktop);
    }

    private static void ReadOnlyShortcut()
    {
        using var scratch = new Scratch();
        string executable = scratch.Executable("version-1");
        string replacement = scratch.Executable("version-2");
        string shortcut = WindowsIntegration.EnsureDesktopShortcut(executable, scratch.Desktop);
        byte[] previous = File.ReadAllBytes(shortcut);
        File.SetAttributes(shortcut, File.GetAttributes(shortcut) | FileAttributes.ReadOnly);
        try
        {
            Throws<IOException>(() => WindowsIntegration.EnsureDesktopShortcut(replacement, scratch.Desktop));
            Check(previous.SequenceEqual(File.ReadAllBytes(shortcut)) && (File.GetAttributes(shortcut) & FileAttributes.ReadOnly) != 0, "An owned read-only link must survive a refused update unchanged.");
            AssertOnlyShortcut(scratch.Desktop);
        }
        finally { File.SetAttributes(shortcut, File.GetAttributes(shortcut) & ~FileAttributes.ReadOnly); }
    }

    private static void ReadDoesNotWrite()
    {
        using var scratch = new Scratch();
        string executable = scratch.Executable("version-1");
        string shortcut = WindowsIntegration.EnsureDesktopShortcut(executable, scratch.Desktop);
        byte[] previous = File.ReadAllBytes(shortcut);
        var stamp = new DateTime(2026, 10, 14, 10, 11, 12, DateTimeKind.Utc); File.SetLastWriteTimeUtc(shortcut, stamp);
        AssertDetails(shortcut, executable); AssertNativeShellDetails(shortcut, executable);
        Check(previous.SequenceEqual(File.ReadAllBytes(shortcut)) && File.GetLastWriteTimeUtc(shortcut) == stamp, "Inspection must not resolve, repair, or rewrite a saved link.");
        AssertOnlyShortcut(scratch.Desktop);
    }

    private static void AssertDetails(string shortcut, string executable)
    {
        var details = WindowsIntegration.ReadShortcut(shortcut);
        Check(SamePath(details.TargetPath, executable), "The saved native target must point at this portable executable.");
        Check(SamePath(details.WorkingDirectory, Path.GetDirectoryName(executable)!), "The working directory must be the portable application folder.");
        Check(details.Arguments.Length == 0, "Desktop startup must not inherit --tray or any other launch argument.");
        Check(SamePath(details.IconPath, executable) && details.IconIndex == 0, "The desktop icon must use the executable's own icon.");
        Check(details.ShowCommand == 1, "The shortcut must launch a normal visible window.");
        Check(details.Description == WindowsIntegration.ShortcutDescription && details.AppId == WindowsIntegration.AppId, "The native persisted description and Windows app identity must match ownership.");
    }

    private static void AssertNativeShellDetails(string shortcut, string executable) => WithShellShortcut(shortcut, link =>
    {
        // An independent Windows automation reader verifies actual .lnk persistence.
        Check(SamePath((string)link.TargetPath, executable), $"Windows itself must read the executable from the native link. Actual: {(string)link.TargetPath}; expected: {executable}");
        Check(SamePath((string)link.WorkingDirectory, Path.GetDirectoryName(executable)!), "Windows itself must read the correct working directory.");
        Check((string)link.Arguments == "" && (int)link.WindowStyle == 1, "Windows must read normal startup without --tray.");
        string icon = (string)link.IconLocation;
        int comma = icon.LastIndexOf(',');
        Check(comma > 0 && icon[(comma + 1)..].Trim() == "0" && SamePath(icon[..comma].Trim().Trim('"'), executable), "Windows must read icon index zero from the portable executable.");
        Check((string)link.Description == WindowsIntegration.ShortcutDescription, "Windows must read the saved ScreenDash description.");
    });

    private static void WithShellShortcut(string path, Action<dynamic> action)
    {
        object? shell = null, link = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("The native Windows shell automation reader is unavailable."));
            link = ((dynamic)shell!).CreateShortcut(path);
            action(link);
        }
        finally
        {
            if (link is not null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    private static bool SamePath(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    private static void AssertOnlyShortcut(string desktop)
    {
        var files = Directory.GetFileSystemEntries(desktop);
        Check(files.Length == 1 && files[0] == Path.Combine(desktop, "ScreenDash.lnk"), "The desktop should contain exactly one final link and no temporary files.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }
    private static void RejectTarget(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (IOException) { return; }
        throw new InvalidOperationException("An invalid launch target must be rejected.");
    }
    private static void CreateJunction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"", UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        }) ?? throw new IOException("Could not create the isolated test junction.");
        string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd(); process.WaitForExit();
        Check(process.ExitCode == 0 && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "Could not create the isolated test junction: " + output + error);
    }

    private sealed class Scratch : IDisposable
    {
        public const string DummyExecutable = "Isolated shell-link target. This is data, never executed.";
        public string Root { get; } = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ScreenDash.WindowsTests-" + Guid.NewGuid().ToString("N")));
        public string Desktop { get; }
        public Scratch() { System.IO.Directory.CreateDirectory(Root); Desktop = Directory("private-desktop"); }
        public string Directory(string name) { string path = Path.Combine(Root, name); System.IO.Directory.CreateDirectory(path); return path; }
        public string Executable(string folder) { string path = Path.Combine(Directory(folder), "ScreenDash.exe"); File.WriteAllText(path, DummyExecutable); return path; }
        public void Dispose() => RemovePrivateTree(Root);
        private void RemovePrivateTree(string path)
        {
            string resolved = Path.GetFullPath(path);
            if (resolved != Root && !resolved.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Scratch cleanup escaped its private root.");
            var attributes = File.GetAttributes(resolved);
            if ((attributes & FileAttributes.Directory) == 0)
            {
                if ((attributes & FileAttributes.ReparsePoint) == 0 && (attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(resolved, attributes & ~FileAttributes.ReadOnly);
                File.Delete(resolved); return;
            }
            if ((attributes & FileAttributes.ReparsePoint) == 0)
                foreach (string child in System.IO.Directory.GetFileSystemEntries(resolved)) RemovePrivateTree(child);
            System.IO.Directory.Delete(resolved, recursive: false);
        }
    }
}
