# Security and what this project touches

## Why you can trust it (and how to check)

- **Source only.** The repo contains no `.exe`, `.dll` or other binaries. `install.ps1` compiles `src/ClaudeAccountLauncher.cs` on your machine with the C# compiler that is part of Windows (`csc.exe` from .NET Framework 4.x). What runs is what you can read.
- **No network.** Nothing in the launcher or the scripts downloads, uploads or connects to anything.
- **No credentials.** The launcher only passes a folder path to Claude. Claude itself handles sign-in inside its own window, and the launcher never opens the profile folder's contents.
- **No changes to Claude.** Claude's install folder, its default profile and its `claude://` link handler are left alone.

## What it reads

| What | Why |
|---|---|
| The list of running `Claude.exe` processes and their command lines | To find out whether an account is already open, and where Claude is installed |
| `HKCU\Software\Classes\Local Settings\...\AppModel\Repository\Packages` | To find the current Claude install folder after updates |
| Claude's top-level windows (handle, visibility, taskbar identity) | To focus the right window. Window titles and contents are never read |

## What it writes

| What | Where | Removed by `uninstall.ps1` |
|---|---|---|
| Launchers and icons (`ClaudeAccountN.exe`, `AccountN.ico`) | `%USERPROFILE%\ClaudeMultiAccount` | Yes |
| Error log, error messages only (`launcher.log`) | Same folder | Yes |
| Shortcuts `Claude - Account N.lnk` | Start menu, and the desktop unless `-NoDesktopShortcut` | Yes (unpin taskbar buttons yourself) |
| Taskbar identity on that account's open windows (`System.AppUserModel.*`) | In memory, gone when the window closes | Yes, reset |
| Profile folder, created by Claude itself | `%LOCALAPPDATA%\ClaudeProfile-AccountN` (with the Store build, physically inside Claude's package storage) | Only with `-RemoveProfileData` |

## Keeping your accounts safe

- A profile folder contains that account's signed-in session. Treat it like a password: don't zip it up and send it, don't sync it to OneDrive, Dropbox or similar, and never commit it to git.
- Only run launchers that you compiled yourself from this source. If someone offers a ready-made `ClaudeAccount2.exe`, don't run it.
- If you fork this repo, keep the `.gitignore`. It stops build output and logs from being committed by accident.

## Reporting a problem

If you find a security issue, please open an issue without exploit details first and ask for a private contact.
