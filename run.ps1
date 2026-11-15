# Start the game in the browser (PowerShell version of run.sh). The database is starship-sim.db in this directory (ignored by git).
# Usage: ./run.ps1   (any arguments are passed to the app)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $env:PATH = "$HOME\.dotnet;$env:PATH"
}

if (-not $env:ASPNETCORE_URLS) {
    $env:ASPNETCORE_URLS = 'http://127.0.0.1:5080'
}

# The content editor (/dev/content) writes files in content/, so it is on only when you run the game from here.
if (-not $env:Web__DeveloperMode) {
    $env:Web__DeveloperMode = 'true'
}

$address = ($env:ASPNETCORE_URLS -split ';')[0]
$editor = if ($env:Web__DeveloperMode -eq 'true') { "the content editor is at $address/dev/content" } else { 'developer mode is off, so there is no content editor' }
$database = if ($env:Web__DatabasePath) { $env:Web__DatabasePath } else { "$PWD\starship-sim.db" }
Write-Host "Starship Sim: $address, $editor (database: $database)"

dotnet run --project src/Web -- @args
exit $LASTEXITCODE
