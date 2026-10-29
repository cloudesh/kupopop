using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kupopop.Shared;

namespace Kupopop;

public sealed record DeviceAccount(Guid AccountId, string AccessToken);

public sealed class CredentialVault(string directory)
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, int flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, int flags, out DataBlob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    public DeviceAccount? Load(Uri server)
    {
        string path = PathFor(server);
        if (!File.Exists(path)) return null;
        byte[] clear = Protect(File.ReadAllBytes(path), false);
        try { return JsonSerializer.Deserialize<DeviceAccount>(clear, Protocol.Json); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public void Save(Uri server, DeviceAccount account)
    {
        Directory.CreateDirectory(directory);
        byte[] clear = Protocol.Encode(account);
        try
        {
            byte[] encrypted = Protect(clear, true);
            string path = PathFor(server);
            File.WriteAllBytes(path + ".tmp", encrypted);
            File.Move(path + ".tmp", path, true);
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    private string PathFor(Uri server) => Path.Combine(directory,
        "device-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(server.GetLeftPart(UriPartial.Authority).ToLowerInvariant())))[..20] + ".dpapi");

    private static byte[] Protect(byte[] source, bool encrypt)
    {
        var input = new DataBlob { Size = source.Length, Data = Marshal.AllocHGlobal(source.Length) };
        var output = new DataBlob();
        try
        {
            Marshal.Copy(source, 0, input.Data, source.Length);
            bool success = encrypt
                ? CryptProtectData(ref input, "Kupopop device credential", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            // Clear plaintext buffers before releasing unmanaged memory.
            if (input.Data != IntPtr.Zero)
            { Marshal.Copy(new byte[input.Size], 0, input.Data, input.Size); Marshal.FreeHGlobal(input.Data); }
            if (output.Data != IntPtr.Zero)
            { Marshal.Copy(new byte[output.Size], 0, output.Data, output.Size); LocalFree(output.Data); }
        }
    }
}
