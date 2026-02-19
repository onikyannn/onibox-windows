using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Onibox.Services;

public sealed class CredentialManager
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int MaxCredentialBlobSize = 512;

    public static string BuildBasicAuthTarget(Uri uri)
        => $"Onibox:BasicAuth:{uri.Scheme}://{uri.Host}:{uri.Port}";

    public bool TryRead(string targetName, out NetworkCredential credential)
    {
        credential = new NetworkCredential();
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return false;
        }

        if (!CredRead(targetName, CredTypeGeneric, 0, out var credentialPtr))
        {
            return false;
        }

        try
        {
            var stored = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
            var password = string.Empty;
            if (stored.CredentialBlob != IntPtr.Zero && stored.CredentialBlobSize > 0)
            {
                password = Marshal.PtrToStringUni(stored.CredentialBlob,
                    (int)stored.CredentialBlobSize / 2) ?? string.Empty;
            }

            credential = new NetworkCredential(stored.UserName ?? string.Empty, password);
            return true;
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    public void Write(string targetName, NetworkCredential credential)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return;
        }

        var userName = credential.UserName ?? string.Empty;
        var password = credential.Password ?? string.Empty;
        var passwordBytes = Encoding.Unicode.GetBytes(password);
        if (passwordBytes.Length > MaxCredentialBlobSize)
        {
            return;
        }

        var credentialBlob = IntPtr.Zero;
        try
        {
            if (passwordBytes.Length > 0)
            {
                credentialBlob = Marshal.AllocCoTaskMem(passwordBytes.Length);
                Marshal.Copy(passwordBytes, 0, credentialBlob, passwordBytes.Length);
            }

            var credentialStruct = new CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = targetName,
                UserName = userName,
                CredentialBlobSize = (uint)passwordBytes.Length,
                CredentialBlob = credentialBlob,
                Persist = CredPersistLocalMachine
            };

            CredWrite(ref credentialStruct, 0);
        }
        finally
        {
            if (credentialBlob != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(credentialBlob);
            }
        }
    }

    public void Delete(string targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return;
        }

        CredDelete(targetName, CredTypeGeneric, 0);
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref CREDENTIAL userCredential, [In] uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree([In] IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string Comment;
        public FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }
}
