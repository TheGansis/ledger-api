FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Ledger.sln ./
COPY src/Ledger.Domain/Ledger.Domain.csproj src/Ledger.Domain/
COPY src/Ledger.Application/Ledger.Application.csproj src/Ledger.Application/
COPY src/Ledger.Infrastructure/Ledger.Infrastructure.csproj src/Ledger.Infrastructure/
COPY src/Ledger.Api/Ledger.Api.csproj src/Ledger.Api/
RUN dotnet restore src/Ledger.Api/Ledger.Api.csproj
COPY src/ src/
RUN dotnet publish src/Ledger.Api/Ledger.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Ledger.Api.dll"]
