# Encender un nodo de TaquillaEDA en cualquier Windows

Esta guía convierte **cualquier PC con Windows** en un nodo remoto del sistema: una réplica más de **Inventario** y otra de **Pagos**, conectadas al **PC principal** (el Linux Mint que corre Kafka, Elasticsearch, el gateway, la app, etc.).

Se pueden conectar **varios Windows a la vez** (pc2, pc3, pc4…). Kafka reparte el trabajo entre todos y, si uno se cae, los demás lo cubren.

> **Resumen rápido:** instalar Docker Desktop → copiar la carpeta del proyecto → misma red que el PC principal → **doble clic en `iniciar-nodo.cmd`** → escribir la IP del PC principal y un nombre (`pc2`, `pc3`…) → esperar a que diga **LISTO**.

---

## 0. Antes de empezar (en el PC principal, Linux)

1. El sistema completo debe estar encendido:
   ```bash
   cd ~/Descargas/demo/taquilla-codigo
   docker compose ps          # todo "running (healthy)"
   ```
2. Anota la **IP del PC principal** (hoy es `192.168.2.7`, pero puede cambiar si cambias de red):
   ```bash
   hostname -I                # la primera IP que NO empiece por 172.
   grep IP_ANFITRION .env     # debe ser la misma IP
   ```
   Si no coinciden, actualízala y aplica el cambio:
   ```bash
   IP=$(ip -4 route get 1.1.1.1 | grep -oP 'src \K\S+'); sed -i "s/^IP_ANFITRION=.*/IP_ANFITRION=$IP/" .env
   docker compose up -d
   ```
3. Deja abierta la vista de grupos. Ahí verás aparecer cada Windows cuando se conecte:
   ```bash
   bash scripts/ver-grupos.sh
   ```

---

## 1. Instalar Docker Desktop (solo la primera vez en ese Windows)

1. Abre **PowerShell como administrador** y ejecuta `wsl --install`. Reinicia si lo pide.
2. Descarga e instala **Docker Desktop**: <https://www.docker.com/products/docker-desktop/> (deja marcado *Use WSL 2*).
3. Abre Docker Desktop y espera a que abajo diga **Engine running**.

---

## 2. Llevar el proyecto a ese Windows

**En el PC principal**, crea el zip:
```bash
cd ~/Descargas/demo
zip -r taquilla-codigo.zip taquilla-codigo -x "*/bin/*" "*/obj/*" "*/.dart_tool/*" "*/build/*"
```

Envíalo por **WhatsApp como Documento** (📎 → Documento, no como foto), por USB o por correo.

**En el Windows:**

1. Clic derecho sobre el zip → **Extraer todo**.
2. Busca la carpeta que tiene **`iniciar-nodo.cmd`** y **`docker-compose.nodo-remoto.yml`** adentro. Esa es la carpeta del proyecto. A veces queda una dentro de otra (`...\taquilla-codigo\taquilla-codigo`): la correcta es la de adentro.
3. **Desbloquea los archivos** (Windows marca todo lo que viene de internet). En esa carpeta: clic derecho en un espacio vacío → *Abrir en Terminal* (o abre PowerShell y haz `cd` a la carpeta), y ejecuta:
   ```powershell
   Get-ChildItem -Recurse | Unblock-File
   ```

---

## 3. Conectar a la misma red

- El Windows y el PC principal deben estar en la **misma red**. Lo más confiable es el **hotspot de un celular**, porque el Wi-Fi de la universidad suele bloquear la conexión entre equipos.
- Comprueba desde el Windows (cambia la IP si es otra):
  ```powershell
  ping 192.168.2.7
  ```

---

## 4. Encender el nodo: doble clic en `iniciar-nodo.cmd`

Se abre una ventana que te hace **dos preguntas**:

| Pregunta | Qué responder |
|---|---|
| **IP del PC principal** | La del paso 0, por ejemplo `192.168.2.7`. Si aparece entre corchetes `[Enter = …]` y es la correcta, solo presiona Enter. |
| **Nombre de ESTE nodo** | **Uno distinto en cada Windows**: `pc2`, `pc3`, `pc4`… Si este Windows ya fue `pc2`, déjalo en `pc2`. |

Después el script hace todo solo:

1. Verifica Docker (si está cerrado, lo abre y espera).
2. Guarda la IP y el nombre en `.env`.
3. Prueba que llega a Kafka (9094), Elasticsearch (9200), la pasarela (8090) y Aspire (18889) del PC principal. **Si algo falla, se detiene y te dice qué revisar.**
4. Si en este Windows está corriendo por error el sistema completo, te ofrece apagarlo.
5. Levanta `inventario` y `pagos`. **La primera vez compila (5–10 min, necesita internet)**; las siguientes veces arranca en segundos.
6. Espera a que los dos queden `healthy` y muestra **LISTO**.

Si Windows muestra *"Windows protegió su PC"*, haz clic en **Más información → Ejecutar de todas formas** (o repite el `Unblock-File` del paso 2).

Por consola también sirve (y permite saltarse las preguntas):
```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\iniciar-nodo.ps1 -IpPc1 192.168.2.7 -Nombre pc3
```

---

## 5. Comprobar en el PC principal

En `ver-grupos.sh` deben aparecer los miembros del nuevo nodo:

```
=============== Grupo: inventario ===============
   /192.168.2.20      inventario-pc2      3
   /192.168.2.31      inventario-pc3      3      <- el Windows nuevo
   /172.18.0.11       inventario-3f2a...  3
   /172.18.0.12       inventario-8e1d...  3
=============== Grupo: pagos ===============
   /192.168.2.20      pagos-pc2           4
   /192.168.2.31      pagos-pc3           4      <- el Windows nuevo
   /172.18.0.13       pagos-b7c4...       4
```

También se ve en Kafbat UI: `http://192.168.2.7:8080` → *Consumers*.

Haz una reserva en la app (`http://192.168.2.7:3000`, desde cualquier PC). Con varias reservas, en los logs del nodo aparecerán cobros `en el nodo pc3`.

---

## 6. Varios Windows a la vez

- **Cada Windows con un nombre distinto** (`pc2`, `pc3`, `pc4`…). Si dos usan el mismo nombre, el sistema sigue funcionando, pero en `ver-grupos` y en los logs no se distingue cuál es cuál (solo por la IP).
- Cada tópico tiene 6 particiones y cada grupo consume 2 tópicos, o sea 12 particiones por grupo. Kafka las reparte entre todos los miembros:

  | Nodos Windows | Miembros en `inventario` (2 del PC principal + Windows) | Particiones c/u | Miembros en `pagos` (1 + Windows) | Particiones c/u |
  |---|---|---|---|---|
  | 1 | 3 | 4 | 2 | 6 |
  | 2 | 4 | 3 | 3 | 4 |
  | 3 | 5 | 2–3 | 4 | 3 |

  Por encima de 12 miembros en un grupo, los que sobran quedan de **reserva** (sin particiones) y entran si otro se cae.
- En el PC principal **no hay que cambiar nada** para sumar más Windows.

---

## 7. Uso diario del nodo

En PowerShell, **dentro de la carpeta del proyecto**. En cada ventana nueva ejecuta primero esta línea (permite correr los scripts en esa ventana):

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
```

| Qué | Comando |
|---|---|
| Estado | `.\scripts\nodo.ps1 ps` |
| Logs en vivo (lo que atiende este PC) | `.\scripts\nodo.ps1 logs -f --tail 0 \| Select-String "Reserva\|Particiones"` |
| Apagar / encender Pagos | `.\scripts\nodo.ps1 stop pagos` / `.\scripts\nodo.ps1 start pagos` |
| Apagar / encender Inventario | `.\scripts\nodo.ps1 stop inventario` / `.\scripts\nodo.ps1 start inventario` |
| Simular crash del nodo | `.\scripts\nodo.ps1 kill` (luego `.\scripts\nodo.ps1 start`) |
| Reiniciar (tras reconectar la red) | `.\scripts\nodo.ps1 restart` |
| **Apagar el nodo** | Doble clic en **`detener-nodo.cmd`** |

(En la tabla, `\|` se escribe como `|`.)

`.\scripts\nodo.ps1 <algo>` es lo mismo que `docker compose -f docker-compose.nodo-remoto.yml <algo>`, así que nunca se te olvida el `-f`.

---

## 8. Situaciones comunes

| Situación | Qué hacer |
|---|---|
| **Cerré la terminal** | No pasa nada: el nodo sigue corriendo. Para ver el estado: `.\scripts\nodo.ps1 ps` |
| **Reinicié o apagué el Windows** | Doble clic en `iniciar-nodo.cmd` y Enter a las dos preguntas. Arranca en segundos, sin compilar. |
| **Cambió la IP del PC principal** (otro día u otra red) | 1) En el PC principal, paso 0.2. 2) En cada Windows, `iniciar-nodo.cmd` con la IP nueva. |
| **Cambié el código** y quiero la versión nueva en el Windows | Copia el proyecto de nuevo y ejecuta `powershell -ExecutionPolicy Bypass -File .\scripts\iniciar-nodo.ps1 -Reconstruir` |
| **Quiero sacar este Windows de la demo** | Doble clic en `detener-nodo.cmd`. Kafka reparte su trabajo entre los que quedan. |

---

## 9. Si el script no funciona: método manual

Mismo resultado, a mano, en PowerShell dentro de la carpeta del proyecto:

```powershell
# 1. Configuración (IP del PC principal y nombre ÚNICO de este nodo)
Set-Content -Path .env -Encoding ascii -Value "IP_ANFITRION=192.168.2.7", "NOMBRE_NODO=pc3"

# 2. Probar conexión (los tres primeros deben dar True)
9094, 9200, 8090, 18889 | ForEach-Object {
  Test-NetConnection 192.168.2.7 -Port $_ -WarningAction SilentlyContinue | Select-Object RemotePort, TcpTestSucceeded
}

# 3. Levantar (--build solo la primera vez o si cambió el código)
docker compose -f docker-compose.nodo-remoto.yml up -d --build

# 4. Verificar: solo inventario y pagos, "healthy" en 30–60 s
docker compose -f docker-compose.nodo-remoto.yml ps
```

---

## 10. Windows sin buen internet (no puede compilar)

Trae las imágenes ya construidas desde el PC principal:

1. **PC principal:**
   ```bash
   docker save -o ~/taquilla-nodo.tar taquilla/inventario:1.0.0 taquilla/pagos:1.0.0
   ```
   Pesa unos 300 MB. Pásalo por USB o por WhatsApp como Documento.
2. **Windows:**
   ```powershell
   docker load -i C:\ruta\taquilla-nodo.tar
   ```
3. Doble clic en `iniciar-nodo.cmd`: como las imágenes ya existen, **no compila**.

Solo sirve si el Windows es Intel/AMD. En Windows con procesador ARM (Snapdragon) hay que compilar.

---

## 11. Problemas frecuentes

| Síntoma | Causa | Solución |
|---|---|---|
| El script dice **"NO responde"** en 9094 / 9200 / 8090 | El Windows no ve al PC principal | Misma red (hotspot), IP correcta, sistema arriba en el PC principal (`docker compose ps`) |
| **"TLS handshake timeout"** o **"failed to fetch"** al compilar | Internet lento o caído en el Windows | Reintenta, cambia de red, o usa la sección 10 |
| Logs de **`kibana`, `kafbat-ui`, `pasarela-mock`** en el Windows | Alguien ejecutó `docker compose up` sin `-f` y levantó el sistema completo | El script ofrece apagarlo; o a mano: `docker compose -p taquilla-eda down` |
| **"Windows containers"** | Docker está en modo Windows | Clic derecho al ícono de Docker → *Switch to Linux containers…* |
| **"la ejecución de scripts está deshabilitada"** | Política de PowerShell | Usa `iniciar-nodo.cmd`, o `Set-ExecutionPolicy -Scope Process Bypass -Force` en esa ventana |
| **"Windows protegió su PC"** al abrir el `.cmd` | Archivo descargado de internet | *Más información → Ejecutar de todas formas*, o `Get-ChildItem -Recurse \| Unblock-File` |
| El nodo **no aparece** en `ver-grupos` aunque dice LISTO | Kafka del PC principal anuncia otra IP | En el PC principal: paso 0.2 y `docker compose up -d` |
| Un nodo **desaparece solo** durante la demo | El Windows se suspendió o perdió Wi-Fi | *Configuración → Sistema → Inicio/apagado y suspensión → Nunca*, y déjalo conectado a la corriente |
| `unhealthy` después de volver la red | El cliente de Kafka quedó colgado | `.\scripts\nodo.ps1 restart` |

---

## Reglas de oro

1. En un Windows nodo, **nunca** ejecutes `docker compose up` a secas. Usa `iniciar-nodo.cmd` o `.\scripts\nodo.ps1`.
2. **Nunca** ejecutes `docker compose down -v` en el **PC principal**: borra los datos de Kafka y Elasticsearch.
3. **Un nombre distinto** por cada Windows.
4. Todos los PCs en la **misma red**, sin suspensión y conectados a la corriente.

Para los escenarios de caída y el guion de la sustentación, ver **`GUIA-DEMO-2PCS.md`** (secciones 5 a 10).
