// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;
#if DOTNET5_5
using System.Runtime.Loader;
#endif
using System.Runtime.Versioning;
using Microsoft.AspNet.Diagnostics;
using Microsoft.AspNet.FileProviders;
using Microsoft.AspNet.Mvc.Logging;
using Microsoft.AspNet.Mvc.Razor.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Dnx.Compilation.CSharp;
using Microsoft.Extensions.PlatformAbstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNet.Mvc.Razor.Compilation
{
    /// <summary>
    /// A type that uses Roslyn to compile C# content.
    /// </summary>
    public abstract class RoslynCompilationService : ICompilationService
    {
        private readonly Lazy<bool> _supportsPdbGeneration = new Lazy<bool>(SymbolsUtility.SupportsSymbolsGeneration);
        private readonly ConcurrentDictionary<string, AssemblyMetadata> _metadataFileCache =
            new ConcurrentDictionary<string, AssemblyMetadata>(StringComparer.OrdinalIgnoreCase);

        private readonly IApplicationEnvironment _environment;
        private readonly IFileProvider _fileProvider;
        private readonly Lazy<List<MetadataReference>> _applicationReferences;
        private readonly string _classPrefix;
        private readonly Action<RoslynCompilationContext> _compilationCallback;
        private readonly CSharpParseOptions _parseOptions;
        private readonly CSharpCompilationOptions _compilationOptions;
        private readonly ILogger _logger;

#if DOTNET5_5
        private readonly RazorLoadContext _razorLoadContext;
#endif

        /// <summary>
        /// Initalizes a new instance of the <see cref="RoslynCompilationService"/> class.
        /// </summary>
        /// <param name="environment">The environment for the executing application.</param>
        /// <param name="host">The <see cref="IMvcRazorHost"/> that was used to generate the code.</param>
        /// <param name="optionsAccessor">Accessor to <see cref="RazorViewEngineOptions"/>.</param>
        /// <param name="fileProviderAccessor">The <see cref="IRazorViewEngineFileProviderAccessor"/>.</param>
        /// <param name="loggerFactory">The <see cref="ILoggerFactory"/>.</param>
        public RoslynCompilationService(
            IApplicationEnvironment environment,
            IMvcRazorHost host,
            IOptions<RazorViewEngineOptions> optionsAccessor,
            IRazorViewEngineFileProviderAccessor fileProviderAccessor,
            ILoggerFactory loggerFactory)
        {
            _environment = environment;
            _applicationReferences = new Lazy<List<MetadataReference>>(GetApplicationReferences);
            _fileProvider = fileProviderAccessor.FileProvider;
            _classPrefix = host.MainClassNamePrefix;
            _compilationCallback = optionsAccessor.Value.CompilationCallback;
            _parseOptions = optionsAccessor.Value.ParseOptions;
            _compilationOptions = optionsAccessor.Value.CompilationOptions;
            _logger = loggerFactory.CreateLogger<RoslynCompilationService>();

#if DOTNET5_5
            _razorLoadContext = new RazorLoadContext();
#endif
        }

        /// <inheritdoc />
        public CompilationResult Compile(RelativeFileInfo fileInfo, string compilationContent)
        {
            if (fileInfo == null)
            {
                throw new ArgumentNullException(nameof(fileInfo));
            }

            if (compilationContent == null)
            {
                throw new ArgumentNullException(nameof(compilationContent));
            }

            _logger.GeneratedCodeToAssemblyCompilationStart(fileInfo.RelativePath);

            var startTimestamp = _logger.IsEnabled(LogLevel.Debug) ? Stopwatch.GetTimestamp() : 0;

            var assemblyName = Path.GetRandomFileName();

            var syntaxTree = SyntaxTreeGenerator.Generate(
                compilationContent,
                assemblyName,
                _parseOptions);

            var references = _applicationReferences.Value;

            var compilation = CSharpCompilation.Create(
                assemblyName,
                options: _compilationOptions,
                syntaxTrees: new[] { syntaxTree },
                references: references);

            compilation = Rewrite(compilation);

            var compilationContext = new RoslynCompilationContext(compilation);
            _compilationCallback(compilationContext);
            compilation = compilationContext.Compilation;

            using (var ms = new MemoryStream())
            {
                using (var pdb = new MemoryStream())
                {
                    EmitResult result;

                    if (_supportsPdbGeneration.Value)
                    {
                        result = compilation.Emit(ms, pdbStream: pdb);
                    }
                    else
                    {
                        result = compilation.Emit(ms);
                    }

                    if (!result.Success)
                    {
                        return GetCompilationFailedResult(
                            fileInfo.RelativePath,
                            compilationContent,
                            assemblyName,
                            result.Diagnostics);
                    }

                    Assembly assembly;
                    ms.Seek(0, SeekOrigin.Begin);

                    if (_supportsPdbGeneration.Value)
                    {
                        pdb.Seek(0, SeekOrigin.Begin);
                        assembly = LoadStream(ms, pdb);
                    }
                    else
                    {
                        assembly = LoadStream(ms, assemblySymbols: null);
                    }

                    var type = assembly
                        .GetExportedTypes()
                        .First(t => t.Name.StartsWith(_classPrefix, StringComparison.Ordinal));

                    _logger.GeneratedCodeToAssemblyCompilationEnd(fileInfo.RelativePath, startTimestamp);

                    return new CompilationResult(type);
                }
            }
        }

        private Assembly LoadStream(MemoryStream ms, MemoryStream assemblySymbols)
        {
#if NET451
            return Assembly.Load(ms.ToArray(), assemblySymbols?.ToArray());
#else
            return _razorLoadContext.Load(ms, assemblySymbols);
#endif
        }

        private CSharpCompilation Rewrite(CSharpCompilation compilation)
        {
            var rewrittenTrees = new List<SyntaxTree>();
            foreach (var tree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
                var rewriter = new ExpressionRewriter(semanticModel);

                var rewrittenTree = tree.WithRootAndOptions(rewriter.Visit(tree.GetRoot()), tree.Options);
                rewrittenTrees.Add(rewrittenTree);
            }

            return compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(rewrittenTrees);
        }

        // Internal for unit testing
        internal CompilationResult GetCompilationFailedResult(
            string relativePath,
            string compilationContent,
            string assemblyName,
            IEnumerable<Diagnostic> diagnostics)
        {
            var diagnosticGroups = diagnostics
                .Where(IsError)
                .GroupBy(diagnostic => GetFilePath(relativePath, diagnostic), StringComparer.Ordinal);

            var failures = new List<CompilationFailure>();
            foreach (var group in diagnosticGroups)
            {
                var sourceFilePath = group.Key;
                string sourceFileContent;
                if (string.Equals(assemblyName, sourceFilePath, StringComparison.Ordinal))
                {
                    // The error is in the generated code and does not have a mapping line pragma
                    sourceFileContent = compilationContent;
                    sourceFilePath = Resources.GeneratedCodeFileName;
                }
                else
                {
                    sourceFileContent = ReadFileContentsSafely(_fileProvider, sourceFilePath);
                }

                var compilationFailure = new CompilationFailure(
                    sourceFilePath,
                    sourceFileContent,
                    compilationContent,
                    group.Select(diagnostic => GetDiagnosticMessage(diagnostic, _environment.RuntimeFramework)));

                failures.Add(compilationFailure);
            }

            return new CompilationResult(failures);
        }

        private static string GetFilePath(string relativePath, Diagnostic diagnostic)
        {
            if (diagnostic.Location == Location.None)
            {
                return relativePath;
            }

            return diagnostic.Location.GetMappedLineSpan().Path;
        }

        protected abstract List<MetadataReference> GetApplicationReferences();

        protected MetadataReference CreateMetadataFileReference(string path)
        {
            var metadata = _metadataFileCache.GetOrAdd(path, _ =>
            {
                using (var stream = File.OpenRead(path))
                {
                    var moduleMetadata = ModuleMetadata.CreateFromStream(stream, PEStreamOptions.PrefetchMetadata);
                    return AssemblyMetadata.Create(moduleMetadata);
                }
            });

            return metadata.GetReference(filePath: path);
        }

        private static bool IsError(Diagnostic diagnostic)
        {
            return diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error;
        }

        private static string ReadFileContentsSafely(IFileProvider fileProvider, string filePath)
        {
            var fileInfo = fileProvider.GetFileInfo(filePath);
            if (fileInfo.Exists)
            {
                try
                {
                    using (var reader = new StreamReader(fileInfo.CreateReadStream()))
                    {
                        return reader.ReadToEnd();
                    }
                }
                catch
                {
                    // Ignore any failures
                }
            }

            return null;
        }

        private static DiagnosticMessage GetDiagnosticMessage(Diagnostic diagnostic, FrameworkName targetFramework)
        {
            var mappedLineSpan = diagnostic.Location.GetMappedLineSpan();
            return new DiagnosticMessage(
                diagnostic.GetMessage(),
                RoslynDiagnosticFormatter.Format(diagnostic, targetFramework),
                mappedLineSpan.Path,
                mappedLineSpan.StartLinePosition.Line + 1,
                mappedLineSpan.StartLinePosition.Character + 1,
                mappedLineSpan.EndLinePosition.Line + 1,
                mappedLineSpan.EndLinePosition.Character + 1);
        }

#if DOTNET5_5
        private class RazorLoadContext : AssemblyLoadContext
        {
            protected override Assembly Load(AssemblyName assemblyName)
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }

            public Assembly Load(Stream assembly, Stream assemblySymbols)
            {
                return LoadFromStream(assembly, assemblySymbols);
            }
        }
#endif
    }
}
