#!/usr/bin/env bash
# =====================================================================
#  Vista en vivo de los grupos de consumidores (modo distribuido, dos PCs).
#  Muestra, cada N segundos, qué réplica (y desde qué IP) atiende cada partición de
#  los grupos "inventario" y "pagos", y el lag (mensajes esperando en Kafka).
#
#  Uso (en el PC principal, desde la carpeta del proyecto):
#     bash scripts/ver-grupos.sh            # refresca cada 2 s (Ctrl+C para salir)
#     bash scripts/ver-grupos.sh 5          # refresca cada 5 s
#     GRUPOS="pagos" bash scripts/ver-grupos.sh
#
#  Cómo leerlo:  HOST = IP desde donde se conecta la réplica (la del PC2 aparece con su IP de red).
#                172.x.x.x = réplica del PC principal (red interna de Docker); 192.168.x.x = PC2.
#                CLIENTE = <servicio>-<hostname>: "inventario-pc2" / "pagos-pc2" son las del PC2.
#                LAG > 0 = eventos esperando a que alguien los procese (no se pierden).
# =====================================================================
set -uo pipefail
cd "$(dirname "$0")/.." || exit 1

INTERVALO="${1:-2}"
GRUPOS="${GRUPOS:-inventario pagos}"

describir() {
  docker compose exec -T kafka /opt/kafka/bin/kafka-consumer-groups.sh \
    --bootstrap-server kafka:9092 --describe --group "$1" "${@:2}" 2>/dev/null
}

while true; do
  salida="$(
    echo "TaquillaEDA · grupos de consumidores · $(date +%H:%M:%S)   (Ctrl+C para salir)"
    for g in $GRUPOS; do
      echo
      echo "=============== Grupo: $g ==============="
      echo "-- Miembros"
      describir "$g" --members | awk '
        $1=="GROUP" { for (i = 1; i <= NF; i++) c[$i] = i
                      printf "   %-18s %-26s %s\n", "HOST", "CLIENTE", "#PARTICIONES"; next }
        c["CLIENT-ID"] && NF >= c["CLIENT-ID"] {
                      printf "   %-18s %-26s %s\n", $c["HOST"], $c["CLIENT-ID"], $c["#PARTITIONS"]; n++ }
        END { if (!n) print "   (sin miembros activos: nadie está consumiendo este grupo)" }'
      echo "-- Particiones"
      describir "$g" | awk '
        $1=="GROUP" { for (i = 1; i <= NF; i++) c[$i] = i
                      printf "   %-15s %-5s %-6s %-18s %s\n", "TOPICO", "PART", "LAG", "HOST", "CLIENTE"; next }
        c["CLIENT-ID"] && NF >= c["CLIENT-ID"] && $c["TOPIC"] ~ /\.v1$/ {
                      printf "   %-15s %-5s %-6s %-18s %s\n", $c["TOPIC"], $c["PARTITION"], $c["LAG"], $c["HOST"], $c["CLIENT-ID"] }' \
        | { IFS= read -r cabecera && echo "$cabecera"; sort -k1,1 -k2,2n; }
    done
  )"
  clear
  echo "$salida"
  sleep "$INTERVALO"
done
