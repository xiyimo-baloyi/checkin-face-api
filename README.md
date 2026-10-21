# checkin-face-api

Blazor Server attendance and check-in app with Azure Face API integration, leave management, and admin analytics.

## Requirements

- .NET 10 SDK (or the SDK version in `global.json` if present)
- Azure Face API credentials (see `appsettings.template.json`)

## Run locally

```bash
dotnet restore
cp appsettings.template.json appsettings.Local.json
# Edit appsettings.Local.json with your secrets (not committed)
dotnet run
```

## Configuration

Copy `appsettings.template.json` to `appsettings.Local.json` for local secrets. Production settings use environment variables and `appsettings.Production.json`.
