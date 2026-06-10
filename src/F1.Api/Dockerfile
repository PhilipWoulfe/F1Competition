# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
# 1. Declare the argument (Defaults to Release)
ARG CONFIGURATION=Release
WORKDIR /app

# Copy project files first for better caching
COPY ["src/F1.Api/F1.Api.csproj", "src/F1.Api/"]
COPY ["src/F1.Core/F1.Core.csproj", "src/F1.Core/"]
COPY ["src/F1.Services/F1.Services.csproj", "src/F1.Services/"]
COPY ["src/F1.Infrastructure/F1.Infrastructure.csproj", "src/F1.Infrastructure/"]

RUN dotnet restore "src/F1.Api/F1.Api.csproj"

COPY . .
WORKDIR "/app/src/F1.Api"

# 2. Use the variable in the publish command
RUN dotnet publish -c $CONFIGURATION -o /app/publish

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
USER root
RUN apt-get update \
	&& apt-get install -y --no-install-recommends curl \
	&& rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .

EXPOSE 8080
USER app
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD curl --fail http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "F1.Api.dll"]