using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace PurrNet.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NetworkModuleAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
            ImmutableArray.Create(
                PurrNetDiagnostics.NetworkModuleOutsideNetworkType,
                PurrNetDiagnostics.StaticNetworkModuleField,
                PurrNetDiagnostics.NetworkModuleProperty,
                PurrNetDiagnostics.UninitializedNetworkModuleField,
                PurrNetDiagnostics.NetworkModuleFieldReassignedAfterInitialization,
                PurrNetDiagnostics.ObserversBypassesOwnerOnly);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(startContext =>
            {
                var symbols = PurrNetSymbols.Create(startContext.Compilation);
                if (!symbols.HasPurrNet || symbols.NetworkModule == null)
                    return;

                startContext.RegisterSymbolAction(symbolContext => AnalyzeField(symbolContext, symbols), SymbolKind.Field);
                startContext.RegisterSymbolAction(symbolContext => AnalyzeProperty(symbolContext, symbols), SymbolKind.Property);
                startContext.RegisterOperationAction(operationContext => AnalyzeAssignment(operationContext, symbols), OperationKind.SimpleAssignment);
                startContext.RegisterOperationAction(operationContext => AnalyzeObserversAccess(operationContext, symbols), OperationKind.PropertyReference);
            });
        }

        /// <summary>
        /// Reading parent.observers from inside a NetworkModule returns the identity's full observer
        /// list, which silently defeats [OwnerOnly]. The module's own 'observers' applies the filter.
        /// </summary>
        private static void AnalyzeObserversAccess(OperationAnalysisContext context, PurrNetSymbols symbols)
        {
            var reference = (IPropertyReferenceOperation)context.Operation;

            if (reference.Property.Name != "observers")
                return;

            if (!symbols.IsNetworkIdentity(reference.Property.ContainingType))
                return;

            // Only modules have a filtered alternative to steer people towards.
            var containingType = context.ContainingSymbol?.ContainingType;
            if (!symbols.IsNetworkModule(containingType))
                return;

            // NetworkModule itself implements the filtered 'observers'; its own reads are the filter.
            if (symbols.IsSame(containingType, symbols.NetworkModule))
                return;

            // parent.observers specifically; identity.observers on some unrelated instance is fine.
            var instance = reference.Instance;
            while (instance is IConversionOperation conversion)
                instance = conversion.Operand;

            if (instance is not IPropertyReferenceOperation { Property.Name: "parent" })
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                PurrNetDiagnostics.ObserversBypassesOwnerOnly,
                reference.Syntax.GetLocation(),
                context.ContainingSymbol?.Name ?? "<unknown>"));
        }

        private static void AnalyzeField(SymbolAnalysisContext context, PurrNetSymbols symbols)
        {
            var field = (IFieldSymbol)context.Symbol;

            if (!symbols.IsNetworkModule(field.Type))
                return;

            if (field.IsImplicitlyDeclared)
                return;

            var containingType = field.ContainingType;

            if (field.IsStatic)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.StaticNetworkModuleField,
                    field.Locations.FirstOrDefault(),
                    field.Name));
                return;
            }

            if (!symbols.IsNetworkType(containingType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.NetworkModuleOutsideNetworkType,
                    field.Locations.FirstOrDefault(),
                    field.Name,
                    containingType?.Name ?? "<unknown>"));
                return;
            }

            if (!IsUnitySerializedField(field) &&
                !HasNonNullFieldInitializer(context.Compilation, field) &&
                !HasAssignmentInInitializationMethod(context.Compilation, field))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.UninitializedNetworkModuleField,
                    field.Locations.FirstOrDefault(),
                    field.Name));
            }
        }

        private static void AnalyzeProperty(SymbolAnalysisContext context, PurrNetSymbols symbols)
        {
            var property = (IPropertySymbol)context.Symbol;
            if (property.IsImplicitlyDeclared || property.IsIndexer)
                return;

            if (!symbols.IsNetworkModule(property.Type))
                return;

            // Auto-properties are registered through their compiler-generated backing field,
            // so they follow the same rules as fields instead of PN0103.
            var backingField = GetBackingField(property);

            if (backingField == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.NetworkModuleProperty,
                    property.Locations.FirstOrDefault(),
                    property.Name));
                return;
            }

            if (property.IsStatic)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.StaticNetworkModuleField,
                    property.Locations.FirstOrDefault(),
                    property.Name));
                return;
            }

            var containingType = property.ContainingType;

            if (!symbols.IsNetworkType(containingType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.NetworkModuleOutsideNetworkType,
                    property.Locations.FirstOrDefault(),
                    property.Name,
                    containingType?.Name ?? "<unknown>"));
                return;
            }

            if (!IsUnitySerializedField(backingField) &&
                !HasNonNullPropertyInitializer(context.Compilation, property) &&
                !HasAssignmentInInitializationMethod(context.Compilation, property))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PurrNetDiagnostics.UninitializedNetworkModuleField,
                    property.Locations.FirstOrDefault(),
                    property.Name));
            }
        }

        private static IFieldSymbol? GetBackingField(IPropertySymbol property)
        {
            var containingType = property.ContainingType;
            if (containingType == null)
                return null;

            foreach (var member in containingType.GetMembers())
            {
                if (member is IFieldSymbol field &&
                    SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property))
                {
                    return field;
                }
            }

            return null;
        }

        private static void AnalyzeAssignment(OperationAnalysisContext context, PurrNetSymbols symbols)
        {
            var assignment = (ISimpleAssignmentOperation)context.Operation;
            var member = UnwrapMemberReference(assignment.Target, out var memberType);

            if (member == null || !symbols.IsNetworkModule(memberType))
                return;

            // Non-auto properties are already covered by PN0103.
            if (member is IPropertySymbol property && GetBackingField(property) == null)
                return;

            if (!symbols.IsNetworkType(member.ContainingType))
                return;

            if (IsAllowedInitializationMethod(context.ContainingSymbol as IMethodSymbol))
                return;

            context.ReportDiagnostic(Diagnostic.Create(
                PurrNetDiagnostics.NetworkModuleFieldReassignedAfterInitialization,
                assignment.Syntax.GetLocation(),
                member.Name));
        }

        private static ISymbol? UnwrapMemberReference(IOperation operation, out ITypeSymbol? memberType)
        {
            while (operation is IConversionOperation conversion)
                operation = conversion.Operand;

            switch (operation)
            {
                case IFieldReferenceOperation fieldReference:
                    memberType = fieldReference.Field.Type;
                    return fieldReference.Field;
                case IPropertyReferenceOperation propertyReference:
                    memberType = propertyReference.Property.Type;
                    return propertyReference.Property;
                default:
                    memberType = null;
                    return null;
            }
        }

        private static bool HasNonNullFieldInitializer(Compilation compilation, IFieldSymbol field)
        {
            foreach (var reference in field.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is not VariableDeclaratorSyntax declarator ||
                    declarator.Initializer == null)
                {
                    continue;
                }

                if (IsNullLikeExpression(declarator.Initializer.Value))
                    continue;

                var semanticModel = compilation.GetSemanticModel(declarator.SyntaxTree);
                var constantValue = semanticModel.GetConstantValue(declarator.Initializer.Value);
                if (constantValue.HasValue && constantValue.Value == null)
                    continue;

                return true;
            }

            return false;
        }

        private static bool HasNonNullPropertyInitializer(Compilation compilation, IPropertySymbol property)
        {
            foreach (var reference in property.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is not PropertyDeclarationSyntax declaration ||
                    declaration.Initializer == null)
                {
                    continue;
                }

                if (IsNullLikeExpression(declaration.Initializer.Value))
                    continue;

                var semanticModel = compilation.GetSemanticModel(declaration.SyntaxTree);
                var constantValue = semanticModel.GetConstantValue(declaration.Initializer.Value);
                if (constantValue.HasValue && constantValue.Value == null)
                    continue;

                return true;
            }

            return false;
        }

        private static bool IsUnitySerializedField(IFieldSymbol field)
        {
            if (field.IsStatic || field.IsConst || field.IsReadOnly)
                return false;

            if (HasAttribute(field, "System.NonSerializedAttribute"))
                return false;

            if (field.DeclaredAccessibility == Accessibility.Public)
                return true;

            return HasAttribute(field, "UnityEngine.SerializeField") ||
                   HasAttribute(field, "UnityEngine.SerializeFieldAttribute") ||
                   HasAttribute(field, "UnityEngine.SerializeReference") ||
                   HasAttribute(field, "UnityEngine.SerializeReferenceAttribute");
        }

        private static bool HasAttribute(ISymbol symbol, string metadataName)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                var attributeClass = attribute.AttributeClass;
                if (attributeClass == null)
                    continue;

                if (SymbolNames.FullName(attributeClass) == metadataName)
                    return true;
            }

            return false;
        }

        private static bool HasAssignmentInInitializationMethod(Compilation compilation, ISymbol member)
        {
            var containingType = member.ContainingType;
            if (containingType == null)
                return false;

            foreach (var reference in containingType.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is not TypeDeclarationSyntax typeDeclaration)
                    continue;

                var semanticModel = compilation.GetSemanticModel(typeDeclaration.SyntaxTree);
                foreach (var typeMember in typeDeclaration.Members)
                {
                    if (!IsInitializationMember(typeMember))
                        continue;

                    foreach (var assignment in typeMember.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                    {
                        var assignedSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
                        if (SymbolEqualityComparer.Default.Equals(assignedSymbol, member))
                            return true;
                    }
                }
            }

            return false;
        }

        private static bool IsInitializationMember(MemberDeclarationSyntax member)
        {
            if (member is ConstructorDeclarationSyntax)
                return true;

            if (member is not MethodDeclarationSyntax method)
                return false;

            return method.Identifier.ValueText == "Awake" ||
                   method.Identifier.ValueText == "OnInitializeModules";
        }

        private static bool IsAllowedInitializationMethod(IMethodSymbol? method)
        {
            if (method == null)
                return false;

            if (method.MethodKind == MethodKind.Constructor)
                return true;

            return method.Name == "Awake" ||
                   method.Name == "OnInitializeModules";
        }

        private static bool IsNullLikeExpression(ExpressionSyntax expression)
        {
            expression = StripParentheses(expression);

            if (expression.IsKind(SyntaxKind.NullLiteralExpression) ||
                expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
                expression is DefaultExpressionSyntax)
            {
                return true;
            }

            if (expression is PostfixUnaryExpressionSyntax postfix &&
                postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            {
                return IsNullLikeExpression(postfix.Operand);
            }

            return false;
        }

        private static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax parenthesized)
                expression = parenthesized.Expression;

            return expression;
        }
    }
}
