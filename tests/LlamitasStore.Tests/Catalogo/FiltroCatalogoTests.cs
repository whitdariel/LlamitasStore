using LlamitasStore.ViewModels;

namespace LlamitasStore.Tests.Catalogo;

public class FiltroCatalogoTests
{
    [Fact]
    public void Normalizar_corrige_valores_invalidos()
    {
        var f = new FiltroCatalogo
        {
            Q = "   ", Categoria = " Procesadores ", Marca = ["AMD", "amd", " ", "Intel"],
            Min = 500, Max = 100, Valoracion = 9, Orden = "inventado", Pagina = -3
        }.Normalizar();

        Assert.Null(f.Q);
        Assert.Equal("procesadores", f.Categoria);
        Assert.Equal(["amd", "intel"], f.Marca);
        Assert.Equal((100m, 500m), (f.Min!.Value, f.Max!.Value)); // se intercambian
        Assert.Null(f.Valoracion);
        Assert.Equal(OrdenCatalogo.Relevancia, f.Orden);
        Assert.Equal(1, f.Pagina);
    }

    [Fact]
    public void Busqueda_muy_larga_se_recorta()
    {
        var f = new FiltroCatalogo { Q = new string('a', 500) }.Normalizar();
        Assert.Equal(FiltroCatalogo.LargoMaximoBusqueda, f.Q!.Length);
    }

    [Fact]
    public void Url_conserva_filtros_y_reinicia_la_pagina()
    {
        var f = new FiltroCatalogo
        {
            Categoria = "procesadores", Q = "ryzen 7", Marca = ["amd"], Min = 100.5m, Disponible = true,
            Orden = OrdenCatalogo.PrecioAsc, Pagina = 4
        }.Normalizar();

        Assert.Equal("/catalogo/procesadores?q=ryzen%207&marca=amd&min=100.5&disponible=true&orden=precio-asc",
            f.Url());
        Assert.Equal("/catalogo/procesadores?q=ryzen%207&marca=amd&marca=intel&min=100.5&disponible=true&orden=precio-asc",
            f.Url(x => x.Marca.Add("intel")));
        Assert.Equal("/catalogo/procesadores?q=ryzen%207&marca=amd&min=100.5&disponible=true&orden=precio-asc&pagina=5",
            f.Url(x => x.Pagina = 5, reiniciarPagina: false));
    }

    [Fact]
    public void Url_no_modifica_el_filtro_original()
    {
        var f = new FiltroCatalogo { Marca = ["amd"] }.Normalizar();
        _ = f.Url(x => x.Marca.Clear());
        Assert.Equal(["amd"], f.Marca);
    }
}
