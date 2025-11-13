FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy project files and required files for restore
COPY ["LLMApi/LLMApi.csproj", "LLMApi/"]
COPY ["mostlylucid.mockllmapi/mostlylucid.mockllmapi.csproj", "mostlylucid.mockllmapi/"]

# Copy directory to get all required files (including release notes.txt with spaces)
COPY mostlylucid.mockllmapi/*.txt mostlylucid.mockllmapi/

# Restore dependencies (target only net8.0 to avoid requiring .NET 9 SDK)
RUN dotnet restore "LLMApi/LLMApi.csproj" /p:TargetFrameworks=net8.0

# Copy remaining source files
COPY . .
WORKDIR "/src/LLMApi"

# Publish directly (skips build step to avoid Dashboard.cshtml compilation errors)
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./LLMApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:TargetFrameworks=net8.0

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LLMApi.dll"]
