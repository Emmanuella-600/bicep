// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.UnitTests;
using Bicep.Core.UnitTests.Mock;
using Bicep.Core.UnitTests.Utils;
using Bicep.LanguageServer;
using Bicep.LanguageServer.Completions;
using Bicep.LanguageServer.Providers;
using Bicep.LanguageServer.Settings;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using ConfigurationManager = Bicep.Core.Configuration.ConfigurationManager;
using IOFileSystem = System.IO.Abstractions.FileSystem;

namespace Bicep.LangServer.UnitTests.Completions
{
    [TestClass]
    public class ModuleReferenceCompletionProviderTests
    {
        [NotNull]
        public TestContext? TestContext { get; set; }

        private static ModulesMetadataProvider modulesMetadataProvider = new ModulesMetadataProvider();
        private IAzureContainerRegistryNamesProvider azureContainerRegistryNamesProvider = StrictMock.Of<IAzureContainerRegistryNamesProvider>().Object;
        private ISettingsProvider settingsProvider = StrictMock.Of<ISettingsProvider>().Object;

        [ClassInitialize]
        public static async Task ClassInitialize(TestContext testContext)
        {
            await modulesMetadataProvider.Initialize();
        }

        [DataTestMethod]
        [DataRow("module test |''", 14)]
        [DataRow("module test ''|", 14)]
        [DataRow("module test '|'", 14)]
        [DataRow("module test '|", 13)]
        [DataRow("module test |", 12)]
        public async Task GetFilteredCompletions_WithBicepRegistryAndTemplateSpecShemaCompletionContext_ReturnsCompletionItems(string inputWithCursors, int expectedEnd)
        {
            var completionContext = GetBicepCompletionContext(inputWithCursors, null, out DocumentUri documentUri);
            var moduleReferenceCompletionProvider = new ModuleReferenceCompletionProvider(azureContainerRegistryNamesProvider, BicepTestConstants.BuiltInOnlyConfigurationManager, modulesMetadataProvider, settingsProvider);
            var completions = await moduleReferenceCompletionProvider.GetFilteredCompletions(documentUri.ToUri(), completionContext);

            completions.Should().SatisfyRespectively(
                c =>
                {
                    c.Label.Should().Be("br:");
                    c.Kind.Should().Be(CompletionItemKind.Reference);
                    c.InsertTextFormat.Should().Be(InsertTextFormat.Snippet);
                    c.InsertText.Should().BeNull();
                    c.Detail.Should().Be("Bicep registry schema name");
                    c.TextEdit!.TextEdit!.NewText.Should().Be("'br:$0'");
                    c.TextEdit.TextEdit.Range.Start.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.Start.Character.Should().Be(12);
                    c.TextEdit.TextEdit.Range.End.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.End.Character.Should().Be(expectedEnd);
                },
                c =>
                {
                    c.Label.Should().Be("br/");
                    c.Kind.Should().Be(CompletionItemKind.Reference);
                    c.InsertTextFormat.Should().Be(InsertTextFormat.Snippet);
                    c.InsertText.Should().BeNull();
                    c.Detail.Should().Be("Bicep registry schema name");
                    c.TextEdit!.TextEdit!.NewText.Should().Be("'br/$0'");
                    c.TextEdit.TextEdit.Range.Start.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.Start.Character.Should().Be(12);
                    c.TextEdit.TextEdit.Range.End.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.End.Character.Should().Be(expectedEnd);
                },
                c =>
                {
                    c.Label.Should().Be("ts:");
                    c.Kind.Should().Be(CompletionItemKind.Reference);
                    c.InsertTextFormat.Should().Be(InsertTextFormat.Snippet);
                    c.InsertText.Should().BeNull();
                    c.Detail.Should().Be("Template spec schema name");
                    c.TextEdit!.TextEdit!.NewText.Should().Be("'ts:$0'");
                    c.TextEdit.TextEdit.Range.Start.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.Start.Character.Should().Be(12);
                    c.TextEdit.TextEdit.Range.End.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.End.Character.Should().Be(expectedEnd);
                },
                c =>
                {
                    c.Label.Should().Be("ts/");
                    c.Kind.Should().Be(CompletionItemKind.Reference);
                    c.InsertTextFormat.Should().Be(InsertTextFormat.Snippet);
                    c.InsertText.Should().BeNull();
                    c.Detail.Should().Be("Template spec schema name");
                    c.TextEdit!.TextEdit!.NewText.Should().Be("'ts/$0'");
                    c.TextEdit.TextEdit.Range.Start.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.Start.Character.Should().Be(12);
                    c.TextEdit.TextEdit.Range.End.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.End.Character.Should().Be(expectedEnd);
                });
        }

        [TestMethod]
        public async Task GetFilteredCompletions_WithInvalidTextInCompletionContext_ReturnsNull()
        {
            var completionContext = GetBicepCompletionContext("module test 'br:/|'", null, out DocumentUri documentUri);
            var moduleReferenceCompletionProvider = new ModuleReferenceCompletionProvider(azureContainerRegistryNamesProvider, BicepTestConstants.BuiltInOnlyConfigurationManager, modulesMetadataProvider, settingsProvider);
            var completions = await moduleReferenceCompletionProvider.GetFilteredCompletions(documentUri.ToUri(), completionContext);

            completions.Should().BeEmpty();
        }

        [TestMethod]
        public async Task GetFilteredCompletions_WithAliasCompletionContext_ReturnsCompletionItems()
        {
            var bicepConfigFileContents = @"{
  ""moduleAliases"": {
    ""br"": {
      ""test1"": {
        ""registry"": ""testacr.azurecr.io"",
        ""modulePath"": ""bicep/modules""
      },
      ""test2"": {
        ""registry"": ""testacr2.azurecr.io""
      }
    }
  }
}";
            var completionContext = GetBicepCompletionContext("module test 'br/|'", bicepConfigFileContents, out DocumentUri documentUri);
            var moduleReferenceCompletionProvider = new ModuleReferenceCompletionProvider(azureContainerRegistryNamesProvider, new ConfigurationManager(new IOFileSystem()), modulesMetadataProvider, settingsProvider);
            var completions = await moduleReferenceCompletionProvider.GetFilteredCompletions(documentUri.ToUri(), completionContext);

            completions.Should().SatisfyRespectively(
                c =>
                {
                    c.Label.Should().Be("public");
                    c.Kind.Should().Be(CompletionItemKind.Snippet);
                    c.InsertTextFormat.Should().Be(InsertTextFormat.Snippet);
                    c.InsertText.Should().BeNull();
                    c.Detail.Should().BeNull();
                    c.TextEdit!.TextEdit!.NewText.Should().Be("'br/public:$0'");
                    c.TextEdit.TextEdit.Range.Start.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.Start.Character.Should().Be(12);
                    c.TextEdit.TextEdit.Range.End.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.End.Character.Should().Be(17);
                },
                c =>
                {
                    c.Label.Should().Be("test1");
                    c.Kind.Should().Be(CompletionItemKind.Snippet);
                    c.InsertTextFormat.Should().Be(InsertTextFormat.Snippet);
                    c.InsertText.Should().BeNull();
                    c.Detail.Should().BeNull();
                    c.TextEdit!.TextEdit!.NewText.Should().Be("'br/test1:$0'");
                    c.TextEdit.TextEdit.Range.Start.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.Start.Character.Should().Be(12);
                    c.TextEdit.TextEdit.Range.End.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.End.Character.Should().Be(17);
                },
                c =>
                {
                    c.Label.Should().Be("test2");
                    c.Kind.Should().Be(CompletionItemKind.Snippet);
                    c.InsertTextFormat.Should().Be(InsertTextFormat.Snippet);
                    c.InsertText.Should().BeNull();
                    c.Detail.Should().BeNull();
                    c.TextEdit!.TextEdit!.NewText.Should().Be("'br/test2:$0'");
                    c.TextEdit.TextEdit.Range.Start.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.Start.Character.Should().Be(12);
                    c.TextEdit.TextEdit.Range.End.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.End.Character.Should().Be(17);
                });
        }

        [DataTestMethod]
        [DataRow("module test 'br:|'", 17)]
        [DataRow("module test 'br:|", 16)]
        public async Task GetFilteredCompletions_WithACRCompletionsSettingSetToFalse_ReturnsACRCompletionItemsUsingBicepConfig(
            string inputWithCursors,
            int endCharacter)
        {
            var bicepConfigFileContents = @"{
  ""moduleAliases"": {
    ""br"": {
      ""test1"": {
        ""registry"": ""testacr.azurecr.io"",
        ""modulePath"": ""bicep/modules""
      },
      ""test2"": {
        ""registry"": ""testacr2.azurecr.io""
      }
    }
  }
}";
            var completionContext = GetBicepCompletionContext(inputWithCursors, bicepConfigFileContents, out DocumentUri documentUri);

            var settingsProviderMock = StrictMock.Of<ISettingsProvider>();
            settingsProviderMock.Setup(x => x.GetSetting(LangServerConstants.IncludeAllAccessibleAzureContainerRegistriesForCompletionsSetting)).Returns(false);

            var moduleReferenceCompletionProvider = new ModuleReferenceCompletionProvider(azureContainerRegistryNamesProvider, new ConfigurationManager(new IOFileSystem()), modulesMetadataProvider, settingsProviderMock.Object);
            var completions = await moduleReferenceCompletionProvider.GetFilteredCompletions(documentUri.ToUri(), completionContext);

            completions.Should().SatisfyRespectively(
                c =>
                {
                    c.Label.Should().Be("mcr.microsoft.com/bicep/");
                    c.Kind.Should().Be(CompletionItemKind.Snippet);
                    c.InsertTextFormat.Should().Be(InsertTextFormat.Snippet);
                    c.InsertText.Should().BeNull();
                    c.Detail.Should().BeNull();
                    c.TextEdit!.TextEdit!.NewText.Should().Be("'br:mcr.microsoft.com/bicep/$0'");
                    c.TextEdit.TextEdit.Range.Start.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.Start.Character.Should().Be(12);
                    c.TextEdit.TextEdit.Range.End.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.End.Character.Should().Be(endCharacter);
                },
                c =>
                {
                    c.Label.Should().Be("testacr.azurecr.io");
                    c.Kind.Should().Be(CompletionItemKind.Snippet);
                    c.InsertTextFormat.Should().Be(InsertTextFormat.Snippet);
                    c.InsertText.Should().BeNull();
                    c.Detail.Should().BeNull();
                    c.TextEdit!.TextEdit!.NewText.Should().Be("br:testacr.azurecr.io/$0'");
                    c.TextEdit.TextEdit.Range.Start.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.Start.Character.Should().Be(12);
                    c.TextEdit.TextEdit.Range.End.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.End.Character.Should().Be(endCharacter);
                },
                c =>
                {
                    c.Label.Should().Be("testacr2.azurecr.io");
                    c.Kind.Should().Be(CompletionItemKind.Snippet);
                    c.InsertTextFormat.Should().Be(InsertTextFormat.Snippet);
                    c.InsertText.Should().BeNull();
                    c.Detail.Should().BeNull();
                    c.TextEdit!.TextEdit!.NewText.Should().Be("br:testacr2.azurecr.io/$0'");
                    c.TextEdit.TextEdit.Range.Start.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.Start.Character.Should().Be(12);
                    c.TextEdit.TextEdit.Range.End.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.End.Character.Should().Be(endCharacter);
                });
        }

        [DataTestMethod]
        [DataRow("module test 'br:|'", 17 )]
        [DataRow("module test 'br:|", 16)]
        public async Task GetFilteredCompletions_WithACRCompletionsSettingSetToTrue_ReturnsACRCompletionItemsUsingResourceGraphClient(
            string inputWithCursors,
            int endCharacter)
        {
            var (bicepFileContents, cursors) = ParserHelper.GetFileWithCursors(inputWithCursors, '|');

            var testOutputPath = FileHelper.GetUniqueTestOutputPath(TestContext);
            var bicepFilePath = FileHelper.SaveResultFile(TestContext, "input.bicep", bicepFileContents, testOutputPath);
            var documentUri = DocumentUri.FromFileSystemPath(bicepFilePath);
            var bicepCompilationManager = BicepCompilationManagerHelper.CreateCompilationManager(documentUri, bicepFileContents, true);
            var compilation = bicepCompilationManager.GetCompilation(documentUri)!.Compilation;
            var completionContext = BicepCompletionContext.Create(BicepTestConstants.Features, compilation, cursors[0]);

            var bicepConfigFileContents = @"{
  ""moduleAliases"": {
    ""br"": {
      ""test1"": {
        ""registry"": ""testacr1.azurecr.io"",
        ""modulePath"": ""bicep/modules""
      },
      ""test2"": {
        ""registry"": ""testacr2.azurecr.io""
      }
    }
  }
}";
            FileHelper.SaveResultFile(TestContext, "bicepconfig.json", bicepConfigFileContents, testOutputPath);

            var settingsProviderMock = StrictMock.Of<ISettingsProvider>();
            settingsProviderMock.Setup(x => x.GetSetting(LangServerConstants.IncludeAllAccessibleAzureContainerRegistriesForCompletionsSetting)).Returns(true);

            var azureContainerRegistryNamesProvider = StrictMock.Of<IAzureContainerRegistryNamesProvider>();
            azureContainerRegistryNamesProvider.Setup(x => x.GetRegistryNames(documentUri.ToUri())).ReturnsAsync(new List<string> { "testacr3.azurecr.io", "testacr4.azurecr.io" });

            var moduleReferenceCompletionProvider = new ModuleReferenceCompletionProvider(azureContainerRegistryNamesProvider.Object, new ConfigurationManager(new IOFileSystem()), modulesMetadataProvider, settingsProviderMock.Object);
            var completions = await moduleReferenceCompletionProvider.GetFilteredCompletions(documentUri.ToUri(), completionContext);

            completions.Should().SatisfyRespectively(
                c =>
                {
                    c.Label.Should().Be("mcr.microsoft.com/bicep/");
                    c.Kind.Should().Be(CompletionItemKind.Snippet);
                    c.InsertTextFormat.Should().Be(InsertTextFormat.Snippet);
                    c.InsertText.Should().BeNull();
                    c.Detail.Should().BeNull();
                    c.TextEdit!.TextEdit!.NewText.Should().Be("'br:mcr.microsoft.com/bicep/$0'");
                    c.TextEdit.TextEdit.Range.Start.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.Start.Character.Should().Be(12);
                    c.TextEdit.TextEdit.Range.End.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.End.Character.Should().Be(endCharacter);
                },
                c =>
                {
                    c.Label.Should().Be("testacr3.azurecr.io");
                    c.Kind.Should().Be(CompletionItemKind.Snippet);
                    c.InsertTextFormat.Should().Be(InsertTextFormat.Snippet);
                    c.InsertText.Should().BeNull();
                    c.Detail.Should().BeNull();
                    c.TextEdit!.TextEdit!.NewText.Should().Be("br:testacr3.azurecr.io/$0'");
                    c.TextEdit.TextEdit.Range.Start.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.Start.Character.Should().Be(12);
                    c.TextEdit.TextEdit.Range.End.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.End.Character.Should().Be(endCharacter);
                },
                c =>
                {
                    c.Label.Should().Be("testacr4.azurecr.io");
                    c.Kind.Should().Be(CompletionItemKind.Snippet);
                    c.InsertTextFormat.Should().Be(InsertTextFormat.Snippet);
                    c.InsertText.Should().BeNull();
                    c.Detail.Should().BeNull();
                    c.TextEdit!.TextEdit!.NewText.Should().Be("br:testacr4.azurecr.io/$0'");
                    c.TextEdit.TextEdit.Range.Start.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.Start.Character.Should().Be(12);
                    c.TextEdit.TextEdit.Range.End.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.End.Character.Should().Be(endCharacter);
                });
        }

        [DataTestMethod]
        [DataRow("module test 'br:|'", 17)]
        [DataRow("module test 'br:|", 16)]
        public async Task GetFilteredCompletions_WithACRCompletionsSettingSetToTrue_AndNoAccessibleRegistries_ReturnsNoACRCompletions(
            string inputWithCursors,
            int endCharacter)
        {
            var bicepConfigFileContents = @"{
  ""moduleAliases"": {
    ""br"": {
      ""test1"": {
        ""registry"": ""testacr1.azurecr.io"",
        ""modulePath"": ""bicep/modules""
      },
      ""test2"": {
        ""registry"": ""testacr2.azurecr.io""
      }
    }
  }
}";
            var completionContext = GetBicepCompletionContext(inputWithCursors, bicepConfigFileContents, out DocumentUri documentUri);

            var settingsProviderMock = StrictMock.Of<ISettingsProvider>();
            settingsProviderMock.Setup(x => x.GetSetting(LangServerConstants.IncludeAllAccessibleAzureContainerRegistriesForCompletionsSetting)).Returns(true);

            var azureContainerRegistryNamesProvider = StrictMock.Of<IAzureContainerRegistryNamesProvider>();
            azureContainerRegistryNamesProvider.Setup(x => x.GetRegistryNames(documentUri.ToUri())).ReturnsAsync(new List<string>());

            var moduleReferenceCompletionProvider = new ModuleReferenceCompletionProvider(azureContainerRegistryNamesProvider.Object, new ConfigurationManager(new IOFileSystem()), modulesMetadataProvider, settingsProviderMock.Object);
            var completions = await moduleReferenceCompletionProvider.GetFilteredCompletions(documentUri.ToUri(), completionContext);

            completions.Should().SatisfyRespectively(
                c =>
                {
                    c.Label.Should().Be("mcr.microsoft.com/bicep/");
                    c.Kind.Should().Be(CompletionItemKind.Snippet);
                    c.InsertTextFormat.Should().Be(InsertTextFormat.Snippet);
                    c.InsertText.Should().BeNull();
                    c.Detail.Should().BeNull();
                    c.TextEdit!.TextEdit!.NewText.Should().Be("'br:mcr.microsoft.com/bicep/$0'");
                    c.TextEdit.TextEdit.Range.Start.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.Start.Character.Should().Be(12);
                    c.TextEdit.TextEdit.Range.End.Line.Should().Be(0);
                    c.TextEdit.TextEdit.Range.End.Character.Should().Be(endCharacter);
                });
        }

        [DataTestMethod]
        [DataRow("module test 'br:mcr.microsoft.com/bicep/|'", "app/dapr-containerapp", "'br:mcr.microsoft.com/bicep/app/dapr-containerapp:$0'", 0, 12, 0, 41)]
        [DataRow("module test 'br:mcr.microsoft.com/bicep/|", "app/dapr-containerapp", "'br:mcr.microsoft.com/bicep/app/dapr-containerapp:$0'", 0, 12, 0, 40)]
        [DataRow("module test 'br/public:|'", "app/dapr-containerapp", "'br/public:app/dapr-containerapp:$0'", 0, 12, 0, 24)]
        [DataRow("module test 'br/public:|", "app/dapr-containerapp", "'br/public:app/dapr-containerapp:$0'", 0, 12, 0, 23)]

        public async Task GetFilteredCompletions_WithPublicMcrModuleRegistryCompletionContext_ReturnsCompletionItems(
            string inputWithCursors,
            string expectedLabel,
            string expectedCompletionText,
            int startLine,
            int startCharacter,
            int endLine,
            int endCharacter)
        {
            var completionContext = GetBicepCompletionContext(inputWithCursors, null, out DocumentUri documentUri);
            var moduleReferenceCompletionProvider = new ModuleReferenceCompletionProvider(azureContainerRegistryNamesProvider, BicepTestConstants.BuiltInOnlyConfigurationManager, modulesMetadataProvider, settingsProvider);
            var completions = await moduleReferenceCompletionProvider.GetFilteredCompletions(documentUri.ToUri(), completionContext);

            completions.Should().Contain(
                x => x.Label == expectedLabel &&
                x.Kind == CompletionItemKind.Snippet &&
                x.InsertText == null &&
                x.TextEdit!.TextEdit!.NewText == expectedCompletionText &&
                x.TextEdit!.TextEdit!.Range.Start.Line == startLine &&
                x.TextEdit!.TextEdit!.Range.Start.Character == startCharacter &&
                x.TextEdit!.TextEdit!.Range.End.Line == endLine &&
                x.TextEdit!.TextEdit!.Range.End.Character == endCharacter);
        }

        [DataTestMethod]
        [DataRow("module test 'br:testacr1.azurecr.io/|'", "bicep/modules", "'br:testacr1.azurecr.io/bicep/modules:$0'", 0, 12, 0, 37)]
        [DataRow("module test 'br:testacr1.azurecr.io/|", "bicep/modules", "'br:testacr1.azurecr.io/bicep/modules:$0'", 0, 12, 0, 36)]
        public async Task GetFilteredCompletions_WithPathCompletionContext_ReturnsCompletionItems(
            string inputWithCursors,
            string expectedLabel,
            string expectedCompletionText,
            int startLine,
            int startCharacter,
            int endLine,
            int endCharacter)
        {
            var bicepConfigFileContents = @"{
  ""moduleAliases"": {
    ""br"": {
      ""test1"": {
        ""registry"": ""testacr1.azurecr.io"",
        ""modulePath"": ""bicep/modules""
      },
      ""test2"": {
        ""registry"": ""testacr2.azurecr.io""
      }
    }
  }
}";
            var completionContext = GetBicepCompletionContext(inputWithCursors, bicepConfigFileContents, out DocumentUri documentUri);
            var moduleReferenceCompletionProvider = new ModuleReferenceCompletionProvider(azureContainerRegistryNamesProvider, new ConfigurationManager(new IOFileSystem()), modulesMetadataProvider, settingsProvider);
            var completions = await moduleReferenceCompletionProvider.GetFilteredCompletions(documentUri.ToUri(), completionContext);

            completions.Should().Contain(
                x => x.Label == expectedLabel &&
                x.Kind == CompletionItemKind.Snippet &&
                x.InsertText == null &&
                x.TextEdit!.TextEdit!.NewText == expectedCompletionText &&
                x.TextEdit!.TextEdit!.Range.Start.Line == startLine &&
                x.TextEdit!.TextEdit!.Range.Start.Character == startCharacter &&
                x.TextEdit!.TextEdit!.Range.End.Line == endLine &&
                x.TextEdit!.TextEdit!.Range.End.Character == endCharacter);
        }

        [DataTestMethod]
        [DataRow("module test 'br/public:app/dapr-containerapp:|'", "1.0.1", "'br/public:app/dapr-containerapp:1.0.1'$0", 0, 12, 0, 46)]
        [DataRow("module test 'br/public:app/dapr-containerapp:|", "1.0.1", "'br/public:app/dapr-containerapp:1.0.1'$0", 0, 12, 0, 45)]
        [DataRow("module test 'br:mcr.microsoft.com/bicep/app/dapr-containerapp:|'", "1.0.1", "'br:mcr.microsoft.com/bicep/app/dapr-containerapp:1.0.1'$0", 0, 12, 0, 63)]
        [DataRow("module test 'br:mcr.microsoft.com/bicep/app/dapr-containerapp:|", "1.0.1", "'br:mcr.microsoft.com/bicep/app/dapr-containerapp:1.0.1'$0", 0, 12, 0, 62)]
        public async Task GetFilteredCompletions_WithMcrVersionCompletionContext_ReturnsCompletionItems(
            string inputWithCursors,
            string expectedLabel,
            string expectedCompletionText,
            int startLine,
            int startCharacter,
            int endLine,
            int endCharacter)
        {
            var completionContext = GetBicepCompletionContext(inputWithCursors, null, out DocumentUri documentUri);
            var moduleReferenceCompletionProvider = new ModuleReferenceCompletionProvider(azureContainerRegistryNamesProvider, BicepTestConstants.BuiltInOnlyConfigurationManager, modulesMetadataProvider, settingsProvider);
            var completions = await moduleReferenceCompletionProvider.GetFilteredCompletions(documentUri.ToUri(), completionContext);

            completions.Should().Contain(
                x => x.Label == expectedLabel &&
                x.Kind == CompletionItemKind.Snippet &&
                x.InsertText == null &&
                x.TextEdit!.TextEdit!.NewText == expectedCompletionText &&
                x.TextEdit!.TextEdit!.Range.Start.Line == startLine &&
                x.TextEdit!.TextEdit!.Range.Start.Character == startCharacter &&
                x.TextEdit!.TextEdit!.Range.End.Line == endLine &&
                x.TextEdit!.TextEdit!.Range.End.Character == endCharacter);
        }

        [DataTestMethod]
        [DataRow("module test 'br:testacr1.azurecr.io/|'", "bicep/modules", "'br:testacr1.azurecr.io/bicep/modules:$0'", 0, 12, 0, 37)]
        [DataRow("module test 'br:testacr1.azurecr.io/|", "bicep/modules", "'br:testacr1.azurecr.io/bicep/modules:$0'", 0, 12, 0, 36)]
        public async Task GetFilteredCompletions_WithPublicAliasOverridenInBicepConfigAndPathCompletionContext_ReturnsCompletionItems(
            string inputWithCursors,
            string expectedLabel,
            string expectedCompletionText,
            int startLine,
            int startCharacter,
            int endLine,
            int endCharacter)
        {
            var bicepConfigFileContents = @"{
  ""moduleAliases"": {
    ""br"": {
      ""public"": {
        ""registry"": ""testacr1.azurecr.io"",
        ""modulePath"": ""bicep/modules""
      },
      ""test2"": {
        ""registry"": ""testacr2.azurecr.io""
      }
    }
  }
}";
            var completionContext = GetBicepCompletionContext(inputWithCursors, bicepConfigFileContents, out DocumentUri documentUri);
            var moduleReferenceCompletionProvider = new ModuleReferenceCompletionProvider(azureContainerRegistryNamesProvider, new ConfigurationManager(new IOFileSystem()), modulesMetadataProvider, settingsProvider);
            var completions = await moduleReferenceCompletionProvider.GetFilteredCompletions(documentUri.ToUri(), completionContext);

            completions.Should().Contain(
                x => x.Label == expectedLabel &&
                x.Kind == CompletionItemKind.Snippet &&
                x.InsertText == null &&
                x.TextEdit!.TextEdit!.NewText == expectedCompletionText &&
                x.TextEdit!.TextEdit!.Range.Start.Line == startLine &&
                x.TextEdit!.TextEdit!.Range.Start.Character == startCharacter &&
                x.TextEdit!.TextEdit!.Range.End.Line == endLine &&
                x.TextEdit!.TextEdit!.Range.End.Character == endCharacter);
        }

//        [DataTestMethod]
//        [DataRow("module test 'br/test/|'", "app/dapr-containerapp", "'br/test/app/dapr-containerapp:$0'", 0, 12, 0, 22)]
//        [DataRow("module test 'br/test/|", "app/dapr-containerapp", "'br/test/app/dapr-containerapp:$0'", 0, 12, 0, 21)]
//        public async Task GetFilteredCompletions_WithAliasForMCRInBicepConfigAndModulePath_ReturnsCompletionItems(
//            string inputWithCursors,
//            string expectedLabel,
//            string expectedCompletionText,
//            int startLine,
//            int startCharacter,
//            int endLine,
//            int endCharacter)
//        {
//            var bicepConfigFileContents = @"{
//  ""moduleAliases"": {
//    ""br"": {
//      ""test"": {
//        ""registry"": ""mcr.microsoft.com""
//      },
//      ""test2"": {
//        ""registry"": ""testacr2.azurecr.io""
//      }
//    }
//  }
//}";
//            var completionContext = GetBicepCompletionContext(inputWithCursors, bicepConfigFileContents, out DocumentUri documentUri);
//            var moduleReferenceCompletionProvider = new ModuleReferenceCompletionProvider(azureContainerRegistryNamesProvider, new ConfigurationManager(new IOFileSystem()), modulesMetadataProvider, settingsProvider);
//            IEnumerable<CompletionItem> completions = await moduleReferenceCompletionProvider.GetFilteredCompletions(documentUri.ToUri(), completionContext);

//            CompletionItem actualCompletionItem = completions.First(x => x.Label == expectedLabel);
//            actualCompletionItem.Kind.Should().Be(CompletionItemKind.Snippet);
//            actualCompletionItem.InsertText.Should().BeNull();

//            var actualTextEdit = actualCompletionItem.TextEdit!.TextEdit;
//            actualTextEdit.Should().NotBeNull();
//            actualTextEdit!.NewText.Should().Be(expectedCompletionText);
//            actualTextEdit!.Range.Start.Line.Should().Be(startLine);
//            actualTextEdit!.Range.Start.Character.Should().Be(startCharacter);
//            actualTextEdit!.Range.End.Line.Should().Be(endLine);
//            actualTextEdit!.Range.End.Character.Should().Be(endCharacter);
//        }

        private BicepCompletionContext GetBicepCompletionContext(
            string inputWithCursors,
            string? bicepConfigFileContents,
            out DocumentUri documentUri)
        {
            var (bicepFileContents, cursors) = ParserHelper.GetFileWithCursors(inputWithCursors, '|');
            var testOutputPath = FileHelper.GetUniqueTestOutputPath(TestContext);
            var bicepFilePath = FileHelper.SaveResultFile(TestContext, "input.bicep", bicepFileContents, testOutputPath);
            documentUri = DocumentUri.FromFileSystemPath(bicepFilePath);
            var bicepCompilationManager = BicepCompilationManagerHelper.CreateCompilationManager(documentUri, bicepFileContents, true);
            var compilation = bicepCompilationManager.GetCompilation(documentUri)!.Compilation;

            if (bicepConfigFileContents is not null)
            {
                FileHelper.SaveResultFile(TestContext, "bicepconfig.json", bicepConfigFileContents, testOutputPath);
            }

            return BicepCompletionContext.Create(BicepTestConstants.Features, compilation, cursors[0]);
        }
    }
}
