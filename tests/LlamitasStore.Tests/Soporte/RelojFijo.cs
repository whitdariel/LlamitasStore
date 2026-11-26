namespace LlamitasStore.Tests.Soporte;

/// <summary>Reloj controlable para probar vigencias (promociones, cupones).</summary>
public sealed class RelojFijo : TimeProvider
{
    private DateTimeOffset _ahora;
    public RelojFijo(DateTime utc) => _ahora = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    public override DateTimeOffset GetUtcNow() => _ahora;
    public void Avanzar(TimeSpan t) => _ahora = _ahora.Add(t);
}
