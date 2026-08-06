# ── Build stage ───────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Install PowerShell (needed to run playwright.ps1)
RUN apt-get update && apt-get install -y wget apt-transport-https \
    && wget -q https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y powershell \
    && rm -rf /var/lib/apt/lists/* packages-microsoft-prod.deb

# Restore dependencies (web app + the enrichment engine it now references)
COPY Scoutix/Scoutix.csproj Scoutix/
COPY Scoutix.Enrichment/Scoutix.Enrichment.csproj Scoutix.Enrichment/
RUN dotnet restore Scoutix/Scoutix.csproj

# Build and publish
COPY Scoutix/ Scoutix/
COPY Scoutix.Enrichment/ Scoutix.Enrichment/
RUN dotnet publish Scoutix/Scoutix.csproj -c Release -o /app/publish

# Install Playwright Chromium browser into /ms-playwright
WORKDIR /app/publish
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN pwsh playwright.ps1 install chromium

# ── Runtime stage ─────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install tini + PowerShell (needed to run playwright.ps1 install-deps)
RUN sed -i 's|http://archive.ubuntu.com|https://archive.ubuntu.com|g' /etc/apt/sources.list.d/ubuntu.sources 2>/dev/null || true && \
    sed -i 's|http://security.ubuntu.com|https://security.ubuntu.com|g' /etc/apt/sources.list.d/ubuntu.sources 2>/dev/null || true && \
    apt-get update -o Acquire::https::Verify-Peer=false && apt-get install -y --no-install-recommends \
    tini \
    wget \
    apt-transport-https \
    ca-certificates \
    && wget -q https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb \
    && apt-get update -o Acquire::https::Verify-Peer=false \
    && apt-get install -y --no-install-recommends powershell \
    && rm -rf packages-microsoft-prod.deb

# Copy published app from build stage
COPY --from=build /app/publish .

# Copy Chromium browser from build stage
COPY --from=build /ms-playwright /ms-playwright
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

# Let Playwright install EVERY Chromium system dependency for the current OS.
# This replaces the prior hand-curated lib list which was incomplete (missing
# libgl1/libegl1/libatspi2.0-0 etc) and caused renderer crashes once
# --disable-gpu was removed.
RUN pwsh playwright.ps1 install-deps chromium \
    && rm -rf /var/lib/apt/lists/*

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# tini as PID 1 — reaps zombie Chrome subprocesses that dotnet cannot handle as PID 1
ENTRYPOINT ["/usr/bin/tini", "--", "dotnet", "Scoutix.dll"]
