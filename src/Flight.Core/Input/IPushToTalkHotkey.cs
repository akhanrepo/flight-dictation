namespace Flight.Core.Input;

/// <summary>Global push-to-talk key. Pressed fires on key-down, Released on key-up.</summary>
public interface IPushToTalkHotkey : IDisposable
{
    event Action? Pressed;
    event Action? Released;

    void Start();
}
