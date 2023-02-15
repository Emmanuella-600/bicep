// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Bicep.Core.Configuration;
using Bicep.Core.Parsing;
using Bicep.LanguageServer.Providers;
using Bicep.LanguageServer.Telemetry;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Bicep.LanguageServer.Completions
{
    public class ModuleReferenceCompletionProvider : IModuleReferenceCompletionProvider
    {
        private readonly IConfigurationManager configurationManager;
        private readonly IModulesMetadataProvider modulesMetadataProvider;
        private readonly IServiceClientCredentialsProvider serviceClientCredentialsProvider;

        private static readonly Dictionary<string, string> BicepRegistryAndTemplateSpecShemaCompletionLabelsWithDetails = new Dictionary<string, string>()
        {
            {"br:", "Bicep registry schema name" },
            {"br/", "Bicep registry schema name" },
            {"ts:", "Template spec schema name" },
            {"ts/", "Template spec schema name" },
        };

        private static readonly Regex McrPublicModuleRegistryAliasWithPath = new Regex(@"br/public:(?<filePath>(.*?)):", RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase);
        private static readonly Regex McrPublicModuleRegistryWithoutAliasWithPath = new Regex(@"br:mcr.microsoft.com/bicep/(?<filePath>(.*?)):", RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase);

        public ModuleReferenceCompletionProvider(
            IConfigurationManager configurationManager,
            IModulesMetadataProvider modulesMetadataProvider,
            IServiceClientCredentialsProvider serviceClientCredentialsProvider)
        {
            this.configurationManager = configurationManager;
            this.modulesMetadataProvider = modulesMetadataProvider;
            this.serviceClientCredentialsProvider = serviceClientCredentialsProvider;
        }

        public IEnumerable<CompletionItem> GetFilteredCompletions(Uri templateUri, BicepCompletionContext context)
        {
            var replacementText = string.Empty;

            if (context.ReplacementTarget is Token token)
            {
                replacementText = token.Text;
            }

            return GetPublicMcrModuleRegistryCompletions(context, replacementText)
                .Concat(GetPublicMcrModuleRegistryVersionCompletions(context, replacementText))
                .Concat(GetRegistryCompletions(context, replacementText, templateUri))
                .Concat(GetBicepRegistryAndTemplateSpecShemaCompletions(context, replacementText));
        }

        private IEnumerable<CompletionItem> GetBicepRegistryAndTemplateSpecShemaCompletions(BicepCompletionContext context, string replacementText)
        {
            if (!context.Kind.HasFlag(BicepCompletionContextKind.ModulePath))
            {
                return Enumerable.Empty<CompletionItem>();
            }

            if (!string.IsNullOrWhiteSpace(replacementText.Trim('\'')))
            {
                return Enumerable.Empty<CompletionItem>();
            }

            List<CompletionItem> completionItems = new List<CompletionItem>();
            foreach (var kvp in BicepRegistryAndTemplateSpecShemaCompletionLabelsWithDetails)
            {
                var text = kvp.Key;
                var sb = new StringBuilder();
                sb.Append("'");
                sb.Append(text);
                sb.Append("$0");
                sb.Append("'");

                var completionText = sb.ToString();

                BicepTelemetryEvent telemetryEvent = BicepTelemetryEvent.CreateBicepRegistryOrTemplateSpecShemaCompletion(text);
                var command = TelemetryHelper.CreateCommand
                (
                    title: "Bicep registry or template spec shema completion",
                    name: TelemetryConstants.CommandName,
                    args: JArray.FromObject(new List<object> { telemetryEvent })
                );

                var completionItem = CompletionItemBuilder.Create(CompletionItemKind.Reference, text)
                    .WithFilterText(completionText)
                    .WithSortText(GetSortText(text, CompletionPriority.Medium))
                    .WithSnippetEdit(context.ReplacementRange, completionText)
                    .WithDetail(kvp.Value)
                    .WithCommand(command)
                    .Build();
                completionItems.Add(completionItem);
            }

            return completionItems;
        }

        private IEnumerable<CompletionItem> GetPublicMcrModuleRegistryVersionCompletions(BicepCompletionContext context, string replacementText)
        {
            if (!context.Kind.HasFlag(BicepCompletionContextKind.OciModuleRegistryReference))
            {
                return Enumerable.Empty<CompletionItem>();
            }

            string? filePath = null;

            if (McrPublicModuleRegistryAliasWithPath.IsMatch(replacementText))
            {
                var matches = McrPublicModuleRegistryAliasWithPath.Matches(replacementText);
                filePath = matches[0].Groups["filePath"].Value;
            }
            if (McrPublicModuleRegistryWithoutAliasWithPath.IsMatch(replacementText))
            {
                var matches = McrPublicModuleRegistryWithoutAliasWithPath.Matches(replacementText);
                filePath = matches[0].Groups["filePath"].Value;
            }

            if (filePath is null)
            {
                return Enumerable.Empty<CompletionItem>();
            }

            List<CompletionItem> completions = new List<CompletionItem>();
            replacementText = replacementText.TrimEnd('\'');
            foreach (var version in modulesMetadataProvider.GetVersions(filePath))
            {
                StringBuilder sb = new StringBuilder(replacementText);
                sb.Append(version);
                sb.Append("'$0");

                var insertText = sb.ToString();

                var completionItem = CompletionItemBuilder.Create(CompletionItemKind.Snippet, version)
                    .WithSnippetEdit(context.ReplacementRange, insertText)
                    .WithFilterText(insertText)
                    .WithSortText(GetSortText(version, CompletionPriority.High))
                    .Build();

                completions.Add(completionItem);
            }

            return completions;
        }

        private IEnumerable<CompletionItem> GetPublicMcrModuleRegistryCompletions(BicepCompletionContext context, string replacementText)
        {
            if (!context.Kind.HasFlag(BicepCompletionContextKind.OciModuleRegistryReference))
            {
                return Enumerable.Empty<CompletionItem>();
            }

            List<CompletionItem> completions = new List<CompletionItem>();

            if (replacementText == "'br/public:'" ||
                replacementText == "'br:mcr.microsoft.com/bicep/'" ||
                replacementText == "'br/public:" ||
                replacementText == "'br:mcr.microsoft.com/bicep/")
            {
                foreach (var moduleName in modulesMetadataProvider.GetModuleNames())
                {
                    StringBuilder sb = new StringBuilder(replacementText.TrimEnd('\''));
                    sb.Append(moduleName);
                    sb.Append(":$0'");

                    var insertText = sb.ToString();

                    var completionItem = CompletionItemBuilder.Create(CompletionItemKind.Snippet, moduleName)
                        .WithSnippetEdit(context.ReplacementRange, insertText)
                        .WithFilterText(insertText)
                        .WithSortText(GetSortText(moduleName, CompletionPriority.High))
                        .Build();

                    completions.Add(completionItem);
                }
            }

            return completions;
        }

        private IEnumerable<CompletionItem> GetRegistryCompletions(BicepCompletionContext context, string replacementText, Uri templateUri)
        {
            if (!context.Kind.HasFlag(BicepCompletionContextKind.OciModuleRegistryReference))
            {
                return Enumerable.Empty<CompletionItem>();
            }

            var replacementTextWithTrimmedEnd = replacementText.TrimEnd('\'');

            var completions = new List<CompletionItem>();
            if (replacementTextWithTrimmedEnd == "'br/")
            {
                var rootConfiguration = configurationManager.GetConfiguration(templateUri);
                var ociArtifactModuleAliases = rootConfiguration.ModuleAliases.GetOciArtifactModuleAliases();

                foreach (var kvp in ociArtifactModuleAliases)
                {
                    var alias = kvp.Key;
                    var insertText = $"{replacementTextWithTrimmedEnd}{alias}:$0'";
                    var completionItem = CompletionItemBuilder.Create(CompletionItemKind.Snippet, alias)
                        .WithFilterText(insertText)
                        .WithSnippetEdit(context.ReplacementRange, insertText)
                        .WithSortText(GetSortText(alias, CompletionPriority.High))
                        .Build();
                    completions.Add(completionItem);
                }
            }
            else if (replacementTextWithTrimmedEnd == "'br:")
            {
                var label = "mcr.microsoft.com/bicep/";
                var insertText = $"{replacementTextWithTrimmedEnd}{label}$0'";
                var mcrCompletionItem = CompletionItemBuilder.Create(CompletionItemKind.Snippet, label)
                    .WithFilterText(insertText)
                    .WithSnippetEdit(context.ReplacementRange, insertText)
                    .WithSortText(GetSortText(label, CompletionPriority.High))
                    .Build();

                completions.Add(mcrCompletionItem);
            }

            return completions;
        }

        private static string GetSortText(string label, CompletionPriority priority) => $"{(int)priority}_{label}";
    }
}
