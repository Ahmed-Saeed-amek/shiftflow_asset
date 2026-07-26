FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ShiftFlow.Web/ShiftFlow.Web.csproj ShiftFlow.Web/
RUN dotnet restore ShiftFlow.Web/ShiftFlow.Web.csproj

COPY ShiftFlow.Web/ ShiftFlow.Web/
RUN dotnet publish ShiftFlow.Web/ShiftFlow.Web.csproj --no-restore -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/out ./

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ShiftFlow.Web.dll"]
