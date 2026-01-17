using WorldApi.World;

namespace WorldApi.Tests;

public class SrtmDecoderTests
{
    private const int SrtmSize = 1201;
    private const int ExpectedByteCount = SrtmSize * SrtmSize * 2;
    private const short MissingDataValue = -32768;

    [Fact]
    public void Decode_ValidData_ReturnsCorrectArraySize()
    {
        // Arrange
        byte[] data = new byte[ExpectedByteCount];

        // Act
        short[] result = SrtmDecoder.Decode(data);

        // Assert
        Assert.Equal(SrtmSize * SrtmSize, result.Length);
    }

    [Fact]
    public void Decode_BigEndianData_ParsesCorrectly()
    {
        // Arrange
        byte[] data = new byte[ExpectedByteCount];
        
        // Set first elevation to 1000 meters (0x03E8 in hex)
        // Big-endian: high byte first, then low byte
        data[0] = 0x03; // high byte
        data[1] = 0xE8; // low byte

        // Set second elevation to 2000 meters (0x07D0 in hex)
        data[2] = 0x07; // high byte
        data[3] = 0xD0; // low byte

        // Set third elevation to -500 meters (0xFE0C in hex, two's complement)
        data[4] = 0xFE; // high byte
        data[5] = 0x0C; // low byte

        // Act
        short[] result = SrtmDecoder.Decode(data);

        // Assert
        Assert.Equal(1000, result[0]);
        Assert.Equal(2000, result[1]);
        Assert.Equal(-500, result[2]);
    }

    [Fact]
    public void Decode_MissingDataValue_IsPreserved()
    {
        // Arrange
        byte[] data = new byte[ExpectedByteCount];
        
        // Set first elevation to missing data value (-32768 = 0x8000 in hex)
        data[0] = 0x80; // high byte
        data[1] = 0x00; // low byte

        // Act
        short[] result = SrtmDecoder.Decode(data);

        // Assert
        Assert.Equal(MissingDataValue, result[0]);
    }

    [Fact]
    public void Decode_AllZeros_ReturnsZeroElevations()
    {
        // Arrange
        byte[] data = new byte[ExpectedByteCount]; // all zeros by default

        // Act
        short[] result = SrtmDecoder.Decode(data);

        // Assert
        Assert.All(result, elevation => Assert.Equal(0, elevation));
    }

    [Fact]
    public void Decode_MaxPositiveValue_ParsesCorrectly()
    {
        // Arrange
        byte[] data = new byte[ExpectedByteCount];
        
        // Set first elevation to max positive value (32767 = 0x7FFF in hex)
        data[0] = 0x7F; // high byte
        data[1] = 0xFF; // low byte

        // Act
        short[] result = SrtmDecoder.Decode(data);

        // Assert
        Assert.Equal(32767, result[0]);
    }

    [Fact]
    public void Decode_MaxNegativeValue_ParsesCorrectly()
    {
        // Arrange
        byte[] data = new byte[ExpectedByteCount];
        
        // Set first elevation to max negative value (-32768 = 0x8000 in hex)
        data[0] = 0x80; // high byte
        data[1] = 0x00; // low byte

        // Act
        short[] result = SrtmDecoder.Decode(data);

        // Assert
        Assert.Equal(-32768, result[0]);
    }

    [Fact]
    public void Decode_IncorrectByteCount_ThrowsArgumentException()
    {
        // Arrange
        byte[] dataTooSmall = new byte[1000];
        byte[] dataTooLarge = new byte[ExpectedByteCount + 1000];

        // Act & Assert
        var exceptionSmall = Assert.Throws<ArgumentException>(() => SrtmDecoder.Decode(dataTooSmall));
        var exceptionLarge = Assert.Throws<ArgumentException>(() => SrtmDecoder.Decode(dataTooLarge));

        Assert.Contains("Expected", exceptionSmall.Message);
        Assert.Contains("Expected", exceptionLarge.Message);
    }

    [Fact]
    public void Decode_EmptyArray_ThrowsArgumentException()
    {
        // Arrange
        byte[] data = Array.Empty<byte>();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => SrtmDecoder.Decode(data));
        Assert.Contains("Expected", exception.Message);
    }

    [Fact]
    public void Decode_RowMajorOrder_MaintainsCorrectOrder()
    {
        // Arrange
        byte[] data = new byte[ExpectedByteCount];
        
        // Set specific elevations at known positions to verify row-major ordering
        // First sample (0,0) = 100
        data[0] = 0x00;
        data[1] = 0x64;
        
        // Second sample (0,1) = 200
        data[2] = 0x00;
        data[3] = 0xC8;
        
        // Sample at position 1201 (start of second row) = 300
        int secondRowIndex = 1201 * 2;
        data[secondRowIndex] = 0x01;
        data[secondRowIndex + 1] = 0x2C;

        // Act
        short[] result = SrtmDecoder.Decode(data);

        // Assert - verify positions
        Assert.Equal(100, result[0]);      // First sample
        Assert.Equal(200, result[1]);      // Second sample in first row
        Assert.Equal(300, result[1201]);   // First sample of second row
    }

    [Fact]
    public void Decode_MixedPositiveAndNegativeValues_ParsesCorrectly()
    {
        // Arrange
        byte[] data = new byte[ExpectedByteCount];
        
        // Create a mix of positive and negative values
        short[] expectedValues = { 5000, -1000, 0, 32767, -32768, 1, -1 };
        
        for (int i = 0; i < expectedValues.Length; i++)
        {
            short value = expectedValues[i];
            int byteIndex = i * 2;
            // Convert to big-endian bytes
            data[byteIndex] = (byte)(value >> 8);     // high byte
            data[byteIndex + 1] = (byte)(value & 0xFF); // low byte
        }

        // Act
        short[] result = SrtmDecoder.Decode(data);

        // Assert
        for (int i = 0; i < expectedValues.Length; i++)
        {
            Assert.Equal(expectedValues[i], result[i]);
        }
    }

    [Fact]
    public void Decode_LastSample_ParsesCorrectly()
    {
        // Arrange
        byte[] data = new byte[ExpectedByteCount];
        
        // Set the last sample to a known value (9999 = 0x270F in hex)
        int lastSampleIndex = (SrtmSize * SrtmSize - 1) * 2;
        data[lastSampleIndex] = 0x27;     // high byte
        data[lastSampleIndex + 1] = 0x0F; // low byte

        // Act
        short[] result = SrtmDecoder.Decode(data);

        // Assert
        Assert.Equal(9999, result[^1]); // Last element
    }

    [Fact]
    public void Decode_IsDeterministic()
    {
        // Arrange
        byte[] data = new byte[ExpectedByteCount];
        
        // Set some sample data
        data[0] = 0x03;
        data[1] = 0xE8;
        data[2] = 0x07;
        data[3] = 0xD0;

        // Act
        short[] result1 = SrtmDecoder.Decode(data);
        short[] result2 = SrtmDecoder.Decode(data);

        // Assert - same input produces same output
        Assert.Equal(result1.Length, result2.Length);
        for (int i = 0; i < result1.Length; i++)
        {
            Assert.Equal(result1[i], result2[i]);
        }
    }
}
