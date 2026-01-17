# ---- Build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . .

# Restore only the API project
RUN dotnet restore src/WorldApi/WorldApi.csproj

# Publish only the API project
RUN dotnet publish src/WorldApi/WorldApi.csproj -c Release -o /app/publish


# ---- Runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:3004
EXPOSE 3004

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "WorldApi.dll"]
