# syntax=docker/dockerfile:1

# ---- Build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore against just the csproj files first, so the layer caches across code changes.
COPY src/RecoverFlow.Domain/RecoverFlow.Domain.csproj src/RecoverFlow.Domain/
COPY src/RecoverFlow.Application/RecoverFlow.Application.csproj src/RecoverFlow.Application/
COPY src/RecoverFlow.Infrastructure/RecoverFlow.Infrastructure.csproj src/RecoverFlow.Infrastructure/
COPY src/RecoverFlow.Api/RecoverFlow.Api.csproj src/RecoverFlow.Api/
RUN dotnet restore src/RecoverFlow.Api/RecoverFlow.Api.csproj

COPY . .
RUN dotnet publish src/RecoverFlow.Api/RecoverFlow.Api.csproj -c Release -o /app/publish --no-restore

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "RecoverFlow.Api.dll"]
