# syntax=docker/dockerfile:1.7

FROM node:22-alpine AS web-build
WORKDIR /src/apps/web
COPY apps/web/package*.json ./
RUN npm ci
COPY apps/web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS api-build
WORKDIR /src
COPY .config/dotnet-tools.json .config/dotnet-tools.json
RUN dotnet tool restore
COPY apps/api/GoldenHour.Api.csproj apps/api/
RUN dotnet restore apps/api/GoldenHour.Api.csproj
COPY apps/api/ apps/api/
RUN dotnet publish apps/api/GoldenHour.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /out/api \
    /p:UseAppHost=false
RUN dotnet ef migrations bundle \
    --project apps/api/GoldenHour.Api.csproj \
    --startup-project apps/api/GoldenHour.Api.csproj \
    --target-runtime linux-musl-x64 \
    --output /out/api/efbundle \
    --force

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
RUN apk add --no-cache curl icu-libs
WORKDIR /app
COPY --from=api-build /out/api/ ./
COPY --from=web-build /src/apps/web/dist/ ./wwwroot/
RUN chmod 0555 /app/efbundle

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080

USER $APP_UID
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD curl --fail --silent --show-error http://127.0.0.1:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "GoldenHour.Api.dll"]
