using System;

namespace SeaPowerTopo.Render;

/// <summary>A lon/lat rectangle on the map, in degrees (longitudes may run past ±180 across the date line).</summary>
internal readonly struct GeoRect : IEquatable<GeoRect>
{
    public readonly double West, South, East, North;

    public GeoRect(double west, double south, double east, double north)
    {
        West = west;
        South = south;
        East = east;
        North = north;
    }

    public double Width => East - West;
    public double Height => North - South;
    public double CentreLon => (West + East) / 2;

    public bool Contains(GeoRect r) => r.West >= West && r.East <= East && r.South >= South && r.North <= North;

    /// <summary>Holds <paramref name="r"/> in any turn of longitude; a rectangle all the way round holds any longitudes.</summary>
    public bool Holds(GeoRect r) => r.South >= South && r.North <= North &&
                                    (Width >= 360.0 - 1e-9 || Contains(r.Near(CentreLon)));

    /// <summary>The same rectangle a whole number of turns east or west, nearest <paramref name="lon"/>.</summary>
    public GeoRect Near(double lon)
    {
        double turns = Math.Round((lon - CentreLon) / 360.0) * 360.0;
        return turns == 0 ? this : new GeoRect(West + turns, South, East + turns, North);
    }

    // A pixel is at most 20 times wider (in degrees) than high.
    private const double MinCosLat = 0.05;

    /// <summary>cos of the latitude nearest the equator: how wide a degree of longitude gets anywhere in it.</summary>
    public double MaxCosLat => South <= 0 && North >= 0 ? 1.0
        : Math.Cos(Math.Min(Math.Abs(South), Math.Abs(North)) * Math.PI / 180.0);

    /// <summary>
    /// On the globe a degree of longitude is cos(latitude) as wide as one of latitude: pixels this many times wider in
    /// degrees (where the rectangle is widest) are as wide on screen as high.
    /// </summary>
    public double GlobeLonStretch => 1.0 / Math.Max(MinCosLat, MaxCosLat);

    public bool Equals(GeoRect r) => r.West == West && r.South == South && r.East == East && r.North == North;

    public override string ToString() => $"{South:0.0}..{North:0.0}°N {West:0.0}..{East:0.0}°E";
}
