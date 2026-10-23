// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Betta.Generators.CodeFixes
{
    /// <summary>
    /// Quick fix for BETTA012: makes the method the warning sits on answer to the locked GUID
    /// again, by listing the old name in FormerNames (on [GrasshopperMethod], or on the type's
    /// [GrasshopperCollection] for a renamed type) or by pinning the old Guid. The generator
    /// says which, and with what, in the diagnostic's properties.
    /// </summary>
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(GuidLockCodeFix)), Shared]
    public sealed class GuidLockCodeFix : CodeFixProvider
    {
        // GuidLock.FixProperty / ValueProperty in Betta.Generators, and the names of its LockFix values.
        private const string FixProperty = "BettaLockFix";
        private const string ValueProperty = "BettaLockValue";

        public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create("BETTA012");

        // A renamed type reports once per method, each with the same edit to the collection:
        // fixing them all at once would list the old name several times.
        public override FixAllProvider GetFixAllProvider() => null;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null) return;

            foreach (var diagnostic in context.Diagnostics)
            {
                if (!diagnostic.Location.IsInSource ||
                    !diagnostic.Properties.TryGetValue(FixProperty, out var fix) ||
                    !diagnostic.Properties.TryGetValue(ValueProperty, out var value) || value == null)
                    continue;
                var method = root.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<MethodDeclarationSyntax>();
                if (method == null) continue;

                SyntaxNode fixedRoot = null;
                string title = null;
                switch (fix)
                {
                    case "MethodFormerName":
                        title = $"Add \"{value}\" to FormerNames";
                        fixedRoot = Edit(root, method.AttributeLists, "GrasshopperMethod", a => AddFormerName(a, value));
                        break;
                    case "CollectionFormerName":
                        title = $"Add \"{value}\" to FormerNames on [GrasshopperCollection]";
                        var type = method.FirstAncestorOrSelf<TypeDeclarationSyntax>();
                        if (type == null) break;
                        fixedRoot = Find(type.AttributeLists, "GrasshopperCollection") == null
                            ? root.ReplaceNode(type, AddCollection(root, type, value))
                            : Edit(root, type.AttributeLists, "GrasshopperCollection", a => AddFormerName(a, value));
                        break;
                    case "PinGuid":
                        title = $"Pin Guid = \"{value}\"";
                        fixedRoot = Edit(root, method.AttributeLists, "GrasshopperMethod", a => SetNamed(a, "Guid", Str(value)));
                        break;
                }
                if (fixedRoot == null) continue;

                var document = context.Document;
                context.RegisterCodeFix(
                    CodeAction.Create(title, _ => Task.FromResult(document.WithSyntaxRoot(fixedRoot)), equivalenceKey: fix + ":" + value),
                    diagnostic);
            }
        }

        private static AttributeSyntax Find(SyntaxList<AttributeListSyntax> lists, string simpleName)
        {
            foreach (var attribute in lists.SelectMany(l => l.Attributes))
            {
                var name = attribute.Name switch
                {
                    QualifiedNameSyntax q => q.Right.Identifier.ValueText,
                    AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
                    SimpleNameSyntax s => s.Identifier.ValueText,
                    _ => null,
                };
                if (name == simpleName || name == simpleName + "Attribute") return attribute;
            }
            return null;
        }

        /// <summary>The root with the attribute edited, or null when there's no attribute or the edit can't be made.</summary>
        private static SyntaxNode Edit(SyntaxNode root, SyntaxList<AttributeListSyntax> lists, string simpleName,
            Func<AttributeSyntax, AttributeSyntax> edit)
        {
            var attribute = Find(lists, simpleName);
            var edited = attribute == null ? null : edit(attribute);
            return edited == null ? null : root.ReplaceNode(attribute, edited.WithAdditionalAnnotations(Formatter.Annotation));
        }

        private static AttributeSyntax AddFormerName(AttributeSyntax attribute, string value)
        {
            var argument = Named(attribute, "FormerNames");
            if (argument == null) return AddArgument(attribute, FormerNames(value));

            var literal = Str(value);
            switch (argument.Expression)
            {
                case ImplicitArrayCreationExpressionSyntax array:
                    return attribute.ReplaceNode(array.Initializer, array.Initializer.AddExpressions(literal));
                case ArrayCreationExpressionSyntax array when array.Initializer != null:
                    return attribute.ReplaceNode(array.Initializer, array.Initializer.AddExpressions(literal));
                case CollectionExpressionSyntax collection:
                    return attribute.ReplaceNode(collection, collection.AddElements(ExpressionElement(literal)));
                default:
                    // A constant or some other expression: the author has to edit it.
                    return null;
            }
        }

        private static AttributeSyntax SetNamed(AttributeSyntax attribute, string name, ExpressionSyntax value)
        {
            var argument = Named(attribute, name);
            return argument == null
                ? AddArgument(attribute, AttributeArgument(NameEquals(IdentifierName(name)), null, value))
                : attribute.ReplaceNode(argument.Expression, value);
        }

        /// <summary>A new [GrasshopperCollection(FormerNames = …)] on its own line above the type, keeping the type's doc comment on top.</summary>
        private static TypeDeclarationSyntax AddCollection(SyntaxNode root, TypeDeclarationSyntax type, string value)
        {
            var leading = type.GetLeadingTrivia();
            var indent = leading.Reverse().TakeWhile(t => t.IsKind(SyntaxKind.WhitespaceTrivia)).Reverse();
            var newline = root.DescendantTrivia().FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia));
            var list = AttributeList(SingletonSeparatedList(
                    Attribute(IdentifierName("GrasshopperCollection"), AttributeArgumentList(SingletonSeparatedList(FormerNames(value))))))
                .WithLeadingTrivia(leading)
                .WithTrailingTrivia(newline.IsKind(SyntaxKind.EndOfLineTrivia) ? newline : CarriageReturnLineFeed)
                .WithAdditionalAnnotations(Formatter.Annotation);
            var bare = type.WithoutLeadingTrivia().WithLeadingTrivia(indent);
            return bare.WithAttributeLists(bare.AttributeLists.Insert(0, list));
        }

        private static AttributeArgumentSyntax Named(AttributeSyntax attribute, string name) =>
            attribute.ArgumentList?.Arguments.FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == name);

        private static AttributeSyntax AddArgument(AttributeSyntax attribute, AttributeArgumentSyntax argument) =>
            attribute.ArgumentList == null
                ? attribute.WithArgumentList(AttributeArgumentList(SingletonSeparatedList(argument)))
                : attribute.WithArgumentList(attribute.ArgumentList.AddArguments(argument));

        private static AttributeArgumentSyntax FormerNames(string value) =>
            AttributeArgument(NameEquals(IdentifierName("FormerNames")), null,
                ImplicitArrayCreationExpression(InitializerExpression(SyntaxKind.ArrayInitializerExpression,
                    SingletonSeparatedList<ExpressionSyntax>(Str(value)))));

        private static LiteralExpressionSyntax Str(string value) =>
            LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(value));
    }
}
