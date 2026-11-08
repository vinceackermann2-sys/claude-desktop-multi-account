# Claude Desktop: several accounts at the same time (Windows)

Run two or more Claude Desktop accounts side by side, for example personal and work. Each account gets its own window, its own sign-in, and its own numbered taskbar button.

> Unofficial community project. Not made by or affiliated with Anthropic.

## How it works

Claude Desktop is an Electron app, and Electron apps accept a `--user-data-dir=<folder>` option. Each folder is a completely separate profile with its own sign-in, settings and local data. Each profile also gets its own "only one copy may run" lock, so a second copy can run next to your normal one.

| | Profile folder |
|---|---|
| Account 1 (your normal Claude app) | Claude's default folder, unchanged |
| Account 2 | `%LOCALAPPDATA%\ClaudeProfile-Account2` |
| Account 3 | `%LOCALAPPDATA%\ClaudeProfile-Account3` |

With the Microsoft Store (MSIX) build of Claude, Windows physically stores these folders inside Claude's own app storage (`%LOCALAPPDATA%\Packages\Claude_…\LocalCache\Local\`), but they still show up at the paths above. As a side effect, **uninstalling or resetting Claude Desktop also deletes the extra accounts' profiles**, and you'll need to sign in to them again.

The rest of this repo makes that convenient: a small launcher per account that finds the installed Claude, opens it with the right profile (or focuses it if it's already open), and gives it its own taskbar button.

## Quick try (nothing installed)

Paste this into Windows PowerShell to open Claude as a separate "Account 2":

```powershell
$claude = Join-Path (Get-AppxPackage -Name Claude | Sort-Object Version -Descending | Select-Object -First 1).InstallLocation 'app\Claude.exe'
Start-Process $claude -ArgumentList "--user-data-dir=`"$env:LOCALAPPDATA\ClaudeProfile-Account2`""
```

Sign in with your second account in the window that opens. That's the whole trick. The steps below just make it a proper Start menu and taskbar app.

## Install the launchers

**You need:** Windows 10 or 11, Claude Desktop installed (tested with the current Microsoft Store / MSIX build), and Windows PowerShell, which is built in. You don't need Visual Studio, the .NET SDK, or anything else from the internet: the launcher is compiled from the source in this repo by the C# compiler that ships with Windows.

1. Get this repo: **Code → Download ZIP** and unzip it, or `git clone` it.
2. Open **Windows PowerShell** in that folder and run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install.ps1 -Account 2
   ```

   For more accounts use `-Account 2,3` and so on, up to 99. Add `-NoDesktopShortcut` if you only want the Start menu entry.
   `-ExecutionPolicy Bypass` applies to that one command only. It does not change your system's settings.

3. Open **Claude - Account 2** from the Start menu and sign in with your second account.
4. Optional: right-click it in the Start menu and choose **Pin to taskbar**.

Your normal Claude app stays account 1. The launchers live in `%USERPROFILE%\ClaudeMultiAccount`. Keep that folder where it is, because the shortcuts point to it.

## Everyday use

- **Minimize, don't close.** Closing Claude with X hides it to the tray and it keeps running. Forcing a hidden Claude window back into view leaves it unresponsive, so the launcher doesn't do that. Instead it asks whether to restart that account (default: No). A restart stops anything running in that account but keeps its sign-in.
- **Updates are fine.** The launcher looks up the current Claude install every time, so shortcuts keep working after Claude updates itself.
- **Clicking the launcher again** brings that account's window to the front instead of starting a second copy.
- **Signing in:** do it from the account's own window. If a sign-in step opens your web browser, the link back into the app may land in your *normal* Claude window instead. Windows sends `claude://` links to the default profile, and this project deliberately doesn't change that handler. If you find a reliable way around this, please open an issue.
- **Claude Code (the Code tab)** keeps its settings, skills, memory and session history in `%USERPROFILE%\.claude`, and all accounts share that folder. Keep that in mind if you separate work and personal accounts.

## Uninstall

Unpin any taskbar buttons first (right-click → *Unpin from taskbar*), then:

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

This removes the launchers and shortcuts. Each account's profile folder (its sign-in and settings) is **kept** unless you add `-RemoveProfileData`. That option asks before deleting anything, and only works while the account is closed.

## Safety

Read [SECURITY.md](SECURITY.md) for exactly what this touches. In short:

- **No binaries in this repo, on purpose.** You compile the launcher yourself from [`src/ClaudeAccountLauncher.cs`](src/ClaudeAccountLauncher.cs), one readable file. Don't run a prebuilt `ClaudeAccount*.exe` that someone sends you.
- The launcher never reads, copies or sends sign-in data, never changes Claude's own files, and never uses the network.
- **Your profile folders are your sign-ins.** Anyone who gets a copy of `ClaudeProfile-AccountN` may be able to use that account. Don't share them, sync them to cloud storage, or commit them anywhere.

## Troubleshooting

- **"Could not find Claude Desktop"**: open the normal Claude app once and try again. If Claude is installed somewhere unusual, create `claude-path.txt` next to the launcher containing the full path to `Claude.exe`.
- **Accounts share one taskbar button**: click that account's launcher once more. That re-tags its windows.
- **See what the launcher sees**:

  ```powershell
  & "$env:USERPROFILE\ClaudeMultiAccount\ClaudeAccount2.exe" --status | Write-Output
  ```

- **Errors** are written to `launcher.log` in the launcher folder.

## Fair use

This is meant for people who have more than one Claude account of their own, such as personal and work. Each account is still subject to Anthropic's terms. This project doesn't bypass any limits or share anything between accounts.

## License

[MIT](LICENSE)
