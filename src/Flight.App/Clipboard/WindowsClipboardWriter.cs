using Flight.Core.Clipboard;
using Windows.ApplicationModel.DataTransfer;

namespace Flight.App.Clipboard;

/// <summary>
/// Writes text through the WinRT clipboard. Other processes open the
/// clipboard briefly all the time (clipboard history, clipboard managers,
/// other dictation tools reading what they just pasted), and while they hold
/// it a write fails (CLIPBRD_E_CANT_OPEN, surfaced as a COMException or access denied) — so a failed write is retried
/// for a short while before it's reported.
/// </summary>
public sealed class WindowsClipboardWriter : IClipboardWriter
{
    private const int Attempts = 6;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(40);

    public void SetText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
                break;
            }
            catch (Exception) when (attempt < Attempts)
            {
                Thread.Sleep(RetryDelay);
            }
        }

        try
        {
            // Keeps the text available after Flight exits. The text is already
            // on the clipboard, so failing here isn't worth failing the copy.
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
        }
        catch (Exception)
        {
        }
    }
}
