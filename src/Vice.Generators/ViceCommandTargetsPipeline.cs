using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Vice.Generators;

public sealed partial class ViceCompositionGenerator
{
    const string COMMAND_ATTR = "Vice.Composition.ViceCommandAttribute";
    const string TARGET_DEF_FQ = "Vice.TargetDef";

    static readonly DiagnosticDescriptor TargetsInferenceFailed = new(
        "VICE010", "Cannot infer targets from chain expression",
        "[ViceCommand] handler '{0}' was registered with a chain expression the generator could not statically analyze: {1}. Provide explicit targets via [ViceCommand(\"id\", \"source\", ...)] or simplify the chain.",
        "Vice.Composition", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    static readonly DiagnosticDescriptor TargetsMultipleRegistrationsDiffer = new(
        "VICE011", "Multiple registrations for [ViceCommand] disagree on targets",
        "[ViceCommand] handler '{0}' is registered with multiple chain expressions that resolve to different target sets ({1} vs {2}). Add explicit targets on the attribute to disambiguate.",
        "Vice.Composition", DiagnosticSeverity.Error, isEnabledByDefault: true);

    void InitializeTargetsPipeline(IncrementalGeneratorInitializationContext context)
    {
        var registrations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsRegisterInvocation(node),
                transform: static (ctx, _) => ExtractRegistration(ctx))
            .Where(static r => r is not null)
            .SelectMany(static (r, _) => r!)
            .Collect();

        var commandMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                COMMAND_ATTR,
                predicate: static (node, _) => node is MethodDeclarationSyntax or ClassDeclarationSyntax,
                transform: static (ctx, _) => (ISymbol)ctx.TargetSymbol)
            .Collect();

        var combined = registrations.Combine(commandMethods).Combine(context.CompilationProvider);

        context.RegisterSourceOutput(combined, static (spc, triple) =>
        {
            var ((regs, cmds), comp) = triple;
            EmitTargets(spc, regs, cmds, comp);
        });
    }

    static bool IsRegisterInvocation(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax inv)
        {
            return false;
        }

        string? name = inv.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name is GenericNameSyntax gn ? gn.Identifier.ValueText : ma.Name.Identifier.ValueText,
            IdentifierNameSyntax id => id.Identifier.ValueText,
            _ => null
        };

        return name is not null && name.StartsWith("Register", StringComparison.Ordinal);
    }

    static IReadOnlyList<RegistrationCandidate>? ExtractRegistration(GeneratorSyntaxContext ctx)
    {
        var inv = (InvocationExpressionSyntax)ctx.Node;
        var args = inv.ArgumentList.Arguments;
        if (args.Count == 0)
        {
            return null;
        }

        var symbolInfo = ctx.SemanticModel.GetSymbolInfo(inv);
        var targetMethod = symbolInfo.Symbol as IMethodSymbol
                            ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
        if (targetMethod is null)
        {
            return null;
        }

        if (!targetMethod.Name.StartsWith("Register", StringComparison.Ordinal))
        {
            return null;
        }

        var owner = targetMethod.ContainingType;
        var ownerFq = owner?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "";
        var isInstanceOnViceApp = !targetMethod.IsExtensionMethod
                                   && IsViceAppType(owner);
        var isExtensionRegistration = ownerFq == "global::Vice.Composition.ViceCommandRegistration"
                                       || ownerFq == "Vice.Composition.ViceCommandRegistration";
        if (!isInstanceOnViceApp && !isExtensionRegistration)
        {
            return null;
        }

        ArgumentSyntax? chainArg = null;
        var handlerMethods = new List<IMethodSymbol>();
        ITypeSymbol? genericCommandType = null;

        if (targetMethod.IsGenericMethod && isExtensionRegistration)
        {
            genericCommandType = targetMethod.TypeArguments.FirstOrDefault();
            chainArg = args[0];
        }
        else
        {
            chainArg = FindChainArgument(targetMethod, args);
            for (var i = 0; i < args.Count; i++)
            {
                if (args[i] == chainArg)
                {
                    continue;
                }

                var argExpr = args[i].Expression;
                var info = ctx.SemanticModel.GetSymbolInfo(argExpr);
                var m = info.Symbol as IMethodSymbol
                        ?? info.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
                if (m is not null && ReturnsTaskOfInt(m))
                {
                    handlerMethods.Add(m);
                }
            }
        }

        if (chainArg is null)
        {
            return null;
        }

        var names = ChainTargetScanner.ScanTargets(chainArg.Expression, ctx.SemanticModel, out var failDetail);
        var location = inv.GetLocation();

        if (genericCommandType is not null)
        {
            return new[]
            {
                new RegistrationCandidate(
                    HandlerKey.ForType(genericCommandType),
                    null,
                    genericCommandType,
                    names,
                    location,
                    failDetail)
            };
        }

        if (handlerMethods.Count == 0)
        {
            return null;
        }

        var result = new List<RegistrationCandidate>(handlerMethods.Count);
        foreach (var m in handlerMethods)
        {
            result.Add(new RegistrationCandidate(
                HandlerKey.ForMethod(m),
                m,
                null,
                names,
                location,
                failDetail));
        }

        return result;
    }

    static ArgumentSyntax? FindChainArgument(IMethodSymbol method, SeparatedSyntaxList<ArgumentSyntax> args)
    {
        var paramIndex = -1;
        for (var i = 0; i < method.Parameters.Length; i++)
        {
            if (method.Parameters[i].Type.ToDisplayString() == "Vice.Nodes.ChainNode")
            {
                paramIndex = i;
                break;
            }
        }

        if (paramIndex < 0)
        {
            return args.Count > 0 ? args[0] : null;
        }

        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].NameColon is { Name.Identifier.ValueText: var nm } && nm == method.Parameters[paramIndex].Name)
            {
                return args[i];
            }
        }

        return paramIndex < args.Count ? args[paramIndex] : null;
    }

    static bool ReturnsTaskOfInt(IMethodSymbol m)
    {
        return m.ReturnType.ToDisplayString() == "System.Threading.Tasks.Task<int>";
    }

    static bool IsViceAppType(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        var fq = type.ToDisplayString();
        if (fq == "Vice.IViceApp")
        {
            return true;
        }

        foreach (var i in type.AllInterfaces)
        {
            if (i.ToDisplayString() == "Vice.IViceApp")
            {
                return true;
            }
        }

        return false;
    }

    static void EmitTargets(
        SourceProductionContext spc,
        ImmutableArray<RegistrationCandidate> registrations,
        ImmutableArray<ISymbol> commandSymbols,
        Compilation comp)
    {
        if (commandSymbols.IsDefaultOrEmpty)
        {
            return;
        }

        var byHandler = new Dictionary<string, List<RegistrationCandidate>>(StringComparer.Ordinal);
        foreach (var r in registrations.Where(r => !r.Key.IsEmpty))
        {
            byHandler.TryGetValue(r.Key.Value, out var list);
            list ??= byHandler[r.Key.Value] = new List<RegistrationCandidate>();
            list.Add(r);
        }

        var emittedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sym in commandSymbols)
        {
            switch (sym)
            {
                case IMethodSymbol method:
                    EmitForMethod(spc, method, byHandler, comp, emittedKeys);
                    break;
                case INamedTypeSymbol type:
                    EmitForType(spc, type, byHandler, comp, emittedKeys);
                    break;
            }
        }
    }

    static IReadOnlyList<string>? ResolveTargetsForKey(
        string key,
        IReadOnlyList<string>? explicitNames,
        Dictionary<string, List<RegistrationCandidate>> byHandler,
        string displayName,
        SourceProductionContext spc)
    {
        if (explicitNames is { Count: > 0 })
        {
            return explicitNames;
        }

        if (!byHandler.TryGetValue(key, out var list) || list.Count == 0)
        {
            return null;
        }

        IReadOnlyList<string>? agreed = null;
        foreach (var reg in list)
        {
            if (reg.TargetNames is null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(TargetsInferenceFailed, reg.Location, displayName, reg.DebugInfo ?? "(no detail)"));
                return null;
            }

            if (agreed is null)
            {
                agreed = reg.TargetNames;
                continue;
            }

            if (!agreed.SequenceEqual(reg.TargetNames))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    TargetsMultipleRegistrationsDiffer,
                    reg.Location,
                    displayName,
                    "[" + string.Join(",", agreed) + "]",
                    "[" + string.Join(",", reg.TargetNames) + "]"));
                return null;
            }
        }

        return agreed;
    }

    static void EmitForMethod(
        SourceProductionContext spc,
        IMethodSymbol method,
        Dictionary<string, List<RegistrationCandidate>> byHandler,
        Compilation comp,
        HashSet<string> emittedKeys)
    {
        var key = HandlerKey.ForMethod(method).Value;
        if (!emittedKeys.Add(key))
        {
            return;
        }

        var attr = method.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == COMMAND_ATTR);
        var explicitNames = ReadExplicitTargets(attr);

        var displayName = method.ToDisplayString();
        var resolved = ResolveTargetsForKey(key, explicitNames, byHandler, displayName, spc);
        if (resolved is null)
        {
            return;
        }

        var owner = method.ContainingType;
        var ns = owner.ContainingNamespace.IsGlobalNamespace
            ? null
            : owner.ContainingNamespace.ToDisplayString();
        var visibility = method.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";

        var sourceText = BuildFreeFunctionTargets(ns, visibility, owner, method, resolved);
        var hint = SanitizeHint($"{owner.ToDisplayString()}.{method.Name}_Targets.g.cs");
        spc.AddSource(hint, SourceText.From(sourceText, Encoding.UTF8));
    }

    static void EmitForType(
        SourceProductionContext spc,
        INamedTypeSymbol type,
        Dictionary<string, List<RegistrationCandidate>> byHandler,
        Compilation comp,
        HashSet<string> emittedKeys)
    {
        var key = HandlerKey.ForType(type).Value;
        if (!emittedKeys.Add(key))
        {
            return;
        }

        var attr = type.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == COMMAND_ATTR);
        var explicitNames = ReadExplicitTargets(attr);

        var displayName = type.ToDisplayString();
        var resolved = ResolveTargetsForKey(key, explicitNames, byHandler, displayName, spc);
        if (resolved is null)
        {
            return;
        }

        var ns = type.ContainingNamespace.IsGlobalNamespace
            ? null
            : type.ContainingNamespace.ToDisplayString();
        var visibility = type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";

        var sourceText = BuildPartialClassTargets(ns, visibility, type, resolved);
        var hint = SanitizeHint($"{type.ToDisplayString()}_Targets.g.cs");
        spc.AddSource(hint, SourceText.From(sourceText, Encoding.UTF8));
    }

    static IReadOnlyList<string>? ReadExplicitTargets(AttributeData? attr)
    {
        if (attr is null)
        {
            return null;
        }

        if (attr.ConstructorArguments.Length == 0)
        {
            return null;
        }

        var arg = attr.ConstructorArguments[0];
        if (arg.Kind != TypedConstantKind.Array)
        {
            return null;
        }

        var list = new List<string>(arg.Values.Length);
        foreach (var v in arg.Values)
        {
            if (v.Value is string s && !string.IsNullOrEmpty(s))
            {
                list.Add(s);
            }
        }

        return list;
    }

    static string BuildFreeFunctionTargets(string? ns, string visibility, INamedTypeSymbol owner, IMethodSymbol method, IReadOnlyList<string> names)
    {
        var className = method.Name + "_Targets";
        var namespaceBlock = ns is not null ? $"namespace {ns};\n\n" : "";
        var propLines = new List<string>();
        foreach (var name in names)
        {
            var propName = ToPascal(name);
            var escaped = EscapeStringLiteral(name);
            propLines.Add(
                $"        public string {propName} => _ctx[\"{escaped}\"] ?? throw new global::System.InvalidOperationException(\"Target '{escaped}' not bound for {EscapeStringLiteral(method.Name)}.\");\n");
        }

        return
            $"#nullable enable\n\n" +
            $"{namespaceBlock}" +
            $"{visibility} static class {className}\n{{\n" +
            $"    public static TargetSet Of(global::Vice.Execution.CommandContext ctx) => new(ctx);\n\n" +
            $"    public readonly struct TargetSet\n    {{\n" +
            $"        private readonly global::Vice.Execution.CommandContext _ctx;\n" +
            $"        public TargetSet(global::Vice.Execution.CommandContext ctx) {{ _ctx = ctx; }}\n" +
            $"{string.Concat(propLines)}" +
            $"    }}\n" +
            $"}}\n";
    }

    static string BuildPartialClassTargets(string? ns, string visibility, INamedTypeSymbol type, IReadOnlyList<string> names)
    {
        var namespaceBlock = ns is not null ? $"namespace {ns};\n\n" : "";
        var propLines = new List<string>();
        foreach (var name in names)
        {
            var propName = ToPascal(name);
            var escaped = EscapeStringLiteral(name);
            propLines.Add(
                $"        public string {propName} => _ctx[\"{escaped}\"] ?? throw new global::System.InvalidOperationException(\"Target '{escaped}' not bound for {EscapeStringLiteral(type.Name)}.\");\n");
        }

        return
            $"#nullable enable\n" +
            $"using global::Vice.Execution;\n\n" +
            $"{namespaceBlock}" +
            $"{visibility} partial class {type.Name} : global::Vice.Composition.IViceCommand\n{{\n" +
            $"    public TargetSet Targets(global::Vice.Execution.CommandContext ctx) => new(ctx);\n\n" +
            $"    public readonly struct TargetSet\n    {{\n" +
            $"        private readonly global::Vice.Execution.CommandContext _ctx;\n" +
            $"        public TargetSet(global::Vice.Execution.CommandContext ctx) {{ _ctx = ctx; }}\n" +
            $"{string.Concat(propLines)}" +
            $"    }}\n}}\n";
    }

    static string EscapeStringLiteral(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    static string SanitizeHint(string s)
    {
        var chars = new List<char>(s.Length);
        foreach (var c in s)
        {
            chars.Add(char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_');
        }

        return new string(chars.ToArray());
    }

    static string ToPascal(string raw)
    {
        var chars = new List<char>(raw.Length);
        var upper = true;
        foreach (var c in raw)
        {
            if (c == '-' || c == '_' || c == '.' || c == ' ')
            {
                upper = true;
                continue;
            }

            if (upper)
            {
                chars.Add(char.ToUpperInvariant(c));
                upper = false;
            }
            else
            {
                chars.Add(c);
            }
        }

        return chars.Count == 0 ? "_" : new string(chars.ToArray());
    }

    sealed class RegistrationCandidate
    {
        public HandlerKey Key { get; }
        public IMethodSymbol? HandlerMethod { get; }
        public ITypeSymbol? GenericCommandType { get; }
        public IReadOnlyList<string>? TargetNames { get; }
        public Location Location { get; }
        public string? DebugInfo { get; }

        public RegistrationCandidate(
            HandlerKey key,
            IMethodSymbol? handlerMethod,
            ITypeSymbol? genericCommandType,
            IReadOnlyList<string>? targetNames,
            Location location,
            string? debugInfo = null)
        {
            Key = key;
            HandlerMethod = handlerMethod;
            GenericCommandType = genericCommandType;
            TargetNames = targetNames;
            Location = location;
            DebugInfo = debugInfo;
        }
    }

    readonly struct HandlerKey : IEquatable<HandlerKey>
    {
        public string Value { get; }
        public HandlerKey(string value) { Value = value; }
        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public static HandlerKey Empty => new("");

        public static HandlerKey ForMethod(IMethodSymbol m)
            => new("M:" + m.ContainingType.ToDisplayString() + "." + m.Name + "(" + string.Join(",", m.Parameters.Select(p => p.Type.ToDisplayString())) + ")");

        public static HandlerKey ForType(ITypeSymbol t)
            => new("T:" + t.ToDisplayString());

        public bool Equals(HandlerKey other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is HandlerKey h && Equals(h);
        public override int GetHashCode() => Value.GetHashCode();
    }
}

internal static class ChainTargetScanner
{
    public static IReadOnlyList<string>? ScanTargets(ExpressionSyntax chain, SemanticModel model, out string? failureDetail)
    {
        var names = new List<string>();
        failureDetail = null;
        var fail = new FailureSink();
        var ok = ScanRecursive(chain, model, names, fail);
        if (!ok)
        {
            failureDetail = fail.Detail;
            return null;
        }

        return names;
    }

    sealed class FailureSink
    {
        public string? Detail;
        public void Set(string s) { if (Detail is null) Detail = s; }
    }

    static bool ScanRecursive(ExpressionSyntax expr, SemanticModel model, List<string> names, FailureSink fail)
    {
        switch (expr)
        {
            case ParenthesizedExpressionSyntax paren:
                return ScanRecursive(paren.Expression, model, names, fail);
            case BinaryExpressionSyntax bin:
                return ScanRecursive(bin.Left, model, names, fail) && ScanRecursive(bin.Right, model, names, fail);
            case InvocationExpressionSyntax inv:
                foreach (var arg in inv.ArgumentList.Arguments)
                {
                    if (!ScanRecursive(arg.Expression, model, names, fail))
                    {
                        return false;
                    }
                }

                return true;
            case MemberAccessExpressionSyntax ma:
                {
                    var symbol = model.GetSymbolInfo(ma).Symbol;
                    if (symbol is IFieldSymbol fs && IsTargetDefField(fs))
                    {
                        var n = ExtractTargetName(fs);
                        if (n is null)
                        {
                            fail.Set($"could not extract name literal for field '{fs.ToDisplayString()}' (initializer={(fs.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as VariableDeclaratorSyntax)?.Initializer?.Value?.GetType().Name ?? "null"})");
                            return false;
                        }

                        names.Add(n);
                        return true;
                    }

                    return true;
                }
            case IdentifierNameSyntax id:
                {
                    var symbol = model.GetSymbolInfo(id).Symbol;
                    if (symbol is IFieldSymbol fs && IsTargetDefField(fs))
                    {
                        var n = ExtractTargetName(fs);
                        if (n is null)
                        {
                            fail.Set($"could not extract name literal for field '{fs.ToDisplayString()}'");
                            return false;
                        }

                        names.Add(n);
                        return true;
                    }

                    if (symbol is ILocalSymbol)
                    {
                        fail.Set($"chain references local '{id.Identifier.ValueText}' — generator cannot resolve through locals");
                        return false;
                    }

                    if (symbol is IParameterSymbol)
                    {
                        fail.Set($"chain references parameter '{id.Identifier.ValueText}' — generator cannot resolve through parameters");
                        return false;
                    }

                    return true;
                }
            case LiteralExpressionSyntax:
                return true;
            default:
                foreach (var child in expr.ChildNodes())
                {
                    if (child is ExpressionSyntax cExpr)
                    {
                        if (!ScanRecursive(cExpr, model, names, fail))
                        {
                            return false;
                        }
                    }
                }

                return true;
        }
    }

    static bool IsTargetDefField(IFieldSymbol f)
        => f.IsStatic && f.Type.ToDisplayString() == "Vice.TargetDef";

    static string? ExtractTargetName(IFieldSymbol field)
    {
        foreach (var attr in field.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != "Vice.Composition.TargetNameAttribute")
            {
                continue;
            }

            if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string s && !string.IsNullOrEmpty(s))
            {
                return s;
            }
        }

        foreach (var syntaxRef in field.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is not VariableDeclaratorSyntax decl)
            {
                continue;
            }

            var initVal = decl.Initializer?.Value;
            ArgumentListSyntax? argList = initVal switch
            {
                ObjectCreationExpressionSyntax obj => obj.ArgumentList,
                ImplicitObjectCreationExpressionSyntax impl => impl.ArgumentList,
                _ => null
            };

            if (argList is null || argList.Arguments.Count == 0)
            {
                continue;
            }

            var firstArg = argList.Arguments[0].Expression;
            if (firstArg is LiteralExpressionSyntax lit && lit.Token.Value is string s2)
            {
                return s2;
            }
        }

        return null;
    }
}
