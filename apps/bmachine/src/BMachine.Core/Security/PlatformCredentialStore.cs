using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BMachine.Core.Security;

/// <summary>Stores application secrets in the current user's operating-system credential vault.</summary>
public interface ISecureCredentialStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public static class SecureCredentialStore
{
    public static ISecureCredentialStore CreateForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return new WindowsCredentialStore();
        if (OperatingSystem.IsMacOS()) return new MacKeychainCredentialStore();
        if (OperatingSystem.IsLinux()) return new LinuxSecretServiceCredentialStore();
        throw new PlatformNotSupportedException("No operating-system credential vault is available on this platform.");
    }
}

internal sealed class WindowsCredentialStore : ISecureCredentialStore
{
    private const uint GenericCredential = 1;
    private const uint PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string targetName, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string targetName, uint type, uint flags);

    [DllImport("Advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredRead(Target(key), GenericCredential, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound) return Task.FromResult<string?>(null);
            throw new Win32Exception(error, "Could not read the Trello credential from Windows Credential Manager.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlobSize > 5120) return Task.FromResult<string?>(null);
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            try { return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes)); }
            finally { Array.Clear(bytes); }
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var blob = Encoding.UTF8.GetBytes(value);
        if (blob.Length > 5120) throw new ArgumentOutOfRangeException(nameof(value), "The credential exceeds Windows Credential Manager's size limit.");

        var target = Marshal.StringToHGlobalUni(Target(key));
        var user = Marshal.StringToHGlobalUni("BMachine");
        var secret = Marshal.AllocHGlobal(Math.Max(blob.Length, 1));
        try
        {
            if (blob.Length > 0) Marshal.Copy(blob, 0, secret, blob.Length);
            var credential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = target,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = secret,
                Persist = PersistLocalMachine,
                UserName = user
            };
            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not save the Trello credential to Windows Credential Manager.");
            return Task.CompletedTask;
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(target);
            Marshal.ZeroFreeGlobalAllocUnicode(user);
            for (var index = 0; index < Math.Max(blob.Length, 1); index++) Marshal.WriteByte(secret, index, 0);
            Marshal.FreeHGlobal(secret);
            Array.Clear(blob);
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredDelete(Target(key), GenericCredential, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
                throw new Win32Exception(error, "Could not remove the Trello credential from Windows Credential Manager.");
        }
        return Task.CompletedTask;
    }

    private static string Target(string key) => $"BMachine/{key}";
}

internal sealed class MacKeychainCredentialStore : ISecureCredentialStore
{
    private const string ServiceName = "com.zhensmarks.BMachine";
    private const int ItemNotFound = -25300;
    private const int DuplicateItem = -25299;
    private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationFramework = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(SecurityFramework, EntryPoint = "SecKeychainFindGenericPassword")]
    private static extern int FindGenericPassword(IntPtr keychainOrArray, uint serviceLength, byte[] service, uint accountLength, byte[] account, out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

    [DllImport(SecurityFramework, EntryPoint = "SecKeychainAddGenericPassword")]
    private static extern int AddGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, uint passwordLength, byte[] password, out IntPtr itemRef);

    [DllImport(SecurityFramework, EntryPoint = "SecKeychainItemModifyAttributesAndData")]
    private static extern int ModifyPassword(IntPtr itemRef, IntPtr attributes, uint passwordLength, byte[] password);

    [DllImport(SecurityFramework, EntryPoint = "SecKeychainItemDelete")]
    private static extern int DeleteItem(IntPtr itemRef);

    [DllImport(SecurityFramework, EntryPoint = "SecKeychainItemFreeContent")]
    private static extern int FreeContent(IntPtr attributes, IntPtr data);

    [DllImport(CoreFoundationFramework, EntryPoint = "CFRelease")]
    private static extern void Release(IntPtr cfObject);

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = Encoding.UTF8.GetBytes(ServiceName);
        var account = Encoding.UTF8.GetBytes(key);
        var status = FindGenericPassword(IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account, out var length, out var data, out var item);
        if (status == ItemNotFound) return Task.FromResult<string?>(null);
        EnsureSuccess(status, "read");
        try
        {
            if (length > 0 && data == IntPtr.Zero) throw new InvalidOperationException("The macOS Keychain returned an invalid credential buffer.");
            var bytes = new byte[length];
            if (bytes.Length > 0) Marshal.Copy(data, bytes, 0, bytes.Length);
            try { return Task.FromResult<string?>(Encoding.UTF8.GetString(bytes)); }
            finally { Array.Clear(bytes); }
        }
        finally
        {
            FreeContent(IntPtr.Zero, data);
            if (item != IntPtr.Zero) Release(item);
        }
    }

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = Encoding.UTF8.GetBytes(ServiceName);
        var account = Encoding.UTF8.GetBytes(key);
        var secret = Encoding.UTF8.GetBytes(value);
        var status = AddGenericPassword(IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account, (uint)secret.Length, secret, out var item);
        try
        {
            if (status == DuplicateItem)
            {
                status = FindGenericPassword(IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account, out _, out var oldData, out item);
                if (status == 0)
                {
                    FreeContent(IntPtr.Zero, oldData);
                    status = ModifyPassword(item, IntPtr.Zero, (uint)secret.Length, secret);
                }
            }
            EnsureSuccess(status, "save");
            return Task.CompletedTask;
        }
        finally
        {
            if (item != IntPtr.Zero) Release(item);
            Array.Clear(secret);
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = Encoding.UTF8.GetBytes(ServiceName);
        var account = Encoding.UTF8.GetBytes(key);
        var status = FindGenericPassword(IntPtr.Zero, (uint)service.Length, service, (uint)account.Length, account, out _, out var data, out var item);
        if (status == ItemNotFound) return Task.CompletedTask;
        EnsureSuccess(status, "locate");
        FreeContent(IntPtr.Zero, data);
        try { EnsureSuccess(DeleteItem(item), "delete"); }
        finally { if (item != IntPtr.Zero) Release(item); }
        return Task.CompletedTask;
    }

    private static void EnsureSuccess(int status, string operation)
    {
        if (status != 0) throw new InvalidOperationException($"Could not {operation} the BMachine credential in macOS Keychain (status {status}).");
    }
}

internal sealed class LinuxSecretServiceCredentialStore : ISecureCredentialStore
{
    private const string ServiceAttribute = "bmachine";
    private const string AccountAttribute = "trello";

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(new[] { "lookup", "application", ServiceAttribute, "account", key }, null, cancellationToken);
        if (result.ExitCode == 1) return null;
        if (result.ExitCode != 0) throw new InvalidOperationException("Could not read the Trello credential from the Linux Secret Service. Ensure the keyring is installed and unlocked.");
        return result.StandardOutput.TrimEnd('\r', '\n');
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(new[] { "store", "--label=BMachine Trello credential", "application", ServiceAttribute, "account", key }, value, cancellationToken);
        if (result.ExitCode != 0) throw new InvalidOperationException("Could not save the Trello credential to the Linux Secret Service. Ensure a Secret Service keyring is installed and unlocked.");
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(new[] { "clear", "application", ServiceAttribute, "account", key }, null, cancellationToken);
        if (result.ExitCode is not (0 or 1)) throw new InvalidOperationException("Could not remove the Trello credential from the Linux Secret Service.");
    }

    private static async Task<CommandResult> RunAsync(IEnumerable<string> arguments, string? standardInput, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("secret-tool")
        {
            UseShellExecute = false,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new InvalidOperationException("Could not start the Linux Secret Service client.");

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        if (standardInput is not null)
        {
            await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException("The Linux Secret Service did not respond. Ensure the user keyring is unlocked.");
        }

        var output = await outputTask;
        _ = await errorTask; // Never surface helper diagnostics; they can include sensitive material.
        return new CommandResult(process.ExitCode, output);
    }

    private sealed record CommandResult(int ExitCode, string StandardOutput);
}
