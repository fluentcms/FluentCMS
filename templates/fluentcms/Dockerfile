# syntax=docker/dockerfile:1
# Build from this folder, for one platform or both:
#   docker build -t fluentcms-app .
#   docker buildx build --platform linux/amd64,linux/arm64 -t fluentcms-app .

# The SDK runs on the build machine and compiles for the target, so no emulation is needed for the other architecture.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

COPY FluentCms-App.csproj ./
RUN dotnet restore -a $TARGETARCH

COPY . .
RUN dotnet publish -c Release -a $TARGETARCH --no-restore -o /app -p:UseAppHost=false \
    && mkdir /data

# Chiseled: no shell, no package manager, a non-root user. The extra variant adds ICU and time zone data, which
# localized content and schedules in a time zone need.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
COPY --from=build --chown=$APP_UID:$APP_UID /data /data

#if (sqlite)
# SQLite in the /data volume, which keeps the database and the media files of the local storage.
ENV ConnectionStrings__FluentCms="Data Source=/data/fluentcms.db"
#else
# ConnectionStrings__FluentCms comes from the environment of the container. /data keeps the media files of the local storage.
#endif
ENV FluentCms__Media__Local__Root=/data/media
VOLUME /data
EXPOSE 8080
USER $APP_UID

# Production stops at start until the signing certificate, FluentCms__PublicOrigin and an encryptor of the key ring are set
# (FCMS1102, FCMS1105, FCMS1106), each named with its setting. The same image applies the migrations first:
#   docker run <image> fluentcms migrate
ENTRYPOINT ["dotnet", "/app/FluentCms-App.dll"]
