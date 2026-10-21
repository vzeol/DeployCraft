using System.Runtime.InteropServices;

namespace DeployCraft.Server.Remote;

/// <summary>Raw Windows calls for the service account: service configuration and the "Ouvrir une session en tant que service" (Log on as a service) right.</summary>
internal static partial class Win32
{
    public const uint SERVICE_CHANGE_CONFIG = 0x0002;
    public const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    public const uint SC_MANAGER_CONNECT = 0x0001;

    public const int LOGON32_LOGON_BATCH = 4;

    public const uint POLICY_CREATE_ACCOUNT = 0x0010;
    public const uint POLICY_LOOKUP_NAMES = 0x0800;

    [StructLayout(LayoutKind.Sequential)]
    public struct LSA_UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LSA_OBJECT_ATTRIBUTES
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "ChangeServiceConfigW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ChangeServiceConfig(
        IntPtr service, uint serviceType, uint startType, uint errorControl, string? binaryPathName, string? loadOrderGroup,
        IntPtr tagId, string? dependencies, string? serviceStartName, string? password, string? displayName);

    [LibraryImport("advapi32.dll")]
    public static partial uint LsaOpenPolicy(IntPtr systemName, ref LSA_OBJECT_ATTRIBUTES attributes, uint desiredAccess, out IntPtr policy);

    [LibraryImport("advapi32.dll")]
    public static partial uint LsaAddAccountRights(IntPtr policy, [In] byte[] accountSid, [In] LSA_UNICODE_STRING[] rights, uint count);

    [LibraryImport("advapi32.dll")]
    public static partial uint LsaClose(IntPtr policy);

    [LibraryImport("advapi32.dll")]
    public static partial int LsaNtStatusToWinError(uint status);
}
