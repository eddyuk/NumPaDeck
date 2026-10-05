namespace NumPaDeck.App;

/// <summary>
/// Minimal file-backed error log at %APPDATA%\NumPaDeck\errors.log.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();

    private static string LogPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NumPaDeck", "errors.log");

    public static void Error(string message) => Write("ERROR  " + message);

    public static void Info(string message) => Write("INFO   " + message);

    private static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // never throw from logging
        }
    }
}
