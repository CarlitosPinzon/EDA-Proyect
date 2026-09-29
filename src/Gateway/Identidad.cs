using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TaquillaEDA.BuildingBlocks.Seguridad;

namespace TaquillaEDA.Gateway;

public sealed record SolicitudToken(string Nombre, string Correo, string? Rol);

/// <summary>
/// Identidad SIMPLIFICADA para el taller: emite un JWT firmado (HS256) con nombre, correo y rol.
/// No hay contraseñas: cualquiera puede "iniciar sesión". En producción este endpoint se reemplaza
/// por un proveedor de identidad (OpenID Connect, p. ej. Keycloak o Entra ID) y el gateway solo valida.
/// </summary>
public static class Identidad
{
    public static WebApplication MapearIdentidad(this WebApplication app)
    {
        app.MapPost("/api/auth/token", IResult (SolicitudToken s, IOptions<OpcionesJwt> opciones, TimeProvider reloj) =>
        {
            var errores = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(s.Nombre) || s.Nombre.Length > 80) errores["nombre"] = ["Entre 1 y 80 caracteres."];
            if (string.IsNullOrWhiteSpace(s.Correo) || !MailAddress.TryCreate(s.Correo, out _)) errores["correo"] = ["Correo inválido."];
            var rol = string.IsNullOrWhiteSpace(s.Rol) ? Roles.Comprador : s.Rol.Trim().ToLowerInvariant();
            if (rol is not (Roles.Comprador or Roles.Organizador)) errores["rol"] = ["Debe ser 'comprador' u 'organizador'."];
            if (errores.Count > 0) return TypedResults.ValidationProblem(errores);

            var jwt = opciones.Value;
            var correo = s.Correo.Trim().ToLowerInvariant();
            // Id estable por correo: el mismo comprador ve sus reservas aunque vuelva a iniciar sesión.
            var clienteId = "cli-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(correo)))[..16].ToLowerInvariant();
            var ahora = reloj.GetUtcNow();
            var expira = ahora.AddMinutes(jwt.MinutosValidez);

            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = jwt.Emisor,
                Audience = jwt.Audiencia,
                IssuedAt = ahora.UtcDateTime,
                NotBefore = ahora.UtcDateTime,
                Expires = expira.UtcDateTime,
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = clienteId,
                    ["email"] = correo,
                    ["name"] = s.Nombre.Trim(),
                    ["role"] = rol,
                },
                SigningCredentials = new SigningCredentials(jwt.LlaveFirma(), SecurityAlgorithms.HmacSha256),
            });

            return TypedResults.Ok(new
            {
                token,
                expiraEn = expira,
                usuario = new { id = clienteId, nombre = s.Nombre.Trim(), correo, rol },
            });
        }).AllowAnonymous();

        return app;
    }
}
