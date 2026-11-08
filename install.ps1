<#
.SYNOPSIS
    Builds launchers that open Claude Desktop as extra accounts, side by side with your normal one.

.DESCRIPTION
    For each account number N (2-99) this script:
      1. draws a numbered icon (AccountN.ico),
      2. compiles src\ClaudeAccountLauncher.cs into ClaudeAccountN.exe with the C# compiler
         that ships with Windows,
      3. adds a "Claude - Account N" shortcut to the Start menu (and the desktop unless
         -NoDesktopShortcut).
    Nothing is downloaded. Claude's own files and your normal Claude account are not changed.
    Running it again for the same number simply rebuilds that launcher.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1 -Account 2

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1 -Account 2,3 -NoDesktopShortcut
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(2, 99)]
    [int[]] $Account,

    # Outside AppData on purpose: when a script runs from inside a packaged (Store) app,
    # Windows can quietly redirect AppData writes into that app's private storage.
    [string] $InstallDir = (Join-Path $env:USERPROFILE 'ClaudeMultiAccount'),

    [switch] $NoDesktopShortcut
)

$ErrorActionPreference = 'Stop'

if ($PSVersionTable.PSEdition -eq 'Core') {
    throw 'Please run this with Windows PowerShell (powershell.exe), not PowerShell 7 (pwsh).'
}

$source = Join-Path $PSScriptRoot 'src\ClaudeAccountLauncher.cs'
if (-not (Test-Path -LiteralPath $source)) {
    throw "Missing $source. Run this script from the repository folder."
}

$package = Get-AppxPackage -Name Claude -ErrorAction SilentlyContinue | Sort-Object Version -Descending | Select-Object -First 1
if (-not $package -and -not (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'AnthropicClaude'))) {
    throw 'Claude Desktop does not seem to be installed for this Windows user. Install it, open it once, then run this again.'
}

# The .NET Framework 4.x C# compiler is part of Windows.
$csc = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    throw "C# compiler not found at $csc. It ships with .NET Framework 4.x, which is part of Windows 10 and 11."
}

Add-Type -AssemblyName System.Drawing

# A rounded square with the account number, saved as a multi-size .ico (PNG images inside).
function New-AccountIcon {
    param([int] $Number, [string] $Path)

    $colors = '#2563EB', '#16A34A', '#9333EA', '#D97706', '#E11D48', '#0D9488', '#475569', '#C026D3'
    $fill = [System.Drawing.ColorTranslator]::FromHtml($colors[($Number - 2) % $colors.Count])
    $images = New-Object System.Collections.Generic.List[byte[]]
    $sizes = 16, 24, 32, 48, 64, 256

    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
            $g.Clear([System.Drawing.Color]::Transparent)

            $d = [Math]::Max(4, [int]($size * 0.44))   # corner diameter
            $e = $size - 1
            $shape = New-Object System.Drawing.Drawing2D.GraphicsPath
            $shape.AddArc(0, 0, $d, $d, 180, 90)
            $shape.AddArc($e - $d, 0, $d, $d, 270, 90)
            $shape.AddArc($e - $d, $e - $d, $d, $d, 0, 90)
            $shape.AddArc(0, $e - $d, $d, $d, 90, 90)
            $shape.CloseFigure()
            $brush = New-Object System.Drawing.SolidBrush $fill
            $g.FillPath($brush, $shape)

            $scale = if ($Number -ge 10) { 0.48 } else { 0.66 }
            $font = New-Object System.Drawing.Font 'Segoe UI', ([float]($size * $scale)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
            $format = New-Object System.Drawing.StringFormat
            $format.Alignment = [System.Drawing.StringAlignment]::Center
            $format.LineAlignment = [System.Drawing.StringAlignment]::Center
            $area = New-Object System.Drawing.RectangleF 0, ([float]($size * 0.03)), $size, $size
            $g.DrawString([string]$Number, $font, [System.Drawing.Brushes]::White, $area, $format)

            $png = New-Object System.IO.MemoryStream
            $bitmap.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
            $images.Add($png.ToArray())
        } finally {
            $g.Dispose()
            $bitmap.Dispose()
        }
    }

    $ico = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $ico
    $writer.Write([UInt16]0)                 # reserved
    $writer.Write([UInt16]1)                 # type: icon
    $writer.Write([UInt16]$images.Count)
    $offset = 6 + 16 * $images.Count
    for ($i = 0; $i -lt $images.Count; $i++) {
        $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }  # 0 means 256
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)               # palette colors
        $writer.Write([byte]0)               # reserved
        $writer.Write([UInt16]1)             # color planes
        $writer.Write([UInt16]32)            # bits per pixel
        $writer.Write([UInt32]$images[$i].Length)
        $writer.Write([UInt32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $writer.Write($image) }
    $writer.Flush()
    [System.IO.File]::WriteAllBytes($Path, $ico.ToArray())
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
$InstallDir = (Resolve-Path -LiteralPath $InstallDir).Path
$shell = New-Object -ComObject WScript.Shell

foreach ($n in $Account) {
    $icon = Join-Path $InstallDir "Account$n.ico"
    $exe = Join-Path $InstallDir "ClaudeAccount$n.exe"

    New-AccountIcon -Number $n -Path $icon

    $compilerOutput = & $csc /nologo /target:winexe /optimize+ "/out:$exe" "/win32icon:$icon" `
        /reference:System.Management.dll /reference:System.Windows.Forms.dll $source
    if ($LASTEXITCODE -ne 0) {
        $compilerOutput | ForEach-Object { Write-Host $_ }
        throw "Building the launcher for account $n failed (see the compiler output above)."
    }

    $folders = @([Environment]::GetFolderPath('Programs'))
    if (-not $NoDesktopShortcut) { $folders += [Environment]::GetFolderPath('Desktop') }

    foreach ($folder in $folders) {
        $lnk = Join-Path $folder "Claude - Account $n.lnk"
        $shortcut = $shell.CreateShortcut($lnk)
        $shortcut.TargetPath = $exe
        $shortcut.WorkingDirectory = $InstallDir
        $shortcut.IconLocation = "$icon,0"
        $shortcut.Description = "Open Claude Desktop as account $n"
        $shortcut.Save()

        $tag = Start-Process -FilePath $exe -ArgumentList '--tag-shortcut', ('"' + $lnk + '"') -Wait -PassThru
        if ($tag.ExitCode -ne 0) {
            Write-Warning "Could not give '$lnk' its own taskbar identity. The shortcut still works. Details are in $InstallDir\launcher.log."
        }
        Write-Host "Created $lnk"
    }
}

Write-Host ''
Write-Host "Launchers are in $InstallDir. Keep that folder where it is; the shortcuts point to it."
Write-Host 'Next:'
Write-Host '  1. Open "Claude - Account N" from the Start menu and sign in with that account.'
Write-Host '  2. To get a taskbar button: right-click it in the Start menu > Pin to taskbar.'
Write-Host 'Your normal Claude app stays account 1.'
