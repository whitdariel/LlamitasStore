namespace LlamitasStore.Models;

public class PermisoRowViewModel
{
    public Permiso Permiso { get; set; } = new();
    public string NombreRol { get; set; } = string.Empty;
    public string NombreModulo { get; set; } = string.Empty;
}