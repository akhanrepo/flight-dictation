namespace Flight.Core.Startup;

/// <summary>
/// "Start with Windows" as a per-user startup entry. <see cref="IsEnabled"/>
/// reads the real entry every time, so the tray's check mark can't drift from
/// what Windows will actually do.
/// </summary>
public interface IStartupRegistration
{
    /// <summary>Only installed copies can register; a dev build in <c>bin/</c> can't.</summary>
    bool IsAvailable { get; }

    bool IsEnabled { get; }

    void Enable();

    void Disable();
}
