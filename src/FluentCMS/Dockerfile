FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["FluentCMS/FluentCMS.csproj", "FluentCMS/"]
COPY ["Backend/FluentCMS.Web.Api/FluentCMS.Web.Api.csproj", "Backend/FluentCMS.Web.Api/"]
COPY ["Backend/FluentCMS.Services/FluentCMS.Services.csproj", "Backend/FluentCMS.Services/"]
COPY ["Providers/ApiTokenProviders/FluentCMS.Providers.ApiTokenProviders.Abstractions/FluentCMS.Providers.ApiTokenProviders.Abstractions.csproj", "Providers/ApiTokenProviders/FluentCMS.Providers.ApiTokenProviders.Abstractions/"]
COPY ["Providers/EmailProviders/FluentCMS.Providers.EmailProviders.Abstractions/FluentCMS.Providers.EmailProviders.Abstractions.csproj", "Providers/EmailProviders/FluentCMS.Providers.EmailProviders.Abstractions/"]
COPY ["Providers/FileStorageProviders/FluentCMS.Providers.FileStorageProviders.Abstractions/FluentCMS.Providers.FileStorageProviders.Abstractions.csproj", "Providers/FileStorageProviders/FluentCMS.Providers.FileStorageProviders.Abstractions/"]
COPY ["Providers/MessageBusProviders/FluentCMS.Providers.MessageBusProviders.Abstractions/FluentCMS.Providers.MessageBusProviders.Abstractions.csproj", "Providers/MessageBusProviders/FluentCMS.Providers.MessageBusProviders.Abstractions/"]
COPY ["Backend/FluentCMS.Identity/FluentCMS.Identity.csproj", "Backend/FluentCMS.Identity/"]
COPY ["Backend/Repositories/FluentCMS.Repositories.Abstractions/FluentCMS.Repositories.Abstractions.csproj", "Backend/Repositories/FluentCMS.Repositories.Abstractions/"]
COPY ["Backend/FluentCMS.Entities/FluentCMS.Entities.csproj", "Backend/FluentCMS.Entities/"]
COPY ["Shared/Shared.csproj", "Shared/"]
COPY ["Backend/Repositories/FluentCMS.Repositories.Caching/FluentCMS.Repositories.Caching.csproj", "Backend/Repositories/FluentCMS.Repositories.Caching/"]
COPY ["Providers/CacheProviders/FluentCMS.Providers.CacheProviders.Abstractions/FluentCMS.Providers.CacheProviders.Abstractions.csproj", "Providers/CacheProviders/FluentCMS.Providers.CacheProviders.Abstractions/"]
COPY ["Backend/Repositories/FluentCMS.Repositories.MongoDB/FluentCMS.Repositories.MongoDB.csproj", "Backend/Repositories/FluentCMS.Repositories.MongoDB/"]
COPY ["Backend/Repositories/FluentCMS.Repositories.LiteDb/FluentCMS.Repositories.LiteDb.csproj", "Backend/Repositories/FluentCMS.Repositories.LiteDb/"]
COPY ["Frontend/FluentCMS.Web.UI/FluentCMS.Web.UI.csproj", "Frontend/FluentCMS.Web.UI/"]
COPY ["Providers/TemplateRenderingProviders/FluentCMS.Providers.TemplateRenderingProviders.Abstractions/FluentCMS.Providers.TemplateRenderingProviders.Abstractions.csproj", "Providers/TemplateRenderingProviders/FluentCMS.Providers.TemplateRenderingProviders.Abstractions/"]
COPY ["Frontend/FluentCMS.Web.ApiClients/FluentCMS.Web.ApiClients.csproj", "Frontend/FluentCMS.Web.ApiClients/"]
COPY ["Frontend/FluentCMS.Web.UI.Components/FluentCMS.Web.UI.Components.csproj", "Frontend/FluentCMS.Web.UI.Components/"]
COPY ["Frontend/Plugins/FluentCMS.Web.Plugins.Contents.TextHTML/FluentCMS.Web.Plugins.Contents.TextHTML.csproj", "Frontend/Plugins/FluentCMS.Web.Plugins.Contents.TextHTML/"]
COPY ["Frontend/Plugins/FluentCMS.Web.Plugins.Base/FluentCMS.Web.Plugins.Base.csproj", "Frontend/Plugins/FluentCMS.Web.Plugins.Base/"]
COPY ["Frontend/Plugins/FluentCMS.Web.Plugins.Contents.Block/FluentCMS.Web.Plugins.Contents.Block.csproj", "Frontend/Plugins/FluentCMS.Web.Plugins.Contents.Block/"]
COPY ["Frontend/Plugins/FluentCMS.Web.Plugins.Contents.RichText/FluentCMS.Web.Plugins.Contents.RichText.csproj", "Frontend/Plugins/FluentCMS.Web.Plugins.Contents.RichText/"]
COPY ["Frontend/Plugins/FluentCMS.Web.Plugins.Contents.ContentViewer/FluentCMS.Web.Plugins.Contents.ContentViewer.csproj", "Frontend/Plugins/FluentCMS.Web.Plugins.Contents.ContentViewer/"]
COPY ["Frontend/Plugins/FluentCMS.Web.Plugins.Admin/FluentCMS.Web.Plugins.Admin.csproj", "Frontend/Plugins/FluentCMS.Web.Plugins.Admin/"]
COPY ["Providers/ApiTokenProviders/FluentCMS.Providers.ApiTokenProviders/FluentCMS.Providers.ApiTokenProviders.csproj", "Providers/ApiTokenProviders/FluentCMS.Providers.ApiTokenProviders/"]
COPY ["Providers/CacheProviders/FluentCMS.Providers.CacheProviders/FluentCMS.Providers.CacheProviders.csproj", "Providers/CacheProviders/FluentCMS.Providers.CacheProviders/"]
COPY ["Providers/EmailProviders/FluentCMS.Providers.EmailProviders/FluentCMS.Providers.EmailProviders.csproj", "Providers/EmailProviders/FluentCMS.Providers.EmailProviders/"]
COPY ["Providers/FileStorageProviders/FluentCMS.Providers.FileStorageProviders/FluentCMS.Providers.FileStorageProviders.csproj", "Providers/FileStorageProviders/FluentCMS.Providers.FileStorageProviders/"]
COPY ["Providers/MessageBusProviders/FluentCMS.Providers.MessageBusProviders/FluentCMS.Providers.MessageBusProviders.csproj", "Providers/MessageBusProviders/FluentCMS.Providers.MessageBusProviders/"]
COPY ["Providers/TemplateRenderingProviders/FluentCMS.Providers.TemplateRenderingProviders/FluentCMS.Providers.TemplateRenderingProviders.csproj", "Providers/TemplateRenderingProviders/FluentCMS.Providers.TemplateRenderingProviders/"]
COPY ["Backend/Repositories/FluentCMS.Repositories.Postgres/FluentCMS.Repositories.Postgres.csproj", "Backend/Repositories/FluentCMS.Repositories.Postgres/"]
RUN dotnet restore "FluentCMS/FluentCMS.csproj"
COPY . .
WORKDIR "/src/FluentCMS"
RUN dotnet build "FluentCMS.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "FluentCMS.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FluentCMS.dll"]
