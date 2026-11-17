# G Energy API in a Linux container. Used on Windows PCs where Smart App Control blocks
# locally built, unsigned DLLs: code inside the container is not subject to that policy.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY backend/GEnergy.Api/GEnergy.Api.csproj backend/GEnergy.Api/
RUN dotnet restore backend/GEnergy.Api/GEnergy.Api.csproj
COPY backend/GEnergy.Api/ backend/GEnergy.Api/
RUN dotnet publish backend/GEnergy.Api/GEnergy.Api.csproj -c Release -o /app --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Run as the image's built-in non-root "app" user (HOME=/home/app). Secrets are never baked into
# the image: compose.yaml mounts the developer's .NET user secrets read-only at runtime.
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "GEnergy.Api.dll"]
