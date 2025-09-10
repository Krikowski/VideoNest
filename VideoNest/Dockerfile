FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY . .
RUN dotnet restore VideoNest.csproj
RUN dotnet publish VideoNest.csproj -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out .
ENV ASPNETCORE_ENVIRONMENT=Development
EXPOSE 80
ENTRYPOINT ["dotnet", "VideoNest.dll"]