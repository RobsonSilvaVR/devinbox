# Garante o .NET 10 Desktop Runtime antes de abrir o DevInbox.
# Executado pelo "Iniciar DevInbox.cmd" — sem janelas, instalação silenciosa.

$ErrorActionPreference = 'Stop'

function Test-DesktopRuntime10 {
    try {
        $runtimes = & dotnet --list-runtimes 2>$null
        return [bool]($runtimes | Where-Object { $_ -match '^Microsoft\.WindowsDesktop\.App 10\.' })
    }
    catch {
        return $false
    }
}

if (-not (Test-DesktopRuntime10)) {
    $installed = $false

    if (Get-Command winget -ErrorAction SilentlyContinue) {
        try {
            winget install --id Microsoft.DotNet.DesktopRuntime.10 --architecture x64 `
                --silent --accept-package-agreements --accept-source-agreements | Out-Null
            $installed = Test-DesktopRuntime10
        }
        catch {
            $installed = $false
        }
    }

    if (-not $installed) {
        # Fallback sem winget: instalador oficial da Microsoft em modo silencioso.
        $installer = Join-Path $env:TEMP 'windowsdesktop-runtime-10-win-x64.exe'
        try {
            Invoke-WebRequest 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe' `
                -OutFile $installer -UseBasicParsing
            Start-Process $installer -ArgumentList '/install', '/quiet', '/norestart' -Wait
        }
        finally {
            Remove-Item $installer -Force -ErrorAction SilentlyContinue
        }
    }

    if (-not (Test-DesktopRuntime10)) {
        [System.Reflection.Assembly]::LoadWithPartialName('System.Windows.Forms') | Out-Null
        [System.Windows.Forms.MessageBox]::Show(
            "Não foi possível instalar o .NET 10 Desktop Runtime automaticamente.`n" +
            "Instale manualmente com:  winget install Microsoft.DotNet.DesktopRuntime.10",
            'DevInbox') | Out-Null
        exit 1
    }
}

Start-Process (Join-Path $PSScriptRoot 'DevInbox.exe')
