FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5020

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/CloudSOA.ServiceManager/CloudSOA.ServiceManager.csproj", "src/CloudSOA.ServiceManager/"]
RUN dotnet restore "src/CloudSOA.ServiceManager/CloudSOA.ServiceManager.csproj"
COPY src/CloudSOA.ServiceManager/ src/CloudSOA.ServiceManager/
WORKDIR /src/src/CloudSOA.ServiceManager
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS="http://+:5020"
ENTRYPOINT ["dotnet", "CloudSOA.ServiceManager.dll"]
