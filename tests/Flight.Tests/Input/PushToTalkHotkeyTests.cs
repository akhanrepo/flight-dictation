using Flight.Core.Input;
using Flight.Core.Settings;
using Flight.Tests.Fakes;
using Xunit;

namespace Flight.Tests.Input;

/// <summary>Feature #8: matching the configured hotkey against raw key events, and recording a new one.</summary>
public class PushToTalkHotkeyTests
{
    private const int Space = VirtualKeys.Space;
    private const int LCtrl = VirtualKeys.LeftCtrl;
    private const int RCtrl = VirtualKeys.RightCtrl;
    private const int LWin = VirtualKeys.LeftWin;
    private const int RAlt = VirtualKeys.RightAlt;
    private const int LShift = VirtualKeys.LeftShift;
    private const int K = 'K';

    private readonly FakeKeyboardHook _keys = new();
    private readonly FakeSettingsStore _settings = new();
    private readonly PushToTalkHotkey _hotkey;
    private readonly List<string> _events = [];

    public PushToTalkHotkeyTests()
    {
        _hotkey = new PushToTalkHotkey(_keys, _settings);
        _hotkey.Pressed += () => _events.Add("pressed");
        _hotkey.Released += () => _events.Add("released");
        _hotkey.Start();
    }

    private async Task UseAsync(string hotkey)
    {
        Assert.True(Hotkey.TryParse(hotkey, out var parsed));
        await _settings.UpdateAsync(_settings.Current with { Hotkey = parsed });
    }

    [Fact]
    public void Default_RightAltPressesAndReleases()
    {
        _keys.Down(RAlt);
        _keys.Up(RAlt);

        Assert.Equal(["pressed", "released"], _events);
    }

    // Criterion: auto-repeat doesn't fire it again
    [Fact]
    public void AutoRepeat_PressesOnce()
    {
        _keys.Down(RAlt);
        _keys.Down(RAlt);
        _keys.Down(RAlt);
        _keys.Up(RAlt);

        Assert.Equal(["pressed", "released"], _events);
    }

    [Fact]
    public void OtherKeys_DoNothing()
    {
        _keys.Down(VirtualKeys.LeftAlt);
        _keys.Up(VirtualKeys.LeftAlt);
        _keys.Down(K);
        _keys.Up(K);

        Assert.Empty(_events);
    }

    // Criterion: pressed when every key is held, in any order
    [Theory]
    [InlineData(LCtrl, LWin)]
    [InlineData(LWin, LCtrl)]
    [InlineData(RCtrl, LWin)]
    public async Task Combination_PressesInAnyOrder(int first, int second)
    {
        await UseAsync("Ctrl+Win");

        _keys.Down(first);
        Assert.Empty(_events);
        _keys.Down(second);

        Assert.Equal(["pressed"], _events);
    }

    // Criterion: released when any of its keys is released
    [Theory]
    [InlineData(LCtrl)]
    [InlineData(LWin)]
    public async Task Combination_ReleasesWhenEitherKeyGoesUp(int releasedFirst)
    {
        await UseAsync("Ctrl+Win");
        _keys.Down(LCtrl);
        _keys.Down(LWin);

        _keys.Up(releasedFirst);

        Assert.Equal(["pressed", "released"], _events);
    }

    // Criterion: an extra non-modifier key held at the same time prevents the match
    [Fact]
    public async Task ExtraNonModifierKey_PreventsTheMatch()
    {
        await UseAsync("Ctrl+Win");
        _keys.Down(K);
        _keys.Down(LCtrl);
        _keys.Down(LWin);

        Assert.Empty(_events);
    }

    // AltGr layouts report Right Alt with a synthetic Left Ctrl: still Right Alt
    [Fact]
    public void ExtraModifier_StillMatches()
    {
        _keys.Down(LCtrl);
        _keys.Down(RAlt);

        Assert.Equal(["pressed"], _events);
    }

    // Criterion: the non-modifier key is swallowed so Ctrl+Space never types a space
    [Fact]
    public async Task NonModifierKeyOfTheHotkey_IsSwallowed_ModifiersPassThrough()
    {
        await UseAsync("Ctrl+Space");

        Assert.False(_keys.Down(LCtrl));
        Assert.True(_keys.Down(Space));
        Assert.True(_keys.Down(Space)); // auto-repeat stays swallowed
        Assert.True(_keys.Up(Space));
        Assert.False(_keys.Up(LCtrl));
        Assert.Equal(["pressed", "released"], _events);
    }

    [Fact]
    public void ModifierOnlyHotkey_IsNeverSwallowed()
    {
        Assert.False(_keys.Down(RAlt));
        Assert.False(_keys.Up(RAlt));
    }

    // A key whose down reached other apps must have its up reach them too
    [Fact]
    public async Task KeyUp_IsSwallowedOnlyIfItsKeyDownWas()
    {
        await UseAsync("Ctrl+Space");

        Assert.False(_keys.Down(Space)); // no Ctrl yet: typed normally
        _keys.Down(LCtrl);               // completes the match now
        Assert.False(_keys.Up(Space));   // its down went through, so must its up

        Assert.Equal(["pressed", "released"], _events);
    }

    [Fact]
    public void Space_IsNotSwallowedWhenItIsNotPartOfAMatch()
    {
        Assert.False(_keys.Down(Space));
        Assert.False(_keys.Up(Space));
    }

    [Theory]
    [InlineData("Right Alt", 1)]
    [InlineData("Ctrl+Space", 0)]
    public async Task MenuActivation_IsSuppressedForAltAndWinHotkeys(string hotkey, int expected)
    {
        await UseAsync(hotkey);
        foreach (var key in Hotkey.TryParse(hotkey, out var parsed) ? parsed.Keys : [])
        {
            _keys.Down(key == VirtualKeys.Ctrl ? LCtrl : key);
        }

        Assert.Equal(expected, _keys.MenuSuppressions);
    }

    [Fact]
    public void InjectedKeys_AreIgnored()
    {
        Assert.False(_keys.Down(RAlt, injected: true));
        Assert.Empty(_events);
    }

    // A key whose up was lost (Win+L locked the screen) doesn't block later matches
    [Fact]
    public async Task StaleKeys_AreDropped()
    {
        await UseAsync("Ctrl+Win");
        _keys.Down(K);
        _keys.ForgottenKeys.Add(K); // the system says K is no longer down

        _keys.Down(LCtrl);
        _keys.Down(LWin);

        Assert.Equal(["pressed"], _events);
    }

    // Criterion: a change applies to the next press without a restart
    [Fact]
    public async Task SettingsChange_AppliesToTheNextPress()
    {
        await UseAsync("F8");

        _keys.Down(RAlt);
        _keys.Up(RAlt);
        Assert.Empty(_events);

        _keys.Down(VirtualKeys.F1 + 7);
        _keys.Up(VirtualKeys.F1 + 7);
        Assert.Equal(["pressed", "released"], _events);
    }

    // Criterion: a dictation in progress still ends on its original keys
    [Fact]
    public async Task SettingsChange_MidPress_StillReleasesOnTheOriginalKey()
    {
        _keys.Down(RAlt);
        await UseAsync("F8");

        _keys.Up(RAlt);

        Assert.Equal(["pressed", "released"], _events);
    }

    // Criterion: recording captures the peak combination and completes on the first key-up
    [Fact]
    public void Recording_CapturesThePeakCombination()
    {
        HotkeyRecording? result = null;
        _hotkey.BeginRecording(r => result = r);

        _keys.Down(LCtrl);
        _keys.Down(LShift);
        _keys.Down(Space);
        Assert.Null(result);
        _keys.Up(Space);

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Equal("Ctrl+Shift+Space", result.Hotkey!.ToString());
        Assert.False(_hotkey.IsRecording);
    }

    // Criterion: while recording, key events don't start a dictation
    [Fact]
    public void Recording_SwallowsKeys_AndNeverStartsADictation()
    {
        _hotkey.BeginRecording(_ => { });

        Assert.True(_keys.Down(RAlt));
        Assert.True(_keys.Up(RAlt));

        Assert.Empty(_events);
    }

    [Fact]
    public void Recording_EscCancels()
    {
        HotkeyRecording? result = null;
        _hotkey.BeginRecording(r => result = r);

        _keys.Down(VirtualKeys.Escape);

        Assert.True(result!.Cancelled);
        Assert.False(_hotkey.IsRecording);
    }

    // Criterion: invalid combinations are reported without being saved
    [Fact]
    public void Recording_ReportsAnInvalidKeyWithoutSavingIt()
    {
        HotkeyRecording? result = null;
        _hotkey.BeginRecording(r => result = r);

        _keys.Down(K);
        _keys.Up(K);

        Assert.False(result!.IsValid);
        Assert.Contains("typing", result.Error);
        Assert.Equal(Hotkey.Default, _settings.Current.Hotkey);
    }

    // A key already down when recording started (Enter on the button) isn't recorded
    [Fact]
    public void Recording_IgnoresTheKeyUpOfAKeyPressedBeforeIt()
    {
        HotkeyRecording? result = null;
        Assert.False(_keys.Down(VirtualKeys.Enter));
        _hotkey.BeginRecording(r => result = r);

        Assert.False(_keys.Up(VirtualKeys.Enter)); // its down wasn't swallowed, so neither is its up
        Assert.Null(result);

        _keys.Down(VirtualKeys.F1 + 8);
        _keys.Up(VirtualKeys.F1 + 8);
        Assert.Equal("F9", result!.Hotkey!.ToString());
    }

    [Fact]
    public void CancelRecording_ReportsCancelled()
    {
        HotkeyRecording? result = null;
        _hotkey.BeginRecording(r => result = r);

        _hotkey.CancelRecording();

        Assert.True(result!.Cancelled);
    }
}
