<#
.SYNOPSIS
  Enciende un nodo remoto de TaquillaEDA (Inventario + Pagos) en cualquier Windows con Docker Desktop.

.DESCRIPTION
  1. Verifica que Docker Desktop este encendido (si no, lo abre y espera).
  2. Pide la IP del PC principal y el nombre de este nodo (pc2, pc3, ...) y los guarda en .env.
  3. Prueba que este PC llegue a Kafka, Elasticsearch, la pasarela y Aspire del PC principal.
  4. Levanta inventario y pagos (compila solo si las imagenes no existen o si se pide -Reconstruir).
  5. Espera a que los dos contenedores queden "healthy" y muestra los siguientes pasos.
  Guia completa: ENCENDER-NODO-WINDOWS.md

.EXAMPLE
  Doble clic en iniciar-nodo.cmd (en la carpeta del proyecto)
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\iniciar-nodo.ps1 -IpPc1 192.168.2.7 -Nombre pc3
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\iniciar-nodo.ps1 -Reconstruir
#>
param(
    [string]$IpPc1 = "",
    [string]$Nombre = "",
    [switch]$Reconstruir
)

$ErrorActionPreference = "Continue"
$raiz = Split-Path -Parent $PSScriptRoot
$compose = Join-Path $raiz "docker-compose.nodo-remoto.yml"
$archivoEnv = Join-Path $raiz ".env"

function Titulo([string]$texto) { Write-Host ""; Write-Host "== $texto" -ForegroundColor Cyan }
function Ok([string]$texto) { Write-Host "   [OK] $texto" -ForegroundColor Green }
function Aviso([string]$texto) { Write-Host "   [!]  $texto" -ForegroundColor Yellow }
function Falla([string]$texto) { Write-Host "   [X]  $texto" -ForegroundColor Red }
function Terminar([int]$codigo) { Write-Host ""; exit $codigo }

function Compose { docker compose -f $compose @args }

function Test-Puerto([string]$ip, [int]$puerto) {
    $cliente = New-Object System.Net.Sockets.TcpClient
    try {
        $tarea = $cliente.ConnectAsync($ip, $puerto)
        return ($tarea.Wait(2500) -and $cliente.Connected)
    } catch {
        return $false
    } finally {
        $cliente.Close()
    }
}

function Leer-Env {
    $valores = @{}
    if (Test-Path $archivoEnv) {
        foreach ($linea in Get-Content $archivoEnv) {
            if ($linea -match '^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') { $valores[$Matches[1]] = $Matches[2].Trim() }
        }
    }
    return $valores
}

function Guardar-Env([string]$ip, [string]$nodo) {
    # Conserva las demas variables (si el .env vino del PC principal) y reemplaza solo estas dos.
    $lineas = @()
    if (Test-Path $archivoEnv) {
        $lineas = @(Get-Content $archivoEnv | Where-Object { $_ -notmatch '^\s*(IP_ANFITRION|NOMBRE_NODO)\s*=' })
    }
    $lineas += "IP_ANFITRION=$ip"
    $lineas += "NOMBRE_NODO=$nodo"
    # ASCII: docker compose no lee bien un .env en UTF-16 (lo que genera "echo >" en PowerShell 5).
    Set-Content -Path $archivoEnv -Value $lineas -Encoding ascii
}

Write-Host "==============================================================" -ForegroundColor Yellow
Write-Host "  TaquillaEDA - encender nodo remoto (Inventario + Pagos)" -ForegroundColor Yellow
Write-Host "  Carpeta: $raiz" -ForegroundColor Yellow
Write-Host "==============================================================" -ForegroundColor Yellow

if (-not (Test-Path $compose)) {
    Falla "No encuentro docker-compose.nodo-remoto.yml en $raiz"
    Falla "Este script debe estar en la carpeta scripts\ del proyecto (no lo muevas)."
    Terminar 1
}

# ---------------------------------------------------------------- 1. Docker
Titulo "1. Docker Desktop"
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Falla "Docker no esta instalado. Instala Docker Desktop: https://www.docker.com/products/docker-desktop/"
    Terminar 1
}
docker info *> $null
if ($LASTEXITCODE -ne 0) {
    $exe = Join-Path $env:ProgramFiles "Docker\Docker\Docker Desktop.exe"
    if (Test-Path $exe) {
        Aviso "Docker Desktop esta apagado; lo abro y espero a que diga 'Engine running' (hasta 3 min)..."
        Start-Process $exe
    } else {
        Aviso "Docker Desktop esta apagado. Abrelo desde el menu Inicio; espero hasta 3 min..."
    }
    $limite = (Get-Date).AddMinutes(3)
    do {
        Start-Sleep -Seconds 5
        docker info *> $null
    } while ($LASTEXITCODE -ne 0 -and (Get-Date) -lt $limite)
    if ($LASTEXITCODE -ne 0) {
        Falla "Docker no arranco. Abre Docker Desktop, espera 'Engine running' y vuelve a ejecutar este script."
        Terminar 1
    }
}
$so = (docker info --format "{{.OSType}}" 2>$null)
if ($so -and $so.Trim() -ne "linux") {
    Falla "Docker esta en modo 'Windows containers'. Clic derecho al icono de Docker > 'Switch to Linux containers...'."
    Terminar 1
}
Ok "Docker funcionando"

# ---------------------------------------------------------------- 2. Configuracion
Titulo "2. Configuracion (.env)"
$actual = Leer-Env
$ipPorDefecto = $actual["IP_ANFITRION"]
if ($ipPorDefecto -eq "localhost") { $ipPorDefecto = "" }

while ($true) {
    if (-not $IpPc1) {
        $pregunta = "   IP del PC principal (el que tiene Kafka)"
        if ($ipPorDefecto) { $pregunta += " [Enter = $ipPorDefecto]" }
        $IpPc1 = (Read-Host $pregunta).Trim()
        if (-not $IpPc1) { $IpPc1 = $ipPorDefecto }
    }
    $ipValida = $null
    if ($IpPc1 -and [System.Net.IPAddress]::TryParse($IpPc1, [ref]$ipValida) -and $IpPc1 -notmatch '^127\.' -and $IpPc1.Split('.').Count -eq 4) { break }
    Aviso "IP no valida: '$IpPc1'. Debe ser la IP del PC principal en la red, por ejemplo 192.168.2.7 (no localhost)."
    $IpPc1 = ""
}

$nombrePorDefecto = $actual["NOMBRE_NODO"]
if (-not $nombrePorDefecto) { $nombrePorDefecto = "pc2" }
while ($true) {
    if (-not $Nombre) {
        $Nombre = (Read-Host "   Nombre de ESTE nodo, distinto en cada Windows (pc2, pc3, ...) [Enter = $nombrePorDefecto]").Trim()
        if (-not $Nombre) { $Nombre = $nombrePorDefecto }
    }
    $Nombre = $Nombre.ToLower()
    if ($Nombre -match '^[a-z0-9][a-z0-9-]{0,30}$') { break }
    Aviso "Nombre no valido: '$Nombre'. Usa solo letras minusculas, numeros y guion (ej. pc3)."
    $Nombre = ""
}

Guardar-Env $IpPc1 $Nombre
Ok "IP_ANFITRION=$IpPc1   NOMBRE_NODO=$Nombre   (guardado en .env)"

# ---------------------------------------------------------------- 3. Conectividad
Titulo "3. Conexion con el PC principal ($IpPc1)"
$puertos = @(
    @{ Puerto = 9094;  Nombre = "Kafka (listener externo)";  Obligatorio = $true  },
    @{ Puerto = 9200;  Nombre = "Elasticsearch";             Obligatorio = $true  },
    @{ Puerto = 8090;  Nombre = "Pasarela de pagos";         Obligatorio = $true  },
    @{ Puerto = 18889; Nombre = "Aspire (trazas, opcional)"; Obligatorio = $false }
)
$fallanObligatorios = 0
foreach ($p in $puertos) {
    if (Test-Puerto $IpPc1 $p.Puerto) {
        Ok ("{0,-6} {1}" -f $p.Puerto, $p.Nombre)
    } elseif (-not $p.Obligatorio) {
        Aviso ("{0,-6} {1}: no responde (no bloquea la demo, pero no veras las trazas de este nodo)" -f $p.Puerto, $p.Nombre)
    } else {
        Falla ("{0,-6} {1}: NO responde" -f $p.Puerto, $p.Nombre)
        $fallanObligatorios++
    }
}
if ($fallanObligatorios -gt 0) {
    Write-Host ""
    Falla "Este PC no llega al PC principal. Revisa:"
    Write-Host "     - Que los dos PCs esten en la MISMA red (hotspot del celular; el Wi-Fi de la U suele bloquearlo)."
    Write-Host "     - Que la IP $IpPc1 sea la actual del PC principal (en Linux: hostname -I)."
    Write-Host "     - Que el PC principal tenga el sistema arriba: docker compose ps"
    Write-Host "     - Que el .env del PC principal tenga IP_ANFITRION=$IpPc1 (y luego docker compose up -d)."
    Terminar 1
}

# ---------------------------------------------------------------- 4. Evitar el sistema completo en este Windows
$completo = docker ps -q --filter "label=com.docker.compose.project=taquilla-eda" 2>$null
if ($completo) {
    Titulo "Atencion"
    Aviso "En ESTE Windows esta corriendo el sistema COMPLETO (Kafka, Kibana, pasarela...)."
    Aviso "Eso pasa con 'docker compose up' sin -f. En un nodo remoto no debe estar."
    $r = Read-Host "   Apagarlo ahora? (S/N)"
    if ($r -match '^[sSyY]') {
        docker compose -p taquilla-eda down
        Ok "Sistema completo apagado en este PC"
    } else {
        Aviso "Lo dejo encendido (puede confundir la demo y consumir mucha memoria)."
    }
}

# ---------------------------------------------------------------- 5. Levantar
Titulo "4. Levantando inventario y pagos (nodo '$Nombre')"
docker image inspect taquilla/inventario:1.0.0 *> $null; $hayInventario = ($LASTEXITCODE -eq 0)
docker image inspect taquilla/pagos:1.0.0 *> $null;      $hayPagos = ($LASTEXITCODE -eq 0)

if ($Reconstruir -or -not ($hayInventario -and $hayPagos)) {
    Aviso "Compilando las imagenes. La primera vez necesita internet y tarda 5-10 min; no cierres esta ventana."
    Compose up -d --build
} else {
    Ok "Imagenes ya construidas: se usan sin compilar (para recompilar: -Reconstruir)"
    Compose up -d
}
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Falla "No se pudo levantar el nodo."
    Write-Host "     - 'TLS handshake timeout' o 'failed to fetch': este PC no tiene buen internet para descargar."
    Write-Host "       Reintenta, cambia de red, o trae las imagenes del PC principal (ver la guia, seccion 'Sin internet')."
    Write-Host "     - Otro error: copia el mensaje completo y revisalo con la guia ENCENDER-NODO-WINDOWS.md."
    Terminar 1
}

# ---------------------------------------------------------------- 6. Esperar healthy
Titulo "5. Esperando a que los servicios queden 'healthy' (hasta 3 min)"
$limite = (Get-Date).AddMinutes(3)
$estados = @{}
do {
    Start-Sleep -Seconds 5
    $estados = @{}
    foreach ($servicio in @("inventario", "pagos")) {
        $id = (Compose ps -q $servicio 2>$null | Select-Object -First 1)
        $estado = "no-existe"
        if ($id) { $estado = (docker inspect -f "{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}" $id 2>$null) }
        $estados[$servicio] = "$estado".Trim()
    }
    Write-Host ("   inventario: {0,-10} pagos: {1,-10}" -f $estados["inventario"], $estados["pagos"])
    $listos = ($estados["inventario"] -eq "healthy" -and $estados["pagos"] -eq "healthy")
} while (-not $listos -and (Get-Date) -lt $limite)

if (-not $listos) {
    Write-Host ""
    Falla "Los servicios no quedaron 'healthy'. Ultimos logs:"
    Compose logs --tail 25
    Write-Host ""
    Write-Host "   Casi siempre es conexion con el PC principal: revisa el paso 3 y la IP del .env."
    Terminar 1
}

# ---------------------------------------------------------------- Listo
Write-Host ""
Write-Host "==============================================================" -ForegroundColor Green
Write-Host "  LISTO: nodo '$Nombre' conectado al PC principal $IpPc1" -ForegroundColor Green
Write-Host "==============================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  En el PC principal (Linux):  bash scripts/ver-grupos.sh"
Write-Host "     -> deben aparecer inventario-$Nombre y pagos-$Nombre"
Write-Host ""
Write-Host "  App desde este PC:           http://${IpPc1}:3000"
Write-Host "  Kafbat UI (grupos y lag):    http://${IpPc1}:8080"
Write-Host ""
Write-Host "  Comandos de este nodo (en PowerShell, dentro de la carpeta del proyecto):"
Write-Host "     Set-ExecutionPolicy -Scope Process Bypass -Force      (una vez por ventana)"
Write-Host "     .\scripts\nodo.ps1 ps                                 estado"
Write-Host "     .\scripts\nodo.ps1 logs -f --tail 0                   ver logs en vivo"
Write-Host "     .\scripts\nodo.ps1 stop pagos / start pagos           apagar / encender Pagos"
Write-Host "     .\scripts\nodo.ps1 stop inventario / start inventario apagar / encender Inventario"
Write-Host "     .\scripts\nodo.ps1 kill                               simular crash del nodo"
Write-Host "  Apagar el nodo: doble clic en detener-nodo.cmd"
Terminar 0
