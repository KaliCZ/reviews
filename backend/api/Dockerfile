FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["api/api.csproj", "api/"]
COPY ["shared/shared.csproj", "shared/"]
COPY ["service-defaults/service-defaults.csproj", "service-defaults/"]
RUN dotnet restore "api/api.csproj"

COPY api/ api/
COPY shared/ shared/
COPY service-defaults/ service-defaults/
RUN dotnet publish "api/api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "api.dll"]
