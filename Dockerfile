# Multi-stage Dockerfile for AgenticSdlc.Api (.NET 10, Phase 6).
# Build: docker build -t agentic-sdlc-net:latest .
# Run:   docker run -p 8080:8080 -e Llm__Anthropic__ApiKey=sk-... agentic-sdlc-net:latest

# ---- Stage 1: build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj + restore (cache layer)
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY AgenticSdlc.sln ./
COPY src/AgenticSdlc.Domain/AgenticSdlc.Domain.csproj            src/AgenticSdlc.Domain/
COPY src/AgenticSdlc.Application/AgenticSdlc.Application.csproj  src/AgenticSdlc.Application/
COPY src/AgenticSdlc.Infrastructure/AgenticSdlc.Infrastructure.csproj src/AgenticSdlc.Infrastructure/
COPY src/AgenticSdlc.Api/AgenticSdlc.Api.csproj                  src/AgenticSdlc.Api/
COPY tests/AgenticSdlc.Tests/AgenticSdlc.Tests.csproj            tests/AgenticSdlc.Tests/
RUN dotnet restore src/AgenticSdlc.Api/AgenticSdlc.Api.csproj

# Copy source + publish Release
COPY . .
RUN dotnet publish src/AgenticSdlc.Api/AgenticSdlc.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---- Stage 2: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The aspnet:10.0 base image already includes a non-root user 'app' (uid 1654) — do not recreate it.
COPY --from=build --chown=app:app /app/publish .
USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=false

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "AgenticSdlc.Api.dll"]
