# QuickCal

A quick ISO week calendar for Windows with Norwegian holidays and school vacations. QuickCal shows three months at a glance, the whole year when maximized, and keeps the current week number in view all day.

QuickCal is free, open source and needs no admin rights. It comes in two versions:

| | **QuickCal Web** | **QuickCal Portable** |
|---|---|---|
| Get it | Open **https://dooniem.github.io/QuickCal/** | Download `QuickCal.exe` from [Releases](../../releases) |
| Install | One click, as an app in Microsoft Edge or Google Chrome | Nothing to install. Runs from any folder or USB stick |
| Week number always visible | On the QuickCal icon on the taskbar | On an icon by the clock |
| Start automatically | Yes, turned on in the browser's app settings | Yes, one click in the app |
| Always on top | No (browsers don't allow it) | Yes, in a compact two-month view |

## Features

- **ISO 8601 week numbers** on every row, including week 53.
- **Three months** in the normal window, with the current month in the middle.
- **The whole year** when the window is maximized. Change year with the arrows or the ← / → keys, and press Space to jump back to today.
- **Norwegian holidays**: New Year's Day, Easter, Labour Day, Constitution Day (17 May), Ascension, Whitsun and Christmas, with names as tooltips. Easter can optionally be shown as a full week off.
- **Summer vacation** of 1 to 3 weeks marked in the calendar.
- **English or Norwegian**, or the language of Windows or the browser.

## QuickCal Web

1. Open **https://dooniem.github.io/QuickCal/** in **Microsoft Edge** (recommended) or Google Chrome.
2. The *Getting started* page opens. Click **Install** and confirm.
3. QuickCal opens in its own window. Right-click its icon on the taskbar and choose **Pin to taskbar**.
4. On the first start, QuickCal shows how to make it start automatically when you sign in.

Edge is recommended because it is already on every Windows PC and gives the app a clean window. Chrome adds a large *Uninstall* button to the app's title bar that cannot be hidden.

If the button says *Show me how* instead of *Install*, QuickCal explains the steps for your browser. Installing is not possible in InPrivate or Incognito windows, and IT departments can block it on work PCs. The calendar still works as a normal web page.

**Uninstall:** open **Settings** in QuickCal and click **Uninstall**, or right-click QuickCal in the Windows Start menu and choose **Uninstall**.

Norwegian instructions you can share with colleagues: [INSTALLASJON.md](INSTALLASJON.md).

## QuickCal Portable

1. Download `QuickCal.exe` from the latest [release](../../releases).
2. Put it in any folder and double-click it. No installation and no .NET runtime needed.
3. The *Getting started* page shows how to turn on autostart and keep the week icon visible by the clock.

Closing the window hides QuickCal to the icon by the clock. Right-click the icon and choose **Exit** to quit.

> **Windows SmartScreen:** Windows may warn you the first time you run `QuickCal.exe`. Choose *More info → Run anyway*. On work PCs, your IT department decides whether the app may run.

**Uninstall:** turn off autostart in **Settings**, choose **Exit** from the icon by the clock, and delete `QuickCal.exe` and `QuickCal.settings.json`.

## Privacy

QuickCal collects no data. The portable app makes no network connections. The web app only loads its own files from GitHub Pages, and your settings are stored in your browser.

## History

QuickCal started as a UWP app on the [Microsoft Store](https://apps.microsoft.com/detail/9n2v3z21f5qv), where it is still available. Many organizations now block the Microsoft Store, so version 2.0 was rewritten as a portable Windows app and a web app. It also fixes several bugs from the Store version, including summer vacation shown a week early in some years, a missing week 53 icon, and wrong Easter dates in some years.

## Building from source

The portable app is a WPF project for .NET 10 in [`portable`](portable). Open `QuickCal.csproj` in Visual Studio 2026, or run:

```
dotnet publish portable/QuickCal.csproj -c Release -p:PublishProfile=Portable
```

The result is `portable/publish/QuickCal.exe`. Releases are built by GitHub Actions from the code in this repository. The web app is the static site in [`docs`](docs), served by GitHub Pages.

## Author and license

Made by Magnus Petersen. QuickCal is free and open source under the [MIT License](LICENSE).
