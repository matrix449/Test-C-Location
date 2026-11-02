using JuninComercial.Escritorio.Datos;
using JuninComercial.Escritorio.Logica;
using JuninComercial.Escritorio.Modelo;
using DescriptionAttribute = NUnit.Framework.DescriptionAttribute;
using CategoryAttribute = NUnit.Framework.CategoryAttribute;
using MySqlConnector;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.ComponentModel;
using System.Linq;

namespace JuninComercial.Pruebas.Unidad;

/// <summary>
/// Pruebas sobre LocationServicio usando el código ORIGINAL (sin dobles de prueba).
/// - Categoría "SinBD": validaciones que fallan antes de tocar el repositorio.
/// - Categoría "ConBD": pruebas de integración contra MySQL (app.config del proyecto de pruebas).
/// Los datos de precondición se crean con el propio servicio y se eliminan en TearDown.
/// Nomenclatura: metodo_condicion_resultadoEsperado.
/// </summary>


[TestFixture]   
public class LocationServicioPruebas
{
    private const double LatBase = -33.1415;
    private const double LngBase = -68.4832;
    private const double Tolerancia = 0.0001;

    private LocationServicio _servicio = null!;
    private LocationRepositorio _repo = null!;
    private Location _locationValida = null!;
    private readonly List<int> _direccionesUsadas = new();

    [OneTimeSetUp]
    public void ConfigurarAppConfig()
    {
        string baseDir = AppContext.BaseDirectory;
        string[] candidatos =
        {
        Path.Combine(baseDir, "JuninComercial.Escritorio.dll.config"),
        Path.Combine(baseDir, "JuninComercial.Escritorio.exe.config")
    };

        string? ruta = candidatos.FirstOrDefault(File.Exists);
        if (ruta == null)
            Assert.Fail($"No se encontró el archivo de configuración en: {baseDir}");

        AppDomain.CurrentDomain.SetData("APP_CONFIG_FILE", ruta);
    }

    [SetUp]
    public void SetUp()
    {
        _servicio = new LocationServicio();
        _repo = new LocationRepositorio();
        _direccionesUsadas.Clear();

        // Datos fijos: solo se usan en pruebas "SinBD" (fallan antes de consultar la BD)
        _locationValida = new Location
        {
            IdLocation = 0,
            IdZone = 1,
            IdAddress = 101,
            Latitude = LatBase,
            Longitude = LngBase
        };
    }

    [TearDown]
    public void TearDown()
    {
        // Limpieza: borra cualquier Location creada por la prueba (se identifica por la dirección usada)
        if (_direccionesUsadas.Count == 0) return;

        try
        {
            foreach (var loc in _repo.ListarTodas().Where(l => l.IdAddress.HasValue && _direccionesUsadas.Contains(l.IdAddress.Value)))
            {
                _repo.Eliminar(loc.IdLocation);
            }
        }
        catch (Exception ex)
        {
            TestContext.Out.WriteLine($"Aviso: no se pudo limpiar los datos de prueba: {ex.Message}");
        }
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private int ObtenerZonaValida()
    {
        var zonas = _repo.ObtenerZonas();
        if (zonas.Length == 0)
            Assert.Inconclusive("La tabla 'zone' no tiene registros.");
        return zonas[0].IdZone;
    }

    private List<int> ReservarDireccionesLibres(int cantidad)
    {
        var libres = _repo.ObtenerDirecciones(null, null).Select(d => d.IdAddress).Take(cantidad).ToList();
        if (libres.Count < cantidad)
            Assert.Inconclusive($"Se necesitan {cantidad} dirección(es) sin Location asignada y hay {libres.Count}.");

        _direccionesUsadas.AddRange(libres);
        return libres;
    }

    private void AsumirCoordenadaLibre(double lat, double lng)
    {
        // idExcluir = -1 => cuenta cualquier Location con esa coordenada
        Assume.That(!_repo.ExisteCoordenada(lat, lng, -1),
            $"Ya existe en la BD una Location con la coordenada ({lat}, {lng}); no se puede usar en la prueba.");
    }

    private static Location NuevaLocation(int idZone, int idAddress, double lat, double lng, int idLocation = 0)
    {
        return new Location
        {
            IdLocation = idLocation,
            IdZone = idZone,
            IdAddress = idAddress,
            Latitude = lat,
            Longitude = lng
        };
    }

    private Location? BuscarPorDireccion(int idAddress)
    {
        return _repo.ListarTodas().FirstOrDefault(l => l.IdAddress == idAddress);
    }

    /// <summary>Crea una Location usando el servicio real y devuelve el registro persistido.</summary>
    private Location CrearYObtener(int idZone, int idAddress, double lat, double lng)
    {
        AsumirCoordenadaLibre(lat, lng);
        _servicio.Guardar(NuevaLocation(idZone, idAddress, lat, lng));

        var creada = BuscarPorDireccion(idAddress);
        Assert.That(creada, Is.Not.Null, "Precondición fallida: no se pudo crear la Location de apoyo.");
        return creada!;
    }

    private static int? ObtenerIdLocationEnUsoPorEntity()
    {
        using var conexion = ConexionBD.Abrir();
        using var cmd = new MySqlCommand("SELECT id_location FROM entity WHERE id_location IS NOT NULL LIMIT 1", conexion);
        var resultado = cmd.ExecuteScalar();
        return resultado == null || resultado == DBNull.Value ? null : Convert.ToInt32(resultado);
    }

    // =========================================================================
    // CREATE (Insertar)
    // =========================================================================

    [Test, Category("ConBD")]
    [Description("CP-LOC-01 [CREATE - Caso Feliz]: Inserción exitosa con coordenadas y datos válidos")]
    public void Guardar_DatosValidos_InsertaExitosamente()
    {
        // Arrange
        int idZone = ObtenerZonaValida();
        int idAddress = ReservarDireccionesLibres(1)[0];
        AsumirCoordenadaLibre(LatBase, LngBase);
        var loc = NuevaLocation(idZone, idAddress, LatBase, LngBase);

        // Act
        Assert.DoesNotThrow(() => _servicio.Guardar(loc));

        // Assert
        Assert.That(loc.DateCreate, Is.Not.Null);
        Assert.That(loc.DateUpdate, Is.Not.Null);

        var guardada = BuscarPorDireccion(idAddress);
        Assert.That(guardada, Is.Not.Null);
        Assert.That(guardada!.IdZone, Is.EqualTo(idZone));
        Assert.That(guardada.Latitude, Is.EqualTo(LatBase).Within(Tolerancia));
        Assert.That(guardada.Longitude, Is.EqualTo(LngBase).Within(Tolerancia));
    }

    [Test, Category("ConBD")]
    [Description("CP-LOC-02 [CREATE - Caso Borde]: Coordenadas en límites exactos (-90.0 latitud y +180.0 longitud)")]
    public void Guardar_CoordenadasEnLimitesExactos_InsertaExitosamente()
    {
        // Arrange
        int idZone = ObtenerZonaValida();
        int idAddress = ReservarDireccionesLibres(1)[0];
        AsumirCoordenadaLibre(-90.0, 180.0);
        var loc = NuevaLocation(idZone, idAddress, -90.0, 180.0);

        // Act & Assert
        Assert.DoesNotThrow(() => _servicio.Guardar(loc));

        var guardada = BuscarPorDireccion(idAddress);
        Assert.That(guardada, Is.Not.Null);
        Assert.That(guardada!.Latitude, Is.EqualTo(-90.0).Within(Tolerancia));
        Assert.That(guardada.Longitude, Is.EqualTo(180.0).Within(Tolerancia));
    }

    [Test, Category("ConBD")]
    [Description("CP-LOC-03 [CREATE - Caso Borde]: Coordenadas en límites opuestos (+90.0 latitud y -180.0 longitud)")]
    public void Guardar_CoordenadasEnLimitesOpuestos_InsertaExitosamente()
    {
        // Arrange
        int idZone = ObtenerZonaValida();
        int idAddress = ReservarDireccionesLibres(1)[0];
        AsumirCoordenadaLibre(90.0, -180.0);
        var loc = NuevaLocation(idZone, idAddress, 90.0, -180.0);

        // Act & Assert
        Assert.DoesNotThrow(() => _servicio.Guardar(loc));

        var guardada = BuscarPorDireccion(idAddress);
        Assert.That(guardada, Is.Not.Null);
        Assert.That(guardada!.Latitude, Is.EqualTo(90.0).Within(Tolerancia));
        Assert.That(guardada.Longitude, Is.EqualTo(-180.0).Within(Tolerancia));
    }

    [Test, Category("SinBD")]
    [Description("CP-LOC-04 [CREATE - Caso Error]: Latitud supera límite superior (+90.0001)")]
    public void Guardar_LatitudMayorANoventa_LanzaReglaNegocioException()
    {
        _locationValida.Latitude = 90.0001;

        var ex = Assert.Throws<ReglaNegocioException>(() => _servicio.Guardar(_locationValida));
        Assert.That(ex!.Message, Is.EqualTo("La Latitud debe estar entre -90 y 90 grados."));
    }

    [Test, Category("SinBD")]
    [Description("CP-LOC-05 [CREATE - Caso Error]: Latitud inferior a límite inferior (-90.0001)")]
    public void Guardar_LatitudMenorAMenosNoventa_LanzaReglaNegocioException()
    {
        _locationValida.Latitude = -90.0001;

        var ex = Assert.Throws<ReglaNegocioException>(() => _servicio.Guardar(_locationValida));
        Assert.That(ex!.Message, Is.EqualTo("La Latitud debe estar entre -90 y 90 grados."));
    }

    [Test, Category("SinBD")]
    [Description("CP-LOC-06 [CREATE - Caso Error]: Longitud supera límite superior (+180.0001)")]
    public void Guardar_LongitudMayorACientoOchenta_LanzaReglaNegocioException()
    {
        _locationValida.Longitude = 180.0001;

        var ex = Assert.Throws<ReglaNegocioException>(() => _servicio.Guardar(_locationValida));
        Assert.That(ex!.Message, Is.EqualTo("La Longitud debe estar entre -180 y 180 grados."));
    }

    [Test, Category("SinBD")]
    [Description("CP-LOC-07 [CREATE - Caso Error]: Longitud inferior a límite inferior (-180.0001)")]
    public void Guardar_LongitudMenorAMenosCientoOchenta_LanzaReglaNegocioException()
    {
        _locationValida.Longitude = -180.0001;

        var ex = Assert.Throws<ReglaNegocioException>(() => _servicio.Guardar(_locationValida));
        Assert.That(ex!.Message, Is.EqualTo("La Longitud debe estar entre -180 y 180 grados."));
    }

    [Test, Category("SinBD")]
    [Description("CP-LOC-08 [CREATE - Caso Error]: Zona nula o menor o igual a cero")]
    public void Guardar_ZonaNulaOInvalida_LanzaReglaNegocioException()
    {
        _locationValida.IdZone = null;
        var ex = Assert.Throws<ReglaNegocioException>(() => _servicio.Guardar(_locationValida));
        Assert.That(ex!.Message, Is.EqualTo("Debe seleccionar una Zona válida."));

        _locationValida.IdZone = 0;
        var exCero = Assert.Throws<ReglaNegocioException>(() => _servicio.Guardar(_locationValida));
        Assert.That(exCero!.Message, Is.EqualTo("Debe seleccionar una Zona válida."));
    }

    [Test, Category("SinBD")]
    [Description("CP-LOC-09 [CREATE - Caso Error]: Dirección nula o menor o igual a cero")]
    public void Guardar_DireccionNulaOInvalida_LanzaReglaNegocioException()
    {
        _locationValida.IdAddress = null;
        var ex = Assert.Throws<ReglaNegocioException>(() => _servicio.Guardar(_locationValida));
        Assert.That(ex!.Message, Is.EqualTo("Debe seleccionar una Dirección válida."));

        _locationValida.IdAddress = 0;
        var exCero = Assert.Throws<ReglaNegocioException>(() => _servicio.Guardar(_locationValida));
        Assert.That(exCero!.Message, Is.EqualTo("Debe seleccionar una Dirección válida."));
    }

    [Test, Category("ConBD")]
    [Description("CP-LOC-10 [CREATE - Caso Error]: Dirección ya asignada a otra ubicación")]
    public void Guardar_DireccionYaAsignadaEnOtraUbicacion_LanzaReglaNegocioException()
    {
        // Arrange: una Location existente que ya usa la dirección
        int idZone = ObtenerZonaValida();
        int idAddress = ReservarDireccionesLibres(1)[0];
        CrearYObtener(idZone, idAddress, LatBase, LngBase);

        // Misma dirección, coordenada distinta (para que dispare la regla de dirección y no la de coordenada)
        AsumirCoordenadaLibre(-33.2000, -68.5000);
        var duplicada = NuevaLocation(idZone, idAddress, -33.2000, -68.5000);

        // Act & Assert
        var ex = Assert.Throws<ReglaNegocioException>(() => _servicio.Guardar(duplicada));
        Assert.That(ex!.Message, Is.EqualTo("La dirección seleccionada ya tiene una Location registrada en el sistema."));
    }

    [Test, Category("ConBD")]
    [Description("CP-LOC-11 [CREATE - Caso Error]: Coordenada GPS duplicada exacta")]
    public void Guardar_CoordenadaDuplicadaExacta_LanzaReglaNegocioException()
    {
        // Arrange: una Location existente con la coordenada
        int idZone = ObtenerZonaValida();
        var direcciones = ReservarDireccionesLibres(2);
        CrearYObtener(idZone, direcciones[0], LatBase, LngBase);

        // Dirección libre distinta, misma coordenada
        var duplicada = NuevaLocation(idZone, direcciones[1], LatBase, LngBase);

        // Act & Assert
        var ex = Assert.Throws<ReglaNegocioException>(() => _servicio.Guardar(duplicada));
        Assert.That(ex!.Message, Is.EqualTo("Ya existe una ubicación registrada en exactamente la misma coordenada GPS."));
    }

    // =========================================================================
    // READ (Buscar por ID / Zona)
    // =========================================================================

    [Test, Category("ConBD")]
    [Description("CP-LOC-12 [READ - Caso Feliz]: Buscar ubicación por ID existente")]
    public void BuscarPorId_IdExistente_RetornaLocation()
    {
        // Arrange
        int idZone = ObtenerZonaValida();
        int idAddress = ReservarDireccionesLibres(1)[0];
        var creada = CrearYObtener(idZone, idAddress, LatBase, LngBase);

        // Act
        var resultado = _servicio.BuscarPorId(creada.IdLocation);

        // Assert
        Assert.That(resultado, Is.Not.Null);
        Assert.That(resultado!.IdLocation, Is.EqualTo(creada.IdLocation));
        Assert.That(resultado.IdZone, Is.EqualTo(idZone));
        Assert.That(resultado.IdAddress, Is.EqualTo(idAddress));
    }

    [Test, Category("ConBD")]
    [Description("CP-LOC-13 [READ - Caso Borde]: Búsqueda con valor límite positivo mínimo (ID = 1)")]
    public void BuscarPorId_IdLimiteMinimoPositivo_RetornaLocation()
    {
        // Arrange: depende de que exista la Location con ID 1 en la BD (solo lectura)
        if (_repo.BuscarPorId(1) == null)
            Assert.Inconclusive("No existe una Location con ID = 1 en la base de datos.");

        // Act
        var resultado = _servicio.BuscarPorId(1);

        // Assert
        Assert.That(resultado, Is.Not.Null);
        Assert.That(resultado!.IdLocation, Is.EqualTo(1));
    }

    [Test, Category("SinBD")]
    [Description("CP-LOC-14/15 [READ - Caso Error]: ID menor o igual a cero")]
    public void BuscarPorId_IdCeroONegativo_LanzaReglaNegocioException()
    {
        var exCero = Assert.Throws<ReglaNegocioException>(() => _servicio.BuscarPorId(0));
        Assert.That(exCero!.Message, Is.EqualTo("El ID ingresado no es válido."));

        var exNegativo = Assert.Throws<ReglaNegocioException>(() => _servicio.BuscarPorId(-10));
        Assert.That(exNegativo!.Message, Is.EqualTo("El ID ingresado no es válido."));
    }

    [Test, Category("SinBD")]
    [Description("CP-LOC-16 [READ - Caso Error]: Filtrado por Zona con ID inválido")]
    public void BuscarPorZona_ZonaInvalida_LanzaReglaNegocioException()
    {
        var ex = Assert.Throws<ReglaNegocioException>(() => _servicio.BuscarPorZona(-1));
        Assert.That(ex!.Message, Is.EqualTo("La zona ingresada no es válida."));
    }

    // =========================================================================
    // UPDATE (Modificar)
    // =========================================================================

    [Test, Category("ConBD")]
    [Description("CP-LOC-17 [UPDATE - Caso Feliz]: Modificación válida de ubicación existente")]
    public void Guardar_ModificacionValida_ActualizaExitosamente()
    {
        // Arrange
        int idZone = ObtenerZonaValida();
        int idAddress = ReservarDireccionesLibres(1)[0];
        var creada = CrearYObtener(idZone, idAddress, LatBase, LngBase);

        const double nuevaLat = -33.2000;
        const double nuevaLng = -68.5000;
        AsumirCoordenadaLibre(nuevaLat, nuevaLng);
        var modificada = NuevaLocation(idZone, idAddress, nuevaLat, nuevaLng, creada.IdLocation);

        // Act & Assert
        Assert.DoesNotThrow(() => _servicio.Guardar(modificada));

        var enBd = _repo.BuscarPorId(creada.IdLocation);
        Assert.That(enBd, Is.Not.Null);
        Assert.That(enBd!.Latitude, Is.EqualTo(nuevaLat).Within(Tolerancia));
        Assert.That(enBd.Longitude, Is.EqualTo(nuevaLng).Within(Tolerancia));
        Assert.That(_repo.ListarTodas().Count(l => l.IdAddress == idAddress), Is.EqualTo(1), "No debe crear un registro nuevo.");
    }

    [Test, Category("ConBD")]
    [Description("CP-LOC-18 [UPDATE - Caso Borde]: Modificación con coordenadas límite (+90 y -180)")]
    public void Guardar_ModificacionEnLimitesSexagesimales_ActualizaExitosamente()
    {
        // Arrange
        int idZone = ObtenerZonaValida();
        int idAddress = ReservarDireccionesLibres(1)[0];
        var creada = CrearYObtener(idZone, idAddress, LatBase, LngBase);

        AsumirCoordenadaLibre(90.0, -180.0);
        var modificada = NuevaLocation(idZone, idAddress, 90.0, -180.0, creada.IdLocation);

        // Act & Assert
        Assert.DoesNotThrow(() => _servicio.Guardar(modificada));

        var enBd = _repo.BuscarPorId(creada.IdLocation);
        Assert.That(enBd, Is.Not.Null);
        Assert.That(enBd!.Latitude, Is.EqualTo(90.0).Within(Tolerancia));
        Assert.That(enBd.Longitude, Is.EqualTo(-180.0).Within(Tolerancia));
    }

    // =========================================================================
    // DELETE (Eliminar)
    // =========================================================================

    [Test, Category("ConBD")]
    [Description("CP-LOC-19 [DELETE - Caso Feliz]: Eliminación de ubicación no referenciada")]
    public void Eliminar_IdExistenteSinUso_EliminaExitosamente()
    {
        // Arrange
        int idZone = ObtenerZonaValida();
        int idAddress = ReservarDireccionesLibres(1)[0];
        var creada = CrearYObtener(idZone, idAddress, LatBase, LngBase);

        // Act & Assert
        Assert.DoesNotThrow(() => _servicio.Eliminar(creada.IdLocation));
        Assert.That(_repo.BuscarPorId(creada.IdLocation), Is.Null);
    }

    [Test, Category("SinBD")]
    [Description("CP-LOC-20 [DELETE - Caso Error]: Eliminación con ID no válido (<= 0)")]
    public void Eliminar_IdCeroONegativo_LanzaReglaNegocioException()
    {
        var exCero = Assert.Throws<ReglaNegocioException>(() => _servicio.Eliminar(0));
        Assert.That(exCero!.Message, Is.EqualTo("El ID a eliminar no es válido."));

        var exNeg = Assert.Throws<ReglaNegocioException>(() => _servicio.Eliminar(-5));
        Assert.That(exNeg!.Message, Is.EqualTo("El ID a eliminar no es válido."));
    }

    [Test, Category("ConBD")]
    [Description("CP-LOC-21 [DELETE - Caso Error]: Intento de eliminación de ubicación vinculada a comercio/entidad activa")]
    public void Eliminar_UbicacionEnUsoPorEntidad_LanzaReglaNegocioException()
    {
        // Arrange: usa una Location que ya esté referenciada por la tabla 'entity' (solo lectura)
        int? idEnUso = ObtenerIdLocationEnUsoPorEntity();
        if (idEnUso == null)
            Assert.Inconclusive("La tabla 'entity' no tiene ninguna fila con id_location asignado.");

        // Act & Assert
        var ex = Assert.Throws<ReglaNegocioException>(() => _servicio.Eliminar(idEnUso!.Value));
        Assert.That(ex!.Message, Is.EqualTo("No se puede eliminar la ubicación porque está asignada a un comercio o entidad activa."));
        Assert.That(_repo.BuscarPorId(idEnUso.Value), Is.Not.Null, "La Location en uso no debe haberse borrado.");
    }
}