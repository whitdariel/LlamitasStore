using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using MySqlConnector;
using LlamitasStore.Models;

namespace LlamitasStore.Controllers;

public class ProductoController : Controller
{
    private static readonly string[] EstadosValidos = { "Activo", "Inactivo", "Descontinuado" };

    private const string SelectProducto = @"
        SELECT p.id_producto, p.id_categoria, c.nombre AS categoria, p.nombre, p.marca, p.modelo,
               p.descripcion, p.precio_compra, p.precio_venta, p.stock_actual, p.estado
        FROM Producto p
        INNER JOIN Categoria c ON c.id_categoria = p.id_categoria";

    private readonly string _connectionString;

    public ProductoController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("MariaDBConnection")
            ?? throw new InvalidOperationException("Falta la cadena de conexión 'MariaDBConnection'.");
    }

    // ---------- LISTAR ----------
    public async Task<IActionResult> Index(string? buscar, int? idCategoria)
    {
        var vm = new ProductoListaViewModel { Buscar = buscar, IdCategoria = idCategoria };

        try
        {
            vm.Categorias = await ObtenerCategoriasAsync();

            string query = SelectProducto + @"
                WHERE (@patron IS NULL OR p.nombre LIKE @patron OR p.marca LIKE @patron OR p.modelo LIKE @patron)
                  AND (@categoria IS NULL OR p.id_categoria = @categoria)
                ORDER BY p.id_producto DESC;";

            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@patron",
                string.IsNullOrWhiteSpace(buscar) ? DBNull.Value : (object)$"%{buscar.Trim()}%");
            command.Parameters.AddWithValue("@categoria",
                idCategoria.HasValue ? (object)idCategoria.Value : DBNull.Value);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                vm.Productos.Add(LeerProducto(reader));
            }
        }
        catch (MySqlException ex)
        {
            TempData["Error"] = "No se pudo leer la base de datos: " + ex.Message;
        }

        return View(vm);
    }

    // ---------- CREAR ----------
    [HttpGet]
    public async Task<IActionResult> Crear()
    {
        await CargarListasAsync();
        return View(new Producto());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(Producto producto)
    {
        ValidarEstado(producto);

        if (ModelState.IsValid)
        {
            const string query = @"
                INSERT INTO Producto (id_categoria, nombre, marca, modelo, descripcion, precio_compra, precio_venta, estado)
                VALUES (@categoria, @nombre, @marca, @modelo, @descripcion, @compra, @venta, @estado);";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(query, connection);
                AgregarParametros(command, producto);
                await command.ExecuteNonQueryAsync();

                TempData["Ok"] = $"Producto \"{producto.Nombre}\" registrado.";
                return RedirectToAction(nameof(Index));
            }
            catch (MySqlException ex) when (ex.Number == 1452) // la categoría no existe
            {
                ModelState.AddModelError(nameof(Producto.IdCategoria), "La categoría seleccionada no existe.");
            }
            catch (MySqlException ex)
            {
                ModelState.AddModelError(string.Empty, "Error de base de datos: " + ex.Message);
            }
        }

        await CargarListasAsync();
        return View(producto);
    }

    // ---------- EDITAR ----------
    [HttpGet]
    public async Task<IActionResult> Editar(int id)
    {
        var producto = await ObtenerProductoAsync(id);
        if (producto == null) return NotFound();

        await CargarListasAsync();
        return View(producto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(int id, Producto producto)
    {
        producto.IdProducto = id;
        ValidarEstado(producto);

        if (ModelState.IsValid)
        {
            // El stock no se toca aquí: lo mantienen los triggers de Inventario y Detalle_Venta.
            const string query = @"
                UPDATE Producto
                SET id_categoria = @categoria, nombre = @nombre, marca = @marca, modelo = @modelo,
                    descripcion = @descripcion, precio_compra = @compra, precio_venta = @venta, estado = @estado
                WHERE id_producto = @id;";

            try
            {
                using var connection = new MySqlConnection(_connectionString);
                await connection.OpenAsync();

                using var command = new MySqlCommand(query, connection);
                AgregarParametros(command, producto);
                command.Parameters.AddWithValue("@id", id);

                int filas = await command.ExecuteNonQueryAsync();
                if (filas == 0) return NotFound();

                TempData["Ok"] = $"Producto \"{producto.Nombre}\" actualizado.";
                return RedirectToAction(nameof(Index));
            }
            catch (MySqlException ex) when (ex.Number == 1452)
            {
                ModelState.AddModelError(nameof(Producto.IdCategoria), "La categoría seleccionada no existe.");
            }
            catch (MySqlException ex)
            {
                ModelState.AddModelError(string.Empty, "Error de base de datos: " + ex.Message);
            }
        }

        await CargarListasAsync();
        return View(producto);
    }

    // ---------- ELIMINAR ----------
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Eliminar(int id)
    {
        const string query = "DELETE FROM Producto WHERE id_producto = @id;";

        try
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@id", id);

            int filas = await command.ExecuteNonQueryAsync();
            TempData["Ok"] = filas > 0 ? "Producto eliminado." : "El producto ya no existe.";
        }
        catch (MySqlException ex) when (ex.Number == 1451) // tiene ingresos o ventas asociados
        {
            TempData["Error"] = "No se puede eliminar: el producto tiene ingresos de inventario o ventas asociados. " +
                                "Edítelo y márquelo como \"Descontinuado\" para conservar el historial.";
        }
        catch (MySqlException ex)
        {
            TempData["Error"] = "Error de base de datos: " + ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    // ---------- AUXILIARES ----------
    private void ValidarEstado(Producto producto)
    {
        if (!EstadosValidos.Contains(producto.Estado))
        {
            ModelState.AddModelError(nameof(Producto.Estado), "Estado no válido.");
        }
    }

    private static void AgregarParametros(MySqlCommand command, Producto p)
    {
        command.Parameters.AddWithValue("@categoria", p.IdCategoria);
        command.Parameters.AddWithValue("@nombre", p.Nombre.Trim());
        command.Parameters.AddWithValue("@marca", TextoONulo(p.Marca));
        command.Parameters.AddWithValue("@modelo", TextoONulo(p.Modelo));
        command.Parameters.AddWithValue("@descripcion", TextoONulo(p.Descripcion));
        command.Parameters.AddWithValue("@compra", p.PrecioCompra);
        command.Parameters.AddWithValue("@venta", p.PrecioVenta);
        command.Parameters.AddWithValue("@estado", p.Estado);
    }

    private static object TextoONulo(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return DBNull.Value;
        return valor.Trim();
    }

    private static Producto LeerProducto(MySqlDataReader reader)
    {
        string? Texto(string columna)
        {
            int i = reader.GetOrdinal(columna);
            return reader.IsDBNull(i) ? null : reader.GetString(i);
        }

        int stockOrdinal = reader.GetOrdinal("stock_actual");

        return new Producto
        {
            IdProducto = reader.GetInt32(reader.GetOrdinal("id_producto")),
            IdCategoria = reader.GetInt32(reader.GetOrdinal("id_categoria")),
            CategoriaNombre = Texto("categoria"),
            Nombre = Texto("nombre") ?? string.Empty,
            Marca = Texto("marca"),
            Modelo = Texto("modelo"),
            Descripcion = Texto("descripcion"),
            PrecioCompra = reader.GetDecimal(reader.GetOrdinal("precio_compra")),
            PrecioVenta = reader.GetDecimal(reader.GetOrdinal("precio_venta")),
            StockActual = reader.IsDBNull(stockOrdinal) ? 0 : reader.GetInt32(stockOrdinal),
            Estado = Texto("estado") ?? "Activo"
        };
    }

    private async Task<Producto?> ObtenerProductoAsync(int id)
    {
        string query = SelectProducto + " WHERE p.id_producto = @id;";

        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand(query, connection);
        command.Parameters.AddWithValue("@id", id);

        using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? LeerProducto(reader) : null;
    }

    private async Task<List<Categoria>> ObtenerCategoriasAsync()
    {
        var lista = new List<Categoria>();

        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        using var command = new MySqlCommand("SELECT id_categoria, nombre FROM Categoria ORDER BY nombre;", connection);
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lista.Add(new Categoria
            {
                IdCategoria = reader.GetInt32(0),
                Nombre = reader.GetString(1)
            });
        }

        return lista;
    }

    private async Task CargarListasAsync()
    {
        var categorias = await ObtenerCategoriasAsync();
        ViewBag.Categorias = new SelectList(categorias, nameof(Categoria.IdCategoria), nameof(Categoria.Nombre));
        ViewBag.Estados = new SelectList(EstadosValidos);
    }
}
