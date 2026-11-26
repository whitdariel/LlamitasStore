using System.Text;
using LlamitasStore.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace LlamitasStore.Data;

public class AppDbContext : IdentityDbContext<Usuario, Rol, int>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Modulo> Modulos => Set<Modulo>();
    public DbSet<Permiso> Permisos => Set<Permiso>();

    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Marca> Marcas => Set<Marca>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<ProductoImagen> ProductoImagenes => Set<ProductoImagen>();
    public DbSet<ProductoEspecificacion> ProductoEspecificaciones => Set<ProductoEspecificacion>();
    public DbSet<ProductoCompatibilidad> ProductoCompatibilidades => Set<ProductoCompatibilidad>();

    public DbSet<Proveedor> Proveedores => Set<Proveedor>();
    public DbSet<OrdenCompra> OrdenesCompra => Set<OrdenCompra>();
    public DbSet<DetalleOrdenCompra> DetallesOrdenCompra => Set<DetalleOrdenCompra>();
    public DbSet<MovimientoInventario> MovimientosInventario => Set<MovimientoInventario>();
    public DbSet<HistorialPrecio> HistorialPrecios => Set<HistorialPrecio>();

    public DbSet<Carrito> Carritos => Set<Carrito>();
    public DbSet<CarritoItem> CarritoItems => Set<CarritoItem>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<DetallePedido> DetallesPedido => Set<DetallePedido>();
    public DbSet<PedidoEstadoHistorial> PedidoHistorial => Set<PedidoEstadoHistorial>();
    public DbSet<Pago> Pagos => Set<Pago>();
    public DbSet<Cupon> Cupones => Set<Cupon>();
    public DbSet<Promocion> Promociones => Set<Promocion>();

    public DbSet<Direccion> Direcciones => Set<Direccion>();
    public DbSet<Favorito> Favoritos => Set<Favorito>();
    public DbSet<Resena> Resenas => Set<Resena>();
    public DbSet<ConfiguracionPc> ConfiguracionesPc => Set<ConfiguracionPc>();
    public DbSet<ConfiguracionPcItem> ConfiguracionPcItems => Set<ConfiguracionPcItem>();
    public DbSet<Notificacion> Notificaciones => Set<Notificacion>();

    public DbSet<MensajeContacto> MensajesContacto => Set<MensajeContacto>();
    public DbSet<AjusteTienda> Ajustes => Set<AjusteTienda>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(12, 2);
        configurationBuilder.Properties<decimal?>().HavePrecision(12, 2);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        ConfigurarIdentidad(b);
        ConfigurarCatalogo(b);
        ConfigurarInventario(b);
        ConfigurarVentas(b);
        ConfigurarClientes(b);
        ConfigurarTienda(b);

        AplicarConvencionesFinales(b);
    }

    private static void ConfigurarIdentidad(ModelBuilder b)
    {
        b.Entity<Usuario>(e =>
        {
            e.ToTable("usuario");
            e.Property(u => u.Nombre).HasMaxLength(60).IsRequired();
            e.Property(u => u.Apellido).HasMaxLength(60).IsRequired();
            e.Property(u => u.FotoUrl).HasMaxLength(500);
            e.Property(u => u.PhoneNumber).HasMaxLength(20);
            e.Ignore(u => u.NombreCompleto);
        });

        b.Entity<Rol>(e =>
        {
            e.ToTable("rol");
            e.Property(r => r.Descripcion).HasMaxLength(150);
        });

        b.Entity<IdentityUserRole<int>>().ToTable("usuario_rol");
        b.Entity<IdentityUserClaim<int>>().ToTable("usuario_claim");
        b.Entity<IdentityUserLogin<int>>().ToTable("usuario_login");
        b.Entity<IdentityUserToken<int>>().ToTable("usuario_token");
        b.Entity<IdentityRoleClaim<int>>().ToTable("rol_claim");

        b.Entity<Modulo>(e =>
        {
            e.ToTable("modulo");
            e.Property(m => m.Clave).HasMaxLength(40).IsRequired();
            e.Property(m => m.Nombre).HasMaxLength(60).IsRequired();
            e.Property(m => m.Descripcion).HasMaxLength(150);
            e.HasIndex(m => m.Clave).IsUnique();
        });

        b.Entity<Permiso>(e =>
        {
            e.ToTable("permiso");
            e.HasIndex(p => new { p.RolId, p.ModuloId }).IsUnique();
            e.HasOne(p => p.Rol).WithMany(r => r.Permisos).HasForeignKey(p => p.RolId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Modulo).WithMany(m => m.Permisos).HasForeignKey(p => p.ModuloId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurarCatalogo(ModelBuilder b)
    {
        b.Entity<Categoria>(e =>
        {
            e.ToTable("categoria");
            e.Property(c => c.Nombre).HasMaxLength(60).IsRequired();
            e.Property(c => c.Slug).HasMaxLength(80).IsRequired();
            e.Property(c => c.Descripcion).HasMaxLength(300);
            e.Property(c => c.Icono).HasMaxLength(60);
            e.HasIndex(c => c.Nombre).IsUnique();
            e.HasIndex(c => c.Slug).IsUnique();
        });

        b.Entity<Marca>(e =>
        {
            e.ToTable("marca");
            e.Property(m => m.Nombre).HasMaxLength(60).IsRequired();
            e.Property(m => m.Slug).HasMaxLength(80).IsRequired();
            e.Property(m => m.Descripcion).HasMaxLength(300);
            e.Property(m => m.SitioWeb).HasMaxLength(200);
            e.HasIndex(m => m.Nombre).IsUnique();
            e.HasIndex(m => m.Slug).IsUnique();
        });

        b.Entity<Producto>(e =>
        {
            e.ToTable("producto", t =>
            {
                t.HasCheckConstraint("ck_producto_precio", "precio >= 0");
                t.HasCheckConstraint("ck_producto_precio_costo", "precio_costo >= 0");
                t.HasCheckConstraint("ck_producto_precio_oferta", "precio_oferta IS NULL OR (precio_oferta > 0 AND precio_oferta < precio)");
                t.HasCheckConstraint("ck_producto_stock", "stock >= 0");
                t.HasCheckConstraint("ck_producto_stock_minimo", "stock_minimo >= 0");
            });
            e.Property(p => p.Sku).HasMaxLength(40).IsRequired();
            e.Property(p => p.Nombre).HasMaxLength(150).IsRequired();
            e.Property(p => p.Slug).HasMaxLength(180).IsRequired();
            e.Property(p => p.Modelo).HasMaxLength(80);
            e.Property(p => p.DescripcionCorta).HasMaxLength(300).IsRequired();
            e.Property(p => p.Descripcion).HasMaxLength(4000).IsRequired();
            e.Property(p => p.ValoracionPromedio).HasPrecision(3, 2);
            e.Ignore(p => p.Disponible);
            e.Ignore(p => p.StockBajo);

            e.HasIndex(p => p.Sku).IsUnique();
            e.HasIndex(p => p.Slug).IsUnique();
            e.HasIndex(p => new { p.CategoriaId, p.Estado });
            e.HasIndex(p => p.Nombre);
            e.HasIndex(p => p.Destacado);

            e.HasOne(p => p.Categoria).WithMany(c => c.Productos).HasForeignKey(p => p.CategoriaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Marca).WithMany(m => m.Productos).HasForeignKey(p => p.MarcaId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ProductoImagen>(e =>
        {
            e.ToTable("producto_imagen");
            e.Property(i => i.Url).HasMaxLength(500).IsRequired();
            e.Property(i => i.TextoAlternativo).HasMaxLength(200).IsRequired();
            e.HasOne(i => i.Producto).WithMany(p => p.Imagenes).HasForeignKey(i => i.ProductoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ProductoEspecificacion>(e =>
        {
            e.ToTable("producto_especificacion");
            e.Property(s => s.Grupo).HasMaxLength(60).IsRequired();
            e.Property(s => s.Nombre).HasMaxLength(80).IsRequired();
            e.Property(s => s.Valor).HasMaxLength(300).IsRequired();
            e.HasOne(s => s.Producto).WithMany(p => p.Especificaciones).HasForeignKey(s => s.ProductoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ProductoCompatibilidad>(e =>
        {
            e.ToTable("producto_compatibilidad");
            e.HasKey(c => c.ProductoId);
            e.Property(c => c.Socket).HasMaxLength(20);
            e.Property(c => c.TiposMemoria).HasMaxLength(30);
            e.Property(c => c.FactorForma).HasMaxLength(20);
            e.Property(c => c.FactoresFormaSoportados).HasMaxLength(80);
            e.Property(c => c.SocketsSoportados).HasMaxLength(120);
            e.Property(c => c.Interfaz).HasMaxLength(20);
            e.HasOne(c => c.Producto).WithOne(p => p.Compatibilidad).HasForeignKey<ProductoCompatibilidad>(c => c.ProductoId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurarInventario(ModelBuilder b)
    {
        b.Entity<Proveedor>(e =>
        {
            e.ToTable("proveedor");
            e.Property(p => p.Nombre).HasMaxLength(100).IsRequired();
            e.Property(p => p.Nit).HasMaxLength(30);
            e.Property(p => p.Telefono).HasMaxLength(20);
            e.Property(p => p.Email).HasMaxLength(150);
            e.Property(p => p.Direccion).HasMaxLength(200);
            e.HasIndex(p => p.Nombre).IsUnique();
        });

        b.Entity<OrdenCompra>(e =>
        {
            e.ToTable("orden_compra", t => t.HasCheckConstraint("ck_orden_compra_total", "total >= 0"));
            e.Property(o => o.Numero).HasMaxLength(30).IsRequired();
            e.Property(o => o.Notas).HasMaxLength(500);
            e.HasIndex(o => o.Numero).IsUnique();
            e.HasOne(o => o.Proveedor).WithMany(p => p.OrdenesCompra).HasForeignKey(o => o.ProveedorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.Usuario).WithMany().HasForeignKey(o => o.UsuarioId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<DetalleOrdenCompra>(e =>
        {
            e.ToTable("detalle_orden_compra", t =>
            {
                t.HasCheckConstraint("ck_detalle_oc_cantidad", "cantidad > 0");
                t.HasCheckConstraint("ck_detalle_oc_costo", "costo_unitario >= 0");
            });
            e.HasOne(d => d.OrdenCompra).WithMany(o => o.Detalles).HasForeignKey(d => d.OrdenCompraId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Producto).WithMany().HasForeignKey(d => d.ProductoId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<MovimientoInventario>(e =>
        {
            e.ToTable("movimiento_inventario", t => t.HasCheckConstraint("ck_movimiento_stock_resultante", "stock_resultante >= 0"));
            e.Property(m => m.Motivo).HasMaxLength(250).IsRequired();
            e.HasIndex(m => new { m.ProductoId, m.Fecha });
            e.HasOne(m => m.Producto).WithMany().HasForeignKey(m => m.ProductoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Pedido).WithMany().HasForeignKey(m => m.PedidoId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(m => m.OrdenCompra).WithMany().HasForeignKey(m => m.OrdenCompraId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(m => m.Usuario).WithMany().HasForeignKey(m => m.UsuarioId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<HistorialPrecio>(e =>
        {
            e.ToTable("historial_precio");
            e.HasIndex(h => new { h.ProductoId, h.Fecha });
            e.HasOne(h => h.Producto).WithMany().HasForeignKey(h => h.ProductoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(h => h.Usuario).WithMany().HasForeignKey(h => h.UsuarioId).OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigurarVentas(ModelBuilder b)
    {
        b.Entity<Carrito>(e =>
        {
            e.ToTable("carrito");
            e.Property(c => c.TokenAnonimo).HasMaxLength(36);
            e.HasIndex(c => c.UsuarioId).IsUnique();
            e.HasIndex(c => c.TokenAnonimo).IsUnique();
            e.HasOne(c => c.Usuario).WithMany().HasForeignKey(c => c.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CarritoItem>(e =>
        {
            e.ToTable("carrito_item", t => t.HasCheckConstraint("ck_carrito_item_cantidad", "cantidad > 0"));
            e.HasIndex(i => new { i.CarritoId, i.ProductoId }).IsUnique();
            e.HasOne(i => i.Carrito).WithMany(c => c.Items).HasForeignKey(i => i.CarritoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Producto).WithMany().HasForeignKey(i => i.ProductoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Pedido>(e =>
        {
            e.ToTable("pedido", t =>
            {
                t.HasCheckConstraint("ck_pedido_subtotal", "subtotal >= 0");
                t.HasCheckConstraint("ck_pedido_descuento", "descuento >= 0");
                t.HasCheckConstraint("ck_pedido_envio", "costo_envio >= 0");
                t.HasCheckConstraint("ck_pedido_total", "total >= 0");
            });
            e.Property(p => p.Numero).HasMaxLength(30).IsRequired();
            e.Property(p => p.CodigoCupon).HasMaxLength(40);
            e.Property(p => p.Notas).HasMaxLength(500);
            e.Property(p => p.EnvioDestinatario).HasMaxLength(120).IsRequired();
            e.Property(p => p.EnvioTelefono).HasMaxLength(20).IsRequired();
            e.Property(p => p.EnvioDepartamento).HasMaxLength(60).IsRequired();
            e.Property(p => p.EnvioCiudad).HasMaxLength(80).IsRequired();
            e.Property(p => p.EnvioDireccion).HasMaxLength(250).IsRequired();
            e.Property(p => p.EnvioReferencia).HasMaxLength(250);
            e.HasIndex(p => p.Numero).IsUnique();
            e.HasIndex(p => new { p.UsuarioId, p.Fecha });
            e.HasIndex(p => p.Estado);
            e.HasOne(p => p.Usuario).WithMany(u => u.Pedidos).HasForeignKey(p => p.UsuarioId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Cupon).WithMany().HasForeignKey(p => p.CuponId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<DetallePedido>(e =>
        {
            e.ToTable("detalle_pedido", t =>
            {
                t.HasCheckConstraint("ck_detalle_pedido_cantidad", "cantidad > 0");
                t.HasCheckConstraint("ck_detalle_pedido_precio", "precio_unitario >= 0");
            });
            e.Property(d => d.NombreProducto).HasMaxLength(150).IsRequired();
            e.Property(d => d.Sku).HasMaxLength(40).IsRequired();
            e.HasOne(d => d.Pedido).WithMany(p => p.Detalles).HasForeignKey(d => d.PedidoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Producto).WithMany().HasForeignKey(d => d.ProductoId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PedidoEstadoHistorial>(e =>
        {
            e.ToTable("pedido_estado_historial");
            e.Property(h => h.Comentario).HasMaxLength(300);
            e.HasOne(h => h.Pedido).WithMany(p => p.Historial).HasForeignKey(h => h.PedidoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(h => h.Usuario).WithMany().HasForeignKey(h => h.UsuarioId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Pago>(e =>
        {
            e.ToTable("pago", t => t.HasCheckConstraint("ck_pago_monto", "monto >= 0"));
            e.Property(p => p.Referencia).HasMaxLength(100);
            e.HasOne(p => p.Pedido).WithMany(p => p.Pagos).HasForeignKey(p => p.PedidoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.ConfirmadoPor).WithMany().HasForeignKey(p => p.ConfirmadoPorId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Cupon>(e =>
        {
            e.ToTable("cupon", t =>
            {
                t.HasCheckConstraint("ck_cupon_valor", "valor > 0");
                t.HasCheckConstraint("ck_cupon_minimo", "monto_minimo >= 0");
                t.HasCheckConstraint("ck_cupon_fechas", "fecha_fin > fecha_inicio");
            });
            e.Property(c => c.Codigo).HasMaxLength(40).IsRequired();
            e.Property(c => c.Descripcion).HasMaxLength(200);
            e.HasIndex(c => c.Codigo).IsUnique();
        });

        b.Entity<Promocion>(e =>
        {
            e.ToTable("promocion", t =>
            {
                t.HasCheckConstraint("ck_promocion_porcentaje", "porcentaje > 0 AND porcentaje <= 90");
                t.HasCheckConstraint("ck_promocion_fechas", "fecha_fin > fecha_inicio");
            });
            e.Property(p => p.Nombre).HasMaxLength(100).IsRequired();
            e.Property(p => p.Descripcion).HasMaxLength(300);
            e.Property(p => p.Porcentaje).HasPrecision(5, 2);
            e.HasIndex(p => new { p.Activa, p.FechaInicio, p.FechaFin });
            e.HasOne(p => p.Categoria).WithMany().HasForeignKey(p => p.CategoriaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Marca).WithMany().HasForeignKey(p => p.MarcaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Producto).WithMany().HasForeignKey(p => p.ProductoId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurarClientes(ModelBuilder b)
    {
        b.Entity<Direccion>(e =>
        {
            e.ToTable("direccion");
            e.Property(d => d.Alias).HasMaxLength(40).IsRequired();
            e.Property(d => d.Destinatario).HasMaxLength(120).IsRequired();
            e.Property(d => d.Telefono).HasMaxLength(20).IsRequired();
            e.Property(d => d.Departamento).HasMaxLength(60).IsRequired();
            e.Property(d => d.Ciudad).HasMaxLength(80).IsRequired();
            e.Property(d => d.DireccionLinea).HasMaxLength(250).IsRequired();
            e.Property(d => d.Referencia).HasMaxLength(250);
            e.HasOne(d => d.Usuario).WithMany(u => u.Direcciones).HasForeignKey(d => d.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Favorito>(e =>
        {
            e.ToTable("favorito");
            e.HasKey(f => new { f.UsuarioId, f.ProductoId });
            e.HasOne(f => f.Usuario).WithMany(u => u.Favoritos).HasForeignKey(f => f.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(f => f.Producto).WithMany().HasForeignKey(f => f.ProductoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Resena>(e =>
        {
            e.ToTable("resena", t => t.HasCheckConstraint("ck_resena_calificacion", "calificacion BETWEEN 1 AND 5"));
            e.Property(r => r.Titulo).HasMaxLength(100).IsRequired();
            e.Property(r => r.Comentario).HasMaxLength(2000).IsRequired();
            e.HasIndex(r => new { r.ProductoId, r.UsuarioId }).IsUnique();
            e.HasOne(r => r.Producto).WithMany(p => p.Resenas).HasForeignKey(r => r.ProductoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Usuario).WithMany().HasForeignKey(r => r.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ConfiguracionPc>(e =>
        {
            e.ToTable("configuracion_pc");
            e.Property(c => c.Nombre).HasMaxLength(100).IsRequired();
            e.Property(c => c.Notas).HasMaxLength(500);
            e.HasOne(c => c.Usuario).WithMany().HasForeignKey(c => c.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ConfiguracionPcItem>(e =>
        {
            e.ToTable("configuracion_pc_item", t => t.HasCheckConstraint("ck_config_pc_item_cantidad", "cantidad > 0"));
            e.HasIndex(i => new { i.ConfiguracionPcId, i.Tipo, i.ProductoId }).IsUnique();
            e.HasOne(i => i.ConfiguracionPc).WithMany(c => c.Items).HasForeignKey(i => i.ConfiguracionPcId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Producto).WithMany().HasForeignKey(i => i.ProductoId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Notificacion>(e =>
        {
            e.ToTable("notificacion");
            e.Property(n => n.Titulo).HasMaxLength(120).IsRequired();
            e.Property(n => n.Mensaje).HasMaxLength(500).IsRequired();
            e.Property(n => n.Url).HasMaxLength(300);
            e.HasIndex(n => new { n.UsuarioId, n.Leida });
            e.HasOne(n => n.Usuario).WithMany().HasForeignKey(n => n.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurarTienda(ModelBuilder b)
    {
        b.Entity<MensajeContacto>(e =>
        {
            e.ToTable("mensaje_contacto");
            e.Property(m => m.Nombre).HasMaxLength(100).IsRequired();
            e.Property(m => m.Email).HasMaxLength(150).IsRequired();
            e.Property(m => m.Asunto).HasMaxLength(150).IsRequired();
            e.Property(m => m.Mensaje).HasMaxLength(3000).IsRequired();
        });

        b.Entity<AjusteTienda>(e =>
        {
            e.ToTable("ajuste_tienda");
            e.HasKey(a => a.Clave);
            e.Property(a => a.Clave).HasMaxLength(60);
            e.Property(a => a.Valor).HasMaxLength(1000).IsRequired();
            e.Property(a => a.Descripcion).HasMaxLength(200);
        });
    }

    /// <summary>
    /// Columnas en snake_case (como la base original) y enums guardados como texto legible
    /// (se ven claros en DBeaver/phpMyAdmin y no dependen del orden del enum).
    /// </summary>
    private static void AplicarConvencionesFinales(ModelBuilder b)
    {
        foreach (var entidad in b.Model.GetEntityTypes())
        {
            foreach (var propiedad in entidad.GetProperties())
            {
                var tabla = StoreObjectIdentifier.Create(entidad, StoreObjectType.Table);
                if (tabla is null) continue;

                if (propiedad.ClrType.IsEnum || Nullable.GetUnderlyingType(propiedad.ClrType)?.IsEnum == true)
                {
                    var tipoEnum = Nullable.GetUnderlyingType(propiedad.ClrType) ?? propiedad.ClrType;
                    var conversor = typeof(Microsoft.EntityFrameworkCore.Storage.ValueConversion.EnumToStringConverter<>).MakeGenericType(tipoEnum);
                    propiedad.SetValueConverter((Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter)Activator.CreateInstance(conversor, new object?[] { null })!);
                    propiedad.SetMaxLength(20);
                }

                propiedad.SetColumnName(ASnakeCase(propiedad.Name));
            }
        }
    }

    internal static string ASnakeCase(string nombre)
    {
        var sb = new StringBuilder(nombre.Length + 8);
        for (var i = 0; i < nombre.Length; i++)
        {
            var c = nombre[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(nombre[i - 1]) || (i + 1 < nombre.Length && char.IsLower(nombre[i + 1]) && char.IsUpper(nombre[i - 1]))))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
