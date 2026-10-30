using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Microsoft.Win32;
using QuickCal.Notes;
using static QuickCal.Notes.NotesUiKit;

namespace QuickCal
{
    /// <summary>
    /// The notes page (all notes by month, with search, color filter, color names, Excel, backup and how long notes
    /// are kept) and the Dropbox page, the same as in the web app.
    /// </summary>
    public partial class MainWindow
    {
        private Border notesMenu;            // the small menu open now (one at a time)
        private FrameworkElement notesMenuButton;
        private TextBlock notesFilterCount;
        private Line notesPicsSlash;
        private Ellipse dropboxDot;

        private static readonly Brush ToolBorder = Brush("#999999");
        private static readonly Brush ToolActive = Brush("#dde6f7");
        private static readonly Brush RowHover = Brush("#f0f4ff");

        // =====================================================================================
        // Notes page
        // =====================================================================================

        private void SetUpNotesPage()
        {
            notesFilterCount = new TextBlock { FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brush(Blue), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -2, -2) };
            var filterIcon = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            filterIcon.Children.Add(Icon(16, PathOf(Funnel, null, Brush("#333333"), 1.7)));
            filterIcon.Children.Add(notesFilterCount);
            notesFilterBtn.Child = filterIcon;

            notesColorsBtn.Child = Icon(18,
                PathOf(Palette, Brush("#f4f4f4"), Brush("#333333"), 1.4),
                Dot(8, 8.5, "#a3324a"), Dot(12.5, 7, "#e07b00"), Dot(16, 9.5, "#2e9e5b"), Dot(6.8, 12.8, "#6a4c9c"));

            notesPicsSlash = new Line { X1 = 3, Y1 = 21, X2 = 21, Y2 = 3, Stroke = Brush("#d93025"), StrokeThickness = 2, Visibility = Visibility.Collapsed };
            notesPicsBtn.Child = new Grid { Children = { Symbol("", 15), new Viewbox { Width = 18, Height = 18, Child = new Canvas { Width = 24, Height = 24, Children = { notesPicsSlash } } } } };
            notesCalBtn.Child = Symbol("", 15);

            notesExport.Content = ExcelIcon(20);
            dropboxDot = new Ellipse { Width = 7, Height = 7, Stroke = Brushes.White, StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -3, -3), Visibility = Visibility.Collapsed };
            notesDropbox.Content = new Grid { Children = { DropboxIcon(20), dropboxDot } };
            notesBackup.Content = BackupIcon(20);
            notesKeep.Content = Symbol("", 17);

            notesSearch.TextChanged += (s, e) =>
            {
                notes.Filter.Q = notesSearch.Text;
                notesSearchPlaceholder.Visibility = notesSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                RefreshNotesList();
            };
            notesSearch.KeyDown += (s, e) => { if (e.Key == Key.Escape && notesSearch.Text != "") { notesSearch.Text = ""; e.Handled = true; } };
            notesFilterBtn.MouseLeftButtonUp += (s, e) => ToggleNotesMenu(notesFilterBtn, FillFilterMenu, false);
            notesColorsBtn.MouseLeftButtonUp += (s, e) => ToggleNotesMenu(notesColorsBtn, FillColorsMenu, false);
            notesPicsBtn.MouseLeftButtonUp += (s, e) =>
            {
                notes.Filter.Pics = notes.Filter.Pics == "" ? "with" : notes.Filter.Pics == "with" ? "without" : "";
                RefreshNotesList();
            };
            notesCalBtn.MouseLeftButtonUp += (s, e) => { notes.Filter.Cal = !notes.Filter.Cal; RefreshNotesList(); };
            notesExport.Click += (s, e) => ExportNotes();
            notesDropbox.Click += (s, e) => OpenDropboxPage();
            notesBackup.Click += (s, e) => ToggleNotesMenu(notesBackup, FillBackupMenu, true);
            notesKeep.Click += (s, e) => ToggleNotesMenu(notesKeep, FillKeepMenu, true);
            notesDeleteAll.Click += (s, e) => DeleteAllNotes();
        }

        private static Ellipse Dot(double x, double y, string hex)
        {
            var e = new Ellipse { Width = 3.6, Height = 3.6, Fill = Brush(hex) };
            Canvas.SetLeft(e, x - 1.8);
            Canvas.SetTop(e, y - 1.8);
            return e;
        }

        private void NotesButton_Click(object sender, MouseButtonEventArgs e) => OpenNotesPage();

        private void NotesBack_Click(object sender, RoutedEventArgs e)
        {
            CloseNotesMenu();
            ShowCalendarView();
        }

        private void OpenNotesPage()
        {
            CloseNotesMenu();
            ShowTab(tabNotes);
            FillNotesList();
            notesScroll.ScrollToTop();
        }

        /// <summary>The list again after the search or filter changed (and the calendar, when it follows the filter).</summary>
        private void RefreshNotesList()
        {
            FillNotesList();
            UpdateNotesButtons();
        }

        /// <summary>The notepad buttons by Settings: shown when notes are on, with a blue ring while the calendar follows the filter.</summary>
        private void UpdateNotesButtons()
        {
            bool on = NotesHere;
            bool filtered = on && notes.Filter.Cal && notes.Filter.Active;
            foreach (Border b in new[] { notesButton1, notesButton2 })
            {
                b.BorderBrush = filtered ? Brush(Blue) : Brushes.Black;
                b.Background = filtered ? ToolActive : Brushes.White;
                b.ToolTip = filtered ? T("Notes (the calendar shows only the notes that match the search and filter)", "Notater (kalenderen viser bare notatene som passer søket og filteret)") : T("Notes", "Notater");
            }
            notesButtonBox1.Visibility = notesButtonBox2.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        private void FillNotesList()
        {
            if (notes == null) return;
            notesList.Children.Clear();
            bool here = NotesOn;
            int all = here ? notes.Index.Count : 0;
            List<string> keys = here ? notes.FilteredKeys() : new List<string>();
            NotesFilter f = notes.Filter;

            notesTitle.Text = T("Notes", "Notater");
            notesSearchPlaceholder.Text = T("Search notes", "Søk i notater");
            notesDeleteAll.Content = T("Delete all", "Slett alle");
            notesEmpty.Text = !NotesHere ? "" : !noteStore.Available
                ? T("The notes file " + NoteStore.FileName + " could not be read, so notes are turned off for now. The file is left as it is.",
                    "Notatfilen " + NoteStore.FileName + " kunne ikke leses, så notater er slått av inntil videre. Filen er latt være som den er.")
                : T("No notes yet. Click a day in the calendar and then ✎ to write one (or double-click the day). Notes are kept " + KeptFor() + ".",
                    "Ingen notater ennå. Klikk på en dag i kalenderen og deretter ✎ for å skrive et (eller dobbeltklikk på dagen). Notater tas vare på " + KeptFor() + ".");
            notesEmpty.Visibility = all == 0 ? Visibility.Visible : Visibility.Collapsed;
            notesListBorder.Visibility = all > 0 ? Visibility.Visible : Visibility.Collapsed;
            notesTools.Visibility = notesDeleteAll.Visibility = notesExport.Visibility = all > 0 ? Visibility.Visible : Visibility.Collapsed;
            // Backup and Dropbox also when there are no notes: that is when restoring or syncing them in is needed
            notesBackup.Visibility = notesDropbox.Visibility = notesKeep.Visibility = here ? Visibility.Visible : Visibility.Collapsed;

            bool active = f.Colors.Count > 0;
            Tool(notesFilterBtn, active);
            notesFilterCount.Text = active ? f.Colors.Count.ToString(CultureInfo.InvariantCulture) : "";
            notesFilterBtn.ToolTip = T("Filter on colors", "Filtrer på farger");
            notesColorsBtn.ToolTip = T("Name the colors", "Gi fargene navn");
            Tool(notesPicsBtn, f.Pics != "");
            notesPicsSlash.Visibility = f.Pics == "without" ? Visibility.Visible : Visibility.Collapsed;
            notesPicsBtn.ToolTip = f.Pics == "with" ? T("Showing notes with pictures. Click: without pictures", "Viser notater med bilde. Klikk: uten bilde")
                : f.Pics == "without" ? T("Showing notes without pictures. Click: all notes", "Viser notater uten bilde. Klikk: alle notater")
                : T("Filter on pictures. Click: only notes with pictures", "Filtrer på bilder. Klikk: bare notater med bilde");
            Tool(notesCalBtn, f.Cal);
            notesCalBtn.ToolTip = f.Cal
                ? T("The calendar shows only the notes that match the search and filter. Click: show all notes", "Kalenderen viser bare notatene som passer søket og filteret. Klikk: vis alle notatene")
                : T("Show only the notes that match the search and filter in the calendar too", "Vis bare notatene som passer søket og filteret i kalenderen også");
            notesExport.IsEnabled = keys.Count > 0;
            notesExport.ToolTip = T("Save the list as an Excel file", "Lagre listen som Excel-fil");
            notesBackup.ToolTip = T("Backup file", "Sikkerhetskopi");
            notesKeep.ToolTip = T("How long notes are kept", "Hvor lenge notater tas vare på");
            ShowDropboxState();
            if (all == 0) return;

            if (keys.Count == 0)
                notesList.Children.Add(new TextBlock { Text = T("No notes match.", "Ingen notater passer."), FontSize = 11.5, Foreground = Brush("#666666"), Margin = new Thickness(4, 8, 0, 0) });

            // By month, with a heading for each. A repeating note shows once in each month it falls in, with its days
            // there on one line. Series without an end are shown only up to the last month with an ordinary note, and
            // at least 3 months ahead, so the list never runs on forever. The window is set from all notes, not only
            // those that match the search, so it stays the same while searching.
            DateTime today = DateTime.Today;
            string lo = Keys.Key(new DateTime(today.Year, today.Month, 1));
            foreach (string k in notes.Index.Keys)
            {
                string d0 = Keys.DayOf(k).Substring(0, 8) + "01";
                if (Keys.Compare(d0, lo) < 0) lo = d0;
            }
            string oldest = NotesEngine.OldestKept(noteStore.KeepWeeks);
            if (Keys.Compare(lo, oldest) < 0) lo = oldest;
            string hi = Keys.Key(new DateTime(today.Year, today.Month, 1).AddMonths(4).AddDays(-1));
            foreach (Note n in notes.Index.Values)
            {
                if (n.Repeat != null) continue;
                DateTime e = Keys.Parse(n.End != "" ? n.End : Keys.DayOf(n.Date));
                string m = Keys.Key(new DateTime(e.Year, e.Month, 1).AddMonths(1).AddDays(-1));
                if (Keys.Compare(m, hi) > 0) hi = m;
            }

            var entries = new List<(string K, string Day, string Sort, List<string> Dates)>();
            foreach (string k in keys)
            {
                Note n = notes.Index[k];
                if (n.Repeat == null) { entries.Add((k, Keys.DayOf(k), k, null)); continue; }
                var byMonth = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (string o in notes.Occurrences(k, n.Repeat, lo, hi))
                {
                    if (notes.IsException(k, o)) continue;   // a day the series has a changed note of its own for
                    string m = o.Substring(0, 7);
                    if (!byMonth.TryGetValue(m, out List<string> list)) byMonth[m] = list = new List<string>();
                    list.Add(o);
                }
                // A series that starts after the window still shows, in its first month
                if (byMonth.Count == 0)
                {
                    var first = notes.Occurrences(k, n.Repeat, Keys.DayOf(k), Keys.AddDays(Keys.DayOf(k), 400)).Where(o => !notes.IsException(k, o)).ToList();
                    if (first.Count > 0) byMonth[first[0].Substring(0, 7)] = first.Where(o => o.Substring(0, 7) == first[0].Substring(0, 7)).ToList();
                }
                foreach (var p in byMonth) entries.Add((k, p.Value[0], p.Value[0] + "~" + k, p.Value));
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.Sort, b.Sort));

            string month = "";
            foreach (var en in entries)
            {
                if (en.Day.Substring(0, 7) != month)
                {
                    month = en.Day.Substring(0, 7);
                    string title = Keys.Parse(month + "-01").ToString("MMMM yyyy", DateCulture);
                    notesList.Children.Add(new TextBlock
                    {
                        Text = char.ToUpper(title[0], DateCulture) + title.Substring(1),
                        FontWeight = FontWeights.Bold, FontSize = 12, Foreground = Brush(Blue), Margin = new Thickness(2, 8, 0, 2)
                    });
                }
                notesList.Children.Add(NoteRow(en.K, en.Day, en.Dates));
            }
        }

        private static void Tool(Border b, bool on)
        {
            b.Background = on ? ToolActive : Brushes.White;
            b.BorderBrush = on ? Brush(Blue) : ToolBorder;
        }

        private UIElement NoteRow(string k, string day, List<string> dates)
        {
            Note n = notes.Index[k];
            var row = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IsMaximized ? 150 : 118) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            var line = new Border { BorderBrush = Brush("#eeeeee"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 3, 0, 3), Child = row };

            Border dot = NumberDot(n.Color);
            dot.ToolTip = ColorLabel(n.Color);
            dot.VerticalAlignment = VerticalAlignment.Top;
            dot.Margin = new Thickness(2, 1, 0, 0);
            row.Children.Add(dot);

            var dateCell = new TextBlock { FontSize = 11.5, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 6, 0) };
            if (dates != null)
            {
                dateCell.Text = "↻ " + DayTitle(Keys.Parse(dates[0]), true);
                dateCell.ToolTip = notes.Summary(k, n.Repeat);
            }
            else
            {
                dateCell.Text = DayTitle(Keys.Parse(k), true);
                if (n.End != "")
                {
                    DateTime e = Keys.Parse(n.End);
                    dateCell.Inlines.Add(new Run(" → " + e.Day + "." + e.Month + ".") { Foreground = Brush("#666666") });
                }
            }
            Grid.SetColumn(dateCell, 1);
            row.Children.Add(dateCell);

            var body = new StackPanel();
            string all = System.Text.RegularExpressions.Regex.Replace((n.Text ?? "").Replace("\r", "").TrimEnd(), @"^\s*\n", "");   // the whole note, every line
            if (all == "" && n.Images.Count > 0) all = T("(picture)", "(bilde)");
            body.Children.Add(new TextBlock { Text = all, FontSize = 11.5, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap });
            // A repeating note: its days in this month (a count when there are many)
            if (dates != null)
            {
                body.Children.Add(new TextBlock
                {
                    Text = dates.Count > 8 ? notes.Summary(k, n.Repeat) + " (" + dates.Count + " " + T("days", "dager") + ")" : string.Join(", ", dates.Select(x => Keys.Parse(x).Day + ".")),
                    FontSize = 10.5, Foreground = Brush("#666666"), TextWrapping = TextWrapping.Wrap
                });
            }
            // Small pictures under the text, all the same size, only when the list fills the screen
            if (n.Images.Count > 0 && IsMaximized)
            {
                var thumbs = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
                foreach (string p in n.Images)
                {
                    var img = noteStore.HasPicture(p) ? Load(System.IO.Path.Combine(noteStore.PicturesFolder, p), 120) : null;
                    if (img != null) thumbs.Children.Add(new Image { Source = img, Height = 60, Margin = new Thickness(0, 0, 4, 0), Stretch = Stretch.Uniform });
                }
                if (thumbs.Children.Count > 0) body.Children.Add(thumbs);
            }
            Grid.SetColumn(body, 2);
            row.Children.Add(body);

            // Checklist and pictures in their own column, so the bin is always in the same place at the right
            var meta = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(6, 0, 4, 0) };
            var lines = (n.Text ?? "").Split('\n');
            int tasks = lines.Count(x => x.StartsWith("☐", StringComparison.Ordinal) || x.StartsWith("☑", StringComparison.Ordinal));
            if (tasks > 0) meta.Children.Add(new TextBlock { Text = "☑ " + lines.Count(x => x.StartsWith("☑", StringComparison.Ordinal)) + "/" + tasks, FontSize = 10.5, Foreground = Brush("#555555"), Margin = new Thickness(0, 0, 6, 0) });
            if (n.Images.Count > 0) meta.Children.Add(new TextBlock { Text = "🖼" + (n.Images.Count > 1 ? n.Images.Count.ToString(CultureInfo.InvariantCulture) : ""), FontSize = 10.5, Foreground = Brush("#555555") });
            Grid.SetColumn(meta, 3);
            row.Children.Add(meta);

            var rm = new Button
            {
                Content = BinIcon(14), Width = 24, Height = 22, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand, ToolTip = T("Delete note", "Slett notat"), VerticalAlignment = VerticalAlignment.Top, Focusable = false
            };
            rm.Click += (s, e) =>
            {
                e.Handled = true;
                Action undo = Snapshot(new[] { k });
                noteStore.Delete(k);
                NotesChanged();
                ShowUndo(T("Note deleted.", "Notatet er slettet."), undo);
            };
            Grid.SetColumn(rm, 4);
            row.Children.Add(rm);

            line.MouseEnter += (s, e) => line.Background = RowHover;
            line.MouseLeave += (s, e) => line.Background = Brushes.Transparent;
            line.MouseLeftButtonUp += (s, e) =>
            {
                if (IsInside(e.OriginalSource as DependencyObject, rm)) return;
                ShowNoteInCalendar(day, k);   // a series: its first day in this month
            };
            return line;
        }

        /// <summary>Goes to the day in the calendar and opens the note there.</summary>
        private void ShowNoteInCalendar(string day, string k)
        {
            CloseNotesMenu();
            DateTime d = Keys.Parse(day);
            if (IsMaximized)
            {
                activeYear = d.Year;
                ShowTab(yearContent);
                UpdateYearView();
            }
            else
            {
                activeMonth = FirstOfMonth(d);
                ShowTab(tab1Content);
                UpdateGrids();
            }
            UpdateLayout();
            DayCell cell = FindCell(day);
            if (cell != null) OpenEditor(d, cell.Tip, false, k);
        }

        // ---------- The small menus (color filter, color names, backup, keep) ----------

        private void ToggleNotesMenu(FrameworkElement button, Action<StackPanel> fill, bool above)
        {
            bool same = notesMenuButton == button;
            CloseNotesMenu();
            if (same) return;
            var panel = new StackPanel();
            fill(panel);
            notesMenu = new Border
            {
                Background = Brushes.White, BorderBrush = Brush("#aaaaaa"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6), Child = panel,
                Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.25 }
            };
            notesMenuButton = button;
            notesMenuLayer.Children.Add(notesMenu);
            notesMenu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Point p = button.TranslatePoint(new Point(0, 0), notesMenuLayer);
            double w = notesMenu.DesiredSize.Width, h = notesMenu.DesiredSize.Height;
            double left = Math.Max(4, Math.Min(p.X, notesMenuLayer.ActualWidth - w - 4));
            double top = above ? p.Y - h - 3 : p.Y + button.ActualHeight + 3;
            Canvas.SetLeft(notesMenu, left);
            Canvas.SetTop(notesMenu, Math.Max(2, top));
            if (panel.Tag is UIElement focus) Dispatcher.BeginInvoke(new Action(() => focus.Focus()), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void CloseNotesMenu()
        {
            if (notesMenu == null) return;
            bool names = notesMenuButton == notesColorsBtn;
            notesMenuLayer.Children.Remove(notesMenu);
            notesMenu = null;
            notesMenuButton = null;
            if (names) FillNotesList();   // the new color names in the list
        }

        /// <summary>A click outside the open menu (and its button) closes it.</summary>
        private void CloseNotesMenusOutside(DependencyObject source)
        {
            if (notesMenu == null || IsInside(source, notesMenu) || IsInside(source, notesMenuButton)) return;
            CloseNotesMenu();
        }

        private Button MenuRow(UIElement icon, string label, bool on, Action click, string sub = null)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (icon != null) content.Children.Add(new Border { Width = 20, Child = icon, Margin = new Thickness(0, 0, 6, 0) });
            var text = new TextBlock { Text = label, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
            if (sub != null) text.Inlines.Add(new Run(" " + sub) { Foreground = Brush("#777777"), FontSize = 10.5 });
            content.Children.Add(text);
            var b = new Button
            {
                Content = content, HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 3, 10, 3), Cursor = Cursors.Hand, IsEnabled = on, Focusable = false
            };
            b.Click += (s, e) => click();
            return b;
        }

        private void FillFilterMenu(StackPanel m)
        {
            NotesFilter f = notes.Filter;
            void Row(string label, string colorId, bool on, Action toggle)
            {
                var icon = new StackPanel { Orientation = Orientation.Horizontal };
                icon.Children.Add(new TextBlock { Text = on ? "✓" : "", Width = 14, FontSize = 11.5, Foreground = Brush(Blue), FontWeight = FontWeights.Bold });
                if (colorId != null) icon.Children.Add(NumberDot(colorId));
                var b = MenuRow(null, "", true, () => { toggle(); RefreshMenu(m, FillFilterMenu); RefreshNotesList(); });
                var c = (StackPanel)b.Content;
                c.Children.Insert(0, icon);
                ((TextBlock)c.Children[1]).Text = label;
                ((TextBlock)c.Children[1]).Margin = new Thickness(6, 0, 0, 0);
                m.Children.Add(b);
            }
            Row(T("All colors", "Alle farger"), null, f.Colors.Count == 0, () => f.Colors.Clear());
            foreach (string id in NoteColors.All.Select(c => c.Id).Concat(new[] { "" }))
            {
                bool on = f.Colors.Contains(id);
                Row(ColorLabel(id), id, on, () => { if (on) f.Colors.Remove(id); else f.Colors.Add(id); });
            }
        }

        private static void RefreshMenu(StackPanel m, Action<StackPanel> fill)
        {
            m.Children.Clear();
            fill(m);
        }

        private void FillColorsMenu(StackPanel m)
        {
            m.Children.Add(new TextBlock { Text = T("Name the colors", "Gi fargene navn"), FontWeight = FontWeights.Bold, FontSize = 11.5, Margin = new Thickness(2, 0, 0, 4) });
            foreach (string id in NoteColors.IdsWithNone)
            {
                string key = id == "" ? "none" : id;
                var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                r.Children.Add(NumberDot(id, 16));
                var box = new TextBox { Width = 170, MaxLength = 40, FontSize = 11.5, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(2, 1, 2, 1) };
                box.Text = noteStore.ColorNames.TryGetValue(key, out string name) ? name : "";
                SetPlaceholder(box, ColorDefault(id));
                box.TextChanged += (s, e) =>
                {
                    if (box.Text.Trim() != "") noteStore.ColorNames[key] = box.Text; else noteStore.ColorNames.Remove(key);
                    noteStore.SaveNames();
                };
                box.KeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Escape) { CloseNotesMenu(); e.Handled = true; } };
                if (m.Tag == null) m.Tag = box;
                r.Children.Add(box);
                m.Children.Add(r);
            }
        }

        private void FillBackupMenu(StackPanel m)
        {
            string last = noteStore.LastBackup;
            string sub = Keys.IsDayKey(last) ? "(" + T("last ", "sist ") + Keys.Parse(last).Day + "." + Keys.Parse(last).Month + ".)" : null;
            m.Children.Add(MenuRow(BackupToFileIcon(18), T("Back up to a file", "Ta sikkerhetskopi"), notes.Index.Count > 0, () => { CloseNotesMenu(); BackUpNotes(); }, sub));
            m.Children.Add(MenuRow(RestoreIcon(18), T("Restore from a backup", "Gjenopprett fra sikkerhetskopi"), noteStore.Available, () => { CloseNotesMenu(); RestoreNotes(); }));
        }

        private void FillKeepMenu(StackPanel m)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new TextBlock { Text = T("Keep notes", "Ta vare på notater"), FontWeight = FontWeights.Bold, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var input = new TextBox { Width = 44, FontSize = 11.5, Text = noteStore.KeepWeeks.ToString(CultureInfo.InvariantCulture), Padding = new Thickness(2, 1, 2, 1), VerticalContentAlignment = VerticalAlignment.Center };
            input.PreviewTextInput += (s, e) => e.Handled = !e.Text.All(char.IsDigit);
            line.Children.Add(input);
            line.Children.Add(new TextBlock { Text = T(" weeks back", " uker bakover"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
            var ok = new Button { Content = T("Save", "Lagre"), FontSize = 11, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 1, 10, 1), Style = (Style)FindResource("CustomButtonStyle") };
            line.Children.Add(ok);
            m.Children.Add(line);
            var info = new StackPanel { MaxWidth = 330, Margin = new Thickness(0, 6, 0, 0) };
            m.Children.Add(info);
            m.Tag = input;

            int Weeks() => input.Text.Trim() == "" || !int.TryParse(input.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
                ? noteStore.KeepWeeks : NotesEngine.CleanWeeks(w);
            void ShowInfo()
            {
                DateTime from = Keys.Parse(NotesEngine.OldestKept(Weeks()));
                string fromText = from.ToString(settingsManager.UseNorwegianTexts ? "ddd d. MMM" : "ddd d MMM", DateCulture).Replace("..", ".");
                info.Children.Clear();
                info.Children.Add(new TextBlock { Text = T("Notes older than " + fromText + " will be deleted.", "Notater eldre enn " + fromText + " vil slettes."), FontWeight = FontWeights.SemiBold, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 3) });
                foreach (string s in new[]
                {
                    T("Weeks are counted from Monday of this week.", "Ukene telles fra mandag inneværende uke."),
                    T("0 deletes all notes older than this week, 1 keeps notes up to and including last week, and so on.",
                      "0 vil slette alle notater eldre enn inneværende uke, 1 vil ta vare på notater til og med forrige uke, og så videre."),
                    T("Notes ahead in time and repeating notes still running are never deleted.", "Notater frem i tid og gjentakelser som pågår, slettes aldri."),
                    T("Standard: 12 weeks.", "Standard: 12 uker.")
                })
                    info.Children.Add(new TextBlock { Text = s, FontSize = 10.5, Foreground = Brush("#444444"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) });
            }
            void Apply()
            {
                int w = Weeks();
                if (w == noteStore.KeepWeeks) { CloseNotesMenu(); return; }
                string limit = NotesEngine.OldestKept(w);
                int gone = notes.Index.Values.Count(n => NotesEngine.Expired(n.Date, n.EndDate, n.Repeat, limit));
                if (gone > 0)
                {
                    string q = T(gone + (gone == 1 ? " note is" : " notes are") + " older than that and will be deleted now. This cannot be undone.",
                                 gone + (gone == 1 ? " notat er" : " notater er") + " eldre enn det og slettes nå. Dette kan ikke angres.") +
                               (noteSync.State != SyncState.Off ? T(" It also applies on your other devices that sync with Dropbox.", " Det gjelder også på de andre enhetene dine som synkroniserer med Dropbox.") : "");
                    if (MessageBox.Show(this, q, "QuickCal", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
                }
                noteStore.SaveKeep(w);
                CloseNotesMenu();
                RemoveOldNotes();
                FillNotesList();
            }
            input.TextChanged += (s, e) => ShowInfo();
            input.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { Apply(); e.Handled = true; }
                else if (e.Key == Key.Escape) { CloseNotesMenu(); e.Handled = true; }
            };
            ok.Click += (s, e) => Apply();
            ShowInfo();
        }

        // ---------- Excel, backup, restore, delete all ----------

        /// <summary>The list as shown (search and color filter) as an Excel file, with the pictures in their own cells.</summary>
        private void ExportNotes()
        {
            List<string> keys = notes.FilteredKeys();
            if (keys.Count == 0) return;
            var dialog = new SaveFileDialog
            {
                FileName = "QuickCal-" + T("notes", "notater") + "-" + Keys.Key(DateTime.Today) + ".xlsx",
                Filter = "Excel (*.xlsx)|*.xlsx", DefaultExt = ".xlsx", AddExtension = true
            };
            if (dialog.ShowDialog(this) != true) return;

            int maxPics = keys.Max(k => notes.Index[k].Images.Count);
            var head = new List<object> { T("Date", "Dato"), T("To", "Til"), T("Day", "Dag"), T("Week", "Uke"), T("No.", "Nr"), T("Color", "Farge"), T("Note", "Notat"), T("Repeats", "Gjentas") };
            for (int i = 0; i < maxPics; i++) head.Add(T("Picture ", "Bilde ") + (i + 1));
            var rows = new List<IList<object>> { head };
            var images = new List<XlsxWriter.Picture>();
            var rowHeights = new Dictionary<int, double>();
            for (int r = 0; r < keys.Count; r++)
            {
                string k = keys[r];
                Note n = notes.Index[k];
                DateTime d = Keys.Parse(k);
                string weekday = d.ToString("dddd", DateCulture);
                var row = new List<object>
                {
                    d, n.End != "" ? (object)Keys.Parse(n.End) : "", char.ToUpper(weekday[0], DateCulture) + weekday.Substring(1),
                    ISOWeek.GetWeekOfYear(d), NoteColors.Number(n.Color), ColorLabel(n.Color), (n.Text ?? "").Trim(), notes.Summary(k, n.Repeat)
                };
                for (int j = 0; j < maxPics; j++) row.Add("");
                rows.Add(row);
                for (int j = 0; j < n.Images.Count; j++)
                {
                    byte[] bytes = noteStore.ReadPicture(n.Images[j]);
                    var bmp = bytes != null ? Decode(bytes) : null;
                    if (bmp == null) continue;
                    rowHeights[r + 1] = 90;
                    images.Add(new XlsxWriter.Picture
                    {
                        Row = r + 1, Col = 8 + j, W = bmp.PixelWidth, H = bmp.PixelHeight, Data = bytes,
                        Png = n.Images[j].EndsWith(".png", StringComparison.Ordinal), Name = k + " " + (j + 1)
                    });
                }
            }
            var widths = new List<double> { 10, 10, 10, 6, 5, 14, 50, 24 };
            for (int i = 0; i < maxPics; i++) widths.Add(22);
            try
            {
                using var file = File.Create(dialog.FileName);
                XlsxWriter.Write(file, rows, T("Notes", "Notater"), widths, new[] { 6, 7 }, images, rowHeights);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, T("Could not save the file: ", "Fikk ikke lagret filen: ") + ex.Message, "QuickCal", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BackUpNotes()
        {
            var dialog = new SaveFileDialog
            {
                FileName = "QuickCal-" + T("notes-backup", "notater-sikkerhetskopi") + "-" + Keys.Key(DateTime.Today) + ".json",
                Filter = "QuickCal (*.json)|*.json", DefaultExt = ".json", AddExtension = true
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                NotesBackup.Write(noteStore, dialog.FileName);
                noteStore.LastBackup = Keys.Key(DateTime.Today);
                noteStore.Save();
                ShowBackupHint();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, T("Could not save the file: ", "Fikk ikke lagret filen: ") + ex.Message, "QuickCal", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>Notes in the file replace notes that start on the same day; all other notes are kept.</summary>
        private void RestoreNotes()
        {
            var dialog = new OpenFileDialog { Filter = "QuickCal (*.json)|*.json|" + T("All files", "Alle filer") + " (*.*)|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            NotesBackup.Contents data = NotesBackup.Read(dialog.FileName);
            if (data == null)
            {
                MessageBox.Show(this, T("This file is not a QuickCal notes backup.", "Denne filen er ikke en sikkerhetskopi av QuickCal-notater."), "QuickCal", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string limit = NotesEngine.OldestKept(noteStore.KeepWeeks);
            var keep = data.Notes.Where(x => !NotesEngine.Expired(x.Note.Date, x.Note.EndDate, x.Note.Repeat, limit)).ToList();
            int old = data.Notes.Count - keep.Count;
            if (keep.Count == 0)
            {
                MessageBox.Show(this, old > 0
                    ? T("All notes in the backup are older than " + KeepText() + " back, so none were restored.", "Alle notatene i sikkerhetskopien er eldre enn " + KeepText() + " bakover, så ingen ble gjenopprettet.")
                    : T("The backup has no notes.", "Sikkerhetskopien har ingen notater."), "QuickCal", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string when = data.Created.HasValue ? data.Created.Value.ToLocalTime().ToString("d", DateCulture) : null;
            if (MessageBox.Show(this,
                    T("Restore " + keep.Count + " notes from the backup" + (when != null ? " of " + when : "") + "?\n\nNotes on the same day are replaced, all other notes are kept.",
                      "Gjenopprette " + keep.Count + " notater fra sikkerhetskopien" + (when != null ? " fra " + when : "") + "?\n\nNotater på samme dag blir erstattet, alle andre notater beholdes."),
                    "QuickCal", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

            long now = Keys.Now();
            var restored = new List<Note>();
            foreach (var (note, pictures) in keep)
            {
                note.Images = pictures.Select(p => noteStore.AddPicture(p.Bytes, p.Png)).ToList();
                note.Updated = now;
                restored.Add(note);
            }
            noteStore.PutMany(restored);
            if (data.ColorNames.Count > 0)
            {
                foreach (var p in data.ColorNames) noteStore.ColorNames[p.Key] = p.Value;
                noteStore.SaveNames();
            }
            NotesChanged();
            MessageBox.Show(this, T(keep.Count + " notes restored.", keep.Count + " notater er gjenopprettet.") +
                (old > 0 ? " " + T(old + " older than " + KeepText() + " back were left out.", old + " eldre enn " + KeepText() + " bakover ble utelatt.") : ""),
                "QuickCal", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DeleteAllNotes()
        {
            if (MessageBox.Show(this, T("Delete all notes?", "Slette alle notater?"), "QuickCal", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            var keys = notes.Index.Keys.ToList();
            if (keys.Count == 0) return;
            Action undo = Snapshot(keys);
            noteStore.DeleteMany(keys);
            NotesChanged();
            ShowUndo(T("All notes deleted.", "Alle notatene er slettet."), undo);
        }

        /// <summary>A small line next to the buttons: synced with Dropbox, or a reminder that notes are stored only here.</summary>
        private void ShowBackupHint()
        {
            notesBackupHint.Inlines.Clear();
            if (!NotesOn) return;
            void Text(string t) => notesBackupHint.Inlines.Add(new Run(t));
            void Pic(UIElement icon) => notesBackupHint.Inlines.Add(new InlineUIContainer(icon) { BaselineAlignment = BaselineAlignment.Center });
            SyncState state = noteSync.State;
            if (state == SyncState.Lost)
            {
                Text(T("No longer connected to Dropbox", "Ikke lenger koblet til Dropbox") + " ");
                Pic(DropboxIcon(11));
                Text(". " + T("The notes are safe here.", "Notatene er trygge her."));
                return;
            }
            if (state != SyncState.Off)
            {
                long synced = noteSync.LastSynced;
                Text(T("Synced with Dropbox", "Synkes med Dropbox") + " ");
                Pic(DropboxIcon(11));
                Text(state == SyncState.Busy ? " …"
                    : state == SyncState.Offline ? " " + T("(waiting for the network)", "(venter på nett)")
                    : state == SyncState.Error ? " " + T("(not right now: ", "(ikke akkurat nå: ") + noteSync.Error + ")"
                    : synced > 0 ? " (" + T("last ", "sist ") + DateTimeOffset.FromUnixTimeMilliseconds(synced).LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) + ")" : "");
                return;
            }
            if (notes.Index.Count == 0) return;
            Text(T("Notes are only stored on this PC. Sync with", "Notatene lagres bare på denne PC-en. Synk med") + " ");
            Pic(DropboxIcon(11));
            Text(" " + T("or back up with", "eller ta sikkerhetskopi med") + " ");
            Pic(BackupIcon(11));
        }

        // =====================================================================================
        // Dropbox page
        // =====================================================================================

        private void SetUpDropboxPage()
        {
            dropboxConnect.Click += (s, e) =>
            {
                try
                {
                    noteSync.Dropbox.StartConnect();
                }
                catch (Exception)
                {
                    MessageBox.Show(this, T("Could not open the browser.", "Fikk ikke åpnet nettleseren."), "QuickCal", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                dropboxCode.Text = "";
                FillDropboxPage();
                dropboxCode.Focus();
            };
            dropboxCodeOk.Click += (s, e) => FinishDropboxConnect();
            dropboxCode.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { FinishDropboxConnect(); e.Handled = true; }
                else if (e.Key == Key.Escape) { noteSync.Dropbox.CancelConnect(); FillDropboxPage(); e.Handled = true; }
            };
            dropboxSyncNow.Click += (s, e) => _ = noteSync.SyncNow();
            dropboxDisconnect.Click += async (s, e) =>
            {
                string folder = DropboxClient.Folder;
                if (MessageBox.Show(this,
                        T("Disconnect from Dropbox?\n\nSyncing stops on this PC. The notes stay both here and in Dropbox, in the folder " + folder + ". To remove them from Dropbox too, delete that folder in Dropbox.",
                          "Koble fra Dropbox?\n\nSynkingen stopper på denne PC-en. Notatene blir liggende både her og i Dropbox, i mappen " + folder + ". Vil du fjerne dem fra Dropbox også, sletter du den mappen i Dropbox."),
                        "QuickCal", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                dropboxDisconnect.IsEnabled = false;
                try { await noteSync.Dropbox.Disconnect(); }
                catch (Exception) { /* forgotten here anyway */ }
                dropboxDisconnect.IsEnabled = true;
                ShowDropboxState();
            };
        }

        private async void FinishDropboxConnect()
        {
            string code = dropboxCode.Text.Trim();
            if (code == "") { dropboxCode.Focus(); return; }
            dropboxCodeOk.IsEnabled = false;
            try
            {
                await noteSync.Dropbox.FinishConnect(code);
                dropboxCode.Text = "";
                noteSync.Start();
                _ = noteSync.SyncNow();
            }
            catch (Exception ex)
            {
                bool offline = ex is DropboxException de && de.Offline;
                MessageBox.Show(this, offline
                    ? T("No network right now. Try again when the PC is online.", "Ikke nett akkurat nå. Prøv igjen når PC-en er på nett.")
                    : T("Dropbox did not accept the code. Click “Connect to Dropbox” to get a new one, and paste all of it.",
                        "Dropbox godtok ikke koden. Trykk «Koble til Dropbox» for å få en ny, og lim inn hele koden."),
                    "QuickCal", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            dropboxCodeOk.IsEnabled = true;
            ShowDropboxState();
        }

        private void OpenDropboxPage()
        {
            CloseNotesMenu();
            ShowTab(tabDropbox);
            FillDropboxPage();
        }

        private void DropboxBack_Click(object sender, RoutedEventArgs e)
        {
            if (noteSync.Dropbox.WaitingForCode && !noteSync.Dropbox.Connected) noteSync.Dropbox.CancelConnect();
            OpenNotesPage();
        }

        /// <summary>
        /// A small dot on the Dropbox button: green when synced, blue while syncing, yellow while offline (it syncs when
        /// the network is back), red when the last sync failed or Dropbox no longer accepts the sign-in.
        /// </summary>
        private void ShowDropboxState()
        {
            if (noteSync == null || dropboxDot == null) return;
            SyncState state = noteSync.State;
            string hex = state == SyncState.Ok ? "#2e9e5b" : state == SyncState.Busy ? "#1a73e8" : state == SyncState.Offline ? "#e0a800"
                : state == SyncState.Error || state == SyncState.Lost ? "#d93025" : null;
            dropboxDot.Visibility = hex != null ? Visibility.Visible : Visibility.Collapsed;
            if (hex != null) dropboxDot.Fill = Brush(hex);
            notesDropbox.ToolTip = state == SyncState.Off ? T("Sync notes with Dropbox", "Synk notater med Dropbox")
                : state == SyncState.Lost ? T("No longer connected to Dropbox", "Ikke lenger koblet til Dropbox")
                : state == SyncState.Offline ? T("Synced with Dropbox: waiting for the network", "Synkes med Dropbox: venter på nett")
                : T("Synced with Dropbox", "Synkes med Dropbox");
            ShowBackupHint();
            if (tabDropbox.Visibility == Visibility.Visible) FillDropboxPage();
        }

        private void FillDropboxPage()
        {
            DropboxClient dbx = noteSync.Dropbox;
            bool on = dbx.Connected, waiting = !on && dbx.WaitingForCode;
            SyncState state = noteSync.State;
            string folder = DropboxClient.Folder;
            dropboxTitle.Text = T("Sync notes with Dropbox", "Synk notater med Dropbox");
            dropboxPoints.Children.Clear();
            void Point(string bold, string text)
            {
                var t = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Brushes.Black, Margin = new Thickness(0, 2, 0, 0) };
                t.Inlines.Add(new Run("•  "));
                t.Inlines.Add(new Bold(new Run(bold + " ")));
                t.Inlines.Add(new Run(text));
                dropboxPoints.Children.Add(t);
            }
            if (waiting)
            {
                dropboxIntro.Text = T("Dropbox opened in your browser. Sign in there and click Allow. Dropbox then shows a code: copy it and paste it here.",
                                      "Dropbox er åpnet i nettleseren. Logg inn der og trykk Tillat (Allow). Dropbox viser så en kode: kopier den og lim den inn her.");
                Point(T("No browser window?", "Ser du ikke nettleseren?"), T("Click “Open Dropbox again”.", "Trykk «Åpne Dropbox igjen»."));
            }
            else if (!on && state == SyncState.Lost)
            {
                dropboxIntro.Text = T("QuickCal is no longer connected to Dropbox. The Dropbox account may be deleted, or QuickCal's access was removed in Dropbox.",
                                      "QuickCal er ikke lenger koblet til Dropbox. Dropbox-kontoen kan være slettet, eller tilgangen for QuickCal er fjernet i Dropbox.");
                Point(T("Your notes are safe.", "Notatene dine er trygge."), T("They are still here on this PC, and nothing was deleted.", "De ligger fortsatt her på denne PC-en, og ingenting er slettet."));
                Point(T("Connect again", "Koble til igjen"), T("to go on syncing, with the same or another Dropbox account.", "for å fortsette synkingen, med samme eller en annen Dropbox-konto."));
            }
            else if (!on)
            {
                dropboxIntro.Text = T("Use QuickCal on your PC and your phone? Dropbox keeps the notes the same on all of them, and keeps a safe copy if a device is lost. No backups by hand.",
                                      "Bruker du QuickCal på PC og mobil? Dropbox holder notatene like på alle enhetene, og tar vare på en sikker kopi hvis en enhet blir borte. Du slipper å ta sikkerhetskopi selv.");
                Point(T("Free.", "Gratis."), T("No Dropbox account? You make a free one (Dropbox Basic) when you connect.", "Har du ikke Dropbox, lager du en gratis konto (Dropbox Basic) når du kobler til."));
                Point(T("Private.", "Privat."), T("The notes are kept in your own Dropbox, in the folder " + folder + ". QuickCal sees nothing else there, and has no server of its own.",
                                                  "Notatene lagres i din egen Dropbox, i mappen " + folder + ". QuickCal ser ingenting annet der og har ingen egen server."));
                Point(T("Once.", "Én gang."), T("Sign in once on each device. After that it syncs by itself.", "Du logger inn én gang på hver enhet. Etterpå synker det av seg selv."));
            }
            else
            {
                string name = dbx.AccountName, email = dbx.AccountEmail;
                dropboxIntro.Text = T("Connected to Dropbox", "Koblet til Dropbox") + (name != "" ? " " + T("as", "som") + " " + name + (email != "" ? " (" + email + ")" : "") : "") + ". " +
                                    T("Notes sync by themselves on every device that is connected.", "Notatene synkes av seg selv på alle enheter som er koblet til.");
                long last = noteSync.LastSynced;
                DateTime lastTime = DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0, last)).LocalDateTime;
                Point(T("Status:", "Status:"), state == SyncState.Busy ? T("syncing …", "synker …")
                    : state == SyncState.Offline ? T("no network right now. Changes are kept here and synced when the network is back.", "ikke nett akkurat nå. Endringer tas vare på her og synkes når nettet er tilbake.")
                    : state == SyncState.Error ? T("could not sync just now. QuickCal tries again by itself.", "fikk ikke synket akkurat nå. QuickCal prøver igjen av seg selv.") + (noteSync.Error != "" ? " (" + noteSync.Error + ")" : "")
                    : last > 0 ? T("last synced ", "sist synket ") + DayTitle(lastTime, true) + " " + lastTime.ToString("HH:mm", CultureInfo.InvariantCulture) + "."
                    : T("not synced yet.", "ikke synket ennå."));
                Point(T("Stored in:", "Lagres i:"), T("the folder " + folder + " in your Dropbox.", "mappen " + folder + " i din Dropbox."));
                Point(T("Other devices:", "Andre enheter:"), T("open QuickCal there and connect to the same Dropbox account.", "åpne QuickCal der og koble til den samme Dropbox-kontoen."));
            }
            dropboxCodeRow.Visibility = waiting ? Visibility.Visible : Visibility.Collapsed;
            dropboxCodeLabel.Text = T("Code from Dropbox:", "Kode fra Dropbox:");
            dropboxConnect.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            dropboxConnect.Content = waiting ? T("Open Dropbox again", "Åpne Dropbox igjen") : T("Connect to Dropbox", "Koble til Dropbox");
            dropboxSyncNow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            dropboxSyncNow.Content = T("Sync now", "Synk nå");
            dropboxSyncNow.IsEnabled = state != SyncState.Busy;
            dropboxDisconnect.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            dropboxDisconnect.Content = T("Disconnect", "Koble fra");
        }
    }
}
