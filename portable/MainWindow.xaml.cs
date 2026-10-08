using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuickCal
{
    /// <summary>
    /// The main window. Ported from the UWP MainPage; layout and colors are kept the same.
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly Color HeaderBlue = Color.FromArgb(0xFF, 0x14, 0x32, 0x75);
        private static readonly Color HolidayRed = Color.FromArgb(150, 245, 39, 63);
        private static readonly Color WeekColumnBlue = Color.FromArgb(255, 103, 125, 225);
        private static readonly Color HoverCyan = Color.FromArgb(255, 50, 220, 242);

        private readonly SettingsManager settingsManager;
        private DateTime activeMonth = FirstOfMonth(DateTime.Today); // always the 1st, so changing month/year never fails
        private Dictionary<DateTime, string> holidays = new Dictionary<DateTime, string>();
        private WindowState lastState = WindowState.Normal;
        private ImageBrush goldBrush;

        public MainWindow(SettingsManager settingsManager)
        {
            InitializeComponent();
            this.settingsManager = settingsManager;

            RestoreWindowPlacement();

            // Remember the normal (not maximized, not compact) size, so we can return to it later
            rootViewbox.SizeChanged += (s, e) =>
            {
                if (IsLoaded && WindowState == WindowState.Normal && !IsCompact) normalClientSize = e.NewSize;
            };

            ApplyUiTexts();
            UpdateDayLabels();
            UpdateGrids();
        }

        private static DateTime FirstOfMonth(DateTime date) => new DateTime(date.Year, date.Month, 1);

        // =====================================================================================
        // Public helpers used by App
        // =====================================================================================

        /// <summary>Same as before: minimizing/hiding and showing the app jumps back to today.</summary>
        public void ResetToToday()
        {
            activeMonth = FirstOfMonth(DateTime.Today);
            activeYear = DateTime.Today.Year;
            ShowCalendarView();
        }

        /// <summary>Called when the date changes (midnight / wake from sleep) so "today" is highlighted correctly.</summary>
        public void RefreshCalendar()
        {
            if (IsYearView) UpdateYearView(); else UpdateGrids();
        }

        // =====================================================================================
        // Calendar drawing
        // =====================================================================================

        private void UpdateGrids()
        {
            LoadHolidays(new[] { -1, 0, 1 }.Select(i => activeMonth.AddMonths(i).Year));
            FillGridWithDates(calGrid1, month1, year1, activeMonth.AddMonths(-1));
            FillGridWithDates(calGrid2, month2, year2, activeMonth);
            FillGridWithDates(calGrid3, month3, year3, activeMonth.AddMonths(1));
        }

        private void LoadHolidays(IEnumerable<int> years)
        {
            holidays = new Dictionary<DateTime, string>();
            if (!settingsManager.Values.ShowHolidays) return;

            foreach (int year in years.Distinct())
            {
                foreach (var pair in HolidayCalculator.ForYear(settingsManager, year))
                {
                    holidays.TryAdd(pair.Key, pair.Value);
                }
            }
        }

        private void FillGridWithDates(Grid grid, TextBlock monthText, TextBlock yearText, DateTime month)
        {
            grid.Children.Clear();

            monthText.Text = settingsManager.GetMonths()[month.Month - 1];
            if (yearText != null) yearText.Text = month.Year.ToString(CultureInfo.InvariantCulture);

            DateTime first = FirstOfMonth(month);
            int offset = ((int)first.DayOfWeek + 6) % 7; // Monday = 0 ... Sunday = 6
            int daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
            DateTime today = DateTime.Today;

            for (int day = 1; day <= daysInMonth; day++)
            {
                int cell = offset + day - 1;
                int rowIndex = cell / 7;
                int columnIndex = cell % 7 + 1;
                DateTime date = new DateTime(month.Year, month.Month, day);

                TextBlock textBlock = CreateTextBlock(day.ToString(CultureInfo.InvariantCulture), Colors.Black);
                Border border;

                if (date == today && date.Month == 9 && date.Day == 5)
                {
                    // Today on 5 September: keep the gold, with today's red border around it
                    textBlock.FontWeight = FontWeights.Bold;
                    border = CreateColoredBorder(textBlock, HeaderBlue, false, settingsManager.BirthdayText());
                    // Fade only the gold (not the whole cell) so the red border stays crisp
                    Brush fadedGold = GetGoldBrush().CloneCurrentValue();
                    fadedGold.Opacity = 0.8;
                    border.Background = fadedGold;
                    border.BorderBrush = Brushes.Red;
                    border.BorderThickness = new Thickness(2);
                }
                else if (date == today)
                {
                    // Today
                    textBlock.Foreground = Brushes.White;
                    border = CreateColoredBorder(textBlock, HeaderBlue, attachEventHandlers: false);
                    border.BorderBrush = Brushes.Red;
                    border.BorderThickness = new Thickness(2);
                }
                else if (holidays.TryGetValue(date, out string tooltip))
                {
                    textBlock.Foreground = Brushes.White;
                    border = CreateColoredBorder(textBlock, HolidayRed, true, tooltip);
                }
                else if (date.Month == 9 && date.Day == 5)
                {
                    // 5 September (gold effect)
                    textBlock.FontWeight = FontWeights.Bold;
                    border = CreateColoredBorder(textBlock, Color.FromArgb(0xFF, 0xFF, 0xD7, 0x00), true, settingsManager.BirthdayText());
                    border.Background = GetGoldBrush();
                    border.Opacity = 0.8;
                }
                else
                {
                    border = CreateBorder(textBlock);
                }

                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, columnIndex);
                grid.Children.Add(border);

                // ISO week number in column 0, once per row
                if (columnIndex == 1 || day == 1)
                {
                    int isoWeek = ISOWeek.GetWeekOfYear(date);
                    TextBlock weekText = CreateTextBlock(isoWeek.ToString(CultureInfo.InvariantCulture), Colors.White);
                    var weekViewbox = new Viewbox { Stretch = Stretch.None, Child = weekText };
                    Border weekBorder = CreateBorder(weekViewbox, attachEventHandlers: false);
                    weekBorder.Background = new SolidColorBrush(WeekColumnBlue);
                    Grid.SetRow(weekBorder, rowIndex);
                    Grid.SetColumn(weekBorder, 0);
                    grid.Children.Add(weekBorder);
                }
            }
        }

        private ImageBrush GetGoldBrush()
        {
            if (goldBrush == null)
            {
                goldBrush = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/Assets/gold_texture.jpg")))
                {
                    Stretch = Stretch.Fill
                };
                goldBrush.Freeze();
            }
            return goldBrush;
        }

        private static TextBlock CreateTextBlock(string text, Color textColor)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(textColor),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Padding = new Thickness(1.6),
            };
        }

        private Border CreateBorder(UIElement child, bool attachEventHandlers = true)
        {
            var border = new Border
            {
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Child = new Viewbox { Child = child },
                Background = Brushes.Transparent // transparent (not null) so the whole cell reacts to the mouse
            };
            if (attachEventHandlers) AttachDayHandlers(border);
            return border;
        }

        private Border CreateColoredBorder(UIElement child, Color backgroundColor, bool attachEventHandlers = true, string tooltipMessage = null)
        {
            var border = new Border
            {
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(backgroundColor),
                Child = new Viewbox { Child = child }
            };
            if (attachEventHandlers) AttachDayHandlers(border);
            if (!string.IsNullOrWhiteSpace(tooltipMessage))
            {
                border.ToolTip = new ToolTip { Content = tooltipMessage };
            }
            return border;
        }

        private void AttachDayHandlers(Border border)
        {
            border.MouseEnter += Button_MouseEnter;
            border.MouseLeave += Button_MouseLeave;
            border.MouseLeftButtonDown += Element_MouseDown;
        }

        private void UpdateDayLabels()
        {
            string[] dayNames = settingsManager.GetDayNames();
            for (int i = 1; i <= 3; i++)
            {
                for (int x = 0; x < 8; x++)
                {
                    if (FindName($"day{i}{x}") is TextBlock label) label.Text = dayNames[x];
                }
            }
        }

        // =====================================================================================
        // Year view: the whole year (4 x 3 months) when the window is maximized
        // =====================================================================================

        private const double NormalViewWidth = 600, NormalViewHeight = 220;
        private const double YearViewWidth = 1024, YearViewHeight = 680;
        private int activeYear = DateTime.Today.Year;

        private bool IsMaximized => WindowState == WindowState.Maximized;
        private bool IsYearView => yearContent.Visibility == Visibility.Visible;

        /// <summary>Shows the year view when maximized, otherwise the normal 3-month view.</summary>
        private void ShowCalendarView()
        {
            if (IsMaximized)
            {
                ShowTab(yearContent);
                UpdateYearView();
            }
            else
            {
                ShowTab(tab1Content);
                UpdateGrids();
            }
        }

        private void UpdateYearView()
        {
            yearTitle.Text = activeYear.ToString(CultureInfo.InvariantCulture);
            LoadHolidays(new[] { activeYear });

            string[] dayNames = settingsManager.GetDayNames();
            yearGrid.Children.Clear();
            for (int m = 1; m <= 12; m++)
            {
                yearGrid.Children.Add(CreateMonthBlock(new DateTime(activeYear, m, 1), dayNames));
            }
        }

        /// <summary>One month in the year view: same look as the normal view, header with month name only.</summary>
        private UIElement CreateMonthBlock(DateTime month, string[] dayNames)
        {
            var block = new Grid { Margin = new Thickness(4) };
            for (int c = 0; c < 8; c++) block.ColumnDefinitions.Add(new ColumnDefinition());
            for (int r = 0; r < 8; r++) block.RowDefinitions.Add(new RowDefinition());

            // Blue month bar
            var bar = new Border { Background = new SolidColorBrush(HeaderBlue) };
            Grid.SetColumnSpan(bar, 8);
            block.Children.Add(bar);

            var monthText = new TextBlock { Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
            var monthBox = new Viewbox { Stretch = Stretch.Uniform, Child = monthText };
            Grid.SetColumn(monthBox, 1);
            Grid.SetColumnSpan(monthBox, 6);
            block.Children.Add(monthBox);

            // Day labels
            for (int x = 0; x < 8; x++)
            {
                var label = new Border
                {
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(2),
                    Padding = new Thickness(x == 0 ? 0.8 : 0.5),
                    Child = new Viewbox { Stretch = Stretch.Uniform, Child = new TextBlock { Text = dayNames[x], Foreground = Brushes.Black } }
                };
                Grid.SetRow(label, 1);
                Grid.SetColumn(label, x);
                block.Children.Add(label);
            }

            // Dates (6 rows x 8 columns, same drawing code as the normal view)
            var dates = new Grid();
            for (int c = 0; c < 8; c++) dates.ColumnDefinitions.Add(new ColumnDefinition());
            for (int r = 0; r < 6; r++) dates.RowDefinitions.Add(new RowDefinition());
            Grid.SetRow(dates, 2);
            Grid.SetRowSpan(dates, 6);
            Grid.SetColumnSpan(dates, 8);
            block.Children.Add(dates);

            FillGridWithDates(dates, monthText, null, month);
            return block;
        }

        private void YearBack_Click(object sender, MouseButtonEventArgs e)
        {
            activeYear--;
            UpdateYearView();
        }

        private void YearForward_Click(object sender, MouseButtonEventArgs e)
        {
            activeYear++;
            UpdateYearView();
        }

        // =====================================================================================
        // Mouse: hover effects, tooltips, navigation
        // =====================================================================================

        private void Button_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                // Remember the original look the first time so MouseLeave can restore it
                if (border.Tag == null) border.Tag = Tuple.Create(border.BorderBrush, border.BorderThickness);
                border.BorderBrush = new SolidColorBrush(HoverCyan);
                border.BorderThickness = new Thickness(2);
            }
        }

        private void Button_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                if (border.ToolTip is ToolTip toolTip) toolTip.IsOpen = false;
                if (border.Tag is Tuple<Brush, Thickness> original)
                {
                    border.BorderBrush = original.Item1;
                    border.BorderThickness = original.Item2;
                }
            }
        }

        private void Element_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderBrush = new SolidColorBrush(HoverCyan);
                border.BorderThickness = new Thickness(1);
            }
        }

        private void Element_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderBrush = Brushes.Transparent;
                border.BorderThickness = new Thickness(0);
            }
        }

        /// <summary>Clicking a holiday shows its tooltip right away (as in the UWP version).</summary>
        private void Element_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.ToolTip is ToolTip toolTip)
            {
                toolTip.PlacementTarget = border;
                toolTip.IsOpen = true;
            }
        }

        private void BackButton_Click(object sender, MouseButtonEventArgs e)
        {
            activeMonth = activeMonth.AddMonths(-1);
            UpdateGrids();
        }

        private void ForwardButton_Click(object sender, MouseButtonEventArgs e)
        {
            activeMonth = activeMonth.AddMonths(1);
            UpdateGrids();
        }

        /// <summary>Click the middle month name to pick a month.</summary>
        private void Month2_Click(object sender, MouseButtonEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = month2Border, Placement = PlacementMode.Bottom };
            string[] months = settingsManager.GetMonths();
            for (int i = 0; i < 12; i++)
            {
                int monthNumber = i + 1;
                var item = new MenuItem { Header = months[i], IsChecked = monthNumber == activeMonth.Month };
                item.Click += (s, args) =>
                {
                    activeMonth = new DateTime(activeMonth.Year, monthNumber, 1);
                    UpdateGrids();
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        /// <summary>Click the middle year to pick a year (±3 years).</summary>
        private void Year2_Click(object sender, MouseButtonEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = year2Border, Placement = PlacementMode.Bottom };
            int activeYear = activeMonth.Year;
            for (int year = activeYear - 3; year <= activeYear + 3; year++)
            {
                int selectedYear = year;
                var item = new MenuItem { Header = year.ToString(CultureInfo.InvariantCulture), IsChecked = year == activeYear };
                item.Click += (s, args) =>
                {
                    activeMonth = new DateTime(selectedYear, activeMonth.Month, 1);
                    UpdateGrids();
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        // =====================================================================================
        // "Always on top" - same as the UWP CompactOverlay mode:
        // a small 405x250 window on top of everything, showing the calendar unscaled
        // so only the first two months are visible. Turning it off restores the previous size.
        // =====================================================================================

        private const double CompactWidth = 405;
        private const double CompactHeight = 250;
        private Size normalClientSize = new Size(600, 220);

        private bool IsCompact => Topmost;

        private bool restoreNormalSizeAfterMaximize;

        private void OnTop_Click(object sender, MouseButtonEventArgs e)
        {
            if (IsCompact)
            {
                ExitCompact(resize: true);
                return;
            }

            if (IsMaximized)
            {
                // From the year view: leave full screen first, then go compact
                restoreNormalSizeAfterMaximize = false;
                WindowState = WindowState.Normal;
            }
            EnterCompact();
        }

        private void EnterCompact()
        {
            Topmost = true;
            alwaysOnTopPanel.Background = Brushes.LightGreen;
            yearAlwaysOnTopPanel.Background = Brushes.LightGreen;
            rootViewbox.Stretch = Stretch.None;
            rootViewbox.HorizontalAlignment = HorizontalAlignment.Left;
            rootViewbox.VerticalAlignment = VerticalAlignment.Bottom;
            SetClientSize(CompactWidth, CompactHeight);
        }

        private void ExitCompact(bool resize)
        {
            Topmost = false;
            alwaysOnTopPanel.Background = Brushes.White;
            yearAlwaysOnTopPanel.Background = Brushes.White;
            rootViewbox.Stretch = Stretch.Uniform;
            rootViewbox.HorizontalAlignment = HorizontalAlignment.Stretch;
            rootViewbox.VerticalAlignment = VerticalAlignment.Stretch;
            if (resize) SetClientSize(normalClientSize.Width, normalClientSize.Height);
        }

        /// <summary>
        /// Sizes the window so its inner area is exactly width x height,
        /// then lets the user resize freely again.
        /// </summary>
        private void SetClientSize(double width, double height)
        {
            rootViewbox.Width = width;
            rootViewbox.Height = height;
            SizeToContent = SizeToContent.WidthAndHeight;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(ReleaseFixedSize));
        }

        private void ReleaseFixedSize()
        {
            SizeToContent = SizeToContent.Manual;
            rootViewbox.Width = double.NaN;
            rootViewbox.Height = double.NaN;
        }

        /// <summary>First time the window is shown: it now has the 600x220 start size, so allow resizing.</summary>
        private void Window_ContentRendered(object sender, EventArgs e)
        {
            if (SizeToContent != SizeToContent.Manual) ReleaseFixedSize();
        }

        // =====================================================================================
        // Keyboard: ← → month, ↑ ↓ year, Space = today
        // =====================================================================================

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.IsRepeat) return;

            if (IsYearView)
            {
                switch (e.Key)
                {
                    case Key.Right:
                    case Key.Up: activeYear++; break;
                    case Key.Left:
                    case Key.Down: activeYear--; break;
                    case Key.Space: activeYear = DateTime.Today.Year; break;
                    default: return;
                }
                UpdateYearView();
                e.Handled = true;
                return;
            }

            if (tab1Content.Visibility != Visibility.Visible) return;

            switch (e.Key)
            {
                case Key.Right: activeMonth = activeMonth.AddMonths(1); break;
                case Key.Left: activeMonth = activeMonth.AddMonths(-1); break;
                case Key.Up: activeMonth = activeMonth.AddYears(1); break;
                case Key.Down: activeMonth = activeMonth.AddYears(-1); break;
                case Key.Space: activeMonth = FirstOfMonth(DateTime.Today); break;
                default: return;
            }
            UpdateGrids();
            e.Handled = true;
        }

        // =====================================================================================
        // Settings page (tab 2)
        // =====================================================================================

        private void ShowTab(Grid tab)
        {
            bool year = tab == yearContent;
            tabContainer.Width = year ? YearViewWidth : NormalViewWidth;
            tabContainer.Height = year ? YearViewHeight : NormalViewHeight;
            // Settings pages in a maximized window: normal size, centered (not blown up to full screen)
            bool calendarPage = year || tab == tab1Content;
            rootViewbox.StretchDirection = IsMaximized && !calendarPage ? StretchDirection.DownOnly : StretchDirection.Both;

            yearContent.Visibility = year ? Visibility.Visible : Visibility.Collapsed;
            tab1Content.Visibility = tab == tab1Content ? Visibility.Visible : Visibility.Collapsed;
            tab2Content.Visibility = tab == tab2Content ? Visibility.Visible : Visibility.Collapsed;
            tab3Content.Visibility = tab == tab3Content ? Visibility.Visible : Visibility.Collapsed;
            tab4Content.Visibility = tab == tab4Content ? Visibility.Visible : Visibility.Collapsed;
            tab5Content.Visibility = tab == tab5Content ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SettingsButton_Click(object sender, MouseButtonEventArgs e)
        {
            AppSettings values = settingsManager.Values;
            languageComboBox.SelectedIndex = (int)values.Language;
            showHolidaysCheckbox.IsChecked = values.ShowHolidays;
            easterFullWeekCheckbox.IsChecked = values.ShowEasterAsFullWeek;
            numberOfWeeksTextBox.Text = values.NumberOfWeeks.ToString(CultureInfo.InvariantCulture);
            ShowTab(tab2Content);
        }

        private void BackToTab1_Click(object sender, RoutedEventArgs e)
        {
            AppSettings values = settingsManager.Values;
            if (languageComboBox.SelectedIndex >= 0) values.Language = (CalendarLanguage)languageComboBox.SelectedIndex;
            values.ShowHolidays = showHolidaysCheckbox.IsChecked;
            values.ShowEasterAsFullWeek = easterFullWeekCheckbox.IsChecked;
            if (int.TryParse(numberOfWeeksTextBox.Text, out int weeks)) values.NumberOfWeeks = Math.Clamp(weeks, 0, 3);
            settingsManager.Save();
            App.Current.OnSettingsChanged();

            ApplyUiTexts();
            UpdateDayLabels();
            ShowCalendarView();
        }

        private void NumberOfWeeks_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(c => c >= '0' && c <= '3');
        }

        private void NumberOfWeeks_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            e.CancelCommand();
        }

        // =====================================================================================
        // Autostart page (tab 3) - replaces the UWP StartupTask
        // =====================================================================================

        private void EnableAutostart_Click(object sender, RoutedEventArgs e)
        {
            subPageReturnTab = tab2Content;
            ShowTab(tab3Content);
            UpdateStartupStateText();
        }

        /// <summary>The page that opened the autostart / week-icon page (settings or getting started).</summary>
        private Grid subPageReturnTab;

        private void BackFromSubPage_Click(object sender, RoutedEventArgs e)
        {
            if (subPageReturnTab == tab5Content) ShowWelcomePage(subPageReturnFromWelcome: true);
            else ShowTab(tab2Content);
        }

        private void ToggleAutostart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                switch (AutostartManager.GetState())
                {
                    case AutostartState.Disabled:
                        AutostartManager.Enable();
                        break;
                    case AutostartState.Enabled:
                        AutostartManager.Disable();
                        break;
                    case AutostartState.DisabledInTaskManager:
                        MessageBoxResult answer = MessageBox.Show(this,
                            T("You have turned off QuickCal in Task Manager's Startup apps list. " +
                              "If you change your mind, open Task Manager (Ctrl+Shift+Esc), go to Startup apps and enable QuickCal there.\n\n" +
                              "Do you want to remove QuickCal from the startup list completely instead?",
                              "Du har slått av QuickCal i listen over oppstartsapper i Oppgavebehandling. " +
                              "Hvis du ombestemmer deg, åpner du Oppgavebehandling (Ctrl+Shift+Esc), går til Oppstartsapper og slår på QuickCal der.\n\n" +
                              "Vil du heller fjerne QuickCal helt fra oppstartslisten?"),
                            T("QuickCal autostart", "QuickCal autostart"), MessageBoxButton.YesNo, MessageBoxImage.Information);
                        if (answer == MessageBoxResult.Yes) AutostartManager.Disable();
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, T("Could not change autostart: ", "Kunne ikke endre autostart: ") + ex.Message, "QuickCal autostart",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            UpdateStartupStateText();
        }

        private void UpdateStartupStateText()
        {
            AutostartState state;
            try
            {
                state = AutostartManager.GetState();
            }
            catch (Exception)
            {
                requestResult.Text = T("Unknown state", "Ukjent status");
                return;
            }

            switch (state)
            {
                case AutostartState.Enabled:
                    requestResult.Text = T("Enabled", "På");
                    APIbutton.Content = T("Disable autostart", "Slå av autostart");
                    break;
                case AutostartState.DisabledInTaskManager:
                    requestResult.Text = T("Disabled by user (Task Manager)", "Slått av av bruker (Oppgavebehandling)");
                    APIbutton.Content = T("Enable autostart", "Slå på autostart");
                    break;
                default:
                    requestResult.Text = T("Not enabled", "Av");
                    APIbutton.Content = T("Enable autostart", "Slå på autostart");
                    break;
            }
        }

        // =====================================================================================
        // "Always show week number" page (tab 4)
        // Windows does not let apps pin their own tray icon, so this page guides the user.
        // =====================================================================================

        private void TrayIconPage_Click(object sender, RoutedEventArgs e)
        {
            subPageReturnTab = tab2Content;
            ShowTab(tab4Content);
        }

        // =====================================================================================
        // "Getting started" page (tab 5) - shown automatically the first time QuickCal runs
        // =====================================================================================

        /// <summary>Where "Get started" leads: the calendar (first run) or back to settings.</summary>
        private bool welcomeOpenedFromSettings;

        public void ShowWelcomePage() => ShowWelcomePage(subPageReturnFromWelcome: false);

        private void ShowWelcomePage(bool subPageReturnFromWelcome)
        {
            if (!subPageReturnFromWelcome) welcomeOpenedFromSettings = tab2Content.Visibility == Visibility.Visible;
            welcomeWeekIconText.Text = ISOWeek.GetWeekOfYear(DateTime.Today).ToString(CultureInfo.InvariantCulture);
            UpdateWelcomeAutostartButton();
            ShowTab(tab5Content);
        }

        private void WelcomePage_Click(object sender, RoutedEventArgs e) => ShowWelcomePage();

        private void UpdateWelcomeAutostartButton()
        {
            AutostartState state;
            try { state = AutostartManager.GetState(); }
            catch (Exception) { state = AutostartState.Disabled; }

            if (state == AutostartState.Enabled)
            {
                welcomeAutostartButton.Content = T("Autostart is on \u2713", "Autostart er på \u2713");
                welcomeAutostartButton.IsEnabled = false;
            }
            else
            {
                welcomeAutostartButton.Content = T("Enable autostart", "Slå på autostart");
                welcomeAutostartButton.IsEnabled = true;
            }
        }

        private void WelcomeAutostart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AutostartManager.GetState() == AutostartState.Disabled)
                {
                    AutostartManager.Enable();
                    UpdateWelcomeAutostartButton();
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, T("Could not change autostart: ", "Kunne ikke endre autostart: ") + ex.Message, "QuickCal autostart",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Turned off in Task Manager: show the autostart page, which explains what to do
            subPageReturnTab = tab5Content;
            ShowTab(tab3Content);
            UpdateStartupStateText();
        }

        private void WelcomeTray_Click(object sender, RoutedEventArgs e)
        {
            subPageReturnTab = tab5Content;
            ShowTab(tab4Content);
        }

        private void WelcomeDone_Click(object sender, RoutedEventArgs e)
        {
            if (welcomeOpenedFromSettings) ShowTab(tab2Content);
            else ShowCalendarView();
        }

        private void OpenTaskbarSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Opens Settings > Personalization > Taskbar
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    T("Could not open Windows Settings: ", "Kunne ikke åpne Innstillinger i Windows: ") + ex.Message,
                    "QuickCal", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // =====================================================================================
        // Texts (English / Norwegian, following the language setting)
        // =====================================================================================

        private string T(string english, string norwegian) => settingsManager.UseNorwegianTexts ? norwegian : english;

        private void ApplyUiTexts()
        {
            // Calendar page
            alwaysOnTopText.Text = T("Always on top", "Alltid øverst");
            settingsButtonText.Text = T("Settings", "Innstillinger");
            info.Text = T("Spacebar or minimize app to reset date", "Mellomrom eller minimer appen for å gå til i dag");

            // Year view
            yearInfo.Text = T("Spacebar to go to the current year", "Mellomrom for å gå til inneværende år");
            yearAlwaysOnTopText.Text = alwaysOnTopText.Text;
            yearSettingsText.Text = settingsButtonText.Text;

            // Settings page
            settingsTitle.Text = T("Settings", "Innstillinger");
            languageLabel.Text = T("Calendar Language", "Kalenderspråk");
            systemLanguageItem.Content = T("Use system language", "Bruk systemspråk");
            autostartPageButton.Content = T("Enable autostart", "Slå på autostart");
            autostartPageButton.ToolTip = T("Keep the week icon updated after reboot", "Hold ukeikonet oppdatert etter omstart");
            holidaysLabel.Text = T("Show holidays in calendar", "Vis helligdager i kalenderen");
            easterLabel.Text = T("Show Easter as a full week off", "Vis hele påskeuka som fri");
            weeksLabelBefore.Text = T("Show", "Vis");
            weeksLabelAfter.Text = T("number of weeks summer vacation (0-3)", "uker sommerferie (0–3)");
            trayIconPageButton.Content = T("Always show week number by the clock", "Vis ukenummeret alltid ved klokka");
            welcomePageButton.Content = T("Getting started", "Kom i gang");

            // Getting started page
            welcomeTitle.Text = T("Getting started with QuickCal", "Kom i gang med QuickCal");
            welcomeAutostartText.Text = T("Start automatically when you sign in", "Start automatisk når du logger på");
            welcomeTrayText.Text = T("Always show the week number by the clock", "Vis ukenummeret alltid ved klokka");
            welcomeTrayButton.Content = T("Show me how", "Vis meg hvordan");
            welcomePinTitle.Text = T("Pin to the taskbar (optional):", "Fest til oppgavelinjen (valgfritt):");
            welcomePinText.Text = T(
                "right-click QuickCal on the taskbar and choose \u201CPin to taskbar\u201D.",
                "høyreklikk QuickCal på oppgavelinjen og velg \u00ABFest til oppgavelinjen\u00BB.");
            welcomeTip.Text = T(
                "Tip: the arrows, the month name and the year at the top are clickable. \u2190 \u2192 change month, \u2191 \u2193 change year, and Spacebar goes to today. Maximize the window to see the whole year.",
                "Tips: Pilene, månedsnavnet og årstallet øverst er klikkbare. \u2190 \u2192 bytter måned, \u2191 \u2193 bytter år, og mellomrom går til i dag. Maksimer vinduet for å se hele året.");
            welcomeDoneButton.Content = "OK";
            UpdateWelcomeAutostartButton();

            // Autostart page
            autostartTitle.Text = T("Enable autostart of app when computer reboots", "Start QuickCal automatisk når du logger på");
            autostartDescription.Text = T(
                "QuickCal starts quietly in the notification area (next to the clock) when you sign in. No administrator rights needed.",
                "QuickCal starter i bakgrunnen med ukeikonet ved klokka når du logger på. Krever ikke administratorrettigheter.");
            startupStateLabel.Text = T("Startup State:", "Status:");

            // "Always show week number" page
            trayTitle.Text = T("Always show the week number by the clock", "Vis ukenummeret alltid ved klokka");
            trayIntro.Text = T(
                "Windows hides new icons behind the arrow (^) next to the clock. Apps are not allowed to change this themselves, but you can do it in a few seconds:",
                "Windows skjuler nye ikoner bak pila (^) ved klokka. Apper får ikke lov til å endre dette selv, men du kan gjøre det på noen sekunder:");
            trayStep1.Text = T(
                "1.  Click the button below. Windows Settings opens on the Taskbar page.",
                "1.  Trykk på knappen under. Innstillinger i Windows åpnes på siden for oppgavelinjen.");
            trayStep2.Text = T(
                "2.  Scroll down and open \u201COther system tray icons\u201D.",
                "2.  Bla ned og åpne \u00ABAndre ikoner i systemstatusfeltet\u00BB.");
            trayStep3.Text = T(
                "3.  Find QuickCal in the list and turn the switch On. The week number now stays visible next to the clock.",
                "3.  Finn QuickCal i listen og slå bryteren på. Ukenummeret vises nå hele tiden ved klokka.");
            openTaskbarSettingsButton.Content = T("Open taskbar settings", "Åpne innstillinger for oppgavelinjen");
            trayTip.Text = T(
                "Tip: you can also drag the week icon from the arrow (^) and drop it next to the clock. If you move QuickCal.exe to another folder, you need to do this again.",
                "Tips: Du kan også dra ukeikonet ut fra pila (^) og slippe det ved klokka. Flytter du QuickCal.exe til en annen mappe, må du gjøre dette på nytt.");

            UpdateStartupStateText();
        }

        // =====================================================================================
        // Window behaviour
        // =====================================================================================

        private void Window_StateChanged(object sender, EventArgs e)
        {
            WindowState previous = lastState;
            lastState = WindowState;

            if (WindowState == WindowState.Maximized)
            {
                // Full screen shows the whole year. "Always on top" (compact) does not make sense here.
                if (IsCompact)
                {
                    ExitCompact(resize: false);
                    restoreNormalSizeAfterMaximize = true;
                }
                ResetToToday();
            }
            else if (WindowState == WindowState.Normal && previous != WindowState.Normal)
            {
                // Back from maximized or minimized: 3 months with the current month in the middle
                ResetToToday();
                if (previous == WindowState.Maximized && restoreNormalSizeAfterMaximize)
                {
                    restoreNormalSizeAfterMaximize = false;
                    SetClientSize(normalClientSize.Width, normalClientSize.Height);
                }
            }
        }

        /// <summary>The X button hides QuickCal to the notification area so the week icon keeps running.</summary>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveWindowPlacement();
            if (App.Current.IsExiting) return;

            e.Cancel = true;
            Hide();
            settingsManager.Save();
            App.Current.OnMainWindowHidden();
        }

        public void SaveWindowPlacement()
        {
            if (WindowState != WindowState.Normal || !IsLoaded) return;
            AppSettings values = settingsManager.Values;
            values.WindowLeft = Left;
            values.WindowTop = Top;
            if (!IsCompact) // don't remember the small "Always on top" size as the normal size
            {
                values.WindowWidth = ActualWidth;
                values.WindowHeight = ActualHeight;
            }
        }

        private void RestoreWindowPlacement()
        {
            AppSettings values = settingsManager.Values;
            if (values.WindowLeft is double left && values.WindowTop is double top &&
                values.WindowWidth is double width && values.WindowHeight is double height)
            {
                // Only restore if the window would be visible on the current screen setup
                var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                      SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                var window = new Rect(left, top, width, height);
                if (screen.IntersectsWith(window) && width >= MinWidth && height >= MinHeight)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    SizeToContent = SizeToContent.Manual;
                    rootViewbox.Width = double.NaN;  // let the calendar fill the saved size
                    rootViewbox.Height = double.NaN;
                    Left = left;
                    Top = top;
                    Width = width;
                    Height = height;
                }
            }
        }
    }
}
