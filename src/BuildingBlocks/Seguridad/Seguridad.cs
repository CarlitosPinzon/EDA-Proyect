using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace TaquillaEDA.BuildingBlocks.Seguridad;

public sealed class OpcionesJwt
{
    public string Clave { get; set; } = "";
    public string Emisor { get; set; } = "taquilla-eda";
    public string Audiencia { get; set; } = "taquilla-app";
    public int MinutosValidez { get; set; } = 480;

    public SymmetricSecurityKey LlaveFirma() => new(Encoding.UTF8.GetBytes(Clave));
}

public static class Roles
{
    public const string Comprador = "comprador";
    public const string Organizador = "organizador";
}

public static class Politicas
{
    public const string Comprador = "comprador";
    public const string Organizador = "organizador";
}

/// <summary>Claims estándar del JWT que identifican al cliente (entidad Cliente, identidad simplificada).</summary>
public static class UsuarioActual
{
    public static string Id(this ClaimsPrincipal u) =>
        u.FindFirstValue("sub") ?? throw new InvalidOperationException("El token no tiene 'sub'");

    public static string Correo(this ClaimsPrincipal u) => u.FindFirstValue("email") ?? "";

    public static string Nombre(this ClaimsPrincipal u) => u.FindFirstValue("name") ?? "";
}

public static class SeguridadExtensions
{
    /// <summary>
    /// Validación de JWT (HS256) y políticas por rol. El gateway valida primero; cada servicio
    /// vuelve a validar (defensa en profundidad) porque necesita los claims del cliente.
    /// </summary>
    public static WebApplicationBuilder AgregarSeguridadJwt(this WebApplicationBuilder builder)
    {
        var jwt = builder.Configuration.GetSection("Jwt").Get<OpcionesJwt>() ?? new OpcionesJwt();
        if (Encoding.UTF8.GetByteCount(jwt.Clave) < 32)
        {
            throw new InvalidOperationException("Jwt:Clave debe tener al menos 32 bytes (HS256).");
        }

        builder.Services.Configure<OpcionesJwt>(builder.Configuration.GetSection("Jwt"));
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Emisor,
                    ValidAudience = jwt.Audiencia,
                    IssuerSigningKey = jwt.LlaveFirma(),
                    NameClaimType = "name",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Politicas.Comprador, p => p.RequireRole(Roles.Comprador, Roles.Organizador))
            .AddPolicy(Politicas.Organizador, p => p.RequireRole(Roles.Organizador));

        return builder;
    }
}
