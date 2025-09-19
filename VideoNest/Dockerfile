FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY . .
RUN dotnet restore VideoNest.csproj
RUN dotnet publish VideoNest.csproj -c Release -o out /p:PublishTrimmed=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/out .
RUN apt-get update && apt-get install -y ffmpeg libgdiplus && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_ENVIRONMENT=Development
ENV DOTNET_PRINT_TELEMETRY_MESSAGE=false
EXPOSE 8080
ENTRYPOINT ["dotnet", "VideoNest.dll"]