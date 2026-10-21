using Africanideasfaceapi.Helpers;
using System;

namespace Africanideasfaceapi.Helpers;

public static class TimeHelper
{
    private static readonly TimeZoneInfo SastTimeZone = GetSastTimeZone();

    private static TimeZoneInfo GetSastTimeZone()
    {
        try
        {
            // Windows TimeZone ID
            return TimeZoneInfo.FindSystemTimeZoneById("South Africa Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                // IANA/Linux TimeZone ID
                return TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");
            }
            catch (TimeZoneNotFoundException)
            {
                // Fallback to UTC offset +2 if both fail
                return TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "SAST", "SAST");
            }
        }
    }

    /// <summary>
    /// Gets the current time in South Africa Standard Time (SAST).
    /// </summary>
    public static DateTime GetNow()
    {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SastTimeZone);
    }

    /// <summary>
    /// Gets today's date in South Africa Standard Time (SAST).
    /// </summary>
    public static DateTime GetToday()
    {
        return GetNow().Date;
    }

    /// <summary>
    /// Converts a UTC DateTime to South Africa Standard Time (SAST).
    /// </summary>
    public static DateTime ConvertToSast(DateTime utcDateTime)
    {
        if (utcDateTime.Kind == DateTimeKind.Unspecified)
        {
            utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        }
        return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, SastTimeZone);
    }

    /// <summary>
    /// Formats a DateTime as SAST string for display.
    /// </summary>
    public static string ToSastString(this DateTime dt, string format = "yyyy-MM-dd HH:mm:ss")
    {
        var sastTime = dt.Kind == DateTimeKind.Utc ? ConvertToSast(dt) : dt;
        return sastTime.ToString(format);
    }

    /// <summary>
    /// Returns a human-friendly "time ago" string for a given DateTime.
    /// </summary>
    public static string GetTimeAgo(DateTime dateTime)
    {
        var timeSpan = GetNow() - dateTime;

        if (timeSpan.TotalMinutes < 1) return "Just now";
        if (timeSpan.TotalMinutes < 60) return $"{(int)timeSpan.TotalMinutes}m ago";
        if (timeSpan.TotalHours < 24) return $"{(int)timeSpan.TotalHours}h ago";
        if (timeSpan.TotalDays < 7) return $"{(int)timeSpan.TotalDays}d ago";
        
        return dateTime.ToString("dd MMM yyyy");
    }
}
