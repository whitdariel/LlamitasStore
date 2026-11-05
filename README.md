##REQUESITOS PREVIOS:
.NET 10
MinimumVisualStudioVersion = 10.0.40219.1

PowerShelldotnet --version
1.Descomprimir el ProyectoDescarga el proyecto (generalmente un .zip) y extráelo en una carpeta de fácil acceso, como Documentos o Escritorio.

2. Abrir PowerShell en la Carpeta del ProyectoAbre Windows PowerShell o Terminal y navega hasta la carpeta descargada usando el comando cd:PowerShellcd Documents\LlamitasStore-main
(Reemplaza Documents\LlamitasStore-main por la ruta correspondiente si lo guardaste en otro lugar).

3. Localizar el Archivo del Proyecto (.csproj)Dado que al descomprimir archivos .zip suelen crearse subcarpetas duplicadas, ejecuta este comando para encontrar la ubicación exacta del archivo ejecutable:PowerShellGet-ChildItem -Filter *.csproj -Recurse
El resultado te mostrará en qué subcarpeta exacta reside el archivo LlamitasStore.csproj.

4. Ingresar a la Subcarpeta y EjecutarEntra a la carpeta informada por el paso anterior y ejecuta la aplicación:PowerShellcd LlamitasStore-main
dotnet run

Método Rápido (Alternativa en 1 Solo Comando)Si ya estás dentro de la carpeta principal (LlamitasStore-main), puedes ejecutar directamente el archivo .csproj especificando su ruta sin necesidad de usar cd:PowerShelldotnet run --project .\LlamitasStore-main\LlamitasStore.csproj
Tabla de Comandos PrincipalesAcciónComando en PowerShellVerificar instalación de .NETdotnet --versionNavegar a una carpetacd NombreDeLaCarpetaVolver a la carpeta anteriorcd ..Buscar el archivo de proyectoGet-ChildItem -Filter *.csproj -RecurseEjecutar el proyectodotnet runject "Ruta\Al\Archivo\LlamitasStore.csproj"
##
