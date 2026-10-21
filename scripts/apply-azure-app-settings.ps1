# Applies App Service settings from azure-app-settings.json (placeholders must be replaced first).
# Usage:
#   .\scripts\apply-azure-app-settings.ps1 -ResourceGroup AIDevelopment -WebAppName africanideasfaceapi

param(
    [Parameter(Mandatory = $true)]
    [string] $ResourceGroup,

    [Parameter(Mandatory = $true)]
    [string] $WebAppName,

    [string] $SettingsFile = (Join-Path $PSScriptRoot "..\azure-app-settings.json")
)

if (-not (Test-Path $SettingsFile)) {
    Write-Error "Settings file not found: $SettingsFile"
    exit 1
}

$entries = Get-Content $SettingsFile -Raw | ConvertFrom-Json
$settings = @{}

foreach ($entry in $entries) {
    if ($entry.value -match '^YOUR_') {
        Write-Warning "Skipping placeholder setting: $($entry.name)"
        continue
    }
    $settings[$entry.name] = [string]$entry.value
}

if ($settings.Count -eq 0) {
    Write-Error "No non-placeholder settings to apply. Edit azure-app-settings.json first."
    exit 1
}

$pairs = foreach ($key in $settings.Keys) {
    "$key=$($settings[$key])"
}

az webapp config appsettings set `
    --resource-group $ResourceGroup `
    --name $WebAppName `
    --settings @pairs

Write-Host "Applied $($settings.Count) App Service settings to $WebAppName."
