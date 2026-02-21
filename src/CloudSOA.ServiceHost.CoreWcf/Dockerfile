FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5010

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/CloudSOA.Common/CloudSOA.Common.csproj", "src/CloudSOA.Common/"]
COPY ["src/CloudSOA.ServiceHost.CoreWcf/CloudSOA.ServiceHost.CoreWcf.csproj", "src/CloudSOA.ServiceHost.CoreWcf/"]
RUN dotnet restore "src/CloudSOA.ServiceHost.CoreWcf/CloudSOA.ServiceHost.CoreWcf.csproj"
COPY src/ src/
RUN rm -rf src/CloudSOA.Common/obj src/CloudSOA.Common/bin src/CloudSOA.ServiceHost.CoreWcf/obj src/CloudSOA.ServiceHost.CoreWcf/bin
WORKDIR /src/src/CloudSOA.ServiceHost.CoreWcf
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

RUN mkdir -p /app/services

ENV ASPNETCORE_URLS="http://+:5010"
ENV SERVICE_DLL_PATH="/app/services/service.dll"
ENTRYPOINT ["dotnet", "CloudSOA.ServiceHost.CoreWcf.dll"]
