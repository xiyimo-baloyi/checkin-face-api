FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Africanideasfaceapi.csproj ./
COPY global.json ./
# .NET 10 detects Blazor framework assets during restore only when Razor files are present.
COPY Components/ ./Components/
RUN dotnet restore Africanideasfaceapi.csproj
COPY . .
RUN dotnet publish Africanideasfaceapi.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Coolify uses curl or wget to probe HTTP health endpoints.
RUN apt-get update && apt-get install -y --no-install-recommends curl wget ca-certificates \
    && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
COPY --from=build /app/publish .
# Hangfire currently writes a SQLite file relative to the working directory.
RUN chown -R $APP_UID:$APP_UID /app
USER $APP_UID
ENTRYPOINT ["dotnet", "Africanideasfaceapi.dll"]
