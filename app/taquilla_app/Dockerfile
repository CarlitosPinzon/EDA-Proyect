# syntax=docker/dockerfile:1
# =====================================================================
#  app-web: compila la app Flutter para web y la sirve con nginx.
#  nginx también reenvía /api/ al gateway, así el navegador usa un solo origen (sin CORS)
#  y el stream SSE pasa sin buffering.
# =====================================================================

# ---------- Etapa 1: compilar con el SDK oficial de Flutter ----------
# El SDK de Linux se publica para x86_64; en equipos ARM (p. ej. Mac M1) Docker lo emula.
FROM --platform=linux/amd64 debian:bookworm-slim AS build
ARG FLUTTER_VERSION=3.47.5

RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl git unzip xz-utils \
    && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL "https://storage.googleapis.com/flutter_infra_release/releases/stable/linux/flutter_linux_${FLUTTER_VERSION}-stable.tar.xz" \
    | tar -xJ -C /opt \
    && git config --global --add safe.directory /opt/flutter

ENV PATH="/opt/flutter/bin:${PATH}" \
    CI=true \
    FLUTTER_SUPPRESS_ANALYTICS=true

RUN flutter --disable-analytics && flutter precache --web

WORKDIR /app
# Dependencias primero (capa cacheable).
COPY pubspec.yaml pubspec.lock ./
RUN flutter pub get
COPY . .
RUN flutter build web --release

# ---------- Etapa 2: servir los archivos estáticos ----------
FROM nginx:1.29-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/build/web /usr/share/nginx/html
EXPOSE 80
HEALTHCHECK --interval=10s --timeout=3s --retries=5 CMD wget -q --spider http://127.0.0.1/ || exit 1
