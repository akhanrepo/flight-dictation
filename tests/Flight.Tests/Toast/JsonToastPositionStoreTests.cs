using Flight.Core;
using Flight.Core.Toast;
using Xunit;

namespace Flight.Tests.Toast;

/// <summary>Feature #7: the dragged position survives a restart.</summary>
public class JsonToastPositionStoreTests
{
    private readonly FlightOptions _options = new()
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "flight-tests-" + Guid.NewGuid().ToString("N")),
    };

    [Fact]
    public void PositionStore_HasNothingBeforeTheFirstDrag()
    {
        Assert.Null(new JsonToastPositionStore(_options).Load());
    }

    [Fact]
    public void PositionStore_RemembersAcrossRestarts()
    {
        new JsonToastPositionStore(_options).Save(new PixelPoint(1234, -56));

        // A new instance is what the next launch gets.
        Assert.Equal(new PixelPoint(1234, -56), new JsonToastPositionStore(_options).Load());
    }

    [Fact]
    public void PositionStore_TreatsACorruptFileAsNoPosition()
    {
        Directory.CreateDirectory(_options.DataDirectory);
        File.WriteAllText(Path.Combine(_options.DataDirectory, "toast-position.json"), "{not json");

        Assert.Null(new JsonToastPositionStore(_options).Load());
    }
}
