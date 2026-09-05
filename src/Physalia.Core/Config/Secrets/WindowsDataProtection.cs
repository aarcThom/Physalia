// Copyright (c) 2026 Physalia Contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Physalia.Core.Config.Secrets;

/// <summary>
/// A minimal DPAPI wrapper over <c>crypt32.dll</c>, scoped to the current OS user.
/// </summary>
/// <remarks>
/// <para><b>Why P/Invoke rather than the <c>System.Security.Cryptography.ProtectedData</c>
/// package.</b> Physalia.Core is merged into the <c>.gha</c> by ILRepack and internalized, and every
/// package added to that merge is another assembly whose identity has to survive the process. DPAPI
/// is two entry points and a struct, so the dependency buys nothing that ~40 lines does not. This is
/// the same "zero new package references" reasoning the in-process MCP stdio transport was built
/// under.</para>
/// <para><b>The byte format matches <c>ProtectedData.Protect</c> exactly</b> — no entropy, no
/// header of our own, <c>CRYPTPROTECT_UI_FORBIDDEN</c>, current-user scope — so a blob written by
/// either can be read by the other. That compatibility is load-bearing: the MCP bridge's token cache
/// was written with <c>ProtectedData</c> and must keep opening its existing files after it moves
/// onto this class.</para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WindowsDataProtection
{
    // Never prompt. A background thread inside Rhino has no business raising a DPAPI dialog, and a
    // blocked prompt would hang the solve rather than fail it. ProtectedData sets this flag too.
    private const int CryptprotectUiForbidden = 0x1;

    /// <summary>
    /// Encrypts bytes for the current OS user.
    /// </summary>
    /// <param name="plaintext">The bytes to protect.</param>
    /// <returns>The encrypted blob.</returns>
    internal static byte[] Protect(byte[] plaintext) => Transform(plaintext, protect: true);

    /// <summary>
    /// Decrypts bytes previously protected for the current OS user.
    /// </summary>
    /// <param name="ciphertext">The encrypted blob.</param>
    /// <returns>The original bytes.</returns>
    internal static byte[] Unprotect(byte[] ciphertext) => Transform(ciphertext, protect: false);

    private static byte[] Transform(byte[] input, bool protect)
    {
        ArgumentNullException.ThrowIfNull(input);

        var inBlob = default(DataBlob);
        var outBlob = default(DataBlob);
        IntPtr buffer = IntPtr.Zero;

        try
        {
            buffer = Marshal.AllocHGlobal(input.Length);
            Marshal.Copy(input, 0, buffer, input.Length);
            inBlob.CbData = input.Length;
            inBlob.PbData = buffer;

            bool ok = protect
                ? CryptProtectData(ref inBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, ref outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptprotectUiForbidden, ref outBlob);

            if (!ok)
            {
                // CryptographicException, not Win32Exception: callers filter on it, and the failure
                // IS a cryptographic one from their point of view — most often a blob written by a
                // different Windows account, whose DPAPI key this process simply does not have.
                int error = Marshal.GetLastWin32Error();
                throw new CryptographicException(
                    $"DPAPI {(protect ? "encryption" : "decryption")} failed (0x{error:X8}).",
                    new System.ComponentModel.Win32Exception(error));
            }

            var result = new byte[outBlob.CbData];
            Marshal.Copy(outBlob.PbData, result, 0, outBlob.CbData);
            return result;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }

            if (outBlob.PbData != IntPtr.Zero)
            {
                LocalFree(outBlob.PbData);
            }
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        ref DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        ref DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int CbData;
        public IntPtr PbData;
    }
}
