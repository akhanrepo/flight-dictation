using Flight.Core;

namespace Flight.App.Platform;

/// <summary>
/// Last-resort error log at <c>%LOCALAPPDATA%\Flight\logs\errors.log</c>. A
/// windowless tray app that fails at startup otherwise just sits there with
/// no window and no explanation.
/// </summary>
public static class ErrorLog
{
    private static readonly object Gate = new();

    public static string FilePath { get; } =
        Path.Combine(FlightOptions.DefaultDataDirectory, "logs", "errors.log");

    public static void Write(string context, Exception exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); // fixed path under %LOCALAPPDATA%, never a root
                File.AppendAllText(FilePath, $"[{DateTimeOffset.Now:O}] {context}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // Logging must never take the app down with it.
        }
    }
}
