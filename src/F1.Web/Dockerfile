FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/F1.Web/F1.Web.csproj", "src/F1.Web/"]
COPY ["src/F1.Core/F1.Core.csproj", "src/F1.Core/"]
RUN dotnet restore "src/F1.Web/F1.Web.csproj"
COPY . .
WORKDIR /src/src/F1.Web
ARG CONFIGURATION=Release
RUN dotnet publish "F1.Web.csproj" -c $CONFIGURATION -o /app/publish /p:UseAppHost=false

# Capture the build argument and write it to a static file
ARG COMMIT_HASH
RUN echo "{ \"version\": \"$COMMIT_HASH\" }" > /app/publish/wwwroot/version.json

FROM nginx:1.25.3-alpine
WORKDIR /usr/share/nginx/html
COPY --from=build /app/publish/wwwroot .
COPY src/F1.Web/nginx.conf /etc/nginx/templates/default.conf.template

# Install jq for runtime configuration injection
RUN apk add --no-cache jq

# Inject environment variables into appsettings.json at runtime
CMD ["/bin/sh", "-c", "jq --arg url \"$API_BASE_URL\" --arg env \"$BLAZOR_ENVIRONMENT\" '.ApiBaseUrl = $url | .Environment = $env' appsettings.json > tmp.json && mv tmp.json appsettings.json && envsubst '$BLAZOR_ENVIRONMENT' < /etc/nginx/templates/default.conf.template > /etc/nginx/conf.d/default.conf && nginx -g 'daemon off;'"]

EXPOSE 80
