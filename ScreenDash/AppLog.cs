using System.Text;

namespace ScreenDash;

public static class AppLog
{
    private const long MaxBytes = 1024 * 1024;
    private const int BackupCount = 3;
    private static readonly object Gate = new();
    public static string DirectoryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenDash", "logs");

    public static void Write(Exception exception) => Write(exception.ToString());

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var path = Path.Combine(DirectoryPath, "screendash.log");
                message ??= "";
                if (message.Length > 32768) message = message[..32768] + " [gekürzt]";
                var line = DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine;
                var data = Encoding.UTF8.GetBytes(line);
                if (File.Exists(path) && new FileInfo(path).Length + data.Length > MaxBytes)
                {
                    for (var i = BackupCount; i >= 1; i--)
                    {
                        var destination = path + "." + i;
                        var source = i == 1 ? path : path + "." + (i - 1);
                        if (File.Exists(destination)) File.Delete(destination);
                        if (File.Exists(source)) File.Move(source, destination);
                    }
                }
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                stream.Write(data);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Diagnostics must never stop or crash recording.
                System.Diagnostics.Debug.WriteLine("ScreenDash-Protokoll nicht verfügbar: " + ex.Message);
            }
        }
    }
}
