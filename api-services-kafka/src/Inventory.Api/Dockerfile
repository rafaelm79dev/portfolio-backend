# Build context is the api-services-kafka folder (see docker-compose.yml), because the project needs
# Contracts and the central package versions.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, with only the project files, so this layer is cached until a dependency changes.
COPY Directory.Packages.props ./
COPY src/Contracts/Contracts.csproj src/Contracts/
COPY src/Inventory.Api/Inventory.Api.csproj src/Inventory.Api/
RUN dotnet restore src/Inventory.Api/Inventory.Api.csproj

COPY src/Contracts src/Contracts
COPY src/Inventory.Api src/Inventory.Api
RUN dotnet publish src/Inventory.Api/Inventory.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Inventory.Api.dll"]
