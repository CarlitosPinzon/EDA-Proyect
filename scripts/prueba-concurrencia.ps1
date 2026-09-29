<#
.SYNOPSIS
  Escenario de calidad "no sobreventa": N solicitudes concurrentes compiten por una localidad con C cupos.

.DESCRIPTION
  1. Publica un evento nuevo (rol organizador) con una sola localidad de C cupos.
  2. Lanza N reservas en paralelo (rol comprador). Las que reciben 429 (sala de espera)
     esperan el Retry-After y se reintentan con la misma Idempotency-Key.
  3. Espera a que todas las sagas terminen y muestra el resultado.
  Resultado esperado: exactamente C reservas CONFIRMADAS, el resto RECHAZADAS y 0 cupos vendidos de mas.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\scripts\prueba-concurrencia.ps1
  powershell -ExecutionPolicy Bypass -File .\scripts\prueba-concurrencia.ps1 -Solicitudes 200 -Cupos 80
#>
param(
    [string]$Gateway = "http://localhost:8000",
    [int]$Solicitudes = 100,
    [int]$Cupos = 50
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

function Obtener-Token([string]$rol) {
    $cuerpo = @{ nombre = "Prueba $rol"; correo = "$rol@prueba.local"; rol = $rol } | ConvertTo-Json
    (Invoke-RestMethod -Method Post -Uri "$Gateway/api/auth/token" -ContentType "application/json" -Body $cuerpo).token
}

Write-Host "== 1. Publicando un evento de prueba con $Cupos cupos" -ForegroundColor Cyan
$organizador = Obtener-Token "organizador"
$evento = @{
    nombre = "Prueba de concurrencia $(Get-Date -Format 'HHmmss')"
    artista = "Script"; categoria = "Demo"; ciudad = "Bogota"; recinto = "Laboratorio"
    fecha = (Get-Date).ToUniversalTime().AddDays(30).ToString("o")
    localidades = @(@{ nombre = "Zona unica"; precio = 10000; capacidad = $Cupos })
} | ConvertTo-Json -Depth 5
$creado = Invoke-RestMethod -Method Post -Uri "$Gateway/api/admin/eventos" -ContentType "application/json" `
    -Headers @{ Authorization = "Bearer $organizador" } -Body ([Text.Encoding]::UTF8.GetBytes($evento))
$eventoId = $creado.id
$localidadId = $creado.localidades[0].localidadId
Write-Host "   evento $eventoId / localidad $localidadId"
Start-Sleep -Seconds 3   # Inventario crea los cupos al consumir EventoPublicado (asincrono)

Write-Host "== 2. Lanzando $Solicitudes reservas concurrentes" -ForegroundColor Cyan
$comprador = Obtener-Token "comprador"
$http = [System.Net.Http.HttpClient]::new()
$http.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $comprador)
$cuerpoReserva = "{""eventoId"":""$eventoId"",""localidadId"":""$localidadId"",""cantidad"":1}"

$pendientes = 1..$Solicitudes | ForEach-Object { [guid]::NewGuid().ToString() }   # una Idempotency-Key por solicitud
$reservas = New-Object System.Collections.Generic.List[string]
$rechazos429 = 0
while ($pendientes.Count -gt 0) {
    $tareas = foreach ($clave in $pendientes) {
        $solicitud = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$Gateway/api/reservas")
        $solicitud.Headers.Add("Idempotency-Key", $clave)
        $solicitud.Content = [System.Net.Http.StringContent]::new($cuerpoReserva, [Text.Encoding]::UTF8, "application/json")
        $http.SendAsync($solicitud)
    }
    [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]$tareas)

    $reintentar = @(); $espera = 1
    for ($i = 0; $i -lt $tareas.Count; $i++) {
        $respuesta = $tareas[$i].Result
        $codigo = [int]$respuesta.StatusCode
        if ($codigo -eq 202) {
            $json = $respuesta.Content.ReadAsStringAsync().Result | ConvertFrom-Json
            $reservas.Add($json.id)
        } elseif ($codigo -eq 429) {
            $rechazos429++
            $reintentar += $pendientes[$i]
            if ($respuesta.Headers.RetryAfter -and $respuesta.Headers.RetryAfter.Delta) {
                $espera = [Math]::Max($espera, [int][Math]::Ceiling($respuesta.Headers.RetryAfter.Delta.TotalSeconds))
            }
        } else {
            throw "Respuesta inesperada $codigo : $($respuesta.Content.ReadAsStringAsync().Result)"
        }
    }
    if ($reintentar.Count -gt 0) {
        Write-Host "   $($reservas.Count) admitidas, $($reintentar.Count) en sala de espera (429); reintento en $espera s"
        Start-Sleep -Seconds $espera
    }
    $pendientes = $reintentar
}
Write-Host "   $($reservas.Count) reservas admitidas (202). Respuestas 429 durante la prueba: $rechazos429"

Write-Host "== 3. Esperando a que terminen las sagas" -ForegroundColor Cyan
$finales = @("CONFIRMADA", "RECHAZADA", "EXPIRADA", "REEMBOLSADA")
$limite = (Get-Date).AddMinutes(2)
do {
    Start-Sleep -Seconds 2
    $estados = foreach ($id in $reservas) {
        (Invoke-RestMethod -Uri "$Gateway/api/reservas/$id" -Headers @{ Authorization = "Bearer $comprador" }).estado
    }
    $enCurso = @($estados | Where-Object { $finales -notcontains $_ }).Count
    Write-Host "   en curso: $enCurso"
} while ($enCurso -gt 0 -and (Get-Date) -lt $limite)

$confirmadas = @($estados | Where-Object { $_ -eq "CONFIRMADA" }).Count
$rechazadas = @($estados | Where-Object { $_ -eq "RECHAZADA" }).Count
Start-Sleep -Seconds 2
$catalogo = Invoke-RestMethod -Uri "$Gateway/api/eventos/$eventoId"

Write-Host ""
Write-Host "================ RESULTADO ================" -ForegroundColor Yellow
Write-Host " Solicitudes:           $Solicitudes"
Write-Host " Cupos de la localidad: $Cupos"
Write-Host " CONFIRMADAS:           $confirmadas"
Write-Host " RECHAZADAS:            $rechazadas"
Write-Host " Disponibles (catalogo): $($catalogo.localidades[0].disponibles)"
if ($confirmadas -le $Cupos) {
    Write-Host " Sobreventa:            NO (0 cupos vendidos por encima de la capacidad)" -ForegroundColor Green
} else {
    Write-Host " Sobreventa:            SI ($($confirmadas - $Cupos) de mas)" -ForegroundColor Red
    exit 1
}
Write-Host " Detalle del documento: http://localhost:9200/inventario-localidades/_doc/$localidadId"
