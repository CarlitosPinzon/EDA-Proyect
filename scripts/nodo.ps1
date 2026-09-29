# Atajo para manejar el nodo remoto desde cualquier carpeta, sin escribir -f cada vez.
# Equivale a: docker compose -f docker-compose.nodo-remoto.yml <lo que escribas>
#   .\scripts\nodo.ps1 ps
#   .\scripts\nodo.ps1 logs -f --tail 0
#   .\scripts\nodo.ps1 stop pagos        .\scripts\nodo.ps1 start pagos
#   .\scripts\nodo.ps1 kill              .\scripts\nodo.ps1 restart
# Si PowerShell no deja ejecutar scripts: Set-ExecutionPolicy -Scope Process Bypass -Force
$compose = Join-Path (Split-Path -Parent $PSScriptRoot) "docker-compose.nodo-remoto.yml"
docker compose -f $compose @args
exit $LASTEXITCODE
