# Adapted from https://github.com/dotnet/dotnet-docker/blob/main/samples/aspnetapp/Dockerfile.chiseled

# Build stage
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /source

# Copy project files and restore as distinct layers
COPY --link Player.Api/*.csproj ./Player.Api/
COPY --link Player.Api.Data/*.csproj ./Player.Api.Data/
COPY --link Player.Api.Migrations.PostgreSQL/*.csproj ./Player.Api.Migrations.PostgreSQL/
WORKDIR /source/Player.Api
RUN dotnet restore -a $TARGETARCH

# Copy source code and publish app
WORKDIR /source
COPY --link . .
WORKDIR /source/Player.Api
RUN dotnet publish -a $TARGETARCH --no-restore -o /app

# Debug Stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS debug
ENV DOTNET_HOSTBUILDER__RELOADCONFIGCHANGE=false
EXPOSE 8080
WORKDIR /app
COPY --link --from=build /app .
USER $APP_UID
ENTRYPOINT ["./Player.Api"]

# Production stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0-noble-chiseled AS prod
ARG commit
ENV COMMIT=$commit
ENV DOTNET_HOSTBUILDER__RELOADCONFIGCHANGE=false
EXPOSE 8080
WORKDIR /app
COPY --link --from=build /app .
ENTRYPOINT ["./Player.Api"]