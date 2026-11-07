# AltWin – Alt Account Switcher

AltWin is a small Windows app for PCs that several people use with their own local accounts.
Tap **Alt** on its own and a blue popup lists the other accounts on the PC. Click **Switch** to go to the sign-in screen for that account.
An admin can add and remove accounts, and share folders between them, for example a game library that a brother's account can also use.
Per-account options include idle lock, daily time limits, allowed sign-in hours and disk limits.
It works on Windows 10 and 11, Home and Pro. It never stores, shows or bypasses passwords: you always sign in on the normal Windows sign-in screen.

## Install

**With winget:**

```powershell
winget install OpiKula2877.AltWin
```

**Or manually:** download `AltWin-Setup-1.0.0.exe` from [Releases](https://github.com/OpiKula2877/AltWin/releases/latest) and run it.

After installing, AltWin starts automatically in every account on the PC. Tap **Left Alt** to open the popup, and open **Settings** from the popup or the tray icon.
To uninstall, go to **Settings > Apps > Installed apps > Alt Account Switcher**.

More details: [docs/DETAILS.md](docs/DETAILS.md)
