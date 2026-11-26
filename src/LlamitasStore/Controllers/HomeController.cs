    using System.Diagnostics;
    using LlamitasStore.Data;
    using LlamitasStore.Models;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.EntityFrameworkCore;

    namespace LlamitasStore.Controllers;

    public class HomeController : Controller
    {
        private const string DatabaseSetupMessage =
            "La base de datos todavía no está lista. Importa LLamitasbasededatos.sql en la instancia MySQL configurada en appsettings.json y vuelve a cargar esta página. El volcado indica el puerto 3307; la configuración actual del proyecto usa 3306.";

        private readonly ILogger<HomeController> _logger;
        private readonly AppDbContext _context;

        public HomeController(ILogger<HomeController> logger, AppDbContext context)
        {
            _logger = logger;
            _context = context;
        }

       public IActionResult Index() => View();

        public IActionResult Privacy() => View();


        public IActionResult Usuario()
        {
            try
            {
                ViewBag.Roles = _context.Roles.AsNoTracking().OrderBy(rol => rol.NombreRol).ToList();
                var usuarios = _context.Usuarios
                    .AsNoTracking()
                    .Include(usuario => usuario.Rol)
                    .OrderBy(usuario => usuario.Apellido)
                    .ThenBy(usuario => usuario.Nombre)
                    .ToList();

                ViewBag.DatabaseReady = true;
                return View(usuarios);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "No se pudo cargar la lista de usuarios.");
                ViewBag.Roles = new List<Rol>();
                ViewBag.DatabaseReady = false;
                ViewBag.DatabaseMessage = DatabaseSetupMessage;
                return View(new List<Usuario>());
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CrearUsuario(Usuario usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario.Nombre)
                || string.IsNullOrWhiteSpace(usuario.Apellido)
                || string.IsNullOrWhiteSpace(usuario.Email)
                || string.IsNullOrWhiteSpace(usuario.Contrasena)
                || usuario.RolId <= 0)
            {
                TempData["Error"] = "Completa los datos obligatorios y selecciona un rol.";
                return RedirectToAction(nameof(Usuario));
            }

            try
            {
                if (!_context.Roles.Any(rol => rol.Id == usuario.RolId))
                {
                    TempData["Error"] = "El rol seleccionado ya no existe. Actualiza la página e inténtalo de nuevo.";
                    return RedirectToAction(nameof(Usuario));
                }

                _context.Usuarios.Add(usuario);
                _context.SaveChanges();
                TempData["Success"] = "El usuario se registró correctamente.";
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "No se pudo registrar el usuario.");
                TempData["Error"] = "No se pudo guardar el usuario. Comprueba la conexión y que el correo no esté repetido.";
            }

            return RedirectToAction(nameof(Usuario));
        }

        public IActionResult Rol()
        {
            try
            {
                var roles = _context.Roles.AsNoTracking().OrderBy(rol => rol.NombreRol).ToList();
                ViewBag.DatabaseReady = true;
                return View(roles);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "No se pudo cargar la lista de roles.");
                ViewBag.DatabaseReady = false;
                ViewBag.DatabaseMessage = DatabaseSetupMessage;
                return View(new List<Rol>());
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CrearRol(Rol rol)
        {
            if (string.IsNullOrWhiteSpace(rol.NombreRol))
            {
                TempData["Error"] = "Escribe un nombre para el rol.";
                return RedirectToAction(nameof(Rol));
            }

            try
            {
                rol.NombreRol = rol.NombreRol.Trim();
                rol.Descripcion = rol.Descripcion?.Trim();
                _context.Roles.Add(rol);
                _context.SaveChanges();
                TempData["Success"] = "El rol se creó correctamente.";
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "No se pudo crear el rol.");
                TempData["Error"] = "No se pudo guardar el rol. Comprueba que la base de datos esté disponible.";
            }

            return RedirectToAction(nameof(Rol));
        }

        public IActionResult Permiso()
{
    try
    {
        var permisos = _context.Permisos.AsNoTracking().ToList();
        return View(permisos);
    }
    catch (Exception exception)
    {
        _logger.LogWarning(exception, "No se pudo cargar la lista de permisos.");
        return View(new List<Permiso>());
    }
}

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CrearPermiso(Permiso permiso)
        {
            try
            {
                var rolExiste = _context.Roles.Any(rol => rol.Id == permiso.IdRol);
                var moduloExiste = _context.Modulos.Any(modulo => modulo.Id == permiso.IdModulo);
                if (!rolExiste || !moduloExiste)
                {
                    TempData["Error"] = "Selecciona un rol y un módulo válidos.";
                    return RedirectToAction(nameof(Permiso));
                }

                if (_context.Permisos.Any(actual => actual.IdRol == permiso.IdRol && actual.IdModulo == permiso.IdModulo))
                {
                    TempData["Error"] = "Ya existe una regla para ese rol y módulo.";
                    return RedirectToAction(nameof(Permiso));
                }

                _context.Permisos.Add(permiso);
                _context.SaveChanges();
                TempData["Success"] = "El permiso se guardó correctamente.";
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "No se pudo guardar el permiso.");
                TempData["Error"] = "No se pudo guardar el permiso. Comprueba la conexión con la base de datos.";
            }

            return RedirectToAction(nameof(Permiso));
        }

        public IActionResult Categoria()
        {
            try
            {
                ViewBag.DatabaseReady = true;
                return View(_context.Categorias.AsNoTracking().OrderBy(categoria => categoria.NombreCategoria).ToList());
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "No se pudo cargar la lista de categorías.");
                ViewBag.DatabaseReady = false;
                ViewBag.DatabaseMessage = DatabaseSetupMessage;
                return View(new List<Categoria>());
            }
        }

        public IActionResult Modulo()
        {
            try
            {
                ViewBag.Categorias = _context.Categorias.AsNoTracking().OrderBy(categoria => categoria.NombreCategoria).ToList();
                var modulos = _context.Modulos.AsNoTracking()
                    .Include(modulo => modulo.Categoria)
                    .OrderBy(modulo => modulo.Nombre)
                    .ToList();
                ViewBag.DatabaseReady = true;
                return View(modulos);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "No se pudo cargar la lista de módulos.");
                ViewBag.Categorias = new List<Categoria>();
                ViewBag.DatabaseReady = false;
                ViewBag.DatabaseMessage = DatabaseSetupMessage;
                return View(new List<Modulo>());
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CrearModulo(Modulo modulo)
        {
            if (string.IsNullOrWhiteSpace(modulo.Nombre) || modulo.CategoriaId <= 0)
            {
                TempData["Error"] = "Escribe un nombre y selecciona una categoría para el módulo.";
                return RedirectToAction(nameof(Modulo));
            }

            try
            {
                modulo.Nombre = modulo.Nombre.Trim();
                modulo.Descripcion = modulo.Descripcion?.Trim();
                if (!_context.Categorias.Any(categoria => categoria.Id == modulo.CategoriaId))
                {
                    TempData["Error"] = "La categoría seleccionada ya no existe. Actualiza la página e inténtalo de nuevo.";
                    return RedirectToAction(nameof(Modulo));
                }

                _context.Modulos.Add(modulo);
                _context.SaveChanges();
                TempData["Success"] = "El módulo se creó correctamente.";
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "No se pudo crear el módulo.");
                TempData["Error"] = "No se pudo guardar el módulo. Comprueba la conexión con la base de datos.";
            }

            return RedirectToAction(nameof(Modulo));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CrearCategoria(Categoria categoria)
        {
            if (string.IsNullOrWhiteSpace(categoria.NombreCategoria))
            {
                TempData["Error"] = "Escribe un nombre para la categoría.";
                return RedirectToAction(nameof(Categoria));
            }

            try
            {
                categoria.NombreCategoria = categoria.NombreCategoria.Trim();
                categoria.Descripcion = categoria.Descripcion?.Trim();
                _context.Categorias.Add(categoria);
                _context.SaveChanges();
                TempData["Success"] = "La categoría se creó correctamente.";
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "No se pudo crear la categoría.");
                TempData["Error"] = "No se pudo guardar la categoría. Comprueba la conexión con la base de datos.";
            }

            return RedirectToAction(nameof(Categoria));
        }

        public IActionResult Marca() => View(new List<Marca>());
        public IActionResult OrdenCompra() => View(new List<OrdenCompra>());
        public IActionResult Inventario() => View();
        public IActionResult Movimientos() => View();
        public IActionResult HistorialPrecios() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
