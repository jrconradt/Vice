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

[Generator]
public sealed partial class ViceCompositionGenerator : IIncrementalGenerator
{
    const string HOST_ATTR = "Vice.Composition.ViceHostAttribute";
    const string PACK_ATTR = "Vice.Composition.ViceCommandPackAttribute";
    const string JOB_RUNNER_ATTR = "Vice.Composition.ViceJobRunnerAttribute";
    const string SESSION_SERVICE_ATTR = "Vice.Composition.ViceSessionServiceAttribute";
    const string OPTION_ATTR = "Vice.Composition.ViceOptionAttribute";

    static readonly DiagnosticDescriptor MissingHost = new(
        "VICE001", "No [ViceHost] type found",
        "ViceCompositionGenerator requires exactly one type marked with [ViceHost] in this compilation; found none — composition will not be emitted",
        "Vice.Composition", DiagnosticSeverity.Info, isEnabledByDefault: true);

    static readonly DiagnosticDescriptor MultipleHosts = new(
        "VICE002", "Multiple [ViceHost] types",
        "ViceCompositionGenerator found more than one type marked with [ViceHost]; only one is allowed per compilation. Offending types: {0}.",
        "Vice.Composition", DiagnosticSeverity.Error, isEnabledByDefault: true);

    static readonly DiagnosticDescriptor UnresolvedParameter = new(
        "VICE003", "Parameter cannot be resolved from host",
        "'{0}' parameter '{1}' of type '{2}' has no matching member on the [ViceHost] type '{3}'. Add a public property of that type (or a derived type) to the host.",
        "Vice.Composition", DiagnosticSeverity.Error, isEnabledByDefault: true);

    static readonly DiagnosticDescriptor NoRegisterMethod = new(
        "VICE004", "[ViceCommandPack] has no Register(IViceApp, ...) method",
        "[ViceCommandPack] class '{0}' must declare a static method 'Register' whose first parameter is Vice.IViceApp",
        "Vice.Composition", DiagnosticSeverity.Error, isEnabledByDefault: true);

    static readonly DiagnosticDescriptor DuplicateFactory = new(
        "VICE005", "Duplicate factory return type",
        "Two [{0}] factories return the same type '{1}': '{2}' and '{3}'. Composition order is unstable; keep one or change the return type.",
        "Vice.Composition", DiagnosticSeverity.Error, isEnabledByDefault: true);

    static readonly DiagnosticDescriptor BadGlobalOptionType = new(
        "VICE006", "[ViceOption] type is not a usable GlobalOption",
        "[ViceOption] class '{0}' must be a non-abstract subclass of Vice.Options.GlobalOption with a public parameterless constructor",
        "Vice.Composition", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var hosts = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                HOST_ATTR,
                predicate: static (_, _) => true,
                transform: static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol)
            .Collect();

        var localPacks = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                PACK_ATTR,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, _) => ctx.TargetSymbol as INamedTypeSymbol)
            .Where(static t => t is not null && t.DeclaredAccessibility != Accessibility.Private)
            .Collect();

        var localJobRunners = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                JOB_RUNNER_ATTR,
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => ctx.TargetSymbol as IMethodSymbol)
            .Where(static m => m is { IsStatic: true } && IsContainerVisible(m))
            .Collect();

        var localSessionServices = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SESSION_SERVICE_ATTR,
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => ctx.TargetSymbol as IMethodSymbol)
            .Where(static m => m is { IsStatic: true } && IsContainerVisible(m))
            .Collect();

        var localOptions = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                OPTION_ATTR,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, _) => ctx.TargetSymbol as INamedTypeSymbol)
            .Where(static t => t is { IsAbstract: false } && t.DeclaredAccessibility != Accessibility.Private)
            .Collect();

        var localCommands = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                COMMAND_ATTR,
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (ctx, _) => ctx.TargetSymbol as INamedTypeSymbol)
            .Where(static t => t is { IsAbstract: false }
                               && t.DeclaredAccessibility != Accessibility.Private
                               && ImplementsIViceCommand(t))
            .Collect();

        var local = localPacks.Combine(localJobRunners)
            .Combine(localSessionServices)
            .Combine(localOptions)
            .Combine(localCommands);

        var compilation = context.CompilationProvider;

        context.RegisterSourceOutput(hosts.Combine(local).Combine(compilation), static (spc, pair) =>
        {
            var ((hostList, locals), comp) = pair;
            var ((((packs, jobRunners), sessionServices), options), commands) = locals;
            var localDiscovery = new LocalDiscovery(packs, jobRunners, sessionServices, options, commands);
            Run(spc, hostList, localDiscovery, comp);
        });

        InitializeTargetsPipeline(context);
    }

    sealed class LocalDiscovery
    {
        public ImmutableArray<INamedTypeSymbol?> Packs { get; }
        public ImmutableArray<IMethodSymbol?> JobRunners { get; }
        public ImmutableArray<IMethodSymbol?> SessionServices { get; }
        public ImmutableArray<INamedTypeSymbol?> Options { get; }
        public ImmutableArray<INamedTypeSymbol?> Commands { get; }

        public LocalDiscovery(
            ImmutableArray<INamedTypeSymbol?> packs,
            ImmutableArray<IMethodSymbol?> jobRunners,
            ImmutableArray<IMethodSymbol?> sessionServices,
            ImmutableArray<INamedTypeSymbol?> options,
            ImmutableArray<INamedTypeSymbol?> commands)
        {
            Packs = packs;
            JobRunners = jobRunners;
            SessionServices = sessionServices;
            Options = options;
            Commands = commands;
        }
    }

    static void Run(
        SourceProductionContext spc,
        ImmutableArray<INamedTypeSymbol> hostList,
        LocalDiscovery local,
        Compilation comp)
    {
        var discovered = Discover(hostList, local, comp);
        var (host, errors) = Validate(discovered, spc);
        foreach (var e in errors)
        {
            spc.ReportDiagnostic(e);
        }

        if (host is null)
        {
            return;
        }

        var source = Emit(host, spc, comp);
        spc.AddSource("ViceComposition.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    static HostInfo Discover(
        ImmutableArray<INamedTypeSymbol> hostList,
        LocalDiscovery local,
        Compilation comp)
    {
        var sink = new DiscoverySink();

        foreach (var pack in local.Packs)
        {
            if (pack is not null)
            {
                sink.Packs.Add(new PackEntry(pack));
            }
        }

        foreach (var runner in local.JobRunners)
        {
            if (runner is not null)
            {
                sink.JobRunners.Add(new FactoryEntry(runner));
            }
        }

        foreach (var service in local.SessionServices)
        {
            if (service is not null)
            {
                sink.SessionServices.Add(new FactoryEntry(service));
            }
        }

        foreach (var option in local.Options)
        {
            if (option is not null)
            {
                sink.GlobalOptions.Add(option);
            }
        }

        foreach (var command in local.Commands)
        {
            if (command is not null)
            {
                sink.CommandClasses.Add(command);
            }
        }

        foreach (var r in comp.References)
        {
            if (comp.GetAssemblyOrModuleSymbol(r) is IAssemblySymbol asm)
            {
                Scan(asm.GlobalNamespace, sink);
            }
        }

        sink.Packs.Sort(static (a, b) => string.CompareOrdinal(LocationKey(a.Type), LocationKey(b.Type)));
        sink.JobRunners.Sort(static (a, b) => string.CompareOrdinal(LocationKey(a.Method), LocationKey(b.Method)));
        sink.SessionServices.Sort(static (a, b) => string.CompareOrdinal(LocationKey(a.Method), LocationKey(b.Method)));
        sink.GlobalOptions.Sort(static (a, b) => string.CompareOrdinal(a.ToDisplayString(), b.ToDisplayString()));
        sink.CommandClasses.Sort(static (a, b) => string.CompareOrdinal(a.ToDisplayString(), b.ToDisplayString()));

        return new HostInfo(
            hostList,
            sink.Packs,
            sink.JobRunners,
            sink.SessionServices,
            sink.GlobalOptions,
            sink.CommandClasses);
    }

    static (HostInfo? host, IReadOnlyList<Diagnostic> errors) Validate(HostInfo discovered, SourceProductionContext spc)
    {
        var errors = new List<Diagnostic>();
        var hostList = discovered.HostList;
        if (hostList.Length == 0)
        {
            errors.Add(Diagnostic.Create(MissingHost, location: null));
            return (null, errors);
        }
        if (hostList.Length > 1)
        {
            var names = string.Join(", ", hostList.Select(h => h.ToDisplayString()));
            errors.Add(Diagnostic.Create(MultipleHosts, location: null, names));
            return (null, errors);
        }
        return (discovered, errors);
    }

    static string Emit(HostInfo info, SourceProductionContext spc, Compilation comp)
    {
        var host = info.HostList[0];
        var hostProps = HostMembers(host);
        var hostDisplay = host.ToDisplayString();

        var ns = host.ContainingNamespace.IsGlobalNamespace ? null : host.ContainingNamespace.ToDisplayString();
        var hostFq = host.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var visibility = host.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
        var hostNullCheck = host.IsReferenceType
            ? "        if (host is null)\n        {\n            throw new global::System.ArgumentNullException(nameof(host));\n        }\n"
            : "";

        WarnDuplicates(spc, info.SessionServices, "ViceSessionService");

        var composeBody =
            RenderJobRunners(info.JobRunners, hostProps, spc, hostDisplay)
            + RenderSessionServices(info.SessionServices, hostProps, spc, hostDisplay)
            + RenderHostSessionServices(host)
            + RenderGlobalOptions(info.GlobalOptions, spc, comp);

        var registerBody =
            RenderPacks(info.Packs, hostProps, spc, hostDisplay, comp)
            + RenderCommands(info.CommandClasses);

        return BuildSource(ns,
                           visibility,
                           hostFq,
                           hostNullCheck,
                           composeBody,
                           registerBody);
    }

    static string RenderJobRunners(
        List<FactoryEntry> jobRunners,
        List<(string Name, ITypeSymbol Type, bool IsField)> hostProps,
        SourceProductionContext spc,
        string hostDisplay)
    {
        var lines = new List<string>();
        foreach (var jr in jobRunners)
        {
            if (!TryRenderFactoryCall(jr.Method, hostProps, spc, "ViceJobRunner", hostDisplay, out var call))
            {
                continue;
            }

            lines.Add($"        builder.WithJobRunner({call});\n");
        }

        return string.Concat(lines);
    }

    static string RenderSessionServices(
        List<FactoryEntry> sessionServices,
        List<(string Name, ITypeSymbol Type, bool IsField)> hostProps,
        SourceProductionContext spc,
        string hostDisplay)
    {
        var lines = new List<string>();
        foreach (var ss in sessionServices)
        {
            if (!TryRenderFactoryCall(ss.Method, hostProps, spc, "ViceSessionService", hostDisplay, out var call))
            {
                continue;
            }

            var returnType = ss.Method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            lines.Add($"        builder.WithSessionService<{returnType}>({call});\n");
        }

        return string.Concat(lines);
    }

    static string RenderHostSessionServices(INamedTypeSymbol host)
    {
        var lines = new List<string>();
        foreach (var hostMember in host.GetMembers()
                     .Where(m => !m.IsStatic && HasAttr(m, SESSION_SERVICE_ATTR)))
        {
            ITypeSymbol? memberType = hostMember switch
            {
                IPropertySymbol p => p.Type,
                IFieldSymbol f => f.Type,
                _ => null
            };
            if (memberType is null)
            {
                continue;
            }

            var typeFq = memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            lines.Add($"        builder.WithSessionService<{typeFq}>(host.{hostMember.Name});\n");
        }

        return string.Concat(lines);
    }

    static string RenderGlobalOptions(
        List<INamedTypeSymbol> globalOptions,
        SourceProductionContext spc,
        Compilation comp)
    {
        var globalOptionType = comp.GetTypeByMetadataName("Vice.Options.GlobalOption");
        var lines = new List<string>();
        foreach (var go in globalOptions)
        {
            if (!IsUsableGlobalOption(go, globalOptionType))
            {
                spc.ReportDiagnostic(Diagnostic.Create(BadGlobalOptionType, go.Locations.FirstOrDefault(), go.ToDisplayString()));
                continue;
            }

            var fq = go.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            lines.Add($"        builder.WithGlobalOption(new {fq}());\n");
        }

        return string.Concat(lines);
    }

    static string RenderPacks(
        List<PackEntry> packs,
        List<(string Name, ITypeSymbol Type, bool IsField)> hostProps,
        SourceProductionContext spc,
        string hostDisplay,
        Compilation comp)
    {
        var lines = new List<string>();
        foreach (var p in packs)
        {
            var register = FindPackRegister(p.Type, comp);
            if (register is null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(NoRegisterMethod, p.Type.Locations.FirstOrDefault(), p.Type.ToDisplayString()));
                continue;
            }
            if (!TryRenderPackRegisterCall(register, hostProps, spc, hostDisplay, out var call))
            {
                continue;
            }

            lines.Add($"        {p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{call};\n");
        }

        return string.Concat(lines);
    }

    static string RenderCommands(List<INamedTypeSymbol> commandClasses)
    {
        var lines = new List<string>();
        foreach (var cmd in commandClasses)
        {
            lines.Add(RenderCommandRegistration(cmd));
        }

        return string.Concat(lines);
    }

    static string BuildSource(
        string? ns,
        string visibility,
        string hostFq,
        string hostNullCheck,
        string composeBody,
        string registerBody)
    {
        var namespaceBlock = ns is not null ? $"namespace {ns};\n\n" : "";

        return
            $"#nullable enable\n" +
            $"using System;\n" +
            $"using Vice;\n" +
            $"\n" +
            $"{namespaceBlock}" +
            $"{visibility} static class ViceComposition\n" +
            $"{{\n" +
            $"    {visibility} static global::Vice.ViceAppBuilder ComposeFromAttributes(this global::Vice.ViceAppBuilder builder, {hostFq} host)\n" +
            $"    {{\n" +
            $"        if (builder is null)\n" +
            $"        {{\n" +
            $"            throw new global::System.ArgumentNullException(nameof(builder));\n" +
            $"        }}\n" +
            $"{hostNullCheck}" +
            $"{composeBody}" +
            $"        return builder;\n" +
            $"    }}\n" +
            $"\n" +
            $"    {visibility} static global::Vice.IViceApp RegisterDiscoveredPacks(this global::Vice.IViceApp app, {hostFq} host)\n" +
            $"    {{\n" +
            $"        if (app is null)\n" +
            $"        {{\n" +
            $"            throw new global::System.ArgumentNullException(nameof(app));\n" +
            $"        }}\n" +
            $"{hostNullCheck}" +
            $"{registerBody}" +
            $"        return app;\n" +
            $"    }}\n" +
            $"}}\n";
    }

    sealed class HostInfo
    {
        public ImmutableArray<INamedTypeSymbol> HostList { get; }
        public List<PackEntry> Packs { get; }
        public List<FactoryEntry> JobRunners { get; }
        public List<FactoryEntry> SessionServices { get; }
        public List<INamedTypeSymbol> GlobalOptions { get; }
        public List<INamedTypeSymbol> CommandClasses { get; }

        public HostInfo(
            ImmutableArray<INamedTypeSymbol> hostList,
            List<PackEntry> packs,
            List<FactoryEntry> jobRunners,
            List<FactoryEntry> sessionServices,
            List<INamedTypeSymbol> globalOptions,
            List<INamedTypeSymbol> commandClasses)
        {
            HostList = hostList;
            Packs = packs;
            JobRunners = jobRunners;
            SessionServices = sessionServices;
            GlobalOptions = globalOptions;
            CommandClasses = commandClasses;
        }
    }

    static List<(string Name, ITypeSymbol Type, bool IsField)> HostMembers(INamedTypeSymbol host)
    {
        var list = new List<(string, ITypeSymbol, bool)>();
        for (INamedTypeSymbol? t = host; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
        {
            foreach (var m in t.GetMembers())
            {
                if (m.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                if (m.IsStatic)
                {
                    continue;
                }

                switch (m)
                {
                    case IPropertySymbol p when p.GetMethod is not null:
                        list.Add((p.Name, p.Type, false));
                        break;
                    case IFieldSymbol f:
                        list.Add((f.Name, f.Type, true));
                        break;
                }
            }
        }
        return list;
    }

    static void Scan(INamespaceSymbol ns, DiscoverySink sink)
    {
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(ns);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            switch (current)
            {
                case INamespaceSymbol n:
                    foreach (var member in n.GetMembers())
                    {
                        pending.Push(member);
                    }

                    break;
                case INamedTypeSymbol t:
                    ScanType(t, sink);
                    foreach (var nested in t.GetTypeMembers())
                    {
                        pending.Push(nested);
                    }

                    break;
            }
        }
    }

    static void ScanType(INamedTypeSymbol t, DiscoverySink sink)
    {
        if (t.DeclaredAccessibility == Accessibility.Private)
        {
            return;
        }

        var packAttr = t.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == PACK_ATTR);
        if (packAttr is not null)
        {
            sink.Packs.Add(new PackEntry(t));
        }

        if (!t.IsAbstract
            && HasAttr(t, COMMAND_ATTR)
            && ImplementsIViceCommand(t))
        {
            sink.CommandClasses.Add(t);
        }

        if (!t.IsAbstract
            && HasAttr(t, OPTION_ATTR))
        {
            sink.GlobalOptions.Add(t);
        }

        foreach (var m in t.GetMembers())
        {
            switch (m)
            {
                case IMethodSymbol method when method.IsStatic:
                    if (HasAttr(method, JOB_RUNNER_ATTR))
                    {
                        sink.JobRunners.Add(new FactoryEntry(method));
                    }

                    if (HasAttr(method, SESSION_SERVICE_ATTR))
                    {
                        sink.SessionServices.Add(new FactoryEntry(method));
                    }

                    break;
            }
        }
    }

    sealed class DiscoverySink
    {
        public List<PackEntry> Packs { get; } = new();
        public List<FactoryEntry> JobRunners { get; } = new();
        public List<FactoryEntry> SessionServices { get; } = new();
        public List<INamedTypeSymbol> GlobalOptions { get; } = new();
        public List<INamedTypeSymbol> CommandClasses { get; } = new();
    }

    static bool HasAttr(ISymbol sym, string fullName)
    {
        foreach (var a in sym.GetAttributes())
        {
            if (a.AttributeClass?.ToDisplayString() == fullName)
            {
                return true;
            }
        }

        return false;
    }

    static bool IsContainerVisible(IMethodSymbol method)
    {
        return method.ContainingType is { DeclaredAccessibility: not Accessibility.Private };
    }

    static bool IsUsableGlobalOption(INamedTypeSymbol type, INamedTypeSymbol? globalOptionType)
    {
        if (globalOptionType is null)
        {
            return false;
        }

        var derives = false;
        for (INamedTypeSymbol? b = type.BaseType; b is not null; b = b.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(b, globalOptionType))
            {
                derives = true;
                break;
            }
        }

        if (!derives)
        {
            return false;
        }

        foreach (var ctor in type.InstanceConstructors)
        {
            if (ctor.Parameters.Length == 0
                && ctor.DeclaredAccessibility == Accessibility.Public)
            {
                return true;
            }
        }

        return false;
    }

    static string LocationKey(ISymbol sym)
    {
        var loc = sym.Locations.FirstOrDefault(l => l.IsInSource) ?? sym.Locations.FirstOrDefault();
        if (loc is null)
        {
            return sym.ToDisplayString();
        }

        var span = loc.GetLineSpan();
        var path = span.Path ?? "";
        var line = span.StartLinePosition.Line;
        var ch = span.StartLinePosition.Character;
        return $"{path}|{line:D8}|{ch:D8}|{sym.ToDisplayString()}";
    }

    static IMethodSymbol? FindPackRegister(INamedTypeSymbol pack, Compilation comp)
    {
        var viceApp = comp.GetTypeByMetadataName("Vice.IViceApp");
        if (viceApp is null)
        {
            return null;
        }

        return pack.GetMembers("Register")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.IsStatic && m.Parameters.Length > 0
                && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, viceApp));
    }

    static bool ImplementsIViceCommand(INamedTypeSymbol t)
    {
        foreach (var i in t.AllInterfaces)
        {
            if (i.ToDisplayString() == "Vice.Composition.IViceCommand")
            {
                return true;
            }
        }

        return false;
    }

    static string RenderCommandRegistration(INamedTypeSymbol cmd)
    {
        var attr = cmd.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == COMMAND_ATTR);
        var fq = cmd.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var verb = ReadCommandVerb(attr, cmd);
        var targets = ReadExplicitTargets(attr) ?? new List<string>();
        var description = ReadCommandDescription(attr);

        var chain = $"global::Vice.Dsl.verb({SymbolDisplay.FormatLiteral(verb, true)})";
        foreach (var target in targets)
        {
            chain += $" * global::Vice.Dsl.target({SymbolDisplay.FormatLiteral(target, true)})";
        }

        return $"        global::Vice.Composition.ViceCommandRegistration.Register<{fq}>(app, {chain}, {SymbolDisplay.FormatLiteral(description, true)});\n";
    }

    static string ReadCommandVerb(AttributeData? attr, INamedTypeSymbol cmd)
    {
        if (attr is not null)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "Verb"
                    && named.Value.Value is string verb
                    && !string.IsNullOrWhiteSpace(verb))
                {
                    return verb;
                }
            }
        }

        return KebabFromTypeName(cmd.Name);
    }

    static string ReadCommandDescription(AttributeData? attr)
    {
        if (attr is not null)
        {
            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "Description"
                    && named.Value.Value is string description)
                {
                    return description;
                }
            }
        }

        return "";
    }

    static string KebabFromTypeName(string name)
    {
        var core = name.EndsWith("Command", StringComparison.Ordinal) && name.Length > 7
            ? name.Substring(0, name.Length - 7)
            : name;

        var result = "";
        for (int i = 0; i < core.Length; i++)
        {
            var c = core[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    result += "-";
                }

                result += char.ToLowerInvariant(c);
            }
            else
            {
                result += c;
            }
        }

        return result.Length == 0 ? name.ToLowerInvariant() : result;
    }

    static bool TryRenderFactoryCall(
        IMethodSymbol method,
        List<(string Name, ITypeSymbol Type, bool IsField)> hostMembers,
        SourceProductionContext spc,
        string attrLabel,
        string hostDisplay,
        out string call)
    {
        call = "";
        if (!TryResolveArgs(method,
                            0,
                            new List<string>(),
                            hostMembers,
                            spc,
                            $"[{attrLabel}] factory {method.ToDisplayString()}",
                            hostDisplay,
                            out var args))
        {
            return false;
        }

        var typeFq = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        call = $"{typeFq}.{method.Name}({string.Join(", ", args)})";
        return true;
    }

    static bool TryRenderPackRegisterCall(
        IMethodSymbol method,
        List<(string Name, ITypeSymbol Type, bool IsField)> hostMembers,
        SourceProductionContext spc,
        string hostDisplay,
        out string call)
    {
        call = "";
        if (!TryResolveArgs(method,
                            1,
                            new List<string> { "app" },
                            hostMembers,
                            spc,
                            $"[ViceCommandPack] {method.ContainingType.ToDisplayString()}.Register",
                            hostDisplay,
                            out var args))
        {
            return false;
        }

        call = $"Register({string.Join(", ", args)})";
        return true;
    }

    static bool TryResolveArgs(
        IMethodSymbol method,
        int startIndex,
        List<string> seedArgs,
        List<(string Name, ITypeSymbol Type, bool IsField)> hostMembers,
        SourceProductionContext spc,
        string label,
        string hostDisplay,
        out List<string> args)
    {
        args = seedArgs;
        for (int i = startIndex; i < method.Parameters.Length; i++)
        {
            var p = method.Parameters[i];
            if (!TryResolveParameter(p, hostMembers, out var expr))
            {
                spc.ReportDiagnostic(Diagnostic.Create(UnresolvedParameter,
                                                       method.Locations.FirstOrDefault(),
                                                       label,
                                                       p.Name,
                                                       p.Type.ToDisplayString(),
                                                       hostDisplay));
                return false;
            }

            args.Add(expr);
        }

        return true;
    }

    static bool TryResolveParameter(
        IParameterSymbol p,
        List<(string Name, ITypeSymbol Type, bool IsField)> hostMembers,
        out string expr)
    {
        foreach (var hm in hostMembers)
        {
            if (SymbolEqualityComparer.Default.Equals(hm.Type, p.Type))
            {
                expr = $"host.{hm.Name}";
                return true;
            }
        }
        foreach (var hm in hostMembers)
        {
            if (IsAssignable(hm.Type, p.Type))
            {
                expr = $"host.{hm.Name}";
                return true;
            }
        }
        expr = "";
        return false;
    }

    static bool IsAssignable(ITypeSymbol from, ITypeSymbol to)
    {
        if (SymbolEqualityComparer.Default.Equals(from, to))
        {
            return true;
        }

        for (var b = from.BaseType; b is not null; b = b.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(b, to))
            {
                return true;
            }
        }

        foreach (var i in from.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(i, to))
            {
                return true;
            }
        }

        return false;
    }

    static void WarnDuplicates(SourceProductionContext spc, List<FactoryEntry> entries, string label)
    {
        var byReturn = entries.GroupBy(e => e.Method.ReturnType, SymbolEqualityComparer.Default);
        foreach (var g in byReturn.Where(g => g.Count() > 1))
        {
            var list = g.ToList();
            spc.ReportDiagnostic(Diagnostic.Create(DuplicateFactory, list[0].Method.Locations.FirstOrDefault(),
                label,
                ((ITypeSymbol)g.Key!).ToDisplayString(),
                list[0].Method.ToDisplayString(),
                list[1].Method.ToDisplayString()));
        }
    }

    sealed class PackEntry
    {
        public INamedTypeSymbol Type { get; }
        public PackEntry(INamedTypeSymbol type) { Type = type; }
    }

    sealed class FactoryEntry
    {
        public IMethodSymbol Method { get; }
        public FactoryEntry(IMethodSymbol method) { Method = method; }
    }
}
