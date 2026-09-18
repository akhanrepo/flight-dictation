using Flight.Core.Clipboard;
using Windows.ApplicationModel.DataTransfer;

namespace Flight.App.Clipboard;

public sealed class WindowsClipboardWriter : IClipboardWriter
{
    public void SetText(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        // Keep the text available after the app closes
        Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
    }
}
