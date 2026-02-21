FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5010

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
ENV NUGET_FALLBACK_PACKAGES=""
COPY ["src/CloudSOA.Common/CloudSOA.Common.csproj", "src/CloudSOA.Common/"]
COPY ["src/CloudSOA.ServiceHost/CloudSOA.ServiceHost.csproj", "src/CloudSOA.ServiceHost/"]
RUN dotnet restore "src/CloudSOA.ServiceHost/CloudSOA.ServiceHost.csproj"
COPY src/ src/
WORKDIR /src/src/CloudSOA.ServiceHost
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

# User service DLLs mounted/copied here
RUN mkdir -p /app/services

ENV ASPNETCORE_URLS="http://+:5010"
ENV SERVICE_DLL_PATH="/app/services/service.dll"
ENTRYPOINT ["dotnet", "CloudSOA.ServiceHost.dll"]
