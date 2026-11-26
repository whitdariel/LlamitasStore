using System.Security.Cryptography;
using LlamitasStore.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Data.Semilla;

/// <summary>
/// Datos iniciales idempotentes: se pueden ejecutar en cada arranque sin duplicar nada.
/// - Siempre: roles, módulos, permisos base y ajustes de la tienda.
/// - Solo si Seed:DatosDemo = true: usuarios, catálogo, cupón y promoción de demostración.
/// </summary>
public class DatosIniciales
{
    private readonly AppDbContext _db;
    private readonly RoleManager<Rol> _roles;
    private readonly UserManager<Usuario> _usuarios;
    private readonly IConfiguration _config;
    private readonly ILogger<DatosIniciales> _logger;

    public DatosIniciales(AppDbContext db, RoleManager<Rol> roles, UserManager<Usuario> usuarios,
        IConfiguration config, ILogger<DatosIniciales> logger)
    {
        _db = db;
        _roles = roles;
        _usuarios = usuarios;
        _config = config;
        _logger = logger;
    }

    public async Task EjecutarAsync()
    {
        await CrearRolesAsync();
        await CrearModulosYPermisosAsync();
        await CrearAjustesAsync();

        if (_config.GetValue("Seed:DatosDemo", false))
        {
            await CrearUsuariosDemoAsync();
            await CatalogoDemo.CrearAsync(_db);
            await CrearPromocionesDemoAsync();
        }
    }

    private async Task CrearRolesAsync()
    {
        (string Nombre, string Descripcion)[] roles =
        [
            (Roles.Administrador, "Control total del sistema"),
            (Roles.Vendedor, "Gestión de pedidos, inventario y atención al cliente"),
            (Roles.Cliente, "Cliente de la tienda online")
        ];

        foreach (var (nombre, descripcion) in roles)
        {
            if (await _roles.RoleExistsAsync(nombre)) continue;
            var resultado = await _roles.CreateAsync(new Rol(nombre, descripcion) { EsSistema = true });
            AsegurarExito(resultado, $"crear el rol {nombre}");
        }
    }

    private async Task CrearModulosYPermisosAsync()
    {
        (string Clave, string Nombre, string Descripcion)[] modulos =
        [
            (Modulos.Dashboard, "Dashboard", "Resumen de ventas y estadísticas"),
            (Modulos.Productos, "Productos", "Catálogo, imágenes y especificaciones"),
            (Modulos.Categorias, "Categorías", "Categorías del catálogo"),
            (Modulos.Marcas, "Marcas", "Fabricantes"),
            (Modulos.Inventario, "Inventario", "Stock, movimientos e historial de precios"),
            (Modulos.Proveedores, "Proveedores", "Proveedores y órdenes de compra"),
            (Modulos.Pedidos, "Pedidos", "Pedidos de clientes y pagos"),
            (Modulos.Usuarios, "Usuarios", "Usuarios, roles y permisos"),
            (Modulos.Promociones, "Promociones", "Promociones y cupones"),
            (Modulos.Resenas, "Reseñas", "Moderación de reseñas"),
            (Modulos.Mensajes, "Mensajes", "Mensajes de contacto"),
            (Modulos.Configuracion, "Configuración", "Ajustes generales de la tienda")
        ];

        var existentes = await _db.Modulos.ToDictionaryAsync(m => m.Clave);
        for (var i = 0; i < modulos.Length; i++)
        {
            var (clave, nombre, descripcion) = modulos[i];
            if (!existentes.ContainsKey(clave))
                _db.Modulos.Add(new Modulo { Clave = clave, Nombre = nombre, Descripcion = descripcion, Orden = i + 1 });
        }
        await _db.SaveChangesAsync();

        if (await _db.Permisos.AnyAsync()) return;

        var idModulo = await _db.Modulos.ToDictionaryAsync(m => m.Clave, m => m.Id);
        var admin = await _roles.FindByNameAsync(Roles.Administrador);
        var vendedor = await _roles.FindByNameAsync(Roles.Vendedor);

        foreach (var clave in Modulos.Todos)
        {
            _db.Permisos.Add(new Permiso
            {
                RolId = admin!.Id, ModuloId = idModulo[clave],
                PuedeLeer = true, PuedeCrear = true, PuedeEditar = true, PuedeEliminar = true
            });
        }

        // Vendedor: opera la tienda pero no administra catálogo, usuarios ni configuración.
        (string Clave, bool Crear, bool Editar)[] vendedorPermisos =
        [
            (Modulos.Dashboard, false, false),
            (Modulos.Productos, false, false),
            (Modulos.Inventario, true, true),
            (Modulos.Proveedores, true, true),
            (Modulos.Pedidos, false, true),
            (Modulos.Resenas, false, true),
            (Modulos.Mensajes, false, true)
        ];
        foreach (var (clave, crear, editar) in vendedorPermisos)
        {
            _db.Permisos.Add(new Permiso
            {
                RolId = vendedor!.Id, ModuloId = idModulo[clave],
                PuedeLeer = true, PuedeCrear = crear, PuedeEditar = editar, PuedeEliminar = false
            });
        }

        await _db.SaveChangesAsync();
    }

    private async Task CrearAjustesAsync()
    {
        (string Clave, string Valor, string Descripcion)[] ajustes =
        [
            (ClavesAjuste.NombreTienda, "LlamitasStore", "Nombre visible de la tienda"),
            (ClavesAjuste.SimboloMoneda, "Bs.", "Símbolo de moneda"),
            (ClavesAjuste.EmailContacto, "contacto@llamitasstore.local", "Correo de contacto (cámbialo en Configuración)"),
            (ClavesAjuste.TelefonoContacto, "+591 70000000", "Teléfono de contacto (cámbialo en Configuración)"),
            (ClavesAjuste.DireccionTienda, "Viacha, La Paz, Bolivia", "Dirección de la tienda"),
            (ClavesAjuste.CostoEnvio, "25.00", "Costo de envío estándar"),
            (ClavesAjuste.EnvioGratisDesde, "1500.00", "Subtotal desde el cual el envío es gratis (0 = nunca)"),
            (ClavesAjuste.DatosTransferencia,
                "El administrador aún no configuró los datos bancarios. Escríbenos por la página de contacto antes de transferir.",
                "Instrucciones que ve el cliente al pagar por transferencia")
        ];

        var existentes = await _db.Ajustes.Select(a => a.Clave).ToListAsync();
        foreach (var (clave, valor, descripcion) in ajustes.Where(a => !existentes.Contains(a.Clave)))
            _db.Ajustes.Add(new AjusteTienda { Clave = clave, Valor = valor, Descripcion = descripcion });

        await _db.SaveChangesAsync();
    }

    private async Task CrearUsuariosDemoAsync()
    {
        await CrearUsuarioAsync("admin@llamitasstore.local", "Admin", "LlamitasStore", Roles.Administrador, "Seed:PasswordAdmin");
        await CrearUsuarioAsync("vendedor@llamitasstore.local", "Valeria", "Vendedora", Roles.Vendedor, "Seed:PasswordVendedor");
        await CrearUsuarioAsync("cliente@llamitasstore.local", "Carlos", "Cliente", Roles.Cliente, "Seed:PasswordCliente");
    }

    private async Task CrearUsuarioAsync(string email, string nombre, string apellido, string rol, string clavePassword)
    {
        if (await _usuarios.FindByEmailAsync(email) is not null) return;

        var password = _config[clavePassword];
        var generada = string.IsNullOrWhiteSpace(password);
        if (generada) password = GenerarPassword();

        var usuario = new Usuario
        {
            UserName = email, Email = email, EmailConfirmed = true,
            Nombre = nombre, Apellido = apellido, Estado = EstadoUsuario.Activo
        };
        AsegurarExito(await _usuarios.CreateAsync(usuario, password!), $"crear el usuario {email}");
        AsegurarExito(await _usuarios.AddToRoleAsync(usuario, rol), $"asignar el rol {rol}");

        if (generada)
            _logger.LogWarning("Usuario demo creado: {Email} / contraseña generada: {Password}  (guárdala: no se volverá a mostrar; " +
                               "puedes fijarla con dotnet user-secrets set \"{Clave}\" \"...\" antes del primer arranque)",
                email, password, clavePassword);
        else
            _logger.LogInformation("Usuario demo creado: {Email} (contraseña tomada de {Clave})", email, clavePassword);
    }

    private async Task CrearPromocionesDemoAsync()
    {
        var hoy = DateTime.UtcNow.Date;

        if (!await _db.Cupones.AnyAsync())
        {
            _db.Cupones.Add(new Cupon
            {
                Codigo = "BIENVENIDA10", Descripcion = "10% en tu primera compra desde Bs. 300",
                Tipo = TipoDescuento.Porcentaje, Valor = 10, MontoMinimo = 300,
                FechaInicio = hoy, FechaFin = hoy.AddYears(1), UsosPorUsuario = 1, Activo = true
            });
            _db.Cupones.Add(new Cupon
            {
                Codigo = "SETUP50", Descripcion = "Bs. 50 de descuento en compras desde Bs. 1000",
                Tipo = TipoDescuento.MontoFijo, Valor = 50, MontoMinimo = 1000,
                FechaInicio = hoy, FechaFin = hoy.AddMonths(6), UsosMaximos = 100, Activo = true
            });
        }

        if (!await _db.Promociones.AnyAsync())
        {
            var ratones = await _db.Categorias.FirstOrDefaultAsync(c => c.Slug == "ratones");
            if (ratones is not null)
            {
                _db.Promociones.Add(new Promocion
                {
                    Nombre = "Semana gamer: ratones", Descripcion = "15% de descuento en todos los ratones",
                    Porcentaje = 15, FechaInicio = hoy, FechaFin = hoy.AddDays(30), CategoriaId = ratones.Id
                });
            }
        }

        await _db.SaveChangesAsync();
    }

    private static string GenerarPassword()
    {
        // Cumple la política: mayúscula, minúscula, dígito y símbolo.
        const string letras = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var cuerpo = new string(Enumerable.Range(0, 14).Select(_ => letras[RandomNumberGenerator.GetInt32(letras.Length)]).ToArray());
        return $"Ll{cuerpo}#7";
    }

    private static void AsegurarExito(IdentityResult resultado, string accion)
    {
        if (!resultado.Succeeded)
            throw new InvalidOperationException($"No se pudo {accion}: {string.Join("; ", resultado.Errors.Select(e => e.Description))}");
    }
}
