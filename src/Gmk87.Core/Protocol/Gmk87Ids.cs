namespace Gmk87.Core.Protocol;

public static class Gmk87Ids
{
    public const ushort VendorId = 0x320F;
    public const ushort ProductIdUsb = 0x5055;
    public const ushort ProductIdDongle = 0x5088;

    /// <summary>VIA raw HID, MI_01, 33-byte reports.</summary>
    public const ushort UsagePageVia = 0xFF60;

    /// <summary>Vendor config / lighting / display, MI_03, 64 out / 8 in.</summary>
    public const ushort UsagePageConfig = 0xFF1C;

    /// <summary>Firmware update endpoint, MI_02 COL04. This app only ever sends its read-only info query.</summary>
    public const ushort UsagePageOta = 0xFFEF;
}

/// <summary>Thrown when the keyboard answers with something the protocol does not allow.</summary>
public sealed class Gmk87ProtocolException(string message) : Exception(message);
