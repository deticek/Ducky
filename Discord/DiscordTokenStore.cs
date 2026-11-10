using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ducky.Discord;

internal static class DiscordTokenStore
{
    private static string FilePath =>
        Path.Combine(DuckyPaths.DataDirectory, "discord-token.dat");

    public static bool HasToken => File.Exists(FilePath);

    public static void Save(string token)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Secure Discord token storage is currently supported on Windows only.");
        }

        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Discord token cannot be empty.", nameof(token));

        byte[] cleartext = Encoding.UTF8.GetBytes(token.Trim());
        try
        {
            byte[] encrypted = Transform(cleartext, protect: true);
            Directory.CreateDirectory(DuckyPaths.DataDirectory);
            string temporaryPath = FilePath + ".tmp";
            File.WriteAllBytes(temporaryPath, encrypted);
            File.Move(temporaryPath, FilePath, true);
            CryptographicOperations.ZeroMemory(encrypted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cleartext);
        }
    }

    public static string? Load()
    {
        if (!File.Exists(FilePath))
            return null;

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Secure Discord token storage is currently supported on Windows only.");
        }

        byte[] encrypted = File.ReadAllBytes(FilePath);
        byte[] cleartext = Transform(encrypted, protect: false);
        try
        {
            return Encoding.UTF8.GetString(cleartext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
            CryptographicOperations.ZeroMemory(cleartext);
        }
    }

    private static byte[] Transform(byte[] input, bool protect)
    {
        GCHandle inputHandle = GCHandle.Alloc(input, GCHandleType.Pinned);
        DataBlob output = default;
        try
        {
            DataBlob source = new()
            {
                Size = input.Length,
                Data = inputHandle.AddrOfPinnedObject()
            };
            bool success = protect
                ? CryptProtectData(
                    ref source,
                    null,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0,
                    out output)
                : CryptUnprotectData(
                    ref source,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0,
                    out output);

            if (!success)
                throw new IOException(
                    "Windows could not protect or read the Discord token.",
                    Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));

            byte[] result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            if (inputHandle.IsAllocated)
                inputHandle.Free();
            if (output.Data != IntPtr.Zero)
                LocalFree(output.Data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("Crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        uint flags,
        out DataBlob dataOut);

    [DllImport("Crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        uint flags,
        out DataBlob dataOut);

    [DllImport("Kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
