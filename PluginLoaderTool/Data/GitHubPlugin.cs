using avaness.PluginLoaderTool.Network;
using avaness.PluginLoaderTool.Compiler;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Serialization;
using System.Threading.Tasks;

namespace avaness.PluginLoaderTool.Data
{
    [ProtoContract]
    public partial class GitHubPlugin : PluginData
    {
        [ProtoMember(1)]
        public string Commit { get; set; }

        [ProtoMember(2)]
        [XmlArray]
        [XmlArrayItem("Directory")]
        public string[] SourceDirectories { get; set; }

        [ProtoMember(3)]
        [XmlArray]
        [XmlArrayItem("Version")]
        public Branch[] AlternateVersions { get; set; }

        [ProtoMember(4)]
        public string AssetFolder { get; set; }

        [ProtoMember(5)]
        public NuGetPackageList NuGetReferences { get; set; }

        [XmlIgnore]
        public string CompiledAssemblyName { get; private set; }
        [XmlIgnore]
        public string UserName { get; private set; }

        private string assemblyNameSafe;
        private CacheManifest manifest;
        private NuGetClient nuget;

        public GitHubPlugin()
        { }

        public void InitPaths(string cacheDir)
        {
            string[] nameArgs = Id.Split(new char[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (nameArgs.Length < 2)
                throw new Exception("Invalid GitHub name: " + Id);

            CleanPaths(SourceDirectories);

            if (!string.IsNullOrWhiteSpace(AssetFolder))
            {
                AssetFolder = AssetFolder.Replace('\\', '/').TrimStart('/');
                if (AssetFolder.Length > 0 && AssetFolder[AssetFolder.Length - 1] != '/')
                    AssetFolder += '/';
            }

            UserName = nameArgs[0];
            CompiledAssemblyName = nameArgs[1];
            assemblyNameSafe = MakeSafeString(nameArgs[1]);
            manifest = CacheManifest.Load(Path.Combine(cacheDir, "GitHub", nameArgs[0], nameArgs[1]));
        }

        private void CleanPaths(string[] paths)
        {
            if (paths != null)
            {
                for (int i = paths.Length - 1; i >= 0; i--)
                {
                    string path = paths[i].Replace('\\', '/').TrimStart('/');

                    if (path.Length == 0)
                        continue;

                    if (path[path.Length - 1] != '/')
                        path += '/';

                    paths[i] = path;
                }
            }
        }

        private string MakeSafeString(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in s)
            {
                if (char.IsLetterOrDigit(ch))
                    sb.Append(ch);
                else
                    sb.Append('_');
            }
            return sb.ToString();
        }

        public async Task<Stream> CompilePluginAsync()
        {
            Stream data;

            // TODO?: Add game version check
            int gameVersion = 0;
            string selectedCommit = Commit;
            if (!manifest.IsCacheValid(selectedCommit, gameVersion, !String.IsNullOrWhiteSpace(AssetFolder), NuGetReferences != null))
            {
                manifest.GameVersion = gameVersion;
                manifest.Commit = selectedCommit;
                manifest.ClearAssets();

                data = new MemoryStream(await CompileFromSourceAsync(selectedCommit));

                using (FileStream fs = File.Create(manifest.DllFile))
                {
                    data.CopyTo(fs);
                    data.Position = 0;
                }

                manifest.DeleteUnknownFiles();
                manifest.Save();
            }
            else
            {
                manifest.DeleteUnknownFiles();
                data = File.OpenRead(manifest.DllFile);
            }

            return data;
        }

        public async Task<byte[]> CompileFromSourceAsync(string commit)
        {
            RoslynCompiler compiler = new RoslynCompiler();
            using (Stream s = await GitHub.DownloadRepoAsync(Id, commit))
            using (ZipArchive zip = new ZipArchive(s))
            {
                for (int i = 0; i < zip.Entries.Count; i++)
                {
                    ZipArchiveEntry entry = zip.Entries[i];
                    await CompileFromSourceAsync(compiler, entry);
                }
            }
            if (NuGetReferences?.PackageIds != null)
            {
                if (nuget == null)
                    nuget = new NuGetClient();
                InstallPackages(await nuget.DownloadPackagesAsync(NuGetReferences.PackageIds), compiler);
            }
            return compiler.Compile(assemblyNameSafe, out _);
        }

        private async Task CompileFromSourceAsync(RoslynCompiler compiler, ZipArchiveEntry entry)
        {
            string path = RemoveRoot(entry.FullName);
            if (NuGetReferences != null && path == NuGetReferences.PackagesConfigNormalized)
            {
                nuget = new NuGetClient();
                NuGetPackage[] packages;
                using (Stream entryStream = entry.Open())
                {
                    packages = await nuget.DownloadFromConfigAsync(entryStream);
                }
                InstallPackages(packages, compiler);
            }
            if (AllowedZipPath(path))
            {
                using (Stream entryStream = entry.Open())
                {
                    compiler.Load(entryStream, entry.FullName);
                }
            }
            if (IsAssetZipPath(path, out string assetFilePath))
            {
                AssetFile newFile = manifest.CreateAsset(assetFilePath);
                if (!manifest.IsAssetValid(newFile))
                {
                    using (Stream entryStream = entry.Open())
                    {
                        manifest.SaveAsset(newFile, entryStream);
                    }
                }
            }
        }

        private void InstallPackages(IEnumerable<NuGetPackage> packages, RoslynCompiler compiler)
        {
            foreach (NuGetPackage package in packages)
                InstallPackage(package, compiler);
        }

        private void InstallPackage(NuGetPackage package, RoslynCompiler compiler)
        {
            foreach (NuGetPackage.Item file in package.LibFiles)
            {
                AssetFile newFile = manifest.CreateAsset(file.FilePath, AssetFile.AssetType.Lib);
                if (!manifest.IsAssetValid(newFile))
                {
                    using (Stream entryStream = File.OpenRead(file.FullPath))
                    {
                        manifest.SaveAsset(newFile, entryStream);
                    }
                }

                if (Path.GetDirectoryName(newFile.FullPath) == newFile.BaseDir)
                    compiler.TryAddDependency(newFile.FullPath);
            }

            foreach (NuGetPackage.Item file in package.ContentFiles)
            {
                AssetFile newFile = manifest.CreateAsset(file.FilePath, AssetFile.AssetType.LibContent);
                if (!manifest.IsAssetValid(newFile))
                {
                    using (Stream entryStream = File.OpenRead(file.FullPath))
                    {
                        manifest.SaveAsset(newFile, entryStream);
                    }
                }
            }
        }

        private bool IsAssetZipPath(string path, out string assetFilePath)
        {
            assetFilePath = null;

            if (path.EndsWith("/") || string.IsNullOrEmpty(AssetFolder))
                return false;

            if (path.StartsWith(AssetFolder, StringComparison.Ordinal) && path.Length > (AssetFolder.Length + 1))
            {
                assetFilePath = path.Substring(AssetFolder.Length).TrimStart('/');
                return true;
            }
            return false;
        }

        private bool AllowedZipPath(string path)
        {
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                return false;

            if (SourceDirectories == null || SourceDirectories.Length == 0)
                return true;

            foreach (string dir in SourceDirectories)
            {
                if (path.StartsWith(dir, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private string RemoveRoot(string path)
        {
            path = path.Replace('\\', '/').TrimStart('/');
            int index = path.IndexOf('/');
            if (index >= 0 && (index + 1) < path.Length)
                return path.Substring(index + 1);
            return path;
        }

        [ProtoContract]
        public class Branch
        {
            [ProtoMember(1)]
            public string Name { get; set; }

            [ProtoMember(2)]
            public string Commit { get; set; }

            public Branch()
            {

            }
        }
    }
}