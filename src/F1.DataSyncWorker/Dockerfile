# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG CONFIGURATION=Release
WORKDIR /app

COPY ["src/F1.DataSyncWorker/F1.DataSyncWorker.csproj", "src/F1.DataSyncWorker/"]
COPY ["src/F1.Core/F1.Core.csproj", "src/F1.Core/"]
COPY ["src/F1.Infrastructure/F1.Infrastructure.csproj", "src/F1.Infrastructure/"]

RUN dotnet restore "src/F1.DataSyncWorker/F1.DataSyncWorker.csproj"

COPY . .
WORKDIR "/app/src/F1.DataSyncWorker"
RUN dotnet publish -c $CONFIGURATION -o /app/publish

# Runtime Stage
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /app/data/imports/phil-2025 /app/data/imports/phil-2025

USER app
ENTRYPOINT ["dotnet", "F1.DataSyncWorker.dll"]
