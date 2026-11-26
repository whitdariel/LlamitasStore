using Microsoft.AspNetCore.Identity;

namespace LlamitasStore.Models.Entities;

public static class Roles
{
    public const string Administrador = "Administrador";
    public const string Vendedor = "Vendedor";
    public const string Cliente = "Cliente";

    /// <summary>Roles que pueden entrar al panel administrativo (luego se filtra por módulo).</summary>
    public const string Personal = Administrador + "," + Vendedor;
}

public class Usuario : IdentityUser<int>
{
    public string Nombre { get; set; } = string.Empty;
    public string Apellido { get; set; } = string.Empty;
    public string? FotoUrl { get; set; }
    public EstadoUsuario Estado { get; set; } = EstadoUsuario.Activo;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime? UltimoAcceso { get; set; }

    public string NombreCompleto => $"{Nombre} {Apellido}".Trim();

    public ICollection<Direccion> Direcciones { get; set; } = new List<Direccion>();
    public ICollection<Pedido> Pedidos { get; set; } = new List<Pedido>();
    public ICollection<Favorito> Favoritos { get; set; } = new List<Favorito>();
}

public class Rol : IdentityRole<int>
{
    public Rol() { }
    public Rol(string nombre, string? descripcion = null) : base(nombre) => Descripcion = descripcion;

    public string? Descripcion { get; set; }

    /// <summary>Los roles del sistema no se pueden eliminar ni renombrar.</summary>
    public bool EsSistema { get; set; }

    public ICollection<Permiso> Permisos { get; set; } = new List<Permiso>();
}

/// <summary>Área del panel administrativo sobre la que se otorgan permisos (heredado del proyecto original).</summary>
public class Modulo
{
    public int Id { get; set; }
    public string Clave { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public int Orden { get; set; }

    public ICollection<Permiso> Permisos { get; set; } = new List<Permiso>();
}

public class Permiso
{
    public int Id { get; set; }
    public int RolId { get; set; }
    public Rol Rol { get; set; } = null!;
    public int ModuloId { get; set; }
    public Modulo Modulo { get; set; } = null!;
    public bool PuedeLeer { get; set; }
    public bool PuedeCrear { get; set; }
    public bool PuedeEditar { get; set; }
    public bool PuedeEliminar { get; set; }
}

/// <summary>Claves de los módulos administrativos. Deben coincidir con los datos iniciales.</summary>
public static class Modulos
{
    public const string Dashboard = "dashboard";
    public const string Productos = "productos";
    public const string Categorias = "categorias";
    public const string Marcas = "marcas";
    public const string Inventario = "inventario";
    public const string Proveedores = "proveedores";
    public const string Pedidos = "pedidos";
    public const string Usuarios = "usuarios";
    public const string Promociones = "promociones";
    public const string Resenas = "resenas";
    public const string Mensajes = "mensajes";
    public const string Configuracion = "configuracion";

    public static readonly string[] Todos =
        [Dashboard, Productos, Categorias, Marcas, Inventario, Proveedores, Pedidos, Usuarios, Promociones, Resenas, Mensajes, Configuracion];
}
