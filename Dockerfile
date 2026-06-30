FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY CeidgMirror.slnx ./
COPY src/CeidgMirror.Api/CeidgMirror.Api.csproj src/CeidgMirror.Api/
COPY src/CeidgMirror.Application/CeidgMirror.Application.csproj src/CeidgMirror.Application/
COPY src/CeidgMirror.Contracts/CeidgMirror.Contracts.csproj src/CeidgMirror.Contracts/
COPY src/CeidgMirror.Domain/CeidgMirror.Domain.csproj src/CeidgMirror.Domain/
COPY src/CeidgMirror.Infrastructure/CeidgMirror.Infrastructure.csproj src/CeidgMirror.Infrastructure/
COPY src/CeidgMirror.Worker/CeidgMirror.Worker.csproj src/CeidgMirror.Worker/
COPY tests/CeidgMirror.Tests/CeidgMirror.Tests.csproj tests/CeidgMirror.Tests/

RUN dotnet restore CeidgMirror.slnx

COPY . .
RUN dotnet publish src/CeidgMirror.Worker/CeidgMirror.Worker.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

RUN adduser \
    --disabled-password \
    --gecos "" \
    --home /app \
    --uid 10001 \
    ceidg

COPY --from=build /app/publish .
USER ceidg

ENTRYPOINT ["dotnet", "CeidgMirror.Worker.dll"]
