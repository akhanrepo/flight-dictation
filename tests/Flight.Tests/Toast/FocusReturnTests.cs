using Flight.Core.Toast;
using Flight.Tests.Fakes;
using Xunit;

namespace Flight.Tests.Toast;

/// <summary>Feature #7: after copy or dismiss, focus goes back to where the user was.</summary>
public class FocusReturnTests
{
    private const nint Outlook = 100;
    private const nint Toast = 200;
    private const nint Teams = 300;

    private readonly FakeForegroundWindow _foreground = new();

    private FocusReturn Create() => new(_foreground);

    [Fact]
    public void FocusReturn_GivesFocusBackAfterThePreviewCloses()
    {
        var focus = Create();
        _foreground.Current = Outlook;

        focus.Remember(Toast);
        _foreground.Current = Toast; // the preview activated
        var restored = focus.Restore(Toast);

        Assert.True(restored);
        Assert.Equal(Outlook, _foreground.Current);
    }

    // The user clicked into Teams before dismissing: don't yank them back
    [Fact]
    public void FocusReturn_LeavesTheUserWhereTheyMovedTo()
    {
        var focus = Create();
        _foreground.Current = Outlook;
        focus.Remember(Toast);

        _foreground.Current = Teams;

        Assert.False(focus.Restore(Toast));
        Assert.Equal(Teams, _foreground.Current);
    }

    // An error card replacing the preview mustn't overwrite the real origin
    [Fact]
    public void FocusReturn_IgnoresTheToastItselfAsTheOrigin()
    {
        var focus = Create();
        _foreground.Current = Outlook;
        focus.Remember(Toast);
        _foreground.Current = Toast;

        focus.Remember(Toast);
        focus.Restore(Toast);

        Assert.Equal(Outlook, _foreground.Current);
    }

    [Fact]
    public void FocusReturn_DoesNothingWithoutAnOrigin()
    {
        _foreground.Current = Toast;

        Assert.False(Create().Restore(Toast));
    }

    [Fact]
    public void FocusReturn_RestoresOnlyOnce()
    {
        var focus = Create();
        _foreground.Current = Outlook;
        focus.Remember(Toast);
        _foreground.Current = Toast;
        focus.Restore(Toast);

        _foreground.Current = Toast;

        Assert.False(focus.Restore(Toast));
    }
}
