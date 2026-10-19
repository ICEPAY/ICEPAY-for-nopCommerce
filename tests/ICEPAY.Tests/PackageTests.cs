using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
using Newtonsoft.Json;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using NUnit.Framework;

namespace ICEPAY.Tests;

public class PackageTests
{
    [Test]
    [Category("Package")]
    public void Zip_is_readable_by_nopCommerce_and_contains_one_plugin_per_assembly()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "ICEPAY.slnx"))) root = root.Parent;
        Assert.That(root, Is.Not.Null);
        var version = XDocument.Load(Path.Combine(root!.FullName, "Directory.Build.props")).Descendants("Version").Single().Value;
        var path = Path.Combine(root.FullName, "artifacts", $"ICEPAY-nopCommerce-5.00-{version}.zip");
        Assert.That(File.Exists(path), Is.True, "Build Release and run python3 scripts/package.py before package tests.");
        using var archive = ZipFile.OpenRead(path);
        using var reader = new StreamReader(archive.GetEntry("uploadedItems.json")!.Open());
        var items = JsonConvert.DeserializeObject<List<UploadService.UploadedItem>>(reader.ReadToEnd())!;
        Assert.That(items, Has.Count.EqualTo(10));
        var paymentCount = 0;
        foreach (var item in items)
        {
            Assert.That(item.SupportedVersions, Is.EqualTo("5.00"));
            using var manifest = new StreamReader(archive.GetEntry(item.DirectoryPath + "plugin.json")!.Open());
            var descriptor = PluginDescriptor.GetPluginDescriptorFromText(manifest.ReadToEnd());
            Assert.That(descriptor.SystemName, Is.EqualTo(item.SystemName));
            Assert.That(descriptor.Version, Is.EqualTo(version));
            var dll = archive.GetEntry(item.DirectoryPath + descriptor.AssemblyFileName)!;
            var assemblyName = Path.GetFileNameWithoutExtension(descriptor.AssemblyFileName);
            var assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == assemblyName);
            if (assembly is null)
            {
                using var stream = dll.Open();
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                bytes.Position = 0;
                assembly = AssemblyLoadContext.Default.LoadFromStream(bytes);
            }
            Assert.That(assembly.GetName().Version!.ToString(3), Is.EqualTo(version.Split('-')[0]), descriptor.SystemName);
            var plugins = assembly.GetTypes().Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface).ToList();
            Assert.That(plugins, Has.Count.EqualTo(1), descriptor.SystemName);
            if (typeof(IPaymentMethod).IsAssignableFrom(plugins.Single()))
            {
                paymentCount++;
                Assert.That(descriptor.DependsOn, Does.Contain("Payments.ICEPAY.Core"));
                var logo = archive.GetEntry(item.DirectoryPath + "logo.png");
                Assert.That(logo, Is.Not.Null, $"Missing checkout logo for {descriptor.SystemName}");
                using var logoStream = logo!.Open();
                var header = new byte[24];
                logoStream.ReadExactly(header);
                Assert.That(header.Take(8), Is.EqualTo(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
                Assert.That(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16)), Is.EqualTo(100));
                Assert.That(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20)), Is.EqualTo(40));
            }
        }
        Assert.That(paymentCount, Is.EqualTo(9));
        Assert.That(archive.Entries.Any(e => e.Name is "Nop.Core.dll" or "Nop.Services.dll" or "Nop.Web.Framework.dll"), Is.False);
        Assert.That(archive.GetEntry("Payments.ICEPAY.Core/ICEPAY.Checkout.dll"), Is.Not.Null);
        Assert.That(archive.GetEntry("Payments.ICEPAY.Core/Assets/PaymentLogos/LICENSE.txt"), Is.Not.Null);
        // The scheduled-task registration must resolve to an executable task.
        var scheduledTask = Type.GetType("Nop.Plugin.Payments.ICEPAY.Services.ICEPAYReconcileTask, Nop.Plugin.Payments.ICEPAY", throwOnError: true);
        Assert.That(typeof(Nop.Services.ScheduleTasks.IScheduleTask).IsAssignableFrom(scheduledTask), Is.True);
    }
}
