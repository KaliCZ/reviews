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

# curl is here purely so the compose healthcheck can probe /alive — the
# aspnet image strips it out by default.
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "worker.dll"]
