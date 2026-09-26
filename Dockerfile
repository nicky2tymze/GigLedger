# GigLedger: SDD 8, NFR-4. Multi-stage: the SDK image builds, the smaller ASP.NET image runs.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json GigLedger.sln ./
COPY src/GigLedger.Core/GigLedger.Core.csproj src/GigLedger.Core/
COPY src/GigLedger.Data/GigLedger.Data.csproj src/GigLedger.Data/
COPY src/GigLedger.Web/GigLedger.Web.csproj src/GigLedger.Web/
RUN dotnet restore src/GigLedger.Web/GigLedger.Web.csproj
COPY src/ src/
RUN dotnet publish src/GigLedger.Web/GigLedger.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
# The ledger lives on a volume, never in the image, so a new image never replaces the data.
# Backups go on the same volume: inside the image they would be lost with the container. On the
# same volume they protect against a bad write or a mistake, not against losing the disk; copy
# them off the machine for that.
ENV ConnectionStrings__Ledger="Data Source=/data/gigledger.db" \
    Backup__Folder=/data/backups \
    ASPNETCORE_URLS=http://+:8080
# Created here and handed to the app user; otherwise Docker creates it owned by root and
# the first write to the ledger fails.
RUN mkdir /data && chown app:app /data
VOLUME /data
EXPOSE 8080
# The non-root "app" user, by number. Kubernetes can only verify runAsNonRoot against a
# numeric user; the name "app" makes it refuse to start the pod. APP_UID is 1654 in the
# .NET 8 images.
USER $APP_UID
ENTRYPOINT ["dotnet", "GigLedger.Web.dll"]
