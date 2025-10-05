using OpenVideoFramework.Common;

namespace OpenVideoFramework.VideoTranscoderUnit;

public class VideoTranscoderUnitSettings
{
    /// <summary>
    /// Output codec.
    /// </summary>
    public required Codec OutputCodec { get; init; }
    
    /// <summary>
    /// Specific encoder name.
    /// If no encoder provided, the default will be used.
    /// For more information check ffmpeg encoders.
    /// </summary>
    public string? SpecificEncoder { get; init; }
    
    /// <summary>
    /// Specific decoder name.
    /// If no decoder provided, the default will be used.
    /// For more information check ffmpeg decoders.
    /// </summary>
    public string? SpecificDecoder { get; init; }
}