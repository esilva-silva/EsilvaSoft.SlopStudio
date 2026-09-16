using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

public sealed class AppUpdateServiceTests
{
    private const string WindowsExecutable = "EsilvaSoft.SlopStudio.Desktop.exe";
    private const string LinuxExecutable = "EsilvaSoft.SlopStudio.Desktop";

    [Test]
    public async Task CheckSelectsPackageForRuntimeAndTreatsRateLimitAsNoUpdate()
    {
        using var fixture = new UpdateFixture();
        fixture.PublishRelease("v0.6.0", "win-x64", Zip((WindowsExecutable, "new")));
        using var service = fixture.Service(fixture.Options());
        var release = await service.CheckAsync(CancellationToken.None);
        Assert.That(release?.Version, Is.EqualTo(AppVersion.Parse("0.6.0")));
        Assert.That(release!.Sha256, Has.Length.EqualTo(64));
        fixture.Routes[UpdateFixture.Api.AbsoluteUri] = () => new HttpResponseMessage(HttpStatusCode.Forbidden);
        Assert.That(await service.CheckAsync(CancellationToken.None), Is.Null, "A rate limit is not an error for the user.");
    }

    [Test]
    public async Task VerifiedZipIsStagedAndAppliedByRenameWhileTheExecutableIsOpen()
    {
        using var fixture = new UpdateFixture();
        var executable = Path.Combine(fixture.Target, WindowsExecutable);
        File.WriteAllText(executable, "old");
        fixture.PublishRelease("v0.6.0", "win-x64", Zip((WindowsExecutable, "new")));
        var options = fixture.Options();
        using var service = fixture.Service(options);
        var release = (await service.CheckAsync(CancellationToken.None))!;
        using (var operation = new ApplicationOperationService().Begin("Baixando"))
            Assert.That((await service.DownloadAsync(release, operation)).Version, Is.EqualTo(release.Version));
        Assert.That(service.GetStagedUpdate()?.Version, Is.EqualTo(release.Version));
        Assert.That(File.ReadAllText(executable), Is.EqualTo("old"), "Nothing is replaced before the application exits.");

        // A running image is opened with delete sharing: it can be renamed but not overwritten.
        using (new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            Assert.That(AppUpdateInstaller.ApplyPending(options), Is.True);
        Assert.That(File.ReadAllText(executable), Is.EqualTo("new"));
        Assert.That(File.ReadAllText(executable + ".old"), Is.EqualTo("old"));
        Assert.That(service.GetStagedUpdate(), Is.Null);
        Assert.That(Directory.GetDirectories(fixture.Updates), Is.Empty);
        AppUpdateInstaller.Cleanup(options);
        Assert.That(File.Exists(executable + ".old"), Is.False);
    }

    [Test]
    public async Task TarGzPackageIsStagedAndKeepsTheExecutableRunnable()
    {
        using var fixture = new UpdateFixture();
        var executable = Path.Combine(fixture.Target, LinuxExecutable);
        File.WriteAllText(executable, "old");
        fixture.PublishRelease("v0.6.0", "linux-x64", TarGz(LinuxExecutable, "new"));
        var options = fixture.Options("linux-x64");
        using var service = fixture.Service(options);
        var release = (await service.CheckAsync(CancellationToken.None))!;
        Assert.That(release.AssetName, Does.EndWith(".tar.gz"));
        using (var operation = new ApplicationOperationService().Begin("Baixando"))
            await service.DownloadAsync(release, operation);
        Assert.That(AppUpdateInstaller.ApplyPending(options), Is.True);
        Assert.That(File.ReadAllText(executable), Is.EqualTo("new"));
        if (!OperatingSystem.IsWindows())
            Assert.That(File.GetUnixFileMode(executable).HasFlag(UnixFileMode.UserExecute), Is.True);
    }

    [Test]
    public async Task HashMismatchOrCancellationStagesNothing()
    {
        using var fixture = new UpdateFixture();
        fixture.PublishRelease("v0.6.0", "win-x64", Zip((WindowsExecutable, "new")), digest: "sha256:" + new string('0', 64));
        using var service = fixture.Service(fixture.Options());
        var release = (await service.CheckAsync(CancellationToken.None))!;
        using (var operation = new ApplicationOperationService().Begin("Baixando"))
            Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(release, operation));
        Assert.That(Directory.Exists(Path.Combine(fixture.Updates, "0.6.0")), Is.False, "The partial and the package are removed.");
        Assert.That(service.GetStagedUpdate(), Is.Null);
        using (var cancelled = new ApplicationOperationService().Begin("Baixando", cancellationToken: new CancellationToken(true)))
            Assert.CatchAsync<OperationCanceledException>(() => service.DownloadAsync(release, cancelled));
        Assert.That(Directory.Exists(Path.Combine(fixture.Updates, "0.6.0")), Is.False);
        Assert.That(File.Exists(Path.Combine(fixture.Updates, "pending.json")), Is.False);
    }

    [Test]
    public async Task FailedReplacementRestoresOriginalsAndKeepsTheUpdateWithItsError()
    {
        using var fixture = new UpdateFixture();
        var executable = Path.Combine(fixture.Target, WindowsExecutable);
        var data = Path.Combine(fixture.Target, "a.dat");
        File.WriteAllText(executable, "old");
        File.WriteAllText(data, "old-data");
        fixture.PublishRelease("v0.6.0", "win-x64", Zip((WindowsExecutable, "new"), ("a.dat", "new-data")));
        var options = fixture.Options();
        using var service = fixture.Service(options);
        var release = (await service.CheckAsync(CancellationToken.None))!;
        using (var operation = new ApplicationOperationService().Begin("Baixando"))
            await service.DownloadAsync(release, operation);

        // Blocks staging the executable after the data file was already swapped.
        Directory.CreateDirectory(executable + ".new");
        Assert.That(AppUpdateInstaller.ApplyPending(options), Is.False);
        Assert.That(File.ReadAllText(executable), Is.EqualTo("old"));
        Assert.That(File.ReadAllText(data), Is.EqualTo("old-data"), "Files swapped before the failure are rolled back.");
        Assert.That(service.GetStagedUpdate()?.LastApplyError, Is.Not.Null.And.Not.Empty);

        Directory.Delete(executable + ".new");
        Assert.That(AppUpdateInstaller.ApplyPending(options), Is.True, "The kept update is retried on the next exit.");
        Assert.That(File.ReadAllText(executable), Is.EqualTo("new"));
        Assert.That(File.ReadAllText(data), Is.EqualTo("new-data"));
    }

    [Test]
    public void StalePendingUpdateIsDiscardedAndOnlyThePublishedExecutableUpdatesItself()
    {
        using var fixture = new UpdateFixture();
        var options = fixture.Options();
        var payload = Path.Combine(fixture.Updates, "0.5.0", "payload");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, WindowsExecutable), "same");
        AppUpdateInstaller.WritePending(fixture.Updates, new PendingAppUpdate("0.5.0", payload, fixture.Target, WindowsExecutable));
        Assert.That(AppUpdateInstaller.ApplyPending(options), Is.False);
        Assert.That(File.Exists(Path.Combine(fixture.Updates, "pending.json")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(fixture.Updates, "0.5.0")), Is.False);

        var host = Path.Combine(fixture.Target, WindowsExecutable);
        var version = AppVersion.Parse("0.5.0");
        Assert.That(AppUpdateInstaller.DetectAvailability(host, version, "win-x64"), Is.EqualTo(AppUpdateAvailability.Supported));
        Assert.That(AppUpdateInstaller.DetectAvailability(host, AppVersion.Parse("0.0.0-local"), "win-x64"), Is.EqualTo(AppUpdateAvailability.Disabled));
        Assert.That(AppUpdateInstaller.DetectAvailability(Path.Combine(fixture.Target, "testhost.exe"), version, "win-x64"), Is.EqualTo(AppUpdateAvailability.Disabled));
        Assert.That(AppUpdateInstaller.DetectAvailability(host, version, null), Is.EqualTo(AppUpdateAvailability.Disabled));
        File.WriteAllText(Path.Combine(fixture.Target, "EsilvaSoft.SlopStudio.Desktop.dll"), "");
        Assert.That(AppUpdateInstaller.DetectAvailability(host, version, "win-x64"), Is.EqualTo(AppUpdateAvailability.Disabled), "dotnet run/build output never replaces itself.");
    }

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        return memory.ToArray();
    }

    private static byte[] TarGz(string name, string content)
    {
        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            using var data = new MemoryStream(Encoding.UTF8.GetBytes(content));
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
            {
                DataStream = data,
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead
            });
        }
        return memory.ToArray();
    }

    private sealed class UpdateFixture : IDisposable
    {
        public static readonly Uri Api = new("https://api.test/repos/slop/releases");
        private readonly string _root = Path.Combine(Path.GetTempPath(), "slop-update-" + Guid.NewGuid().ToString("N"));
        public UpdateFixture() => Directory.CreateDirectory(Target);
        public string Target => Path.Combine(_root, "app");
        public string Updates => Path.Combine(_root, "updates");
        public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = [];

        public AppUpdateOptions Options(string rid = "win-x64") => new(AppUpdateAvailability.Supported, AppVersion.Parse("0.5.0"), rid, Target,
            rid.StartsWith("win-", StringComparison.Ordinal) ? WindowsExecutable : LinuxExecutable, Updates, Api);

        public GitHubAppUpdateService Service(AppUpdateOptions options) => new(options, new RouteHandler(this));

        public void PublishRelease(string tag, string rid, byte[] package, string? digest = null)
        {
            var version = AppVersion.Parse(tag);
            var name = AppUpdateSelector.GetAssetName(version, rid);
            var url = $"https://downloads.test/{tag}/{name}";
            Routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) };
            var json = JsonSerializer.Serialize(new[]
            {
                new
                {
                    tag_name = tag, draft = false, prerelease = version.IsPrerelease, html_url = $"https://github.test/releases/{tag}",
                    assets = new[] { new { name, size = package.LongLength, browser_download_url = url, digest = digest ?? "sha256:" + Convert.ToHexStringLower(SHA256.HashData(package)) } }
                }
            });
            Routes[Api.AbsoluteUri] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); }
            catch (IOException) { }
        }
    }

    private sealed class RouteHandler(UpdateFixture fixture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(fixture.Routes.TryGetValue(request.RequestUri!.AbsoluteUri, out var route) ? route() : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
