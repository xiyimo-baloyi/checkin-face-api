using System;
using System.Collections.Generic;
using System.Linq;

namespace Africanideasfaceapi.Services;

public class HolidayService
{
    public bool IsWorkDay(DateTime date)
    {
        return !IsWeekend(date) && !IsPublicHoliday(date);
    }

    public bool IsWeekend(DateTime date)
    {
        return date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
    }

    public bool IsPublicHoliday(DateTime date)
    {
        var year = date.Year;
        var holidays = GetPublicHolidays(year);
        
        return holidays.Any(h => h.Date == date.Date);
    }

    public List<(DateTime Date, string Name)> GetPublicHolidays(int year)
    {
        var holidays = new List<(DateTime Date, string Name)>();

        // Fixed dates
        AddHoliday(holidays, new DateTime(year, 1, 1), "New Year's Day");
        AddHoliday(holidays, new DateTime(year, 3, 21), "Human Rights Day");
        AddHoliday(holidays, new DateTime(year, 4, 27), "Freedom Day");
        AddHoliday(holidays, new DateTime(year, 5, 1), "Workers' Day");
        AddHoliday(holidays, new DateTime(year, 6, 16), "Youth Day");
        AddHoliday(holidays, new DateTime(year, 8, 9), "National Women's Day");
        AddHoliday(holidays, new DateTime(year, 9, 24), "Heritage Day");
        AddHoliday(holidays, new DateTime(year, 12, 16), "Day of Reconciliation");
        AddHoliday(holidays, new DateTime(year, 12, 25), "Christmas Day");
        AddHoliday(holidays, new DateTime(year, 12, 26), "Day of Goodwill");

        // Variable dates (Easter)
        var easterSunday = GetEasterSunday(year);
        AddHoliday(holidays, easterSunday.AddDays(-2), "Good Friday");
        AddHoliday(holidays, easterSunday.AddDays(1), "Family Day");

        return holidays;
    }

    private void AddHoliday(List<(DateTime Date, string Name)> list, DateTime date, string name)
    {
        list.Add((date, name));
        
        // South African rule: If a public holiday falls on a Sunday, the following Monday is a holiday.
        if (date.DayOfWeek == DayOfWeek.Sunday)
        {
            list.Add((date.AddDays(1), $"{name} (Observed)"));
        }
    }

    private DateTime GetEasterSunday(int year)
    {
        // Anonymous Gregorian algorithm
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = ((h + l - 7 * m + 114) % 31) + 1;

        return new DateTime(year, month, day);
    }
}
