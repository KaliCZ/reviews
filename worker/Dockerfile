FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["worker/worker.csproj", "worker/"]
COPY ["shared/shared.csproj", "shared/"]
COPY ["service-defaults/service-defaults.csproj", "service-defaults/"]
RUN dotnet restore "worker/worker.csproj"

COPY worker/ worker/
COPY shared/ shared/
COPY service-defaults/ service-defaults/
RUN dotnet publish "worker/worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "worker.dll"]
