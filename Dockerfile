# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Socks5Proxy/Socks5Proxy.csproj src/Socks5Proxy/
RUN dotnet restore src/Socks5Proxy/Socks5Proxy.csproj

COPY src/Socks5Proxy/ src/Socks5Proxy/
RUN dotnet publish src/Socks5Proxy/Socks5Proxy.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN useradd --user-group --create-home --shell /bin/false socks5
COPY --from=build /app/publish .
RUN chown -R socks5:socks5 /app

USER socks5

ENV DOTNET_EnableDiagnostics=0 \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 1080/tcp
EXPOSE 1080/udp

ENTRYPOINT ["dotnet", "Socks5Proxy.dll"]
