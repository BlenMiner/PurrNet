using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Mono.Cecil;
using NUnit.Framework;

namespace PurrNet.Codegen.Tests
{
    public sealed class AssemblyResolverTests
    {
        private string _directory;
        private readonly List<AssemblyDefinition> _assemblies = new List<AssemblyDefinition>();

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "PurrNet-AssemblyResolver-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var assembly in _assemblies)
                assembly.Dispose();
            _assemblies.Clear();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }

        [Test]
        public void SuccessfulResolutionRemainsConsistentUntilTheNextInvocation()
        {
            var path = WriteAssembly("references/Dependency.dll", 1);
            var resolver = Resolver(path);
            var first = Resolve(resolver, "Dependency");

            WriteAssembly("references/Dependency.dll", 2);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

            Assert.That(Resolve(resolver, "Dependency"), Is.SameAs(first));
            Assert.That(first.Name.Version.Major, Is.EqualTo(1));
            Assert.That(Resolve(Resolver(path), "Dependency").Name.Version.Major, Is.EqualTo(2));
        }

        [Test]
        public void SuccessfulResolutionDoesNotRequireTheBackingFileOnCacheHits()
        {
            var path = WriteAssembly("references/Dependency.dll", 1);
            var resolver = Resolver(path);
            var first = Resolve(resolver, "Dependency");
            File.Delete(path);

            Assert.That(Resolve(resolver, "Dependency"), Is.SameAs(first));
        }

        [Test]
        public void ExplicitDllWinsOverAnEarlierExeReference()
        {
            var exe = WriteAssembly("exe/Dependency.exe", 1);
            var dll = WriteAssembly("dll/Dependency.dll", 2);

            Assert.That(Resolve(Resolver(exe, dll), "Dependency").Name.Version.Major, Is.EqualTo(2));
        }

        [Test]
        public void FirstExplicitDllWinsWhenReferenceNamesRepeat()
        {
            var first = WriteAssembly("first/Dependency.dll", 1);
            var second = WriteAssembly("second/Dependency.dll", 2);

            Assert.That(Resolve(Resolver(first, second), "Dependency").Name.Version.Major, Is.EqualTo(1));
        }

        [Test]
        public void ExplicitReferenceNamesUseOrdinalMatching()
        {
            var otherCase = WriteAssembly("upper/DEPENDENCY.dll", 2);
            var exactCase = WriteAssembly("exact/Dependency.dll", 1);

            Assert.That(Resolve(Resolver(otherCase, exactCase), "Dependency").Name.Version.Major, Is.EqualTo(1));
        }

        [Test]
        public void ExplicitExeWinsOverDllInAFallbackDirectory()
        {
            WriteAssembly("fallback/Dependency.dll", 1);
            var exe = WriteAssembly("explicit/Dependency.exe", 2);
            var anchor = Path.Combine(_directory, "fallback/Anchor.dll");

            Assert.That(Resolve(Resolver(anchor, exe), "Dependency").Name.Version.Major, Is.EqualTo(2));
        }

        [Test]
        public void FallbackDirectoriesRetainReferenceOrder()
        {
            WriteAssembly("first/Dependency.dll", 1);
            WriteAssembly("second/Dependency.dll", 2);
            var firstAnchor = Path.Combine(_directory, "first/Anchor.dll");
            var secondAnchor = Path.Combine(_directory, "second/Anchor.dll");

            Assert.That(Resolve(Resolver(secondAnchor, firstAnchor, secondAnchor), "Dependency").Name.Version.Major,
                Is.EqualTo(2));
        }

        [Test]
        public void FailedLookupCanFindADependencyCreatedLater()
        {
            var anchor = Path.Combine(_directory, "references/Anchor.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(anchor));
            var resolver = Resolver(anchor);

            Assert.That(resolver.Resolve(Name("Dependency")), Is.Null);
            WriteAssembly("references/Dependency.dll", 1);

            Assert.That(Resolve(resolver, "Dependency").Name.Version.Major, Is.EqualTo(1));
        }

        [Test]
        public void ReferenceListIsSnapshottedAtConstruction()
        {
            var first = WriteAssembly("first/Dependency.dll", 1);
            var second = WriteAssembly("second/Dependency.dll", 2);
            var references = new[] { first };
            var resolver = Resolver(references);
            references[0] = second;

            Assert.That(Resolve(resolver, "Dependency").Name.Version.Major, Is.EqualTo(1));
        }

        [Test]
        public void SelfResolutionWinsWithoutAnyReferenceFile()
        {
            var resolver = Resolver();
            var self = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("TestInput", new Version(1, 0)),
                "TestInput", ModuleKind.Dll);
            _assemblies.Add(self);
            resolver.SetSelf(self);

            Assert.That(resolver.Resolve(Name("TestInput")), Is.SameAs(self));
        }

        private AssemblyDefinition Resolve(AssemblyResolver resolver, string name)
        {
            var assembly = resolver.Resolve(Name(name));
            if (assembly != null && !_assemblies.Contains(assembly))
                _assemblies.Add(assembly);
            return assembly;
        }

        private static AssemblyNameReference Name(string name) => new AssemblyNameReference(name, new Version(0, 0));

        private static AssemblyResolver Resolver(params string[] references)
        {
            // Keep these tests independent of Unity's ILPP host interface assembly.
            var constructor = typeof(AssemblyResolver).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(string), typeof(string[]) }, null);
            Assert.That(constructor, Is.Not.Null);
            return (AssemblyResolver)constructor.Invoke(new object[] { "TestInput", references });
        }

        private string WriteAssembly(string relativePath, int version)
        {
            var path = Path.Combine(_directory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var name = Path.GetFileNameWithoutExtension(path);
            using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name, new Version(version, 0)),
                name, ModuleKind.Dll);
            assembly.Write(path);
            return path;
        }
    }
}
