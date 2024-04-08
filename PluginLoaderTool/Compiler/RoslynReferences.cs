﻿using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace avaness.PluginLoaderTool.Compiler
{
    public static class RoslynReferences
    {
        private static Dictionary<string, MetadataReference> allReferences = new Dictionary<string, MetadataReference>();
        private static readonly HashSet<string> referenceBlacklist = new HashSet<string>(new[] { "System.ValueTuple", "protobuf-net", "protobuf-net.Core" });

        public static void GenerateAssemblyList()
        {
            if (allReferences.Count > 0)
                return;

            AssemblyName harmonyInfo = typeof(HarmonyLib.Harmony).Assembly.GetName();

            Stack<Assembly> loadedAssemblies = new Stack<Assembly>(AppDomain.CurrentDomain.GetAssemblies().Where(IsValidReference));

            StringBuilder sb = new StringBuilder();

            sb.AppendLine();
            string line = "===================================";
            sb.AppendLine(line);
            sb.AppendLine("Assembly References");
            sb.AppendLine(line);

            try
            {
                foreach (Assembly a in loadedAssemblies)
                {
                    // Prevent other Harmony versions from being loaded
                    AssemblyName name = a.GetName();
                    if (name.Name == harmonyInfo.Name && name.Version != harmonyInfo.Version)
                    {
                        continue;
                    }

                    AddAssemblyReference(a);
                    sb.AppendLine(a.FullName);
                }
                foreach (Assembly a in GetOtherReferences())
                {
                    AddAssemblyReference(a);
                    sb.AppendLine(a.FullName);
                }
                sb.AppendLine(line);
                while (loadedAssemblies.Count > 0)
                {
                    Assembly a = loadedAssemblies.Pop();

                    foreach (AssemblyName name in a.GetReferencedAssemblies())
                    {
                        // Prevent other Harmony versions from being loaded
                        if (name.Name == harmonyInfo.Name && name.Version != harmonyInfo.Version)
                        {
                            continue;
                        }

                        if (!ContainsReference(name) && TryLoadAssembly(name, out Assembly aRef) && IsValidReference(aRef))
                        {
                            AddAssemblyReference(aRef);
                            sb.AppendLine(name.FullName);
                            loadedAssemblies.Push(aRef);
                        }
                    }
                }
                sb.AppendLine(line);
            }
            catch (Exception e)
            {
                sb.Append("Error: ").Append(e).AppendLine();
            }
            Console.WriteLine(sb.ToString());
        }

        /// <summary>
        /// This method is used to load references that otherwise would not exist or be optimized out
        /// </summary>
        private static IEnumerable<Assembly> GetOtherReferences()
        {
            yield return typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly;
        }

        private static bool ContainsReference(AssemblyName name)
        {
            return allReferences.ContainsKey(name.Name);
        }

        private static bool TryLoadAssembly(AssemblyName name, out Assembly aRef)
        {
            try
            {
                aRef = Assembly.Load(name);
                return true;
            }
            catch (IOException)
            {
                aRef = null;
                return false;
            }
        }

        private static void AddAssemblyReference(Assembly a)
        {
            string name = a.GetName().Name;
            if (!allReferences.ContainsKey(name))
                allReferences.Add(name, MetadataReference.CreateFromFile(a.Location));
        }

        public static IEnumerable<MetadataReference> EnumerateAllReferences()
        {
            return allReferences.Values;
        }

        private static bool IsValidReference(Assembly a)
        {
            return !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location) && !referenceBlacklist.Contains(a.GetName().Name);
        }

        public static bool Contains(string id)
        {
            return allReferences.ContainsKey(id);
        }
    }
}
