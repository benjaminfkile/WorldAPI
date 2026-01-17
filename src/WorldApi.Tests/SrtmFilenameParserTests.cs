using WorldApi.World;

namespace WorldApi.Tests;

public class SrtmFilenameParserTests
{
    private const double Tolerance = 0.000001;

    [Fact]
    public void Parse_UppercaseFilename_ParsesCorrectly()
    {
        // Arrange
        string filename = "N46W113.hgt";

        // Act
        var tile = SrtmFilenameParser.Parse(filename);

        // Assert
        Assert.Equal(46.0, tile.MinLatitude, Tolerance);
        Assert.Equal(47.0, tile.MaxLatitude, Tolerance);
        Assert.Equal(-113.0, tile.MinLongitude, Tolerance);
        Assert.Equal(-112.0, tile.MaxLongitude, Tolerance);
        Assert.Equal(filename, tile.S3Key);
    }

    [Fact]
    public void Parse_LowercaseFilename_ParsesCorrectly()
    {
        // Arrange
        string filename = "n46w113.hgt";

        // Act
        var tile = SrtmFilenameParser.Parse(filename);

        // Assert
        Assert.Equal(46.0, tile.MinLatitude, Tolerance);
        Assert.Equal(47.0, tile.MaxLatitude, Tolerance);
        Assert.Equal(-113.0, tile.MinLongitude, Tolerance);
        Assert.Equal(-112.0, tile.MaxLongitude, Tolerance);
        Assert.Equal(filename, tile.S3Key);
    }

    [Fact]
    public void Parse_MixedCaseFilename_ParsesCorrectly()
    {
        // Arrange
        string filename = "N46w113.HGT";

        // Act
        var tile = SrtmFilenameParser.Parse(filename);

        // Assert
        Assert.Equal(46.0, tile.MinLatitude, Tolerance);
        Assert.Equal(47.0, tile.MaxLatitude, Tolerance);
        Assert.Equal(-113.0, tile.MinLongitude, Tolerance);
        Assert.Equal(-112.0, tile.MaxLongitude, Tolerance);
        Assert.Equal(filename, tile.S3Key);
    }

    [Fact]
    public void Parse_SouthernHemisphere_ParsesCorrectly()
    {
        // Arrange
        string filename = "S46E010.hgt";

        // Act
        var tile = SrtmFilenameParser.Parse(filename);

        // Assert
        Assert.Equal(-46.0, tile.MinLatitude, Tolerance);
        Assert.Equal(-45.0, tile.MaxLatitude, Tolerance);
        Assert.Equal(10.0, tile.MinLongitude, Tolerance);
        Assert.Equal(11.0, tile.MaxLongitude, Tolerance);
        Assert.Equal(filename, tile.S3Key);
    }

    [Fact]
    public void Parse_SouthernHemisphereLowercase_ParsesCorrectly()
    {
        // Arrange
        string filename = "s46e010.hgt";

        // Act
        var tile = SrtmFilenameParser.Parse(filename);

        // Assert
        Assert.Equal(-46.0, tile.MinLatitude, Tolerance);
        Assert.Equal(-45.0, tile.MaxLatitude, Tolerance);
        Assert.Equal(10.0, tile.MinLongitude, Tolerance);
        Assert.Equal(11.0, tile.MaxLongitude, Tolerance);
        Assert.Equal(filename, tile.S3Key);
    }

    [Fact]
    public void Parse_EasternHemisphere_ParsesCorrectly()
    {
        // Arrange
        string filename = "N46E113.hgt";

        // Act
        var tile = SrtmFilenameParser.Parse(filename);

        // Assert
        Assert.Equal(46.0, tile.MinLatitude, Tolerance);
        Assert.Equal(47.0, tile.MaxLatitude, Tolerance);
        Assert.Equal(113.0, tile.MinLongitude, Tolerance);
        Assert.Equal(114.0, tile.MaxLongitude, Tolerance);
        Assert.Equal(filename, tile.S3Key);
    }

    [Fact]
    public void Parse_WithoutExtension_ParsesCorrectly()
    {
        // Arrange
        string filename = "N46W113";

        // Act
        var tile = SrtmFilenameParser.Parse(filename);

        // Assert
        Assert.Equal(46.0, tile.MinLatitude, Tolerance);
        Assert.Equal(47.0, tile.MaxLatitude, Tolerance);
        Assert.Equal(-113.0, tile.MinLongitude, Tolerance);
        Assert.Equal(-112.0, tile.MaxLongitude, Tolerance);
        Assert.Equal(filename, tile.S3Key);
    }

    [Fact]
    public void Parse_EquatorPrimeMeridian_ParsesCorrectly()
    {
        // Arrange
        string filename = "N00E000.hgt";

        // Act
        var tile = SrtmFilenameParser.Parse(filename);

        // Assert
        Assert.Equal(0.0, tile.MinLatitude, Tolerance);
        Assert.Equal(1.0, tile.MaxLatitude, Tolerance);
        Assert.Equal(0.0, tile.MinLongitude, Tolerance);
        Assert.Equal(1.0, tile.MaxLongitude, Tolerance);
        Assert.Equal(filename, tile.S3Key);
    }

    [Fact]
    public void Parse_ThreeDigitLongitude_ParsesCorrectly()
    {
        // Arrange
        string filename = "N46W179.hgt";

        // Act
        var tile = SrtmFilenameParser.Parse(filename);

        // Assert
        Assert.Equal(46.0, tile.MinLatitude, Tolerance);
        Assert.Equal(47.0, tile.MaxLatitude, Tolerance);
        Assert.Equal(-179.0, tile.MinLongitude, Tolerance);
        Assert.Equal(-178.0, tile.MaxLongitude, Tolerance);
        Assert.Equal(filename, tile.S3Key);
    }

    [Fact]
    public void Parse_TwoDigitLatitude_ParsesCorrectly()
    {
        // Arrange
        string filename = "N89W113.hgt";

        // Act
        var tile = SrtmFilenameParser.Parse(filename);

        // Assert
        Assert.Equal(89.0, tile.MinLatitude, Tolerance);
        Assert.Equal(90.0, tile.MaxLatitude, Tolerance);
        Assert.Equal(-113.0, tile.MinLongitude, Tolerance);
        Assert.Equal(-112.0, tile.MaxLongitude, Tolerance);
        Assert.Equal(filename, tile.S3Key);
    }

    [Fact]
    public void Parse_IsDeterministic()
    {
        // Arrange
        string filename = "N46W113.hgt";

        // Act
        var tile1 = SrtmFilenameParser.Parse(filename);
        var tile2 = SrtmFilenameParser.Parse(filename);

        // Assert
        Assert.Equal(tile1.MinLatitude, tile2.MinLatitude, Tolerance);
        Assert.Equal(tile1.MaxLatitude, tile2.MaxLatitude, Tolerance);
        Assert.Equal(tile1.MinLongitude, tile2.MinLongitude, Tolerance);
        Assert.Equal(tile1.MaxLongitude, tile2.MaxLongitude, Tolerance);
        Assert.Equal(tile1.S3Key, tile2.S3Key);
    }

    [Fact]
    public void Parse_CaseVariations_ProduceSameCoordinates()
    {
        // Arrange
        string upper = "N46W113.hgt";
        string lower = "n46w113.hgt";
        string mixed = "N46w113.HGT";

        // Act
        var tileUpper = SrtmFilenameParser.Parse(upper);
        var tileLower = SrtmFilenameParser.Parse(lower);
        var tileMixed = SrtmFilenameParser.Parse(mixed);

        // Assert - all should have same coordinates
        Assert.Equal(tileUpper.MinLatitude, tileLower.MinLatitude, Tolerance);
        Assert.Equal(tileUpper.MinLatitude, tileMixed.MinLatitude, Tolerance);
        Assert.Equal(tileUpper.MaxLatitude, tileLower.MaxLatitude, Tolerance);
        Assert.Equal(tileUpper.MaxLatitude, tileMixed.MaxLatitude, Tolerance);
        Assert.Equal(tileUpper.MinLongitude, tileLower.MinLongitude, Tolerance);
        Assert.Equal(tileUpper.MinLongitude, tileMixed.MinLongitude, Tolerance);
        Assert.Equal(tileUpper.MaxLongitude, tileLower.MaxLongitude, Tolerance);
        Assert.Equal(tileUpper.MaxLongitude, tileMixed.MaxLongitude, Tolerance);

        // But S3Keys should preserve original case
        Assert.Equal(upper, tileUpper.S3Key);
        Assert.Equal(lower, tileLower.S3Key);
        Assert.Equal(mixed, tileMixed.S3Key);
    }

    [Fact]
    public void Parse_FilenameOnly_PreservesOriginalCase()
    {
        // Arrange
        string filename = "N46W113.hgt";

        // Act
        var tile = SrtmFilenameParser.Parse(filename);

        // Assert - S3Key should preserve the original case
        Assert.Equal(filename, tile.S3Key);
    }
}
