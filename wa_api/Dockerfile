# syntax=docker/dockerfile:1

# ── Build stage ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first (cached layer when only source changes)
COPY wa_api/wa_api.csproj wa_api/
RUN dotnet restore wa_api/wa_api.csproj

# Copy the rest of the project and publish
COPY wa_api/ wa_api/
RUN dotnet publish wa_api/wa_api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ── Runtime stage ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# curl is used by the container HEALTHCHECK to probe /health/live (the aspnet image
# doesn't ship it). Kept minimal; apt lists are removed to keep the layer small.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Kestrel listens on 8080 inside the container (mapped to host in compose)
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

ENTRYPOINT ["dotnet", "wa_api.dll"]
