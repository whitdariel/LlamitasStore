using LlamitasStore.Models.Entities;
using LlamitasStore.Utilidades;
using Microsoft.EntityFrameworkCore;

namespace LlamitasStore.Data.Semilla;

/// <summary>
/// Catálogo de demostración. Las especificaciones técnicas se tomaron de las fichas públicas de cada
/// fabricante, pero los PRECIOS Y EL STOCK SON FICTICIOS. Antes de vender, verifica cada ficha.
/// Ventas, valoraciones y reseñas empiezan en cero: solo se llenan con operaciones reales.
/// </summary>
internal static class CatalogoDemo
{
    private sealed record Cat(string Nombre, string Slug, string Icono, TipoComponente Tipo, string Descripcion);

    private sealed record P(
        string Sku, string Nombre, string Marca, string Categoria, decimal Precio, decimal? Oferta, int Stock,
        string Corta, string Descripcion, (string Grupo, string Nombre, string Valor)[] Specs,
        Action<ProductoCompatibilidad>? Compat = null, bool Destacado = false, bool Nuevo = false, string? Modelo = null);

    private static readonly Cat[] Categorias =
    [
        new("Procesadores", "procesadores", "cpu", TipoComponente.Procesador, "CPUs AMD Ryzen e Intel Core para gaming y productividad."),
        new("Tarjetas gráficas", "tarjetas-graficas", "gpu", TipoComponente.TarjetaGrafica, "GPUs NVIDIA GeForce y AMD Radeon."),
        new("Placas madre", "placas-madre", "placa", TipoComponente.PlacaMadre, "Motherboards AM4, AM5 y LGA1700."),
        new("Memorias RAM", "memorias-ram", "ram", TipoComponente.MemoriaRam, "Kits DDR4 y DDR5 de alto rendimiento."),
        new("Almacenamiento", "almacenamiento", "ssd", TipoComponente.Almacenamiento, "SSD NVMe, SSD SATA y discos duros."),
        new("Fuentes de poder", "fuentes-de-poder", "psu", TipoComponente.FuentePoder, "Fuentes certificadas 80 PLUS."),
        new("Gabinetes", "gabinetes", "gabinete", TipoComponente.Gabinete, "Cases ATX, Micro-ATX y Mini-ITX."),
        new("Refrigeración", "refrigeracion", "cooler", TipoComponente.Refrigeracion, "Disipadores por aire para CPU."),
        new("Monitores", "monitores", "monitor", TipoComponente.Ninguno, "Monitores gaming de alta frecuencia."),
        new("Teclados", "teclados", "teclado", TipoComponente.Ninguno, "Teclados mecánicos y de membrana."),
        new("Ratones", "ratones", "raton", TipoComponente.Ninguno, "Ratones gaming con y sin cable."),
        new("Auriculares", "auriculares", "auriculares", TipoComponente.Ninguno, "Headsets gaming con micrófono."),
        new("Accesorios gaming", "accesorios-gaming", "accesorio", TipoComponente.Ninguno, "Mousepads, soportes y accesorios para tu setup.")
    ];

    private static readonly (string Nombre, bool Destacada, string Descripcion)[] Marcas =
    [
        ("AMD", true, "Procesadores Ryzen y gráficas Radeon"),
        ("Intel", true, "Procesadores Intel Core"),
        ("ASUS", true, "Placas madre, gráficas y periféricos ROG/TUF"),
        ("MSI", true, "Placas madre y tarjetas gráficas"),
        ("GIGABYTE", false, "Placas madre y tarjetas gráficas AORUS"),
        ("Sapphire", false, "Tarjetas gráficas AMD Radeon"),
        ("Corsair", true, "Memorias, fuentes, gabinetes y periféricos"),
        ("Kingston", false, "Memorias y almacenamiento FURY"),
        ("Samsung", true, "SSD y monitores Odyssey"),
        ("Crucial", false, "Memorias y SSD"),
        ("Seagate", false, "Discos duros BarraCuda"),
        ("Seasonic", false, "Fuentes de poder"),
        ("Cooler Master", false, "Gabinetes, fuentes y refrigeración"),
        ("NZXT", false, "Gabinetes y refrigeración"),
        ("DeepCool", false, "Refrigeración por aire"),
        ("Thermalright", false, "Disipadores de alto rendimiento"),
        ("Noctua", false, "Ventiladores y disipadores silenciosos"),
        ("LG", false, "Monitores UltraGear"),
        ("AOC", false, "Monitores gaming"),
        ("Redragon", false, "Periféricos gaming"),
        ("HyperX", false, "Teclados y auriculares gaming"),
        ("Logitech", true, "Periféricos Logitech G"),
        ("Razer", true, "Periféricos gaming"),
        ("Elgato", false, "Equipos para streaming")
    ];

    private static IEnumerable<P> Productos()
    {
        // ---------- Procesadores ----------
        yield return new("LS-CPU-0001", "AMD Ryzen 5 7600", "AMD", "procesadores", 1690m, 1590m, 14,
            "6 núcleos y 12 hilos, socket AM5, DDR5, gráficos integrados y disipador incluido.",
            "Procesador Zen 4 de 6 núcleos ideal para gaming en 1080p y 1440p. Incluye disipador Wraith Stealth y gráficos Radeon integrados para uso básico.",
            [("Procesador", "Núcleos / hilos", "6 / 12"), ("Procesador", "Frecuencia base / turbo", "3.8 GHz / 5.1 GHz"),
             ("Procesador", "Caché L3", "32 MB"), ("Plataforma", "Socket", "AM5"), ("Plataforma", "Memoria", "DDR5"),
             ("Energía", "TDP", "65 W"), ("Energía", "Potencia máxima (PPT)", "88 W"), ("Extras", "Gráficos integrados", "Radeon (2 CU)"),
             ("Extras", "Disipador incluido", "Wraith Stealth")],
            c => { c.Socket = "AM5"; c.TiposMemoria = "DDR5"; c.ConsumoW = 88; c.GraficosIntegrados = true; c.IncluyeCooler = true; },
            Destacado: true, Modelo: "100-100001015BOX");

        yield return new("LS-CPU-0002", "AMD Ryzen 7 7800X3D", "AMD", "procesadores", 3490m, null, 8,
            "8 núcleos con 3D V-Cache, el referente para gaming en AM5.",
            "Procesador de 8 núcleos con 96 MB de caché L3 gracias a la tecnología 3D V-Cache. No incluye disipador.",
            [("Procesador", "Núcleos / hilos", "8 / 16"), ("Procesador", "Frecuencia base / turbo", "4.2 GHz / 5.0 GHz"),
             ("Procesador", "Caché L3", "96 MB"), ("Plataforma", "Socket", "AM5"), ("Plataforma", "Memoria", "DDR5"),
             ("Energía", "TDP", "120 W"), ("Energía", "Potencia máxima (PPT)", "162 W"), ("Extras", "Gráficos integrados", "Radeon (2 CU)"),
             ("Extras", "Disipador incluido", "No")],
            c => { c.Socket = "AM5"; c.TiposMemoria = "DDR5"; c.ConsumoW = 162; c.GraficosIntegrados = true; c.IncluyeCooler = false; },
            Destacado: true, Modelo: "100-100000910WOF");

        yield return new("LS-CPU-0003", "AMD Ryzen 5 5600", "AMD", "procesadores", 890m, null, 20,
            "6 núcleos Zen 3 para AM4 con DDR4. Excelente relación precio/rendimiento.",
            "Procesador Zen 3 de 6 núcleos para la plataforma AM4. Requiere tarjeta gráfica dedicada. Incluye disipador Wraith Stealth.",
            [("Procesador", "Núcleos / hilos", "6 / 12"), ("Procesador", "Frecuencia base / turbo", "3.5 GHz / 4.4 GHz"),
             ("Procesador", "Caché L3", "32 MB"), ("Plataforma", "Socket", "AM4"), ("Plataforma", "Memoria", "DDR4"),
             ("Energía", "TDP", "65 W"), ("Energía", "Potencia máxima (PPT)", "76 W"), ("Extras", "Gráficos integrados", "No"),
             ("Extras", "Disipador incluido", "Wraith Stealth")],
            c => { c.Socket = "AM4"; c.TiposMemoria = "DDR4"; c.ConsumoW = 76; c.GraficosIntegrados = false; c.IncluyeCooler = true; },
            Modelo: "100-100000927BOX");

        yield return new("LS-CPU-0004", "Intel Core i5-14600K", "Intel", "procesadores", 2390m, null, 9,
            "14 núcleos (6P + 8E), desbloqueado, socket LGA1700, DDR4/DDR5.",
            "Procesador Intel de 14.ª generación con multiplicador desbloqueado. Compatible con placas DDR4 o DDR5 según el modelo de placa. No incluye disipador.",
            [("Procesador", "Núcleos / hilos", "14 (6P + 8E) / 20"), ("Procesador", "Frecuencia turbo máx.", "5.3 GHz"),
             ("Procesador", "Caché", "24 MB Intel Smart Cache"), ("Plataforma", "Socket", "LGA1700"), ("Plataforma", "Memoria", "DDR4 o DDR5"),
             ("Energía", "Potencia base", "125 W"), ("Energía", "Potencia turbo máxima", "181 W"),
             ("Extras", "Gráficos integrados", "Intel UHD 770"), ("Extras", "Disipador incluido", "No")],
            c => { c.Socket = "LGA1700"; c.TiposMemoria = "DDR4,DDR5"; c.ConsumoW = 181; c.GraficosIntegrados = true; c.IncluyeCooler = false; },
            Nuevo: true, Modelo: "BX8071514600K");

        yield return new("LS-CPU-0005", "Intel Core i5-12400F", "Intel", "procesadores", 990m, 920m, 16,
            "6 núcleos y 12 hilos, LGA1700, disipador incluido. Requiere GPU dedicada.",
            "Procesador de 12.ª generación muy eficiente para gaming. Sin gráficos integrados (versión F). Incluye disipador Intel Laminar RM1.",
            [("Procesador", "Núcleos / hilos", "6 / 12"), ("Procesador", "Frecuencia base / turbo", "2.5 GHz / 4.4 GHz"),
             ("Procesador", "Caché", "18 MB Intel Smart Cache"), ("Plataforma", "Socket", "LGA1700"), ("Plataforma", "Memoria", "DDR4 o DDR5"),
             ("Energía", "Potencia base", "65 W"), ("Energía", "Potencia turbo máxima", "117 W"),
             ("Extras", "Gráficos integrados", "No"), ("Extras", "Disipador incluido", "Intel Laminar RM1")],
            c => { c.Socket = "LGA1700"; c.TiposMemoria = "DDR4,DDR5"; c.ConsumoW = 117; c.GraficosIntegrados = false; c.IncluyeCooler = true; },
            Modelo: "BX8071512400F");

        // ---------- Placas madre ----------
        yield return new("LS-MB-0001", "ASUS TUF GAMING B650-PLUS WIFI", "ASUS", "placas-madre", 1790m, null, 7,
            "ATX AM5 con DDR5, Wi-Fi 6, 3 ranuras M.2 y 4 SATA.",
            "Placa madre robusta para Ryzen 7000/8000/9000 con VRM reforzado, Wi-Fi 6 y PCIe 5.0 en M.2.",
            [("Plataforma", "Socket", "AM5"), ("Plataforma", "Chipset", "AMD B650"), ("Formato", "Factor de forma", "ATX"),
             ("Memoria", "Tipo", "DDR5"), ("Memoria", "Ranuras", "4"), ("Memoria", "Máximo", "192 GB"),
             ("Almacenamiento", "Ranuras M.2", "3"), ("Almacenamiento", "Puertos SATA", "4"), ("Conectividad", "Red", "Wi-Fi 6, 2.5 Gb LAN")],
            c => { c.Socket = "AM5"; c.FactorForma = "ATX"; c.TiposMemoria = "DDR5"; c.SlotsMemoria = 4; c.MemoriaMaximaGb = 192; c.SlotsM2 = 3; c.PuertosSata = 4; },
            Destacado: true);

        yield return new("LS-MB-0002", "MSI PRO B650M-A WIFI", "MSI", "placas-madre", 1390m, null, 10,
            "Micro-ATX AM5 con DDR5, Wi-Fi 6E, 2 ranuras M.2.",
            "Placa Micro-ATX para AM5 con buena relación precio/prestaciones, ideal para builds compactos.",
            [("Plataforma", "Socket", "AM5"), ("Plataforma", "Chipset", "AMD B650"), ("Formato", "Factor de forma", "Micro-ATX"),
             ("Memoria", "Tipo", "DDR5"), ("Memoria", "Ranuras", "4"), ("Memoria", "Máximo", "192 GB"),
             ("Almacenamiento", "Ranuras M.2", "2"), ("Almacenamiento", "Puertos SATA", "4"), ("Conectividad", "Red", "Wi-Fi 6E, 2.5 Gb LAN")],
            c => { c.Socket = "AM5"; c.FactorForma = "Micro-ATX"; c.TiposMemoria = "DDR5"; c.SlotsMemoria = 4; c.MemoriaMaximaGb = 192; c.SlotsM2 = 2; c.PuertosSata = 4; });

        yield return new("LS-MB-0003", "MSI MAG B550 TOMAHAWK", "MSI", "placas-madre", 1190m, 1090m, 6,
            "ATX AM4 con DDR4, 2 ranuras M.2 y 6 puertos SATA.",
            "Placa ATX para Ryzen 3000/5000 con excelente VRM y abundantes puertos de almacenamiento.",
            [("Plataforma", "Socket", "AM4"), ("Plataforma", "Chipset", "AMD B550"), ("Formato", "Factor de forma", "ATX"),
             ("Memoria", "Tipo", "DDR4"), ("Memoria", "Ranuras", "4"), ("Memoria", "Máximo", "128 GB"),
             ("Almacenamiento", "Ranuras M.2", "2"), ("Almacenamiento", "Puertos SATA", "6"), ("Conectividad", "Red", "2.5 Gb LAN + 1 Gb LAN")],
            c => { c.Socket = "AM4"; c.FactorForma = "ATX"; c.TiposMemoria = "DDR4"; c.SlotsMemoria = 4; c.MemoriaMaximaGb = 128; c.SlotsM2 = 2; c.PuertosSata = 6; });

        yield return new("LS-MB-0004", "GIGABYTE B760M DS3H DDR4", "GIGABYTE", "placas-madre", 890m, null, 12,
            "Micro-ATX LGA1700 con DDR4, ideal para Intel 12.ª a 14.ª generación.",
            "Placa Micro-ATX económica para procesadores Intel LGA1700 con memoria DDR4.",
            [("Plataforma", "Socket", "LGA1700"), ("Plataforma", "Chipset", "Intel B760"), ("Formato", "Factor de forma", "Micro-ATX"),
             ("Memoria", "Tipo", "DDR4"), ("Memoria", "Ranuras", "4"), ("Memoria", "Máximo", "128 GB"),
             ("Almacenamiento", "Ranuras M.2", "2"), ("Almacenamiento", "Puertos SATA", "4")],
            c => { c.Socket = "LGA1700"; c.FactorForma = "Micro-ATX"; c.TiposMemoria = "DDR4"; c.SlotsMemoria = 4; c.MemoriaMaximaGb = 128; c.SlotsM2 = 2; c.PuertosSata = 4; });

        yield return new("LS-MB-0005", "ASUS PRIME Z790-P WIFI", "ASUS", "placas-madre", 1990m, null, 5,
            "ATX LGA1700 con chipset Z790, DDR5 y Wi-Fi 6.",
            "Placa ATX con chipset Z790 para overclock de procesadores Intel K, memoria DDR5 y tres ranuras M.2.",
            [("Plataforma", "Socket", "LGA1700"), ("Plataforma", "Chipset", "Intel Z790"), ("Formato", "Factor de forma", "ATX"),
             ("Memoria", "Tipo", "DDR5"), ("Memoria", "Ranuras", "4"), ("Memoria", "Máximo", "192 GB"),
             ("Almacenamiento", "Ranuras M.2", "3"), ("Almacenamiento", "Puertos SATA", "4"), ("Conectividad", "Red", "Wi-Fi 6, 2.5 Gb LAN")],
            c => { c.Socket = "LGA1700"; c.FactorForma = "ATX"; c.TiposMemoria = "DDR5"; c.SlotsMemoria = 4; c.MemoriaMaximaGb = 192; c.SlotsM2 = 3; c.PuertosSata = 4; },
            Nuevo: true);

        // ---------- Memorias RAM ----------
        yield return new("LS-RAM-0001", "Kingston FURY Beast 32GB (2x16GB) DDR5-6000 CL36", "Kingston", "memorias-ram", 890m, null, 18,
            "Kit DDR5 de 32 GB a 6000 MT/s con perfiles Intel XMP 3.0 y AMD EXPO.",
            "Kit de dos módulos DDR5 con disipador de bajo perfil, listo para plataformas AM5 y LGA1700 DDR5.",
            [("Memoria", "Tipo", "DDR5"), ("Memoria", "Capacidad", "32 GB (2 x 16 GB)"), ("Memoria", "Velocidad", "6000 MT/s"),
             ("Memoria", "Latencia", "CL36"), ("Memoria", "Perfiles", "XMP 3.0 / EXPO")],
            c => { c.TiposMemoria = "DDR5"; c.CapacidadGb = 32; c.Modulos = 2; c.VelocidadMhz = 6000; });

        yield return new("LS-RAM-0002", "Corsair Vengeance 32GB (2x16GB) DDR5-6000 CL30", "Corsair", "memorias-ram", 990m, 940m, 15,
            "Kit DDR5 de baja latencia CL30, el punto dulce para Ryzen 7000.",
            "Kit de memoria DDR5 de 32 GB a 6000 MT/s con latencia CL30.",
            [("Memoria", "Tipo", "DDR5"), ("Memoria", "Capacidad", "32 GB (2 x 16 GB)"), ("Memoria", "Velocidad", "6000 MT/s"),
             ("Memoria", "Latencia", "CL30"), ("Memoria", "Modelo", "CMK32GX5M2B6000C30")],
            c => { c.TiposMemoria = "DDR5"; c.CapacidadGb = 32; c.Modulos = 2; c.VelocidadMhz = 6000; },
            Destacado: true);

        yield return new("LS-RAM-0003", "Corsair Vengeance LPX 16GB (2x8GB) DDR4-3200 CL16", "Corsair", "memorias-ram", 390m, null, 25,
            "Kit DDR4 de 16 GB a 3200 MHz de perfil bajo.",
            "Memoria DDR4 compatible con plataformas AM4 e Intel DDR4.",
            [("Memoria", "Tipo", "DDR4"), ("Memoria", "Capacidad", "16 GB (2 x 8 GB)"), ("Memoria", "Velocidad", "3200 MHz"),
             ("Memoria", "Latencia", "CL16"), ("Memoria", "Modelo", "CMK16GX4M2B3200C16")],
            c => { c.TiposMemoria = "DDR4"; c.CapacidadGb = 16; c.Modulos = 2; c.VelocidadMhz = 3200; });

        yield return new("LS-RAM-0004", "Kingston FURY Beast 32GB (2x16GB) DDR4-3200 CL16", "Kingston", "memorias-ram", 650m, null, 14,
            "Kit DDR4 de 32 GB para gaming y creación de contenido.",
            "Kit de dos módulos DDR4 con disipador y perfiles XMP.",
            [("Memoria", "Tipo", "DDR4"), ("Memoria", "Capacidad", "32 GB (2 x 16 GB)"), ("Memoria", "Velocidad", "3200 MHz"),
             ("Memoria", "Latencia", "CL16")],
            c => { c.TiposMemoria = "DDR4"; c.CapacidadGb = 32; c.Modulos = 2; c.VelocidadMhz = 3200; });

        // ---------- Tarjetas gráficas ----------
        yield return new("LS-GPU-0001", "MSI GeForce RTX 4060 VENTUS 2X BLACK 8G OC", "MSI", "tarjetas-graficas", 2690m, null, 10,
            "RTX 4060 de 8 GB compacta (199 mm) con DLSS 3.",
            "Tarjeta gráfica compacta de doble ventilador, ideal para gaming en 1080p con trazado de rayos y DLSS 3.",
            [("GPU", "Chip", "NVIDIA GeForce RTX 4060"), ("GPU", "Memoria", "8 GB GDDR6"), ("Dimensiones", "Largo", "199 mm"),
             ("Energía", "Consumo de la tarjeta", "115 W"), ("Energía", "Fuente recomendada", "550 W"), ("Conectividad", "Salidas", "3x DisplayPort 1.4a, 1x HDMI 2.1a")],
            c => { c.ConsumoW = 115; c.FuenteRecomendadaW = 550; c.LargoMm = 199; },
            Destacado: true);

        yield return new("LS-GPU-0002", "ASUS Dual GeForce RTX 4070 SUPER OC 12GB", "ASUS", "tarjetas-graficas", 5290m, 4990m, 6,
            "RTX 4070 SUPER de 12 GB para 1440p de alto refresco.",
            "Gráfica de doble ventilador con 12 GB GDDR6X, excelente para 1440p con trazado de rayos.",
            [("GPU", "Chip", "NVIDIA GeForce RTX 4070 SUPER"), ("GPU", "Memoria", "12 GB GDDR6X"), ("Dimensiones", "Largo", "267 mm"),
             ("Energía", "Consumo de la tarjeta", "220 W"), ("Energía", "Fuente recomendada", "650 W")],
            c => { c.ConsumoW = 220; c.FuenteRecomendadaW = 650; c.LargoMm = 267; },
            Destacado: true);

        yield return new("LS-GPU-0003", "Sapphire PULSE Radeon RX 7600 8GB", "Sapphire", "tarjetas-graficas", 2290m, null, 8,
            "Radeon RX 7600 de 8 GB para 1080p.",
            "Tarjeta gráfica AMD RDNA 3 con doble ventilador, pensada para gaming 1080p.",
            [("GPU", "Chip", "AMD Radeon RX 7600"), ("GPU", "Memoria", "8 GB GDDR6"), ("Dimensiones", "Largo", "240 mm"),
             ("Energía", "Consumo de la tarjeta", "165 W"), ("Energía", "Fuente recomendada", "550 W")],
            c => { c.ConsumoW = 165; c.FuenteRecomendadaW = 550; c.LargoMm = 240; });

        yield return new("LS-GPU-0004", "GIGABYTE GeForce RTX 4070 Ti SUPER GAMING OC 16G", "GIGABYTE", "tarjetas-graficas", 7490m, null, 3,
            "RTX 4070 Ti SUPER de 16 GB y triple ventilador (336 mm).",
            "Gráfica de gama alta con 16 GB GDDR6X y disipación WINDFORCE de tres ventiladores. Verifica el espacio de tu gabinete: mide 336 mm.",
            [("GPU", "Chip", "NVIDIA GeForce RTX 4070 Ti SUPER"), ("GPU", "Memoria", "16 GB GDDR6X"), ("Dimensiones", "Largo", "336 mm"),
             ("Energía", "Consumo de la tarjeta", "285 W"), ("Energía", "Fuente recomendada", "750 W")],
            c => { c.ConsumoW = 285; c.FuenteRecomendadaW = 750; c.LargoMm = 336; },
            Nuevo: true);

        // ---------- Almacenamiento ----------
        yield return new("LS-SSD-0001", "Samsung 990 PRO 1TB NVMe M.2 PCIe 4.0", "Samsung", "almacenamiento", 890m, null, 20,
            "SSD NVMe de hasta 7450 MB/s de lectura.",
            "Unidad M.2 2280 PCIe 4.0 de alto rendimiento para juegos y edición.",
            [("Almacenamiento", "Capacidad", "1 TB"), ("Almacenamiento", "Interfaz", "M.2 2280 NVMe PCIe 4.0"),
             ("Rendimiento", "Lectura secuencial", "Hasta 7450 MB/s"), ("Rendimiento", "Escritura secuencial", "Hasta 6900 MB/s")],
            c => { c.Interfaz = "M.2"; }, Destacado: true);

        yield return new("LS-SSD-0002", "Kingston NV3 1TB NVMe M.2 PCIe 4.0", "Kingston", "almacenamiento", 490m, 450m, 30,
            "SSD NVMe económico de hasta 6000 MB/s.",
            "Unidad M.2 2280 PCIe 4.0 con gran relación precio/capacidad.",
            [("Almacenamiento", "Capacidad", "1 TB"), ("Almacenamiento", "Interfaz", "M.2 2280 NVMe PCIe 4.0"),
             ("Rendimiento", "Lectura secuencial", "Hasta 6000 MB/s")],
            c => { c.Interfaz = "M.2"; });

        yield return new("LS-SSD-0003", "Crucial MX500 1TB SATA 2.5\"", "Crucial", "almacenamiento", 520m, null, 15,
            "SSD SATA de 2.5\" de hasta 560 MB/s.",
            "Unidad SSD SATA ideal para ampliar almacenamiento o revivir equipos antiguos.",
            [("Almacenamiento", "Capacidad", "1 TB"), ("Almacenamiento", "Interfaz", "SATA III 6 Gb/s, 2.5\""),
             ("Rendimiento", "Lectura secuencial", "Hasta 560 MB/s")],
            c => { c.Interfaz = "SATA"; });

        yield return new("LS-HDD-0001", "Seagate BarraCuda 2TB 7200 RPM 3.5\"", "Seagate", "almacenamiento", 490m, null, 12,
            "Disco duro de 2 TB a 7200 RPM para almacenamiento masivo.",
            "Disco duro SATA de 3.5\" para juegos, fotos y respaldos.",
            [("Almacenamiento", "Capacidad", "2 TB"), ("Almacenamiento", "Interfaz", "SATA III 6 Gb/s, 3.5\""),
             ("Rendimiento", "Velocidad", "7200 RPM"), ("Almacenamiento", "Modelo", "ST2000DM008")],
            c => { c.Interfaz = "SATA"; });

        // ---------- Fuentes ----------
        yield return new("LS-PSU-0001", "Corsair RM750e 750W 80 PLUS Gold", "Corsair", "fuentes-de-poder", 890m, null, 11,
            "Fuente modular de 750 W con certificación 80 PLUS Gold.",
            "Fuente totalmente modular, silenciosa y eficiente para equipos de gama media-alta.",
            [("Energía", "Potencia", "750 W"), ("Energía", "Certificación", "80 PLUS Gold"), ("Formato", "Formato", "ATX"), ("Formato", "Cableado", "Totalmente modular")],
            c => { c.PotenciaW = 750; }, Destacado: true);

        yield return new("LS-PSU-0002", "Seasonic FOCUS GX-650 650W 80 PLUS Gold", "Seasonic", "fuentes-de-poder", 790m, null, 9,
            "Fuente modular de 650 W, 80 PLUS Gold.",
            "Fuente de gran calidad de componentes con ventilador híbrido.",
            [("Energía", "Potencia", "650 W"), ("Energía", "Certificación", "80 PLUS Gold"), ("Formato", "Formato", "ATX"), ("Formato", "Cableado", "Totalmente modular")],
            c => { c.PotenciaW = 650; });

        yield return new("LS-PSU-0003", "Cooler Master MWE 550 Bronze V2", "Cooler Master", "fuentes-de-poder", 390m, null, 14,
            "Fuente de 550 W 80 PLUS Bronze para equipos de entrada.",
            "Fuente no modular, confiable para builds de presupuesto ajustado.",
            [("Energía", "Potencia", "550 W"), ("Energía", "Certificación", "80 PLUS Bronze"), ("Formato", "Formato", "ATX"), ("Formato", "Cableado", "No modular")],
            c => { c.PotenciaW = 550; });

        yield return new("LS-PSU-0004", "Corsair RM850x 850W 80 PLUS Gold", "Corsair", "fuentes-de-poder", 1190m, null, 6,
            "Fuente modular de 850 W para gráficas de gama alta.",
            "Fuente totalmente modular con condensadores japoneses y modo de ventilador cero RPM.",
            [("Energía", "Potencia", "850 W"), ("Energía", "Certificación", "80 PLUS Gold"), ("Formato", "Formato", "ATX"), ("Formato", "Cableado", "Totalmente modular")],
            c => { c.PotenciaW = 850; });

        // ---------- Gabinetes ----------
        yield return new("LS-CASE-0001", "NZXT H5 Flow", "NZXT", "gabinetes", 790m, null, 8,
            "Gabinete ATX con panel frontal perforado y gran flujo de aire.",
            "Gabinete compacto de media torre con excelente ventilación y gestión de cables.",
            [("Formato", "Placas admitidas", "ATX, Micro-ATX, Mini-ITX"), ("Espacio", "GPU máxima", "365 mm"),
             ("Espacio", "Altura máxima del disipador", "165 mm")],
            c => { c.FactoresFormaSoportados = "ATX,Micro-ATX,Mini-ITX"; c.LargoGpuMaximoMm = 365; c.AlturaCoolerMaximaMm = 165; },
            Destacado: true);

        yield return new("LS-CASE-0002", "Corsair 4000D Airflow", "Corsair", "gabinetes", 890m, 840m, 7,
            "Media torre ATX con frontal de alto flujo.",
            "Uno de los gabinetes más populares por su ventilación y facilidad de montaje.",
            [("Formato", "Placas admitidas", "ATX, Micro-ATX, Mini-ITX"), ("Espacio", "GPU máxima", "360 mm"),
             ("Espacio", "Altura máxima del disipador", "170 mm")],
            c => { c.FactoresFormaSoportados = "ATX,Micro-ATX,Mini-ITX"; c.LargoGpuMaximoMm = 360; c.AlturaCoolerMaximaMm = 170; });

        yield return new("LS-CASE-0003", "Cooler Master MasterBox Q300L", "Cooler Master", "gabinetes", 450m, null, 10,
            "Gabinete Micro-ATX compacto y económico.",
            "Gabinete Micro-ATX con filtros magnéticos y panel de E/S reposicionable. No admite placas ATX.",
            [("Formato", "Placas admitidas", "Micro-ATX, Mini-ITX"), ("Espacio", "GPU máxima", "360 mm"),
             ("Espacio", "Altura máxima del disipador", "157 mm")],
            c => { c.FactoresFormaSoportados = "Micro-ATX,Mini-ITX"; c.LargoGpuMaximoMm = 360; c.AlturaCoolerMaximaMm = 157; });

        yield return new("LS-CASE-0004", "Cooler Master MasterBox NR200P", "Cooler Master", "gabinetes", 690m, null, 5,
            "Gabinete Mini-ITX de formato pequeño (SFF).",
            "Gabinete Mini-ITX para builds compactos. Solo admite placas Mini-ITX.",
            [("Formato", "Placas admitidas", "Mini-ITX"), ("Espacio", "GPU máxima", "330 mm"),
             ("Espacio", "Altura máxima del disipador", "155 mm")],
            c => { c.FactoresFormaSoportados = "Mini-ITX"; c.LargoGpuMaximoMm = 330; c.AlturaCoolerMaximaMm = 155; });

        // ---------- Refrigeración ----------
        yield return new("LS-COOL-0001", "DeepCool AK400", "DeepCool", "refrigeracion", 290m, null, 16,
            "Disipador de torre de 155 mm compatible con AM4, AM5 y LGA1700.",
            "Disipador por aire con 4 heatpipes y ventilador de 120 mm.",
            [("Refrigeración", "Tipo", "Aire, torre simple"), ("Dimensiones", "Altura", "155 mm"),
             ("Compatibilidad", "Sockets", "LGA1700, LGA1200, LGA115x, AM5, AM4")],
            c => { c.AlturaMm = 155; c.SocketsSoportados = "LGA1700,LGA1200,LGA115x,AM5,AM4"; });

        yield return new("LS-COOL-0002", "Thermalright Peerless Assassin 120 SE", "Thermalright", "refrigeracion", 320m, null, 12,
            "Disipador de doble torre de 155 mm con dos ventiladores.",
            "Disipador de doble torre con 6 heatpipes, gran rendimiento por su precio.",
            [("Refrigeración", "Tipo", "Aire, doble torre"), ("Dimensiones", "Altura", "155 mm"),
             ("Compatibilidad", "Sockets", "LGA1700, LGA1200, LGA115x, AM5, AM4")],
            c => { c.AlturaMm = 155; c.SocketsSoportados = "LGA1700,LGA1200,LGA115x,AM5,AM4"; });

        yield return new("LS-COOL-0003", "Noctua NH-D15", "Noctua", "refrigeracion", 990m, null, 4,
            "Disipador de doble torre premium de 165 mm.",
            "Disipador de doble torre con dos ventiladores NF-A15. Para LGA1700 necesita el kit NM-i17xx-MP78 (incluido en unidades recientes; consúltanos).",
            [("Refrigeración", "Tipo", "Aire, doble torre"), ("Dimensiones", "Altura", "165 mm"),
             ("Compatibilidad", "Sockets", "AM5, AM4, LGA1700 (con kit NM-i17xx-MP78)")],
            c => { c.AlturaMm = 165; c.SocketsSoportados = "AM5,AM4,LGA1700"; });

        // ---------- Monitores ----------
        yield return new("LS-MON-0001", "LG UltraGear 27GP850-B 27\" QHD 165 Hz", "LG", "monitores", 2890m, 2690m, 5,
            "Nano IPS 2560x1440, 165 Hz (180 Hz OC), 1 ms GtG.",
            "Monitor gaming QHD con panel Nano IPS de colores amplios y compatibilidad G-SYNC.",
            [("Pantalla", "Tamaño", "27\""), ("Pantalla", "Resolución", "2560 x 1440"), ("Pantalla", "Panel", "Nano IPS"),
             ("Pantalla", "Frecuencia", "165 Hz (180 Hz OC)"), ("Pantalla", "Tiempo de respuesta", "1 ms GtG")],
            Destacado: true);

        yield return new("LS-MON-0002", "Samsung Odyssey G5 27\" QHD 144 Hz curvo", "Samsung", "monitores", 2190m, null, 6,
            "Curvo 1000R, VA 2560x1440 a 144 Hz.",
            "Monitor curvo para inmersión total con HDR10 y FreeSync Premium.",
            [("Pantalla", "Tamaño", "27\""), ("Pantalla", "Resolución", "2560 x 1440"), ("Pantalla", "Panel", "VA curvo 1000R"),
             ("Pantalla", "Frecuencia", "144 Hz"), ("Pantalla", "Tiempo de respuesta", "1 ms MPRT")]);

        yield return new("LS-MON-0003", "AOC 24G2SP 24\" Full HD 165 Hz", "AOC", "monitores", 1190m, null, 9,
            "IPS 1920x1080 a 165 Hz para esports.",
            "Monitor de 24 pulgadas con panel IPS y alta frecuencia a un precio accesible.",
            [("Pantalla", "Tamaño", "23.8\""), ("Pantalla", "Resolución", "1920 x 1080"), ("Pantalla", "Panel", "IPS"),
             ("Pantalla", "Frecuencia", "165 Hz"), ("Pantalla", "Tiempo de respuesta", "1 ms MPRT")]);

        // ---------- Teclados ----------
        yield return new("LS-KB-0001", "Redragon Kumara K552 RGB", "Redragon", "teclados", 290m, null, 25,
            "Teclado mecánico TKL con switches Outemu.",
            "Teclado mecánico compacto sin teclado numérico, estructura metálica e iluminación RGB.",
            [("Teclado", "Formato", "TKL (87 teclas)"), ("Teclado", "Switches", "Outemu Red"), ("Teclado", "Iluminación", "RGB"), ("Teclado", "Conexión", "USB con cable")]);

        yield return new("LS-KB-0002", "HyperX Alloy Origins Core", "HyperX", "teclados", 690m, null, 10,
            "TKL mecánico con switches HyperX y cuerpo de aluminio.",
            "Teclado mecánico sin teclado numérico, cuerpo de aluminio aeronáutico y cable USB-C desmontable.",
            [("Teclado", "Formato", "TKL"), ("Teclado", "Switches", "HyperX Red"), ("Teclado", "Iluminación", "RGB por tecla"), ("Teclado", "Conexión", "USB-C desmontable")],
            Destacado: true);

        yield return new("LS-KB-0003", "Logitech G213 Prodigy", "Logitech", "teclados", 390m, null, 14,
            "Teclado de membrana RGB resistente a salpicaduras.",
            "Teclado de tamaño completo con teclas Mech-Dome, controles multimedia y reposamuñecas integrado.",
            [("Teclado", "Formato", "Completo"), ("Teclado", "Tipo", "Membrana Mech-Dome"), ("Teclado", "Iluminación", "RGB por zonas"), ("Teclado", "Conexión", "USB con cable")]);

        // ---------- Ratones ----------
        yield return new("LS-MOU-0001", "Logitech G502 HERO", "Logitech", "ratones", 390m, null, 20,
            "Sensor HERO 25K, 11 botones programables y pesas ajustables.",
            "El clásico ratón gaming con rueda hiperrápida y peso personalizable.",
            [("Ratón", "Sensor", "HERO 25K"), ("Ratón", "DPI", "100 – 25 600"), ("Ratón", "Botones", "11 programables"), ("Ratón", "Conexión", "USB con cable")],
            Destacado: true);

        yield return new("LS-MOU-0002", "Razer DeathAdder V3", "Razer", "ratones", 590m, null, 10,
            "Ratón ergonómico ultraligero de 59 g con sensor Focus Pro 30K.",
            "Diseño ergonómico icónico, sensor de 30 000 DPI y switches ópticos.",
            [("Ratón", "Sensor", "Focus Pro 30K"), ("Ratón", "DPI", "Hasta 30 000"), ("Ratón", "Peso", "59 g"), ("Ratón", "Conexión", "USB con cable")]);

        yield return new("LS-MOU-0003", "Logitech G305 LIGHTSPEED", "Logitech", "ratones", 330m, null, 22,
            "Inalámbrico LIGHTSPEED con sensor HERO y hasta 250 h de batería.",
            "Ratón inalámbrico ligero con conexión de baja latencia y una pila AA.",
            [("Ratón", "Sensor", "HERO"), ("Ratón", "DPI", "Hasta 12 000"), ("Ratón", "Conexión", "Inalámbrico LIGHTSPEED"), ("Ratón", "Batería", "Hasta 250 h (1 pila AA)")],
            Nuevo: true);

        // ---------- Auriculares ----------
        yield return new("LS-AUD-0001", "HyperX Cloud II", "HyperX", "auriculares", 590m, null, 12,
            "Headset con sonido envolvente 7.1 virtual y almohadillas de espuma viscoelástica.",
            "Auriculares cómodos para largas sesiones con micrófono desmontable y tarjeta de sonido USB.",
            [("Audio", "Drivers", "53 mm"), ("Audio", "Sonido", "7.1 virtual"), ("Audio", "Micrófono", "Desmontable con cancelación de ruido"), ("Audio", "Conexión", "3.5 mm / USB")],
            Destacado: true);

        yield return new("LS-AUD-0002", "Razer BlackShark V2 X", "Razer", "auriculares", 390m, null, 15,
            "Headset ligero con drivers TriForce de 50 mm.",
            "Auriculares para esports con aislamiento pasivo de ruido y micrófono cardioide.",
            [("Audio", "Drivers", "TriForce 50 mm"), ("Audio", "Sonido", "7.1 (software, Windows)"), ("Audio", "Conexión", "3.5 mm")]);

        yield return new("LS-AUD-0003", "Logitech G435 LIGHTSPEED", "Logitech", "auriculares", 490m, 450m, 9,
            "Inalámbricos ultraligeros con LIGHTSPEED y Bluetooth.",
            "Auriculares de 165 g con doble conectividad y micrófonos beamforming integrados.",
            [("Audio", "Drivers", "40 mm"), ("Audio", "Conexión", "LIGHTSPEED 2.4 GHz y Bluetooth"), ("Audio", "Batería", "Hasta 18 h"), ("Audio", "Peso", "165 g")]);

        // ---------- Accesorios ----------
        yield return new("LS-ACC-0001", "Razer Gigantus V2 XXL", "Razer", "accesorios-gaming", 250m, null, 18,
            "Mousepad extendido de tela de 940 x 410 mm.",
            "Superficie de tela texturizada con base de goma antideslizante para teclado y ratón.",
            [("Accesorio", "Dimensiones", "940 x 410 x 4 mm"), ("Accesorio", "Superficie", "Tela"), ("Accesorio", "Base", "Goma antideslizante")]);

        yield return new("LS-ACC-0002", "Corsair ST100 RGB", "Corsair", "accesorios-gaming", 450m, null, 6,
            "Soporte para auriculares con iluminación RGB y hub USB.",
            "Soporte de aluminio para auriculares con base RGB, hub USB 3.1 y conector de audio 3.5 mm.",
            [("Accesorio", "Tipo", "Soporte para auriculares"), ("Accesorio", "Extras", "Hub USB 3.1, audio 3.5 mm"), ("Accesorio", "Iluminación", "RGB")]);

        yield return new("LS-ACC-0003", "Elgato Stream Deck MK.2", "Elgato", "accesorios-gaming", 1290m, null, 4,
            "15 teclas LCD personalizables para streaming y productividad.",
            "Controlador con 15 teclas LCD para lanzar escenas, macros y aplicaciones con un toque.",
            [("Accesorio", "Teclas", "15 LCD personalizables"), ("Accesorio", "Conexión", "USB-C"), ("Accesorio", "Compatibilidad", "Windows y macOS")],
            Nuevo: true);
    }

    public static async Task CrearAsync(AppDbContext db)
    {
        if (await db.Productos.AnyAsync()) return;

        var categorias = new Dictionary<string, Categoria>();
        for (var i = 0; i < Categorias.Length; i++)
        {
            var c = Categorias[i];
            var existente = await db.Categorias.FirstOrDefaultAsync(x => x.Slug == c.Slug);
            categorias[c.Slug] = existente ?? db.Categorias.Add(new Categoria
            {
                Nombre = c.Nombre, Slug = c.Slug, Icono = c.Icono, TipoComponente = c.Tipo,
                Descripcion = c.Descripcion, Orden = i + 1, Activa = true
            }).Entity;
        }

        var marcas = new Dictionary<string, Marca>(StringComparer.OrdinalIgnoreCase);
        foreach (var (nombre, destacada, descripcion) in Marcas)
        {
            var slug = Slugs.Generar(nombre);
            var existente = await db.Marcas.FirstOrDefaultAsync(x => x.Slug == slug);
            marcas[nombre] = existente ?? db.Marcas.Add(new Marca
            {
                Nombre = nombre, Slug = slug, Destacada = destacada, Descripcion = descripcion, Activa = true
            }).Entity;
        }

        foreach (var p in Productos())
        {
            var categoria = categorias[p.Categoria];
            var producto = new Producto
            {
                Sku = p.Sku, Nombre = p.Nombre, Slug = Slugs.Generar(p.Nombre), Modelo = p.Modelo,
                DescripcionCorta = p.Corta, Descripcion = p.Descripcion,
                Categoria = categoria, Marca = marcas[p.Marca],
                Precio = p.Precio, PrecioOferta = p.Oferta, PrecioCosto = Math.Round(p.Precio * 0.78m, 2),
                Stock = p.Stock, StockMinimo = 3, Estado = EstadoProducto.Activo,
                Destacado = p.Destacado, EsNuevo = p.Nuevo, GarantiaMeses = 12
            };

            producto.Imagenes.Add(new ProductoImagen
            {
                Url = $"/images/productos/{categoria.Icono}.svg",
                TextoAlternativo = $"Ilustración de {p.Nombre}", Orden = 0, EsPrincipal = true
            });

            for (var i = 0; i < p.Specs.Length; i++)
            {
                var (grupo, nombre, valor) = p.Specs[i];
                producto.Especificaciones.Add(new ProductoEspecificacion { Grupo = grupo, Nombre = nombre, Valor = valor, Orden = i });
            }

            if (p.Compat is not null)
            {
                var compat = new ProductoCompatibilidad();
                p.Compat(compat);
                producto.Compatibilidad = compat;
            }

            db.Productos.Add(producto);
            db.MovimientosInventario.Add(new MovimientoInventario
            {
                Producto = producto, Tipo = TipoMovimiento.Entrada, Cantidad = p.Stock,
                StockResultante = p.Stock, Motivo = "Stock inicial de demostración"
            });
        }

        await db.SaveChangesAsync();
    }
}
