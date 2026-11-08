<#
.SYNOPSIS
    Removes the extra-account launchers and their shortcuts.

.DESCRIPTION
    Removes ClaudeAccountN.exe, its icon and its Start menu / desktop shortcuts. If that account is
    open, its windows go back to Claude's normal taskbar button.

    Each account's profile folder (its sign-in and settings) is KEPT unless you add
    -RemoveProfileData. Deleting it asks for confirmation and only runs while that account is closed.

    Unpin taskbar buttons yourself first (right-click > Unpin from taskbar); Windows has no
    supported way for a script to do that.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\uninstall.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\uninstall.ps1 -Account 3 -RemoveProfileData
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    # Account numbers to remove. Leave out to remove every launcher found in -InstallDir.
    [ValidateRange(2, 99)]
    [int[]] $Account,

    [string] $InstallDir = (Join-Path $env:USERPROFILE 'ClaudeMultiAccount'),

    [switch] $RemoveProfileData
)

$ErrorActionPreference = 'Stop'

$numbers = @($Account)
if (-not $Account) {
    $numbers = @(Get-ChildItem -LiteralPath $InstallDir -Filter 'ClaudeAccount*.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.BaseName -match '^ClaudeAccount(\d+)$' } |
        ForEach-Object { [int]($_.BaseName -replace '^ClaudeAccount', '') })
}
if ($numbers.Count -eq 0) {
    Write-Host "No launchers found in $InstallDir."
    return
}

$folders = [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')

foreach ($n in $numbers) {
    $exe = Join-Path $InstallDir "ClaudeAccount$n.exe"
    $profileDir = Join-Path $env:LOCALAPPDATA "ClaudeProfile-Account$n"
    $flag = '--user-data-dir="?' + [regex]::Escape($profileDir) + '("|\s|$)'
    $running = @(Get-CimInstance Win32_Process -Filter "Name='Claude.exe'" | Where-Object {
        $_.CommandLine -and $_.CommandLine -notmatch '--type=' -and $_.CommandLine -match $flag
    })

    if ($running.Count -gt 0 -and (Test-Path -LiteralPath $exe)) {
        Start-Process -FilePath $exe -ArgumentList '--reset-taskbar' -Wait
    }

    foreach ($folder in $folders) {
        $lnk = Join-Path $folder "Claude - Account $n.lnk"
        if (Test-Path -LiteralPath $lnk) {
            Remove-Item -LiteralPath $lnk -Force
            Write-Host "Removed $lnk"
        }
    }
    foreach ($file in $exe, (Join-Path $InstallDir "Account$n.ico")) {
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
    }
    Write-Host "Removed the account $n launcher."

    if (-not (Test-Path -LiteralPath $profileDir)) { continue }
    if (-not $RemoveProfileData) {
        Write-Host "Kept account $n's sign-in and settings in $profileDir (add -RemoveProfileData to delete them)."
        continue
    }
    if ($running.Count -gt 0) {
        Write-Warning "Account $n is still running, so its data was not deleted. Quit it from its tray icon, then run this again."
        continue
    }
    if ($PSCmdlet.ShouldProcess($profileDir, "Delete account $n's sign-in, settings and local data")) {
        Remove-Item -LiteralPath $profileDir -Recurse -Force
        Write-Host "Deleted $profileDir"
    }
}

# Remove the install folder when nothing of yours is left in it.
if (Test-Path -LiteralPath $InstallDir) {
    $log = Join-Path $InstallDir 'launcher.log'
    if (-not (Get-ChildItem -LiteralPath $InstallDir -Filter 'ClaudeAccount*.exe')) {
        if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force }
        if (-not (Get-ChildItem -LiteralPath $InstallDir -Force)) { Remove-Item -LiteralPath $InstallDir -Force }
    }
}
