namespace JonathanDuke.FixHdhrAspect;

public static class AspectRatioExtensions
{
    public static string ToRatioString(this MpegAspectRatio value)
    {
        if (value == MpegAspectRatio.Default)
        {
            return value.ToString();
        }

        return value.ToString().Replace("x", ":").Replace("_", ".").TrimStart('.');
    }

    public static string FromAspectRatio(this string aspectRatioString)
    {
        if (string.IsNullOrEmpty(aspectRatioString) || string.Equals(aspectRatioString, MpegAspectRatio.Default.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return MpegAspectRatio.Default.ToString();
        }

        return "_" + aspectRatioString.Replace(".", "_").Replace(":", "x");
    }
}
