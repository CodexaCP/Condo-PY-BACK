using System.IO.Compression;
using Condo.Api.Controllers;
using Condo.Api.Services;
using Condo.Application.Abstractions;
using Condo.Application.Models;
using Condo.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Condo.Tests.Support;

// Aviso de actualizacion de la app: el versionCode sale del propio APK publicado y, cuando suben uno nuevo, se avisa por push a los celulares.
public class ApkUpdateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "apk-update-" + Guid.NewGuid().ToString("N"));

    public ApkUpdateTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // AndroidManifest.xml real (binario) de un APK de Expenses compilado con versionCode 2.
    private static byte[] ManifestV2() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AndroidManifest-v2.bin"));

    // Un APK de mentira: un zip con ese manifest y un contenido distinto por "build" para que cambie la huella.
    private string WriteApk(string name, byte[] manifest, string build, DateTime? lastWriteUtc = null)
    {
        var path = Path.Combine(_dir, name);
        if (File.Exists(path)) File.Delete(path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            using (var entry = zip.CreateEntry("AndroidManifest.xml").Open()) entry.Write(manifest);
            using (var entry = zip.CreateEntry("assets/build.txt").Open()) entry.Write(System.Text.Encoding.UTF8.GetBytes(build));
        }

        File.SetLastWriteTimeUtc(path, lastWriteUtc ?? DateTime.UtcNow.AddHours(-1));
        return path;
    }

    private static byte[] WithVersionCode(byte[] manifest, int versionCode)
    {
        // Cambia el valor del atributo versionCode (el entero 2 del manifest real) para simular otro build. Un atributo entero ocupa
        // [tamaño=8 (2 bytes)][0][tipo=0x10][valor (4 bytes)]; se comprueba al final que el lector ve el valor nuevo.
        var current = AndroidManifestReader.ReadVersionCode(manifest)!.Value;
        for (var i = 0; i + 8 <= manifest.Length; i++)
        {
            if (BitConverter.ToUInt16(manifest, i) != 8 || manifest[i + 2] != 0 || manifest[i + 3] != 0x10 || BitConverter.ToInt32(manifest, i + 4) != current)
                continue;

            var copy = (byte[])manifest.Clone();
            BitConverter.GetBytes(versionCode).CopyTo(copy, i + 4);
            if (AndroidManifestReader.ReadVersionCode(copy) == versionCode) return copy;
        }

        throw new InvalidOperationException("No se encontro el atributo versionCode en el manifest de prueba.");
    }

    private (ApkInfoService Apk, IConfiguration Config) Services(string apkPath)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:ApkFilePath"] = apkPath,
            ["App:ApkStatePath"] = Path.Combine(_dir, "state.txt")
        }).Build();
        return (new ApkInfoService(config, NullLogger<ApkInfoService>.Instance), config);
    }

    private sealed class RecordingSender : IPushNotificationSender
    {
        public List<(string Token, string Title, string Body, string? Type)> Sent { get; } = [];

        public Task SendAsync(string deviceToken, string title, string body, IDictionary<string, string>? data, CancellationToken cancellationToken)
        {
            Sent.Add((deviceToken, title, body, data is not null && data.TryGetValue("type", out var t) ? t : null));
            return Task.CompletedTask;
        }
    }

    private static (ApkUpdateAnnouncer Announcer, RecordingSender Sender) Announcer(TestDb t, ApkInfoService apk, IConfiguration config)
    {
        var sender = new RecordingSender();
        var services = new ServiceCollection();
        services.AddSingleton<ICondoDbContext>(t.Db);
        services.AddSingleton<IPushNotificationSender>(sender);
        services.AddSingleton<ILogger<PushDispatcher>>(NullLogger<PushDispatcher>.Instance);
        services.AddScoped<PushDispatcher>();
        var provider = services.BuildServiceProvider();
        return (new ApkUpdateAnnouncer(provider.GetRequiredService<IServiceScopeFactory>(), apk, config, NullLogger<ApkUpdateAnnouncer>.Instance), sender);
    }

    private static void AddDevices(TestDb t, params string[] tokens)
    {
        var company = t.AddCompany();
        var user = t.AddUser(company);
        foreach (var token in tokens)
            t.Db.DeviceTokens.Add(new DeviceToken { CompanyId = company.Id, UserId = user.Id, Token = token });
        t.Db.SaveChanges();
    }

    [Fact]
    public void El_versionCode_se_lee_del_manifest_binario_de_un_apk_real()
    {
        Assert.Equal(2, AndroidManifestReader.ReadVersionCode(ManifestV2()));
        Assert.Null(AndroidManifestReader.ReadVersionCode([1, 2, 3]));
        Assert.Null(AndroidManifestReader.ReadVersionCode([]));
    }

    [Fact]
    public void La_version_anunciada_sale_del_apk_publicado_sin_configurar_nada()
    {
        var apkPath = WriteApk("expenses.apk", WithVersionCode(ManifestV2(), 7), "a");
        var (apk, config) = Services(apkPath);

        var result = new AppController(config, apk).GetVersion();

        var dto = Assert.IsType<AppVersionDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(7, dto.LatestVersionCode);
        Assert.Equal(1, dto.MinVersionCode);
    }

    [Fact]
    public void Si_el_apk_no_esta_la_version_anunciada_es_la_configurada()
    {
        var (apk, config) = Services(Path.Combine(_dir, "no-existe.apk"));
        var dto = (AppVersionDto)((OkObjectResult)new AppController(config, apk).GetVersion().Result!).Value!;
        Assert.Equal(1, dto.LatestVersionCode);
    }

    [Fact]
    public async Task La_primera_vez_solo_toma_nota_y_no_avisa()
    {
        using var t = new TestDb();
        AddDevices(t, "tok-1", "tok-2");
        var (apk, config) = Services(WriteApk("expenses.apk", ManifestV2(), "a"));
        var (announcer, sender) = Announcer(t, apk, config);

        Assert.False(await announcer.CheckOnceAsync(DateTime.UtcNow, CancellationToken.None));
        Assert.Empty(sender.Sent);
        Assert.True(File.Exists(Path.Combine(_dir, "state.txt")));
    }

    [Fact]
    public async Task Un_apk_nuevo_con_mayor_versionCode_avisa_a_cada_celular_una_sola_vez()
    {
        using var t = new TestDb();
        AddDevices(t, "tok-1", "tok-2");
        var apkPath = WriteApk("expenses.apk", ManifestV2(), "a");
        var (apk, config) = Services(apkPath);
        var (announcer, sender) = Announcer(t, apk, config);
        await announcer.CheckOnceAsync(DateTime.UtcNow, CancellationToken.None); // base

        WriteApk("expenses.apk", WithVersionCode(ManifestV2(), 3), "b");
        Assert.True(await announcer.CheckOnceAsync(DateTime.UtcNow, CancellationToken.None));

        Assert.Equal(["tok-1", "tok-2"], sender.Sent.Select(x => x.Token).OrderBy(x => x).ToList());
        Assert.All(sender.Sent, x => Assert.Equal("AppUpdate", x.Type));
        Assert.Contains("actualización", sender.Sent[0].Title);

        // Otra revision con el mismo APK no repite el aviso.
        Assert.False(await announcer.CheckOnceAsync(DateTime.UtcNow, CancellationToken.None));
        Assert.Equal(2, sender.Sent.Count);
    }

    [Fact]
    public async Task Un_reinicio_no_repite_el_aviso_porque_el_estado_queda_en_un_archivo()
    {
        using var t = new TestDb();
        AddDevices(t, "tok-1");
        var apkPath = WriteApk("expenses.apk", ManifestV2(), "a");
        var (apk, config) = Services(apkPath);
        var first = Announcer(t, apk, config);
        await first.Announcer.CheckOnceAsync(DateTime.UtcNow, CancellationToken.None);
        WriteApk("expenses.apk", WithVersionCode(ManifestV2(), 3), "b");
        await first.Announcer.CheckOnceAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.Single(first.Sender.Sent);

        // "Reinicio": un anunciador nuevo, mismo archivo de estado.
        var second = Announcer(t, new ApkInfoService(config, NullLogger<ApkInfoService>.Instance), config);
        Assert.False(await second.Announcer.CheckOnceAsync(DateTime.UtcNow, CancellationToken.None));
        Assert.Empty(second.Sender.Sent);
    }

    [Fact]
    public async Task No_avisa_mientras_el_apk_se_esta_copiando_ni_si_el_versionCode_no_subio()
    {
        using var t = new TestDb();
        AddDevices(t, "tok-1");
        var apkPath = WriteApk("expenses.apk", ManifestV2(), "a");
        var (apk, config) = Services(apkPath);
        var (announcer, sender) = Announcer(t, apk, config);
        await announcer.CheckOnceAsync(DateTime.UtcNow, CancellationToken.None); // base

        // Recien escrito (hace 5 segundos): se espera.
        WriteApk("expenses.apk", WithVersionCode(ManifestV2(), 3), "b", DateTime.UtcNow.AddSeconds(-5));
        Assert.False(await announcer.CheckOnceAsync(DateTime.UtcNow, CancellationToken.None));
        Assert.Empty(sender.Sent);

        // Pasado un rato pero con el MISMO versionCode que la base (2): se anota sin molestar a nadie.
        WriteApk("expenses.apk", ManifestV2(), "c");
        Assert.False(await announcer.CheckOnceAsync(DateTime.UtcNow, CancellationToken.None));
        Assert.Empty(sender.Sent);
    }
}
