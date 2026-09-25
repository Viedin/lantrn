# syntax=docker/dockerfile:1

# The SDK runs on the build machine's own architecture and cross-publishes for the target,
# so a multi-arch build never runs the SDK under emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:11.0 AS build
ARG TARGETARCH
WORKDIR /src

# Restore on its own layer so code changes don't re-download packages.
COPY Lantrn.csproj .
RUN dotnet restore -a $TARGETARCH

COPY . .
# Publish restores again: the SDK only adds the package carrying blazor.web.js once it sees .razor files.
RUN dotnet publish Lantrn.csproj -a $TARGETARCH -c Release -o /app \
        -p:UseAppHost=false \
        -p:DebugType=none \
        -p:SatelliteResourceLanguages=en \
    # The runtime image has no shell, so the volume directory is made here. It lives outside /app so
    # the chowned COPY below is what creates ./data; --chown doesn't touch a directory that already exists.
    && mkdir -p /data

# Chiseled: Ubuntu stripped to what .NET needs, no shell or package manager, runs as a non-root user.
FROM mcr.microsoft.com/dotnet/aspnet:11.0-resolute-chiseled
WORKDIR /app
COPY --from=build /app .
# 1654 is the image's non-root "app" user; a new named volume copies this ownership.
COPY --from=build --chown=1654:1654 /data ./data

ENV ConnectionStrings__Lantrn="Data Source=/app/data/lantrn.db"
VOLUME /app/data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Lantrn.dll"]
