using Africanideasfaceapi.Models;

namespace Africanideasfaceapi.Services;

public class LocationService
{
    /// <summary>
    /// Calculates the distance between two points in meters using the Haversine formula.
    /// </summary>
    public double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var r = 6371e3; // Earth's radius in meters
        var phi1 = lat1 * Math.PI / 180;
        var phi2 = lat2 * Math.PI / 180;
        var deltaPhi = (lat2 - lat1) * Math.PI / 180;
        var deltaLambda = (lon2 - lon1) * Math.PI / 180;

        var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) *
                Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);
        
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return r * c; // Distance in meters
    }

    /// <summary>
    /// Checks if a location is within the geofence radius of a branch.
    /// </summary>
    public bool IsWithinGeofence(Branch branch, double userLat, double userLon)
    {
        if (branch == null) return false;
        
        var distance = CalculateDistance(branch.Latitude, branch.Longitude, userLat, userLon);
        return distance <= branch.GeofenceRadius;
    }
}
