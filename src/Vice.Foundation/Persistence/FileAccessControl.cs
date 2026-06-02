using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Vice.Persistence;

public static class FileAccessControl
{
    public static void RestrictToCurrentUser(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            || OperatingSystem.IsFreeBSD())
        {
            var mode = Directory.Exists(path)
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite;
            File.SetUnixFileMode(path, mode);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            RestrictWindows(path);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RestrictWindows(string path)
    {
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("current Windows identity has no SID");

        if (Directory.Exists(path))
        {
            var dir = new DirectoryInfo(path);
            var security = dir.GetAccessControl();
            ResetAcl(security, currentUser, isDirectory: true);
            dir.SetAccessControl(security);
        }
        else
        {
            var file = new FileInfo(path);
            var security = file.GetAccessControl();
            ResetAcl(security, currentUser, isDirectory: false);
            file.SetAccessControl(security);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ResetAcl(FileSystemSecurity security, SecurityIdentifier owner, bool isDirectory)
    {
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            security.RemoveAccessRule(rule);
        }

        var inheritance = isDirectory
            ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
            : InheritanceFlags.None;
        security.AddAccessRule(new FileSystemAccessRule(
            owner,
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
    }
}
