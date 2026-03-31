FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish qubic_doge_stats/qubic_doge_stats.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    DATA_DIR=/data \
    LITEDB_FILE=doge_stats.db

COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "qubic_doge_stats.dll"]
