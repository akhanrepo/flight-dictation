using System.Globalization;
using Flight.Core.Storage;

namespace Flight.Core.Accounts;

/// <summary>
/// Single-row device table, cached in memory. The device id is generated once
/// on first run and never changes, so the entitlement cache survives restarts.
/// </summary>
public class SqliteDeviceStore : IDeviceStore
{
    private readonly FlightDatabase _database;
    private readonly object _writeLock = new();
    private Device _current;

    public SqliteDeviceStore(FlightDatabase database)
    {
        _database = database;
        _current = LoadOrCreate();
    }

    public Device Current => Volatile.Read(ref _current);

    public Task<Device> UpdateAsync(Device device, CancellationToken cancellationToken = default)
    {
        lock (_writeLock)
        {
            Save(device);
            Volatile.Write(ref _current, device);
        }

        return Task.FromResult(device);
    }

    private Device LoadOrCreate()
    {
        using var connection = _database.OpenConnection();
        using (var select = connection.CreateCommand())
        {
            select.CommandText = """
                SELECT id, account_id, cached_entitlement, last_synced_at
                FROM devices
                WHERE row_id = 1;
                """;

            using var reader = select.ExecuteReader();
            if (reader.Read())
            {
                return new Device
                {
                    Id = reader.GetString(0),
                    AccountId = reader.IsDBNull(1) ? null : reader.GetString(1),
                    CachedEntitlement = reader.IsDBNull(2) ? null : reader.GetString(2),
                    LastSyncedAt = reader.IsDBNull(3)
                        ? null
                        : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                };
            }
        }

        var device = new Device { Id = Guid.NewGuid().ToString("N") };
        Save(device);
        return device;
    }

    private void Save(Device device)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO devices (row_id, id, account_id, cached_entitlement, last_synced_at)
            VALUES (1, $id, $accountId, $entitlement, $lastSyncedAt)
            ON CONFLICT (row_id) DO UPDATE SET
                id = excluded.id,
                account_id = excluded.account_id,
                cached_entitlement = excluded.cached_entitlement,
                last_synced_at = excluded.last_synced_at;
            """;
        command.Parameters.AddWithValue("$id", device.Id);
        command.Parameters.AddWithValue("$accountId", (object?)device.AccountId ?? DBNull.Value);
        command.Parameters.AddWithValue("$entitlement", (object?)device.CachedEntitlement ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastSyncedAt",
            device.LastSyncedAt is { } synced
                ? synced.ToString("O", CultureInfo.InvariantCulture)
                : DBNull.Value);
        command.ExecuteNonQuery();
    }
}
