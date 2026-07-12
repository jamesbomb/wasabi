using System.Globalization;

namespace Wasabi.Core.Diagnostics;

public static class WasabiLog
{
    private static readonly object Sync = new();
    private static readonly string DirectoryPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WASABI", "Logs");

    public static string FilePath => Path.Combine(DirectoryPath, "wasabi.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(
                    FilePath,
                    $"{DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never interrupt audio routing.
        }
    }
}
