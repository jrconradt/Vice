using System.Diagnostics;
using Vice.Commands;

namespace Vice.Plugins;

internal static class PluginDispatcher
{
    public static bool TryFind(
        string appName,
        string[] args,
        ICommandRegistry registry,
        out string pluginPath,
        out string[] pluginArgs)
    {
        pluginPath = "";
        pluginArgs = Array.Empty<string>();

        if (args.Length == 0)
        {
            return false;
        }

        var verb = args[0];
        if (string.IsNullOrEmpty(verb))
        {
            return false;
        }

        if (verb.StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        if (verb.StartsWith("-", StringComparison.Ordinal))
        {
            return false;
        }

        if (registry.FindByVerb(verb) is not null)
        {
            return false;
        }

        var pluginDir = ResolvePluginDir();
        if (pluginDir is null)
        {
            return false;
        }

        var name = $"{appName}-{verb}";
        if (OperatingSystem.IsWindows())
        {
            name += ".exe";
        }

        string candidate;
        try
        {
            candidate = Path.Combine(pluginDir, name);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!File.Exists(candidate))
        {
            return false;
        }

        if (!IsTrustedPluginFile(candidate, pluginDir))
        {
            return false;
        }

        pluginPath = candidate;
        pluginArgs = args.AsSpan(1).ToArray();
        return true;
    }

    public static bool TryFindOnPath(string name, out string pluginPath)
    {
        pluginPath = "";
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var pluginDir = ResolvePluginDir();
        if (pluginDir is null)
        {
            return false;
        }

        var fileName = OperatingSystem.IsWindows() ? name + ".exe" : name;

        string candidate;
        try
        {
            candidate = Path.Combine(pluginDir, fileName);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!File.Exists(candidate))
        {
            return false;
        }

        if (!IsTrustedPluginFile(candidate, pluginDir))
        {
            return false;
        }

        pluginPath = candidate;
        return true;
    }

    private static string? ResolvePluginDir()
    {
        var overrideDir = Environment.GetEnvironmentVariable("VICE_PLUGIN_DIR");
        if (!string.IsNullOrEmpty(overrideDir))
        {
            return CanonicaliseDir(overrideDir);
        }

        var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string baseData;
        if (!string.IsNullOrEmpty(xdgData))
        {
            baseData = xdgData;
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
            {
                return null;
            }

            baseData = Path.Combine(home, ".local", "share");
        }
        return CanonicaliseDir(Path.Combine(baseData, "vice", "plugins"));
    }

    private static string? CanonicaliseDir(string raw)
    {
        try
        {
            var full = Path.GetFullPath(raw);
            if (!Directory.Exists(full))
            {
                return full;
            }

            var target = File.ResolveLinkTarget(full, returnFinalTarget: true);
            if (target is not null && !string.IsNullOrEmpty(target.FullName))
            {
                return Path.GetFullPath(target.FullName);
            }

            return full;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsTrustedPluginFile(string candidate, string pluginDir)
    {
        string resolved;
        try
        {
            resolved = Path.GetFullPath(candidate);
            var link = File.ResolveLinkTarget(resolved, returnFinalTarget: true);
            if (link is not null && !string.IsNullOrEmpty(link.FullName))
            {
                resolved = Path.GetFullPath(link.FullName);
            }
        }
        catch
        {
            return false;
        }

        var rooted = pluginDir.EndsWith(Path.DirectorySeparatorChar)
            ? pluginDir
            : pluginDir + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(rooted, StringComparison.Ordinal) &&
            !string.Equals(resolved, pluginDir, StringComparison.Ordinal))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return IsTrustedWindowsAcl(resolved);
        }

        try
        {
            var mode = File.GetUnixFileMode(resolved);
            if ((mode & UnixFileMode.OtherWrite) != 0)
            {
                return false;
            }

            if ((mode & UnixFileMode.GroupWrite) != 0)
            {
                return false;
            }
        }
        catch (Exception)
        {
            return false;
        }
        return true;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool IsTrustedWindowsAcl(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var security = info.GetAccessControl();
            var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().User;
            if (currentUser is null)
            {
                return false;
            }

            var ownerSid = security.GetOwner(typeof(System.Security.Principal.SecurityIdentifier))
                as System.Security.Principal.SecurityIdentifier;
            if (ownerSid is null)
            {
                return false;
            }

            var localSystem = new System.Security.Principal.SecurityIdentifier(
                System.Security.Principal.WellKnownSidType.LocalSystemSid, null);
            var administrators = new System.Security.Principal.SecurityIdentifier(
                System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null);
            var trustedAdmin = new System.Security.Principal.SecurityIdentifier(
                System.Security.Principal.WellKnownSidType.AccountDomainAdminsSid, null);

            if (!ownerSid.Equals(currentUser) && !ownerSid.Equals(localSystem)
                && !ownerSid.Equals(administrators) && !ownerSid.Equals(trustedAdmin))
            {
                return false;
            }

            var everyone = new System.Security.Principal.SecurityIdentifier(
                System.Security.Principal.WellKnownSidType.WorldSid, null);
            var authenticated = new System.Security.Principal.SecurityIdentifier(
                System.Security.Principal.WellKnownSidType.AuthenticatedUserSid, null);
            var users = new System.Security.Principal.SecurityIdentifier(
                System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null);

            var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true,
                typeof(System.Security.Principal.SecurityIdentifier));
            foreach (System.Security.AccessControl.FileSystemAccessRule rule in rules)
            {
                if (rule.AccessControlType != System.Security.AccessControl.AccessControlType.Allow)
                {
                    continue;
                }

                var sid = (System.Security.Principal.SecurityIdentifier)rule.IdentityReference;
                if (!sid.Equals(everyone) && !sid.Equals(authenticated) && !sid.Equals(users))
                {
                    continue;
                }

                var writeMask = System.Security.AccessControl.FileSystemRights.Write
                    | System.Security.AccessControl.FileSystemRights.WriteData
                    | System.Security.AccessControl.FileSystemRights.AppendData
                    | System.Security.AccessControl.FileSystemRights.Modify
                    | System.Security.AccessControl.FileSystemRights.FullControl;
                if ((rule.FileSystemRights & writeMask) != 0)
                {
                    return false;
                }
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static async Task<int> RunAsync(string pluginPath, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = pluginPath,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
        {
            return 127;
        }

        using var registration = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        });

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Vice.Execution.ViceExitCode.INTERRUPTED;
        }
        return process.ExitCode;
    }
}
