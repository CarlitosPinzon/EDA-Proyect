# syntax=docker/dockerfile:1
# =====================================================================
#  Imagen común para TODOS los servicios .NET de TaquillaEDA (multi-stage).
#  Se construye desde la raíz del repositorio indicando el proyecto:
#    docker build -f src/Dockerfile --build-arg SERVICIO=Inventario -t taquilla/inventario .
#  (docker compose lo hace automáticamente para cada servicio)
# =====================================================================
ARG DOTNET_VERSION=10.0

# ---------- Etapa 1: compilar con el SDK ----------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG SERVICIO
WORKDIR /repo

# 1) Restaurar paquetes. Esta capa solo se repite si cambian los .csproj (caché de Docker).
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Contracts/Contracts.csproj src/Contracts/
COPY src/BuildingBlocks/BuildingBlocks.csproj src/BuildingBlocks/
COPY src/${SERVICIO}/${SERVICIO}.csproj src/${SERVICIO}/
RUN dotnet restore src/${SERVICIO}/${SERVICIO}.csproj

# 2) Copiar el código y publicar en modo Release.
COPY src/Contracts/ src/Contracts/
COPY src/BuildingBlocks/ src/BuildingBlocks/
COPY src/${SERVICIO}/ src/${SERVICIO}/
RUN dotnet publish src/${SERVICIO}/${SERVICIO}.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# ---------- Etapa 2: imagen final, solo con el runtime de ASP.NET Core ----------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
ARG SERVICIO

# curl solo se usa para el health check del contenedor.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0 \
    ENSAMBLADO=TaquillaEDA.${SERVICIO}.dll
EXPOSE 8080

# Usuario sin privilegios definido por las imágenes oficiales de .NET.
USER $APP_UID

HEALTHCHECK --interval=10s --timeout=3s --start-period=30s --retries=12 \
    CMD curl -fs http://localhost:8080/health/ready || exit 1

# sh descarta las variables de entorno cuyo nombre no es un identificador válido (con guion o punto):
# la configuración por variables debe usar solo letras, números y "_" (p. ej. Kafka__BootstrapServers).
ENTRYPOINT ["sh", "-c", "exec dotnet \"$ENSAMBLADO\""]
