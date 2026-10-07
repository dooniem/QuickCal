# QuickCal

A quick ISO week calendar for Windows with Norwegian holidays and school vacations. QuickCal shows three months at a glance, the whole year when maximized, and keeps the current week number right next to the clock.

QuickCal comes in two versions:

| | **QuickCal Portable** (Windows) | **QuickCal Web** (browser) |
|---|---|---|
| Get it | Download `QuickCal.exe` from [Releases](../../releases) | Open **https://dooniem.github.io/QuickCal/** |
| Installation | None. One file, runs from any folder or USB stick | None. Optionally install as an app from Edge or Chrome |
| Admin rights | Not needed | Not needed |
| Week number always visible | Icon with the week number in the notification area, by the clock | Badge on the QuickCal taskbar icon (installed app) |
| Always on top | Yes (compact mode) | No (browsers don't allow it) |
| Start automatically | One click in the app | In the browser's app settings (explained in the app) |

## Features

- **ISO 8601 week numbers** on every row, including week 53.
- **Three months** in the normal window, with the current month in the middle.
- **The whole year** (4 × 3 months) when the window is maximized. Change year with the arrows, the ← / → keys, and press Space to jump back to the current year.
- **Norwegian holidays**: New Year's Day, Easter, Labour Day, Constitution Day (17 May), Ascension, Whitsun and Christmas, with names shown as tooltips. Easter can optionally be shown as a full week off.
- **Summer vacation** (1 to 3 weeks, configurable) marked in the calendar.
- **Language**: English, Norwegian, or the Windows/browser language. Month and day names come from the operating system, so every language works.
- **Always on top**: shrinks the window to a compact two-month view that stays above other windows (Portable only).
- **Getting started page** on first run, explaining how to pin the app, turn on autostart and keep the week number visible.

## QuickCal Portable (Windows)

1. Download `QuickCal.exe` from the latest [release](../../releases).
2. Put it in any folder you like and double-click it. No installation and no .NET runtime needed.
3. Follow the *Getting started* page to turn on autostart and keep the week icon visible by the clock.

Closing the window with **X** hides it to the notification area, so the week number stays visible. Right-click the week icon and choose **Exit** to quit.

Settings are stored in `QuickCal.settings.json` next to `QuickCal.exe`. If that folder is read-only, `%AppData%\QuickCal` is used instead. Autostart uses the current user's `Run` registry key, so it needs no admin rights and can be turned off in Task Manager under *Startup apps*.

> **Windows SmartScreen / Smart App Control:** `QuickCal.exe` is not code-signed, so Windows may warn you the first time you run it. Choose *More info → Run anyway*. On managed work PCs, your IT department's policies decide whether unsigned apps may run.

### Build from source

Requirements: Visual Studio 2026 with the **.NET desktop development** workload (includes the .NET 10 SDK).

1. Open `portable/QuickCal.csproj` in Visual Studio and press **F5** to build and run.
2. To create the single portable file, double-click `portable/publish.cmd`, or right-click the project → **Publish** → profile **Portable**.
3. The finished file is `portable/publish/QuickCal.exe`. It is self-contained and is the only file you need to distribute.

Command line alternative:

```
dotnet publish portable/QuickCal.csproj -c Release -p:PublishProfile=Portable
```

## QuickCal Web (browser)

The web version lives in the [`docs`](docs) folder and is published with GitHub Pages. It works offline once opened, and can be installed as an app from Microsoft Edge or Google Chrome by clicking the install icon in the address bar.

To try it locally, open `docs/index.html` in a browser. Installing as an app and the taskbar badge only work when the page is served over HTTPS, for example from GitHub Pages.

When you change the web files, bump `VERSION` at the top of `docs/sw.js` so users get the new version.

## Repository layout

```
quickcal/
├── portable/   QuickCal Portable: WPF / .NET 10 Visual Studio project
└── docs/       QuickCal Web: static site served by GitHub Pages
```

## History

QuickCal started as a UWP app on the Microsoft Store. Version 2.0 is a rewrite as a portable WPF app (no installation, no admin rights) plus a web version, with these fixes over the Store version:

- Summer vacation was shown one week too early in some years (e.g. 2027 and 2028).
- Week 53 had no week icon.
- Easter was wrong in some years (e.g. 2049 and 2076).
- With the system language selected, month names were shifted by one month.
- The app crashed when changing month on the 29th, 30th or 31st.

## Author

Magnus Petersen
