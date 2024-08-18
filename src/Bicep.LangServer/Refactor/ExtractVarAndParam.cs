// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Bicep.Core;
using Bicep.Core.CodeAction;
using Bicep.Core.Extensions;
using Bicep.Core.Parsing;
using Bicep.Core.PrettyPrintV2;
using Bicep.Core.Semantics;
using Bicep.Core.Syntax;
using Bicep.Core.Text;
using Bicep.Core.TypeSystem;
using Bicep.Core.TypeSystem.Types;
using Bicep.LanguageServer.CompilationManager;
using Bicep.LanguageServer.Completions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using static Bicep.LanguageServer.Completions.BicepCompletionContext;
using static Bicep.LanguageServer.Refactor.StringizeType;
using static Google.Protobuf.Reflection.ExtensionRangeOptions.Types;

namespace Bicep.LanguageServer.Refactor
{
    // asdfg Convert var to param

    /*
    Nullable-typed parameters may not be assigned default values. They have an implicit default of 'null' that cannot be overridden.bicep(BCP326):

        var commandToExecute = 'powershell -ExecutionPolicy Unrestricted -File writeblob.ps1'
    resource testResource 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' {
                    properties: {
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
                }

    EXTRACT settings:

        var commandToExecute = 'powershell -ExecutionPolicy Unrestricted -File writeblob.ps1'

param settings { commandToExecute: string, fileUris: string[] }? = {
  fileUris: [
    uri(_artifactsLocation, 'writeblob.ps1${_artifactsLocationSasToken}')
  ]
  commandToExecute: commandToExecute
}
resource testResource 'Microsoft.Compute/virtualMachines/extensions@2019-12-01' {
                properties: {
                    publisher: 'Microsoft.Compute'
                    type: 'CustomScriptExtension'
                    typeHandlerVersion: '1.8'
                    autoUpgradeMinorVersion: true
                    settings: settings
                }
            }

    */

    // Provides code actions/fixes for a range in a Bicep document
    public static class ExtractVarAndParam
{
    private const int MaxExpressionLengthInCodeAction = 75;

    static string NewLine(SemanticModel semanticModel) => semanticModel.Configuration.Formatting.Data.NewlineKind.ToEscapeSequence();

    public static IEnumerable<CodeFix> GetSuggestions(
        CompilationContext compilationContext,
        Compilation compilation,
        SemanticModel semanticModel,
        List<SyntaxBase> nodesInRange)
    {
        if (SyntaxMatcher.FindLastNodeOfType<ExpressionSyntax, ExpressionSyntax>(nodesInRange) is not (ExpressionSyntax expressionSyntax, _))
        {
            yield break;
        }

        PrintAllTypes(semanticModel);

        TypeProperty? typeProperty = null; // asdfg better name
        string? defaultNewName = null;

        // Semi-intelligent default names for new variable
        if (semanticModel.Binder.GetParent(expressionSyntax) is ObjectPropertySyntax propertySyntax
            && propertySyntax.TryGetKeyText() is string propertyName)
        {
            // `{ objectPropertyName: <<expression>> }` // entire property value expression selected
            //   -> default to the name "objectPropertyName"
            defaultNewName = propertyName;
            typeProperty = propertySyntax.TryGetTypeProperty(semanticModel); //asdfg testpoint
        }
        else if (expressionSyntax is ObjectPropertySyntax propertySyntax2
            && propertySyntax2.TryGetKeyText() is string propertyName2)
        {
            // `{ <<objectPropertyName>>: expression }` // property itself is selected
            //   -> default to the name "objectPropertyName"
            defaultNewName = propertyName2;

            // The expression we want to replace is the property value, not the property syntax
            var propertyValueSyntax = propertySyntax2.Value as ExpressionSyntax;
            if (propertyValueSyntax != null)
            {
                expressionSyntax = propertyValueSyntax;
                typeProperty = propertySyntax2.TryGetTypeProperty(semanticModel); //asdfg testpoint
            }
            else
            {
                yield break;
            }
        }
        else if (expressionSyntax is PropertyAccessSyntax propertyAccessSyntax)
        {
            // `object.topPropertyName.propertyName`
            //   -> default to the name "topPropertyNamePropertyName"
            //
            // `object.topPropertyName.propertyName`
            //   -> default to the name "propertyName"
            //  
            // More than two levels is less likely to be desirable

            string lastPartName = propertyAccessSyntax.PropertyName.IdentifierName;
            var parent = propertyAccessSyntax.BaseExpression;
            string? firstPartName = parent switch
            {
                PropertyAccessSyntax propertyAccess => propertyAccess.PropertyName.IdentifierName,
                VariableAccessSyntax variableAccess => variableAccess.Name.IdentifierName,
                FunctionCallSyntax functionCall => functionCall.Name.IdentifierName,
                _ => null
            };

            defaultNewName = firstPartName is { } ? firstPartName + lastPartName.UppercaseFirstLetter() : lastPartName;
        }

        if (semanticModel.Binder.GetNearestAncestor<StatementSyntax>(expressionSyntax) is not StatementSyntax statementSyntax)
        {
            yield break;
        }

        var newVarName = FindUnusedName(compilation, expressionSyntax.Span.Position, defaultNewName ?? "newVariable");
        var newParamName = FindUnusedName(compilation, expressionSyntax.Span.Position, defaultNewName ?? "newParameter");

        var varDeclarationSyntax = SyntaxFactory.CreateVariableDeclaration(newVarName, expressionSyntax);
        var varDeclaration = PrettyPrinterV2.PrintValid(varDeclarationSyntax, PrettyPrinterV2Options.Default) + NewLine(semanticModel); //asdfg

        var statementLineNumber = TextCoordinateConverter.GetPosition(compilationContext.LineStarts, statementSyntax.Span.Position).line;
        var definitionInsertionPosition = TextCoordinateConverter.GetOffset(compilationContext.LineStarts, statementLineNumber, 0);

        yield return new CodeFix(
           $"Create variable for {GetQuotedExpressionText(expressionSyntax)}",
           isPreferred: false,
           CodeFixKind.RefactorExtract,
           new CodeReplacement(expressionSyntax.Span, newVarName),
           new CodeReplacement(new TextSpan(definitionInsertionPosition, 0), varDeclaration));


        // For the new param's type, try to use the declared type if there is one (i.e. the type of
        //   what we're assigning to), otherwise use the actual calculated type of the expression
        var inferredType = semanticModel.GetTypeInfo(expressionSyntax);
        var declaredType = semanticModel.GetDeclaredType(expressionSyntax);
        var newParamType = NullIfErrorOrAny(declaredType) ?? NullIfErrorOrAny(inferredType);

        var looseParamExtraction = CreateExtractParameterCodeFix(
            $"Create parameter for {GetQuotedExpressionText(expressionSyntax)}",
            semanticModel, typeProperty, newParamType, newParamName, definitionInsertionPosition, expressionSyntax, StringizeType.Strictness.Loose);
        var customTypedParamExtraction = CreateExtractParameterCodeFix(
            $"Create parameter with custom types for {GetQuotedExpressionText(expressionSyntax)}",
            semanticModel, typeProperty, newParamType, newParamName, definitionInsertionPosition, expressionSyntax, StringizeType.Strictness.Medium);

        yield return looseParamExtraction;
        if (looseParamExtraction.Replacements.Skip(1).First().Text != customTypedParamExtraction.Replacements.Skip(1).First().Text) //asdfg refactor
        {
            yield return customTypedParamExtraction;
        }
    }

    private static CodeFix CreateExtractParameterCodeFix(
        string title,
        SemanticModel semanticModel,
        TypeProperty? typeProperty,
        TypeSymbol? newParamType,
        string newParamName,
        int definitionInsertionPosition,
        ExpressionSyntax expressionSyntax,
        StringizeType.Strictness strictness)
    {
        var declaration = CreateNewParameterDeclaration(semanticModel, typeProperty, newParamType, newParamName, expressionSyntax, strictness);

        return new CodeFix(
            title,
            isPreferred: false,
            CodeFixKind.RefactorExtract,
            new CodeReplacement(expressionSyntax.Span, newParamName),
            new CodeReplacement(new TextSpan(definitionInsertionPosition, 0), declaration));
    }

    private static string CreateNewParameterDeclaration(
        SemanticModel semanticModel,
        TypeProperty? typeProperty,
        TypeSymbol? newParamType,
        string newParamName,
        SyntaxBase defaultValueSyntax,
        StringizeType.Strictness strictness)
    {
        var expressionTypeName = StringizeType.Stringize(newParamType, typeProperty, strictness);
        Trace.WriteLine($"{Enum.GetName(strictness)}: {expressionTypeName}"); //asdfg

        //asdfg use syntax nodes properly
        var expressionTypeIdentifier = SyntaxFactory.CreateIdentifierWithTrailingSpace(expressionTypeName);

        var paramDeclarationSyntax = SyntaxFactory.CreateParameterDeclaration(
            newParamName,
            new TypeVariableAccessSyntax(expressionTypeIdentifier),
            defaultValueSyntax);
        var paramDeclaration = PrettyPrinterV2.PrintValid(paramDeclarationSyntax, PrettyPrinterV2Options.Default) + NewLine(semanticModel);
        return paramDeclaration;
    }

    private static TypeSymbol? NullIfErrorOrAny(TypeSymbol? type) => type is ErrorType || type is AnyType ? null : type;

    private static string FindUnusedName(Compilation compilation, int offset, string preferredName) //asdfg
    {
        var activeScopes = ActiveScopesVisitor.GetActiveScopes(compilation.GetEntrypointSemanticModel().Root, offset);
        for (int i = 1; i < int.MaxValue; ++i)
        {
            var tryingName = $"{preferredName}{(i < 2 ? "" : i)}";
            if (!activeScopes.Any(s => s.GetDeclarationsByName(tryingName).Any()))
            {
                preferredName = tryingName;
                break;
            }
        }

        return preferredName;
    }

    private static string GetQuotedExpressionText(ExpressionSyntax expressionSyntax)
    {
        return "\""
            + SyntaxStringifier.Stringify(expressionSyntax, newlineReplacement: " ")
                .TruncateWithEllipses(MaxExpressionLengthInCodeAction)
                .Trim()
            + "\"";
    }

    //asdfg: remove
    private static void PrintAllTypes(SemanticModel semanticModel)
    {
        var asdfg = SyntaxCollectorVisitor.Build(semanticModel.Root.Syntax);
        foreach (var node1 in asdfg.Where(s => s.Syntax is not Token))
        {
            //asdfg
            var node = node1.Syntax;
            Trace.WriteLine($"** {node.GetDebuggerDisplay().ReplaceNewlines(" ").TruncateWithEllipses(150)}");
            Trace.WriteLine($"  ... type info: {semanticModel.GetTypeInfo(node).Name}");
            Trace.WriteLine($"  ... declared type: {semanticModel.GetDeclaredType(node)?.Name}");
        }
    }

    //asdfg: remove
    public class SyntaxCollectorVisitor : CstVisitor
    {
        public record SyntaxItem(SyntaxBase Syntax, SyntaxItem? Parent, int Depth)
        {
            public IEnumerable<SyntaxItem> GetAncestors()
            {
                var data = this;
                while (data.Parent is { } parent)
                {
                    yield return parent;
                    data = parent;
                }
            }
        }

        private readonly IList<SyntaxItem> syntaxList = new List<SyntaxItem>();
        private SyntaxItem? parent = null;
        private int depth = 0;

        private SyntaxCollectorVisitor()
        {
        }

        public static ImmutableArray<SyntaxItem> Build(SyntaxBase syntax)
        {
            var visitor = new SyntaxCollectorVisitor();
            visitor.Visit(syntax);

            return [.. visitor.syntaxList];
        }

        protected override void VisitInternal(SyntaxBase syntax)
        {
            var syntaxItem = new SyntaxItem(Syntax: syntax, Parent: parent, Depth: depth);
            syntaxList.Add(syntaxItem);

            var prevParent = parent;
            parent = syntaxItem;
            depth++;
            base.VisitInternal(syntax);
            depth--;
            parent = prevParent;
        }
    }
}
}
