using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace QuickCal
{
    /// <summary>
    /// Replaces the UWP taskbar badge and the "UserPresent" background task.
    /// Shows the current ISO week number as an icon in the notification area (next to the clock)
    /// and checks every 30 seconds whether the week or date has changed.
    /// </summary>
    public sealed class TrayIconManager : IDisposable
    {
        private readonly SettingsManager settings;
        private readonly Forms.NotifyIcon notifyIcon;
        private readonly Forms.ToolStripMenuItem openItem;
        private readonly Forms.ToolStripMenuItem exitItem;
        private readonly DispatcherTimer timer;
        private Icon currentIcon;
        private int shownWeek = -1;
        private DateTime lastSeenDate = DateTime.Today;

        /// <summary>Raised when the calendar date changes (e.g. at midnight or after the PC wakes up).</summary>
        public event EventHandler DateChanged;

        public TrayIconManager(SettingsManager settings, Action onOpen, Action onExit)
        {
            this.settings = settings;

            openItem = new Forms.ToolStripMenuItem(string.Empty, null, (s, e) => onOpen());
            exitItem = new Forms.ToolStripMenuItem(string.Empty, null, (s, e) => onExit());
            openItem.Font = new Font(openItem.Font, System.Drawing.FontStyle.Bold);

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(openItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(exitItem);

            notifyIcon = new Forms.NotifyIcon { ContextMenuStrip = menu };
            notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left) onOpen();
            };

            Refresh(force: true);
            notifyIcon.Visible = true;

            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            timer.Tick += (s, e) => Refresh(force: false);
            timer.Start();

            // Update immediately (not just within 30 seconds) when the PC wakes from sleep,
            // the user unlocks the PC, or the clock/date is changed.
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
            Microsoft.Win32.SystemEvents.TimeChanged += OnTimeChanged;
        }

        private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume) RefreshSoon();
        }

        private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock ||
                e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLogon) RefreshSoon();
        }

        private void OnTimeChanged(object sender, EventArgs e) => RefreshSoon();

        /// <summary>System events arrive on another thread; hand the work to the UI thread.</summary>
        private void RefreshSoon()
        {
            timer.Dispatcher.BeginInvoke(new Action(() => Refresh(force: false)));
        }

        public void Refresh(bool force)
        {
            DateTime today = DateTime.Today;
            int week = ISOWeek.GetWeekOfYear(today);

            if (force || week != shownWeek)
            {
                Icon newIcon = WeekIconRenderer.Create(week);
                notifyIcon.Icon = newIcon;
                currentIcon?.Dispose();
                currentIcon = newIcon;
                shownWeek = week;
            }

            notifyIcon.Text = settings.TrayTooltip(week);
            openItem.Text = settings.TrayOpenText();
            exitItem.Text = settings.TrayExitText();

            if (today != lastSeenDate)
            {
                lastSeenDate = today;
                DateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void ShowHint(string text)
        {
            notifyIcon.ShowBalloonTip(4000, "QuickCal", text, Forms.ToolTipIcon.Info);
        }

        /// <summary>Takes the icon away from the notification area (used when QuickCal crashes and restarts).</summary>
        public void HideIcon()
        {
            notifyIcon.Visible = false;
        }

        public void Dispose()
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
            Microsoft.Win32.SystemEvents.TimeChanged -= OnTimeChanged;
            timer.Stop();
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            currentIcon?.Dispose();
        }
    }

    /// <summary>Draws a small icon with the week number (works for week 53 too).</summary>
    public static class WeekIconRenderer
    {
        private static readonly Color Background = Color.FromArgb(255, 0x14, 0x32, 0x75); // same blue as the month headers
        private static readonly Color Frame = Color.FromArgb(255, 103, 125, 225);           // same as the week-number column

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static Icon Create(int week)
        {
            int size = Math.Max(16, Forms.SystemInformation.SmallIconSize.Width);
            string text = week.ToString(CultureInfo.InvariantCulture);

            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);

                    using (var bg = new SolidBrush(Background))
                    {
                        g.FillRectangle(bg, 0, 0, size, size);
                    }
                    float penWidth = Math.Max(1f, size / 16f);
                    using (var pen = new Pen(Frame, penWidth))
                    {
                        g.DrawRectangle(pen, penWidth / 2f, penWidth / 2f, size - penWidth, size - penWidth);
                    }

                    using (var format = new StringFormat(StringFormat.GenericTypographic))
                    {
                        format.Alignment = StringAlignment.Center;
                        format.LineAlignment = StringAlignment.Center;
                        format.FormatFlags |= StringFormatFlags.NoWrap;

                        // Largest bold font that fits inside the frame
                        float fontSize = size * 0.85f;
                        Font font = null;
                        try
                        {
                            while (true)
                            {
                                font?.Dispose();
                                font = new Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
                                SizeF measured = g.MeasureString(text, font, PointF.Empty, format);
                                if ((measured.Width <= size * 0.86f && measured.Height <= size * 0.95f) || fontSize <= 6f) break;
                                fontSize -= 0.5f;
                            }
                            g.DrawString(text, font, Brushes.White, new RectangleF(0, size * 0.03f, size, size), format);
                        }
                        finally
                        {
                            font?.Dispose();
                        }
                    }
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                    {
                        return (Icon)temporary.Clone(); // the clone owns its own handle
                    }
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }
    }
}
