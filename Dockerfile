FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Ledger.slnx ./
COPY src/Ledger.Domain/Ledger.Domain.csproj src/Ledger.Domain/
COPY src/Ledger.Application/Ledger.Application.csproj src/Ledger.Application/
COPY src/Ledger.Infrastructure/Ledger.Infrastructure.csproj src/Ledger.Infrastructure/
COPY src/Ledger.Api/Ledger.Api.csproj src/Ledger.Api/
COPY src/Ledger.Notifications/Ledger.Notifications.csproj src/Ledger.Notifications/
RUN dotnet restore src/Ledger.Api/Ledger.Api.csproj && dotnet restore src/Ledger.Notifications/Ledger.Notifications.csproj
COPY src/ src/
RUN dotnet publish src/Ledger.Api/Ledger.Api.csproj -c Release -o /app/api --no-restore \
 && dotnet publish src/Ledger.Notifications/Ledger.Notifications.csproj -c Release -o /app/notifications --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /app/api .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Ledger.Api.dll"]

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS notifications
WORKDIR /app
COPY --from=build /app/notifications .
ENTRYPOINT ["dotnet", "Ledger.Notifications.dll"]
