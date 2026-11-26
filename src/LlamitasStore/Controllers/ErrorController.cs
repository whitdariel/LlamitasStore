using System.Diagnostics;
using LlamitasStore.Models;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LlamitasStore.Controllers;

/// <summary>Manejo centralizado de errores: excepciones no controladas y códigos HTTP (404, 403, 429…).</summary>
[Route("error")]
[ApiExplorerSettings(IgnoreApi = true)]
public class ErrorController : Controller
{
    private readonly ILogger<ErrorController> _logger;

    public ErrorController(ILogger<ErrorController> logger) => _logger = logger;

    [Route("{codigo:int}")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Index(int codigo)
    {
        if (codigo == 500)
        {
            var excepcion = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
            if (excepcion is not null)
                _logger.LogError(excepcion.Error, "Error no controlado en {Ruta}", excepcion.Path);
        }

        Response.StatusCode = codigo;
        return View("~/Views/Shared/Error.cshtml", new ErrorViewModel
        {
            Codigo = codigo,
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
        });
    }
}
