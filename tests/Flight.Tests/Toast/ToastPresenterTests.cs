using Flight.Core.Models;
using Flight.Core.Toast;
using Xunit;

namespace Flight.Tests.Toast;

/// <summary>Feature #9: what the indicator pill shows in each state.</summary>
public class ToastPresenterTests
{
    private readonly ToastPresenter _toast = new();

    [Fact]
    public void Toast_ListeningIsIconOnly()
    {
        _toast.ShowCapturing();

        Assert.False(_toast.View.IsHidden);
        Assert.Equal(ToastTone.Listening, _toast.View.Tone);
        Assert.Equal("Listening", _toast.View.Title); // the accessible name
        Assert.False(_toast.View.ShowsText);
        Assert.Null(_toast.View.AutoHideAfter);
    }

    [Fact]
    public void Toast_ProcessingIsASpinner()
    {
        _toast.ShowProcessing();

        Assert.Equal(ToastTone.Processing, _toast.View.Tone);
        Assert.False(_toast.View.ShowsText);
    }

    // Criterion: copied, nothing-heard and error states hide themselves
    [Fact]
    public void Toast_CopiedIsABriefCheckThatHidesItself()
    {
        _toast.ShowCopied();

        Assert.Equal(ToastTone.Success, _toast.View.Tone);
        Assert.Equal(ToastPresenter.CopiedDuration, _toast.View.AutoHideAfter);
        Assert.True(_toast.View.AutoHideAfter < TimeSpan.FromSeconds(1.5));
    }

    [Fact]
    public void Toast_NothingHeardSaysSoAndHidesItself()
    {
        _toast.ShowNothingHeard();

        Assert.Equal("Didn't catch that", _toast.View.Title);
        Assert.True(_toast.View.ShowsText);
        Assert.Equal(ToastPresenter.NothingHeardDuration, _toast.View.AutoHideAfter);
    }

    [Fact]
    public void Toast_ErrorShowsTheFirstLineAndKeepsTheRestForTheTooltip()
    {
        _toast.ShowError("The cloud model didn't respond.\nStatus 503.");

        Assert.Equal(ToastTone.Error, _toast.View.Tone);
        Assert.Equal("The cloud model didn't respond.", _toast.View.Title);
        Assert.Equal("The cloud model didn't respond.\nStatus 503.", _toast.View.Detail);
        Assert.Equal(ToastPresenter.ErrorDuration, _toast.View.AutoHideAfter);
    }

    [Fact]
    public void Toast_DownloadShowsProgress()
    {
        _toast.ShowModelDownloadProgress(new ModelDownloadProgress("model.gguf", 42, 100));

        Assert.Equal("Downloading model 42%", _toast.View.Title);
        Assert.Equal(42, _toast.View.Progress);
        Assert.True(_toast.View.ShowsText);
    }

    [Fact]
    public void Toast_UnknownDownloadSizeIsIndeterminate()
    {
        _toast.ShowModelDownloadProgress(new ModelDownloadProgress("model.gguf", 42, null));

        Assert.True(double.IsNaN(_toast.View.Progress!.Value));
    }

    // Criterion: the level reaches the window while listening only
    [Fact]
    public void Toast_LevelIsRaisedWhileListeningOnly()
    {
        var levels = new List<float>();
        _toast.LevelChanged += levels.Add;

        _toast.ShowLevel(0.5f); // hidden: ignored
        _toast.ShowCapturing();
        _toast.ShowLevel(0.3f);
        _toast.ShowLevel(3f); // clamped
        _toast.ShowProcessing();
        _toast.ShowLevel(0.6f); // processing: ignored

        Assert.Equal([0.3f, 1f], levels);
    }

    // No overlap: every title fits on one line
    [Fact]
    public void Toast_EveryTitleFitsOnOneLine()
    {
        var states = new Action[]
        {
            _toast.ShowCapturing,
            _toast.ShowProcessing,
            _toast.ShowCopied,
            _toast.ShowNothingHeard,
            () => _toast.ShowError(new string('x', 2000)),
            () => _toast.ShowModelDownloadProgress(new ModelDownloadProgress("f", 1, 3)),
        };

        foreach (var show in states)
        {
            show();
            Assert.True(_toast.View.Title.Length <= ToastPresenter.MaxTitleLength, _toast.View.Title);
            Assert.DoesNotContain('\n', _toast.View.Title);
        }
    }

    [Fact]
    public void Toast_RaisesChangedForEachState()
    {
        var seen = new List<ToastView>();
        _toast.Changed += seen.Add;

        _toast.ShowCapturing();
        _toast.ShowProcessing();
        _toast.ShowCopied();
        _toast.Hide();

        Assert.Equal(4, seen.Count);
        Assert.True(seen[^1].IsHidden);
    }
}
