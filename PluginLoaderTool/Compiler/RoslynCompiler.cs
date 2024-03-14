using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace avaness.PluginLoaderTool.Compiler
{
    public class RoslynCompiler
    {
        private readonly ConcurrentBag<Source> source = new ConcurrentBag<Source>();
        private readonly ConcurrentBag<MetadataReference> customReferences = new ConcurrentBag<MetadataReference>();
        private bool debugBuild;

        public RoslynCompiler(bool debugBuild = false)
        {
            this.debugBuild = debugBuild;
        }

        public void Load(Stream s, string name)
        {
            MemoryStream mem = new MemoryStream();
            using (mem)
            {
                s.CopyTo(mem);
                source.Add(new Source(mem, name, debugBuild));
            }
        }

        public byte[] Compile(string assemblyName, out byte[] symbols)
        {
            symbols = null;

            var sourceCopy = source.ToArray();

            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees: sourceCopy.Select(x => x.Tree),
                references: RoslynReferences.EnumerateAllReferences().Concat(customReferences.ToArray()),
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: debugBuild ? OptimizationLevel.Debug : OptimizationLevel.Release,
                    allowUnsafe: true));

            using (MemoryStream pdb = new MemoryStream())
            using (MemoryStream ms = new MemoryStream())
            {
                // write IL code into memory
                EmitResult result;
                if (debugBuild)
                {
                    result = compilation.Emit(ms, pdb,
                        embeddedTexts: sourceCopy.Select(x => x.Text),
                        options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb, pdbFilePath: Path.ChangeExtension(assemblyName, "pdb")));
                }
                else
                {
                    result = compilation.Emit(ms);
                }

                if (!result.Success)
                {
                    // handle exceptions
                    IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsWarningAsError ||
                        diagnostic.Severity == DiagnosticSeverity.Error);

                    foreach (Diagnostic diagnostic in failures)
                    {
                        Location location = diagnostic.Location;
                        Source source = sourceCopy.FirstOrDefault(x => x.Tree == location.SourceTree);
                        Console.WriteLine($"{diagnostic.Id}: {diagnostic.GetMessage()} in file:\n{source?.Name ?? "null"} ({location.GetLineSpan().StartLinePosition})");
                    }
                    throw new Exception("Compilation failed!");
                }
                else
                {
                    if (debugBuild)
                    {
                        pdb.Seek(0, SeekOrigin.Begin);
                        symbols = pdb.ToArray();
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    return ms.ToArray();
                }
            }

        }

        public void TryAddDependency(string dll)
        {
            if (Path.HasExtension(dll)
                && Path.GetExtension(dll).Equals(".dll", StringComparison.OrdinalIgnoreCase)
                && File.Exists(dll))
            {
                try
                {
                    MetadataReference reference = MetadataReference.CreateFromFile(dll);
                    if (reference != null)
                    {
                        Console.WriteLine("Custom compiler reference: " + (reference.Display ?? dll));
                        customReferences.Add(reference);
                    }
                }
                catch
                { }
            }
        }

        private class Source
        {
            public string Name { get; }
            public SyntaxTree Tree { get; }
            public EmbeddedText Text { get; }

            public Source(Stream s, string name, bool includeText)
            {
                Name = name;
                SourceText source = SourceText.From(s, canBeEmbedded: includeText);
                if (includeText)
                {
                    Text = EmbeddedText.FromSource(name, source);
                    Tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest), name);
                }
                else
                {
                    Tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
                }
            }
        }
    }

}
