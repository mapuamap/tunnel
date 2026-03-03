FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5101
ENV ASPNETCORE_URLS=http://0.0.0.0:5101

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/TunnelManager.Web/TunnelManager.Web.csproj", "src/TunnelManager.Web/"]
RUN dotnet restore "src/TunnelManager.Web/TunnelManager.Web.csproj"
COPY . .
WORKDIR "/src/src/TunnelManager.Web"
RUN dotnet build "TunnelManager.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TunnelManager.Web.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TunnelManager.Web.dll"]
