namespace Flight.Core.Accounts;

/// <summary>
/// This install's <see cref="Device"/> row, including the cached entitlement
/// that keeps cloud gating correct while offline.
/// </summary>
public interface IDeviceStore
{
    Device Current { get; }

    Task<Device> UpdateAsync(Device device, CancellationToken cancellationToken = default);
}
