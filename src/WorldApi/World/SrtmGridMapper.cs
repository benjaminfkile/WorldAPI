namespace WorldApi.World;

public readonly record struct GridCoordinates(double X, double Y);

public static class SrtmGridMapper
{
    public static GridCoordinates ToGridCoordinates(SrtmTileData tile, double latitude, double longitude)
    {
        // X increases eastward (longitude increases)
        // Map longitude to [0, Width-1]
        double x = (longitude - tile.MinLongitude) / (tile.MaxLongitude - tile.MinLongitude) * (tile.Width - 1);

        // Y increases southward (latitude decreases)
        // North edge (MaxLatitude) is row 0
        // South edge (MinLatitude) is row Height-1
        double y = (tile.MaxLatitude - latitude) / (tile.MaxLatitude - tile.MinLatitude) * (tile.Height - 1);

        return new GridCoordinates(x, y);
    }
}
