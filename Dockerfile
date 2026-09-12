FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ShiftFlow.Web/ShiftFlow.Web.csproj ShiftFlow.Web/
RUN dotnet restore ShiftFlow.Web/ShiftFlow.Web.csproj

COPY ShiftFlow.Web/ ShiftFlow.Web/
RUN dotnet publish ShiftFlow.Web/ShiftFlow.Web.csproj --no-restore -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# curl exists only for the HEALTHCHECK below — the aspnet runtime image ships without an HTTP client.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/out ./

# App_Data/uploads (attachments) and logs/ are written at runtime, so they must be owned by the
# non-root user the app runs as. The "app" user ships with the aspnet image.
RUN mkdir -p /app/App_Data/uploads /app/logs && chown -R app:app /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Don't run the web app as root.
USER app

# /Account/Login is anonymous and cheap — a 200 means the app is actually serving requests,
# unlike a plain process-alive check.
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl --fail --silent --show-error http://localhost:8080/Account/Login || exit 1

ENTRYPOINT ["dotnet", "ShiftFlow.Web.dll"]
