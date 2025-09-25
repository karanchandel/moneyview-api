# Use .NET 9 SDK for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy remaining files and build
COPY . ./
RUN dotnet publish -c Release -o /app

# Use ASP.NET runtime image for final container
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .

# Expose port 80
EXPOSE 80

# Run the app
ENTRYPOINT ["dotnet", "MoneyViewAPI.dll"]
