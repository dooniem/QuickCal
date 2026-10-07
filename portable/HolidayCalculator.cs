using System;
using System.Collections.Generic;
using System.Globalization;

namespace QuickCal
{
    /// <summary>
    /// Norwegian holidays and school vacations. Same rules as the UWP version, with two fixes:
    /// - Summer vacation now uses real ISO weeks (the old calculation was a week early in e.g. 2027 and 2028).
    /// - Easter uses the standard Gregorian (Meeus) algorithm.
    /// </summary>
    public static class HolidayCalculator
    {
        /// <summary>Returns holiday date -> tooltip text for one year. First rule wins if dates overlap.</summary>
        public static Dictionary<DateTime, string> ForYear(SettingsManager settings, int year)
        {
            var result = new Dictionary<DateTime, string>();
            void Add(DateTime date, string text) => result.TryAdd(date.Date, text);

            bool fullEaster = settings.Values.ShowEasterAsFullWeek;
            int summerWeeks = settings.Values.NumberOfWeeks;

            // Fixed dates
            string[] names = settings.StaticDates();
            Add(new DateTime(year, 1, 1), names[0]);
            Add(new DateTime(year, 5, 1), names[1]);
            Add(new DateTime(year, 5, 17), names[2]);
            Add(new DateTime(year, 12, 31), names[3]);

            // Christmas 23-30 December
            names = settings.ChristmasSpecific();
            for (int day = 23; day <= 30; day++)
            {
                string text;
                switch (day)
                {
                    case 23: text = names[0]; break;
                    case 24: text = names[1]; break;
                    case 25: text = names[2]; break;
                    case 26: text = names[3]; break;
                    default: text = names[4]; break;
                }
                Add(new DateTime(year, 12, day), text);
            }

            // Easter
            names = settings.EasterSpecific();
            DateTime easter = EasterSunday(year);
            IEnumerable<DateTime> easterDays;
            if (fullEaster)
            {
                // Monday of Easter week through Easter Monday
                var days = new List<DateTime>();
                DateTime monday = easter.AddDays(-(((int)easter.DayOfWeek + 6) % 7));
                for (DateTime d = monday; d <= easter.AddDays(1); d = d.AddDays(1)) days.Add(d);
                easterDays = days;
            }
            else
            {
                easterDays = new[] { easter.AddDays(-3), easter.AddDays(-2), easter, easter.AddDays(1) };
            }
            foreach (DateTime d in easterDays)
            {
                string text;
                switch ((d - easter).Days)
                {
                    case -3: text = names[0]; break; // Maundy Thursday
                    case -2: text = names[1]; break; // Good Friday
                    case -1: text = names[2]; break; // Easter Eve
                    case 0: text = names[3]; break;  // Easter Sunday
                    case 1: text = names[4]; break;  // Easter Monday
                    default: text = names[5]; break; // "Easter"
                }
                Add(d, text);
            }

            // Ascension and Pentecost
            names = settings.EasterRelated();
            Add(easter.AddDays(39), names[0]);
            Add(easter.AddDays(49), names[1]);
            Add(easter.AddDays(50), names[2]);

            // Summer vacation: the last 1-3 of ISO weeks 28-30
            string summer = settings.SummerBreakText();
            for (int week = 28; week <= 30; week++)
            {
                bool include = summerWeeks == 3 || (summerWeeks == 2 && week >= 29) || (summerWeeks == 1 && week == 30);
                if (!include) continue;
                DateTime monday = ISOWeek.ToDateTime(year, week, DayOfWeek.Monday);
                for (int i = 0; i < 7; i++) Add(monday.AddDays(i), summer);
            }

            return result;
        }

        /// <summary>Gregorian Easter Sunday (Anonymous Gregorian / Meeus algorithm).</summary>
        public static DateTime EasterSunday(int year)
        {
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
}
