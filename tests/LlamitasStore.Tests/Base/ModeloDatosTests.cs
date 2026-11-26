using LlamitasStore.Models.Entities;
using LlamitasStore.Tests.Soporte;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Tests.Base;

/// <summary>Las reglas de integridad viven en la base de datos, no solo en el código.</summary>
public class ModeloDatosTests
{
    private static async Task<(Categoria, Marca)> CrearCatalogoMinimoAsync(BaseDatosPrueba bd)
    {
        await using var db = bd.NuevoContexto();
        var categoria = new Categoria { Nombre = "Procesadores", Slug = "procesadores" };
        var marca = new Marca { Nombre = "AMD", Slug = "amd" };
        db.AddRange(categoria, marca);
        await db.SaveChangesAsync();
        return (categoria, marca);
    }

    private static Producto NuevoProducto(Categoria c, Marca m, string sku = "SKU-1") => new()
    {
        Sku = sku, Nombre = "Producto " + sku, Slug = "producto-" + sku.ToLowerInvariant(),
        DescripcionCorta = "Corta", Descripcion = "Larga",
        CategoriaId = c.Id, MarcaId = m.Id, Precio = 100m, PrecioCosto = 70m, Stock = 5
    };

    [Fact]
    public async Task Producto_valido_se_guarda()
    {
        await using var bd = await BaseDatosPrueba.CrearAsync();
        var (c, m) = await CrearCatalogoMinimoAsync(bd);

        await using var db = bd.NuevoContexto();
        db.Productos.Add(NuevoProducto(c, m));
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.Productos.CountAsync());
    }

    [Fact]
    public async Task Stock_negativo_es_rechazado_por_la_base()
    {
        await using var bd = await BaseDatosPrueba.CrearAsync();
        var (c, m) = await CrearCatalogoMinimoAsync(bd);

        await using var db = bd.NuevoContexto();
        var p = NuevoProducto(c, m);
        p.Stock = -1;
        db.Productos.Add(p);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(100)]  // igual al precio
    [InlineData(150)]  // mayor al precio
    [InlineData(0)]    // cero
    public async Task Precio_oferta_debe_ser_menor_que_el_precio(int oferta)
    {
        await using var bd = await BaseDatosPrueba.CrearAsync();
        var (c, m) = await CrearCatalogoMinimoAsync(bd);

        await using var db = bd.NuevoContexto();
        var p = NuevoProducto(c, m);
        p.PrecioOferta = oferta;
        db.Productos.Add(p);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Sku_duplicado_es_rechazado()
    {
        await using var bd = await BaseDatosPrueba.CrearAsync();
        var (c, m) = await CrearCatalogoMinimoAsync(bd);

        await using var db = bd.NuevoContexto();
        db.Productos.Add(NuevoProducto(c, m, "DUP"));
        await db.SaveChangesAsync();

        var otro = NuevoProducto(c, m, "DUP");
        otro.Slug = "otro-slug";
        db.Productos.Add(otro);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Categoria_con_productos_no_se_puede_borrar()
    {
        await using var bd = await BaseDatosPrueba.CrearAsync();
        var (c, m) = await CrearCatalogoMinimoAsync(bd);
        await using (var db = bd.NuevoContexto())
        {
            db.Productos.Add(NuevoProducto(c, m));
            await db.SaveChangesAsync();
        }

        await using var db2 = bd.NuevoContexto();
        db2.Categorias.Remove(await db2.Categorias.SingleAsync());

        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    [Fact]
    public async Task Resena_fuera_de_1_a_5_estrellas_es_rechazada()
    {
        await using var bd = await BaseDatosPrueba.CrearAsync();
        var (c, m) = await CrearCatalogoMinimoAsync(bd);

        await using var db = bd.NuevoContexto();
        var usuario = new Usuario { UserName = "a@b.c", Email = "a@b.c", Nombre = "A", Apellido = "B" };
        var producto = NuevoProducto(c, m);
        db.AddRange(usuario, producto);
        await db.SaveChangesAsync();

        db.Resenas.Add(new Resena { ProductoId = producto.Id, UsuarioId = usuario.Id, Calificacion = 6, Titulo = "t", Comentario = "c" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Permiso_es_unico_por_rol_y_modulo()
    {
        await using var bd = await BaseDatosPrueba.CrearAsync();
        await using var db = bd.NuevoContexto();
        var rol = new Rol("Pruebas") { NormalizedName = "PRUEBAS" };
        var modulo = new Modulo { Clave = "productos", Nombre = "Productos" };
        db.AddRange(rol, modulo);
        await db.SaveChangesAsync();

        db.Permisos.Add(new Permiso { RolId = rol.Id, ModuloId = modulo.Id, PuedeLeer = true });
        db.Permisos.Add(new Permiso { RolId = rol.Id, ModuloId = modulo.Id, PuedeEditar = true });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
