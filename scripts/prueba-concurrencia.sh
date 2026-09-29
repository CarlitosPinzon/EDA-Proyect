#!/usr/bin/env bash
# =====================================================================
#  Escenario de calidad "no sobreventa" (Linux / macOS).
#  N solicitudes concurrentes compiten por una localidad con C cupos.
#
#  1. Publica un evento nuevo (rol organizador) con una localidad de C cupos.
#  2. Lanza N reservas en paralelo (rol comprador). Las que reciben 429
#     (sala de espera) esperan el Retry-After y reintentan con la misma
#     Idempotency-Key.
#  3. Espera a que todas las sagas terminen y muestra el resultado.
#  Resultado esperado: exactamente C reservas CONFIRMADAS y 0 cupos de mas.
#
#  Uso:   bash scripts/prueba-concurrencia.sh [solicitudes] [cupos]
#  Ej.:   bash scripts/prueba-concurrencia.sh 200 80
#  Requiere solo curl.
# =====================================================================
set -uo pipefail

GATEWAY="${GATEWAY:-http://localhost:8000}"
SOLICITUDES="${1:-100}"
CUPOS="${2:-50}"
PARALELO="${PARALELO:-50}"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# Extrae el primer "campo":"valor" o "campo":numero de un JSON (suficiente para estas respuestas).
campo()  { grep -o "\"$1\":\"[^\"]*\"" | head -n1 | cut -d'"' -f4; }
numero() { grep -o "\"$1\":[0-9]*" | head -n1 | cut -d: -f2; }
uuid()   { cat /proc/sys/kernel/random/uuid 2>/dev/null || echo "clave-$$-$RANDOM$RANDOM$RANDOM"; }

token() {
  curl -fsS -X POST "$GATEWAY/api/auth/token" -H 'Content-Type: application/json' \
    -d "{\"nombre\":\"Prueba $1\",\"correo\":\"$1@prueba.local\",\"rol\":\"$1\"}" | campo token
}

echo "== 1. Publicando un evento de prueba con $CUPOS cupos"
ORGANIZADOR="$(token organizador)" || { echo "No responde el gateway en $GATEWAY (docker compose ps)"; exit 1; }
FECHA="$(date -u -d '+30 days' +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || date -u -v+30d +%Y-%m-%dT%H:%M:%SZ)"
EVENTO="$(curl -fsS -X POST "$GATEWAY/api/admin/eventos" \
  -H "Authorization: Bearer $ORGANIZADOR" -H 'Content-Type: application/json' \
  -d "{\"nombre\":\"Prueba de concurrencia $(date +%H%M%S)\",\"artista\":\"Script\",\"categoria\":\"Demo\",\"ciudad\":\"Bogota\",\"recinto\":\"Laboratorio\",\"fecha\":\"$FECHA\",\"localidades\":[{\"nombre\":\"Zona unica\",\"precio\":10000,\"capacidad\":$CUPOS}]}")" \
  || { echo "No se pudo publicar el evento"; exit 1; }
EVENTO_ID="$(echo "$EVENTO" | campo id)"
LOCALIDAD_ID="$(echo "$EVENTO" | campo localidadId)"
echo "   evento $EVENTO_ID / localidad $LOCALIDAD_ID"
sleep 3   # Inventario crea los cupos al consumir EventoPublicado (asincrono)

echo "== 2. Lanzando $SOLICITUDES reservas concurrentes ($PARALELO en paralelo)"
COMPRADOR="$(token comprador)"
CUERPO="{\"eventoId\":\"$EVENTO_ID\",\"localidadId\":\"$LOCALIDAD_ID\",\"cantidad\":1}"

# Una solicitud: reintenta mientras el gateway responda 429 (sala de espera).
reservar() {
  local clave salida codigo espera
  clave="$(uuid)"
  while true; do
    salida="$(curl -sS -D - -X POST "$GATEWAY/api/reservas" \
      -H "Authorization: Bearer $COMPRADOR" -H 'Content-Type: application/json' \
      -H "Idempotency-Key: $clave" -d "$CUERPO")" || { sleep 1; continue; }
    codigo="$(printf '%s\n' "$salida" | head -n1 | awk '{print $2}')"
    case "$codigo" in
      202) printf '%s\n' "$salida" | tail -n1 | campo id; return 0 ;;
      429) echo 429 >&2
           espera="$(printf '%s\n' "$salida" | grep -i '^retry-after:' | tr -dc '0-9')"
           sleep "${espera:-1}" ;;
      *)   echo "ERROR $codigo" >&2; return 1 ;;
    esac
  done
}
export -f reservar campo uuid
export GATEWAY COMPRADOR CUERPO

seq "$SOLICITUDES" | xargs -P "$PARALELO" -I{} bash -c 'reservar' > "$TMP/ids.txt" 2> "$TMP/errores.txt"
ADMITIDAS="$(grep -c . "$TMP/ids.txt")"
RESPUESTAS_429="$(grep -c '^429$' "$TMP/errores.txt")"
ERRORES="$(grep -c '^ERROR' "$TMP/errores.txt")"
echo "   $ADMITIDAS reservas admitidas (202). Respuestas 429 durante la prueba: $RESPUESTAS_429. Errores: $ERRORES"

echo "== 3. Esperando a que terminen las sagas"
LIMITE=$(( $(date +%s) + 120 ))
while :; do
  CONFIRMADAS=0; RECHAZADAS=0; EN_CURSO=0
  while read -r id; do
    estado="$(curl -fsS "$GATEWAY/api/reservas/$id" -H "Authorization: Bearer $COMPRADOR" | campo estado)"
    case "$estado" in
      CONFIRMADA) CONFIRMADAS=$((CONFIRMADAS + 1)) ;;
      RECHAZADA|EXPIRADA|REEMBOLSADA) RECHAZADAS=$((RECHAZADAS + 1)) ;;
      *) EN_CURSO=$((EN_CURSO + 1)) ;;
    esac
  done < "$TMP/ids.txt"
  echo "   en curso: $EN_CURSO"
  [ "$EN_CURSO" -eq 0 ] || [ "$(date +%s)" -ge "$LIMITE" ] && break
  sleep 2
done

sleep 2
DISPONIBLES="$(curl -fsS "$GATEWAY/api/eventos/$EVENTO_ID" | numero disponibles)"

echo
echo "================ RESULTADO ================"
echo " Solicitudes:            $SOLICITUDES"
echo " Cupos de la localidad:  $CUPOS"
echo " CONFIRMADAS:            $CONFIRMADAS"
echo " RECHAZADAS:             $RECHAZADAS"
echo " Disponibles (catalogo): $DISPONIBLES"
if [ "$CONFIRMADAS" -le "$CUPOS" ]; then
  echo " Sobreventa:             NO (0 cupos vendidos por encima de la capacidad)"
else
  echo " Sobreventa:             SI ($((CONFIRMADAS - CUPOS)) de mas)"
  exit 1
fi
echo " Documento de la localidad: http://localhost:9200/inventario-localidades/_doc/$LOCALIDAD_ID"
