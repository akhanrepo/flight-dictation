using Flight.Core.Input;
using Xunit;

namespace Flight.Tests.Input;

/// <summary>Feature #8: the hotkey's text form and which hotkeys are allowed.</summary>
public class HotkeyTests
{
    private static Hotkey Parse(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey), text);
        return hotkey;
    }

    // Criterion: existing installs keep Right Alt
    [Fact]
    public void Hotkey_DefaultIsRightAlt()
    {
        Assert.Equal("Right Alt", Hotkey.Default.ToString());
        Assert.Null(Hotkey.Default.Validate());
    }

    // Criterion: hotkeys round-trip through their text form
    [Theory]
    [InlineData("Right Alt")]
    [InlineData("Right Ctrl")]
    [InlineData("Ctrl+Win")]
    [InlineData("Ctrl+Shift+Space")]
    [InlineData("Ctrl+Alt+D")]
    [InlineData("F8")]
    [InlineData("Win+F13")]
    [InlineData("Ctrl+Num 5")]
    public void Hotkey_RoundTripsThroughText(string text)
    {
        Assert.Equal(text, Parse(text).ToString());
    }

    [Theory]
    [InlineData("shift+ctrl+space", "Ctrl+Shift+Space")] // canonical order, any case
    [InlineData(" Win + Ctrl ", "Ctrl+Win")]
    [InlineData("Right Alt+Space", "Alt+Space")]        // modifiers in combinations are side-neutral
    public void Hotkey_ParsingNormalizes(string text, string expected)
    {
        Assert.Equal(expected, Parse(text).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Banana")]
    [InlineData("+")]
    public void Hotkey_RejectsUnknownText(string text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
    }

    [Fact]
    public void Hotkey_EqualityIsByKeys()
    {
        Assert.Equal(Parse("Ctrl+Win"), Parse("Win+Ctrl"));
        Assert.NotEqual(Parse("Ctrl+Win"), Parse("Ctrl+Alt"));
    }

    // Criterion: allowed alone — F1–F24 and right-hand modifiers
    [Theory]
    [InlineData("Right Alt")]
    [InlineData("Right Ctrl")]
    [InlineData("Right Shift")]
    [InlineData("Right Win")]
    [InlineData("F1")]
    [InlineData("F24")]
    [InlineData("Caps Lock")]
    [InlineData("Scroll Lock")]
    [InlineData("Ctrl+Win")]
    [InlineData("Ctrl+Space")]
    [InlineData("Alt+Shift+K")]
    [InlineData("Shift+F9")]
    public void Hotkey_AcceptsUsableKeys(string text)
    {
        Assert.Null(Parse(text).Validate());
    }

    // Criterion: keys that would break normal typing are rejected with a reason
    [Theory]
    [InlineData("A")]
    [InlineData("7")]
    [InlineData("Space")]
    [InlineData("Enter")]
    [InlineData("Tab")]
    [InlineData("Backspace")]
    [InlineData("Esc")]
    [InlineData("Left Alt")]
    [InlineData("Left Ctrl")]
    [InlineData("Left Shift")]
    [InlineData("Win")]
    [InlineData("Shift+A")]          // just a capital A
    [InlineData("Ctrl+Esc")]         // Esc cancels recording
    [InlineData("Ctrl+Alt+Shift+Win+K")] // more than four keys
    public void Hotkey_RejectsKeysThatBreakTyping(string text)
    {
        var problem = Parse(text).Validate();

        Assert.False(string.IsNullOrWhiteSpace(problem), text);
    }

    [Fact]
    public void Hotkey_FromPressedKeys_KeepsTheSideOfALoneModifier()
    {
        Assert.Equal("Right Ctrl", Hotkey.FromPressedKeys([VirtualKeys.RightCtrl]).ToString());
        Assert.Equal("Ctrl+Win", Hotkey.FromPressedKeys([VirtualKeys.RightWin, VirtualKeys.LeftCtrl]).ToString());
    }

    [Theory]
    [InlineData("Right Alt", true)]
    [InlineData("Ctrl+Win", true)]
    [InlineData("Ctrl+Space", false)]
    [InlineData("F8", false)]
    public void Hotkey_KnowsWhenItNeedsMenuSuppression(string text, bool expected)
    {
        Assert.Equal(expected, Parse(text).InvolvesMenuKey);
    }
}
