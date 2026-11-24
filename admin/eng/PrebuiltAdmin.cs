// The first-party build inside FluentCMS.Admin.Hosting (admin spec 6.1, AQ8): the wwwroot of a publish of FluentCMS.Admin.App,
// the files of fluentcms-admin-{version}.zip, as static web assets of the package with the endpoints the publish gave them,
// so that MapStaticAssets serves them in a host as it serves an app the host references: fingerprinted files cached for good
// and a compressed copy by Accept-Encoding. The pack of the package compiles this file in its inline tasks.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace FluentCMS.Admin.Hosting.Build
{
    // The assets and endpoints of a publish, from the endpoints manifest it writes beside wwwroot. A compressed copy is the
    // alternative of the file it compresses, as the publish has it. It refuses a publish without the files the zip's check
    // asks for, and one with a resource of a pseudo-culture, which no release carries (admin spec 4.17).
    public sealed class ReadPrebuiltAdmin : Task
    {
        private static readonly string[] Required = { "index.html", "config.json", "_framework/blazor.webassembly.js", "LICENSE", "THIRD-PARTY-NOTICES.md" };

        private static readonly string[] PseudoCultures = { "en-XA", "ar-XB" };

        [Required]
        public string PublishDirectory { get; set; } = "";

        [Required]
        public string Source { get; set; } = "";

        [Output]
        public ITaskItem[] Assets { get; set; } = Array.Empty<ITaskItem>();

        [Output]
        public ITaskItem[] Endpoints { get; set; } = Array.Empty<ITaskItem>();

        public override bool Execute()
        {
            var root = Path.GetFullPath(Path.Combine(PublishDirectory, "wwwroot")) + Path.DirectorySeparatorChar;
            var manifests = Directory.Exists(PublishDirectory) ? Directory.GetFiles(PublishDirectory, "*.staticwebassets.endpoints.json") : Array.Empty<string>();
            if (manifests.Length != 1)
            {
                Log.LogError($"{PublishDirectory} is not a publish of the admin app: it holds {manifests.Length} manifests of static web asset endpoints, where a publish writes one.");
                return false;
            }

            List<XElement> endpoints;
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(File.ReadAllBytes(manifests[0]), XmlDictionaryReaderQuotas.Max))
            {
                endpoints = XElement.Load(reader).Element("Endpoints")?.Elements("item").ToList() ?? new List<XElement>();
            }

            var files = endpoints.Select(endpoint => (string)endpoint.Element("AssetFile")!).Distinct(StringComparer.Ordinal).OrderBy(file => file, StringComparer.Ordinal).ToList();
            foreach (var missing in Required.Where(file => !files.Contains(file, StringComparer.Ordinal)))
            {
                Log.LogError($"The publish of the admin app in {PublishDirectory} misses {missing}.");
            }

            foreach (var file in files.Where(IsPseudoCulture))
            {
                Log.LogError($"The publish of the admin app in {PublishDirectory} holds {file}, a resource of a pseudo-culture, which only the end-to-end checks build (FluentCmsAdminPseudoCultures).");
            }

            if (Log.HasLoggedErrors)
            {
                return false;
            }

            var encodings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var endpoint in endpoints)
            {
                foreach (var selector in endpoint.Element("Selectors")?.Elements("item") ?? Enumerable.Empty<XElement>())
                {
                    if ((string)selector.Element("Name") == "Content-Encoding")
                    {
                        encodings[(string)endpoint.Element("AssetFile")!] = (string)selector.Element("Value")!;
                    }
                }
            }

            Assets = files.Select(file => Asset(root, file, encodings)).ToArray();
            Endpoints = endpoints.Select(endpoint =>
            {
                var item = new TaskItem((string)endpoint.Element("Route")!);
                item.SetMetadata("AssetFile", PathOf(root, (string)endpoint.Element("AssetFile")!));
                item.SetMetadata("Selectors", Json(endpoint.Element("Selectors")));
                item.SetMetadata("ResponseHeaders", Json(endpoint.Element("ResponseHeaders")));
                item.SetMetadata("EndpointProperties", Json(endpoint.Element("EndpointProperties")));
                return (ITaskItem)item;
            }).ToArray();
            return true;
        }

        private ITaskItem Asset(string root, string file, Dictionary<string, string> encodings)
        {
            var path = PathOf(root, file);
            byte[] hash;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                hash = sha.ComputeHash(stream);
            }

            var compressed = encodings.TryGetValue(file, out var encoding);
            var item = new TaskItem(path);
            item.SetMetadata("SourceId", Source);
            item.SetMetadata("SourceType", "Discovered");
            item.SetMetadata("ContentRoot", root);
            item.SetMetadata("BasePath", "/");
            item.SetMetadata("RelativePath", file);
            // A host serves them in its build too, as dotnet run does, and publishes them.
            item.SetMetadata("AssetKind", "All");
            item.SetMetadata("AssetMode", "All");
            item.SetMetadata("AssetRole", compressed ? "Alternative" : "Primary");
            item.SetMetadata("AssetMergeBehavior", "");
            item.SetMetadata("AssetMergeSource", "");
            item.SetMetadata("RelatedAsset", compressed ? PathOf(root, file.Substring(0, file.LastIndexOf('.'))) : "");
            item.SetMetadata("AssetTraitName", compressed ? "Content-Encoding" : "");
            item.SetMetadata("AssetTraitValue", compressed ? encoding : "");
            item.SetMetadata("Fingerprint", Fingerprint(hash));
            item.SetMetadata("Integrity", Convert.ToBase64String(hash));
            item.SetMetadata("CopyToOutputDirectory", "Never");
            item.SetMetadata("CopyToPublishDirectory", "PreserveNewest");
            item.SetMetadata("OriginalItemSpec", path);
            item.SetMetadata("FileLength", new FileInfo(path).Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            item.SetMetadata("LastWriteTime", File.GetLastWriteTimeUtc(path).ToString("o", System.Globalization.CultureInfo.InvariantCulture));
            return item;
        }

        private static string PathOf(string root, string file) => root + file.Replace('/', Path.DirectorySeparatorChar);

        private static bool IsPseudoCulture(string file) =>
            PseudoCultures.Any(culture => file.Split('/').Contains(culture, StringComparer.OrdinalIgnoreCase)
                || file.IndexOf("." + culture + ".", StringComparison.OrdinalIgnoreCase) >= 0);

        // As the static web assets of a project are fingerprinted: ten characters of the hash in base 36.
        private static string Fingerprint(byte[] hash)
        {
            const string Digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            return new string(hash.Take(10).Select(value => Digits[value % Digits.Length]).ToArray());
        }

        // The JSON of a property of an endpoint, written back from the XML the reader made of it.
        private static string Json(XElement element)
        {
            if (element is null)
            {
                return "[]";
            }

            using var buffer = new MemoryStream();
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(buffer, new UTF8Encoding(false), ownsStream: false))
            {
                new XElement("root", element.Attributes(), element.Nodes()).WriteTo(writer);
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }

    // The props file the pack writes keeps the related asset of a compressed copy as the path of the publish it came from,
    // where every other path is one under the package. A host would then find no file for it to be the copy of, and stop.
    public sealed class RelatePrebuiltAdminAssets : Task
    {
        [Required]
        public string PropsFile { get; set; } = "";

        [Required]
        public string PublishDirectory { get; set; } = "";

        public override bool Execute()
        {
            var root = Path.GetFullPath(Path.Combine(PublishDirectory, "wwwroot")) + Path.DirectorySeparatorChar;
            var text = File.ReadAllText(PropsFile);
            var related = new Regex("<RelatedAsset>" + Regex.Escape(root) + "(?<path>[^<]+)</RelatedAsset>");
            var written = related.Replace(text, match =>
                "<RelatedAsset>$([MSBuild]::NormalizePath('$(MSBuildThisFileDirectory)', '..', 'staticwebassets', '"
                + match.Groups["path"].Value.Replace(Path.DirectorySeparatorChar, '/') + "'))</RelatedAsset>");
            var left = Regex.Matches(written, "<RelatedAsset>(?<path>[^<$][^<]*)</RelatedAsset>").Cast<Match>().Select(match => match.Groups["path"].Value).ToList();
            if (left.Count > 0)
            {
                Log.LogError($"{PropsFile} names a related asset outside the package: {left[0]}.");
                return false;
            }

            if (written != text)
            {
                File.WriteAllText(PropsFile, written, new UTF8Encoding(false));
            }

            return true;
        }
    }
}
