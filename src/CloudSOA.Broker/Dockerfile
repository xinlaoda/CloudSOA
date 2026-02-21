FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000 5001 5443 5444

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/CloudSOA.Common/CloudSOA.Common.csproj", "src/CloudSOA.Common/"]
COPY ["src/CloudSOA.ServiceHost/CloudSOA.ServiceHost.csproj", "src/CloudSOA.ServiceHost/"]
COPY ["src/CloudSOA.Broker/CloudSOA.Broker.csproj", "src/CloudSOA.Broker/"]
RUN dotnet restore "src/CloudSOA.Broker/CloudSOA.Broker.csproj"
COPY src/ src/
WORKDIR /src/src/CloudSOA.Broker
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
# Ports configured by Program.cs based on TLS mode
ENTRYPOINT ["dotnet", "CloudSOA.Broker.dll"]
