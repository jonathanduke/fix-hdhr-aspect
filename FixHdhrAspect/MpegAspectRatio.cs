namespace JonathanDuke.FixHdhrAspect;

/// <summary>
/// Supported aspect ratio values in the MPEG specification.
/// </summary>
/// <see cref="http://dvdnav.mplayerhq.hu/dvdinfo/mpeghdrs.html"/>
public enum MpegAspectRatio : byte
{
    /// <summary>
    /// Do not override the aspect ratio.
    /// </summary>
    Default = 0,
    /// <summary>
    /// Override the stream with a 1:1 aspect ratio.
    /// </summary>
    _1x1 = 1,
    /// <summary>
    /// Override the stream with a 4:3 aspect ratio.
    /// </summary>
    _4x3 = 2,
    /// <summary>
    /// Override the stream with a 16:9 aspect ratio.
    /// </summary>
    _16x9 = 3,
    /// <summary>
    /// Override the stream with a 2.21:1 aspect ratio.
    /// </summary>
    _2_21x1 = 4,
}
