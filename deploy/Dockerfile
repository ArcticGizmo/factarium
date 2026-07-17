# syntax=docker/dockerfile:1

# --- Stage 1: build the Vue SPA into the API's wwwroot ---
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build   # emits to /src/Factarium.Api/wwwroot (see vite.config.js)

# --- Stage 2: build & publish the .NET host ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ ./src/
COPY --from=web /src/Factarium.Api/wwwroot ./src/Factarium.Api/wwwroot
RUN dotnet publish src/Factarium.Api/Factarium.Api.csproj -c Release -o /app

# --- Stage 3: runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Factarium.Api.dll"]
