param(
    [Parameter(Mandatory = $true)][string]$ExecutablePath,
    [Parameter(Mandatory = $true)][string]$IconPath
)
$ErrorActionPreference = 'Stop'
# Read PE resources directly: shell-associated icons can be stale or theme-dependent.
if (-not ('EiriIconResources' -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class EiriIconResources {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern IntPtr LoadLibraryEx(string name, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
    [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    public static byte[] ReadIcon(string path, int id) {
        IntPtr module = LoadLibraryEx(path, IntPtr.Zero, 2);
        if (module == IntPtr.Zero) throw new Win32Exception();
        try {
            IntPtr resource = FindResource(module, (IntPtr)id, (IntPtr)3);
            if (resource == IntPtr.Zero) throw new Win32Exception();
            byte[] bytes = new byte[SizeofResource(module, resource)];
            IntPtr address = LockResource(LoadResource(module, resource));
            if (address == IntPtr.Zero) throw new Win32Exception();
            Marshal.Copy(address, bytes, 0, bytes.Length);
            return bytes;
        } finally { FreeLibrary(module); }
    }
}
"@
}
$executable = [IO.Path]::GetFullPath($ExecutablePath)
$icon = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($IconPath))
if ($icon.Length -lt 6 -or [BitConverter]::ToUInt16($icon, 2) -ne 1) { throw 'Invalid ICO header.' }
$count = [BitConverter]::ToUInt16($icon, 4)
if ($count -eq 0) { throw 'The icon has no image frames.' }
$sha = [Security.Cryptography.SHA256]::Create()
try {
    for ($index = 0; $index -lt $count; $index++) {
        $entry = 6 + 16 * $index
        $length = [BitConverter]::ToUInt32($icon, $entry + 8)
        $offset = [BitConverter]::ToUInt32($icon, $entry + 12)
        $expected = [byte[]]::new($length)
        [Array]::Copy($icon, $offset, $expected, 0, $length)
        $actual = [EiriIconResources]::ReadIcon($executable, $index + 1)
        if ([Convert]::ToBase64String($sha.ComputeHash($expected)) -cne [Convert]::ToBase64String($sha.ComputeHash($actual))) {
            throw "Executable icon frame $index does not match icon.ico."
        }
    }
} finally { $sha.Dispose() }
Write-Output "Application icon verified ($count embedded frames): $executable"
