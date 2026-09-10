#if UNITY_MONO_CECIL
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace PurrNet.Codegen
{
    public class AssemblyResolver : IAssemblyResolver
    {
        private readonly Dictionary<string, string> m_ReferencePathsByFileName =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly string[] m_SearchDirectories;

        // One resolver belongs to one Process invocation. Its compiled inputs are a snapshot:
        // after loading an assembly, keep that definition consistent for the entire invocation.
        // A later invocation gets a new resolver and observes any changed reference files.
        private readonly Dictionary<string, AssemblyDefinition> m_AssemblyCache =
            new Dictionary<string, AssemblyDefinition>(StringComparer.Ordinal);

        private readonly string m_CompiledAssemblyName;
        private AssemblyDefinition m_SelfAssembly;

        public AssemblyResolver(ICompiledAssembly compiledAssembly)
            : this(compiledAssembly.Name, compiledAssembly.References)
        {
        }

        internal AssemblyResolver(string compiledAssemblyName, string[] references)
        {
            m_CompiledAssemblyName = compiledAssemblyName;
            var directories = new List<string>();
            var seenDirectories = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reference in references)
            {
                var fileName = Path.GetFileName(reference);
                if (fileName != null && !m_ReferencePathsByFileName.ContainsKey(fileName))
                    m_ReferencePathsByFileName.Add(fileName, reference);

                var directory = Path.GetDirectoryName(reference);
                if (seenDirectories.Add(directory))
                    directories.Add(directory);
            }
            m_SearchDirectories = directories.ToArray();
        }

        public void Dispose()
        {
        }

        public AssemblyDefinition Resolve(AssemblyNameReference name) =>
            Resolve(name, null);

        public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
        {
            lock (m_AssemblyCache)
            {
                if (name.Name == m_CompiledAssemblyName)
                {
                    return m_SelfAssembly;
                }

                if (m_AssemblyCache.TryGetValue(name.Name, out var result))
                    return result;

                var fileName = FindFile(name);
                if (fileName == null)
                {
                    return null;
                }

                parameters ??= new ReaderParameters(ReadingMode.Deferred);
                parameters.AssemblyResolver = this;

                var ms = MemoryStreamFor(fileName);
                var pdb = $"{fileName}.pdb";
                if (File.Exists(pdb))
                {
                    parameters.SymbolStream = MemoryStreamFor(pdb);
                }

                var assemblyDefinition = AssemblyDefinition.ReadAssembly(ms, parameters);
                m_AssemblyCache.Add(name.Name, assemblyDefinition);

                return assemblyDefinition;
            }
        }

        private string FindFile(AssemblyNameReference name)
        {
            if (m_ReferencePathsByFileName.TryGetValue($"{name.Name}.dll", out var fileName))
            {
                return fileName;
            }

            // perhaps the type comes from an exe instead
            if (m_ReferencePathsByFileName.TryGetValue($"{name.Name}.exe", out fileName))
            {
                return fileName;
            }

            // Do not cache misses: a dependency can become available before a later lookup.
            foreach (var directory in m_SearchDirectories)
            {
                var candidate = Path.Combine(directory, $"{name.Name}.dll");
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        private static MemoryStream MemoryStreamFor(string fileName)
        {
            return Retry(10, TimeSpan.FromSeconds(1), () =>
            {
                byte[] byteArray;
                using (var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byteArray = new byte[fs.Length];
                    var readLength = fs.Read(byteArray, 0, (int)fs.Length);
                    if (readLength != fs.Length)
                    {
                        throw new InvalidOperationException("File read length is not full length of file.");
                    }
                }

                return new MemoryStream(byteArray);
            });
        }

        private static MemoryStream Retry(int retryCount, TimeSpan waitTime, Func<MemoryStream> func)
        {
            try
            {
                return func();
            }
            catch (IOException)
            {
                if (retryCount == 0)
                {
                    throw;
                }

                Console.WriteLine($"Caught IO Exception, trying {retryCount} more times");
                Thread.Sleep(waitTime);

                return Retry(retryCount - 1, waitTime, func);
            }
        }

        public void SetSelf(AssemblyDefinition assemblyDefinition)
        {
            m_SelfAssembly = assemblyDefinition;
        }
    }
}
#endif
