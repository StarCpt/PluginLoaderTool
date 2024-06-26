﻿using avaness.PluginLoaderTool.Compiler;
using avaness.PluginLoaderTool.Data;
using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace avaness.PluginLoaderTool
{
    public class Program
    {
        public static string ExeDir { get; private set; }

        private static HttpClientHandler handler = new HttpClientHandler()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        private static HttpClient webClient = new HttpClient(handler);
        private static XmlSerializer pluginDataSerializer = new XmlSerializer(typeof(PluginData));
        private static string SpaceEngineersExe = null;
        private static string PluginLoaderDll = null;

        private const string WhitelistFileName = "whitelist.bin";
        private const string PluginFileName = "plugin.dll";
        private const string ManifestFileName = "manifest.xml";

        public class Options
        {
            [Option('z', "zip", Group = "input", HelpText = "Add a zip file with xml data files to the input.")]
            public string InputArchive { get; set; }

            [Option("http", Group = "input", HelpText = "Add a zip file with xml data files via url to the input.")]
            public string InputHttp { get; set; }

            [Option('i', "input", Group = "input", HelpText = "List of xml data files or folders containing xml files.")]
            public IEnumerable<string> InputFiles { get; set; }

            [Option('v', "verify", Group = "output", HelpText = "Verifies the content of the xml files without creating an output file.")]
            public bool VerifyOnly { get; set; }

            [Option('o', "out", Group = "output", HelpText = "Name of the final zip file.")]
            public string Output { get; set; }

            [Option("cache", Required = false, HelpText = "Folder of previously compiled plugins.")]
            public string CacheDir { get; set; }

            [Option("steamdir", Required = false, HelpText = "Location of the folder containing Space Engineers Dedicated Server.")]
            public string SteamDir { get; set; }
        }

        public static async Task Main(string[] args)
        {
#if DEBUG
            args = new[] { "--http", @"https://github.com/sepluginloader/PluginHub/archive/main.zip", "--cache", "compiled_plugins", "--steamdir", @"C:\Users\lurkingstar\Desktop\seds", "-o", "plugins.zip" };
#endif

            ParserResult<Options> result = Parser.Default.ParseArguments<Options>(args);
            if (result is Parsed<Options> parsedArgs) // Equivalent to ParserResult<T>.WithParsed<T>()
            {
                Options o = parsedArgs.Value;

                if (o.SteamDir != null)
                {
                    ExeDir = Path.GetFullPath(Path.Combine(o.SteamDir, "DedicatedServer64"));
                    SpaceEngineersExe = Path.GetFullPath(Path.Combine(ExeDir, "SpaceEngineersDedicated.exe"));
                    PluginLoaderDll = Path.GetFullPath(Path.Combine(ExeDir, "PluginLoader.dll"));
                }
                else
                {
                    ExeDir = Path.GetFullPath(Path.Combine("steamapps", "common", "SpaceEngineersDedicatedServer", "DedicatedServer64"));
                    SpaceEngineersExe = Path.GetFullPath(Path.Combine(ExeDir, "SpaceEngineersDedicated.exe"));
                    PluginLoaderDll = Path.GetFullPath(Path.Combine(ExeDir, "PluginLoader.dll"));
                }
                if (!File.Exists(SpaceEngineersExe))
                    throw new Exception("Space Engineers is not installed!");
                if (!File.Exists(PluginLoaderDll))
                    Console.WriteLine("WARNING: Plugin Loader is not installed! Some plugins may depend on the PluginLoader assembly.");

                PluginData[] plugins = await GetPluginsAsync(o);
                if (plugins.Length == 0)
                {
                    Console.WriteLine("No plugins found!");
                    return;
                }

                if (String.IsNullOrEmpty(o.CacheDir))
                {
                    o.CacheDir = Path.Combine(ExeDir, "Plugins");
                }

                o.CacheDir = Path.GetFullPath(o.CacheDir);

                if (o.VerifyOnly)
                {
                    foreach(PluginData plugin in plugins)
                    {
                        if (plugin is GitHubPlugin github)
                            github.InitPaths(o.CacheDir);
                    }
                }
                else if (o.Output != null)
                {
                    LoadPluginReferences();

                    await CompilePlugins(plugins, o.Output, o.CacheDir);
                }

                Console.WriteLine("Done!");
            }
        }

        private static void LoadPluginReferences()
        {
            Assembly.LoadFile(SpaceEngineersExe);
            Assembly.LoadFile(PluginLoaderDll);
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
            RoslynReferences.GenerateAssemblyList();
            AppDomain.CurrentDomain.AssemblyResolve -= CurrentDomain_AssemblyResolve;

        }

        private static Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
        {
            string dll;
            if(args.RequestingAssembly != null)
            {
                if (args.RequestingAssembly.IsDynamic)
                    return null;
                dll = args.RequestingAssembly.Location;
            }
            else
            {
                dll = SpaceEngineersExe;
            }

            string assemblyPath = Path.Combine(Path.GetDirectoryName(dll), new AssemblyName(args.Name).Name + ".dll");
            if (!File.Exists(assemblyPath))
                return null;

            Assembly assembly = Assembly.LoadFile(assemblyPath);
            Console.WriteLine("Loaded " + assembly.FullName);
            return assembly;
        }

        private static async Task<bool> CompilePlugins(PluginData[] plugins, string filePath, string cacheDir)
        {
            using (MemoryStream mem = new MemoryStream())
            {
                using (ZipArchive newArchive = new ZipArchive(mem, ZipArchiveMode.Create, true))
                {
                    IEnumerable<(GitHubPlugin Plugin, Task Task)> compilationTasks =
                        plugins.OfType<GitHubPlugin>().Select(plugin => (plugin, CompilePlugin(newArchive, plugin, cacheDir)));

                    // Calling .ToArray on the IEnumerable starts all of the tasks
                    foreach ((GitHubPlugin plugin, Task task) in compilationTasks.ToArray())
                    {
                        try
                        {
                            await task;
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Failed to compile " + plugin + ": " + e);
                            throw;
                        }

                        Console.WriteLine("Compiled " + plugin);
                    }

                    ZipArchiveEntry whitelistFile = newArchive.CreateEntry(WhitelistFileName);
                    using (Stream s = whitelistFile.Open())
                    {
                        ProtoBuf.Serializer.Serialize(s, plugins);
                    }
                }

                mem.Position = 0;
                using (FileStream fs = File.Create(filePath))
                {
                    await mem.CopyToAsync(fs);
                }
            }

            return true;
        }

        private static SemaphoreSlim _zipLock = new SemaphoreSlim(1, 1);

        private static async Task CompilePlugin(ZipArchive newArchive, GitHubPlugin plugin, string cacheDir)
        {
            if (String.IsNullOrEmpty(cacheDir))
            {
                throw new ArgumentNullException(nameof(cacheDir));
            }

            plugin.InitPaths(cacheDir);

            string rootDir = Path.Combine("GitHub", plugin.UserName, plugin.CompiledAssemblyName);

            using (Stream assemblyData = await plugin.CompilePluginAsync())
            {
                // Do not asynchronously write to zip archives as only one entry can be open at a time.

                string manifestFilePath = Path.Combine(cacheDir, rootDir, ManifestFileName);

                // Ensure only one thread is writing to the ZipArchive at a time
                await _zipLock.WaitAsync();

                ZipArchiveEntry dllFile = newArchive.CreateEntry(Path.Combine(rootDir, PluginFileName));
                using (Stream s = dllFile.Open())
                {
                    assemblyData.CopyTo(s);
                }

                ZipArchiveEntry manifestFile = newArchive.CreateEntry(Path.Combine(rootDir, ManifestFileName));
                using (Stream s = manifestFile.Open())
                using (FileStream current = File.OpenRead(manifestFilePath))
                {
                    current.CopyTo(s);
                }

                _zipLock.Release();
            }
        }

        private static async Task<PluginData[]> GetPluginsAsync(Options o)
        {
            Dictionary<string, PluginData> plugins = new Dictionary<string, PluginData>();

            // Zip file
            if (o.InputArchive != null && File.Exists(o.InputArchive))
            {
                using (Stream inputArchive = File.OpenRead(o.InputArchive))
                {
                    foreach (PluginData data in ParseArchive(inputArchive))
                        plugins[data.Id] = data;
                }
            }

            // Url zip file
            if (o.InputHttp != null && Uri.TryCreate(o.InputHttp, UriKind.Absolute, out Uri inputUrl) && (inputUrl.Scheme == Uri.UriSchemeHttp || inputUrl.Scheme == Uri.UriSchemeHttps))
            {
                foreach (PluginData data in await DownloadArchiveAsync(inputUrl))
                    plugins[data.Id] = data;
            }

            // Xml files
            foreach (string file in o.InputFiles)
            {
                if (file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && File.Exists(file))
                {
                    using (Stream inputFile = File.OpenRead(file))
                    {
                        PluginData data = ParseFile(inputFile);
                        plugins[data.Id] = data;
                    }
                }
                else if (Directory.Exists(file))
                {
                    foreach (string subFile in Directory.EnumerateFiles(file, "*.xml", SearchOption.AllDirectories))
                    {
                        using (Stream inputFile = File.OpenRead(subFile))
                        {
                            PluginData data = ParseFile(inputFile);
                            plugins[data.Id] = data;
                        }
                    }
                }
            }

            return plugins.Values.ToArray();
        }


        private static async Task<List<PluginData>> DownloadArchiveAsync(Uri url)
        {
            List<PluginData> result;
            using (Stream response = await webClient.GetStreamAsync(url))
            using (MemoryStream memory = new MemoryStream())
            {
                await response.CopyToAsync(memory);
                memory.Position = 0;
                result = ParseArchive(memory);
            }
            return result;
        }

        private static List<PluginData> ParseArchive(Stream fileStream)
        {
            List<PluginData> result = new List<PluginData>();
            using (ZipArchive archive = new ZipArchive(fileStream, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        using (Stream entryStream = entry.Open())
                        {
                            result.Add(ParseFile(entryStream));
                        }
                    }
                }
            }
            return result;
        }

        private static PluginData ParseFile(Stream inputFile)
        {
            return (PluginData)pluginDataSerializer.Deserialize(inputFile);
        }
    }
}
