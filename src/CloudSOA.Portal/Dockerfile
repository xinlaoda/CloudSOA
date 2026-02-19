FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5030

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/CloudSOA.Portal/CloudSOA.Portal.csproj", "src/CloudSOA.Portal/"]
RUN dotnet restore "src/CloudSOA.Portal/CloudSOA.Portal.csproj"
COPY src/CloudSOA.Portal/ src/CloudSOA.Portal/
WORKDIR /src/src/CloudSOA.Portal
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS="http://+:5030"
ENTRYPOINT ["dotnet", "CloudSOA.Portal.dll"]
