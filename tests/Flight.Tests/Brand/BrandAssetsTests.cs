using System.Text.Json;
using Xunit;

namespace Flight.Tests.Brand;

/// <summary>
/// Feature #6: the logo assets that ship. Legibility at 16 px is a manual
/// check; what's verifiable without rendering is checked here.
/// </summary>
public class BrandAssetsTests
{
    private static readonly int[] RequiredFrames = [16, 24, 32, 48, 64, 256];

    private static string Brand(params string[] parts) => RepoPaths.Combine(["assets", "brand", .. parts]);

    [Fact]
    public void Brand_HasSvgMasterAndPngExports()
    {
        Assert.Contains("<svg", File.ReadAllText(Brand("flight-logo.svg")));
        foreach (var size in new[] { 16, 32, 48, 64, 128, 256, 512, 1024 })
        {
            Assert.True(File.Exists(Brand("png", $"flight-{size}.png")), $"missing flight-{size}.png");
        }
    }

    [Theory]
    [InlineData("flight.ico")]
    [InlineData("flight-listening.ico")]
    [InlineData("flight-paused.ico")]
    public void Brand_IcoContainsRequiredFrameSizes(string icon)
    {
        var sizes = ReadIcoFrameSizes(Brand(icon));

        foreach (var size in RequiredFrames)
        {
            Assert.Contains(size, sizes);
        }
    }

    // Small frames use the simplified mark (bigger plane, no bars) rather
    // than the full mark shrunk into a blur.
    [Fact]
    public void Brand_SmallFramesUseTheSimplifiedMark()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Brand("icon-manifest.json")));
        var frames = manifest.RootElement.GetProperty("frames");

        foreach (var frame in frames.EnumerateObject())
        {
            var expected = int.Parse(frame.Name) <= 32 ? "small" : "full";
            Assert.Equal(expected, frame.Value.GetString());
        }

        // The manifest describes the files that actually ship.
        var declared = frames.EnumerateObject().Select(f => int.Parse(f.Name)).Order().ToArray();
        Assert.Equal(declared, ReadIcoFrameSizes(Brand("flight.ico")).Order().ToArray());
    }

    /// <summary>Reads frame sizes from an ICO directory (ICONDIR + ICONDIRENTRY).</summary>
    internal static int[] ReadIcoFrameSizes(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        Assert.Equal(0, reader.ReadUInt16()); // reserved
        Assert.Equal(1, reader.ReadUInt16()); // type 1 = icon
        var count = reader.ReadUInt16();

        var sizes = new int[count];
        for (var i = 0; i < count; i++)
        {
            var width = reader.ReadByte();
            sizes[i] = width == 0 ? 256 : width; // 0 means 256
            reader.ReadBytes(15);
        }

        return sizes;
    }
}
