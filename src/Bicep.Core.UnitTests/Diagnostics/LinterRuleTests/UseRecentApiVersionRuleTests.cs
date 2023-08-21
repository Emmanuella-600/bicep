// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Bicep.Core.Analyzers.Linter.Rules;
using Bicep.Core.Analyzers.Linter.ApiVersions;
using Bicep.Core.Configuration;
using Bicep.Core.Json;
using Bicep.Core.Parsing;
using Bicep.Core.TypeSystem;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Bicep.Core.CodeAction;
using Bicep.Core.UnitTests.Mock;
using Bicep.Core.Resources;

#pragma warning disable CA1825 // Avoid zero-length array allocations

namespace Bicep.Core.UnitTests.Diagnostics.LinterRuleTests
{
    [TestClass]
    public partial class UseRecentApiVersionRuleTests : LinterRuleTestsBase
    {
        public record DiagnosticAndFixes
        (
            string ExpectedDiagnosticMessage,
            string ExpectedFixTitle,
            string ExpectedSubstringInReplacedBicep
        );

        private static void CompileAndTestWithFakeDateAndTypes(string bicep, ResourceScope scope, string[] resourceTypes, string fakeToday, string[] expectedMessagesForCode, OnCompileErrors onCompileErrors = OnCompileErrors.IncludeErrors, int? maxAgeInDays = null, bool? preferStableVersions = null)
        {
            AssertLinterRuleDiagnostics(UseRecentApiVersionRule.Code,
                bicep,
                expectedMessagesForCode,
                new Options(
                    OnCompileErrors: onCompileErrors,
                    IncludePosition.LineNumber,
                    ConfigurationPatch: c => CreateConfigurationWithFakeToday(c, fakeToday, maxAgeInDays, preferStableVersions),
                    // Test with the linter thinking today's date is fakeToday and also fake resource types from FakeResourceTypes
                    // Note: The compiler does not know about these fake types, only the linter.
                    AzResourceTypeLoader: resourceTypes.Any() ? FakeResourceTypes.GetAzResourceTypeLoaderWithInjectedTypes(resourceTypes).Object : null));
        }

        private static void CompileAndTestFixWithFakeDateAndTypes(string bicep, ResourceScope scope, string[] resourceTypes, string fakeToday, DiagnosticAndFixes[] expectedDiagnostics, int? maxAgeInDays = null, bool? preferStableVersions = null)
        {
            AssertLinterRuleDiagnostics(
                UseRecentApiVersionRule.Code,
                bicep,
                (diags) =>
                {
                    // Diagnostic titles
                    diags.Select(d => d.Message).Should().BeEquivalentTo(expectedDiagnostics.Select(d => d.ExpectedDiagnosticMessage));

                    // Fixes
                    for (int i = 0; i < diags.Count(); ++i)
                    {
                        var actual = diags.Skip(i).First();
                        var expected = expectedDiagnostics[i];

                        var fixable = actual.Should().BeAssignableTo<IFixable>().Which;
                        fixable.Fixes.Should().HaveCount(1, "Expecting 1 fix");
                        var fix = fixable.Fixes.First();

                        fix.Kind.Should().Be(CodeFixKind.QuickFix);
                        fix.Title.Should().Be(expected.ExpectedFixTitle);

                        fix.Replacements.Should().HaveCount(1, "Expecting 1 replacement");
                        var replacement = fix.Replacements.First();
                        var replacementText = replacement.Text;
                        var newBicep = string.Concat(bicep.AsSpan(0, replacement.Span.Position), replacementText, bicep.AsSpan(replacement.Span.Position + replacement.Span.Length));

                        newBicep.Should().Contain(expected.ExpectedSubstringInReplacedBicep, "the suggested API version should be replaced in the bicep file");
                    }
                },
                new Options(
                    OnCompileErrors.IncludeErrors,
                    IncludePosition.LineNumber,
                    ConfigurationPatch: c => CreateConfigurationWithFakeToday(c, fakeToday, maxAgeInDays, preferStableVersions),
                    // Test with the linter thinking today's date is fakeToday and also fake resource types from FakeResourceTypes
                    // Note: The compiler does not know about these fake types, only the linter.
                    AzResourceTypeLoader: FakeResourceTypes.GetAzResourceTypeLoaderWithInjectedTypes(resourceTypes).Object));
        }

        private static RootConfiguration CreateConfigurationWithFakeToday(RootConfiguration original, string today, int? maxAgeInDays = null, bool? preferStableVersions = null)
        {
            return new RootConfiguration(
                original.Cloud,
                original.ModuleAliases,
                new AnalyzersConfiguration(
                    JsonElementFactory.CreateElement(@"
                    {
                        ""core"": {
                            ""enabled"": true,
                            ""rules"": {
                                ""use-recent-api-versions"": {
                                    ""level"": ""warning"",
                                    ""test-today"": ""<TESTING_TODAY_DATE>"",
                                    ""test-warn-not-found"": true
                                    <MAX_AGE_PROP>
                                    <PREFER_STABLE_VERSIONS>
                                }
                            }
                        }
                    }".Replace("<TESTING_TODAY_DATE>", today)
                    .Replace("<MAX_AGE_PROP>", maxAgeInDays.HasValue ? $", \"maxAgeInDays\": {maxAgeInDays}" : "")
                    .Replace("<PREFER_STABLE_VERSIONS>", preferStableVersions.HasValue ? $", \"preferStableVersions\": {(preferStableVersions.Value ? "true" : "false")}" : "")
                )),
                original.CacheRootDirectory,
                original.ExperimentalFeaturesEnabled,
                original.Formatting,
                null,
                null);
        }

        [TestClass]
        public class GetAcceptableApiVersionsTests
        {
            private static void TestGetAcceptableApiVersions(string fullyQualifiedResourceType, ResourceScope scope, string resourceTypes, string today, string[] expectedApiVersions, int maxAgeInDays = UseRecentApiVersionRule.DefaultMaxAgeInDays, bool? preferStableVersions = null)
            {
                var apiVersionProvider = new ApiVersionProvider(BicepTestConstants.Features, Enumerable.Empty<ResourceTypeReference>());
                apiVersionProvider.InjectTypeReferences(scope, FakeResourceTypes.GetFakeResourceTypeReferences(resourceTypes));
                var (_, allowedVersions) = UseRecentApiVersionRule.GetAcceptableApiVersions(apiVersionProvider, ApiVersionHelper.ParseDateFromApiVersion(today), maxAgeInDays, PreferStableVersions(preferStableVersions), scope, fullyQualifiedResourceType);
                var allowedVersionsStrings = allowedVersions.Select(v => v.Formatted).ToArray();
                allowedVersionsStrings.Should().BeEquivalentTo(expectedApiVersions, options => options.WithStrictOrdering());
            }

            private static bool PreferStableVersions(bool? preferStableVersions)
            {
                return preferStableVersions switch
                {
                    true => true,
                    false => false,
                    null => true, // defaults to true
                };
            }

            [TestMethod]
            public void CaseInsensitiveResourceType()
            {
                TestGetAcceptableApiVersions(
                    "Fake.KUSTO/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2418-09-07-preview
                    ",
                    "2421-07-07",
                    new string[]
                    {
                        "2418-09-07-preview",
                    });
            }

            [TestMethod]
            public void CaseInsensitiveApiSuffix()
            {
                TestGetAcceptableApiVersions(
                    "Fake.KUSTO/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2418-09-07-PREVIEW
                    ",
                    "2421-07-07",
                    new string[]
                    {
                        "2418-09-07-preview",
                    });
            }

            [TestMethod]
            public void ResourceTypeNotRecognized_ReturnNone()
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kisto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2421-01-01
                    ",
                    "2421-07-07",
                    new string[]
                    {
                    });
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void NoStable_ButOldPreview_PickOnlyMostRecentPreview(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2413-01-21-preview
                        Fake.Kusto/clusters@2413-05-15-beta
                        Fake.Kusto/clusters@2413-09-07-alpha
                    ",
                    "2421-07-07",
                    new string[]
                    {
                        "2413-09-07-alpha", // preferStableVersions makes no difference here
                    },
                    preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void NoStable_ButOldPreview_PickOnlyMostRecentPreview_MultiplePreviewWithSameDate(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2413-01-21-preview
                        Fake.Kusto/clusters@2413-05-15-beta
                        Fake.Kusto/clusters@2413-09-07-alpha
                        Fake.Kusto/clusters@2413-09-07-beta
                    ",
                    "2421-07-07",
                    new string[] // preferStableVersions makes no difference here
                    {
                        "2413-09-07-alpha",
                        "2413-09-07-beta",
                    },
                    preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void NoStable_ButNewPreview_PickAllNewPreview(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2419-07-21-preview
                        Fake.Kusto/clusters@2419-08-15-beta
                        Fake.Kusto/clusters@2419-09-07-alpha
                    ",
                    "2421-07-07",
                    new string[] // preferStableVersions makes no difference here
                    {
                        "2419-09-07-alpha",
                        "2419-08-15-beta",
                        "2419-07-21-preview",
                    },
                    preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void NoStable_ButNewPreview_PickNewPreview_MultiplePreviewHaveSameDate(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2419-07-21-preview
                        Fake.Kusto/clusters@2419-08-15-beta
                        Fake.Kusto/clusters@2419-09-07-alpha
                        Fake.Kusto/clusters@2419-07-21-beta
                        Fake.Kusto/clusters@2419-08-15-privatepreview
                        Fake.Kusto/clusters@2419-09-07-beta
                        Fake.Kusto/clusters@2419-09-07-privatepreview
                    ",
                    "2421-07-07",
                    new string[] // preferStableVersions makes no difference here
                    {
                        "2419-09-07-alpha",
                        "2419-09-07-beta",
                        "2419-09-07-privatepreview",
                        "2419-08-15-beta",
                        "2419-08-15-privatepreview",
                        "2419-07-21-beta",
                        "2419-07-21-preview",
                    },
                    preferStableVersions: preferStableVersions
                );
            }

            public void NoStable_ButOldAndNewPreview_PickNewPreview()
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2419-07-15-privatepreview
                        Fake.Kusto/clusters@2413-01-21-preview
                        Fake.Kusto/clusters@2414-05-15-beta
                        Fake.Kusto/clusters@2415-09-07-alpha
                        Fake.Kusto/clusters@2419-08-21-beta
                        Fake.Kusto/clusters@2419-09-07-beta
                    ",
                    "2421-07-07",
                    new string[]
                    {
                        "2419-09-07-beta",
                        "2419-08-21-beta",
                        "2419-07-15-privatepreview",
                    });
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void OldStable_AndNoPreview_PickOnlyMostRecentStable(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2419-01-21
                        Fake.Kusto/clusters@2419-05-15
                        Fake.Kusto/clusters@2419-09-07
                        Fake.Kusto/clusters@2419-11-09
                        Fake.Kusto/clusters@2420-02-15
                        Fake.Kusto/clusters@2420-06-14
                        Fake.Kusto/clusters@2420-09-18
                    ",
                    "2500-07-07",
                    new string[] // preferStableVersions makes no difference here
                    {
                        "2420-09-18",
                    },
                    preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void OldStable_AndOldPreview_AndNewestPreviewIsOlderThanNewestStable_PickOnlyNewestStable(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2413-01-21
                        Fake.Kusto/clusters@2413-05-15
                        Fake.Kusto/clusters@2413-06-15-preview
                        Fake.Kusto/clusters@2413-09-07
                    ",
                    "2421-07-07",
                    new string[] // preferStableVersions makes no difference here
                    {
                        "2413-09-07",
                    },
                    preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void OldStable_AndOldPreview_AndNewestPreviewIsSameAgeAsNewestStable(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2413-01-21
                        Fake.Kusto/clusters@2413-05-15
                        Fake.Kusto/clusters@2413-06-15-preview
                        Fake.Kusto/clusters@2413-06-15
                        Fake.Kusto/clusters@2413-06-15-beta
                    ",
                    "2421-07-07",
                    PreferStableVersions(preferStableVersions) ?
                        new string[]
                        {
                            // pick only the recent stable
                            "2413-06-15",
                        }
                        : new string[]
                        {
                            // pick only the most recent (there are three with the same age)
                            "2413-06-15",
                            "2413-06-15-beta",
                            "2413-06-15-preview",
                        },
                    preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void OldStable_AndOldPreview_AndNewestPreviewIsNewerThanNewestStable(bool? preferStableVersions) //asdfg
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2413-01-21
                        Fake.Kusto/clusters@2413-01-21-preview
                        Fake.Kusto/clusters@2413-05-15
                        Fake.Kusto/clusters@2413-06-15
                        Fake.Kusto/clusters@2413-09-07-preview
                        Fake.Kusto/clusters@2413-09-07-beta
                        Fake.Kusto/clusters@2413-09-08-beta
                    ",
                    "2421-07-07",
                    PreferStableVersions(preferStableVersions)
                        ? new string[] { "2413-06-15" } // pick just newest stable
                        : new string[] { "2413-09-08-beta" }, // all are old, so pick just newest (which happens to be preview)
                    preferStableVersions: preferStableVersions
                 );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void OldStable_AndNewPreview(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2413-01-21
                        Fake.Kusto/clusters@2413-05-15
                        Fake.Kusto/clusters@2413-06-15
                        Fake.Kusto/clusters@2419-09-07-preview
                        Fake.Kusto/clusters@2419-09-07-beta
                        Fake.Kusto/clusters@2419-09-08-beta
                    ",
                    "2421-07-07",
                    PreferStableVersions(preferStableVersions)
                        ? new string[]
                            {
                                // pick newest stable and new preview
                                "2419-09-08-beta",
                                "2419-09-07-beta",
                                "2419-09-07-preview",
                                "2413-06-15",
                            }
                        : new string[]
                            {
                                // pick only recent (which are all preview)
                                "2419-09-08-beta",
                                "2419-09-07-beta",
                                "2419-09-07-preview",
                            },
                     preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void OldStable_AndOldAndNewPreview(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2413-01-21
                        Fake.Kusto/clusters@2413-05-15
                        Fake.Kusto/clusters@2413-06-15-beta
                        Fake.Kusto/clusters@2419-09-07-preview
                        Fake.Kusto/clusters@2419-09-07-beta
                    ",
                    "2421-07-07",
                    PreferStableVersions(preferStableVersions)
                        ? new string[]
                            {
                                // pick newest stable and recent previews
                                "2419-09-07-beta",
                                "2419-09-07-preview",
                                "2413-05-15",
                            }
                        : new string[]
                            {
                                // pick only recent (which are all preview)
                                "2419-09-07-beta",
                                "2419-09-07-preview",
                            },
                    preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void NewStable_ButNoPreview_PickNewStable(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2419-07-21
                        Fake.Kusto/clusters@2419-08-15
                        Fake.Kusto/clusters@2420-09-18
                    ",
                    "2421-07-07",
                    new string[] // preferStableVersions makes no difference here
                    {
                        "2420-09-18",
                        "2419-08-15",
                        "2419-07-21",
                    },
                    preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void OnlyPickPreviewThatAreNewerThanNewestStable_NoPreviewAreNewer(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                   "Fake.Kusto/clusters",
                   ResourceScope.ResourceGroup,
                   @"
                        Fake.Kusto/clusters@2413-07-21
                        Fake.Kusto/clusters@2419-07-21
                        Fake.Kusto/clusters@2419-07-15-alpha
                        Fake.Kusto/clusters@2419-07-16-beta
                        Fake.Kusto/clusters@2420-09-18
                    ",
                   "2421-07-07",
                   PreferStableVersions(preferStableVersions) ?
                       new string[]
                       {
                            "2420-09-18",
                            "2419-07-21",
                       }
                       : new string[]
                       {
                           // Pick all recent
                            "2420-09-18",
                            "2419-07-21",
                            "2419-07-16-beta",
                            "2419-07-15-alpha",
                       },
                   preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            public void OnlyPickPreviewThatAreNewerThanNewestStable_OnePreviewIsOlder(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2419-07-21
                        Fake.Kusto/clusters@2419-07-15-alpha
                        Fake.Kusto/clusters@2420-09-18
                        Fake.Kusto/clusters@2421-07-16-beta
                    ",
                    "2421-07-07",
                    PreferStableVersions(preferStableVersions) ?
                        new string[]
                        {
                            "2421-07-16-beta",
                            "2420-09-18",
                            "2419-07-21",
                        }
                        : new string[]
                        {
                            // Pick all recent
                            "2421-07-16-beta",
                            "2420-09-18",
                            "2419-07-21",
                            "2419-07-15-alpha",
                        },
                   preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void OnlyPickPreviewThatAreNewerThanNewestStable_MultiplePreviewsAreNewer(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2413-07-21
                        Fake.Kusto/clusters@2419-07-21
                        Fake.Kusto/clusters@2419-07-15-alpha
                        Fake.Kusto/clusters@2420-09-18
                        Fake.Kusto/clusters@2421-07-16-beta
                        Fake.Kusto/clusters@2421-07-17-preview
                    ",
                    "2421-07-07",
                    PreferStableVersions(preferStableVersions) ?
                        new string[]
                        {
                            // Pick all recent stable, plus previews that are newer than the most recent stable
                            "2421-07-17-preview",
                            "2421-07-16-beta",
                            "2420-09-18",
                            "2419-07-21",
                        }
                        : new string[]
                        {
                            // Pick all recent
                            "2421-07-17-preview",
                            "2421-07-16-beta",
                            "2420-09-18",
                            "2419-07-21",
                            "2419-07-15-alpha",
                        },
                   preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void OnlyPickPreviewThatAreNewerThanNewestStable_MultiplePreviewsAreNewer_AllAreOld(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2415-07-21
                        Fake.Kusto/clusters@2415-07-15-alpha
                        Fake.Kusto/clusters@2416-09-18
                        Fake.Kusto/clusters@2417-07-16-beta
                        Fake.Kusto/clusters@2417-07-17-preview
                    ",
                    "2421-07-07",
                    new string[]
                    {
                        PreferStableVersions(preferStableVersions) ? "2416-09-18" : "2417-07-17-preview",
                    },
                   preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void OnlyPickPreviewThatAreNewerThanNewestStable_MultiplePreviewsAreNewer_AllStableAreOld(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2415-07-21
                        Fake.Kusto/clusters@2415-07-15-alpha
                        Fake.Kusto/clusters@2416-09-18
                        Fake.Kusto/clusters@2421-07-16-beta
                        Fake.Kusto/clusters@2421-07-17-preview
                    ",
                    "2421-07-07",
                    PreferStableVersions(preferStableVersions)
                        ? new string[]
                            {
                                // pick all recent, plus most recent stable
                                "2421-07-17-preview",
                                "2421-07-16-beta",
                                "2416-09-18",
                            }
                        : new string[]
                            {
                                // pick just all recent
                                "2421-07-17-preview",
                                "2421-07-16-beta",
                            },
                   preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void OnlyPickPreviewThatAreNewerThanNewestStable_AllAreNew_NoPreviewAreNewerThanStable(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2413-01-21-preview
                        Fake.Kusto/clusters@2419-07-11
                        Fake.Kusto/clusters@2419-07-15-alpha
                        Fake.Kusto/clusters@2419-07-16-beta
                        Fake.Kusto/clusters@2420-09-18
                    ",
                    "2421-07-07",
                    PreferStableVersions(preferStableVersions) ?
                        new string[]
                        {
                            // Pick just the recent stables
                            "2420-09-18",
                            "2419-07-11",
                        }
                        : new string[]
                        {
                            // Pick all recent
                            "2420-09-18",
                            "2419-07-16-beta",
                            "2419-07-15-alpha",
                            "2419-07-11",
                        },
                   preferStableVersions: preferStableVersions
                ); ;
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            public void OldAndNewStable_ButNoPreview_PickNewStable(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2419-01-21
                        Fake.Kusto/clusters@2419-05-15
                        Fake.Kusto/clusters@2419-09-07
                        Fake.Kusto/clusters@2421-01-01
                        Fake.Kusto/clusters@2425-01-01
                    ",
                    "2421-07-07",
                    new string[] // preferStableVersions makes no difference here
                    {
                        "2425-01-01",
                        "2421-01-01",
                        "2419-09-07",
                    },
                   preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            public void OldAndNewStable_AndOldPreview_PickNewStable(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2413-09-07-privatepreview
                        Fake.Kusto/clusters@2413-09-07-preview
                        Fake.Kusto/clusters@2414-01-21
                        Fake.Kusto/clusters@2414-05-15
                        Fake.Kusto/clusters@2414-09-07
                        Fake.Kusto/clusters@2415-11-09
                        Fake.Kusto/clusters@2415-02-15
                        Fake.Kusto/clusters@2420-06-14
                        Fake.Kusto/clusters@2420-09-18
                    ",
                    "2421-07-07",
                    new string[] // preferStableVersions makes no difference here
                    {
                        "2420-09-18",
                        "2420-06-14",
                    },
                   preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void OldAndNewStable_NewPreviewButOlderThanNewestStable(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2419-09-07-privatepreview
                        Fake.Kusto/clusters@2419-09-07-preview
                        Fake.Kusto/clusters@2414-01-21
                        Fake.Kusto/clusters@2414-05-15
                        Fake.Kusto/clusters@2414-09-07
                        Fake.Kusto/clusters@2415-11-09
                        Fake.Kusto/clusters@2415-02-15
                        Fake.Kusto/clusters@2420-06-14
                        Fake.Kusto/clusters@2420-09-18
                    ",
                    "2421-07-07",
                    PreferStableVersions(preferStableVersions) ?
                        new string[]
                            {
                                // pick new stable only
                                "2420-09-18",
                                "2420-06-14",
                            }
                        : new string[]
                            {
                                // allow all recent versions, stable or not
                                "2420-09-18",
                                "2420-06-14",
                                "2419-09-07-preview",
                                "2419-09-07-privatepreview",
                            },
                   preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void OldAndNewStable_AndOldAndNewPreview(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2413-09-07-privatepreview
                        Fake.Kusto/clusters@2421-09-07-privatepreview
                        Fake.Kusto/clusters@2419-09-07-preview
                        Fake.Kusto/clusters@2417-09-07-preview
                        Fake.Kusto/clusters@2414-01-21
                        Fake.Kusto/clusters@2414-05-15
                        Fake.Kusto/clusters@2414-09-07
                        Fake.Kusto/clusters@2415-11-09
                        Fake.Kusto/clusters@2415-02-15
                        Fake.Kusto/clusters@2420-06-14
                        Fake.Kusto/clusters@2420-09-18
                    ",
                    "2421-07-07",
                    PreferStableVersions(preferStableVersions) ?
                        new string[]
                        {
                            // pick recent stables, plus previews more recent than most recent stable
                            "2421-09-07-privatepreview",
                            "2420-09-18",
                            "2420-06-14",
                        }
                        : new string[]
                        {
                            // pick all recent, whether stable or not
                            "2421-09-07-privatepreview",
                            "2420-09-18",
                            "2420-06-14",
                            "2419-09-07-preview",
                        },
                   preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void OnlyPreviewVersionsAvailable_AcceptAllRecentPreviews(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2410-01-01-preview
                        Fake.Kusto/clusters@2410-01-02-preview
                        Fake.Kusto/clusters@2421-01-01-preview
                        Fake.Kusto/clusters@2421-01-02-preview
                        Fake.Kusto/clusters@2421-01-03-preview
                        Fake.Kusto/clusters@2421-03-01-preview
                        Fake.Kusto/clusters@2421-04-01-preview
                        Fake.Kusto/clusters@2421-04-02-preview
                    ",
                    "2421-07-07",
                    new string[] // preferStableVersions makes no difference here
                    {
                        "2421-04-02-preview",
                        "2421-04-01-preview",
                        "2421-03-01-preview",
                        "2421-01-03-preview",
                        "2421-01-02-preview",
                        "2421-01-01-preview",
                    },
                   preferStableVersions: preferStableVersions
                );
            }

            [DataTestMethod]
            [DataRow(true)]
            [DataRow(false)]
            [DataRow(null)]
            public void LotsOfPreviewVersions_AndOneRecentGA(bool? preferStableVersions)
            {
                TestGetAcceptableApiVersions(
                    "Fake.Kusto/clusters",
                    ResourceScope.ResourceGroup,
                    @"
                        Fake.Kusto/clusters@2410-01-01-preview
                        Fake.Kusto/clusters@2410-01-02-preview
                        Fake.Kusto/clusters@2421-01-01-preview
                        Fake.Kusto/clusters@2421-01-02-preview
                        Fake.Kusto/clusters@2421-01-03-preview
                        Fake.Kusto/clusters@2421-02-01
                        Fake.Kusto/clusters@2421-03-01-preview
                        Fake.Kusto/clusters@2421-04-01-beta
                        Fake.Kusto/clusters@2421-04-02-preview
                    ",
                    "2421-07-07",
                    PreferStableVersions(preferStableVersions)
                        ? new string[]
                            {
                                // pick recent stable, plus all previews more recent than the stable
                                "2421-04-02-preview",
                                "2421-04-01-beta",
                                "2421-03-01-preview",
                                "2421-02-01",
                            }
                        : new string[]
                            {
                                // Allow all recent versions, preview or not
                                "2421-04-02-preview",
                                "2421-04-01-beta",
                                "2421-03-01-preview",
                                "2421-02-01",
                                "2421-01-03-preview",
                                "2421-01-02-preview",
                                "2421-01-01-preview",
                            },
                   preferStableVersions: preferStableVersions
                );
            }
        }

        [TestClass]
        public class GetAcceptableApiVersionsInvariantsTests //asdfg
        {
            private static readonly ApiVersionProvider RealApiVersionProvider = new(BicepTestConstants.Features, FakeResourceTypes.GetFakeResourceTypeReferences(FakeResourceTypes.ResourceScopeTypes));
            private static readonly bool Exhaustive = false;

            public class TestData
            {
                public TestData(ResourceScope resourceScope, string fullyQualifiedResourceType, DateTime today, int maxAgeInDays, bool preferStableVersions)
                {
                    ResourceScope = resourceScope;
                    FullyQualifiedResourceType = fullyQualifiedResourceType;
                    MaxAgeInDays = maxAgeInDays;
                    Today = today;
                    PreferStableVersions = preferStableVersions;
                }

                public string FullyQualifiedResourceType { get; }
                public ResourceScope ResourceScope { get; }
                public int MaxAgeInDays { get; }
                public bool PreferStableVersions { get; }
                public DateTime Today { get; }

                public static string GetDisplayName(MethodInfo _, object[] data)
                {
                    var me = ((TestData)data[0]);
                    return $"{me.ResourceScope}:{me.FullyQualifiedResourceType} (max={me.MaxAgeInDays} days) (preferStable={me.PreferStableVersions})";
                }
            }

            private static IEnumerable<object[]> GetTestData()
            {
                const int maxAgeInDays = 2 * 365;
                const int maxResourcesToTest = 500; // test is slow

                foreach (ResourceScope scope in new ResourceScope[]
                    {
                        ResourceScope.ResourceGroup,
                        ResourceScope.Tenant,
                        ResourceScope.ManagementGroup,
                        ResourceScope.Subscription,
                    })
                {
                    foreach (string typeName in RealApiVersionProvider.GetResourceTypeNames(scope).Take(maxResourcesToTest))
                    {
                        var apiVersionDates = RealApiVersionProvider.GetApiVersions(scope, typeName).Select(v => v.Date);

                        var datesToTest = new List<DateTime>();
                        if (Exhaustive)

                        {
                            foreach (DateTime dt in apiVersionDates)
                            {
                                datesToTest.Add(dt.AddDays(-1));
                                datesToTest.Add(dt);
                                datesToTest.Add(dt.AddDays(1));
                            }
                        }

                        datesToTest.Add(apiVersionDates.Min().AddDays(-(maxAgeInDays - 1)));
                        if (Exhaustive)
                        {
                            datesToTest.Add(apiVersionDates.Min().AddDays(-maxAgeInDays));
                            datesToTest.Add(apiVersionDates.Min().AddDays(-(maxAgeInDays + 1)));
                        }

                        datesToTest.Add(apiVersionDates.Max().AddDays(maxAgeInDays - 1));
                        if (Exhaustive)
                        {
                            datesToTest.Add(apiVersionDates.Max().AddDays(maxAgeInDays));
                            datesToTest.Add(apiVersionDates.Max().AddDays(maxAgeInDays + 1));
                        }

                        datesToTest = datesToTest.Distinct().ToList();
                        foreach (var today in datesToTest)
                        {
                            yield return new object[] { new TestData(scope, typeName, today, maxAgeInDays, preferStableVersions: true) };
                            yield return new object[] { new TestData(scope, typeName, today, maxAgeInDays, preferStableVersions: false) };
                        }
                    }
                }
            }

            private static bool DateIsRecent(DateTime dt, DateTime today, int maxAllowedAgeDays)
            {
                return DateIsEqualOrMoreRecentThan(dt.AddDays(maxAllowedAgeDays), today);
            }

            private static bool DateIsOld(DateTime dt, DateTime today, int maxAllowedAgeDays)
            {
                return !DateIsRecent(dt, today, maxAllowedAgeDays);
            }

            private static bool DateIsMoreRecentThan(DateTime dt, DateTime other)
            {
                return DateTime.Compare(dt, other) > 0;
            }

            private static bool DateIsEqualOrMoreRecentThan(DateTime dt, DateTime other)
            {
                return DateTime.Compare(dt, other) >= 0;
            }

            private static bool DateIsOlderThan(DateTime dt, DateTime other)
            {
                return DateTime.Compare(dt, other) < 0;
            }

            private static bool DateIsEqualOrOlderThan(DateTime dt, DateTime other)
            {
                return DateTime.Compare(dt, other) < 0;
            }

            //asdfg
            [DataTestMethod]
            [DynamicData(nameof(GetTestData), DynamicDataSourceType.Method, DynamicDataDisplayNameDeclaringType = typeof(TestData), DynamicDataDisplayName = nameof(TestData.GetDisplayName))]
            public void Invariants(TestData data)
            {
                var (allVersions, allowedVersions) = UseRecentApiVersionRule.GetAcceptableApiVersions(RealApiVersionProvider, data.Today, data.MaxAgeInDays, data.PreferStableVersions, data.ResourceScope, data.FullyQualifiedResourceType);

                allVersions.Should().NotBeNull();
                allowedVersions.Should().NotBeNull();

                allVersions.Should().NotBeEmpty();
                allowedVersions.Should().NotBeEmpty();

                var stableVersions = allVersions.Where(v => v.IsStable).ToArray();
                var recentStableVersions = stableVersions.Where(v => DateIsRecent(v.Date, data.Today, data.MaxAgeInDays)).ToArray();

                var previewVersions = allVersions.Where(v => v.IsPreview).ToArray();
                var recentPreviewVersions = previewVersions.Where(v => DateIsRecent(v.Date, data.Today, data.MaxAgeInDays)).ToArray();

                // if there are any stable versions available...
                if (stableVersions.Any())
                {
                    // The most recent stable version is always allowed
                    allowedVersions.Count(v => v.IsStable).Should().BeGreaterThan(0, "the most recent stable version is always allowed");
                    var mostRecentStable = stableVersions.OrderBy(v => v.Date).Last();
                    allowedVersions.Should().Contain(mostRecentStable, "the most recent stable version is always allowed");

                    // All recent stable versions (< 2 years old by default) are allowed
                    if (recentStableVersions.Any())
                    {
                        allowedVersions.Should().Contain(recentStableVersions, "all stable versions < 2 years old are allowed");
                    }

                    foreach (var preview in previewVersions)
                    {
                        var previewDate = preview.Date;

                        //asdfg
                        if (data.PreferStableVersions)
                        {
                            // Any recent preview version (< 2 years old by default) is acceptable *if* it's more recent than the most recent stable version
                            if (DateIsRecent(preview.Date, data.Today, data.MaxAgeInDays))
                            {
                                if (DateIsMoreRecentThan(previewDate, mostRecentStable.Date))
                                {
                                    allowedVersions.Should().Contain(preview, "preferStableVersions=true: any preview version more recent than the most recent stable version and < 2 years old is allowed");
                                }
                            }
                            else
                            {
                                // Any recent preiew version (< 2 years old by default) is acceptable *if* it's more recent than the most recent stable version
                                if (DateIsRecent(preview.Date, data.Today, data.MaxAgeInDays))
                                {
                                    if (DateIsMoreRecentThan(previewDate, mostRecentStable.Date))
                                    {
                                        allowedVersions.Should().Contain(preview, "preferStableVersions=true: any preview version more recent than the most recent stable version and < 2 years old is allowed");
                                    }
                                }

                                // If a preview version has a more recent or equally recent stable version, it is not allowed
                                if (stableVersions.Any(v => DateIsEqualOrMoreRecentThan(v.Date, previewDate)))
                                {
                                    allowedVersions.Should().NotContain(preview, "if a preview version has a more recent or equally recent stable version, it is not allowed");
                                }
                            }
                        }
                        else
                        {
                            // No stable versions

                            // If no stable versions at all, the most recent preview version is allowed, no matter its age
                            var mostRecentPreview = previewVersions.OrderBy(v => v.Date).Last();
                            allowedVersions.Should().Contain(mostRecentPreview, "if no stable versions at all, the most recent preview version is allowed, no matter its age");
                        }

                        // COROLLARIES TO THE IMPLEMENTATION RULES

                        // If the most recent version is a preview version that’s > max age, only that one preview version is allowed
                        var mostRecent = allVersions.OrderBy(v => v.Date).Last();
                        if (mostRecent.IsPreview && DateIsOld(mostRecent.Date, data.Today, data.MaxAgeInDays))
                        {
                            allowedVersions.Where(v => v.IsPreview).Select(v => v.Formatted).Should().BeEquivalentTo(new string[] { mostRecent.Formatted }, "if the most recent version is a preview version that’s > 2 years old, that one preview version is allowed");
                        }

                        // If there are no stable versions, all recent preview versions are allowed
                        if (!stableVersions.Any())
                        {
                            allowedVersions.Should().BeEquivalentTo(previewVersions.Where(v => DateIsRecent(v.Date, data.Today, data.MaxAgeInDays)));
                        }
                    }
                }

                [TestClass]
                public class AnalyzeApiVersionTests
            {
                private static void Test(
                    DateTime currentVersionDate,
                    string currentVersionSuffix,
                    DateTime[] gaVersionDates,
                    DateTime[] previewVersionDates,
                    (string message, string acceptableVersions, string replacement)? expectedFix,
                    int maxAllowedAgeInDays = 730,
                    bool preferStableVersions = true)
                {
                    string currentVersion = ApiVersionHelper.Format(currentVersionDate) + currentVersionSuffix;
                    string[] gaVersions = gaVersionDates.Select(d => "Whoever.whatever/whichever@" + ApiVersionHelper.Format(d)).ToArray();
                    string[] previewVersions = previewVersionDates.Select(d => "Whoever.whatever/whichever@" + ApiVersionHelper.Format(d) + "-preview").ToArray();
                    var apiVersionProvider = new ApiVersionProvider(BicepTestConstants.Features, Enumerable.Empty<ResourceTypeReference>());
                    apiVersionProvider.InjectTypeReferences(ResourceScope.ResourceGroup, FakeResourceTypes.GetFakeResourceTypeReferences(gaVersions.Concat(previewVersions)));
                    var result = UseRecentApiVersionRule.AnalyzeApiVersion(
                        apiVersionProvider,
                        DateTime.Today,
                        maxAllowedAgeInDays,
                        preferStableVersions,
                        new TextSpan(17, 47),
                        new TextSpan(17, 47),
                        ResourceScope.ResourceGroup,
                        "Whoever.whatever/whichever",
                        new ApiVersion(currentVersion),
                        returnNotFoundDiagnostics: true);

                    if (expectedFix == null)
                    {
                        result.Should().BeNull();
                    }
                    else
                    {
                        result.Should().NotBeNull();
                        result!.Span.Should().Be(new TextSpan(17, 47));
                        result.Message.Should().Be(expectedFix.Value.message);
                        (string.Join(", ", result.AcceptableVersions.Select(v => v.Formatted))).Should().Be(expectedFix.Value.acceptableVersions);
                        result.Fixes.Should().HaveCount(1); // Right now we only create one fix
                        result.Fixes[0].Replacements.Should().SatisfyRespectively(r => r.Span.Should().Be(new TextSpan(17, 47)));
                        result.Fixes[0].Replacements.Select(r => r.Text).Should().BeEquivalentTo(new string[] { expectedFix.Value.replacement });
                    }
                }

                [TestMethod]
                public void WithCurrentVersionLessThanTwoYearsOld_ShouldNotAddDiagnostics()
                {
                    DateTime currentVersionDate = DateTime.Today.AddDays(-1 * 365);
                    DateTime recentGAVersionDate = DateTime.Today.AddDays(-5 * 31);

                    Test(currentVersionDate, "", new DateTime[] { currentVersionDate, recentGAVersionDate }, new DateTime[] { },
                        null);
                }

                [TestMethod]
                public void WithCurrentVersionMoreThanTwoYearsOldAndRecentApiVersionIsAvailable_ShouldAddDiagnostics()
                {
                    DateTime currentVersionDate = DateTime.Today.AddDays(-3 * 365);
                    string currentVersion = ApiVersionHelper.Format(currentVersionDate);
                    DateTime recentGAVersionDate = DateTime.Today.AddDays(-5 * 30);
                    string recentGAVersion = ApiVersionHelper.Format(recentGAVersionDate);

                    Test(currentVersionDate, "", new DateTime[] { currentVersionDate, recentGAVersionDate }, new DateTime[] { },
                        (
                            $"Use more recent API version for 'Whoever.whatever/whichever'. '{currentVersion}' is {3 * 365} days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available.",
                            acceptableVersions: recentGAVersion,
                            replacement: recentGAVersion
                        ),
                        preferStableVersions: false);
                }

                //asdfg
                [TestMethod]
                public void WithCurrentVersionMoreThanTwoYearsOldAndRecentApiVersionIsAvailable_NotPreferStable_ShouldAddDiagnostics()
                {
                    DateTime currentVersionDate = DateTime.Today.AddDays(-3 * 365);
                    string currentVersion = ApiVersionHelper.Format(currentVersionDate);
                    DateTime recentGAVersionDate = DateTime.Today.AddDays(-5 * 30);
                    string recentGAVersion = ApiVersionHelper.Format(recentGAVersionDate);

                    Test(currentVersionDate, "", new DateTime[] { currentVersionDate, recentGAVersionDate }, new DateTime[] { },
                        (
                            $"Use more recent API version for 'Whoever.whatever/whichever'. '{currentVersion}' is {3 * 365} days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available.",
                            acceptableVersions: recentGAVersion,
                            replacement: recentGAVersion
                        ));
                }

                [TestMethod]
                public void WithCurrentAndRecentApiVersionsMoreThanTwoYearsOld_ShouldAddDiagnosticsToUseRecentApiVersion()
                {
                    DateTime currentVersionDate = DateTime.Today.AddDays(-4 * 365);
                    string currentVersion = ApiVersionHelper.Format(currentVersionDate);

                    DateTime recentGAVersionDate = DateTime.Today.AddDays(-3 * 365);
                    string recentGAVersion = ApiVersionHelper.Format(recentGAVersionDate);

                    Test(currentVersionDate, "", new DateTime[] { currentVersionDate, recentGAVersionDate }, new DateTime[] { },
                         (
                            $"Use more recent API version for 'Whoever.whatever/whichever'. '{currentVersion}' is {4 * 365} days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available.",
                            acceptableVersions: recentGAVersion,
                            replacement: recentGAVersion
                         ));
                }

                [TestMethod]
                public void WithCurrentAndRecentApiVersionsTooOld_CustomMaxAge_ShouldAddDiagnosticsToUseRecentApiVersion()
                {
                    DateTime currentVersionDate = DateTime.Today.AddDays(-4 * 365);
                    string currentVersion = ApiVersionHelper.Format(currentVersionDate);

                    DateTime recentGAVersionDate = DateTime.Today.AddDays(-3 * 365);
                    string recentGAVersion = ApiVersionHelper.Format(recentGAVersionDate);

                    Test(currentVersionDate, "", new DateTime[] { currentVersionDate, recentGAVersionDate }, new DateTime[] { },
                         (
                            $"Use more recent API version for 'Whoever.whatever/whichever'. '{currentVersion}' is {4 * 365} days old, should be no more than 20 days old, or the most recent. Must use a non-preview version if available.",
                            acceptableVersions: recentGAVersion,
                            replacement: recentGAVersion
                         ),
                         20);
                }

                [TestMethod]
                public void CustomMaxAge_Zero()
                {
                    DateTime currentVersionDate = DateTime.Today.AddDays(-4 * 365);
                    string currentVersion = ApiVersionHelper.Format(currentVersionDate);

                    DateTime recentGAVersionDate = DateTime.Today.AddDays(-3 * 365);
                    string recentGAVersion = ApiVersionHelper.Format(recentGAVersionDate);

                    Test(currentVersionDate, "", new DateTime[] { currentVersionDate, recentGAVersionDate }, new DateTime[] { },
                         (
                            $"Use more recent API version for 'Whoever.whatever/whichever'. '{currentVersion}' is {4 * 365} days old, should be no more than 0 days old, or the most recent. Must use a non-preview version if available.",
                            acceptableVersions: recentGAVersion,
                            replacement: recentGAVersion
                         ),
                         0);
                }

                [TestMethod]
                public void WhenCurrentAndRecentApiVersionsAreSameAndMoreThanTwoYearsOld_ShouldNotAddDiagnostics()
                {
                    DateTime currentVersionDate = DateTime.Today.AddDays(-3 * 365);

                    Test(currentVersionDate, "", new DateTime[] { currentVersionDate }, new DateTime[] { },
                        null);
                }

                [TestMethod]
                public void WithPreviewVersion_WhenCurrentPreviewVersionIsLatest_ShouldNotAddDiagnostics()
                {
                    DateTime currentVersionDate = DateTime.Today.AddDays(-365);

                    DateTime recentGAVersionDate = DateTime.Today.AddDays(-3 * 365);

                    DateTime recentPreviewVersionDate = currentVersionDate;


                    Test(currentVersionDate, "", new DateTime[] { currentVersionDate, recentGAVersionDate }, new DateTime[] { recentPreviewVersionDate },
                         null);
                }

                [TestMethod]
                public void WithOldPreviewVersion_WhenRecentPreviewVersionIsAvailable_ButIsOlderThanGAVersion_ShouldAddDiagnosticsAboutBeingOld()
                {
                    DateTime currentVersionDate = DateTime.Today.AddDays(-5 * 365);
                    string currentVersion = ApiVersionHelper.Format(currentVersionDate, "-preview");

                    DateTime recentGAVersionDate = DateTime.Today.AddDays(-1 * 365);
                    string recentGAVersion = ApiVersionHelper.Format(recentGAVersionDate);

                    DateTime recentPreviewVersionDate = DateTime.Today.AddDays(-2 * 365);

                    Test(currentVersionDate, "-preview", new DateTime[] { recentGAVersionDate }, new DateTime[] { currentVersionDate, recentPreviewVersionDate },
                        (
                           $"Use more recent API version for 'Whoever.whatever/whichever'. '{currentVersion}' is {5 * 365} days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available.",
                           acceptableVersions: recentGAVersion,
                           replacement: recentGAVersion
                        ));
                }

                [TestMethod]
                public void WithPreviewVersion_WhenRecentPreviewVersionIsAvailable_AndIfNewerGAVersion_ShouldAddDiagnosticsToUseGA()
                {
                    DateTime currentVersionDate = DateTime.Today.AddDays(-5 * 365);
                    string currentVersion = ApiVersionHelper.Format(currentVersionDate, "-preview");

                    DateTime recentGAVersionDate = DateTime.Today.AddDays(-3 * 365);
                    string recentGAVersion = ApiVersionHelper.Format(recentGAVersionDate);

                    DateTime recentPreviewVersionDate = DateTime.Today.AddDays(-2 * 365);
                    string recentPreviewVersion = ApiVersionHelper.Format(recentPreviewVersionDate, "-preview");

                    Test(currentVersionDate, "-preview", new DateTime[] { recentGAVersionDate }, new DateTime[] { currentVersionDate, recentPreviewVersionDate },
                        (

                           $"Use more recent API version for 'Whoever.whatever/whichever'. '{currentVersion}' is {5 * 365} days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available.",
                           acceptableVersions: $"{recentPreviewVersion}, {recentGAVersion}",
                           replacement: recentPreviewVersion // TODO recommend most recent, or just most recent GA version? Right now we always suggest the most recent
                        ));
                }

                [TestMethod]
                public void WithOldPreviewVersion_WhenRecentGAVersionIsAvailable_ShouldAddDiagnostics()
                {
                    DateTime currentVersionDate = DateTime.Today.AddDays(-5 * 365);
                    string currentVersion = ApiVersionHelper.Format(currentVersionDate);

                    DateTime recentGAVersionDate = DateTime.Today.AddDays(-2 * 365);
                    string recentGAVersion = ApiVersionHelper.Format(recentGAVersionDate);

                    DateTime recentPreviewVersionDate = DateTime.Today.AddDays(-3 * 365);

                    Test(currentVersionDate, "-preview", new DateTime[] { recentGAVersionDate }, new DateTime[] { currentVersionDate, recentPreviewVersionDate },
                      (
                         $"Use more recent API version for 'Whoever.whatever/whichever'. '{currentVersion}-preview' is {5 * 365} days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available.",
                         acceptableVersions: recentGAVersion,
                         replacement: recentGAVersion
                      ));
                }

                [TestMethod]
                public void WithRecentPreviewVersion_WhenRecentGAVersionIsAvailable_ShouldAddDiagnostics()
                {
                    DateTime currentVersionDate = DateTime.Today.AddDays(-2 * 365);
                    string currentVersion = ApiVersionHelper.Format(currentVersionDate);

                    DateTime recentGAVersionDate = DateTime.Today.AddDays(-1 * 365);
                    string recentGAVersion = ApiVersionHelper.Format(recentGAVersionDate);

                    Test(currentVersionDate, "-preview", new DateTime[] { recentGAVersionDate }, new DateTime[] { currentVersionDate },
                      (
                         $"Use more recent API version for 'Whoever.whatever/whichever'. '{currentVersion}-preview' is a preview version and there is a more recent non-preview version available. Must use a non-preview version if available.",
                         acceptableVersions: recentGAVersion,
                         replacement: recentGAVersion
                      ));
                }

                [TestMethod]
                public void WithRecentPreviewVersion_WhenRecentGAVersionIsSameAsPreviewVersion_ShouldAddDiagnosticsUsingGAVersion()
                {
                    DateTime currentVersionDate = DateTime.Today.AddDays(-2 * 365);
                    string currentVersion = ApiVersionHelper.Format(currentVersionDate, "-preview");

                    DateTime recentGAVersionDate = currentVersionDate;
                    string recentGAVersion = ApiVersionHelper.Format(recentGAVersionDate);

                    Test(currentVersionDate, "-preview", new DateTime[] { recentGAVersionDate }, new DateTime[] { currentVersionDate },
                     (
                        $"Use more recent API version for 'Whoever.whatever/whichever'. '{currentVersion}' is a preview version and there is a non-preview version available with the same date. Must use a non-preview version if available.",
                        acceptableVersions: recentGAVersion,
                        replacement: recentGAVersion
                     ));
                }

                [TestMethod]
                public void WithPreviewVersion_WhenGAVersionisNull_AndCurrentVersionIsNotRecent_ShouldAddDiagnosticsUsingRecentPreviewVersion()
                {
                    DateTime currentVersionDate = DateTime.Today.AddDays(-3 * 365);
                    string currentVersion = ApiVersionHelper.Format(currentVersionDate, "-preview");

                    DateTime recentPreviewVersionDate = DateTime.Today.AddDays(-2 * 365);
                    string recentPreviewVersion = ApiVersionHelper.Format(recentPreviewVersionDate, "-preview");

                    Test(currentVersionDate, "-preview", new DateTime[] { }, new DateTime[] { recentPreviewVersionDate, currentVersionDate },
                        (
                           $"Use more recent API version for 'Whoever.whatever/whichever'. '{currentVersion}' is {3 * 365} days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available.",
                           acceptableVersions: recentPreviewVersion,
                          replacement: recentPreviewVersion
                        ));
                }

                [TestMethod]
                public void WithPreviewVersion_WhenGAVersionisNull_AndCurrentVersionIsRecent_ShouldNotAddDiagnostics()
                {
                    DateTime currentVersionDate = DateTime.Today.AddDays(-2 * 365);

                    DateTime recentPreviewVersionDate = currentVersionDate;

                    Test(currentVersionDate, "-preview", new DateTime[] { }, new DateTime[] { recentPreviewVersionDate, currentVersionDate },
                        null);
                }
            }

            [TestClass]
            public class CompilationTests
            {
                [TestMethod]
                public void ArmTtk_ApiVersionIsNotAnExpression_Error()
                {
                    string bicep = @"
                    resource publicIPAddress1 'fake.Network/publicIPAddresses@[concat(\'2020\', \'01-01\')]' = {
                      name: 'publicIPAddress1'
                      #disable-next-line no-loc-expr-outside-params
                      location: resourceGroup().location
                      tags: {
                        displayName: 'publicIPAddress1'
                      }
                      properties: {
                        publicIPAllocationMethod: 'Dynamic'
                      }
                    }";
                    CompileAndTestWithFakeDateAndTypes(bicep,
                        ResourceScope.ResourceGroup,
                        FakeResourceTypes.ResourceScopeTypes,
                        "2422-07-04",
                        new string[] { "[2] The resource type is not valid. Specify a valid resource type of format \"<types>@<apiVersion>\"." });
                }

                [TestMethod]
                public void NestedResources1_Fail()
                {
                    string bicep = @"
                    param location string

                    resource namespace1 'fake.ServiceBus/namespaces@2418-01-01-preview' = {
                      name: 'namespace1'
                      location: location
                      properties: {
                      }
                    }

                    // Using 'parent'
                    resource namespace1_queue1 'fake.ServiceBus/namespaces/queues@2417-04-01' = {  // this is the latest stable version
                      parent: namespace1
                      name: 'queue1'
                    }

                    // Using 'parent'
                    resource namespace1_queue1_rule1 'fake.ServiceBus/namespaces/queues/authorizationRules@2415-08-01' = {
                      parent: namespace1_queue1
                      name: 'rule1'
                    }

                    // Using nested name
                    resource namespace1_queue2 'fake.ServiceBus/namespaces/queues@2417-04-01' = { // this is the latest stable version
                      name: 'namespace1/queue1'
                    }

                    // Using 'parent'
                    resource namespace1_queue2_rule2 'fake.ServiceBus/namespaces/queues/authorizationRules@2418-01-01-preview' = {
                      parent: namespace1_queue2
                      name: 'rule2'
                    }

                    // Using nested name
                    resource namespace1_queue2_rule3 'fake.ServiceBus/namespaces/queues/authorizationRules@4017-04-01' = {
                      name: 'namespace1/queue2/rule3'
                    }";

                    CompileAndTestWithFakeDateAndTypes(
                        bicep,
                        ResourceScope.ResourceGroup,
                        FakeResourceTypes.ResourceScopeTypes,
                        "2422-07-04",
                        new string[] {
                        "[4] Use more recent API version for 'fake.ServiceBus/namespaces'. '2418-01-01-preview' is 1645 days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2421-06-01-preview, 2421-01-01-preview, 2417-04-01",
                        "[18] Use more recent API version for 'fake.ServiceBus/namespaces/queues/authorizationRules'. '2415-08-01' is 2529 days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2421-06-01-preview, 2421-01-01-preview, 2417-04-01",
                        "[29] Use more recent API version for 'fake.ServiceBus/namespaces/queues/authorizationRules'. '2418-01-01-preview' is 1645 days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2421-06-01-preview, 2421-01-01-preview, 2417-04-01",
                        "[35] Could not find apiVersion 4017-04-01 for fake.ServiceBus/namespaces/queues/authorizationRules. Must use a non-preview version if available. Acceptable  versions: 2421-06-01-preview, 2421-01-01-preview, 2417-04-01",
                        });
                }

                [TestMethod]
                public void NestedResources2_Fail()
                {
                    string bicep = @"
                        param location string

                        // Using resource nesting
                        resource namespace2 'fake.ServiceBus/namespaces@2418-01-01-preview' = {
                          name: 'namespace2'
                          location: location

                          resource queue1 'queues@2415-08-01' = {
                            name: 'queue1'
                            location: location

                            resource rule1 'authorizationRules@2418-01-01-preview' = {
                              name: 'rule1'
                            }
                          }
                        }

                        // Using nested name (parent is a nested resource)
                        resource namespace2_queue1_rule2 'fake.ServiceBus/namespaces/queues/authorizationRules@2415-08-01' = {
                          name: 'namespace2/queue1/rule2'
                        }

                        // Using parent (parent is a nested resource)
                        resource namespace2_queue1_rule3 'fake.ServiceBus/namespaces/queues/authorizationRules@2415-08-01' = {
                          parent: namespace2::queue1
                          name: 'rule3'
                        }";

                    CompileAndTestWithFakeDateAndTypes(
                        bicep,
                        ResourceScope.ResourceGroup,
                        FakeResourceTypes.ResourceScopeTypes,
                        "2422-07-04",
                        new[] {
                        "[5] Use more recent API version for 'fake.ServiceBus/namespaces'. '2418-01-01-preview' is 1645 days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2421-06-01-preview, 2421-01-01-preview, 2417-04-01",
                        "[9] Use more recent API version for 'fake.ServiceBus/namespaces/queues'. '2415-08-01' is 2529 days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2421-06-01-preview, 2421-01-01-preview, 2417-04-01",
                        "[13] Use more recent API version for 'fake.ServiceBus/namespaces/queues/authorizationRules'. '2418-01-01-preview' is 1645 days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2421-06-01-preview, 2421-01-01-preview, 2417-04-01",
                        "[20] Use more recent API version for 'fake.ServiceBus/namespaces/queues/authorizationRules'. '2415-08-01' is 2529 days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2421-06-01-preview, 2421-01-01-preview, 2417-04-01",
                        "[25] Use more recent API version for 'fake.ServiceBus/namespaces/queues/authorizationRules'. '2415-08-01' is 2529 days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2421-06-01-preview, 2421-01-01-preview, 2417-04-01",
                        });
                }

                [TestMethod]
                public void ArmTtk_NotAString_Error()
                {
                    string bicep = @"
                    resource publicIPAddress1 'fake.Network/publicIPAddresses@True' = {
                    name: 'publicIPAddress1'
                    #disable-next-line no-loc-expr-outside-params no-hardcoded-location
                    location: 'westus'
                    tags: {
                        displayName: 'publicIPAddress1'
                    }
                    properties: {
                        publicIPAllocationMethod: 'Dynamic'
                    }
                }
                ";

                    CompileAndTestWithFakeDateAndTypes(
                        bicep,
                        ResourceScope.ResourceGroup,
                        FakeResourceTypes.ResourceScopeTypes,
                        "2422-07-04",
                        new string[] {
                      "[2] The resource type is not valid. Specify a valid resource type of format \"<types>@<apiVersion>\"."
                        });
                }

                [TestMethod]
                public void ArmTtk_PreviewWhenNonPreviewIsAvailable_WithSameDateAsStable_Fail()
                {
                    string bicep = @"
                        resource db 'fake.DBforMySQL/servers@2417-12-01-preview' = {
                          name: 'db]'
                        #disable-next-line no-hardcoded-location
                          location: 'westeurope'
                          properties: {
                            administratorLogin: 'sa'
                            administratorLoginPassword: 'don\'t put passwords in plain text'
                            createMode: 'Default'
                            sslEnforcement: 'Disabled'
                          }
                        }
                    ";

                    CompileAndTestWithFakeDateAndTypes(
                        bicep,
                        ResourceScope.ResourceGroup,
                        new string[]
                        {
                           "Fake.DBforMySQL/servers@2417-12-01",
                           "Fake.DBforMySQL/servers@2417-12-01-preview",
                        },
                        fakeToday: "2422-07-04",
                        new[] {
                        "[2] Use more recent API version for 'fake.DBforMySQL/servers'. '2417-12-01-preview' is 1676 days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2417-12-01",
                        });
                }

                [TestMethod]
                public void ArmTtk_PreviewWhenNonPreviewIsAvailable_WithLaterDateThanStable_Fail()
                {
                    string bicep = @"
                        resource db 'fake.DBforMySQL/servers@2417-12-01-preview' = {
                          name: 'db]'
                        #disable-next-line no-hardcoded-location
                          location: 'westeurope'
                          properties: {
                            administratorLogin: 'sa'
                            administratorLoginPassword: 'don\'t put passwords in plain text'
                            createMode: 'Default'
                            sslEnforcement: 'Disabled'
                          }
                        }
                    ";

                    CompileAndTestWithFakeDateAndTypes(
                        bicep,
                        ResourceScope.ResourceGroup,
                        new string[]
                        {
                           "Fake.DBforMySQL/servers@2417-12-02",
                           "Fake.DBforMySQL/servers@2417-12-01-preview",
                        },
                        fakeToday: "2422-07-04",
                        new[] {
                        "[2] Use more recent API version for 'fake.DBforMySQL/servers'. '2417-12-01-preview' is 1676 days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2417-12-02",
                        });
                }

                [TestMethod]
                public void ArmTtk_PreviewWhenNonPreviewIsAvailable_WithEarlierDateThanStable_Pass()
                {
                    string bicep = @"
                    resource db 'fake.DBforMySQL/servers@2417-12-01-preview' = {
                        name: 'db]'
                    #disable-next-line no-hardcoded-location
                        location: 'westeurope'
                        properties: {
                        administratorLogin: 'sa'
                        administratorLoginPassword: 'don\'t put passwords in plain text'
                        createMode: 'Default'
                        sslEnforcement: 'Disabled'
                        }
                    }
                ";

                    CompileAndTestWithFakeDateAndTypes(
                        bicep,
                        ResourceScope.ResourceGroup,
                        new string[]
                        {
                        "Fake.DBforMySQL/servers@2417-11-30",
                        "Fake.DBforMySQL/servers@2417-12-01-preview",
                        },
                        fakeToday: "2422-07-04",
                        new[] {
                        "[2] Use more recent API version for 'fake.DBforMySQL/servers'. '2417-12-01-preview' is 1676 days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2417-11-30",
                        });
                }

                [TestMethod]
                public void ArmTtk_OnlyPreviewAvailable_EvenIfOld_Pass()
                {
                    string bicep = @"
                       resource namespace 'Fake.DevTestLab/schedules@2417-08-01-preview' = {
                          name: 'namespace'
                          location: 'global'
                          properties: {
                          }
                       }";

                    CompileAndTestWithFakeDateAndTypes(
                        bicep,
                        ResourceScope.ResourceGroup,
                        new string[]
                        {
                           "Fake.DevTestLab/schedules@2417-06-01-preview",
                           "Fake.DevTestLab/schedules@2417-08-01-preview",
                        },
                        fakeToday: "2422-07-04",
                        new string[] { });
                }

                [TestMethod]
                public void NewerPreviewAvailable_Fail()
                {
                    string bicep = @"
                       resource namespace 'Fake.MachineLearningCompute/operationalizationClusters@2417-06-01-preview' = {
                          name: 'clusters'
                          location: 'global'
                       }";

                    CompileAndTestWithFakeDateAndTypes(
                        bicep,
                        ResourceScope.ResourceGroup,
                        new string[]
                        {
                           "Fake.MachineLearningCompute/operationalizationClusters@2417-06-01-preview",
                           "Fake.MachineLearningCompute/operationalizationClusters@2417-08-01-preview",
                        },
                        fakeToday: "2422-07-04",
                        new[] {
                        "[2] Use more recent API version for 'Fake.MachineLearningCompute/operationalizationClusters'. '2417-06-01-preview' is 1859 days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2417-08-01-preview",
                        });
                }

                [TestMethod]
                public void ExtensionResources_RoleAssignment_Pass()
                {
                    // https://docs.microsoft.com/en-us/azure/azure-resource-manager/bicep/scope-extension-resources#apply-to-resource
                    /*
                        [-] apiVersions Should Be Recent (15 ms)
                        Api versions must be the latest or under 2 years old (730 days) - API version 2020-04-01-preview of Microsoft.Authorization/roleAssignments is 830 days old Line: 40, Column: 8
                        Valid Api Versions:
                        2018-07-01
                        2022-01-01-preview
                        2021-04-01-preview
                        2020-10-01-preview
                        2020-08-01-preview
                    */
                    CompileAndTestWithFakeDateAndTypes(@"
                        targetScope = 'subscription'

                        @description('The principal to assign the role to')
                        param principalId string

                        @allowed([
                          'Owner'
                          'Contributor'
                          'Reader'
                        ])
                        @description('Built-in role to assign')
                        param builtInRoleType string

                        var role = {
                          Owner: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/8e3af657-a8ff-443c-a75c-2fe8c4bcb635'
                          Contributor: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/b24988ac-6180-42a0-ab88-20f7382dd24c'
                          Reader: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/acdd72a7-3385-48ef-bd42-f606fba81ae7'
                        }

                        resource roleAssignSub 'fake.Authorization/roleAssignments@2420-04-01-preview' = {
                          name: guid(subscription().id, principalId, role[builtInRoleType])
                          properties: {
                            roleDefinitionId: role[builtInRoleType]
                            principalId: principalId
                          }
                        }",
                            ResourceScope.Subscription,
                            FakeResourceTypes.SubscriptionScopeTypes,
                           fakeToday: "2422-07-04",
                           new String[] {
                           "[21] Use more recent API version for 'fake.Authorization/roleAssignments'. '2420-04-01-preview' is 824 days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2420-10-01-preview, 2420-08-01-preview, 2417-09-01"
                           });
                }

                [TestMethod]
                public void ExtensionResources_Lock_Pass()
                {
                    // https://docs.microsoft.com/en-us/azure/azure-resource-manager/bicep/scope-extension-resources#apply-to-resource
                    CompileAndTestWithFakeDateAndTypes(@"
                       resource createRgLock 'Fake.Authorization/locks@2420-05-01' = {
                          name: 'rgLock'
                          properties: {
                            level: 'CanNotDelete'
                            notes: 'Resource group should not be deleted.'
                          }
                        }",
                        ResourceScope.ResourceGroup,
                        FakeResourceTypes.ResourceScopeTypes,
                        "2422-07-04",
                        new string[] { });
                }

                [TestMethod]
                public void ExtensionResources_SubscriptionRole_Pass()
                {
                    // https://docs.microsoft.com/en-us/azure/azure-resource-manager/bicep/scope-extension-resources#apply-to-resource
                    CompileAndTestWithFakeDateAndTypes(@"
                        targetScope = 'subscription'

                        @description('The principal to assign the role to')
                        param principalId string

                        @allowed([
                          'Owner'
                          'Contributor'
                          'Reader'
                        ])
                        @description('Built-in role to assign')
                        param builtInRoleType string

                        var role = {
                          Owner: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/8e3af657-a8ff-443c-a75c-2fe8c4bcb635'
                          Contributor: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/b24988ac-6180-42a0-ab88-20f7382dd24c'
                          Reader: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/acdd72a7-3385-48ef-bd42-f606fba81ae7'
                        }

                        resource roleAssignSub 'fake.Authorization/roleAssignments@2420-08-01-preview' = {
                          name: guid(subscription().id, principalId, role[builtInRoleType])
                          properties: {
                            roleDefinitionId: role[builtInRoleType]
                            principalId: principalId
                          }
                        }",
                            ResourceScope.Subscription,
                            FakeResourceTypes.SubscriptionScopeTypes,
                            "2422-07-04",
                            new string[] { }
                        );
                }

                [TestMethod]
                public void ExtensionResources_SubscriptionRole_Fail()
                {
                    // https://docs.microsoft.com/en-us/azure/azure-resource-manager/bicep/scope-extension-resources#apply-to-resource
                    CompileAndTestWithFakeDateAndTypes(@"
                        targetScope = 'subscription'

                        @description('The principal to assign the role to')
                        param principalId string

                        @allowed([
                          'Owner'
                          'Contributor'
                          'Reader'
                        ])
                        @description('Built-in role to assign')
                        param builtInRoleType string

                        var role = {
                          Owner: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/8e3af657-a8ff-443c-a75c-2fe8c4bcb635'
                          Contributor: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/b24988ac-6180-42a0-ab88-20f7382dd24c'
                          Reader: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/acdd72a7-3385-48ef-bd42-f606fba81ae7'
                        }

                        resource roleAssignSub 'fake.Authorization/roleAssignments@2417-10-01-preview' = {
                          name: guid(subscription().id, principalId, role[builtInRoleType])
                          properties: {
                            roleDefinitionId: role[builtInRoleType]
                            principalId: principalId
                          }
                        }",
                            ResourceScope.Subscription,
                            FakeResourceTypes.SubscriptionScopeTypes,
                            "2422-07-04",
                            new[] {
                            "[21] Use more recent API version for 'fake.Authorization/roleAssignments'. '2417-10-01-preview' is 1737 days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2420-10-01-preview, 2420-08-01-preview, 2417-09-01",
                            }
                        );
                }

                [TestMethod]
                public void ExtensionResources_ScopeProperty()
                {
                    // https://docs.microsoft.com/en-us/azure/azure-resource-manager/bicep/scope-extension-resources#apply-to-resource
                    CompileAndTestWithFakeDateAndTypes(@"
                        @description('The principal to assign the role to')
                        param principalId string

                        @allowed([
                          'Owner'
                          'Contributor'
                          'Reader'
                        ])
                        @description('Built-in role to assign')
                        param builtInRoleType string

                        param location string = resourceGroup().location

                        var role = {
                          Owner: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/8e3af657-a8ff-443c-a75c-2fe8c4bcb635'
                          Contributor: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/b24988ac-6180-42a0-ab88-20f7382dd24c'
                          Reader: '/subscriptions/${subscription().subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/acdd72a7-3385-48ef-bd42-f606fba81ae7'
                        }
                        #disable-next-line no-loc-expr-outside-params
                        var uniqueStorageName = 'storage${uniqueString(resourceGroup().id)}'

                        // newer stable available
                        resource demoStorageAcct 'fake.Storage/storageAccounts@2420-08-01-preview' = {
                          name: uniqueStorageName
                          location: location
                          sku: {
                            name: 'Standard_LRS'
                          }
                          kind: 'Storage'
                          properties: {}
                        }

                        // old
                        resource roleAssignStorage 'fake.Authorization/roleAssignments@2420-04-01-preview' = {
                          name: guid(demoStorageAcct.id, principalId, role[builtInRoleType])
                          properties: {
                            roleDefinitionId: role[builtInRoleType]
                            principalId: principalId
                          }
                          scope: demoStorageAcct
                        }",
                        ResourceScope.ResourceGroup,
                        FakeResourceTypes.ResourceScopeTypes,
                        "2422-07-04",
                        new[] {
                        "[24] Use more recent API version for 'fake.Storage/storageAccounts'. '2420-08-01-preview' is a preview version and there is a more recent non-preview version available. Must use a non-preview version if available. Acceptable  versions: 2421-06-01, 2421-04-01, 2421-02-01, 2421-01-01",
                        "[35] Use more recent API version for 'fake.Authorization/roleAssignments'. '2420-04-01-preview' is 824 days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2420-10-01-preview, 2420-08-01-preview, 2415-07-01",
                        });
                }

                [TestMethod]
                public void ExtensionResources_ScopeProperty_ExistingResource_PartiallyPass()
                {
                    // https://docs.microsoft.com/en-us/azure/azure-resource-manager/bicep/scope-extension-resources#apply-to-resource
                    CompileAndTestWithFakeDateAndTypes(@"
                        resource demoStorageAcct 'fake.Storage/storageAccounts@2421-04-01' existing = {
                          name: 'examplestore'
                        }

                        resource createStorageLock 'fake.Authorization/locks@2416-09-01' = {
                          name: 'storeLock'
                          scope: demoStorageAcct
                          properties: {
                            level: 'CanNotDelete'
                            notes: 'Storage account should not be deleted.'
                          }
                        }",
                         ResourceScope.ResourceGroup,
                         FakeResourceTypes.ResourceScopeTypes,
                         "2422-07-04",
                         new[] {
                        "[6] Use more recent API version for 'fake.Authorization/locks'. '2416-09-01' is 2132 days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2420-05-01",
                         });
                }

                [TestMethod]
                public void SubscriptionDeployment_OldApiVersion_Fail()
                {
                    CompileAndTestWithFakeDateAndTypes(@"
                        targetScope='subscription'

                        param resourceGroupName string
                        param resourceGroupLocation string

                        resource newRG 'fake.Resources/resourceGroups@2419-05-10' = {
                          name: resourceGroupName
                          location: resourceGroupLocation
                        }",
                        ResourceScope.Subscription,
                        FakeResourceTypes.SubscriptionScopeTypes,
                        "2422-07-04",
                        new[] {
                        "[7] Use more recent API version for 'fake.Resources/resourceGroups'. '2419-05-10' is 1151 days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2421-05-01, 2421-04-01, 2421-01-01, 2420-10-01, 2420-08-01"
                        });
                }

                [TestMethod]
                public void SubscriptionDeployment_Pass()
                {
                    CompileAndTestWithFakeDateAndTypes(@"
                        targetScope='subscription'

                        param resourceGroupName string
                        param resourceGroupLocation string

                        resource newRG 'fake.Resources/resourceGroups@2421-01-01' = {
                          name: resourceGroupName
                          location: resourceGroupLocation
                        }",
                        ResourceScope.Subscription,
                        FakeResourceTypes.SubscriptionScopeTypes,
                        "2422-07-04",
                        new string[] { });
                }

                [TestMethod]
                public void ResourceTypeNotFound_Error()
                {
                    string bicep = @"
                       resource namespace 'DontKnowWho.MachineLearningCompute/operationalizationClusters@2417-06-01-preview' = {
                          name: 'clusters'
                          location: 'global'
                       }";

                    CompileAndTestWithFakeDateAndTypes(
                        bicep,
                        ResourceScope.ResourceGroup,
                        FakeResourceTypes.ResourceScopeTypes,
                        "2422-07-04",
                    new string[] {
                    "[2] Could not find resource type \"DontKnowWho.MachineLearningCompute/operationalizationClusters\".",
                    });
                }

                [TestMethod]
                public void ApiVersionNotFound_Error()
                {
                    string bicep = @"
                       resource namespace 'Fake.MachineLearningCompute/operationalizationClusters@2417-06-01-beta' = {
                          name: 'clusters'
                          location: 'global'
                       }";

                    CompileAndTestWithFakeDateAndTypes(
                        bicep,
                        ResourceScope.ResourceGroup,
                        FakeResourceTypes.ResourceScopeTypes,
                        "2422-07-04",
                        new string[] {
                        "[2] Could not find apiVersion 2417-06-01-beta for Fake.MachineLearningCompute/operationalizationClusters. Must use a non-preview version if available. Acceptable  versions: 2417-08-01-preview"
                        });
                }

                [TestMethod]
                public void ProvidesFixWithMostRecentVersion()
                {
                    CompileAndTestFixWithFakeDateAndTypes(@"
                        targetScope='subscription'

                        param resourceGroupName string
                        param resourceGroupLocation string

                        resource newRG 'fake.Resources/resourceGroups@2419-05-10' = {
                          name: resourceGroupName
                          location: resourceGroupLocation
                        }",
                        ResourceScope.Subscription,
                        FakeResourceTypes.SubscriptionScopeTypes,
                        "2422-07-04",
                        new[] {
                        new DiagnosticAndFixes(
                            "Use more recent API version for 'fake.Resources/resourceGroups'. '2419-05-10' is 1151 days old, should be no more than 730 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2421-05-01, 2421-04-01, 2421-01-01, 2420-10-01, 2420-08-01",
                            "Replace with 2421-05-01",
                            "resource newRG 'fake.Resources/resourceGroups@2421-05-01'"
                            )
                        });
                }

                [TestMethod]
                public void ProvidesFixWithMostRecentVersion_CustomMaxAge_TooOld()
                {
                    // Just before cut-off for 2421-04-01
                    CompileAndTestFixWithFakeDateAndTypes(@"
                        targetScope='subscription'

                        param resourceGroupName string
                        param resourceGroupLocation string

                        resource newRG 'fake.Resources/resourceGroups@2419-05-10' = {
                          name: resourceGroupName
                          location: resourceGroupLocation
                        }",
                        ResourceScope.Subscription,
                        FakeResourceTypes.SubscriptionScopeTypes,
                        "2422-07-04",
                        new[] {
                        new DiagnosticAndFixes(
                            "Use more recent API version for 'fake.Resources/resourceGroups'. '2419-05-10' is 1151 days old, should be no more than 459 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2421-05-01, 2421-04-01",
                            "Replace with 2421-05-01",
                            "resource newRG 'fake.Resources/resourceGroups@2421-05-01'"
                            )
                        },
                        maxAgeInDays: 459
                    );

                    // Just after cut-off for 2421-04-01

                    CompileAndTestFixWithFakeDateAndTypes(@"
                        targetScope='subscription'

                        param resourceGroupName string
                        param resourceGroupLocation string

                        resource newRG 'fake.Resources/resourceGroups@2419-05-10' = {
                          name: resourceGroupName
                          location: resourceGroupLocation
                        }",
                        ResourceScope.Subscription,
                        FakeResourceTypes.SubscriptionScopeTypes,
                        "2422-07-04",
                        new[] {
                        new DiagnosticAndFixes(
                            "Use more recent API version for 'fake.Resources/resourceGroups'. '2419-05-10' is 1151 days old, should be no more than 458 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2421-05-01",
                            "Replace with 2421-05-01",
                            "resource newRG 'fake.Resources/resourceGroups@2421-05-01'"
                            )
                        },
                        maxAgeInDays: 458
                    );
                }

                [TestMethod]
                public void NotTooOld_CustomMaxAge_ShouldBeNoErrors()
                {
                    // Just before cut-off for 2421-04-01
                    CompileAndTestWithFakeDateAndTypes(@"
                        targetScope='subscription'

                        param resourceGroupName string
                        param resourceGroupLocation string

                        resource newRG 'fake.Resources/resourceGroups@2421-04-01' = {
                          name: resourceGroupName
                          location: resourceGroupLocation
                        }",
                        ResourceScope.Subscription,
                        FakeResourceTypes.SubscriptionScopeTypes,
                        "2422-07-04",
                        new string[] {
                        },
                        maxAgeInDays: 459
                    );
                }

                [TestMethod]
                public void ProvidesFixWithMostRecentVersion_CustomAgeZero()
                {
                    CompileAndTestFixWithFakeDateAndTypes(@"
                        targetScope='subscription'

                        param resourceGroupName string
                        param resourceGroupLocation string

                        resource newRG 'fake.Resources/resourceGroups@2419-05-10' = {
                          name: resourceGroupName
                          location: resourceGroupLocation
                        }",
                        ResourceScope.Subscription,
                        FakeResourceTypes.SubscriptionScopeTypes,
                        "2422-07-04",
                        new[] {
                        new DiagnosticAndFixes(
                            "Use more recent API version for 'fake.Resources/resourceGroups'. '2419-05-10' is 1151 days old, should be no more than 0 days old, or the most recent. Must use a non-preview version if available. Acceptable  versions: 2421-05-01",
                            "Replace with 2421-05-01",
                            "resource newRG 'fake.Resources/resourceGroups@2421-05-01'"
                            )
                        },
                        maxAgeInDays: 0);
                }

                [TestMethod]
                public void LinterIgnoresNotAzureResources()
                {
                    CompileAndTestWithFakeDateAndTypes(@"
                        import 'kubernetes@1.0.0' {
                          namespace: 'default'
                          kubeConfig: ''
                        }
                        resource service 'core/Service@v1' existing = {
                          metadata: {
                            name: 'existing-service'
                            namespace: 'default'
                            labels: {
                              format: 'k8s-extension'
                            }
                            annotations: {
                              foo: 'bar'
                            }
                          }
                        }
                    ",
                        ResourceScope.ResourceGroup,
                        FakeResourceTypes.ResourceScopeTypes,
                        "2422-07-04",
                        new string[] {
                            // pass
                        },
                        OnCompileErrors.Ignore);
                }
            }
        }
    }
