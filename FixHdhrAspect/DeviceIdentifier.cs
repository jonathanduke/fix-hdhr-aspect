using System.Text.RegularExpressions;

namespace JonathanDuke.FixHdhrAspect;

public partial class DeviceIdentifier
{
    [GeneratedRegex("^[0-9A-F]{8}$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex DeviceIdRegex();
    private static readonly Regex _rxDeviceId = DeviceIdRegex();
    private static readonly byte[] _checksumLookupTable = [0xA, 0x5, 0xF, 0x6, 0x7, 0xC, 0x1, 0xB, 0x9, 0x2, 0x8, 0xD, 0x4, 0x3, 0xE, 0x0];

    private uint _value;

    public DeviceIdentifier(uint deviceId)
    {
        _value = deviceId;
    }

    public bool Validate()
    {
        return CalculateChecksum(_value) == 0;
    }

    internal static uint CalculateChecksum(uint value)
    {
        // checksum algorithm from: https://github.com/Silicondust/libhdhomerun/blob/master/hdhomerun_discover.c#L1773
        byte checksum = 0;
        checksum ^= _checksumLookupTable[(value >> 28) & 0x0F];
        checksum ^= (byte)((value >> 24) & 0x0F);
        checksum ^= _checksumLookupTable[(value >> 20) & 0x0F];
        checksum ^= (byte)((value >> 16) & 0x0F);
        checksum ^= _checksumLookupTable[(value >> 12) & 0x0F];
        checksum ^= (byte)((value >> 8) & 0x0F);
        checksum ^= _checksumLookupTable[(value >> 4) & 0x0F];
        checksum ^= (byte)((value >> 0) & 0x0F);
        return checksum;
    }

    public static explicit operator uint(DeviceIdentifier id) => id._value;

    public static DeviceIdentifier Parse(string value)
    {
        if (value.Length != 8 || !_rxDeviceId.IsMatch(value)) throw new ArgumentException("The device ID should be 8 hex digits.", nameof(value));
        return ParseInternal(value);
    }

    private static DeviceIdentifier ParseInternal(string value)
    {
        uint parsedId = 0;

        for (int i = 0; i < value.Length; i += 2)
        {
            parsedId <<= 8;
            parsedId |= (byte)int.Parse(value.Substring(i, 2), System.Globalization.NumberStyles.HexNumber);
        }

        return new DeviceIdentifier(parsedId);
    }

    public static bool TryParse(string value, out DeviceIdentifier? instance)
    {
        if (value.Length == 8 && _rxDeviceId.IsMatch(value))
        {
            try
            {
                instance = ParseInternal(value);
                return true;
            }
            catch { }
        }

        instance = null;
        return false;
    }

    public override string ToString()
    {
        return BitConverter.ToString(BitConverter.GetBytes(_value).Reverse().ToArray()).Replace("-", "");
    }
}
