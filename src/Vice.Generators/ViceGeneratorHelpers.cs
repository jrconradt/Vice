using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Vice.Generators;

internal static class ViceGeneratorHelpers
{
    public const string COMMAND_ATTR = "Vice.Composition.ViceCommandAttribute";

    public static bool HasAttr(ISymbol sym, string fullName)
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

    public static string KebabFromTypeName(string name)
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

    public static bool ImplementsIViceCommand(INamedTypeSymbol t)
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

    public static IReadOnlyList<string>? ReadExplicitTargets(AttributeData? attr)
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
}
