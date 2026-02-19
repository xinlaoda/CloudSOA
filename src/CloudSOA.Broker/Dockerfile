FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000 5001

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
ENV ASPNETCORE_URLS="http://+:5000;http://+:5001"
ENTRYPOINT ["dotnet", "CloudSOA.Broker.dll"]
