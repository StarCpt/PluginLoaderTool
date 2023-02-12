using avaness.PluginLoaderTool.Compiler;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace avaness.PluginLoaderTool.Data
{
    [ProtoContract]
    public class GitHubPlugin : PluginData
    {
        [ProtoMember(1)]
        public string Commit { get; set; }

        [ProtoMember(2)]
        [XmlArray]
        [XmlArrayItem("Directory")]
        public string[] SourceDirectories { get; set; }

        [XmlIgnore]
        public string CompiledAssemblyName { get; private set; }
        [XmlIgnore]
        public string UserName { get; private set; }

        private string assemblyNameSafe;

        public GitHubPlugin()
        { }

        public void Init()
        {
            string[] nameArgs = Id.Split(new char[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (nameArgs.Length != 2)
                throw new Exception("Invalid GitHub name: " + Id);

            if (SourceDirectories != null)
            {
                for (int i = SourceDirectories.Length - 1; i >= 0; i--)
                {
                    string path = SourceDirectories[i].Replace('\\', '/').TrimStart('/');

                    if (path.Length == 0)
                        continue;

                    if (path[path.Length - 1] != '/')
                        path += '/';


                    SourceDirectories[i] = path;
                }
            }

            UserName = nameArgs[0];
            CompiledAssemblyName = nameArgs[1];
            assemblyNameSafe = MakeSafeString(nameArgs[1]);
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


        public byte[] CompileFromSource(Stream archive)
        {
            RoslynCompiler compiler = new RoslynCompiler();
            using (ZipArchive zip = new ZipArchive(archive))
            {
                for (int i = 0; i < zip.Entries.Count; i++)
                {
                    ZipArchiveEntry entry = zip.Entries[i];
                    CompileFromSource(compiler, entry);
                }
            }
            return compiler.Compile(assemblyNameSafe, out _);
        }

        private void CompileFromSource(RoslynCompiler compiler, ZipArchiveEntry entry)
        {
            if (AllowedZipPath(entry.FullName))
            {
                using (Stream entryStream = entry.Open())
                {
                    compiler.Load(entryStream, entry.FullName);
                }
            }
        }

        private bool AllowedZipPath(string path)
        {
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                return false;

            if (SourceDirectories == null || SourceDirectories.Length == 0)
                return true;

            path = RemoveRoot(path); // Make the base of the path the root of the repository

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
    }
}