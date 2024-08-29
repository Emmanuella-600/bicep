// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Bicep.Core.CodeAction;
using Bicep.Core.Diagnostics;
using Bicep.Core.Extensions;
using Bicep.Core.Parsing;
using Bicep.Core.Samples;
using Bicep.Core.Syntax;
using Bicep.Core.Text;
using Bicep.Core.UnitTests;
using Bicep.Core.UnitTests.Assertions;
using Bicep.Core.UnitTests.PrettyPrintV2;
using Bicep.Core.UnitTests.Serialization;
using Bicep.Core.UnitTests.Utils;
using Bicep.Core.Workspaces;
using Bicep.LangServer.IntegrationTests.Helpers;
using Bicep.LanguageServer.Extensions;
using Bicep.LanguageServer.Utils;
using FluentAssertions;
using FluentAssertions.Execution;
using Humanizer;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using static Google.Protobuf.Reflection.SourceCodeInfo.Types;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

/* bug: getting linter errors:
 resource nestedDiscriminatorMissingKey 'Microsoft.DocumentDB/databaseAccounts@2020-06-01-preview' = {
  name: 'test'
  location: 'l'
  properties: {
  }
}

var nestedDiscriminatorMissingKeyCompletions = nestedDiscriminatorMissingKey.properties.cr
var nestedDiscriminatorMissingKeyCompletions2 = nestedDiscriminatorMissingKey['properties'].

var nestedDiscriminatorMissingKeyIndexCompletions = nestedDiscriminatorMissingKey.properties['']
//@[004:049) [no-unused-vars (Warning)] Variable "nestedDiscriminatorMissingKeyIndexCompletions" is declared but never used. (bicep core linter https://aka.ms/bicep/linter/no-unused-vars) |nestedDiscriminatorMissingKeyIndexCompletions|
//@[092:096) [prefer-unquoted-property-names (Warning)] Property names that are valid identifiers should be declared without quotation marks and accessed using dot notation. (bicep core linter https://aka.ms/bicep/linter/prefer-unquoted-property-names) |['']|

*/

/*asdfg



@description('Properties to create and update Azure Cosmos DB database accounts.')
param properties object? /* DatabaseAccountCreateUpdatePropertiesOrDatabaseAccountGetProperties * / = {}
resource nestedDiscriminatorMissingKey 'Microsoft.DocumentDB/databaseAccounts@2020-06-01-preview' = {
  name: 'test'
  location: 'l'
  properties: properties
}

var nestedDiscriminatorMissingKeyCompletions = nestedDiscriminatorMissingKey.properties.cr
var nestedDiscriminatorMissingKeyCompletions2 = nestedDiscriminatorMissingKey['properties'].

var nestedDiscriminatorMissingKeyIndexCompletions = nestedDiscriminatorMissingKey.properties['']
/@[004:049) [no-unused-vars (Warning)] Variable "nestedDiscriminatorMissingKeyIndexCompletions" is declared but never used. (bicep core linter https://aka.ms/bicep/linter/no-unused-vars) |nestedDiscriminatorMissingKeyIndexCompletions|
/@[092:096) [prefer-unquoted-property-names (Warning)] Property names that are valid identifiers should be declared without quotation marks and accessed using dot notation. (bicep core linter https://aka.ms/bicep/linter/prefer-unquoted-property-names) |['']|



*/


/* asdfg



type negativeIntLiteral = -10
type negatedIntReference = -negativeIntLiteral
type negatedBoolLiteral = !true
type negatedBoolReference = !negatedBoolLiteral
type t = {
  a: negativeIntLiteral
  b: negatedIntReference
  c: negatedBoolLiteral
  d: negatedBoolReference
}
param p t = {
  a: -10
  b: 10
  c: false
  d: true
}


extract sku: - end up with 'string'|string, and also required 'tier'
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-04-01' = {
  name: storageAccountConfig.name
  location: location
  sku: {
    name: storageAccountConfig.sku
  }
  kind: 'StorageV2'
}


https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/data-types#custom-tagged-union-data-type





type anObject = {
  property: string
  optionalProperty: string?
}
 
param aParameter anObject = {
  property: 'value'
  otionalProperty: 'value'
}



module type appears (dependsOn)

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-04-01' = <<{
  name: storageAccountName
  location: location
  sku: {
    name: storageAccountSKU
  }
  kind: 'StorageV2'
}>>



type invalidRecursiveObjectType = {
  level1: {
    level2: {
      level3: {
        level4: {
          level5: invalidRecursiveObjectType
        }
      }
    }
  }
}
param p invalidRecursiveObjectType = {
              level1: {
                level2: {
                  level3: {
                    level4: {
                      level5: null
                    }
                  }
                }
              }
            }



type obj = {
  @description('The object ID')
  id: int

  @description('Additional properties')
  @minLength(10)
  *: string
}



var blah1 = [<<{ foo: 'bar' }>>, { foo: 'baz' }]
why isn't this extractding just the object?


fileUris should be string[] not [string]
     settings: {
      fileUris: [
        uri(_artifactsLocation, 'writeblob.ps1${_artifactsLocationSasToken}')
      ]
      commandToExecute: 'commandToExecute'
    }


// ======================= ISSUE CRASHES
// type TFoo = {
//   property: TFoo?
// }
// param pfoo TFoo
// var fv = pfoo



 type foo = {
  property: foo?
}

bad:
param <<p1>> int = 2


type recursive1 = [string, recursive1?]
param p1 recursive1 = ['a', ['b', ['c', ['d', null]]]]
var a1 = p1


 */

//asdfg move new parameter/var to top
//asdfg rename
// asdfg multi-line formatting



namespace Bicep.LangServer.IntegrationTests;

[TestClass]
public class ExtractVarAndParamTests : CodeActionTestBase
{
    private const string ExtractToVariableTitle = "Extract variable";
    private const string ExtractToParameterTitle = "Extract parameter";


    ////////////////////////////////////////////////////////////////////

    [DataTestMethod]
    [DataRow(
        """
            type superComplexType = {
                p: string
                i: 123 || 456
            }

            param p { *: superComplexType } = {
                a: <<{ p: 'mystring', i: 123 }>>
            }
            """,
        """
            type superComplexType = {
                p: string
                i: 123 | 456
            }

            param a object = { p: 'mystring', i: 123 }
            param p { *: superComplexType } = {
                a: a
            }
            """,
        """
            type superComplexType = {
                p: string
                i: 123 | 456
            }

            param a { i: 123 | 456, p: string } = { p: 'mystring', i: 123 }
            param p { *: superComplexType } = {
                a: a
            }
            """)]

    [DataRow(
        """
            var blah = |[{foo: 'bar'}, {foo: 'baz'}]
            """,
        """
            param newParameter array = [{ foo: 'bar' }, { foo: 'baz' }]
            var blah = newParameter
            """,
        """
            param newParameter { foo: string }[] = [{ foo: 'bar' }, { foo: 'baz' }]
            var blah = newParameter
            """)]

    [DataRow(
        """
            param p1 { intVal: int} = { intVal:123}
            output o object = <<p1>>
            """,
        """
            param p1 { intVal: int} = { intVal:123}
            param newParameter object = p1
            output o object = newParameter
            """,
        """
            param p1 { intVal: int} = { intVal:123}
            param newParameter { intVal: int } = p1
            output o object = newParameter
            """)]

    [DataRow(
        """
            param p2 'foo' || 'bar'
            param v1 int = |p2
            """,
        """
            param p2 'foo' | 'bar'
            param newParameter string = p2
            param v1 int = newParameter
            """,
        """
            param p2 'foo' | 'bar'
            param newParameter 'bar' | 'foo' = p2
            param v1 int = newParameter
            """)]

    [DataRow(
        """
            param p1 { intVal: int}
            output o = <<p1>>
            """,
        """
            param p1 { intVal: int}
            param newParameter object = p1
            output o = newParameter
            """,
        """
            param p1 { intVal: int}
            param newParameter { intVal: int } = p1
            output o = newParameter
            """)]

    public async Task BicepDiscussion(string fileWithSelection, string expectedLooseParamText, string expectedMediumParamText)
    {
        await RunExtractToParameterTest(fileWithSelection, expectedLooseParamText, expectedMediumParamText);
    }

    ////////////////////////////////////////////////////////////////////

    [DataTestMethod]
    [DataRow(
        """
            var a = '|b'
            """,
        """
            var newVariable = 'b'
            var a = newVariable
            """,
        """
            param newParameter string = 'b'
            var a = newParameter
            """)]
    [DataRow(
        """
            var a = 'a'
            var b = '|b'
            var c = 'c'
            """,
        """
            var a = 'a'
            var newVariable = 'b'
            var b = newVariable
            var c = 'c'
            """,
        """
            var a = 'a'
            param newParameter string = 'b'
            var b = newParameter
            var c = 'c'
            """)]
    [DataRow(
        """
            var a = 1 + |2
            """,
        """
            var newVariable = 2
            var a = 1 + newVariable
            """,
        """
            param newParameter int = 2
            var a = 1 + newParameter
            """)]
    [DataRow(
        """
            var a = <<1 + 2>>
            """,
        """
            var newVariable = 1 + 2
            var a = newVariable
            """,
        """
            param newParameter int = 1 + 2
            var a = newParameter
            """)]
    [DataRow(
        """
            var a = <<1 +>> 2
            """,
        """
            var newVariable = 1 + 2
            var a = newVariable
            """,
        "IGNORE")]
    [DataRow(
        """
            var a = 1 |+ 2
            """,
        """
            var newVariable = 1 + 2
            var a = newVariable
            """,
        "IGNORE")]
    [DataRow(
        """
            var a = 1 <<+ 2 + 3 >>+ 4
            """,
        """
            var newVariable = 1 + 2 + 3 + 4
            var a = newVariable
            """,
        "IGNORE")]
    [DataRow(
        """
            param p1 int = 1 + |2
            """,
        """
            var newVariable = 2
            param p1 int = 1 + newVariable
            """,
        "IGNORE")]
    [DataRow(
        """
            var a = 1 + 2
            var b = '${a}|{a}'
            """,
        """
            var a = 1 + 2
            var newVariable = '${a}{a}'
            var b = newVariable
            """,
        """
            var a = 1 + 2
            param newParameter string = '${a}{a}'
            var b = newParameter
            """,
        DisplayName = "Full interpolated string")]
    [DataRow(
        """
            // comment 1
            @secure
            // comment 2
            param a = '|a'
            """,
        """
            // comment 1
            var newVariable = 'a'
            @secure
            // comment 2
            param a = newVariable
            """,
        """
            // comment 1
            param newParameter string = 'a'
            @secure
            // comment 2
            param a = newParameter
            """,
        DisplayName = "Preceding lines")]
    [DataRow(
        """
            var a = 1
            var b = [
                'a'
                1 + <<2>>
                'c'
            ]
            """,
        """
            var a = 1
            var newVariable = 2
            var b = [
                'a'
                1 + newVariable
                'c'
            ]
            """,
        """
            var a = 1
            param newParameter int = 2
            var b = [
                'a'
                1 + newParameter
                'c'
            ]
            """,
        DisplayName = "Inside a data structure")]
    [DataRow(
        """
            // My comment here
            resource storageaccount 'Microsoft.Storage/storageAccounts@2021-02-01' = {
                name: 'name'
                location: |'westus'
                kind: 'StorageV2'
                sku: {
                name: 'Premium_LRS'
                }
            }
            """,
        """
            // My comment here
            var location = 'westus'
            resource storageaccount 'Microsoft.Storage/storageAccounts@2021-02-01' = {
                name: 'name'
                location: location
                kind: 'StorageV2'
                sku: {
                name: 'Premium_LRS'
                }
            }
            """,
        """
            // My comment here
            @description('Required. Gets or sets the location of the resource. This will be one of the supported and registered Azure Geo Regions (e.g. West US, East US, Southeast Asia, etc.). The geo region of a resource cannot be changed once it is created, but if an identical geo region is specified on update, the request will succeed.')
            param location string = 'westus'
            resource storageaccount 'Microsoft.Storage/storageAccounts@2021-02-01' = {
                name: 'name'
                location: location
                kind: 'StorageV2'
                sku: {
                name: 'Premium_LRS'
                }
            }
            """)]
    public async Task Basics(string fileWithSelection, string? expectedVarText, string? expectedLooseParamText = null, string? expectedMediumParamText = null)
    {
        await RunExtractToVariableAndParameterTest(fileWithSelection, expectedVarText, expectedLooseParamText, expectedMediumParamText);
    }

    ////////////////////////////////////////////////////////////////////

    [DataTestMethod]
    [DataRow(
        """
            var a = '|b'
            """,
        """
            param newParameter string = 'b'
            var a = newParameter
            """,
        null // no second option
        )]
    [DataRow(
        """
            var a = |{a: 'b'}
            """,
        """
            param newParameter object = { a: 'b' }
            var a = newParameter
            """,
        """
            param newParameter { a: string } = { a: 'b' }
            var a = newParameter
            """)]
    public async Task ShouldOfferTwoParameterExtractions_IffTheExtractedTypesAreDifferent(string fileWithSelection, string? expectedLooseParamText, string? expectedMediumParamText)
    {
        await RunExtractToParameterTest(fileWithSelection, expectedLooseParamText, expectedMediumParamText);
    }

    ////////////////////////////////////////////////////////////////////

    [DataTestMethod]
    [DataRow(
        """
            var newVariable = 'newVariable'
            param newVariable2 string = '|newVariable2'
            """,
                """
            var newVariable = 'newVariable'
            var newVariable3 = 'newVariable2'
            param newVariable2 string = newVariable3
            """,
        DisplayName = "Simple naming conflict")
    ]
    [DataRow(
        """
            var id = [1, 2, 3]
            param id2 string = 'hello'
            resource id6 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = [
                for (id3, id4) in id: {
                    name: 'subnet${id3}'
                    properties: {
                        addressPrefix: '10.0.${id4}.0/24'
                        natGateway: {
                        id: '|gatewayId'
                        }
                    }
                }
            ]
            output id5 string = id2
            """,
        """
            var id = [1, 2, 3]
            var id7 = 'gatewayId'
            param id2 string = 'hello'
            resource id6 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = [
                for (id3, id4) in id: {
                    name: 'subnet${id3}'
                    properties: {
                        addressPrefix: '10.0.${id4}.0/24'
                        natGateway: {
                        id: id7
                        }
                    }
                }
            ]
            output id5 string = id2
            """,
        DisplayName = "Complex naming conflicts")]
    public async Task ShouldRenameToAvoidConflicts(string fileWithSelection, string expectedText)
    {
        await RunExtractToVariableTest(fileWithSelection, expectedText);
    }

    ////////////////////////////////////////////////////////////////////

    [TestMethod]
    public async Task ShouldHandleArrays()
    {
        await RunExtractToVariableAndParameterTest(
            """
                resource subnets 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = [
                  for (item, index) in <<[1, 2, 3]>>: {
                    name: 'subnet${index}'
                    properties: {
                      addressPrefix: '10.0.${index}.0/24'
                    }
                  }
                ]
                """,
            """
                var newVariable = [1, 2, 3]
                resource subnets 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = [
                  for (item, index) in newVariable: {
                    name: 'subnet${index}'
                    properties: {
                      addressPrefix: '10.0.${index}.0/24'
                    }
                  }
                ]
                """,
            """
                param newParameter array = [1, 2, 3]
                resource subnets 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = [
                  for (item, index) in newParameter: {
                    name: 'subnet${index}'
                    properties: {
                      addressPrefix: '10.0.${index}.0/24'
                    }
                  }
                ]
                """,
            """
                param newParameter int[] = [1, 2, 3]
                resource subnets 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = [
                  for (item, index) in newParameter: {
                    name: 'subnet${index}'
                    properties: {
                      addressPrefix: '10.0.${index}.0/24'
                    }
                  }
                ]
                """);
    }

    ////////////////////////////////////////////////////////////////////

    [TestMethod]
    public async Task ShouldHandleObjects()
    {
        await RunExtractToVariableAndParameterTest("""
            param _artifactsLocation string
            param  _artifactsLocationSasToken string

            resource resourceWithProperties 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' = if (isWindowsOS && provisionExtensions) {
                parent: vmName_resource
                name: 'cse-windows'
                location: location
                properties: <<{
                    // Entire properties object selected
                    publisher: 'Microsoft.Compute'
                    type: 'CustomScriptExtension'
                    typeHandlerVersion: '1.8'
                    autoUpgradeMinorVersion: true
                    settings: {
                      fileUris: [
                        uri(_artifactsLocation, 'writeblob.ps1${_artifactsLocationSasToken}')
                      ]
                      commandToExecute: commandToExecute
                    }
                }>>
            }
            """,
        """
            param _artifactsLocation string
            param  _artifactsLocationSasToken string

            var properties = {
              // Entire properties object selected
              publisher: 'Microsoft.Compute'
              type: 'CustomScriptExtension'
              typeHandlerVersion: '1.8'
              autoUpgradeMinorVersion: true
              settings: {
                fileUris: [
                  uri(_artifactsLocation, 'writeblob.ps1${_artifactsLocationSasToken}')
                ]
                commandToExecute: commandToExecute
              }
            }
            resource resourceWithProperties 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' = if (isWindowsOS && provisionExtensions) {
                parent: vmName_resource
                name: 'cse-windows'
                location: location
                properties: properties
            }
            """,
        """
            param _artifactsLocation string
            param  _artifactsLocationSasToken string
            @description('Describes the properties of a Virtual Machine Extension.')
            param properties object = {
              // Entire properties object selected
              publisher: 'Microsoft.Compute'
              type: 'CustomScriptExtension'
              typeHandlerVersion: '1.8'
              autoUpgradeMinorVersion: true
              settings: {
                fileUris: [
                  uri(_artifactsLocation, 'writeblob.ps1${_artifactsLocationSasToken}')
                ]
                commandToExecute: commandToExecute
              }
            }

            resource resourceWithProperties 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' = if (isWindowsOS && provisionExtensions) {
                parent: vmName_resource
                name: 'cse-windows'
                location: location
                properties: properties
            }
            """,
        """
            param _artifactsLocation string
            param  _artifactsLocationSasToken string
            @description('Describes the properties of a Virtual Machine Extension.')
            param properties {
              autoUpgradeMinorVersion: bool?
              forceUpdateTag: string?
              instanceView: {
                name: string?
                statuses: {
                  code: string?
                  displayStatus: string?
                  level: ('Error' | 'Info' | 'Warning')?
                  message: string?
                  time: string?
                }[]?
                substatuses: {
                  code: string?
                  displayStatus: string?
                  level: ('Error' | 'Info' | 'Warning')?
                  message: string?
                  time: string?
                }[]?
                type: string?
                typeHandlerVersion: string?
              }?
              protectedSettings: object? /* any */
              publisher: string?
              settings: object? /* any */
              type: string?
              typeHandlerVersion: string?
            } = {
              // Entire properties object selected
              publisher: 'Microsoft.Compute'
              type: 'CustomScriptExtension'
              typeHandlerVersion: '1.8'
              autoUpgradeMinorVersion: true
              settings: {
                fileUris: [
                  uri(_artifactsLocation, 'writeblob.ps1${_artifactsLocationSasToken}')
                ]
                commandToExecute: commandToExecute
              }
            }

            resource resourceWithProperties 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' = if (isWindowsOS && provisionExtensions) {
                parent: vmName_resource
                name: 'cse-windows'
                location: location
                properties: properties
            }
            """);
    }

    ////////////////////////////////////////////////////////////////////

    [DataRow(
        """
            var i = <<1>>
            """,
        """
            param newParameter int = 1
            var i = newParameter
            """,
        null,
        DisplayName = "Literal integer")]
    [DataRow(
        """
            param i int = 1
            var j = <<i>> + 1
            """,
        """
            param i int = 1
            param newParameter int = i
            var j = newParameter + 1
            """,
        null,
        DisplayName = "int parameter reference")]
    [DataRow(
        """
            param i int = 1
            var j = <<i + 1>>
            """,
        """
            param i int = 1
            param newParameter int = i + 1
            var j = newParameter
            """,
        null,
        DisplayName = "int expression with param")]
    [DataRow(
        """
            param i string
            var j = <<concat(i, i)>>
            """,
        """
            param i string
            param newParameter string = concat(i, i)
            var j = newParameter
            """,
        null,
        DisplayName = "strings concatenated")]
    [DataRow(
        """
            param i string = 'i'
            var i2 = 'i2'
            var j = <<'{i}{i2}'>>
            """,
        """
            param i string = 'i'
            param newParameter string = '{i}{i2}'
            var i2 = 'i2'
            var j = newParameter
            """,
        null,
        DisplayName = "strings concatenated")]
    [DataRow(
        """
            var p = <<[ 1, 2, 3 ]>>
            """,
        """
            param newParameter array = [1, 2, 3]
            var p = newParameter
            """,
        """
            param newParameter int[] = [1, 2, 3]
            var p = newParameter
            """,
        DisplayName = "array literal")]
    [DataRow(
        """
            var p = <<{ a: 1, b: 'b' }>>
            """,
        """
            param newParameter object = { a: 1, b: 'b' }
            var p = newParameter
            """,
        """
            param newParameter { a: int, b: string } = { a: 1, b: 'b' }
            var p = newParameter
            """,
        DisplayName = "object literal with literal types")]
    [DataRow(
        """
            var p = { a: <<1>>, b: 'b' }
            """,
        """
            param a int = 1
            var p = { a: a, b: 'b' }
            """,
        null,
        DisplayName = "property value from object literal")]
    [DataRow(
        """
            var o1 = { a: 1, b: 'b' }
            var a = <<o1.a>>
            """,
        """
            var o1 = { a: 1, b: 'b' }
            param o1A int = o1.a
            var a = o1A
            """,
        null,
        DisplayName = "referenced property value from object literal")]
    [DataRow(
        """
            param p 'a'||'b' = 'a'
            var v = <<p>>
            """,
        """
            param p 'a'|'b' = 'a'
            param newParameter string = p
            var v = newParameter
            """,
        """
            param p 'a'|'b' = 'a'
            param newParameter 'a' | 'b' = p
            var v = newParameter
            """,
        DisplayName = "string literal union")]
    [DataRow(
        """
            var a = {
                int: 1
            }
            var b = |a.int
            """,
        """
            var a = {
                int: 1
            }
            param newParameter object = a
            var b = newParameter.int
            """,
        """
            var a = {
                int: 1
            }
            param newParameter { int: int } = a
            var b = newParameter.int
            """,
    DisplayName = "object properties")]
    [DataRow(
        """
            param p {
                i: int
                o: {
                i2: int
                }
            } = { i:1, o: { i2: 2} }
            var v = <<p>>.o.i2
            """,
        """
            param p {
                i: int
                o: {
                i2: int
                }
            } = { i:1, o: { i2: 2} }
            param newParameter object = p
            var v = newParameter.o.i2
            """,
        """
            param p {
                i: int
                o: {
                i2: int
                }
            } = { i:1, o: { i2: 2} }
            param newParameter { i: int, o: { i2: int } } = p
            var v = newParameter.o.i2
            """,
        DisplayName = "custom object type, whole object")]
    [DataRow(
        """
            param p {
                i: int
                o: {
                i2: int
                }
            } = { i:1, o: { i2: 2} }
            var v = p.|o.i2
            """,
        """
            param p {
                i: int
                o: {
                i2: int
                }
            } = { i:1, o: { i2: 2} }
            param pO object = p.o
            var v = pO.i2
            """,
        """
            param p {
                i: int
                o: {
                i2: int
                }
            } = { i:1, o: { i2: 2} }
            param pO { i2: int } = p.o
            var v = pO.i2
            """,
        DisplayName = "custom object type, partial")]
    [DataRow("""
            resource aksCluster 'Microsoft.ContainerService/managedClusters@2021-03-01' = {
                unknownProperty: |123
            }
            """,
        """
            param unknownProperty int = 123
            resource aksCluster 'Microsoft.ContainerService/managedClusters@2021-03-01' = {
                unknownProperty: unknownProperty
            }
            """,
        null,
        DisplayName = "resource types undefined 1")]
    [DataRow(
        """
            param p1 'abc'||'def'
            resource aksCluster 'Microsoft.ContainerService/managedClusters@2021-03-01' = {
                unknownProperty: |p1
            }
            """,
        """
            param p1 'abc'|'def'
            param unknownProperty string = p1
            resource aksCluster 'Microsoft.ContainerService/managedClusters@2021-03-01' = {
                unknownProperty: unknownProperty
            }
            """,
        """
            param p1 'abc'|'def'
            param unknownProperty 'abc' | 'def' = p1
            resource aksCluster 'Microsoft.ContainerService/managedClusters@2021-03-01' = {
                unknownProperty: unknownProperty
            }
            """,
        DisplayName = "resource properties unknown property, follows expression's inferred type")]
    [DataRow("""
            var foo = <<{ intVal: 2 }>>
            """,
        """
            param newParameter object = { intVal: 2 }
            var foo = newParameter
            """,
        """
            param newParameter { intVal: int } = { intVal: 2 }
            var foo = newParameter
            """)]
    //asdfg gives this: param newParameter { asserts: object, dependsOn: (module[] | (resource | module) | resource[])[], eTag: string, extendedLocation: { name: string?, type: ('EdgeZone' | string)? }?, identity: { type: ('None' | 'SystemAssigned' | 'UserAssigned')?, userAssignedIdentities: object? }?, kind: string, location: string, managedBy: string, managedByExtended: string[], name: string, plan: object, properties: { aadProfile: { adminGroupObjectIDs: string[]?, clientAppID: string?, enableAzureRBAC: bool?, managed: bool?, serverAppID: string?, serverAppSecret: string?, tenantID: string? }?, addonProfiles: object?, agentPoolProfiles: { availabilityZones: string[]?, count: int?, enableAutoScaling: bool?, enableEncryptionAtHost: bool?, enableFIPS: bool?, enableNodePublicIP: bool?, gpuInstanceProfile: ('MIG1g' | 'MIG2g' | 'MIG3g' | 'MIG4g' | 'MIG7g' | string)?, kubeletConfig: { allowedUnsafeSysctls: string[]?, containerLogMaxFiles: int?, containerLogMaxSizeMB: int?, cpuCfsQuota: bool?, cpuCfsQuotaPeriod: string?, cpuManagerPolicy: string?, failSwapOn: bool?, imageGcHighThreshold: int?, imageGcLowThreshold: int?, podMaxPids: int?, topologyManagerPolicy: string? }?, kubeletDiskType: ('OS' | 'Temporary' | string)?, linuxOSConfig: { swapFileSizeMB: int?, sysctls: { fsAioMaxNr: int?, fsFileMax: int?, fsInotifyMaxUserWatches: int?, fsNrOpen: int?, kernelThreadsMax: int?, netCoreNetdevMaxBacklog: int?, netCoreOptmemMax: int?, netCoreRmemDefault: int?, netCoreRmemMax: int?, netCoreSomaxconn: int?, netCoreWmemDefault: int?, netCoreWmemMax: int?, netIpv4IpLocalPortRange: string?, netIpv4NeighDefaultGcThresh1: int?, netIpv4NeighDefaultGcThresh2: int?, netIpv4NeighDefaultGcThresh3: int?, netIpv4TcpFinTimeout: int?, netIpv4TcpKeepaliveProbes: int?, netIpv4TcpKeepaliveTime: int?, netIpv4TcpMaxSynBacklog: int?, netIpv4TcpMaxTwBuckets: int?, netIpv4TcpTwReuse: bool?, netIpv4TcpkeepaliveIntvl: int?, netNetfilterNfConntrackBuckets: int?, netNetfilterNfConntrackMax: int?, vmMaxMapCount: int?, vmSwappiness: int?, vmVfsCachePressure: int? }?, transparentHugePageDefrag: string?, transparentHugePageEnabled: string? }?, maxCount: int?, maxPods: int?, minCount: int?, mode: ('System' | 'User' | string)?, name: string, nodeLabels: object?, nodePublicIPPrefixID: string?, nodeTaints: string[]?, orchestratorVersion: string?, osDiskSizeGB: int?, osDiskType: ('Ephemeral' | 'Managed' | string)?, osSKU: ('CBLMariner' | 'Ubuntu' | string)?, osType: ('Linux' | 'Windows' | string)?, podSubnetID: string?, proximityPlacementGroupID: string?, scaleSetEvictionPolicy: ('Deallocate' | 'Delete' | string)?, scaleSetPriority: ('Regular' | 'Spot' | string)?, spotMaxPrice: int?, tags: object?, type: ('AvailabilitySet' | 'VirtualMachineScaleSets' | string)?, upgradeSettings: { maxSurge: string? }?, vmSize: string?, vnetSubnetID: string? }[]?, apiServerAccessProfile: { authorizedIPRanges: string[]?, enablePrivateCluster: bool?, privateDNSZone: string? }?, autoScalerProfile: { 'balance-similar-node-groups': string?, expander: ('least-waste' | 'most-pods' | 'priority' | 'random' | string)?, 'max-empty-bulk-delete': string?, 'max-graceful-termination-sec': string?, 'max-node-provision-time': string?, 'max-total-unready-percentage': string?, 'new-pod-scale-up-delay': string?, 'ok-total-unready-count': string?, 'scale-down-delay-after-add': string?, 'scale-down-delay-after-delete': string?, 'scale-down-delay-after-failure': string?, 'scale-down-unneeded-time': string?, 'scale-down-unready-time': string?, 'scale-down-utilization-threshold': string?, 'scan-interval': string?, 'skip-nodes-with-local-storage': string?, 'skip-nodes-with-system-pods': string? }?, autoUpgradeProfile: { upgradeChannel: ('node-image' | 'none' | 'patch' | 'rapid' | 'stable' | string)? }?, disableLocalAccounts: bool?, diskEncryptionSetID: string?, dnsPrefix: string?, enablePodSecurityPolicy: bool?, enableRBAC: bool?, fqdnSubdomain: string?, httpProxyConfig: { httpProxy: string?, httpsProxy: string?, noProxy: string[]?, trustedCa: string? }?, identityProfile: object?, kubernetesVersion: string?, linuxProfile: { adminUsername: string, ssh: { publicKeys: { keyData: string }[] } }?, networkProfile: { dnsServiceIP: string?, dockerBridgeCidr: string?, loadBalancerProfile: { allocatedOutboundPorts: int?, effectiveOutboundIPs: { id: string? }[]?, idleTimeoutInMinutes: int?, managedOutboundIPs: { count: int? }?, outboundIPPrefixes: { publicIPPrefixes: { id: string? }[]? }?, outboundIPs: { publicIPs: { id: string? }[]? }? }?, loadBalancerSku: ('basic' | 'standard' | string)?, networkMode: ('bridge' | 'transparent' | string)?, networkPlugin: ('azure' | 'kubenet' | string)?, networkPolicy: ('azure' | 'calico' | string)?, outboundType: ('loadBalancer' | 'userDefinedRouting' | string)?, podCidr: string?, serviceCidr: string? }?, nodeResourceGroup: string?, podIdentityProfile: { allowNetworkPluginKubenet: bool?, enabled: bool?, userAssignedIdentities: { bindingSelector: string?, identity: { clientId: string?, objectId: string?, resourceId: string? }, name: string, namespace: string }[]?, userAssignedIdentityExceptions: { name: string, namespace: string, podLabels: object }[]? }?, privateLinkResources: { groupId: string?, id: string?, name: string?, requiredMembers: string[]?, type: string? }[]?, servicePrincipalProfile: { clientId: string, secret: string? }?, windowsProfile: { adminPassword: string?, adminUsername: string, enableCSIProxy: bool?, licenseType: ('None' | 'Windows_Server' | string)? }? }?, scale: { capacity: int, maximum: int, minimum: int }, sku: { name: ('Basic' | string)?, tier: ('Free' | 'Paid' | string)? }?, tags: object?, zones: string[] } = aksCluster
    //[DataRow("""
    //        var a = <<aksCluster>>
    //        resource aksCluster 'Microsoft.ContainerService/managedClusters@2021-03-01' = { }
    //        """,
    //    """
    //        param newParameter resource 'Microsoft.ContainerService/managedClusters@2021-03-01' = aksCluster
    //        var a = newParameter
    //        resource aksCluster 'Microsoft.ContainerService/managedClusters@2021-03-01' = { }
    //        """,
    //      """
    //        param newParameter resource 'Microsoft.ContainerService/managedClusters@2021-03-01' = aksCluster
    //        var a = newParameter
    //        resource aksCluster 'Microsoft.ContainerService/managedClusters@2021-03-01' = { }
    //        """,
    //    DisplayName = "resource type")]
    [DataRow(
        """
            resource peering 'Microsoft.Network/virtualNetworks/virtualNetworkPeerings@2020-07-01' = {
                name: |'virtualNetwork/name'
                properties: {
                    allowVirtualNetworkAccess: true
                    remoteVirtualNetwork: {
                        id: virtualNetworksId
                    }
                }
            }
            """,
        """
            @description('The resource name')
            param name string = 'virtualNetwork/name'
            resource peering 'Microsoft.Network/virtualNetworks/virtualNetworkPeerings@2020-07-01' = {
                name: name
                properties: {
                    allowVirtualNetworkAccess: true
                    remoteVirtualNetwork: {
                        id: virtualNetworksId
                    }
                }
            }
            """,
        null,
        DisplayName = "resource types - string property")]
    [DataRow(
        """
            resource storageaccount 'Microsoft.Storage/storageAccounts@2021-02-01' = {
                name: 'name'
                location: 'location'
                kind: 'StorageV2'
                sku: {
                    name: |'Premium_LRS'
                }
            }
            """,
        """
            @description('The SKU name. Required for account creation; optional for update. Note that in older versions, SKU name was called accountType.')
            param name string = 'Premium_LRS'
            resource storageaccount 'Microsoft.Storage/storageAccounts@2021-02-01' = {
                name: 'name'
                location: 'location'
                kind: 'StorageV2'
                sku: {
                    name: name
                }
            }
            """,
        """
            @description('The SKU name. Required for account creation; optional for update. Note that in older versions, SKU name was called accountType.')
            param name 'Premium_LRS' | 'Premium_ZRS' | 'Standard_GRS' | 'Standard_GZRS' | 'Standard_LRS' | 'Standard_RAGRS' | 'Standard_RAGZRS' | 'Standard_ZRS' | string = 'Premium_LRS'
            resource storageaccount 'Microsoft.Storage/storageAccounts@2021-02-01' = {
                name: 'name'
                location: 'location'
                kind: 'StorageV2'
                sku: {
                    name: name
                }
            }
            """,
        DisplayName = "resource properties - string union")]
    [DataRow(
        """
            param p int?
            var v = |p
            """,
        """
            param p int?
            param newParameter int = p
            var v = newParameter
            """,
        null,
        DisplayName = "nullable types")]
    [DataRow(
        """
            param whoops int = 'not an int'
            var v = <<p + 1>>
            """,
        """
            param whoops int = 'not an int'
            param newParameter object? /* unknown */ = p + 1
            var v = newParameter
            """,
        null,
        DisplayName = "error types")]
    [DataRow(
        """
            param p1 { a: { b: string } }
            var v = |p1
            """,
        """
            param p1 { a: { b: string } }
            param newParameter object = p1
            var v = newParameter
            """,
        """
            param p1 { a: { b: string } }
            param newParameter { a: { b: string } } = p1
            var v = newParameter
            """)]
    [DataTestMethod]
    public async Task Params_InferType(string fileWithSelection, string expectedMediumParameterText, string expectedStrictParameterText)
    {
        await RunExtractToParameterTest(fileWithSelection, expectedMediumParameterText, expectedStrictParameterText);
    }

    ////////////////////////////////////////////////////////////////////

    [TestMethod]
    public async Task IfJustPropertyNameSelected_ThenExtractPropertyValue()
    {
        await RunExtractToParameterTest("""
            var isWindowsOS = true
            var provisionExtensions = true
            param _artifactsLocation string
            @secure()
            param _artifactsLocationSasToken string

            resource resourceWithProperties 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' = if (isWindowsOS && provisionExtensions) {
                name: 'cse-windows/extension'
                location: 'location'
                properties: {
                    publisher: 'Microsoft.Compute'
                    type: 'CustomScriptExtension'
                    typeHandlerVersion: '1.8'
                    autoUpgradeMinorVersion: true
                    setting|s: { // Property key selected - extract just the value
                        fileUris: [
                            uri(_artifactsLocation, 'writeblob.ps1${_artifactsLocationSasToken}')
                        ]
                        commandToExecute: 'commandToExecute'
                    }
                }
            }
            """,
        """
            var isWindowsOS = true
            var provisionExtensions = true
            param _artifactsLocation string
            @secure()
            param _artifactsLocationSasToken string
            @description('Json formatted public settings for the extension.')
            param settings object = {
              // Property key selected - extract just the value
              fileUris: [
                uri(_artifactsLocation, 'writeblob.ps1${_artifactsLocationSasToken}')
              ]
              commandToExecute: 'commandToExecute'
            }

            resource resourceWithProperties 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' = if (isWindowsOS && provisionExtensions) {
                name: 'cse-windows/extension'
                location: 'location'
                properties: {
                    publisher: 'Microsoft.Compute'
                    type: 'CustomScriptExtension'
                    typeHandlerVersion: '1.8'
                    autoUpgradeMinorVersion: true
                    settings: settings
                }
            }
            """,
        "IGNORE");
    }

    ////////////////////////////////////////////////////////////////////

    [DataTestMethod]
    [DataRow(
        """
            resource vmName_resource 'Microsoft.Compute/virtualMachines@2019-12-01' = {
              name: vmName
              location: location
              properties: {
                osProfile: {
                  computerName: vmName
                  myproperty: {
                    abc: [
                      {
                        def: [
                          'ghi'
                          '|jkl'
                        ]
                      }
                    ]
                  }
                }
              }
            }
            """,
        """
            var newVariable = 'jkl'
            resource vmName_resource 'Microsoft.Compute/virtualMachines@2019-12-01' = {
              name: vmName
              location: location
              properties: {
                osProfile: {
                  computerName: vmName
                  myproperty: {
                    abc: [
                      {
                        def: [
                          'ghi'
                          newVariable
                        ]
                      }
                    ]
                  }
                }
              }
            }
            """,
        DisplayName = "Array element, don't pick up property name")]
    [DataRow(
    """
            resource vmName_resource 'Microsoft.Compute/virtualMachines@2019-12-01' = {
                name: vmName
                location: location
                properties: {
                osProfile: {
                    computerName: vmName
                    myproperty: {
                    abc: <<[
                        {
                        def: [
                            'ghi'
                            'jkl'
                        ]
                        }
                    ]>>
                    }
                }
                }
            }
            """,
        """
            var abc = [
              {
                def: [
                  'ghi'
                  'jkl'
                ]
              }
            ]
            resource vmName_resource 'Microsoft.Compute/virtualMachines@2019-12-01' = {
                name: vmName
                location: location
                properties: {
                osProfile: {
                    computerName: vmName
                    myproperty: {
                    abc: abc
                    }
                }
                }
            }
            """,
        DisplayName = "Full property value as array, pick up property name")]
    public async Task ShouldPickUpPropertyName_ButOnlyIfFullPropertyValue(string fileWithSelection, string? expectedVarText)
    {
        await RunExtractToVariableTest(fileWithSelection, expectedVarText);
    }

    ////////////////////////////////////////////////////////////////////

    [DataTestMethod]
    [DataRow(
        "var a = resourceGroup().locati|on",
        """
            var resourceGroupLocation = resourceGroup().location
            var a = resourceGroupLocation
            """)]
    [DataRow(
        "var a = abc|().bcd",
        """
            var newVariable = abc()
            var a = newVariable.bcd
            """)]
    [DataRow(
        "var a = abc.bcd.|def",
        """
            var bcdDef = abc.bcd.def
            var a = bcdDef
            """)]
    [DataRow(
        "var a = abc.b|cd",
        """
            var abcBcd = abc.bcd
            var a = abcBcd
            """)]
    [DataRow(
        "var a = abc.bc|d",
        """
            var abcBcd = abc.bcd
            var a = abcBcd
            """)]
    [DataRow(
        "var a = reference(storageAccount.id, '2018-02-01').primaryEndpoints.blob|",
        """
            var primaryEndpointsBlob = reference(storageAccount.id, '2018-02-01').primaryEndpoints.blob
            var a = primaryEndpointsBlob
            """)]
    [DataRow(
        "var a = reference(storageAccount.id, '2018-02-01').prim|aryEndpoints.blob",
        """
            var referencePrimaryEndpoints = reference(storageAccount.id, '2018-02-01').primaryEndpoints
            var a = referencePrimaryEndpoints.blob
            """)]
    [DataRow(
        "var a = a.b.|c.d.e",
        """
            var bC = a.b.c
            var a = bC.d.e
            """)]
    public async Task PickUpNameFromPropertyAccess_UpToTwoLevels(string fileWithSelection, string? expectedVariableText)
    {
        await RunExtractToVariableTest(fileWithSelection, expectedVariableText);
    }

    ////////////////////////////////////////////////////////////////////

    [DataTestMethod]
    //
    // Closest ancestor expression is the top-level expression itself -> offer to update full expression
    //
    [DataRow(
        "storageUri:| reference(storageAccount.id, '2018-02-01').primaryEndpoints.blob",
        "var storageUri = reference(storageAccount.id, '2018-02-01').primaryEndpoints.blob",
        "storageUri: storageUri"
        )]
    [DataRow(
        "storageUri: reference(storageAccount.id, '2018-02-01').primaryEndpoints.|blob",
        "var storageUri = reference(storageAccount.id, '2018-02-01').primaryEndpoints.blob",
        "storageUri: storageUri"
        )]
    [DataRow(
        "storageUri: reference(storageAccount.id, '2018-02-01').primaryEndpoints.<<blo>>b",
        "var storageUri = reference(storageAccount.id, '2018-02-01').primaryEndpoints.blob",
        "storageUri: storageUri"
        )]
    //
    // Cursor is inside the property name -> offer full expression
    //
    [DataRow(
        "storageUri|: reference(storageAccount.id, '2018-02-01').primaryEndpoints.blob",
        "var storageUri = reference(storageAccount.id, '2018-02-01').primaryEndpoints.blob",
        "storageUri: storageUri"
        )]
    [DataRow(
        "<<storageUri: re>>ference(storageAccount.id, '2018-02-01').primaryEndpoints.blob",
        "var storageUri = reference(storageAccount.id, '2018-02-01').primaryEndpoints.blob",
        "storageUri: storageUri"
        )]
    [DataRow(
        "<<storageUri: reference(storageAccount.id, '2018-02-01').primaryEndpoints.blob>>",
        "var storageUri = reference(storageAccount.id, '2018-02-01').primaryEndpoints.blob",
        "storageUri: storageUri"
        )]
    //
    // Cursor is inside a subexpression -> only offer to extract that specific subexpression
    //
    // ... reference() call
    [DataRow(
        "storageUri: reference(storageAccount.id, '2018-02-01').|primaryEndpoints.blob",
        "var referencePrimaryEndpoints = reference(storageAccount.id, '2018-02-01').primaryEndpoints",
        "storageUri: referencePrimaryEndpoints.blob"
        )]
    [DataRow(
        "storageUri: reference|(storageAccount.id, '2018-02-01').primaryEndpoints.blob",
        "var newVariable = reference(storageAccount.id, '2018-02-01')",
        "storageUri: newVariable.primaryEndpoints.blob"
        )]
    [DataRow(
        "storageUri: refere<<nce(storageAccount.id, '201>>8-02-01').primaryEndpoints.blob",
        "var newVariable = reference(storageAccount.id, '2018-02-01')",
        "storageUri: newVariable.primaryEndpoints.blob"
        )]
    //   ... '2018-02-01'
    [DataRow(
        "storageUri: reference(storageAccount.id, |'2018-02-01').primaryEndpoints.blob",
        "var newVariable = '2018-02-01'",
        "storageUri: reference(storageAccount.id, newVariable).primaryEndpoints.blob"
        )]
    [DataRow(
        "storageUri: reference(storageAccount.id, '2018-02-01|').primaryEndpoints.blob",
        "var newVariable = '2018-02-01'",
        "storageUri: reference(storageAccount.id, newVariable).primaryEndpoints.blob"
        )]
    //   ... storageAccount.id
    [DataRow(
        "storageUri: reference(storageAccount.|id, '2018-02-01').primaryEndpoints.blob",
        "var storageAccountId = storageAccount.id",
        "storageUri: reference(storageAccountId, '2018-02-01').primaryEndpoints.blob"
        )]
    [DataRow(
        "storageUri: reference(storageAccount.i|d, '2018-02-01').primaryEndpoints.blob",
        "var storageAccountId = storageAccount.id",
        "storageUri: reference(storageAccountId, '2018-02-01').primaryEndpoints.blob"
        )]
    // ... storageAccount
    [DataRow(
        "storageUri: reference(storageAc|count.id, '2018-02-01').primaryEndpoints.blob",
        "var newVariable = storageAccount",
        "storageUri: reference(newVariable.id, '2018-02-01').primaryEndpoints.blob"
        )]
    [DataRow(
        "storageUri: reference(storageAc|count.id, '2018-02-01').primaryEndpoints.blob",
        "var newVariable = storageAccount",
        "storageUri: reference(newVariable.id, '2018-02-01').primaryEndpoints.blob"
        )]
    [DataRow(
        "storageUri: reference(storageAc|count.id, '2018-02-01').primaryEndpoints.blob",
        "var newVariable = storageAccount",
        "storageUri: reference(newVariable.id, '2018-02-01').primaryEndpoints.blob"
        )]
    // ... inside reference(x, y) but not inside x or y -> closest enclosing expression is the reference()
    [DataRow(
        "storageUri: reference(storageAccount.id,| '2018-02-01').primaryEndpoints.blob",
        "var newVariable = reference(storageAccount.id, '2018-02-01')",
        "storageUri: newVariable.primaryEndpoints.blob"
        )]
    [DataRow(
        "storageUri: reference(storageAccount.id, '2018-02-01' |).primaryEndpoints.blob",
        "var newVariable = reference(storageAccount.id, '2018-02-01')",
        "storageUri: newVariable.primaryEndpoints.blob"
        )]
    [DataRow(
        "storageUri: reference|(storageAccount.id, '2018-02-01').primaryEndpoints.blob",
        "var newVariable = reference(storageAccount.id, '2018-02-01')",
        "storageUri: newVariable.primaryEndpoints.blob"
        )]
    public async Task ShouldExpandSelectedExpressionsInALogicalWay(string lineWithSelection, string expectedNewVarDeclaration, string expectedModifiedLine)
    {
        await RunExtractToVarSingleLineTest(
            inputTemplateWithSelection: """
            resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = { name: 'storageaccountname' }

            resource vm 'Microsoft.Compute/virtualMachines@2019-12-01' = { name: 'vm', location: 'eastus'
              properties: {
                diagnosticsProfile: {
                  bootDiagnostics: {
                    LINEWITHSELECTION
                  }
                }
              }
            }
            """,
            expectedOutputTemplate: """
            resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = { name: 'storageaccountname' }

            EXPECTEDNEWDECLARATION
            resource vm 'Microsoft.Compute/virtualMachines@2019-12-01' = { name: 'vm', location: 'eastus'
              properties: {
                diagnosticsProfile: {
                  bootDiagnostics: {
                    EXPECTEDMODIFIEDLINE
                  }
                }
              }
            }
            """,
            lineWithSelection,
            expectedNewVarDeclaration,
            expectedModifiedLine);
    }

    ////////////////////////////////////////////////////////////////////

    [DataTestMethod]
    [DataRow(
        "storageUri: reference(stora<<geAccount.i>>d, '2018-02-01').primaryEndpoints.blob",
        "var storageAccountId = storageAccount.id",
        "param storageAccountId string = storageAccount.id",
        null,
        "storageUri: reference(storageAccountId, '2018-02-01').primaryEndpoints.blob"
        )]
    [DataRow(
        "storageUri: refer<<ence(storageAccount.id, '2018-02-01').primaryEndpoints.bl>>ob",
        "var storageUri = reference(storageAccount.id, '2018-02-01').primaryEndpoints.blob",
        """
            @description('Uri of the storage account to use for placing the console output and screenshot.')
            param storageUri object? /* unknown */ = reference(storageAccount.id, '2018-02-01').primaryEndpoints.blob
            """,
        null,
        "storageUri: storageUri"
        )]
    [DataRow(
        "storageUri: reference(storageAccount.id, '2018-02-01').primar<<yEndpoints.blob>>",
        "var storageUri = reference(storageAccount.id, '2018-02-01').primaryEndpoints.blob",
        """
            @description('Uri of the storage account to use for placing the console output and screenshot.')
            param storageUri object? /* unknown */ = reference(storageAccount.id, '2018-02-01').primaryEndpoints.blob
            """,
        null,
        "storageUri: storageUri"
        )]
    public async Task IfThereIsASelection_ThenPickUpEverythingInTheSelection_AfterExpanding(string lineWithSelection, string expectedNewVarDeclaration, string expectedNewParamLooseDeclaration, string expectedNewParamMediumDeclaration, string expectedModifiedLine)
    {
        await RunExtractToVarAndParamOnSingleLineTest(
            inputTemplateWithSelection: """
                    resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = { name: 'storageaccountname' }

                    resource vm 'Microsoft.Compute/virtualMachines@2019-12-01' = { name: 'vm', location: 'eastus'
                      properties: {
                        diagnosticsProfile: {
                          bootDiagnostics: {
                            LINEWITHSELECTION
                          }
                        }
                      }
                    }
                    """,
            expectedOutputTemplate: """
                    resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = { name: 'storageaccountname' }

                    EXPECTEDNEWDECLARATION
                    resource vm 'Microsoft.Compute/virtualMachines@2019-12-01' = { name: 'vm', location: 'eastus'
                      properties: {
                        diagnosticsProfile: {
                          bootDiagnostics: {
                            EXPECTEDMODIFIEDLINE
                          }
                        }
                      }
                    }
                    """,
            lineWithSelection,
            expectedNewVarDeclaration,
            expectedNewParamLooseDeclaration,
            expectedNewParamMediumDeclaration,
            expectedModifiedLine);
    }

    ////////////////////////////////////////////////////////////////////

    [DataTestMethod]
    [DataRow(
        """
            // My comment here
            resource cassandraKeyspace 'Microsoft.DocumentDB/databaseAccounts/cassandraKeyspaces@2021-06-15' = {
                name: 'testResource/cassandraKeyspace'
                properties: {
                resource: {
                    id: 'id'
                }
                <<options>>: {}
                }
            }
            """,
        "IGNORE",
        """
            // My comment here
            @description('A key-value pair of options to be applied for the request. This corresponds to the headers sent with the request.')
            param options { autoscaleSettings: { maxThroughput: int? }?, throughput: int? } = {}
            resource cassandraKeyspace 'Microsoft.DocumentDB/databaseAccounts/cassandraKeyspaces@2021-06-15' = {
                name: 'testResource/cassandraKeyspace'
                properties: {
                resource: {
                    id: 'id'
                }
                options: options
                }
            }
            """,
        DisplayName = "Resource property description")]
    [DataRow(
        """
            type t = {
                @description('My string\'s field')
                myString: string

                @description('''
            My int's field
            is very long
            ''')
                myInt: int
            }

            param p t = {
                myString: |'hello'
                myInt: 42
            }
            """,
        """
            type t = {
                @description('My string\'s field')
                myString: string

                @description('''
            My int's field
            is very long
            ''')
                myInt: int
            }

            @description('My string\'s field')
            param myString string = 'hello'
            param p t = {
                myString: myString
                myInt: 42
            }
            """,
        null,
        DisplayName = "Apostrophe in description")]
    [DataRow(
        """
            type t = {
                @description('My string\'s field')
                myString: string

                @description('''
            My int's field
            is very long
            ''')
                myInt: int
            }

            param p t = {
                myString: 'hello'
                myInt: |42
            }
            """,
        """
            type t = {
                @description('My string\'s field')
                myString: string

                @description('''
            My int's field
            is very long
            ''')
                myInt: int
            }

            @description('My int\'s field\nis very long\n')
            param myInt int = 42
            param p t = {
                myString: 'hello'
                myInt: myInt
            }
            """,
        null,
        DisplayName = "multiline description")]
    public async Task Params_ShouldPickUpDescriptions(string fileWithSelection, string expectedLooseParamText, string? expectedMediumParamText)
    {
        await RunExtractToParameterTest(fileWithSelection, expectedLooseParamText, expectedMediumParamText);
    }

    ////////////////////////////////////////////////////////////////////

    public async Task asdfg(string fileWithSelection, string expectedLooseParamText, string? expectedMediumParamText)
    {
        await RunExtractToParameterTest(fileWithSelection, expectedLooseParamText, expectedMediumParamText);
    }

    ////////////////////////////////////////////////////////////////////

    [DataTestMethod]
    //asdfg
    //[DataRow(
    //    """
    //        var v = <<1>>
    //        """,
    //    """
    //        var newVariable = 1
    //        var v = newVariable
    //        """,
    //    """
    //        param newParameter int = 1
    //        var v = newParameter
    //        """,
    //    DisplayName = "Extracting at top of file -> insert at top")]
    //[DataRow(
    //    """
    //        metadata firstLine = 'first line'
    //        metadata secondLine = 'second line'

    //        // Some comments
    //        var v = <<1>>
    //        """,
    //    """
    //        metadata firstLine = 'first line'
    //        metadata secondLine = 'second line'

    //        // Some comments
    //        var newVariable = 1
    //        var v = newVariable
    //        """,
    //    """
    //        metadata firstLine = 'first line'
    //        metadata secondLine = 'second line'

    //        // Some comments
    //        param newParameter int = 1
    //        var v = newParameter
    //        """,
    //    DisplayName = "No existing params/vars above -> insert right before extraction line")]
    [DataRow(
        """
            param location string
            param resourceGroup string
            var simpleCalculation = 1 + 1
            var complexCalculation = simpleCalculation * 2

            metadata line = 'line'

            var v = <<1>>
            """,
        """
            param location string
            param resourceGroup string
            var simpleCalculation = 1 + 1
            var complexCalculation = simpleCalculation * 2
            var newVariable = 1

            metadata line = 'line'

            var v = newVariable
            """,
        """
            param location string
            param resourceGroup string
            param newParameter int = 1
            var simpleCalculation = 1 + 1
            var complexCalculation = simpleCalculation * 2

            metadata line = 'line'

            var v = newParameter
            """,
        DisplayName = "Existing params and vars at top of file -> param and var inserted after their corresponding existing declarations")]
    //[DataRow(//asdfg not handling comments before line as part of the line
    //    """
    //        // location comment
    //        param location string
    //        // rg comment
    //        param resourceGroup string
    //        var simpleCalculation = 1 + 1
    //        var complexCalculation = simpleCalculation * 2

    //        metadata line = 'line'

    //        var v = <<1>>
    //        """,
    //    """
    //        // location comment
    //        param location string
    //        // rg comment
    //        param resourceGroup string
    //        var simpleCalculation = 1 + 1
    //        var complexCalculation = simpleCalculation * 2
    //        var newVariable = 1

    //        metadata line = 'line'

    //        var v = newVariable
    //        """,
    //    """
    //        // location comment
    //        param location string
    //        // rg comment
    //        param resourceGroup string
    //        param newParameter int = 1
    //        var simpleCalculation = 1 + 1
    //        var complexCalculation = simpleCalculation * 2

    //        metadata line = 'line'

    //        var v = newParameter
    //        """,
    //    DisplayName = "Existing params and vars at top of file -> param and var inserted after their corresponding existing declarations")]
    //[DataRow(
    //    //asdfg
    //    """
    //        // location comment
    //        param location string

    //        // rg comment
    //        param resourceGroup string

    //        var simpleCalculation = 1 + 1

    //        @export()
    //        @description('this still counts as having an empty line beforehand')
    //        var complexCalculation = simpleCalculation * 2

    //        metadata line = 'line'

    //        var v = <<1>>
    //        """,
    //    """
    //        // location comment
    //        param location string

    //        // rg comment
    //        param resourceGroup string

    //        var simpleCalculation = 1 + 1

    //        @export()
    //        @description('this still counts as having an empty line beforehand')
    //        var complexCalculation = simpleCalculation * 2

    //        var newVariable = 1

    //        metadata line = 'line'

    //        var v = newVariable
    //        """,
    //    """
    //        // location comment
    //        param location string

    //        // rg comment
    //        param resourceGroup string

    //        param newParameter int = 1

    //        var simpleCalculation = 1 + 1

    //        @export()
    //        @description('this still counts as having an empty line beforehand')
    //        var complexCalculation = simpleCalculation * 2

    //        metadata line = 'line'

    //        var v = newParameter
    //        """,
    //    DisplayName = "If closest existing declaration has a blank line before it, insert a blank line above the new declaration")]
    //[DataRow(
    //    """
    //        param location string
    //        param resourceGroup string
    //        var simpleCalculation = 1 + 1
    //        var complexCalculation = simpleCalculation * 2

    //        metadata line = 'line'

    //        param location2 string
    //        param resourceGroup2 string
    //        var simpleCalculation2 = 1 + 1
    //        var complexCalculation2 = simpleCalculation * 2

    //        metadata line2 = 'line2'

    //        var v = <<1>>

    //        param location3 string
    //        param resourceGroup3 string
    //        var simpleCalculation3 = 1 + 1
    //        var complexCalculation3 = simpleCalculation * 2            
    //        """,
    //    """
    //        param location string
    //        param resourceGroup string
    //        var simpleCalculation = 1 + 1
    //        var complexCalculation = simpleCalculation * 2

    //        metadata line = 'line'

    //        param location2 string
    //        param resourceGroup2 string
    //        var simpleCalculation2 = 1 + 1
    //        var complexCalculation2 = simpleCalculation * 2
    //        var newVariable = 1

    //        metadata line2 = 'line2'

    //        var v = newVariable

    //        param location3 string
    //        param resourceGroup3 string
    //        var simpleCalculation3 = 1 + 1
    //        var complexCalculation3 = simpleCalculation * 2            
    //        """,
    //    """
    //        param location string
    //        param resourceGroup string
    //        var simpleCalculation = 1 + 1
    //        var complexCalculation = simpleCalculation * 2

    //        metadata line = 'line'

    //        param location2 string
    //        param resourceGroup2 string
    //        param newParameter int = 1
    //        var simpleCalculation2 = 1 + 1
    //        var complexCalculation2 = simpleCalculation * 2

    //        metadata line2 = 'line2'

    //        var v = newParameter

    //        param location3 string
    //        param resourceGroup3 string
    //        var simpleCalculation3 = 1 + 1
    //        var complexCalculation3 = simpleCalculation * 2            
    //        """,
    //    DisplayName = "Existing params and vars in multiple places in file -> insert after closest existing declarations above extraction")]
    //[DataRow(
    //    """
    //        param location string

    //        resource virtualMachine 'Microsoft.Compute/virtualMachines@2020-12-01' = {
    //          name: 'name'
    //          location: location
    //        }

    //        resource windowsVMExtensions 'Microsoft.Compute/virtualMachines/extensions@2020-12-01' = {
    //          parent: virtualMachine
    //          name: 'name'
    //          location: location
    //          properties: {
    //            publisher: 'Microsoft.Compute'
    //            type: 'CustomScriptExtension'
    //            typeHandlerVersion: '1.10'
    //            autoUpgradeMinorVersion: true
    //            settings: {
    //              fileUris: [
    //                'fileUris'
    //              ]
    //            }
    //            <<protectedSettings>>: {
    //              commandToExecute: 'loadTextContent(\'files/my script.ps1\')'
    //            }
    //          }
    //        }
    //        """,
    //    "IGNORE",
    //    """
    //        param location string
    //        @description('The extension can contain either protectedSettings or protectedSettingsFromKeyVault or no protected settings at all.')
    //        param protectedSettings object = {
    //          commandToExecute: 'loadTextContent(\'files/my script.ps1\')'
    //        }

    //        resource virtualMachine 'Microsoft.Compute/virtualMachines@2020-12-01' = {
    //          name: 'name'
    //          location: location
    //        }

    //        resource windowsVMExtensions 'Microsoft.Compute/virtualMachines/extensions@2020-12-01' = {
    //          parent: virtualMachine
    //          name: 'name'
    //          location: location
    //          properties: {
    //            publisher: 'Microsoft.Compute'
    //            type: 'CustomScriptExtension'
    //            typeHandlerVersion: '1.10'
    //            autoUpgradeMinorVersion: true
    //            settings: {
    //              fileUris: [
    //                'fileUris'
    //              ]
    //            }
    //            protectedSettings: protectedSettings
    //          }
    //        }
    //        """,
    //    DisplayName = "get the rename position correct")]
    public async Task VarsAndParams_InsertAfterExistingDeclarations(string fileWithSelection, string expectedVarText, string? expectedParamText)
    {
        await RunExtractToVariableAndParameterTest(fileWithSelection.ReplaceNewlines("\n"), expectedVarText, expectedParamText, "IGNORE");
        await RunExtractToVariableAndParameterTest(fileWithSelection.ReplaceNewlines("\r\n"), expectedVarText, expectedParamText, "IGNORE");
    }

    #region Support

    private async Task RunExtractToVarSingleLineTest(
        string inputTemplateWithSelection,
        string expectedOutputTemplate,
        string lineWithSelection,
        string? expectedNewVarDeclaration,
        string expectedModifiedLine)
    {
        await RunExtractToVariableTest(
            inputTemplateWithSelection.Replace("LINEWITHSELECTION", lineWithSelection),
            expectedOutputTemplate.Replace("EXPECTEDNEWDECLARATION", expectedNewVarDeclaration)
                .Replace("EXPECTEDMODIFIEDLINE", expectedModifiedLine));
    }

    private async Task RunExtractToParamSingleLineTest(
        string inputTemplateWithSelection,
        string expectedOutputTemplate,
        string lineWithSelection,
        string expectedNewParamLooseDeclaration,
        string? expectedNewParamMediumDeclaration,
        string expectedModifiedLine)
    {
        await RunExtractToParameterTest(
            inputTemplateWithSelection.Replace("LINEWITHSELECTION", lineWithSelection),
            expectedOutputTemplate.Replace("EXPECTEDNEWDECLARATION", expectedNewParamLooseDeclaration)
                .Replace("EXPECTEDMODIFIEDLINE", expectedModifiedLine),
            expectedNewParamMediumDeclaration == null ? null :
                expectedOutputTemplate.Replace("EXPECTEDNEWDECLARATION", expectedNewParamMediumDeclaration)
                    .Replace("EXPECTEDMODIFIEDLINE", expectedModifiedLine)
        );
    }

    private async Task RunExtractToVarAndParamOnSingleLineTest(
        string inputTemplateWithSelection,
        string expectedOutputTemplate,
        string lineWithSelection,
        string expectedNewVarDeclaration,
        string expectedNewParamLooseDeclaration,
        string? expectedNewParamMediumDeclaration,
        string expectedModifiedLine
        )
    {
        await RunExtractToVarSingleLineTest(
            inputTemplateWithSelection,
            expectedOutputTemplate,
            lineWithSelection,
            expectedNewVarDeclaration,
            expectedModifiedLine);

        await RunExtractToParamSingleLineTest(
            inputTemplateWithSelection,
            expectedOutputTemplate,
            lineWithSelection,
            expectedNewParamLooseDeclaration,
            expectedNewParamMediumDeclaration,
            expectedModifiedLine);
    }

private async Task RunExtractToVariableAndParameterTest(string fileWithSelection, string? expectedVariableText, string? expectedLooseParamText, string? expectedMediumParamText)
{
    await RunExtractToVariableTest(
        fileWithSelection,
        expectedVariableText);
    await RunExtractToParameterTest(
        fileWithSelection,
        expectedLooseParamText,
        expectedMediumParamText);
}

private async Task RunExtractToVariableTest(string fileWithSelection, string? expectedText)
{
    (var codeActions, var bicepFile) = await GetCodeActionsForSyntaxTest(fileWithSelection);
    var extractedVar = codeActions.FirstOrDefault(x => x.Title.StartsWith(ExtractToVariableTitle));

    if (expectedText == null)
    {
        extractedVar.Should().BeNull("expected no code action for extract var");
    }
    else if (expectedText != "IGNORE")
    {
        extractedVar.Should().NotBeNull("expected an action to extract to variable");
        extractedVar!.Kind.Should().Be(CodeActionKind.RefactorExtract);

        var updatedFile = ApplyCodeAction(bicepFile, extractedVar);
        updatedFile.Should().HaveSourceText(expectedText, "extract to variable should match expected outcome");
    }
}

// expectedMediumParameterText can be "SAME" or "IGNORE"
private async Task RunExtractToParameterTest(string fileWithSelection, string? expectedLooseParameterText, string? expectedMediumParameterText)
{
    if (expectedMediumParameterText == "SAME")
    {
        expectedMediumParameterText = expectedLooseParameterText;
    }

    (var codeActions, var bicepFile) = await GetCodeActionsForSyntaxTest(fileWithSelection);
    var extractedParamFixes = codeActions.Where(x => x.Title.StartsWith(ExtractToParameterTitle)).ToArray();
    extractedParamFixes.Length.Should().BeLessThanOrEqualTo(2);

    if (expectedLooseParameterText == null)
    {
        extractedParamFixes.Should().BeEmpty("expected no code actions to extract parameter");
        expectedMediumParameterText.Should().BeNull();
    }
    else
    {
        if (expectedLooseParameterText != "IGNORE")
        {
            extractedParamFixes.Should().HaveCountGreaterThanOrEqualTo(1).Should().NotBeNull("expected at least one code action to extract to parameter");
            var looseFix = extractedParamFixes[0];
            looseFix.Kind.Should().Be(CodeActionKind.RefactorExtract);

            var updatedFileLoose = ApplyCodeAction(bicepFile, looseFix);
            updatedFileLoose.Should().HaveSourceText(expectedLooseParameterText, "extract to param with loose typing should match expected outcome");
        }

        if (expectedMediumParameterText == null)
        {
            extractedParamFixes.Length.Should().Be(1, "expected only one code action to extract parameter (as loosely typed - which means the medium-strict version was the same as the loose version)");
        }
        else
        {
            if (expectedMediumParameterText != "IGNORE")
            {
                extractedParamFixes.Length.Should().Be(2, "expected a second option to extract to parameter");

                var mediumFix = extractedParamFixes[1];
                mediumFix.Kind.Should().Be(CodeActionKind.RefactorExtract);

                var updatedFileMedium = ApplyCodeAction(bicepFile, mediumFix);
                updatedFileMedium.Should().HaveSourceText(expectedMediumParameterText, "extract to param with medium-strict typing should match expected outcome");
            }
        }
    }
}
}

#endregion
