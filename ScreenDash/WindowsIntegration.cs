using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ScreenDash;

public sealed record ShortcutDetails(string TargetPath, string WorkingDirectory, string Arguments,
    string IconPath, int IconIndex, int ShowCommand, string Description, string AppId);

/// <summary>Small, local Windows shell operations. No target is resolved or launched here.</summary>
public static class WindowsIntegration
{
    public const string AppId = "ScreenDash.App";
    public const string ShortcutDescription = "ScreenDash · Lokale Bildschirm-Dashcam";
    private const int NormalWindow = 1;
    private const long MaximumShortcutBytes = 1024 * 1024;
    private static readonly PropertyKey AppIdKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    public static void SetAppIdentity() => Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId));

    public static bool TryRestoreExistingWindow()
    {
        try { return RestoreExistingWindow(); }
        catch (Win32Exception) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static bool RestoreExistingWindow()
    {
        int session;
        using (var current = Process.GetCurrentProcess()) session = current.SessionId;
        var processIds = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName("ScreenDash"))
        {
            using (process)
            {
                try { if (process.Id != Environment.ProcessId && process.SessionId == session) processIds.Add((uint)process.Id); }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }
        }
        if (processIds.Count == 0) return false;
        nint found = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint processId);
            if (!processIds.Contains(processId) || GetWindow(window, 4) != 0) return true;
            var title = new StringBuilder(256);
            GetWindowText(window, title, title.Capacity);
            if (!title.ToString().StartsWith("ScreenDash", StringComparison.Ordinal)) return true;
            found = window;
            return false;
        }, 0);
        if (found == 0) return false;
        // SW_RESTORE also brings an old tray-hidden ScreenDash window back onto the desktop.
        ShowWindow(found, 9);
        SetForegroundWindow(found);
        return IsWindowVisible(found);
    }

    public static string EnsureDesktopShortcut(string executablePath, string? desktopDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        string target = Path.GetFullPath(executablePath);
        if (!Path.GetFileName(target).Equals("ScreenDash.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Die Verknüpfung benötigt eine vorhandene ScreenDash.exe.", nameof(executablePath));
        RequireRegularFile(target);
        string directory = Path.GetFullPath(desktopDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        RequireUnredirectedParents(directory);
        var attributes = File.GetAttributes(directory);
        if ((attributes & FileAttributes.Directory) == 0 || IsUnsupportedReparsePoint(directory, attributes))
            throw new IOException("Der Desktopordner darf keine umgeleitete Verknüpfung sein.");
        return InSta(() => EnsureShortcutCore(target, directory));
    }

    public static ShortcutDetails ReadShortcut(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        return InSta(() => ReadShortcutCore(fullPath));
    }

    private static string EnsureShortcutCore(string target, string directory)
    {
        string path = Path.Combine(directory, "ScreenDash.lnk");
        var desired = new ShortcutDetails(target, Path.GetDirectoryName(target)!, "", target, 0, NormalWindow, ShortcutDescription, AppId);
        var initialAttributes = AttributesOrMissing(path);
        if (initialAttributes.HasValue)
        {
            var existing = ReadShortcutCore(path);
            RequireOwned(existing);
            if (Matches(existing, desired)) return path;
            if ((initialAttributes.Value & FileAttributes.ReadOnly) != 0)
                throw new IOException("Die ScreenDash-Verknüpfung ist schreibgeschützt und bleibt unverändert.");
        }

        string temporary = Path.Combine(directory, $".ScreenDash-{Guid.NewGuid():N}.tmp.lnk");
        bool temporaryOwned = false;
        try
        {
            // Reserve only our own random name. The native shell writer does not receive the final path.
            using (new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            temporaryOwned = true;
            WriteShortcut(temporary, desired);
            if (!Matches(ReadShortcutCore(temporary), desired))
                throw new IOException("Windows hat die Verknüpfung nicht vollständig gespeichert.");

            // Recheck before the atomic move: an intervening foreign or malformed link is preserved.
            var finalAttributes = AttributesOrMissing(path);
            if (initialAttributes.HasValue)
            {
                if (!finalAttributes.HasValue) throw new IOException("Die Desktopverknüpfung wurde zwischenzeitlich verändert.");
                RequireOwned(ReadShortcutCore(path));
                if ((finalAttributes.Value & FileAttributes.ReadOnly) != 0)
                    throw new IOException("Die ScreenDash-Verknüpfung ist schreibgeschützt und bleibt unverändert.");
                File.Move(temporary, path, overwrite: true);
            }
            else
            {
                // Create-only avoids overwriting any shortcut that appeared during creation.
                File.Move(temporary, path, overwrite: false);
            }
            temporaryOwned = false;
            return path;
        }
        catch (COMException ex) { throw new IOException("Windows konnte die Desktopverknüpfung nicht speichern.", ex); }
        catch (UnauthorizedAccessException ex) { throw new IOException("Die vorhandene Desktopverknüpfung kann nicht ersetzt werden und bleibt unverändert.", ex); }
        finally
        {
            if (temporaryOwned)
            {
                try
                {
                    var attributes = AttributesOrMissing(temporary);
                    if (attributes.HasValue && (attributes.Value & FileAttributes.Directory) == 0 && !IsUnsupportedReparsePoint(temporary, attributes.Value))
                        File.Delete(temporary);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static ShortcutDetails ReadShortcutCore(string path)
    {
        using var guard = OpenRegularFileGuard(path);
        if (RandomAccess.GetLength(guard) is <= 0 or > MaximumShortcutBytes)
            throw new IOException("Die vorhandene Desktopverknüpfung ist ungültig und bleibt unverändert.");
        object? shell = null;
        try
        {
            shell = new ShellLink();
            ((IPersistFile)shell).Load(path, 0);
            var link = (IShellLinkW)shell;
            var target = new StringBuilder(32768);
            var workingDirectory = new StringBuilder(32768);
            var arguments = new StringBuilder(32768);
            var icon = new StringBuilder(32768);
            var description = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, 0, 4);
            link.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);
            link.GetArguments(arguments, arguments.Capacity);
            link.GetIconLocation(icon, icon.Capacity, out int iconIndex);
            link.GetDescription(description, description.Capacity);
            link.GetShowCmd(out int showCommand);
            if (target.Length == 0)
                throw new IOException("Die vorhandene Desktopverknüpfung enthält kein gültiges Dateiziel und bleibt unverändert.");
            var propertyKey = AppIdKey;
            ((IPropertyStore)shell).GetValue(ref propertyKey, out var appId);
            string appIdText;
            try { appIdText = appId.StringValue ?? ""; }
            finally { appId.Clear(); }
            return new(target.ToString(), workingDirectory.ToString(), arguments.ToString(), icon.ToString(), iconIndex,
                showCommand, description.ToString(), appIdText);
        }
        catch (COMException ex) { throw new IOException("Die vorhandene Desktopverknüpfung ist ungültig und bleibt unverändert.", ex); }
        finally { if (shell is not null) Marshal.FinalReleaseComObject(shell); }
    }

    private static void WriteShortcut(string path, ShortcutDetails details)
    {
        object shell = new ShellLink();
        try
        {
            var link = (IShellLinkW)shell;
            link.SetPath(details.TargetPath);
            link.SetWorkingDirectory(details.WorkingDirectory);
            link.SetArguments(details.Arguments);
            link.SetIconLocation(details.IconPath, details.IconIndex);
            link.SetDescription(details.Description);
            link.SetShowCmd(details.ShowCommand);
            var store = (IPropertyStore)shell;
            var key = AppIdKey;
            var value = PropVariant.FromString(details.AppId);
            try { store.SetValue(ref key, ref value); store.Commit(); }
            finally { value.Clear(); }
            ((IPersistFile)shell).Save(path, true);
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    private static bool Matches(ShortcutDetails current, ShortcutDetails desired) =>
        current.TargetPath.Equals(desired.TargetPath, StringComparison.OrdinalIgnoreCase) &&
        current.WorkingDirectory.Equals(desired.WorkingDirectory, StringComparison.OrdinalIgnoreCase) &&
        current.IconPath.Equals(desired.IconPath, StringComparison.OrdinalIgnoreCase) &&
        current.Arguments == desired.Arguments && current.IconIndex == desired.IconIndex && current.ShowCommand == desired.ShowCommand &&
        current.Description == desired.Description && current.AppId == desired.AppId;

    private static void RequireOwned(ShortcutDetails details)
    {
        if (details.Description != ShortcutDescription || details.AppId != AppId)
            throw new IOException("Eine fremde ScreenDash.lnk ist bereits vorhanden und bleibt unverändert.");
    }

    private static void RequireRegularFile(string path)
    {
        RequireUnredirectedParents(path);
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) != 0 || IsUnsupportedReparsePoint(path, attributes))
            throw new IOException("ScreenDash benötigt eine normale Datei ohne Umleitung.");
    }

    private static SafeFileHandle OpenRegularFileGuard(string path)
    {
        RequireUnredirectedParents(path);
        // OPEN_REPARSE_POINT, read-only, sharing read only: a held source cannot be modified or deleted during native loading.
        var handle = CreateFile(path, 0x80000000, 1, 0, 3, 0x00200000, 0);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error(); handle.Dispose();
            throw new IOException("Die vorhandene Desktopverknüpfung kann nicht sicher gelesen werden.", new Win32Exception(error));
        }
        if (!GetFileInformationByHandleEx(handle, 9, out var info, (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
        {
            int error = Marshal.GetLastWin32Error(); handle.Dispose();
            throw new IOException("Die vorhandene Desktopverknüpfung kann nicht geprüft werden.", new Win32Exception(error));
        }
        if ((info.Attributes & FileAttributes.Directory) != 0 ||
            ((info.Attributes & FileAttributes.ReparsePoint) != 0 && !IsCloudTag(info.ReparseTag)))
        {
            handle.Dispose();
            throw new IOException("Eine umgeleitete Desktopverknüpfung bleibt unverändert.");
        }
        return handle;
    }

    private static void RequireUnredirectedParents(string path)
    {
        for (var parent = Directory.GetParent(path); parent is not null; parent = parent.Parent)
        {
            var attributes = File.GetAttributes(parent.FullName);
            if ((attributes & FileAttributes.Directory) == 0 || IsUnsupportedReparsePoint(parent.FullName, attributes))
                throw new IOException("Ein umgeleiteter Ordnerpfad bleibt unverändert.");
        }
    }

    private static bool IsUnsupportedReparsePoint(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) == 0) return false;
        // Query the object itself without following it. Cloud Files placeholders keep
        // their own path; junctions, symbolic links and unknown reparse tags are refused.
        using var handle = CreateFile(path, 0x80, 7, 0, 3, 0x02200000, 0);
        if (handle.IsInvalid || !GetFileInformationByHandleEx(handle, 9, out var info, (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
            throw new IOException("Der Ordner- oder Dateipfad kann nicht geprüft werden.", new Win32Exception(Marshal.GetLastWin32Error()));
        return !IsCloudTag(info.ReparseTag);
    }

    // MS-FSCC Cloud Files tags: CLOUD and CLOUD_1..CLOUD_F; legacy ONEDRIVE.
    // https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-fscc/c8e77b37-3909-4fe6-a4ea-2b9d423b1ee4
    private static bool IsCloudTag(uint tag) => (tag & 0xFFFF0FFF) == 0x9000001A || tag == 0x80000021;

    private static FileAttributes? AttributesOrMissing(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static T InSta<T>(Func<T> action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) return action();
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() => { try { result = action(); } catch (Exception ex) { failure = ex; } })
        { IsBackground = true, Name = "ScreenDash shell link" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
        public PropertyKey(Guid formatId, uint propertyId) { FormatId = formatId; PropertyId = propertyId; }
    }

    // The native union occupies two pointer widths: 16 bytes on x64, 8 bytes on x86.
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        private ushort _type, _reserved1, _reserved2, _reserved3;
        private nint _pointer, _unionRemainder;
        public static PropVariant FromString(string value) => new() { _type = 31, _pointer = Marshal.StringToCoTaskMemUni(value) };
        public readonly string? StringValue => _type switch { 31 => Marshal.PtrToStringUni(_pointer), 8 => Marshal.PtrToStringBSTR(_pointer), _ => null };
        public void Clear() { Marshal.ThrowExceptionForHR(PropVariantClear(ref this)); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo { public FileAttributes Attributes; public uint ReparseTag; }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int maximum, nint findData, uint flags);
        void GetIDList(out nint list);
        void SetIDList(nint list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int maximum);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int maximum);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string path);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maximum);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int maximum, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(nint window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    private delegate bool EnumWindowCallback(nint window, nint parameter);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int maximum);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass, out FileAttributeTagInfo information, uint size);
}
