using System;

namespace KEISAN_HRIS_v2.Helpers
{
    public static class TimeFormatHelper
    {
        /// <summary>
        /// Formats minutes to display as "X mins" or "X min" for singular
        /// Example: 45.5 -> "46 mins", 1 -> "1 min", 0 -> "0 min"
        /// </summary>
        public static string FormatMinutes(double minutes)
        {
            if (minutes == 0) return "0 min";

            int roundedMinutes = (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
            return roundedMinutes == 1 ? "1 min" : $"{roundedMinutes} mins";
        }

        /// <summary>
        /// Formats decimal hours to display as "X hrs Y mins" or just "X hrs" if no minutes
        /// Example: 5.75 -> "5 hrs 45 mins", 8.0 -> "8 hrs", 0.5 -> "30 mins"
        /// </summary>
        public static string FormatHours(double hours)
        {
            if (hours == 0) return "0 hrs";

            int wholeHours = (int)hours;
            double decimalPart = hours - wholeHours;
            int minutes = (int)Math.Round(decimalPart * 60, MidpointRounding.AwayFromZero);

            // If rounding minutes gives us 60, add to hours
            if (minutes == 60)
            {
                wholeHours++;
                minutes = 0;
            }

            if (wholeHours == 0)
            {
                // Only minutes
                return minutes == 1 ? "1 min" : $"{minutes} mins";
            }
            else if (minutes == 0)
            {
                // Only hours
                return wholeHours == 1 ? "1 hr" : $"{wholeHours} hrs";
            }
            else
            {
                // Both hours and minutes
                string hourPart = wholeHours == 1 ? "1 hr" : $"{wholeHours} hrs";
                string minPart = minutes == 1 ? "1 min" : $"{minutes} mins";
                return $"{hourPart} {minPart}";
            }
        }

        /// <summary>
        /// Formats decimal hours to display with 2 decimal places
        /// Use this for summary reports or exports where exact values are needed
        /// Example: 5.75 -> "5.75", 8.0 -> "8.00"
        /// </summary>
        public static string FormatHoursDecimal(double hours)
        {
            return hours.ToString("0.00");
        }
    }
}