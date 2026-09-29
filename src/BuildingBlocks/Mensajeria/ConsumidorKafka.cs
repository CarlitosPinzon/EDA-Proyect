using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaquillaEDA.BuildingBlocks.Mensajeria;

public sealed class OpcionesConsumidor
{
    public required string Servicio { get; init; }
    public required string Grupo { get; init; }
    public required IReadOnlyList<string> Topicos { get; init; }
}

/// <summary>
/// Consumidor Kafka de larga duración (BackgroundService). Todas las instancias de un servicio
/// comparten el grupo, así que Kafka les reparte las particiones (escalado horizontal).
/// Entrega "al menos una vez": el offset se guarda solo después de procesar el mensaje.
/// </summary>
public sealed class ConsumidorKafka(
    OpcionesConsumidor opciones,
    IOptions<OpcionesKafka> kafka,
    IServiceScopeFactory alcances,
    DespachadorEventos despachador,
    IRegistroProcesados registro,
    EnviadorDlq dlq,
    ILogger<ConsumidorKafka> log) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken ct) =>
        // Consume() es bloqueante: se ejecuta en un hilo dedicado para no bloquear el arranque del host.
        Task.Factory.StartNew(() => BucleAsync(ct), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task BucleAsync(CancellationToken ct)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = kafka.Value.BootstrapServers,
            GroupId = opciones.Grupo,
            ClientId = $"{opciones.Servicio}-{Environment.MachineName}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,          // confirma periódicamente...
            EnableAutoOffsetStore = false,    // ...solo los offsets que guardamos tras procesar
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky,
            SessionTimeoutMs = 30_000,
            MaxPollIntervalMs = 300_000,
        };

        var pipeline = PipelineConsumo.Crear(despachador, registro, dlq, log);

        using var consumidor = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => log.LogWarning("Consumidor Kafka: {Error}", e.Reason))
            .SetPartitionsAssignedHandler((_, ps) =>
                log.LogInformation("Particiones asignadas al grupo {Grupo}: {Particiones}", opciones.Grupo,
                    string.Join(", ", ps.Select(p => $"{p.Topic}[{p.Partition.Value}]"))))
            .SetPartitionsRevokedHandler((_, ps) =>
                log.LogInformation("Particiones revocadas: {Particiones}",
                    string.Join(", ", ps.Select(p => $"{p.Topic}[{p.Partition.Value}]"))))
            .Build();

        consumidor.Subscribe(opciones.Topicos);
        log.LogInformation("Consumidor {Grupo} suscrito a {Topicos}", opciones.Grupo, string.Join(", ", opciones.Topicos));

        while (!ct.IsCancellationRequested)
        {
            ConsumeResult<string, string>? resultado;
            try
            {
                resultado = consumidor.Consume(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                log.LogWarning("Error de consumo: {Error}", ex.Error.Reason);
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                continue;
            }

            if (resultado?.Message is null) continue;

            // Si falla la infraestructura (p. ej. la DLQ no responde) el mismo mensaje se reintenta
            // sin avanzar el offset: nunca se pierde un evento.
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var alcance = alcances.CreateScope();
                    await pipeline.EjecutarAsync(new ContextoMensaje
                    {
                        Resultado = resultado,
                        Servicios = alcance.ServiceProvider,
                        Servicio = opciones.Servicio,
                        Cancelacion = ct,
                    });
                    consumidor.StoreOffset(resultado);
                    break;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Fallo de infraestructura procesando {Topico}@{Offset}; se reintenta",
                        resultado.Topic, resultado.Offset.Value);
                    await Task.Delay(TimeSpan.FromSeconds(5), ct).ContinueWith(_ => { }, CancellationToken.None);
                }
            }
        }

        consumidor.Close();
    }
}
