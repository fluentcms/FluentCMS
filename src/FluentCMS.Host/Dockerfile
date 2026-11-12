# syntax=docker/dockerfile:1
# The reference host. Build from the repository root, for one platform or both:
#   docker build -f src/FluentCMS.Host/Dockerfile -t fluentcms .
#   docker buildx build -f src/FluentCMS.Host/Dockerfile --platform linux/amd64,linux/arm64 -t fluentcms .

# The SDK runs on the build machine and compiles for the target, so no emulation is needed for the other architecture.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY docs/resources/icon.png docs/resources/
# Every project the host references, directly or not; restore skips one that is missing, and the publish then fails.
COPY src/FluentCMS.Sdk/FluentCMS.Sdk.csproj src/FluentCMS.Sdk/
COPY src/FluentCMS.Plugins.Abstractions/FluentCMS.Plugins.Abstractions.csproj src/FluentCMS.Plugins.Abstractions/
COPY src/FluentCMS.Core/FluentCMS.Core.csproj src/FluentCMS.Core/
COPY src/FluentCMS.Generators/FluentCMS.Generators.csproj src/FluentCMS.Generators/
COPY src/FluentCMS.Analyzers/FluentCMS.Analyzers.csproj src/FluentCMS.Analyzers/
COPY src/FluentCMS.Data.PostgreSql/FluentCMS.Data.PostgreSql.csproj src/FluentCMS.Data.PostgreSql/
COPY src/FluentCMS.Data.Sqlite/FluentCMS.Data.Sqlite.csproj src/FluentCMS.Data.Sqlite/
COPY src/FluentCMS.Data.SqlServer/FluentCMS.Data.SqlServer.csproj src/FluentCMS.Data.SqlServer/
COPY src/plugins/FluentCMS.Plugins.AiAssist/FluentCMS.Plugins.AiAssist.csproj src/plugins/FluentCMS.Plugins.AiAssist/
COPY src/plugins/FluentCMS.Plugins.GraphQL/FluentCMS.Plugins.GraphQL.csproj src/plugins/FluentCMS.Plugins.GraphQL/
COPY src/plugins/FluentCMS.Plugins.Mcp/FluentCMS.Plugins.Mcp.csproj src/plugins/FluentCMS.Plugins.Mcp/
COPY src/plugins/FluentCMS.Plugins.Media/FluentCMS.Plugins.Media.csproj src/plugins/FluentCMS.Plugins.Media/
COPY src/plugins/FluentCMS.Plugins.Media.S3/FluentCMS.Plugins.Media.S3.csproj src/plugins/FluentCMS.Plugins.Media.S3/
COPY src/plugins/FluentCMS.Plugins.MultiSite/FluentCMS.Plugins.MultiSite.csproj src/plugins/FluentCMS.Plugins.MultiSite/
COPY src/plugins/FluentCMS.Plugins.Webhooks/FluentCMS.Plugins.Webhooks.csproj src/plugins/FluentCMS.Plugins.Webhooks/
COPY samples/Blog/Blog.csproj samples/Blog/
COPY src/FluentCMS.Host/FluentCMS.Host.csproj src/FluentCMS.Host/
RUN dotnet restore src/FluentCMS.Host/FluentCMS.Host.csproj -a $TARGETARCH

COPY src/ src/
COPY samples/Blog/ samples/Blog/
RUN dotnet publish src/FluentCMS.Host/FluentCMS.Host.csproj -c Release -a $TARGETARCH --no-restore -o /app -p:UseAppHost=false \
    && mkdir /data

# Chiseled: no shell, no package manager, a non-root user. The extra variant adds ICU and time zone data, which
# localized content and schedules in a time zone need.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
COPY --from=build --chown=$APP_UID:$APP_UID /data /data

# SQLite in the /data volume with no configuration. No Production refusal is lifted here, so a run with nothing else
# set stops and names what to set; docs/docker.md shows the settings, and the opt-ins of an evaluation run.
ENV ConnectionStrings__FluentCms="Data Source=/data/fluentcms.db"
VOLUME /data
EXPOSE 8080
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 CMD ["dotnet", "/app/FluentCMS.Host.dll", "--probe"]
ENTRYPOINT ["dotnet", "/app/FluentCMS.Host.dll"]
