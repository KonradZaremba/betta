// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Betta.Services
{
    /// <summary>
    /// Thin wrapper over the Win32 Credential Manager APIs
    /// (<c>CredRead</c> / <c>CredWrite</c> / <c>CredDelete</c> /
    /// <c>CredEnumerate</c>). All values live under the current user's
    /// generic credential store — DPAPI-backed, encrypted at rest, unlocked by
    /// the user's login. Service names are namespaced with the
    /// <see cref="TargetPrefix"/> so Betta secrets don't collide with other
    /// applications' credentials.
    ///
    /// This is the storage backend for <c>[GrasshopperSecret]</c> params.
    /// End users manage entries via the <c>Betta_Secrets</c> Rhino command
    /// (or GH menu → Betta → Secrets…).
    /// </summary>
    public static class SecretStore
    {
        public const string TargetPrefix = "Betta:";

        /// <summary>Read a stored secret by service key. Returns false if missing.</summary>
        public static bool TryGet(string service, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(service)) return false;

            var target = TargetPrefix + service;
            if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var credPtr))
                return false;

            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
                    return false;

                // Blob is stored as UTF-16 bytes. We ignore any trailing NUL.
                var bytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
                value = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                return true;
            }
            finally
            {
                CredFree(credPtr);
            }
        }

        /// <summary>Write / overwrite a secret. Non-empty values only.</summary>
        public static bool Set(string service, string value)
        {
            if (string.IsNullOrEmpty(service) || value == null) return false;

            var target = TargetPrefix + service;
            var blob = Encoding.Unicode.GetBytes(value);
            var blobPtr = Marshal.AllocHGlobal(blob.Length);
            try
            {
                Marshal.Copy(blob, 0, blobPtr, blob.Length);

                var cred = new CREDENTIAL
                {
                    Type = CRED_TYPE_GENERIC,
                    TargetName = target,
                    CredentialBlob = blobPtr,
                    CredentialBlobSize = (uint)blob.Length,
                    Persist = CRED_PERSIST_LOCAL_MACHINE,
                    UserName = Environment.UserName,
                    Comment = "Betta secret store",
                };
                return CredWrite(ref cred, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(blobPtr);
            }
        }

        /// <summary>Remove a stored secret. Returns false if it wasn't present.</summary>
        public static bool Delete(string service)
        {
            if (string.IsNullOrEmpty(service)) return false;
            return CredDelete(TargetPrefix + service, CRED_TYPE_GENERIC, 0);
        }

        /// <summary>Enumerate Betta-scoped service keys (values are NOT returned).</summary>
        public static IReadOnlyList<string> List()
        {
            var result = new List<string>();

            // filter "Betta:*" — Win32 will scan all creds otherwise.
            if (!CredEnumerate(TargetPrefix + "*", 0, out var count, out var credsPtr))
                return result;

            try
            {
                var ptrs = new IntPtr[count];
                Marshal.Copy(credsPtr, ptrs, 0, count);
                foreach (var p in ptrs)
                {
                    var cred = Marshal.PtrToStructure<CREDENTIAL>(p);
                    if (!string.IsNullOrEmpty(cred.TargetName) &&
                        cred.TargetName.StartsWith(TargetPrefix, StringComparison.Ordinal))
                        result.Add(cred.TargetName.Substring(TargetPrefix.Length));
                }
            }
            finally
            {
                CredFree(credsPtr);
            }

            return result;
        }

        // ---- P/Invoke ----------------------------------------------------

        private const uint CRED_TYPE_GENERIC = 1;
        private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, uint type, uint reservedFlag, out IntPtr credentialPtr);

        [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref CREDENTIAL userCredential, uint flags);

        [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, uint type, uint reservedFlag);

        [DllImport("Advapi32.dll", EntryPoint = "CredEnumerateW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredEnumerate(string filter, uint flag, out int count, out IntPtr credentialPtr);

        [DllImport("Advapi32.dll", SetLastError = true)]
        private static extern void CredFree(IntPtr buffer);
    }
}
