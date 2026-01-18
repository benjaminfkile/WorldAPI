using WorldApi.World;

namespace WorldApi.Tests;

public class BilinearInterpolationTests
{
    private const double Tolerance = 0.000001;

    [Fact]
    public void Interpolate_AtTopLeftCorner_ReturnsTopLeftValue()
    {
        // Arrange - fx=0, fy=0 should return z00
        short z00 = 100, z10 = 200, z01 = 300, z11 = 400;

        // Act
        double result = BilinearInterpolation.Interpolate(z00, z10, z01, z11, 0.0, 0.0);

        // Assert
        Assert.Equal(100.0, result, Tolerance);
    }

    [Fact]
    public void Interpolate_AtTopRightCorner_ReturnsTopRightValue()
    {
        // Arrange - fx=1, fy=0 should return z10
        short z00 = 100, z10 = 200, z01 = 300, z11 = 400;

        // Act
        double result = BilinearInterpolation.Interpolate(z00, z10, z01, z11, 1.0, 0.0);

        // Assert
        Assert.Equal(200.0, result, Tolerance);
    }

    [Fact]
    public void Interpolate_AtBottomLeftCorner_ReturnsBottomLeftValue()
    {
        // Arrange - fx=0, fy=1 should return z01
        short z00 = 100, z10 = 200, z01 = 300, z11 = 400;

        // Act
        double result = BilinearInterpolation.Interpolate(z00, z10, z01, z11, 0.0, 1.0);

        // Assert
        Assert.Equal(300.0, result, Tolerance);
    }

    [Fact]
    public void Interpolate_AtBottomRightCorner_ReturnsBottomRightValue()
    {
        // Arrange - fx=1, fy=1 should return z11
        short z00 = 100, z10 = 200, z01 = 300, z11 = 400;

        // Act
        double result = BilinearInterpolation.Interpolate(z00, z10, z01, z11, 1.0, 1.0);

        // Assert
        Assert.Equal(400.0, result, Tolerance);
    }

    [Fact]
    public void Interpolate_AtCenter_ReturnsAverageOfFourCorners()
    {
        // Arrange - fx=0.5, fy=0.5 should return average
        short z00 = 100, z10 = 200, z01 = 300, z11 = 400;
        double expected = (100.0 + 200.0 + 300.0 + 400.0) / 4.0;

        // Act
        double result = BilinearInterpolation.Interpolate(z00, z10, z01, z11, 0.5, 0.5);

        // Assert
        Assert.Equal(expected, result, Tolerance);
    }

    [Fact]
    public void Interpolate_WithUniformValues_ReturnsConstantValue()
    {
        // Arrange - all corners same value
        short z00 = 500, z10 = 500, z01 = 500, z11 = 500;

        // Act
        double result = BilinearInterpolation.Interpolate(z00, z10, z01, z11, 0.3, 0.7);

        // Assert
        Assert.Equal(500.0, result, Tolerance);
    }

    [Fact]
    public void Interpolate_WithNegativeValues_InterpolatesCorrectly()
    {
        // Arrange
        short z00 = -100, z10 = -200, z01 = -300, z11 = -400;
        double expected = (-100.0 + -200.0 + -300.0 + -400.0) / 4.0;

        // Act
        double result = BilinearInterpolation.Interpolate(z00, z10, z01, z11, 0.5, 0.5);

        // Assert
        Assert.Equal(expected, result, Tolerance);
    }

    [Fact]
    public void Interpolate_AlongXAxis_InterpolatesLinearly()
    {
        // Arrange - fy=0, interpolate only along x
        short z00 = 0, z10 = 100, z01 = 0, z11 = 100;

        // Act
        double resultQuarter = BilinearInterpolation.Interpolate(z00, z10, z01, z11, 0.25, 0.0);
        double resultHalf = BilinearInterpolation.Interpolate(z00, z10, z01, z11, 0.5, 0.0);
        double resultThreeQuarter = BilinearInterpolation.Interpolate(z00, z10, z01, z11, 0.75, 0.0);

        // Assert
        Assert.Equal(25.0, resultQuarter, Tolerance);
        Assert.Equal(50.0, resultHalf, Tolerance);
        Assert.Equal(75.0, resultThreeQuarter, Tolerance);
    }

    [Fact]
    public void Interpolate_AlongYAxis_InterpolatesLinearly()
    {
        // Arrange - fx=0, interpolate only along y
        short z00 = 0, z10 = 0, z01 = 100, z11 = 100;

        // Act
        double resultQuarter = BilinearInterpolation.Interpolate(z00, z10, z01, z11, 0.0, 0.25);
        double resultHalf = BilinearInterpolation.Interpolate(z00, z10, z01, z11, 0.0, 0.5);
        double resultThreeQuarter = BilinearInterpolation.Interpolate(z00, z10, z01, z11, 0.0, 0.75);

        // Assert
        Assert.Equal(25.0, resultQuarter, Tolerance);
        Assert.Equal(50.0, resultHalf, Tolerance);
        Assert.Equal(75.0, resultThreeQuarter, Tolerance);
    }

    [Fact]
    public void Interpolate_IsDeterministic()
    {
        // Arrange
        short z00 = 100, z10 = 200, z01 = 300, z11 = 400;

        // Act
        double result1 = BilinearInterpolation.Interpolate(z00, z10, z01, z11, 0.3, 0.7);
        double result2 = BilinearInterpolation.Interpolate(z00, z10, z01, z11, 0.3, 0.7);

        // Assert
        Assert.Equal(result1, result2, Tolerance);
    }
}
