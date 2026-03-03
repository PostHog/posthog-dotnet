FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

# Copy the solution and project files
COPY Directory.Build.props ./
COPY src/PostHog/PostHog.csproj src/PostHog/
COPY sdk_compliance_adapter/SdkComplianceAdapter.csproj sdk_compliance_adapter/

# Restore dependencies
RUN dotnet restore sdk_compliance_adapter/SdkComplianceAdapter.csproj

# Copy source code
COPY src/PostHog/ src/PostHog/
COPY sdk_compliance_adapter/ sdk_compliance_adapter/

# Build and publish
RUN dotnet publish sdk_compliance_adapter/SdkComplianceAdapter.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SdkComplianceAdapter.dll"]
