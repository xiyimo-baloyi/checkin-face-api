# checkin-face-api

Blazor Server attendance and check-in app with Azure Face API integration, leave management, and admin analytics.

## Requirements

- .NET 10 SDK (`global.json` pins the SDK; CI uses `10.0.x`)
- Azure Face API, Cosmos DB, and Blob Storage (see `appsettings.template.json`)

## Run locally

```bash
dotnet restore
cp appsettings.template.json appsettings.Local.json
# Edit appsettings.Local.json with your secrets (not committed)
dotnet run
```

## Configuration

Copy `appsettings.template.json` to `appsettings.Local.json` for local secrets. Production uses **Azure App Service application settings** (double underscore maps to JSON sections, e.g. `AzureFaceApi__Key`).

Reference list: `azure-app-settings.json` (replace `YOUR_*` placeholders before applying).

Apply settings with Azure CLI (after editing placeholders):

```powershell
.\scripts\apply-azure-app-settings.ps1 -ResourceGroup AIDevelopment -WebAppName africanideasfaceapi
```

Or in Portal: **App Service → Settings → Environment variables**.

Required in Production (app throws on startup if missing):

- `JwtSettings__SecretKey`
- `ApiSettings__ApiKey`
- `CosmosDb__AccountEndpoint` / `CosmosDb__AccountKey`
- `AzureFaceApi__Endpoint` / `AzureFaceApi__Key`

Recommended:

- `Azure__SignalR__ConnectionString` — Blazor Server scale-out and reconnect stability
- `AzureStorage__ConnectionString` — private blob storage for face/ID uploads
- `APPLICATIONINSIGHTS_CONNECTION_STRING`
- `AppBaseUrl` — public URL of the Web App (e.g. `https://africanideasfaceapi.azurewebsites.net`)

## Deploy to Azure Web App

Target Web App (existing): **africanideasfaceapi** in resource group **AIDevelopment**.

### Option A — GitHub Actions (recommended)

1. In Azure Portal: **App Service → Deployment Center → Manage publish profile → Download**.
2. In GitHub: repo **Settings → Secrets and variables → Actions → New repository secret**  
   Name: `AZURE_WEBAPP_PUBLISH_PROFILE`  
   Value: entire contents of the `.PublishSettings` file.
3. Push to `main` (or run **Actions → Deploy to Azure Web App → Run workflow**).

Workflow file: `.github/workflows/azure-webapp.yml`. Change `AZURE_WEBAPP_NAME` in that file if your app name differs.

### Option B — Visual Studio Code Azure extension

1. Install **Azure App Service** extension.
2. Run task **publish-release** (Release publish to `bin\Release\net10.0\publish`).
3. Deploy using the configured subscription/site in `.vscode/settings.json`.

### App Service checklist

| Setting | Why |
|--------|-----|
| **Configuration → General settings → Always On** | Blazor Server + Hangfire background jobs |
| **Stack: .NET 10** | Matches `TargetFramework` |
| `WEBSITES_ENABLE_APP_SERVICE_STORAGE=true` | Persists `hangfire.db` on Windows plans |
| `SCM_DO_BUILD_DURING_DEPLOYMENT=false` | CI publishes the app; site only runs the package |
| HTTPS only | Production |

Health endpoints (for probes): `/health`, `/health/ready`, `/health/live`.

### Manual publish (CLI)

```bash
dotnet publish Africanideasfaceapi.csproj -c Release -o ./publish
az webapp deploy --resource-group AIDevelopment --name africanideasfaceapi --src-path ./publish --type zip
```

(Requires Azure CLI `az webapp deploy` extension / recent CLI version.)
