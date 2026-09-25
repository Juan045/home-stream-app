FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/StreamMedia.Domain/StreamMedia.Domain.csproj src/StreamMedia.Domain/
COPY src/StreamMedia.Application/StreamMedia.Application.csproj src/StreamMedia.Application/
COPY src/StreamMedia.Infrastructure/StreamMedia.Infrastructure.csproj src/StreamMedia.Infrastructure/
COPY src/StreamMedia.WebApi/StreamMedia.WebApi.csproj src/StreamMedia.WebApi/
RUN dotnet restore src/StreamMedia.WebApi/StreamMedia.WebApi.csproj

COPY . .
RUN dotnet publish src/StreamMedia.WebApi/StreamMedia.WebApi.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "StreamMedia.WebApi.dll"]
