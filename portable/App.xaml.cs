using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace QuickCal
{
    /// <summary>
    /// Replaces the UWP App class.
    /// - Only one QuickCal runs at a time. Starting it again just shows the running window.
    /// - "--tray" (used by autostart) starts QuickCal hidden, with only the week icon in the notification area.
    /// - Closing the window hides it; "Exit" in the tray menu quits for real.
    /// - Unexpected errors: QuickCal keeps running if it can, otherwise it restarts itself (see "Safety net").
    /// </summary>
    public partial class App : Application
    {
        public const string TrayArgument = "--tray";
        private const string RestartedArgument = "--restarted";

        /// <summary>A restarted QuickCal that crashes again within this time is not restarted again.</summary>
        private static readonly TimeSpan MinimumUptimeBeforeRestart = TimeSpan.FromMinutes(5);

        private const string MutexName = @"Local\QuickCalPortable_SingleInstance_7F3A2C1E";
        private const string ShowEventName = @"Local\QuickCalPortable_Show_7F3A2C1E";

        private Mutex singleInstanceMutex;
        private bool ownsMutex;
        private EventWaitHandle showEvent;
        private RegisteredWaitHandle showWaitRegistration;
        private TrayIconManager trayIcon;
        private MainWindow mainWindow;
        private readonly Stopwatch uptime = Stopwatch.StartNew();
        private bool startedAfterCrash;
        private volatile bool mainWindowVisible;
        private int restartStarted;

        public static new App Current => (App)Application.Current;
        public SettingsManager Settings { get; private set; }
        public bool IsExiting { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            InstallSafetyNet();

            startedAfterCrash = e.Args.Any(a => string.Equals(a, RestartedArgument, StringComparison.OrdinalIgnoreCase));

            // Single instance: if QuickCal is already running, ask it to show itself and quit.
            singleInstanceMutex = new Mutex(true, MutexName, out ownsMutex);
            if (!ownsMutex && startedAfterCrash)
            {
                // Restarted after a crash: the old QuickCal is still closing down, wait for it
                ownsMutex = WaitForPreviousInstanceToExit();
            }
            if (!ownsMutex)
            {
                try
                {
                    using (var existing = EventWaitHandle.OpenExisting(ShowEventName))
                    {
                        existing.Set();
                    }
                }
                catch (Exception)
                {
                    // The other instance is shutting down; nothing to do.
                }
                Shutdown();
                return;
            }

            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            showWaitRegistration = ThreadPool.RegisterWaitForSingleObject(
                showEvent,
                (state, timedOut) => Dispatcher.BeginInvoke(new Action(ShowMainWindow)),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);

            Settings = new SettingsManager();
            Settings.Load();

            // A portable app can be moved: keep the autostart entry pointing at this .exe.
            AutostartManager.RefreshPathIfEnabled();

            mainWindow = new MainWindow(Settings);
            mainWindow.IsVisibleChanged += (s, args) => mainWindowVisible = mainWindow.IsVisible;

            trayIcon = new TrayIconManager(Settings, ShowMainWindow, ExitApplication);
            trayIcon.DateChanged += (s, args) => mainWindow.RefreshCalendar();

            bool startInTray = e.Args.Any(a => string.Equals(a, TrayArgument, StringComparison.OrdinalIgnoreCase));
            if (!Settings.Values.WelcomeShown)
            {
                // First time QuickCal runs here: show the "Getting started" page once
                ShowMainWindow();
                mainWindow.ShowWelcomePage();
                Settings.Values.WelcomeShown = true;
                Settings.Save();
            }
            else if (!startInTray)
            {
                ShowMainWindow();
            }
        }

        // =====================================================================================
        // Safety net (no log files):
        // - Errors on the UI thread (almost all): ignored, QuickCal keeps running.
        // - Errors on other threads: .NET always ends the app, so QuickCal starts itself again,
        //   so the week icon comes back. If it crashes again shortly after such a restart, it just closes.
        // =====================================================================================

        private void InstallSafetyNet()
        {
            DispatcherUnhandledException += (s, args) =>
            {
                // If QuickCal never finished starting (no week icon yet), let it end and restart instead
                // of running on invisibly
                if (trayIcon != null) args.Handled = true;
            };
            TaskScheduler.UnobservedTaskException += (s, args) => args.SetObserved();
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.IsTerminating) RestartAfterCrash();
            };
        }

        private void RestartAfterCrash()
        {
            // Only once, never while quitting on purpose, and never in a crash loop
            if (Interlocked.Exchange(ref restartStarted, 1) == 1) return;
            if (IsExiting || !ownsMutex) return;
            if (startedAfterCrash && uptime.Elapsed < MinimumUptimeBeforeRestart) return;

            try
            {
                // Remove the old week icon right away instead of leaving a dead one by the clock
                trayIcon?.HideIcon();
            }
            catch (Exception)
            {
            }

            try
            {
                string exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                // Come back the way the user left it: window open, or only the week icon
                string arguments = mainWindowVisible ? RestartedArgument : TrayArgument + " " + RestartedArgument;
                Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false });
            }
            catch (Exception)
            {
                // Could not restart; nothing more to do.
            }
        }

        private bool WaitForPreviousInstanceToExit()
        {
            try
            {
                // Generous: Windows may keep the crashed process open for a while (error reporting)
                return singleInstanceMutex.WaitOne(TimeSpan.FromMinutes(2));
            }
            catch (AbandonedMutexException)
            {
                // The old QuickCal ended without releasing it; we own it now.
                return true;
            }
        }

        public void ShowMainWindow()
        {
            if (mainWindow == null || IsExiting) return;

            mainWindow.ResetToToday();
            if (!mainWindow.IsVisible) mainWindow.Show();
            if (mainWindow.WindowState == WindowState.Minimized) mainWindow.WindowState = WindowState.Normal;
            mainWindow.Activate();
        }

        /// <summary>Called by the main window after it has been hidden (closed with X).</summary>
        public void OnMainWindowHidden()
        {
            if (!Settings.Values.TrayHintShown)
            {
                trayIcon?.ShowHint(Settings.TrayHiddenHint());
                Settings.Values.TrayHintShown = true;
                Settings.Save();
            }
        }

        /// <summary>Called after the settings page is saved (language may have changed).</summary>
        public void OnSettingsChanged()
        {
            trayIcon?.Refresh(force: true);
        }

        public void ExitApplication()
        {
            IsExiting = true;
            mainWindow?.SaveWindowPlacement();
            Settings?.Save();
            mainWindow?.Close();
            Shutdown();
        }

        /// <summary>Windows is logging off or shutting down: quit without blocking it.</summary>
        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            IsExiting = true;
            mainWindow?.SaveWindowPlacement();
            Settings?.Save();
            base.OnSessionEnding(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            trayIcon?.Dispose();
            showWaitRegistration?.Unregister(null);
            showEvent?.Dispose();
            if (singleInstanceMutex != null)
            {
                if (ownsMutex)
                {
                    try { singleInstanceMutex.ReleaseMutex(); } catch (Exception) { }
                }
                singleInstanceMutex.Dispose();
            }
            base.OnExit(e);
        }
    }
}
