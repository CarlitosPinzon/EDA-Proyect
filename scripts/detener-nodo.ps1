# Apaga el nodo remoto de este Windows (inventario y pagos). No toca nada del PC principal.
# Kafka reparte sus particiones entre las replicas que queden (apagado ordenado: 1-3 s).
$compose = Join-Path (Split-Path -Parent $PSScriptRoot) "docker-compose.nodo-remoto.yml"
Write-Host "== Apagando el nodo remoto (inventario y pagos)..." -ForegroundColor Cyan
docker compose -f $compose down
if ($LASTEXITCODE -eq 0) {
    Write-Host "   [OK] Nodo apagado. Para encenderlo de nuevo: doble clic en iniciar-nodo.cmd" -ForegroundColor Green
} else {
    Write-Host "   [X]  No se pudo apagar. Esta abierto Docker Desktop?" -ForegroundColor Red
}
exit $LASTEXITCODE
