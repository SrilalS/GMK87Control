using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Gmk87.Core.Hid;

/// <summary>One HID top-level collection exposed by a device.</summary>
public sealed record HidInterfaceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort ReleaseNumber,
    ushort UsagePage,
    ushort Usage,
    int InputReportLength,
    int OutputReportLength,
    int FeatureReportLength,
    string? ProductName)
{
    /// <summary>"MI_01", "MI_02 COL04" and so on, parsed from the device path.</summary>
    public string InterfaceTag
    {
        get
        {
            var mi = Regex.Match(Path, @"mi_([0-9a-f]{2})", RegexOptions.IgnoreCase);
            var col = Regex.Match(Path, @"col([0-9a-f]{2})", RegexOptions.IgnoreCase);
            var tag = mi.Success ? $"MI_{mi.Groups[1].Value.ToUpperInvariant()}" : "?";
            return col.Success ? $"{tag} COL{col.Groups[1].Value.ToUpperInvariant()}" : tag;
        }
    }
}

public static class HidEnumerator
{
    /// <summary>
    /// Lists HID collections for the given VID (and PID, if supplied). Each handle is opened with
    /// zero access rights, which lets us read capabilities without any I/O to the device.
    /// </summary>
    public static IReadOnlyList<HidInterfaceInfo> Find(ushort vendorId, ushort? productId = null)
    {
        var results = new List<HidInterfaceInfo>();
        NativeMethods.HidD_GetHidGuid(out var hidGuid);
        var set = NativeMethods.SetupDiGetClassDevsW(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
            NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1))
            return results;

        try
        {
            for (uint i = 0; ; i++)
            {
                var data = new NativeMethods.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<NativeMethods.SP_DEVICE_INTERFACE_DATA>() };
                if (!NativeMethods.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref data))
                    break;

                var path = GetDevicePath(set, ref data);
                if (path is null)
                    continue;

                var info = TryDescribe(path);
                if (info is null || info.VendorId != vendorId)
                    continue;
                if (productId is not null && info.ProductId != productId)
                    continue;
                results.Add(info);
            }
        }
        finally
        {
            NativeMethods.SetupDiDestroyDeviceInfoList(set);
        }

        return results;
    }

    private static string? GetDevicePath(IntPtr set, ref NativeMethods.SP_DEVICE_INTERFACE_DATA data)
    {
        NativeMethods.SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out var required, IntPtr.Zero);
        if (required <= 0)
            return null;

        var buffer = Marshal.AllocHGlobal(required);
        try
        {
            // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 on x64/arm64, 6 on x86.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!NativeMethods.SetupDiGetDeviceInterfaceDetailW(set, ref data, buffer, required, out _, IntPtr.Zero))
                return null;
            return Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static HidInterfaceInfo? TryDescribe(string path)
    {
        using var handle = NativeMethods.CreateFileW(path, 0,
            NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero,
            NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            return null;

        var attributes = new NativeMethods.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<NativeMethods.HIDD_ATTRIBUTES>() };
        if (!NativeMethods.HidD_GetAttributes(handle, ref attributes))
            return null;

        if (!NativeMethods.HidD_GetPreparsedData(handle, out var preparsed))
            return null;

        NativeMethods.HIDP_CAPS caps;
        try
        {
            if (NativeMethods.HidP_GetCaps(preparsed, out caps) != NativeMethods.HIDP_STATUS_SUCCESS)
                return null;
        }
        finally
        {
            NativeMethods.HidD_FreePreparsedData(preparsed);
        }

        string? product = null;
        var chars = new char[128];
        if (NativeMethods.HidD_GetProductString(handle, chars, chars.Length * 2))
            product = new string(chars).TrimEnd('\0');

        return new HidInterfaceInfo(path, attributes.VendorID, attributes.ProductID, attributes.VersionNumber,
            caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength,
            caps.FeatureReportByteLength, product);
    }
}
