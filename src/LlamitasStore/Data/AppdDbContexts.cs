using Microsoft.EntityFrameworkCore;
using LlamitasStore.Models;

namespace LlamitasStore.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Usuario> Usuarios { get; set; }
    public DbSet<Rol> Roles { get; set; }
    public DbSet<Permiso> Permisos { get; set; }
    public DbSet<Modulo> Modulos { get; set; }
    public DbSet<Marca> Marcas { get; set; }
    public DbSet<OrdenCompra> OrdenesCompra { get; set; }
    public DbSet<DetalleOrdenCompra> DetallesOrdenCompra { get; set; }
    public DbSet<MovimientoInventario> MovimientosInventario { get; set; }
    public DbSet<HistorialPrecio> HistorialPrecios { get; set; }
    public DbSet<Categoria> Categorias { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Usuario>().ToTable("usuario");
        modelBuilder.Entity<Rol>().ToTable("rol");
        modelBuilder.Entity<Permiso>().ToTable("permiso");
        modelBuilder.Entity<Permiso>().ToTable("permiso");
        modelBuilder.Entity<Categoria>().ToTable("categoria");
        modelBuilder.Entity<Modulo>().ToTable("modulo");
        modelBuilder.Entity<Marca>().ToTable("marca");
    }
}