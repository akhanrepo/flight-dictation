using System.Text.Json;

namespace Flight.Core.Toast;

/// <summary>Where the user last dragged the toast, so it reopens there.</summary>
public interface IToastPositionStore
{
    /// <returns>The remembered top-left in physical pixels, or null when there isn't one.</returns>
    PixelPoint? Load();

    void Save(PixelPoint position);
}

/// <summary>
/// A tiny JSON file in the data directory. Unreadable or missing means "no
/// remembered position" — the toast falls back to its default spot rather
/// than failing.
/// </summary>
public sealed class JsonToastPositionStore : IToastPositionStore
{
    private readonly string _path;

    public JsonToastPositionStore(FlightOptions options) =>
        _path = Path.Combine(options.DataDirectory, "toast-position.json");

    public PixelPoint? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(_path));
            return saved is null ? null : new PixelPoint(saved.X, saved.Y);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(PixelPoint position)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!); // DataDirectory/<file>, never a root
        File.WriteAllText(_path, JsonSerializer.Serialize(new Saved { X = position.X, Y = position.Y }));
    }

    private sealed class Saved
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
}
