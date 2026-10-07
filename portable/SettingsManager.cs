using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuickCal
{
    public enum CalendarLanguage
    {
        English,
        Norwegian,
        UseSystemLanguage
    }

    /// <summary>Everything that is saved between runs.</summary>
    public sealed class AppSettings
    {
        public CalendarLanguage Language { get; set; } = CalendarLanguage.UseSystemLanguage;
        public bool ShowHolidays { get; set; } = true;
        public bool ShowEasterAsFullWeek { get; set; } = true;
        public int NumberOfWeeks { get; set; } = 3;
        public bool TrayHintShown { get; set; }
        public bool WelcomeShown { get; set; }

        // Window position and size (null = not saved yet)
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }
    }

    /// <summary>
    /// Replaces ApplicationData.LocalSettings with a small JSON file.
    /// The file is stored next to QuickCal.exe so the settings travel with the app (truly portable).
    /// If that folder is read-only, %AppData%\QuickCal is used instead.
    /// Also holds all display texts (English / Norwegian), as before.
    /// </summary>
    public sealed class SettingsManager
    {
        private const string FileName = "QuickCal.settings.json";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private string settingsPath;

        public AppSettings Values { get; private set; } = new AppSettings();

        private static string ExeFolder =>
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

        private static string PortablePath => Path.Combine(ExeFolder, FileName);

        private static string AppDataPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickCal", FileName);

        public void Load()
        {
            foreach (string candidate in new[] { PortablePath, AppDataPath })
            {
                try
                {
                    if (File.Exists(candidate))
                    {
                        Values = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(candidate), JsonOptions) ?? new AppSettings();
                        settingsPath = candidate;
                        break;
                    }
                }
                catch (Exception)
                {
                    // Corrupt or unreadable file: fall back to defaults.
                }
            }
            Values.NumberOfWeeks = Math.Clamp(Values.NumberOfWeeks, 0, 3);
        }

        public void Save()
        {
            string json = JsonSerializer.Serialize(Values, JsonOptions);
            string preferred = settingsPath ?? PortablePath;
            try
            {
                File.WriteAllText(preferred, json);
                settingsPath = preferred;
            }
            catch (Exception) when (preferred != AppDataPath)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(AppDataPath));
                    File.WriteAllText(AppDataPath, json);
                    settingsPath = AppDataPath;
                }
                catch (Exception)
                {
                    // Nowhere to save; settings will just not be remembered.
                }
            }
            catch (Exception)
            {
            }
        }

        // ---------------------------------------------------------------------------------
        // Display texts
        // ---------------------------------------------------------------------------------

        /// <summary>True when holiday names etc. should be Norwegian.</summary>
        public bool UseNorwegianTexts
        {
            get
            {
                switch (Values.Language)
                {
                    case CalendarLanguage.Norwegian: return true;
                    case CalendarLanguage.UseSystemLanguage: return IsNorwegianCulture(SystemCulture);
                    default: return false;
                }
            }
        }

        /// <summary>The Windows display language (e.g. nb-NO on Norwegian Windows).</summary>
        private static CultureInfo SystemCulture => CultureInfo.CurrentUICulture;

        private static bool IsNorwegianCulture(CultureInfo culture)
        {
            string lang = culture.TwoLetterISOLanguageName;
            return lang == "nb" || lang == "nn" || lang == "no";
        }

        public string[] GetMonths()
        {
            switch (Values.Language)
            {
                case CalendarLanguage.Norwegian:
                    return new[] { "Januar", "Februar", "Mars", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Desember" };
                case CalendarLanguage.UseSystemLanguage:
                    return SystemCulture.DateTimeFormat.MonthNames.Take(12).Select(Capitalize).ToArray();
                default:
                    return new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
            }
        }

        /// <summary>Column headers: week label followed by Monday ... Sunday.</summary>
        public string[] GetDayNames()
        {
            switch (Values.Language)
            {
                case CalendarLanguage.Norwegian:
                    return new[] { "Uke", "Ma", "Ti", "On", "To", "Fr", "Lø", "Sø" };
                case CalendarLanguage.UseSystemLanguage:
                    string[] shortest = SystemCulture.DateTimeFormat.ShortestDayNames; // Sunday first, e.g. "sø.", "ma." in Norwegian
                    var result = new string[8];
                    result[0] = UseNorwegianTexts ? "Uke" : "Week";
                    for (int i = 0; i < 7; i++)
                    {
                        result[i + 1] = Capitalize(shortest[(i + 1) % 7].TrimEnd('.')); // Monday first, "ma." -> "Ma"
                    }
                    return result;
                default:
                    return new[] { "Week", "Mo", "Tu", "We", "Th", "Fr", "Sa", "Su" };
            }
        }

        public string[] StaticDates() => UseNorwegianTexts
            ? new[] { "1. Nyttårsdag", "Arbeidernes dag", "Grunnlovsdagen", "Nyttårsaften" }
            : new[] { "New Year's Day", "Labour Day", "Constitution Day", "New Year's Eve" };

        public string[] EasterRelated() => UseNorwegianTexts
            ? new[] { "Kristi himmelfartsdag", "1. Pinsedag", "2. Pinsedag" }
            : new[] { "Ascension Day", "Pentecost", "Whit Monday" };

        public string[] EasterSpecific() => UseNorwegianTexts
            ? new[] { "Skjærtorsdag", "Langfredag", "Påskeaften", "1. påskedag", "2. påskedag", "Påske" }
            : new[] { "Maundy Thursday", "Good Friday", "Easter Eve", "Easter Sunday", "Easter Monday", "Easter" };

        public string[] ChristmasSpecific() => UseNorwegianTexts
            ? new[] { "Lillejulaften", "Julaften", "1. Juledag", "2. Juledag", "Juleferie" }
            : new[] { "Christmas break", "Christmas Eve", "Christmas Day", "Boxing Day", "Christmas break" };

        public string SummerBreakText() => UseNorwegianTexts ? "Sommerferie" : "Summer break";

        public string BirthdayText() => UseNorwegianTexts ? "Utviklerens bursdag" : "Developer's birthday";

        // Tray texts
        public string TrayTooltip(int week) => UseNorwegianTexts ? $"QuickCal – uke {week}" : $"QuickCal – week {week}";
        public string TrayOpenText() => UseNorwegianTexts ? "Vis QuickCal" : "Show QuickCal";
        public string TrayExitText() => UseNorwegianTexts ? "Avslutt" : "Exit";
        public string TrayHiddenHint() => UseNorwegianTexts
            ? "QuickCal kjører fortsatt her. Høyreklikk på ikonet og velg Avslutt for å lukke helt."
            : "QuickCal is still running here. Right-click the icon and choose Exit to quit.";

        private static string Capitalize(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0], SystemCulture) + s.Substring(1);
    }
}
