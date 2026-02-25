# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Kumburgaz.Web.csproj ./
RUN dotnet restore Kumburgaz.Web.csproj

COPY . .
RUN dotnet publish Kumburgaz.Web.csproj -c Release -o /app/out --no-restore

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/out ./

EXPOSE 8080

ENTRYPOINT ["dotnet", "Kumburgaz.Web.dll"]
