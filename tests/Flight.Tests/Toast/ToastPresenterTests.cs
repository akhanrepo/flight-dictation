using Flight.Core.Models;
using Flight.Core.Toast;
using Xunit;

namespace Flight.Tests.Toast;

/// <summary>Feature #7: what the toast shows in each state.</summary>
public class ToastPresenterTests
{
    private readonly ToastPresenter _toast = new();

    [Fact]
    public void Toast_ListeningIsAPillThatKeepsFocusWhereItIs()
    {
        _toast.ShowCapturing();

        Assert.Equal(ToastKind.Pill, _toast.View.Kind);
        Assert.Equal(ToastTone.Listening, _toast.View.Tone);
        Assert.Equal("Listening", _toast.View.Title);
        Assert.Equal("Release Right Alt to finish", _toast.View.Hint);
        Assert.False(_toast.View.TakesFocus);
    }

    [Fact]
    public void Toast_ProcessingIsAPillWithASpinner()
    {
        _toast.ShowProcessing();

        Assert.Equal(ToastKind.Pill, _toast.View.Kind);
        Assert.True(_toast.View.ShowSpinner);
        Assert.False(_toast.View.TakesFocus);
    }

    [Fact]
    public void Toast_PreviewIsACardWithTheTextAndCopyDismiss()
    {
        _toast.ShowPreview("Send the SOW to Priya by Friday.");

        var view = _toast.View;
        Assert.Equal(ToastKind.Card, view.Kind);
        Assert.Equal("Preview", view.Title);
        Assert.Equal("Send the SOW to Priya by Friday.", view.Body);
        Assert.Equal("Enter to copy · Esc to dismiss", view.Hint);
        Assert.Equal(ToastAction.Copy, view.Primary);
        Assert.Equal(ToastAction.Dismiss, view.Secondary);
        Assert.True(view.TakesFocus); // so Enter and Esc work
    }

    [Fact]
    public void Toast_ErrorIsACardWithTheMessageAndDismiss()
    {
        _toast.ShowError("The cloud model didn't respond.");

        Assert.Equal(ToastTone.Error, _toast.View.Tone);
        Assert.Equal("The cloud model didn't respond.", _toast.View.Body);
        Assert.Equal(ToastAction.Dismiss, _toast.View.Primary);
        Assert.Null(_toast.View.Secondary);
    }

    [Fact]
    public void Toast_DownloadIsAPillWithProgress()
    {
        _toast.ShowModelDownloadProgress(new ModelDownloadProgress("model.gguf", 42, 100));

        Assert.Equal(ToastKind.Pill, _toast.View.Kind);
        Assert.Equal("Setting up · downloading model 42%", _toast.View.Title);
        Assert.Equal(42, _toast.View.Progress);
        Assert.False(_toast.View.TakesFocus);
    }

    [Fact]
    public void Toast_DownloadWithUnknownSizeIsIndeterminate()
    {
        _toast.ShowModelDownloadProgress(new ModelDownloadProgress("model.gguf", 42, null));

        Assert.True(double.IsNaN(_toast.View.Progress!.Value));
    }

    [Fact]
    public void Toast_CopyThenHideShowsABriefConfirmation()
    {
        _toast.ShowPreview("text");

        _toast.NotifyCopyRequested();
        _toast.Hide();

        Assert.Equal(ToastKind.Card, _toast.View.Kind);
        Assert.Equal(ToastTone.Success, _toast.View.Tone);
        Assert.Equal("Copied to clipboard", _toast.View.Title);
        Assert.Equal(TimeSpan.FromSeconds(1), _toast.View.AutoHideAfter);
        Assert.False(_toast.View.TakesFocus);
    }

    [Fact]
    public void Toast_DismissHidesWithoutAConfirmation()
    {
        _toast.ShowPreview("text");

        _toast.Hide();

        Assert.Equal(ToastKind.Hidden, _toast.View.Kind);
    }

    // A failed copy (error) must not leave a stale "copied" for the next hide
    [Fact]
    public void Toast_CopyRequestDoesNotOutliveAnError()
    {
        _toast.ShowPreview("text");
        _toast.NotifyCopyRequested();
        _toast.ShowError("Clipboard is busy.");

        _toast.Hide();

        Assert.Equal(ToastKind.Hidden, _toast.View.Kind);
    }

    // No overlap: every title fits on one line
    [Fact]
    public void Toast_EveryTitleFitsOnOneLine()
    {
        var states = new Action[]
        {
            _toast.ShowCapturing,
            _toast.ShowProcessing,
            () => _toast.ShowPreview(new string('x', 2000)),
            () => _toast.ShowError(new string('x', 2000)),
            () => _toast.ShowModelDownloadProgress(new ModelDownloadProgress("f", 1, 3)),
        };

        foreach (var show in states)
        {
            show();
            Assert.True(_toast.View.Title.Length <= ToastPresenter.MaxTitleLength, _toast.View.Title);
        }
    }

    [Fact]
    public void Toast_RaisesChangedForEachState()
    {
        var seen = new List<ToastKind>();
        _toast.Changed += v => seen.Add(v.Kind);

        _toast.ShowCapturing();
        _toast.ShowProcessing();
        _toast.ShowPreview("text");
        _toast.Hide();

        Assert.Equal([ToastKind.Pill, ToastKind.Pill, ToastKind.Card, ToastKind.Hidden], seen);
    }
}
