// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNet.Mvc.Razor;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyModel;

namespace Microsoft.AspNet.Mvc
{
    /// <summary>
    /// Sets up compilation and parse option default options for <see cref="RazorViewEngineOptions"/> using <see cref="DependencyContext"/>
    /// </summary>
    public class DependencyContextRazorViewEngineOptionsSetup : ConfigureOptions<RazorViewEngineOptions>
    {
        /// <summary>
        /// Initializes a new instance of <see cref="DependencyContextRazorViewEngineOptionsSetup"/>.
        /// </summary>
        public DependencyContextRazorViewEngineOptionsSetup() : this(DependencyContext.Default)
        {
        }

        /// <summary>
        /// Initializes a new instance of <see cref="DependencyContextRazorViewEngineOptionsSetup"/>.
        /// </summary>
        /// <param name="dependencyContext"><see cref="DependencyContext"/> to use as compilation and parse option source.</param>
        public DependencyContextRazorViewEngineOptionsSetup(DependencyContext dependencyContext) : base(options => ConfigureRazor(options, dependencyContext))
        {
        }

        private static void ConfigureRazor(RazorViewEngineOptions options, DependencyContext dependencyContext)
        {
            var compilationOptions = dependencyContext.CompilationOptions;

            SetParseOptions(options, compilationOptions);
            SetCompilationOptions(options, compilationOptions);
        }

        private static void SetCompilationOptions(RazorViewEngineOptions options, Microsoft.Extensions.DependencyModel.CompilationOptions compilationOptions)
        {
            var roslynOptions = options.CompilationOptions;

            // Disable 1702 until roslyn turns this off by default
            roslynOptions = roslynOptions.WithSpecificDiagnosticOptions(
                new Dictionary<string, ReportDiagnostic>
                {
                    {"CS1701", ReportDiagnostic.Suppress}, // Binding redirects
                    {"CS1702", ReportDiagnostic.Suppress},
                    {"CS1705", ReportDiagnostic.Suppress}
                });

            if (compilationOptions.AllowUnsafe.HasValue)
            {
                roslynOptions = roslynOptions.WithAllowUnsafe(compilationOptions.AllowUnsafe.Value);
            }

            if (compilationOptions.Optimize.HasValue)
            {
                var optimizationLevel = compilationOptions.Optimize.Value ? OptimizationLevel.Debug : OptimizationLevel.Release;
                roslynOptions = roslynOptions.WithOptimizationLevel(optimizationLevel);
            }

            if (compilationOptions.WarningsAsErrors.HasValue)
            {
                var reportDiagnostic = compilationOptions.WarningsAsErrors.Value ? ReportDiagnostic.Error : ReportDiagnostic.Default;
                roslynOptions = roslynOptions.WithGeneralDiagnosticOption(reportDiagnostic);
            }

            options.CompilationOptions = roslynOptions;
        }

        private static void SetParseOptions(RazorViewEngineOptions options, Microsoft.Extensions.DependencyModel.CompilationOptions compilationOptions)
        {
            var roslynParseOptions = options.ParseOptions;
            roslynParseOptions = roslynParseOptions.WithPreprocessorSymbols(compilationOptions.Defines);

            var languageVersion = roslynParseOptions.LanguageVersion;
            if (Enum.TryParse(compilationOptions.LanguageVersion, ignoreCase: true, result: out languageVersion))
            {
                roslynParseOptions = roslynParseOptions.WithLanguageVersion(languageVersion);
            }

            options.ParseOptions = roslynParseOptions;
        }
    }
}