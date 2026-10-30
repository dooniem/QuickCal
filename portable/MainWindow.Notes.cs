using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using QuickCal.Notes;
using static QuickCal.Notes.NotesUiKit;

namespace QuickCal
{
    /// <summary>
    /// Notes on days, the same as in the web app: click a day for a small box with ✎, double-click to write at once.
    /// A note has text (with checklists), a color, pictures, can run over several days and can repeat. Two notes fit
    /// on a day (top and bottom half). Drag a note to move it, or its edge to make it longer. Notes are kept in
    /// QuickCal.notes.json next to QuickCal.exe, and can sync with QuickCal on other devices through Dropbox.
    /// Kept apart from the calendar: the calendar calls in here through a few small hooks.
    /// </summary>
    public partial class MainWindow
    {
        private enum CellKind { Plain, Today, Holiday, Gold }

        /// <summary>A day in the calendar as drawn now.</summary>
        private sealed class DayCell
        {
            public Border Border;
            public TextBlock Text;
            public DateTime Date;
            public string Key;
            public CellKind Kind;
            public string Tip;   // holiday or birthday text
        }

        private NoteStore noteStore;
        private NotesEngine notes;
        private NotesSync noteSync;
        private readonly Dictionary<Border, DayCell> dayCells = new Dictionary<Border, DayCell>();
        private readonly Dictionary<int, HashSet<DateTime>> offDays = new Dictionary<int, HashSet<DateTime>>();
        private bool notesStarted;

        /// <summary>Notes are turned on in Settings.</summary>
        private bool NotesHere => settingsManager.Values.NotesEnabled && noteStore != null;
        /// <summary>Notes are turned on and could be read.</summary>
        private bool NotesOn => NotesHere && noteStore.Available;

        private static readonly Brush LayerShadowBorder = Brush("#888888");

        private void InitNotes()
        {
            noteStore = new NoteStore();
            noteStore.Load();
            notes = new NotesEngine(T, IsOffDay);
            notes.SetIndex(noteStore.Notes);
            noteSync = new NotesSync(noteStore, T) { Busy = () => editor != null };
            noteSync.NotesChanged += () =>
            {
                notes.Rebuild();
                RerenderCalendar();
                if (tabNotes.Visibility == Visibility.Visible) FillNotesList();
            };
            noteSync.StateChanged += ShowDropboxState;
            noteSync.KeepShortened += () => RemoveOldNotes();

            PreviewMouseLeftButtonDown += Notes_PreviewMouseDown;
            PreviewMouseMove += Notes_PreviewMouseMove;
            PreviewMouseLeftButtonUp += Notes_PreviewMouseUp;
            PreviewKeyUp += Notes_PreviewKeyUp;
            Deactivated += (s, e) => { if (drag != null) EndDrag(); };
            IsVisibleChanged += (s, e) => { if (IsVisible && NotesOn) noteSync.Soon(0); };

            SetUpNotesPage();
            SetUpDropboxPage();
            StartNotes();
        }

        /// <summary>Notes turned on (at start or in Settings): tidy up and start syncing. Off only hides them.</summary>
        private void StartNotes()
        {
            if (!NotesOn || notesStarted) return;
            notesStarted = true;
            noteStore.RemoveUnusedPictures();
            RemoveOldNotes();
            noteSync.Start();
        }

        /// <summary>Notes that ended before the first day kept are deleted (at start, each new day, and when the setting changes).</summary>
        public void RemoveOldNotes()
        {
            if (!NotesOn) return;
            if (noteStore.RemoveOld() == 0) return;
            notes.Rebuild();
            RerenderCalendar();
            if (tabNotes.Visibility == Visibility.Visible) FillNotesList();
        }

        /// <summary>Work days for repeating notes ("4 work days after"): not a weekend and not a red day in the calendar.</summary>
        private bool IsOffDay(DateTime d)
        {
            if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) return true;
            if (!offDays.TryGetValue(d.Year, out HashSet<DateTime> days))
                offDays[d.Year] = days = new HashSet<DateTime>(HolidayCalculator.ForYear(settingsManager, d.Year).Keys);
            return days.Contains(d.Date);
        }

        /// <summary>Settings changed (Easter week, summer weeks): the work days of repeating notes may move.</summary>
        private void NotesSettingsChanged()
        {
            offDays.Clear();
            notes?.Invalidate();
        }

        /// <summary>Draws the calendar again after notes changed (only the calendar pages).</summary>
        private void RerenderCalendar()
        {
            if (IsYearView) UpdateYearView();
            else if (tab1Content.Visibility == Visibility.Visible) UpdateGrids();
        }

        private void NotesChanged()
        {
            notes.Rebuild();
            RerenderCalendar();
            if (tabNotes.Visibility == Visibility.Visible) FillNotesList();
        }

        // =====================================================================================
        // Calendar hooks
        // =====================================================================================

        /// <summary>Called for every day drawn: remember it, and mark it when it has a note.</summary>
        private void RegisterDayCell(Border border, TextBlock text, DateTime date, CellKind kind, string tip)
        {
            var cell = new DayCell { Border = border, Text = text, Date = date, Key = Keys.Key(date), Kind = kind, Tip = tip };
            dayCells[border] = cell;
            if (!NotesHere) return;
            border.Cursor = Cursors.Hand;
            border.ToolTipOpening += DayCell_ToolTipOpening;
            border.MouseLeftButtonDown += DayCell_MouseLeftButtonDown;
            if (NotesOn) DecorateCell(cell);
        }

        /// <summary>Before a month or the year is drawn again.</summary>
        private void ForgetDayCells()
        {
            ClosePeek();
            ClearDropPreview();
            dayCells.Clear();
        }

        private static readonly Brush JoinLine = Brush("#c8c8c8");

        /// <summary>
        /// Marks a day that has a note: its color, light hatch and a small notepad in the top right corner.
        /// A note over several days looks like one piece: a light line between its days in the same week (and
        /// month), and the notepad only on its last day. Two notes on a day: the top half has one, the bottom half
        /// the other, with a thin white line between. A holiday, today or birthday keeps its look: the note's color
        /// shows as a corner mark.
        /// </summary>
        private void DecorateCell(DayCell cell)
        {
            Showing[] pair = notes.ShownAt(cell.Key);
            var shown = pair.Where(a => a != null).ToList();
            if (shown.Count == 0) return;

            Border b = cell.Border;
            if (b.ToolTip == null) b.ToolTip = new ToolTip();
            UIElement content = b.Child;
            b.Child = null;
            var grid = new Grid();
            b.Child = grid;

            bool plain = cell.Kind == CellKind.Plain;
            var hexes = shown.Select(a => NoteColors.Hex(notes.Index[a.K].Color)).ToList();
            bool colored = false, mixed = false;
            if (shown.Count == 2 && plain)
            {
                Color top = hexes[0] != null ? (Color)ColorConverter.ConvertFromString(hexes[0]) : Colors.White;
                Color bottom = hexes[1] != null ? (Color)ColorConverter.ConvertFromString(hexes[1]) : Colors.White;
                var split = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                split.GradientStops.Add(new GradientStop(top, 0));
                split.GradientStops.Add(new GradientStop(top, 0.485));
                split.GradientStops.Add(new GradientStop(Colors.White, 0.485));
                split.GradientStops.Add(new GradientStop(Colors.White, 0.515));
                split.GradientStops.Add(new GradientStop(bottom, 0.515));
                split.GradientStops.Add(new GradientStop(bottom, 1));
                b.Background = split;
                if (hexes[0] != null && hexes[1] != null) colored = true; else mixed = true;
            }
            else
            {
                string hex = hexes[0] ?? (hexes.Count > 1 ? hexes[1] : null);
                if (hex != null && plain)
                {
                    b.Background = Brush(hex);
                    colored = true;
                }
                else
                {
                    string c1 = shown.Count == 2 ? hexes[0] : hex, c2 = shown.Count == 2 ? hexes[1] : null;
                    if (c1 != null) grid.Children.Add(CornerMark(c1, top: true));
                    if (c2 != null) grid.Children.Add(CornerMark(c2, top: false));
                }
            }
            if (colored) cell.Text.Foreground = Brushes.White;
            if (mixed) cell.Text.Foreground = Brush("#111111");

            grid.Children.Add(content);
            grid.Children.Add(new Rectangle { Fill = Hatch(light: colored || !plain), IsHitTestVisible = false });

            // Several days: joined to the days beside it in the same week and month
            bool noIcon = false;
            if (shown.Any(a => a.E != a.S))
            {
                DateTime date = cell.Date;
                bool lastOfMonth = date.Day == DateTime.DaysInMonth(date.Year, date.Month);
                bool goesOn = shown.All(a => cell.Key != a.E), cameFrom = shown.All(a => cell.Key != a.S);
                if (plain)
                {
                    Thickness t = b.BorderThickness;
                    if (cameFrom && date.DayOfWeek != DayOfWeek.Monday && date.Day != 1)
                    {
                        t.Left = 0;
                        grid.Children.Add(new Rectangle { Width = 1, Fill = JoinLine, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false });
                    }
                    if (goesOn && date.DayOfWeek != DayOfWeek.Sunday && !lastOfMonth)
                    {
                        t.Right = 0;
                        grid.Children.Add(new Rectangle { Width = 1, Fill = JoinLine, HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false });
                    }
                    b.BorderThickness = t;
                }
                noIcon = goesOn && !lastOfMonth;
            }
            if (!noIcon)
            {
                var corner = new Grid { IsHitTestVisible = false };
                corner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68, GridUnitType.Star) });
                corner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32, GridUnitType.Star) });
                corner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36, GridUnitType.Star) });
                corner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64, GridUnitType.Star) });
                Viewbox icon = CellNotepad();
                icon.MaxWidth = 9;
                icon.MaxHeight = 10;
                icon.Margin = new Thickness(0, 1, 1, 0);
                icon.HorizontalAlignment = HorizontalAlignment.Right;
                icon.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetColumn(icon, 1);
                corner.Children.Add(icon);
                grid.Children.Add(corner);
            }
        }

        /// <summary>A colored corner (top right, or bottom right for the bottom note) on a holiday, today or birthday.</summary>
        private static Rectangle CornerMark(string hex, bool top)
        {
            Color c = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new LinearGradientBrush
            {
                StartPoint = top ? new Point(1, 0) : new Point(1, 1),
                EndPoint = top ? new Point(0, 1) : new Point(0, 0)
            };
            brush.GradientStops.Add(new GradientStop(c, 0));
            brush.GradientStops.Add(new GradientStop(c, 0.28));
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.28));
            return new Rectangle { Fill = brush, IsHitTestVisible = false };
        }

        private static string SpanText(string s, string e)
        {
            DateTime a = Keys.Parse(s), b = Keys.Parse(e);
            return a.Day + "." + a.Month + ". – " + b.Day + "." + b.Month + ".";
        }

        /// <summary>Hover over a day with a note: the text (and the first pictures), under the holiday name if any.</summary>
        private void DayCell_ToolTipOpening(object sender, ToolTipEventArgs e)
        {
            if (editor != null || (drag != null && drag.Active)) { e.Handled = true; return; }
            if (!(sender is Border b) || !dayCells.TryGetValue(b, out DayCell cell) || !(b.ToolTip is ToolTip tip)) return;
            UIElement content = NotesOn ? NoteTipContent(cell) : null;
            if (content != null) tip.Content = content;
            else if (cell.Tip != null) tip.Content = cell.Tip;
            else e.Handled = true;
        }

        private UIElement NoteTipContent(DayCell cell)
        {
            List<Showing> list = notes.ShownList(cell.Key);
            if (list.Count == 0) return null;
            var panel = new StackPanel { MaxWidth = 280 };
            if (cell.Tip != null) panel.Children.Add(new TextBlock { Text = cell.Tip, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
            for (int i = 0; i < list.Count; i++)
            {
                Showing a = list[i];
                Note n = notes.Index[a.K];
                var part = new StackPanel();
                var frame = new Border { Child = part };
                if (list.Count > 1)
                {
                    string hex = NoteColors.Hex(n.Color);
                    frame.BorderBrush = Brush(hex ?? "#bbbbbb");
                    frame.BorderThickness = new Thickness(3, 0, 0, 0);
                    frame.Padding = new Thickness(5, 0, 0, 0);
                }
                if (i > 0)
                {
                    var line = new Border { BorderBrush = Brush("#dddddd"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 5, 0, 0), Padding = new Thickness(0, 4, 0, 0), Child = frame };
                    panel.Children.Add(line);
                }
                else panel.Children.Add(frame);
                if (a.E != a.S) part.Children.Add(new TextBlock { Text = SpanText(a.S, a.E), FontSize = 11, Foreground = Brush("#555555"), Margin = new Thickness(0, 0, 0, 2) });
                if (n.Repeat != null) part.Children.Add(new TextBlock { Text = "↻ " + notes.Summary(a.K, n.Repeat), FontSize = 11, Foreground = Brush("#555555"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) });
                string text = (n.Text ?? "").Trim();
                int max = list.Count > 1 ? 200 : 400;
                if (text != "") part.Children.Add(new TextBlock { Text = text.Length > max ? text.Substring(0, max) + "…" : text, TextWrapping = TextWrapping.Wrap });
                foreach (string p in n.Images.Take(list.Count > 1 ? 1 : 2))
                {
                    BitmapImage img = Load(System.IO.Path.Combine(noteStore.PicturesFolder, p), 280);
                    if (img != null) part.Children.Add(new Image { Source = img, MaxWidth = 260, MaxHeight = 140, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) });
                }
            }
            return panel;
        }

        // =====================================================================================
        // Click a day: a small box (peek) with ✎ for each note. Double-click opens the note at once.
        // =====================================================================================

        private Border peekBox;
        private Rectangle peekFrame;
        private DayCell peekCell;
        private DayCell pressedCell;
        private DateTime ignoreDoubleClickUntil;

        private void DayCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!NotesHere || !(sender is Border b) || !dayCells.TryGetValue(b, out DayCell cell)) return;
            if (b.ToolTip is ToolTip tip) tip.IsOpen = false;
            if (e.ClickCount >= 2)
            {
                pressedCell = null;
                if (DateTime.Now < ignoreDoubleClickUntil) return;
                ClosePeek();
                OpenEditor(cell.Date, cell.Tip);
                e.Handled = true;
                return;
            }
            // A click (opens the small box on release), or the start of a drag
            pressedCell = cell;
            StartDrag(cell, e);
        }

        private void OpenPeek(DayCell cell)
        {
            // A second click on the same day closes it again (back to the plain calendar)
            bool same = peekCell != null && peekCell.Key == cell.Key;
            ClosePeek();
            if (same) return;
            List<Showing> list = NotesOn ? notes.ShownList(cell.Key) : new List<Showing>();
            var row = new StackPanel { Orientation = Orientation.Horizontal };

            void AddButton(string text, string title, string pick, string hex)
            {
                var button = new Button
                {
                    Content = text, ToolTip = title, Width = 24, Height = 22, Padding = new Thickness(0), FontSize = 14,
                    Margin = new Thickness(0, 0, 5, 0), Style = (Style)FindResource("CustomButtonStyle"), Background = Brush(Color.FromArgb(26, 8, 8, 8))
                };
                if (hex != null) { button.BorderBrush = Brush(hex); button.BorderThickness = new Thickness(0, 0, 0, 3); }
                button.Click += (s, e) => { ClosePeek(); OpenEditor(cell.Date, cell.Tip, false, pick); };
                row.Children.Add(button);
            }

            if (list.Count == 0) AddButton("✎", T("Write a note", "Skriv notat"), null, null);
            for (int i = 0; i < list.Count; i++)
            {
                Note n = notes.Index[list[i].K];
                string first = (n.Text ?? "").Trim().Split('\n')[0].Trim();
                string title = (list.Count > 1 ? T(i > 0 ? "Bottom note" : "Top note", i > 0 ? "Nederste notat" : "Øverste notat") : T("Open note", "Åpne notat")) + (first != "" ? ": " + first : "");
                AddButton("✎", title, list[i].K, list.Count > 1 ? NoteColors.Hex(n.Color) : null);
            }
            if (list.Count == 1 && NotesOn && notes.FreeSlot(cell.Key, cell.Key, null) >= 0) AddButton("+", T("Another note on this day", "Et notat til på denne dagen"), "new", null);
            if (cell.Tip != null) row.Children.Add(new TextBlock { Text = cell.Tip, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });

            peekBox = Floating(row, new Thickness(2, 2, 0, 2));
            peekBox.MaxWidth = 260;
            peekBox.MouseLeftButtonUp += (s, e) => ClosePeek();   // a click on the box itself (not a button) just closes it
            Rect r = CellRect(cell.Border);
            peekFrame = new Rectangle { Width = r.Width, Height = r.Height, Stroke = Brushes.Black, StrokeThickness = 2, IsHitTestVisible = false };
            Canvas.SetLeft(peekFrame, r.Left);
            Canvas.SetTop(peekFrame, r.Top);
            noteLayer.Children.Add(peekFrame);
            noteLayer.Children.Add(peekBox);
            peekCell = cell;
            Place(peekBox, r, small: true);
        }

        private void ClosePeek()
        {
            if (peekBox != null) noteLayer.Children.Remove(peekBox);
            if (peekFrame != null) noteLayer.Children.Remove(peekFrame);
            peekBox = null;
            peekFrame = null;
            peekCell = null;
        }

        /// <summary>A white box with a thin frame and a shadow, for things that float over the calendar.</summary>
        private static Border Floating(UIElement child, Thickness padding) => new Border
        {
            Background = Brushes.White,
            BorderBrush = LayerShadowBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = padding,
            Child = child,
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.25, Direction = 270 }
        };

        private Rect CellRect(FrameworkElement cell)
        {
            if (!cell.IsArrangeValid) UpdateLayout();
            return new Rect(cell.TranslatePoint(new Point(0, 0), noteLayer), new Size(cell.ActualWidth, cell.ActualHeight));
        }

        /// <summary>Next to the day (small: under it, or above if no room, so the days beside it stay clickable).</summary>
        private void Place(FrameworkElement box, Rect r, bool small)
        {
            double vw = noteLayer.ActualWidth > 0 ? noteLayer.ActualWidth : tabContainer.Width;
            double vh = noteLayer.ActualHeight > 0 ? noteLayer.ActualHeight : tabContainer.Height;
            box.Measure(new Size(double.IsNaN(box.Width) ? double.PositiveInfinity : box.Width, double.PositiveInfinity));
            double w = box.DesiredSize.Width, h = Math.Min(box.DesiredSize.Height, double.IsInfinity(box.MaxHeight) ? double.MaxValue : box.MaxHeight);
            double left, top;
            if (small)
            {
                top = r.Bottom + 4;
                if (top + h > vh - 4) top = r.Top - h - 4;
                left = Math.Max(4, Math.Min(vw - w - 4, r.Left));
                top = Math.Max(4, top);
            }
            else
            {
                left = r.Right + 6;
                top = r.Top;
                if (left + w > vw - 4) left = r.Left - w - 6;
                if (left < 4) left = Math.Max(4, Math.Min(vw - w - 4, r.Left));
                if (top + h > vh - 4) top = Math.Max(4, vh - h - 4);
            }
            Canvas.SetLeft(box, left);
            Canvas.SetTop(box, top);
        }

        /// <summary>The cell drawn now for a day (the month view's middle month first), or null.</summary>
        private DayCell FindCell(string dayKey)
        {
            DayCell found = null;
            foreach (DayCell c in dayCells.Values)
            {
                if (c.Key != dayKey) continue;
                if (found == null || IsInside(c.Border, calGrid2)) found = c;
            }
            return found;
        }

        private static bool IsInside(DependencyObject d, DependencyObject container)
        {
            while (d != null)
            {
                if (d == container) return true;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        // =====================================================================================
        // Mouse and keys for the notes (from the window)
        // =====================================================================================

        private void Notes_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            if (imageView != null) { CloseImageView(); e.Handled = true; return; }
            if (repeatHelp != null)
            {
                if (!IsInside(source, repeatHelpBox)) { CloseRepeatHelp(); e.Handled = true; }
                return;
            }
            if (editor != null)
            {
                // While a note is open, a click outside it only closes it
                if (!IsInside(source, editor.Box) && !IsInside(source, undoBox))
                {
                    CloseEditor();
                    ignoreDoubleClickUntil = DateTime.Now.AddMilliseconds(600);
                    e.Handled = true;
                }
                return;
            }
            if (peekBox != null && !IsInside(source, peekBox) && !dayCells.Keys.Any(b => IsInside(source, b))) ClosePeek();
            CloseNotesMenusOutside(source);
        }

        private void Notes_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            DayCell pressed = pressedCell;
            pressedCell = null;
            if (drag != null)
            {
                bool wasDrag = drag.Active;
                FinishDrag(e);
                if (wasDrag) { e.Handled = true; return; }
            }
            if (pressed != null && editor == null && e.ClickCount < 2 && dayCells.ContainsKey(pressed.Border) && pressed.Border.IsMouseOver) OpenPeek(pressed);
        }

        /// <summary>Keys for the notes. True when the calendar must not act on the key (a note is open, for example).</summary>
        private bool NotesKeyDown(KeyEventArgs e)
        {
            if (imageView != null) { if (e.Key == Key.Escape) { CloseImageView(); e.Handled = true; } return true; }
            if (repeatHelp != null) { if (e.Key == Key.Escape) { CloseRepeatHelp(); e.Handled = true; } return true; }
            if (drag != null && drag.Active)
            {
                if (e.Key == Key.Escape) { EndDrag(); e.Handled = true; }
                else if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl) { drag.Ctrl = true; Retarget(); }
                return true;
            }
            if (editor != null)
            {
                if (e.Key == Key.Escape) { CloseEditor(); e.Handled = true; }
                return true;   // typing in a note: no calendar shortcuts
            }
            if (!NotesHere) return false;
            bool calendar = tab1Content.Visibility == Visibility.Visible || IsYearView;
            // Enter: open the note of the picked day (the one with the small box), or of today
            if (e.Key == Key.Enter && calendar && !e.IsRepeat && Keyboard.Modifiers == ModifierKeys.None && !(e.OriginalSource is TextBox))
            {
                DayCell cell = peekCell ?? dayCells.Values.FirstOrDefault(c => c.Kind == CellKind.Today || (c.Date == DateTime.Today));
                if (cell != null)
                {
                    ClosePeek();
                    OpenEditor(cell.Date, cell.Tip);
                    e.Handled = true;
                    return true;
                }
            }
            ClosePeek();
            return false;
        }

        private void Notes_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (drag != null && drag.Active && (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)) { drag.Ctrl = false; Retarget(); }
        }

        // =====================================================================================
        // Editor: write, pick a color, paste a picture, set the last day or a repeat. Saves by itself.
        // =====================================================================================

        private sealed class EditorState
        {
            public Border Box;
            public ScrollViewer Scroll;
            public Note Note;            // what is being written; Date is the key it is saved under
            public bool Dirty;
            public Note Orig;            // as it was, for Undo (null: a new note)
            public string Series;        // one day of a repeating note: the series' key
            public bool IsSeries;        // the whole series is open
            public string Day;
            public DispatcherTimer Timer;
            public string CellKey;       // the day it was opened from (to place it)
            public Action ShowColor, ShowImages, ShowRepeat, ShowSeriesBar;
            public Action<string> ChangeStart;
            public TextBox Area;
        }

        private EditorState editor;
        private const double EditorWidth = 340;

        /// <summary>
        /// pick: which note on the day (its key), "new" for another note on the day, or null for the first one.
        /// A day inside a multi-day note opens that note (from its first day). A day of a repeating note opens that
        /// day: it becomes its own note only when changed, in a free half of that day. wholeSeries opens the series itself.
        /// </summary>
        private void OpenEditor(DateTime date, string holidayText, bool wholeSeries = false, string pick = null)
        {
            ClosePeek();
            if (editor != null) CloseEditor();
            string dk = Keys.Key(date);
            Showing a = null;
            string k;
            string series = null;
            if (NotesOn)
            {
                List<Showing> list = notes.ListAt(dk);
                if (pick != "new" && pick == null)
                    pick = (notes.ShownList(dk).FirstOrDefault() ?? (notes.FreeSlot(dk, dk, null) < 0 ? list.FirstOrDefault() : null))?.K ?? "new";
                if (pick != "new")
                {
                    a = list.FirstOrDefault(x => x.K == pick);
                    if (a == null && pick != null && notes.Index.TryGetValue(pick, out Note pn))
                        a = new Showing { K = pick, S = Keys.DayOf(pick), E = pn.End == "" ? Keys.DayOf(pick) : pn.End, Slot = Keys.SlotOf(pick) };
                }
                series = a != null && notes.Index.TryGetValue(a.K, out Note an) && an.Repeat != null && !wholeSeries ? a.K : null;
                if (series != null)
                {
                    int? sl = new[] { a.Slot, 1 - a.Slot }.Cast<int?>().FirstOrDefault(x =>
                    {
                        for (string d = a.S; Keys.Compare(d, a.E) <= 0; d = Keys.AddDays(d, 1)) if (notes.CoverAt(x.Value, d) != null) return false;
                        return !notes.Index.ContainsKey(Keys.SlotKey(a.S, x.Value));
                    });
                    k = Keys.SlotKey(a.S, sl ?? a.Slot);
                }
                else if (a != null) k = a.K;
                else
                {
                    int free = notes.FreeSlot(dk, dk, null);
                    k = Keys.SlotKey(dk, free < 0 ? 0 : free);
                }
            }
            else k = dk;
            string day = Keys.DayOf(k);
            if (day != dk) { date = Keys.Parse(day); holidayText = null; }
            bool isSeries = NotesOn && notes.Index.TryGetValue(k, out Note kn) && kn.Repeat != null;

            var ed = new EditorState { Series = series, IsSeries = isSeries, Day = day, CellKey = dk };
            var root = new StackPanel();
            ed.Scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root, Focusable = false };
            ed.Box = Floating(ed.Scroll, new Thickness(8, 6, 8, 6));
            ed.Box.Width = EditorWidth;
            ed.Box.MaxHeight = Math.Max(120, (noteLayer.ActualHeight > 0 ? noteLayer.ActualHeight : tabContainer.Height) - 8);
            ed.Box.Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 6, Opacity = 0.25, Direction = 270 };
            TextElement.SetFontSize(ed.Box, 13);

            // Head: the day, – last day, holiday name, ✕
            var head = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            var headGrid = new DockPanel { LastChildFill = true };
            var close = new Button { Content = "✕", ToolTip = T("Close", "Lukk"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Brush("#555555"), FontSize = 14, Padding = new Thickness(2, 0, 2, 0), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top, Style = (Style)FindResource("CustomButtonStyle") };
            close.Background = Brushes.Transparent;
            close.Click += (s, e) => CloseEditor();
            DockPanel.SetDock(close, Dock.Right);
            headGrid.Children.Add(close);
            headGrid.Children.Add(head);
            root.Children.Add(headGrid);
            head.Children.Add(new TextBlock { Text = DayTitle(date, false), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });

            if (!NotesOn)
            {
                root.Children.Add(new TextBlock
                {
                    Text = T("The notes could not be read (QuickCal.notes.json is damaged). It is left as it is.",
                             "Notatene kunne ikke leses (QuickCal.notes.json er skadet). Filen blir ikke rørt."),
                    TextWrapping = TextWrapping.Wrap, Foreground = Brush("#666666"), FontSize = 10.5, Margin = new Thickness(0, 4, 0, 0)
                });
                editor = ed;
                noteLayer.Children.Add(ed.Box);
                PositionEditor();
                return;
            }

            var dash = new TextBlock { Text = "–", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            var endPicker = DateField(Keys.Parse(day), Keys.Parse(notes.MaxEnd(k)));
            endPicker.SelectedDate = Keys.Parse(day);
            endPicker.ToolTip = T("Last day of the note", "Siste dag for notatet");
            head.Children.Add(dash);
            head.Children.Add(endPicker);
            if (!string.IsNullOrEmpty(holidayText))
                head.Children.Add(new TextBlock { Text = holidayText, Foreground = Brush("#b0102a"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });

            var bar = new WrapPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 4) };
            root.Children.Add(bar);

            var area = new TextBox
            {
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 54, MaxHeight = 140, FontSize = 13,
                Padding = new Thickness(3, 2, 3, 2), BorderBrush = Brush("#aaaaaa"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            SetPlaceholder(area, T("Write a note…", "Skriv et notat …"));
            ed.Area = area;
            root.Children.Add(area);
            var images = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            root.Children.Add(images);

            // Colors, checklist, repeat, picture, delete
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 0), LastChildFill = true };
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(tools, Dock.Right);
            var swatches = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(tools);
            row.Children.Add(swatches);
            root.Children.Add(row);
            Button ToolButton(object content, string tip)
            {
                var b = new Button { Content = content, ToolTip = tip, Height = 26, MinWidth = 32, FontSize = 15, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(6, 0, 0, 0), Style = (Style)FindResource("CustomButtonStyle"), Background = Brush(Color.FromArgb(31, 8, 8, 8)) };
                tools.Children.Add(b);
                return b;
            }
            Button check = ToolButton("☑", T("Checklist: tick box on this line", "Sjekkliste: avkrysningsboks på denne linjen"));
            Button rep = ToolButton("↻", T("Repeat", "Gjenta"));
            Button addImg = ToolButton(Symbol("", 15), T("Add picture (or paste with Ctrl+V)", "Legg til bilde (eller lim inn med Ctrl+V)"));
            Button del = ToolButton(BinIcon(16), T("Delete note", "Slett notat"));

            var panel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 5, 0, 0) };
            root.Children.Add(panel);
            var hint = new TextBlock
            {
                Text = T("Saved automatically. Paste pictures with Ctrl+V. Kept " + KeptFor() + ". ☑ makes a checklist: click a box to tick it. Ctrl+Enter saves and closes.",
                         "Lagres automatisk. Lim inn bilder med Ctrl+V. Tas vare på " + KeptFor() + ". ☑ lager sjekkliste: klikk på en boks for å krysse av. Ctrl+Enter lagrer og lukker."),
                TextWrapping = TextWrapping.Wrap, Foreground = Brush("#666666"), FontSize = 10.5, Margin = new Thickness(0, 4, 0, 0)
            };
            root.Children.Add(hint);

            Note note = new Note { Date = k, Series = series ?? "" };
            ed.Note = note;
            ed.Dirty = series == null;
            ed.Timer = new DispatcherTimer();
            ed.Timer.Tick += (s, e) => { ed.Timer.Stop(); FlushEditor(); };
            editor = ed;
            noteLayer.Children.Add(ed.Box);

            // Start from what is saved (a day of a series: from the series' text, color and pictures, on this day)
            Note saved = noteStore.Get(series ?? k)?.Copy();
            if (saved != null && series != null)
                saved = new Note { Date = k, Text = saved.Text, Color = saved.Color, Images = saved.Images, EndDate = Keys.Compare(a.E, a.S) > 0 ? a.E : null };
            if (saved != null && !string.IsNullOrEmpty(saved.Series)) note.Series = saved.Series;
            if (series == null) ed.Orig = saved?.Copy();
            if (saved != null)
            {
                note.Text = saved.Text ?? "";
                note.Color = saved.Color ?? "";
                note.Images = new List<string>(saved.Images);
                note.EndDate = saved.EndDate;
                note.Repeat = saved.Repeat?.Copy();
            }
            area.Text = note.Text;
            area.CaretIndex = area.Text.Length;
            endPicker.SelectedDate = Keys.Parse(string.IsNullOrEmpty(note.EndDate) ? day : note.EndDate);
            rep.Background = note.Repeat != null ? Brush("#dbe8ff") : Brush(Color.FromArgb(31, 8, 8, 8));
            if (series != null) rep.Visibility = Visibility.Collapsed;   // one day of a series: no rule of its own

            void SaveSoon(int ms = 400)
            {
                ed.Dirty = true;
                ed.Timer.Stop();
                ed.Timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms));
                ed.Timer.Start();
            }

            // Two notes on a day may not have the same color: the colors another note has on this note's days are
            // greyed out (No color is always free). A color the note already has stays, so older notes still open.
            HashSet<string> TakenColors()
            {
                var taken = new HashSet<string>();
                string last = !string.IsNullOrEmpty(note.EndDate) && Keys.Compare(note.EndDate, day) > 0 ? note.EndDate : day;
                int i = 0;
                for (string d = day; Keys.Compare(d, last) <= 0 && i < NotesEngine.MaxSpanDays; d = Keys.AddDays(d, 1), i++)
                    foreach (Showing o in notes.ListAt(d))
                        if (o.K != k && o.K != series && notes.Index.TryGetValue(o.K, out Note on) && !string.IsNullOrEmpty(on.Color)) taken.Add(on.Color);
                return taken;
            }
            ed.ShowColor = () =>
            {
                swatches.Children.Clear();
                HashSet<string> taken = TakenColors();
                foreach (string id in NoteColors.IdsWithNone)
                {
                    bool on = id == (note.Color ?? ""), off = taken.Contains(id) && !on;
                    string hex = NoteColors.Hex(id);
                    var dot = new Border
                    {
                        Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 4, 0),
                        Background = hex != null ? Brush(hex) : Brushes.White,
                        BorderBrush = on ? Brushes.Black : Brush("#999999"), BorderThickness = new Thickness(on ? 2 : 1),
                        Cursor = off ? Cursors.No : Cursors.Hand, Opacity = off ? 0.3 : 1,
                        ToolTip = NoteColors.Number(id) + " · " + ColorLabel(id) + (off ? " · " + T("used by the other note this day", "brukt av det andre notatet denne dagen") : "")
                    };
                    var inner = new Grid();
                    if (on && hex != null) inner.Children.Add(new Ellipse { Stroke = Brushes.White, StrokeThickness = 2 });
                    inner.Children.Add(new TextBlock { Text = NoteColors.Number(id).ToString(), FontSize = 10, FontWeight = FontWeights.Bold, Foreground = hex != null ? Brushes.White : Brush("#555555"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                    if (off) inner.Children.Add(new Line { X1 = 3, Y1 = 15, X2 = 15, Y2 = 3, Stroke = Brushes.Black, StrokeThickness = 2 });
                    dot.Child = inner;
                    if (!off) dot.MouseLeftButtonUp += (s, e) => { note.Color = id; ed.ShowColor(); SaveSoon(0); };
                    swatches.Children.Add(dot);
                }
            };
            ed.ShowImages = () =>
            {
                images.Children.Clear();
                for (int i = 0; i < note.Images.Count; i++)
                {
                    int index = i;
                    string name = note.Images[i];
                    string path = System.IO.Path.Combine(noteStore.PicturesFolder, name);
                    var img = new Image { Source = Load(path, 104), Height = 52, MaxWidth = 90, Stretch = Stretch.UniformToFill, Cursor = Cursors.Hand, ToolTip = T("Show picture", "Vis bildet") };
                    img.MouseLeftButtonUp += (s, e) => ShowImageView(path);
                    var frame = new Border { BorderBrush = Brush("#cccccc"), BorderThickness = new Thickness(1), Child = img, ClipToBounds = true };
                    var remove = new Button
                    {
                        Content = "✕", Width = 16, Height = 16, FontSize = 9, Padding = new Thickness(0), Foreground = Brushes.White,
                        ToolTip = T("Remove picture", "Fjern bildet"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, -5, -5, 0), Cursor = Cursors.Hand
                    };
                    remove.Template = RoundButtonTemplate(Brush("#444444"));
                    remove.Click += (s, e) => { note.Images.RemoveAt(index); ed.ShowImages(); SaveSoon(0); };
                    var wrap = new Grid { Margin = new Thickness(0, 0, 8, 4) };
                    wrap.Children.Add(frame);
                    wrap.Children.Add(remove);
                    images.Children.Add(wrap);
                }
                images.Visibility = note.Images.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                PositionEditor();
            };

            bool AddPictures(IEnumerable<BitmapSource> pictures)
            {
                bool any = false;
                foreach (BitmapSource p in pictures)
                {
                    if (note.Images.Count >= NoteStore.MaxImages) break;
                    try
                    {
                        note.Images.Add(noteStore.AddPicture(Shrink(p), png: false));
                        any = true;
                    }
                    catch (Exception)
                    {
                        // Not a picture after all
                    }
                }
                if (any) { ed.ShowImages(); SaveSoon(0); }
                return any;
            }

            area.TextChanged += (s, e) => { if (area.Text != note.Text) { note.Text = area.Text; SaveSoon(); } };

            // Checklists: a line that starts with ☐ is a task, ☑ a done task
            void SetText(string text, int caret)
            {
                note.Text = text;
                area.Text = text;
                area.CaretIndex = Math.Max(0, Math.Min(caret, text.Length));
                SaveSoon(0);
            }
            static bool IsBox(string line) => line.StartsWith("☐", StringComparison.Ordinal) || line.StartsWith("☑", StringComparison.Ordinal);
            static string Unbox(string line) => IsBox(line) ? line.Substring(line.Length > 1 && line[1] == ' ' ? 2 : 1) : line;
            check.Click += (s, e) =>
            {
                string v = area.Text;
                int a0 = area.SelectionStart, b0 = a0 + area.SelectionLength;
                int start = a0 == 0 ? 0 : v.LastIndexOf('\n', a0 - 1) + 1, end = v.IndexOf('\n', b0);
                if (end < 0) end = v.Length;
                string[] lines = v.Substring(start, end - start).Split('\n');
                bool off = lines.All(l => IsBox(l.TrimEnd('\r')));
                string block = string.Join("\n", lines.Select(l => off ? Unbox(l) : "☐ " + l));
                SetText(v.Substring(0, start) + block + v.Substring(end), start + block.TrimEnd('\r').Length);
                area.Focus();
            };
            area.PreviewMouseLeftButtonUp += (s, e) =>
            {
                // A click on a box: tick it (or untick)
                if (area.SelectionLength != 0) return;
                string v = area.Text;
                int p = area.CaretIndex;
                foreach (int i in new[] { p, p - 1 })
                {
                    if (i < 0 || i >= v.Length || (v[i] != '☐' && v[i] != '☑') || (i > 0 && v[i - 1] != '\n')) continue;
                    SetText(v.Substring(0, i) + (v[i] == '☐' ? '☑' : '☐') + v.Substring(i + 1), Math.Min(v.Length, i + 2));
                    break;
                }
            };
            area.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    // Ctrl+Enter: save and close (no new line in the text)
                    e.Handled = true;
                    CloseEditor();
                    return;
                }
                if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None || area.SelectionLength != 0) return;
                // Enter on a task line: the next line is a task too
                string v = area.Text;
                int p = area.CaretIndex, start = p == 0 ? 0 : v.LastIndexOf('\n', p - 1) + 1;
                string line = v.Substring(start, p - start);
                if (!IsBox(line)) return;
                e.Handled = true;
                if (Unbox(line).Trim() == "") SetText(v.Substring(0, start) + v.Substring(p), start);   // empty task: end the list
                else SetText(v.Substring(0, p) + "\n☐ " + v.Substring(p), p + 3);
            };
            // Paste a picture (Ctrl+V)
            CommandManager.AddPreviewExecutedHandler(area, (s, e) =>
            {
                if (e.Command != ApplicationCommands.Paste) return;
                List<BitmapSource> pics = ClipboardPictures();
                if (pics.Count > 0) { AddPictures(pics); e.Handled = true; }
            });
            ed.Box.AllowDrop = true;
            ed.Box.PreviewDragOver += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; } };
            ed.Box.PreviewDrop += (s, e) =>
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files) { AddPictures(FilePictures(files)); e.Handled = true; }
            };
            addImg.Click += (s, e) =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Multiselect = true,
                    Filter = T("Pictures", "Bilder") + "|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tif;*.tiff;*.webp;*.heic|" + T("All files", "Alle filer") + "|*.*"
                };
                if (dialog.ShowDialog(this) == true) AddPictures(FilePictures(dialog.FileNames));
            };
            endPicker.SelectedDateChanged += (s, e) =>
            {
                string v = endPicker.SelectedDate.HasValue ? Keys.Key(endPicker.SelectedDate.Value) : day;
                string max = notes.MaxEnd(k);
                if (Keys.Compare(v, day) < 0) v = day;
                if (Keys.Compare(v, max) > 0) v = max;
                if (endPicker.SelectedDate != Keys.Parse(v)) { endPicker.SelectedDate = Keys.Parse(v); return; }
                string newEnd = Keys.Compare(v, day) > 0 ? v : null;
                if (newEnd == note.EndDate || (string.IsNullOrEmpty(newEnd) && string.IsNullOrEmpty(note.EndDate))) return;
                note.EndDate = newEnd;
                ed.ShowColor();   // more days can mean more colors taken
                SaveSoon(0);
            };
            del.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(note.Series)) { SkipDay(); return; }   // a day of a series: the series skips it
                note.Text = "";
                note.Color = "";
                note.Images = new List<string>();
                note.Repeat = null;
                ed.Dirty = true;
                CloseEditor();
            };
            ed.ShowRepeat = () =>
            {
                rep.Background = note.Repeat != null ? Brush("#dbe8ff") : Brush(Color.FromArgb(31, 8, 8, 8));
                FillRepeatPanel(panel, note, () => { ed.ShowRepeat(); SaveSoon(0); });
                PositionEditor();
            };
            rep.Click += (s, e) =>
            {
                bool show = panel.Visibility != Visibility.Visible;
                panel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                hint.Visibility = show ? Visibility.Collapsed : Visibility.Visible;   // room for the panel in a small window
                if (show) ed.ShowRepeat(); else PositionEditor();
            };

            // The bar for a repeating note: what this is, and what can be done with the series
            ed.ShowSeriesBar = () =>
            {
                string sk = !string.IsNullOrEmpty(note.Series) ? note.Series : isSeries ? k : "";
                Note sn = sk != "" ? noteStore.Get(sk) : null;
                bar.Children.Clear();
                bar.Visibility = sn?.Repeat != null ? Visibility.Visible : Visibility.Collapsed;
                if (sn?.Repeat == null) return;
                TextBlock BarText(string t) => new TextBlock { Text = "↻ " + t, Foreground = Brush(Blue), TextWrapping = TextWrapping.Wrap, Width = EditorWidth - 20, FontSize = 12, Margin = new Thickness(0, 0, 0, 2) };
                if (isSeries)
                {
                    bar.Children.Add(BarText(T("The whole series. Changes apply to all its days.", "Hele serien. Endringer gjelder alle dagene.")));
                    panel.Visibility = Visibility.Visible;
                    hint.Visibility = Visibility.Collapsed;
                    ed.ShowRepeat();
                    return;
                }
                bar.Children.Add(BarText(!string.IsNullOrEmpty(note.Series) && series == null
                    ? T("This day is changed from the series.", "Denne dagen er endret fra serien.")
                    : T("One day in a series. Writing here changes only this day.", "Én dag i en serie. Det du skriver her, gjelder bare denne dagen.")));
                void BarButton(string label, string title, Action fn)
                {
                    var b = new Button { Content = label, ToolTip = title, FontSize = 11, Height = 20, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 4, 0), Background = Brushes.White, BorderBrush = Brush("#8fa5d6"), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
                    b.Click += (s, e) => fn();
                    bar.Children.Add(b);
                }
                BarButton(T("Edit series", "Rediger serien"), notes.Summary(sk, sn.Repeat), () =>
                {
                    CloseEditor();
                    OpenEditor(Keys.Parse(sk), null, wholeSeries: true);
                });
                BarButton(T("Delete this day", "Slett denne dagen"), T("The series skips this day", "Serien hopper over denne dagen"), SkipDay);
                BarButton(T("End series here", "Avslutt serien her"), T("No more days from this one", "Ingen flere dager fra og med denne"), () =>
                {
                    Drop();
                    Action undo = Snapshot(new[] { sk });
                    Note n = noteStore.Get(sk)?.Copy();
                    if (n?.Repeat == null) return;
                    if (Keys.Compare(Keys.AddDays(day, -1), Keys.DayOf(n.Date)) < 0)
                    {
                        // Nothing left of it
                        n.Text = ""; n.Color = ""; n.Images = new List<string>(); n.Repeat = null;
                    }
                    else n.Repeat.Until = Keys.AddDays(day, -1);
                    noteStore.Put(n);
                    NotesChanged();
                    ShowUndo(T("Series ended.", "Serien er avsluttet."), undo);
                });
            };
            void SkipDay()
            {
                string sk = note.Series;
                Drop();
                Action undo = Snapshot(new[] { sk, k });
                Note n = noteStore.Get(sk)?.Copy();
                if (n?.Repeat == null) return;
                n.Repeat.Skip = (n.Repeat.Skip ?? new List<string>()).Concat(new[] { day }).ToList();
                if (noteStore.Get(k)?.Series == sk) noteStore.Delete(k);
                noteStore.Put(n);
                NotesChanged();
                ShowUndo(T("Day deleted from the series.", "Dagen er slettet fra serien."), undo);
            }
            // Close without saving what is open (the series buttons save themselves)
            void Drop()
            {
                if (editor != null) { editor.Timer?.Stop(); editor.Note = null; }
                CloseEditor();
            }
            ed.ChangeStart = v =>
            {
                // The series gets a new first day (it is the note's key)
                if (string.IsNullOrEmpty(v) || v == day) return;
                if (noteStore.Get(Keys.SlotKey(v, Keys.SlotOf(k))) != null)
                {
                    MessageBox.Show(this, T("There is already a note on that day.", "Det er allerede et notat på den dagen."), "QuickCal", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                FlushEditor();
                Note n = noteStore.Get(k)?.Copy();
                if (n == null) return;
                int len = !string.IsNullOrEmpty(n.EndDate) ? Keys.DaysBetween(n.Date, n.EndDate) : 0;
                Drop();
                Relocate(n, v, Keys.AddDays(v, len));
                OpenEditor(Keys.Parse(v), null, wholeSeries: true);
            };

            ed.ShowColor();
            ed.ShowImages();
            ed.ShowSeriesBar();
            PositionEditor();
            area.Focus();
        }

        /// <summary>A grey hint in an empty text box.</summary>
        private static void SetPlaceholder(TextBox box, string text)
        {
            var visual = new VisualBrush
            {
                Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                Visual = new TextBlock { Text = text, Foreground = Brush("#999999"), FontSize = box.FontSize, Margin = new Thickness(5, 3, 0, 0) }
            };
            void Update() => box.Background = box.Text.Length == 0 ? visual : (Brush)Brushes.White;
            box.TextChanged += (s, e) => Update();
            Update();
        }

        private static ControlTemplate RoundButtonTemplate(Brush background)
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, background);
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            template.VisualTree = border;
            return template;
        }

        /// <summary>A date field (the web app's date input). Its calendar only offers first..last.</summary>
        private DatePicker DateField(DateTime? first, DateTime? last)
        {
            var picker = new DatePicker
            {
                FontSize = 11.5, Height = 22, Width = 112, Padding = new Thickness(2, 0, 2, 0), VerticalContentAlignment = VerticalAlignment.Center,
                DisplayDateStart = first, DisplayDateEnd = last, SelectedDateFormat = DatePickerFormat.Short, FirstDayOfWeek = DayOfWeek.Monday
            };
            return picker;
        }

        private void PositionEditor()
        {
            if (editor == null) return;
            // After the new content is laid out
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (editor == null) return;
                DayCell cell = FindCell(editor.CellKey) ?? FindCell(editor.Day);
                Rect r = cell != null ? CellRect(cell.Border) : new Rect(noteLayer.ActualWidth / 2 - 10, 20, 0, 0);
                Place(editor.Box, r, small: false);
            }));
        }

        private void FlushEditor()
        {
            EditorState ed = editor;
            if (ed?.Note == null || !ed.Dirty) return;
            ed.Timer.Stop();
            Note n = new Note
            {
                Date = ed.Note.Date,
                Text = (ed.Note.Text ?? "").Replace("\r\n", "\n"),
                Color = ed.Note.Color ?? "",
                Images = new List<string>(ed.Note.Images),
                EndDate = string.IsNullOrEmpty(ed.Note.EndDate) ? null : ed.Note.EndDate,
                Repeat = ed.Note.Repeat?.Copy(),
                Series = string.IsNullOrEmpty(ed.Note.Series) ? null : ed.Note.Series
            };
            Note stored = noteStore.Get(n.Date);
            // Nothing changed: leave it (so other devices do not get the same note again)
            if (stored != null && !Note.Differs(stored, n) && (stored.Series ?? "") == (n.Series ?? "")) return;
            if (stored == null && n.IsEmpty) return;
            noteStore.Put(n);
            NotesChanged();
        }

        private void CloseEditor()
        {
            if (editor == null) return;
            EditorState ed = editor;
            CloseRepeatHelp();
            FlushEditor();
            editor = null;
            noteLayer.Children.Remove(ed.Box);
            // Undo after changing or deleting a note (not after writing a new one). A changed day of a series: Undo removes the change.
            Note n = ed.Note;
            if (n != null && ed.Dirty)
            {
                Note now = noteStore.Get(n.Date);
                Note orig = ed.Orig;
                bool changed = orig != null ? (now == null || Note.Differs(orig, now)) : (!string.IsNullOrEmpty(n.Series) && n.Date != n.Series && now != null);
                if (changed)
                {
                    string k = n.Date;
                    bool gone = now == null;
                    ShowUndo(orig != null ? (gone ? T("Note deleted.", "Notatet er slettet.") : T("Note changed.", "Notatet er endret.")) : T("Day changed.", "Dagen er endret."),
                        () => { noteStore.PutBack(new[] { k }, new[] { orig }); NotesChanged(); });
                }
            }
            Focus();
        }

        // ---------- Pictures in: from the clipboard or files ----------

        private static List<BitmapSource> ClipboardPictures()
        {
            var result = new List<BitmapSource>();
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    result.AddRange(FilePictures(Clipboard.GetFileDropList().Cast<string>()));
                    if (result.Count > 0) return result;
                }
                // Browsers and the Snipping Tool put a PNG on the clipboard: it keeps the colors right
                if (Clipboard.ContainsData("PNG") && Clipboard.GetData("PNG") is MemoryStream png)
                {
                    BitmapSource p = Decode(png.ToArray());
                    if (p != null) { result.Add(p); return result; }
                }
                if (Clipboard.ContainsImage())
                {
                    BitmapSource img = Clipboard.GetImage();
                    // Without the alpha channel: the clipboard's bitmap often says "transparent" for every pixel
                    if (img != null) result.Add(new FormatConvertedBitmap(img, PixelFormats.Bgr32, null, 0));
                }
            }
            catch (Exception)
            {
                // The clipboard is busy or holds something else
            }
            return result;
        }

        private static IEnumerable<BitmapSource> FilePictures(IEnumerable<string> files)
        {
            foreach (string f in files)
            {
                BitmapSource p = null;
                try { if (File.Exists(f) && new FileInfo(f).Length < 60_000_000) p = Decode(File.ReadAllBytes(f)); }
                catch (Exception) { p = null; }
                if (p != null) yield return p;
            }
        }

        // ---------- A picture at full size ----------

        private Border imageView;

        private void ShowImageView(string path)
        {
            BitmapImage img = Load(path);
            if (img == null) return;
            imageView = new Border
            {
                Background = Brush(Color.FromArgb(204, 0, 0, 0)),
                Width = noteLayer.ActualWidth,
                Height = noteLayer.ActualHeight,
                Cursor = Cursors.Hand,
                Child = new Image { Source = img, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(6) }
            };
            noteLayer.Children.Add(imageView);
        }

        private void CloseImageView()
        {
            if (imageView != null) noteLayer.Children.Remove(imageView);
            imageView = null;
        }

        // =====================================================================================
        // The repeat panel in the editor: a few choices that read as a sentence, and the next days it gives
        // =====================================================================================

        private void FillRepeatPanel(StackPanel panel, Note note, Action changed)
        {
            RepeatRule r = note.Repeat;
            DateTime S = Keys.Parse(note.Date);
            panel.Children.Clear();
            int[] wds = { 1, 2, 3, 4, 5, 6, 0 };

            WrapPanel Line()
            {
                var l = new WrapPanel { Margin = new Thickness(0, 0, 0, 3) };
                panel.Children.Add(l);
                return l;
            }
            TextBlock Words(string t, string color = null) => new TextBlock { Text = t, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Foreground = color != null ? Brush(color) : Brushes.Black };
            ComboBox Select(IEnumerable<(string Value, string Label)> options, string value, Action<string> onChange)
            {
                var c = new ComboBox { FontSize = 11.5, Height = 22, Padding = new Thickness(4, 1, 4, 1), Margin = new Thickness(0, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center };
                foreach (var o in options) c.Items.Add(new ComboBoxItem { Content = o.Label, Tag = o.Value });
                c.SelectedItem = c.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == value) ?? c.Items[0];
                c.SelectionChanged += (s, e) => { if (c.SelectedItem is ComboBoxItem i) onChange((string)i.Tag); };
                return c;
            }
            TextBox Num(int value, int min, int max, Action<int> onChange)
            {
                var t = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture), Width = 42, Height = 22, FontSize = 11.5, Margin = new Thickness(0, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center };
                void Commit()
                {
                    if (!int.TryParse(t.Text.Replace('−', '-'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) v = min > 0 ? min : 0;
                    v = Math.Max(min, Math.Min(max, v));
                    t.Text = v.ToString(CultureInfo.InvariantCulture);
                    if (v != value) { value = v; onChange(v); }
                }
                t.LostKeyboardFocus += (s, e) => Commit();
                t.KeyDown += (s, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
                return t;
            }
            void Set(Action apply) { apply(); changed(); }

            var helpButton = new Button
            {
                Content = "?", Width = 20, Height = 20, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brush(Blue), Cursor = Cursors.Hand,
                ToolTip = T("How repeating works", "Slik virker gjentakelse"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(0)
            };
            helpButton.Template = RoundButtonTemplate(Brush("#dbe8ff"));
            helpButton.Click += (s, e) => ShowRepeatHelp();

            WrapPanel l1 = Line();
            l1.Children.Add(Words(T("Repeat", "Gjenta")));
            l1.Children.Add(Select(new[] { ("", T("Never", "Aldri")), ("day", T("Daily", "Daglig")), ("week", T("Weekly", "Ukentlig")), ("month", T("Monthly", "Månedlig")), ("year", T("Yearly", "Årlig")) },
                r?.Freq ?? "", v =>
                {
                    note.Repeat = v != "" ? new RepeatRule { Freq = v, Every = 1, Days = new List<int> { Keys.Wd(S) }, By = "date", Nth = -1, Wd = Keys.Wd(S), ShiftUnit = "", Shift = 0, ShiftWd = 4, Until = "" } : null;
                    changed();
                }));
            if (r == null) { l1.Children.Add(helpButton); return; }
            l1.Children.Add(Words(T("every", "hver")));
            l1.Children.Add(Num(Math.Max(1, r.Every), 1, 99, v => Set(() => r.Every = v)));
            var unitsOne = new Dictionary<string, (string, string)> { ["day"] = ("day", "dag"), ["week"] = ("week", "uke"), ["month"] = ("month", "måned"), ["year"] = ("year", "år") };
            var unitsMany = new Dictionary<string, (string, string)> { ["day"] = ("days", ". dag"), ["week"] = ("weeks", ". uke"), ["month"] = ("months", ". måned"), ["year"] = ("years", ". år") };
            var unit = r.Every > 1 ? unitsMany[r.Freq] : unitsOne[r.Freq];
            l1.Children.Add(Words(T(unit.Item1, unit.Item2)));
            l1.Children.Add(helpButton);

            if (r.Freq == "week")
            {
                WrapPanel l2 = Line();
                foreach (int wd in wds)
                {
                    bool on = (r.Days ?? new List<int>()).Contains(wd);
                    var b = new Button
                    {
                        Content = notes.WdShort(wd), Width = 26, Height = 20, FontSize = 11, Padding = new Thickness(0), Margin = new Thickness(0, 0, 3, 0), Cursor = Cursors.Hand,
                        Background = on ? Brush(Blue) : Brushes.White, Foreground = on ? Brushes.White : Brushes.Black, BorderBrush = on ? Brush(Blue) : Brush("#999999")
                    };
                    b.Click += (s, e) =>
                    {
                        var d = new List<int>(r.Days ?? new List<int>());
                        if (d.Contains(wd)) { if (d.Count > 1) d.Remove(wd); } else d.Add(wd);
                        Set(() => r.Days = d);
                    };
                    l2.Children.Add(b);
                }
            }
            if (r.Freq == "month")
            {
                WrapPanel l3 = Line();
                l3.Children.Add(Select(new[] { ("date", T("on day " + S.Day, "på dag " + S.Day)), ("1", notes.NthName(1)), ("2", notes.NthName(2)), ("3", notes.NthName(3)), ("4", notes.NthName(4)), ("-1", notes.NthName(-1)) },
                    r.By == "nth" ? r.Nth.ToString(CultureInfo.InvariantCulture) : "date", v =>
                    {
                        if (v == "date") r.By = "date";
                        else { r.By = "nth"; r.Nth = int.Parse(v, CultureInfo.InvariantCulture); }
                        changed();
                    }));
                if (r.By == "nth") l3.Children.Add(Select(wds.Select(wd => (wd.ToString(CultureInfo.InvariantCulture), notes.WdName(wd))), r.Wd.ToString(CultureInfo.InvariantCulture), v => Set(() => r.Wd = int.Parse(v, CultureInfo.InvariantCulture))));
                l3.Children.Add(Words(T("in the month", "i måneden")));
            }

            WrapPanel l4 = Line();
            l4.Children.Add(Words(T("Move", "Flytt")));
            if (r.ShiftUnit == "days" || r.ShiftUnit == "work") l4.Children.Add(Num(r.Shift, -60, 60, v => Set(() => r.Shift = v)));
            l4.Children.Add(Select(new[] { ("", T("no", "nei")), ("days", T("days", "dager")), ("work", T("work days", "arbeidsdager")), ("next", T("to the first", "til første")) },
                r.ShiftUnit ?? "", v => Set(() => r.ShiftUnit = v)));
            if (r.ShiftUnit == "next")
            {
                l4.Children.Add(Select(wds.Select(wd => (wd.ToString(CultureInfo.InvariantCulture), notes.WdName(wd))), r.ShiftWd.ToString(CultureInfo.InvariantCulture), v => Set(() => r.ShiftWd = int.Parse(v, CultureInfo.InvariantCulture))));
                l4.Children.Add(Words(T("after", "etter")));
            }

            WrapPanel l0 = Line();
            l0.Children.Add(Words(T("Starts", "Starter")));
            DatePicker start = DateField(null, null);
            start.SelectedDate = Keys.Parse(Keys.DayOf(note.Date));
            start.Margin = new Thickness(0, 0, 4, 0);
            start.SelectedDateChanged += (s, e) =>
            {
                if (start.SelectedDate.HasValue && editor?.ChangeStart != null)
                {
                    string v = Keys.Key(start.SelectedDate.Value);
                    Dispatcher.BeginInvoke(new Action(() => editor?.ChangeStart(v)));
                }
            };
            l0.Children.Add(start);
            l0.Children.Add(Words(T("until", "til og med")));
            DatePicker until = DateField(Keys.Parse(Keys.DayOf(note.Date)), null);
            until.SelectedDate = string.IsNullOrEmpty(r.Until) ? (DateTime?)null : Keys.Parse(r.Until);
            until.Margin = new Thickness(0, 0, 4, 0);
            until.SelectedDateChanged += (s, e) =>
            {
                string v = until.SelectedDate.HasValue ? Keys.Key(until.SelectedDate.Value) : "";
                if (v != "" && Keys.Compare(v, Keys.DayOf(note.Date)) < 0) v = "";
                if (v != (r.Until ?? "")) Set(() => r.Until = v);
            };
            l0.Children.Add(until);
            if (string.IsNullOrEmpty(r.Until)) l0.Children.Add(Words(T("(no end)", "(uten slutt)"), "#777777"));

            List<string> next = notes.NextOccurrences(note.Date, r, 4);
            panel.Children.Add(new TextBlock
            {
                Text = "↻ " + System.Text.RegularExpressions.Regex.Replace(notes.Summary(note.Date, r), @"\.?$", ". ") +
                       (next.Count > 0 ? T("Next: ", "Neste: ") + string.Join(", ", next.Select(notes.ShortDate)) : T("No more days.", "Ingen flere dager.")),
                FontSize = 11, Foreground = Brush("#333333"), TextWrapping = TextWrapping.Wrap
            });
        }

        // ---------- The ? in the repeat panel: what each choice does, with examples ----------

        private Border repeatHelp, repeatHelpBox;

        private void ShowRepeatHelp()
        {
            CloseRepeatHelp();
            bool no = T("", "x") == "x";
            string[] html = no ? new[]
            {
                "<h2>Slik virker gjentakelse</h2>",
                "<p><b>Gjenta</b> daglig, ukentlig, månedlig eller årlig. «Hver 2.» betyr annenhver gang. Ukentlig: velg en eller flere ukedager. Månedlig: <i>på dag 15</i> (samme dato som notatet) eller <i>første … siste</i> ukedag i måneden.</p>",
                "<p><b>Flytt</b> skyver hver dato etterpå:</p>",
                "<li><i>dager</i>: vanlige kalenderdager. Minus flytter bakover (−2 = to dager før).</li>",
                "<li><i>arbeidsdager</i>: hopper over helger og røde dager i kalenderen (helligdager, påske, jul). 0 = neste arbeidsdag hvis datoen er en fridag.</li>",
                "<li><i>til første … etter</i>: neste valgte ukedag, uansett helligdager.</li>",
                "<p><b>Til og med</b>: siste dato. Tom = uten slutt.</p>",
                "<p><b>Neste:</b> nederst i panelet viser de neste datoene. Sjekk dem når du lager en regel.</p>",
                "<h3>Eksempler</h3>",
                "<li><b>Frist 4 arbeidsdager etter siste søndag</b> (cut-off søndag): Månedlig · siste søndag · Flytt 4 arbeidsdager. Velg <i>arbeidsdager</i>, ikke «til første torsdag etter»: torsdagen blir feil i uker med helligdager, for eksempel i jula og påsken.</li>",
                "<li><b>Første arbeidsdag i måneden</b>: lag notatet på den 1., Månedlig · på dag 1 · Flytt 0 arbeidsdager.</li>",
                "<li><b>Annenhver mandag</b>: Ukentlig · hver 2. uke · ma.</li>",
                "<li><b>Bursdag</b>: Årlig.</li>",
                "<h3>Enkeltdager og hele serien</h3>",
                "<p>Åpner du en dag i serien, gjelder det du skriver bare den dagen (for eksempel avkrysninger i en sjekkliste). Dagen blir et eget notat og slettes når den er eldre enn " + KeepText() + " bakover, som andre notater, mens serien fortsetter. <i>Rediger serien</i> endrer tekst, farge og regel for alle dagene, og <i>Starter</i> kan flyttes der. <i>Slett denne dagen</i> hopper over én dag, og <i>Avslutt serien her</i> stopper den fra og med den dagen.</p>"
            } : new[]
            {
                "<h2>How repeating works</h2>",
                "<p><b>Repeat</b> daily, weekly, monthly or yearly. \"Every 2\" means every other time. Weekly: pick one or more weekdays. Monthly: <i>on day 15</i> (the note's date) or the <i>first … last</i> weekday of the month.</p>",
                "<p><b>Move</b> shifts each date afterwards:</p>",
                "<li><i>days</i>: calendar days. Minus moves back (−2 = two days before).</li>",
                "<li><i>work days</i>: skips weekends and red days in the calendar (holidays, Easter, Christmas). 0 = the next work day if the date is a day off.</li>",
                "<li><i>to the first … after</i>: the next chosen weekday, holidays or not.</li>",
                "<p><b>Until</b>: the last date. Empty = no end.</p>",
                "<p><b>Next:</b> at the bottom of the panel shows the coming dates. Check them when you make a rule.</p>",
                "<h3>Examples</h3>",
                "<li><b>Deadline 4 work days after the last Sunday</b> (cut-off Sunday): Monthly · last Sunday · Move 4 work days. Choose <i>work days</i>, not \"to the first Thursday after\": that Thursday is wrong in weeks with holidays, such as Christmas and Easter.</li>",
                "<li><b>First work day of the month</b>: make the note on the 1st, Monthly · on day 1 · Move 0 work days.</li>",
                "<li><b>Every other Monday</b>: Weekly · every 2 weeks · Mo.</li>",
                "<li><b>Birthday</b>: Yearly.</li>",
                "<h3>One day or the whole series</h3>",
                "<p>When you open a day of the series, what you write applies to that day only (ticked checklist items, for example). The day becomes a note of its own and is deleted when it is older than " + KeepText() + " back, like other notes, while the series goes on. <i>Edit series</i> changes text, color and rule for all days, and <i>Starts</i> can be moved there. <i>Delete this day</i> skips one day, and <i>End series here</i> stops it from that day on.</p>"
            };
            var body = new StackPanel();
            foreach (string part in html) body.Children.Add(HelpBlock(part));
            var close = new Button { Content = "✕", ToolTip = T("Close", "Lukk"), FontSize = 14, Foreground = Brush("#555555"), Padding = new Thickness(2, 0, 2, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Style = (Style)FindResource("CustomButtonStyle"), Background = Brushes.Transparent };
            close.Click += (s, e) => CloseRepeatHelp();
            var inner = new Grid();
            inner.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 4, 0) });
            inner.Children.Add(close);
            double vw = noteLayer.ActualWidth, vh = noteLayer.ActualHeight;
            repeatHelpBox = new Border
            {
                Background = Brushes.White, BorderBrush = Brush("#888888"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 8, 8), Width = Math.Min(520, vw - 12), MaxHeight = vh - 12, Child = inner,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.3, Direction = 270 }
            };
            TextElement.SetFontSize(repeatHelpBox, 12);
            repeatHelp = new Border { Width = vw, Height = vh, Background = Brush(Color.FromArgb(89, 0, 0, 0)), Child = repeatHelpBox };
            noteLayer.Children.Add(repeatHelp);
        }

        /// <summary>One paragraph of the help: h2, h3, p or li, with &lt;b&gt; and &lt;i&gt; inside.</summary>
        private static TextBlock HelpBlock(string html)
        {
            var m = System.Text.RegularExpressions.Regex.Match(html, @"^<(h2|h3|p|li)>(.*)</\1>$", System.Text.RegularExpressions.RegexOptions.Singleline);
            string tag = m.Success ? m.Groups[1].Value : "p", inner = m.Success ? m.Groups[2].Value : html;
            var block = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 16 };
            switch (tag)
            {
                case "h2": block.FontSize = 14; block.FontWeight = FontWeights.Bold; block.Margin = new Thickness(0, 0, 24, 4); break;
                case "h3": block.FontSize = 12.5; block.FontWeight = FontWeights.Bold; block.Margin = new Thickness(0, 8, 0, 2); break;
                case "li": block.Margin = new Thickness(18, 2, 0, 2); block.Inlines.Add(new System.Windows.Documents.Run("•  ")); break;
                default: block.Margin = new Thickness(0, 3, 0, 3); break;
            }
            bool bold = false, italic = false;
            foreach (string piece in System.Text.RegularExpressions.Regex.Split(inner, "(</?[bi]>)"))
            {
                switch (piece)
                {
                    case "<b>": bold = true; continue;
                    case "</b>": bold = false; continue;
                    case "<i>": italic = true; continue;
                    case "</i>": italic = false; continue;
                }
                if (piece.Length == 0) continue;
                var run = new System.Windows.Documents.Run(piece);
                if (bold) run.FontWeight = FontWeights.Bold;
                if (italic) run.FontStyle = FontStyles.Italic;
                block.Inlines.Add(run);
            }
            return block;
        }

        private void CloseRepeatHelp()
        {
            if (repeatHelp != null) noteLayer.Children.Remove(repeatHelp);
            repeatHelp = null;
            repeatHelpBox = null;
        }

        // =====================================================================================
        // Undo: put notes back as they were
        // =====================================================================================

        /// <summary>The notes of these keys now, and an action that puts them back (null = no note there).</summary>
        private Action Snapshot(IList<string> keys)
        {
            var recs = keys.Select(k => noteStore.Get(k)?.Copy()).ToList();
            return () => { noteStore.PutBack(keys, recs); NotesChanged(); };
        }

        private Border undoBox;
        private DispatcherTimer undoTimer;

        /// <summary>A normal-size window: a small "↶ Undo" button in the top right corner (the message is its
        /// tooltip), so it hides little of the calendar. Maximized: the message and the button at the bottom.</summary>
        private void ShowUndo(string message, Action undo)
        {
            HideUndo();
            bool small = !IsMaximized;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (!small) row.Children.Add(new TextBlock { Text = message, Foreground = Brushes.White, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 10, 0) });
            var b = new Button
            {
                Content = (small ? "↶ " : "") + T("Undo", "Angre"), Foreground = Brushes.White, Background = Brushes.Transparent,
                BorderBrush = Brush(small ? "#888888" : "#aaaaaa"), BorderThickness = new Thickness(1), FontSize = small ? 11 : 12,
                Padding = new Thickness(small ? 6 : 8, 1, small ? 6 : 8, 1), Cursor = Cursors.Hand, ToolTip = small ? message : null
            };
            b.Click += (s, e) => { HideUndo(); undo(); };
            row.Children.Add(b);
            undoBox = new Border { Background = Brush("#333333"), CornerRadius = new CornerRadius(4), Padding = small ? new Thickness(2) : new Thickness(6, 4, 6, 4), Child = row };
            noteLayer.Children.Add(undoBox);
            undoBox.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double vw = noteLayer.ActualWidth, vh = noteLayer.ActualHeight;
            if (small) { Canvas.SetLeft(undoBox, vw - undoBox.DesiredSize.Width - 3); Canvas.SetTop(undoBox, 3); }
            else { Canvas.SetLeft(undoBox, (vw - undoBox.DesiredSize.Width) / 2); Canvas.SetTop(undoBox, vh - undoBox.DesiredSize.Height - 8); }
            undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            undoTimer.Tick += (s, e) => HideUndo();
            undoTimer.Start();
        }

        private void HideUndo()
        {
            undoTimer?.Stop();
            if (undoBox != null) noteLayer.Children.Remove(undoBox);
            undoBox = null;
        }

        // =====================================================================================
        // Drag with the mouse: move a note to other days, or drag its first/last day's edge to change its length.
        // Hand cursor over a note, ↔ at the outer edges. A drag starts after a few pixels, so a click stays a
        // click. Dashed frame on the days it would cover (red if it cannot go there). Esc cancels. Repeating notes
        // are not dragged (change them in the ↻ panel). Dragging an edge into another week copies the note to the
        // same weekdays there; Ctrl stretches it instead.
        // =====================================================================================

        private sealed class Block { public string S, E, Key; }

        private sealed class DragTarget
        {
            public string S, E;
            public List<Block> Blocks;
            public bool Ok;
        }

        private sealed class DragState
        {
            public string Mode, K, S, E, From, Hk;
            public Point Start;
            public bool Active, Ctrl;
            public DragTarget Target;
        }

        private DragState drag;
        private const int MaxCopyWeeks = 26;
        private readonly List<Rectangle> dropMarks = new List<Rectangle>();

        /// <summary>What a press at this point of this cell would do: move, end or start, with the note; or null.
        /// A day with two notes: the top half grabs the top note, the bottom half the bottom one.</summary>
        private (string Mode, Showing A)? DragMode(DayCell cell, Point p)
        {
            Showing[] pair = notes.ShownAt(cell.Key);
            double w = cell.Border.ActualWidth, h = cell.Border.ActualHeight;
            Showing a = pair[0] != null && pair[1] != null ? pair[p.Y > h / 2 ? 1 : 0] : pair[0] ?? pair[1];
            if (a == null || !notes.Index.TryGetValue(a.K, out Note n) || n.Repeat != null) return null;
            double edge = Math.Max(4, w * 0.2);
            if (cell.Key == a.E && p.X > w - edge) return ("end", a);
            if (cell.Key == a.S && p.X < edge) return ("start", a);
            return ("move", a);
        }

        private void StartDrag(DayCell cell, MouseButtonEventArgs e)
        {
            if (!NotesOn || editor != null) return;
            var m = DragMode(cell, e.GetPosition(cell.Border));
            if (m == null) return;
            Showing a = m.Value.A;
            drag = new DragState
            {
                Mode = m.Value.Mode, K = a.K, S = a.S, E = a.E, From = cell.Key, Start = e.GetPosition(this),
                Ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0
            };
        }

        private DayCell CellAt(Point windowPoint)
        {
            DayCell found = null;
            VisualTreeHelper.HitTest(this, null, result =>
            {
                for (DependencyObject d = result.VisualHit; d != null; d = VisualTreeHelper.GetParent(d))
                {
                    if (d is Border b && dayCells.TryGetValue(b, out DayCell c)) { found = c; return HitTestResultBehavior.Stop; }
                }
                return HitTestResultBehavior.Continue;
            }, new PointHitTestParameters(windowPoint));
            return found;
        }

        private void Notes_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!NotesOn) return;
            if (drag == null)
            {
                // The cursor shows what a press would do
                if (editor != null || e.LeftButton == MouseButtonState.Pressed) return;
                DayCell cell = CellAt(e.GetPosition(this));
                if (cell == null) return;
                var m = DragMode(cell, e.GetPosition(cell.Border));
                cell.Border.Cursor = m == null ? Cursors.Hand : m.Value.Mode == "move" ? Cursors.SizeAll : Cursors.SizeWE;
                return;
            }
            if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }
            Point p = e.GetPosition(this);
            if (!drag.Active)
            {
                if (Math.Abs(p.X - drag.Start.X) + Math.Abs(p.Y - drag.Start.Y) < 6) return;
                drag.Active = true;
                pressedCell = null;
                ClosePeek();
                CaptureMouse();
                foreach (Border b in dayCells.Keys) if (b.ToolTip is ToolTip t) t.IsOpen = false;
            }
            drag.Ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            DayCell over = CellAt(p);
            if (over == null) return;
            drag.Hk = over.Key;
            Retarget();
        }

        private void Retarget()
        {
            if (drag == null || !drag.Active || drag.Hk == null) return;
            drag.Target = Target(drag, drag.Hk);
            ShowDropPreview(drag.Target);
            Mouse.OverrideCursor = !drag.Target.Ok ? Cursors.No : drag.Mode == "move" ? Cursors.SizeAll : Cursors.SizeWE;
        }

        private static int Weekday(string k) => ((int)Keys.Parse(k).DayOfWeek + 6) % 7;   // Monday = 0
        private static string MondayOf(string k) => Keys.AddDays(k, -Weekday(k));

        /// <summary>Where the drag would put the note: blocks, the first is the note itself, any others are copies.
        /// Dragging an edge into another week copies the note to the same weekdays of each week in between
        /// (Mon-Thu dragged down two rows = Mon-Thu in three weeks). With Ctrl held it grows over every day instead.</summary>
        private DragTarget Target(DragState d, string hk)
        {
            string s = d.S, e = d.E, w0 = MondayOf(d.S), wk = MondayOf(hk);
            List<Block> blocks;
            if (d.Mode != "move" && !d.Ctrl && w0 == MondayOf(d.E) && wk != w0)
            {
                int c0 = Weekday(d.S), c1 = Weekday(d.E);
                if (d.Mode == "end") c1 = Math.Max(c0, Weekday(hk)); else c0 = Math.Min(c1, Weekday(hk));
                int rows = Math.Min(Math.Abs(Keys.DaysBetween(w0, wk)) / 7, MaxCopyWeeks), step = Keys.Compare(wk, w0) > 0 ? 7 : -7;
                blocks = new List<Block>();
                for (int i = 0; i <= rows; i++) blocks.Add(new Block { S = Keys.AddDays(w0, i * step + c0), E = Keys.AddDays(w0, i * step + c1) });
                s = blocks[0].S;
                e = blocks[0].E;
            }
            else
            {
                if (d.Mode == "move") { int off = Keys.DaysBetween(d.From, hk); s = Keys.AddDays(d.S, off); e = Keys.AddDays(d.E, off); }
                else if (d.Mode == "end") e = Keys.Compare(hk, d.S) < 0 ? d.S : hk;
                else s = Keys.Compare(hk, d.E) > 0 ? d.E : hk;
                blocks = new List<Block> { new Block { S = s, E = e } };
            }
            // Each block goes in a free half of its days: the note's own half if it can, else the other one
            bool ok = Keys.DaysBetween(s, e) < NotesEngine.MaxSpanDays;
            foreach (Block b in blocks)
            {
                int sl = ok ? notes.FreeSlot(b.S, b.E, d.K, Keys.SlotOf(d.K)) : -1;
                if (sl < 0) ok = false; else b.Key = Keys.SlotKey(b.S, sl);
            }
            return new DragTarget { S = s, E = e, Blocks = blocks, Ok = ok };
        }

        private void ShowDropPreview(DragTarget t)
        {
            ClearDropPreview();
            Brush stroke = t.Ok ? Brush(Blue) : Brush("#cc0000");
            foreach (DayCell c in dayCells.Values)
            {
                if (!t.Blocks.Any(b => Keys.Compare(c.Key, b.S) >= 0 && Keys.Compare(c.Key, b.E) <= 0)) continue;
                Rect r = CellRect(c.Border);
                var mark = new Rectangle
                {
                    Width = Math.Max(0, r.Width - 4), Height = Math.Max(0, r.Height - 4), Stroke = stroke, StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 2, 1.5 }, IsHitTestVisible = false
                };
                Canvas.SetLeft(mark, r.Left + 2);
                Canvas.SetTop(mark, r.Top + 2);
                noteLayer.Children.Add(mark);
                dropMarks.Add(mark);
            }
        }

        private void ClearDropPreview()
        {
            foreach (Rectangle m in dropMarks) noteLayer.Children.Remove(m);
            dropMarks.Clear();
        }

        private void EndDrag()
        {
            ClearDropPreview();
            Mouse.OverrideCursor = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
            drag = null;
        }

        private void FinishDrag(MouseButtonEventArgs e)
        {
            DragState d = drag;
            EndDrag();
            if (d == null || !d.Active) return;   // just a click
            if (d.Target == null || !d.Target.Ok) return;
            if (d.Target.Blocks.Count > 1) { CopyNote(d.K, d.Target.Blocks); return; }
            if (d.Target.Blocks[0].Key == d.K && d.Target.E == d.E) return;
            MoveNote(d.K, d.Target.Blocks[0].Key, d.Target.E, d.Mode == "move" ? T("Note moved.", "Notatet er flyttet.") : T("Note changed.", "Notatet er endret."));
        }

        /// <summary>nk: the note's new key (first day and half).</summary>
        private void MoveNote(string k, string nk, string e, string message)
        {
            Note n = noteStore.Get(k)?.Copy();
            if (n == null) return;
            string beforeDate = n.Date, beforeEnd = n.EndDate;
            Relocate(n, Keys.DayOf(nk), e, Keys.SlotOf(nk));
            ShowUndo(message, () =>
            {
                Note now = noteStore.Get(n.Date)?.Copy();
                if (now != null) Relocate(now, Keys.DayOf(beforeDate), string.IsNullOrEmpty(beforeEnd) ? Keys.DayOf(beforeDate) : beforeEnd, Keys.SlotOf(beforeDate));
            });
        }

        /// <summary>The note goes on blocks[0] and a copy (text, color, pictures) on each of the other blocks.</summary>
        private void CopyNote(string k, List<Block> blocks)
        {
            Note n = noteStore.Get(k)?.Copy();
            if (n == null) return;
            string beforeDate = n.Date, beforeEnd = string.IsNullOrEmpty(n.EndDate) ? Keys.DayOf(n.Date) : n.EndDate;
            var copies = blocks.Skip(1).ToList();
            Relocate(n, blocks[0].S, blocks[0].E, Keys.SlotOf(blocks[0].Key));
            foreach (Block b in copies)
            {
                var c = new Note { Date = b.Key, Text = n.Text, Color = n.Color, Images = new List<string>(n.Images) };
                if (Keys.Compare(b.E, b.S) > 0) c.EndDate = b.E;
                noteStore.Put(c);
            }
            NotesChanged();
            ShowUndo(T("Note copied to " + copies.Count + (copies.Count == 1 ? " more week." : " more weeks."),
                       "Notatet er kopiert til " + copies.Count + (copies.Count == 1 ? " uke til." : " uker til.")), () =>
            {
                noteStore.DeleteMany(copies.Select(b => b.Key));
                notes.Rebuild();
                Note now = noteStore.Get(n.Date)?.Copy();
                if (now != null) Relocate(now, Keys.DayOf(beforeDate), beforeEnd, Keys.SlotOf(beforeDate));
                else NotesChanged();
            });
        }

        /// <summary>Stores note n on days s..e in half sl (default: the half it is in). Its key may change with it.</summary>
        private void Relocate(Note n, string s, string e, int? sl = null)
        {
            string old = n.Date;
            n.Date = Keys.SlotKey(s, sl ?? Keys.SlotOf(old));
            n.EndDate = Keys.Compare(e, s) > 0 ? e : null;
            if (old != n.Date) noteStore.Delete(old);
            noteStore.Put(n);
            NotesChanged();
        }

        // =====================================================================================
        // Texts
        // =====================================================================================

        private CultureInfo DateCulture => settingsManager.UseNorwegianTexts ? new CultureInfo("nb-NO") : new CultureInfo("en-GB");

        /// <summary>"Torsdag 30. oktober 2026" (short: "Tor. 30. okt. 2026").</summary>
        private string DayTitle(DateTime d, bool shortForm)
        {
            string format = settingsManager.UseNorwegianTexts ? (shortForm ? "ddd d. MMM yyyy" : "dddd d. MMMM yyyy") : (shortForm ? "ddd d MMM yyyy" : "dddd d MMMM yyyy");
            string s = d.ToString(format, DateCulture).Replace("..", ".");
            return s.Length > 0 ? char.ToUpper(s[0], DateCulture) + s.Substring(1) : s;
        }

        /// <summary>"12 weeks" / "12 uker", for the texts that say how long notes are kept.</summary>
        private string KeepText()
        {
            int w = noteStore?.KeepWeeks ?? NotesEngine.KeepDefault;
            return w == 0 ? T("this week", "denne uken") : w == 1 ? T("1 week", "1 uke") : T(w + " weeks", w + " uker");
        }

        /// <summary>"for 12 weeks back" / "i 12 uker bakover" ("this week only" for 0).</summary>
        private string KeptFor()
        {
            int w = noteStore?.KeepWeeks ?? NotesEngine.KeepDefault;
            return w == 0 ? T("this week only", "bare denne uken") : T("for " + KeepText() + " back", "i " + KeepText() + " bakover");
        }

        private string ColorDefault(string id)
        {
            NoteColors.Entry c = NoteColors.All.FirstOrDefault(x => x.Id == id);
            return c != null ? T(c.En, c.No) : T("No color", "Uten farge");
        }

        /// <summary>A color's own name (e.g. "Prosjekt A"), or its plain name.</summary>
        private string ColorLabel(string id)
        {
            string key = string.IsNullOrEmpty(id) ? "none" : id;
            return noteStore.ColorNames.TryGetValue(key, out string name) && !string.IsNullOrWhiteSpace(name) ? name.Trim() : ColorDefault(id);
        }
    }
}
