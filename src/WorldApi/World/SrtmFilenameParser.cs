namespace WorldApi.World;

public static class SrtmFilenameParser
{
    public static DemTile Parse(string filename)
    {
        // Remove .hgt extension if present
        string name = filename.EndsWith(".hgt", StringComparison.OrdinalIgnoreCase)
            ? filename[..^4]
            : filename;

        // Parse latitude: [N|S][lat]
        char latDir = char.ToUpperInvariant(name[0]);
        int latEndIndex = 1;
        while (latEndIndex < name.Length && char.IsDigit(name[latEndIndex]))
        {
            latEndIndex++;
        }

        int latValue = int.Parse(name[1..latEndIndex]);
        double lat = latDir == 'S' ? -latValue : latValue;

        // Parse longitude: [E|W][lon]
        char lonDir = char.ToUpperInvariant(name[latEndIndex]);
        string lonString = name[(latEndIndex + 1)..];
        int lonValue = int.Parse(lonString);
        double lon = lonDir == 'W' ? -lonValue : lonValue;

        // Each tile covers exactly 1° x 1°
        return new DemTile
        {
            MinLatitude = lat,
            MaxLatitude = lat + 1.0,
            MinLongitude = lon,
            MaxLongitude = lon + 1.0,
            S3Key = filename
        };
    }
}
