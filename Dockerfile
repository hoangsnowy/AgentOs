# Multi-stage Dockerfile for AgentOs.Api (.NET 10, modular monolith).
# Build: docker build -t agentos-api:latest .
# Run:   docker run -p 8080:8080 -e Llm__Claude__ApiKey=sk-ant-... agentos-api:latest

# ---- Stage 1: build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj graph + restore (cache layer). All 9 modules + 2 hosts + ServiceDefaults are pulled
# in by the Api csproj; copying every csproj first keeps the restore layer cached across source-only
# edits.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY AgentOs.slnx ./
COPY src/AgentOs.Domain/AgentOs.Domain.csproj                       src/AgentOs.Domain/
COPY src/AgentOs.SharedKernel/AgentOs.SharedKernel.csproj           src/AgentOs.SharedKernel/
COPY src/AgentOs.Modules.AppConfig/AgentOs.Modules.AppConfig.csproj src/AgentOs.Modules.AppConfig/
COPY src/AgentOs.Modules.Llm/AgentOs.Modules.Llm.csproj             src/AgentOs.Modules.Llm/
COPY src/AgentOs.Modules.Pipeline/AgentOs.Modules.Pipeline.csproj   src/AgentOs.Modules.Pipeline/
COPY src/AgentOs.Modules.Identity/AgentOs.Modules.Identity.csproj   src/AgentOs.Modules.Identity/
COPY src/AgentOs.Modules.Tenants/AgentOs.Modules.Tenants.csproj     src/AgentOs.Modules.Tenants/
COPY src/AgentOs.Modules.Integration/AgentOs.Modules.Integration.csproj src/AgentOs.Modules.Integration/
COPY src/AgentOs.Modules.RemoteAgent/AgentOs.Modules.RemoteAgent.csproj src/AgentOs.Modules.RemoteAgent/
COPY src/AgentOs.ServiceDefaults/AgentOs.ServiceDefaults.csproj     src/AgentOs.ServiceDefaults/
COPY src/AgentOs.Api/AgentOs.Api.csproj                             src/AgentOs.Api/
RUN dotnet restore src/AgentOs.Api/AgentOs.Api.csproj

# Copy source + publish Release
COPY . .
RUN dotnet publish src/AgentOs.Api/AgentOs.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---- Stage 2: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The aspnet:10.0 base image already includes a non-root user 'app' (uid 1654).
COPY --from=build --chown=app:app /app/publish .
USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=false

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD wget --quiet --tries=1 --spider http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "AgentOs.Api.dll"]
