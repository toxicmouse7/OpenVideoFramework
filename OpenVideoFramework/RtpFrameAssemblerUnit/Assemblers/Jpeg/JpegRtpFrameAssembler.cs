using System.Net;
using Microsoft.Extensions.Logging;
using OpenVideoFramework.Common;
using OpenVideoFramework.RtspSource.Rtp;

namespace OpenVideoFramework.RtpFrameAssemblerUnit.Assemblers.Jpeg;

public class JpegRtpFrameAssembler : RtpFrameAssembler
{
    private readonly ILogger<JpegRtpFrameAssembler> _logger;
    private readonly List<RtpJpegFragment> _jpegFragments = [];
    private uint _currentTimestamp;
    private RtpPacket _lastPacket = null!;

    // Стандартные таблицы квантизации для разных уровней качества
    private static readonly byte[][] DefaultLuminanceQuantTables =
    {
        // Q=0 (максимальное сжатие)
        new byte[]
        {
            16, 11, 10, 16, 24, 40, 51, 61,
            12, 12, 14, 19, 26, 58, 60, 55,
            14, 13, 16, 24, 40, 57, 69, 56,
            14, 17, 22, 29, 51, 87, 80, 62,
            18, 22, 37, 56, 68, 109, 103, 77,
            24, 35, 55, 64, 81, 104, 113, 92,
            49, 64, 78, 87, 103, 121, 120, 101,
            72, 92, 95, 98, 112, 100, 103, 99
        },
        // Q=1 (высокое качество)
        new byte[]
        {
            8, 6, 5, 8, 12, 20, 26, 31,
            6, 6, 7, 10, 13, 29, 30, 28,
            7, 7, 8, 12, 20, 29, 35, 28,
            7, 9, 11, 15, 26, 44, 40, 31,
            9, 11, 19, 28, 34, 55, 52, 39,
            12, 18, 28, 32, 41, 52, 57, 46,
            25, 32, 39, 44, 52, 61, 60, 51,
            36, 46, 48, 49, 56, 50, 52, 50
        }
    };

    private static readonly byte[][] DefaultChrominanceQuantTables =
    {
        // Q=0 (максимальное сжатие)
        new byte[]
        {
            17, 18, 24, 47, 99, 99, 99, 99,
            18, 21, 26, 66, 99, 99, 99, 99,
            24, 26, 56, 99, 99, 99, 99, 99,
            47, 66, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99
        },
        // Q=1 (высокое качество)
        new byte[]
        {
            9, 9, 12, 24, 50, 50, 50, 50,
            9, 11, 13, 33, 50, 50, 50, 50,
            12, 13, 28, 50, 50, 50, 50, 50,
            24, 33, 50, 50, 50, 50, 50, 50,
            50, 50, 50, 50, 50, 50, 50, 50,
            50, 50, 50, 50, 50, 50, 50, 50,
            50, 50, 50, 50, 50, 50, 50, 50,
            50, 50, 50, 50, 50, 50, 50, 50
        }
    };

    // Стандартные Huffman таблицы
    private static readonly byte[] HuffmanDcLuminance =
    {
        0x00, 0x01, 0x05, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b
    };

    private static readonly byte[] HuffmanAcLuminance =
    {
        0x00, 0x02, 0x01, 0x03, 0x03, 0x02, 0x04, 0x03, 0x05, 0x05, 0x04, 0x04, 0x00, 0x00, 0x01, 0x7d,
        0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07,
        0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xa1, 0x08, 0x23, 0x42, 0xb1, 0xc1, 0x15, 0x52, 0xd1, 0xf0,
        0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0a, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x25, 0x26, 0x27, 0x28,
        0x29, 0x2a, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49,
        0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
        0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
        0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7,
        0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 0xc5,
        0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda, 0xe1, 0xe2,
        0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
        0xf9, 0xfa
    };

    private static readonly byte[] HuffmanDcChrominance =
    {
        0x00, 0x03, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b
    };

    private static readonly byte[] HuffmanAcChrominance =
    {
        0x00, 0x02, 0x01, 0x02, 0x04, 0x04, 0x03, 0x04, 0x07, 0x05, 0x04, 0x04, 0x00, 0x01, 0x02, 0x77,
        0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71,
        0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91, 0xa1, 0xb1, 0xc1, 0x09, 0x23, 0x33, 0x52, 0xf0,
        0x15, 0x62, 0x72, 0xd1, 0x0a, 0x16, 0x24, 0x34, 0xe1, 0x25, 0xf1, 0x17, 0x18, 0x19, 0x1a, 0x26,
        0x27, 0x28, 0x29, 0x2a, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
        0x49, 0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68,
        0x69, 0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x88, 0x89, 0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5,
        0xa6, 0xa7, 0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3,
        0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda,
        0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8,
        0xf9, 0xfa
    };

    private class RtpJpegFragment
    {
        public uint FragmentOffset { get; set; }
        public byte[] Data { get; set; } = [];
        public byte[]? QuantizationTables { get; set; }
    }

    public JpegRtpFrameAssembler(ILogger<JpegRtpFrameAssembler> logger)
    {
        _logger = logger;
    }

    public override CompleteFrame? AddPacket(RtpPacket packet)
    {
        var header = RtpJpegHeader.Deserialize(packet.Content);
        
        if (_jpegFragments.Count > 0 && packet.Header.Timestamp != _currentTimestamp)
        {
            _logger.LogWarning("New frame started before previous completed. Dropping incomplete frame.");
            DropFrame();
        }

        _currentTimestamp = packet.Header.Timestamp;
        _lastPacket = packet;
        
        var expectedOffset = _jpegFragments.Sum(f => f.Data.Length);
        if (header.FragmentOffset != expectedOffset)
        {
            _logger.LogError("Fragment offset mismatch. Expected: {Expected}, Got: {Actual}. Dropping frame.",
                expectedOffset, header.FragmentOffset);
            DropFrame();
            return null;
        }
        
        var fragment = ExtractFragmentData(packet.Content, header);
        if (fragment == null)
        {
            _logger.LogError("Failed to extract fragment data. Dropping frame.");
            DropFrame();
            return null;
        }

        _jpegFragments.Add(fragment);
        
        if (packet.Header.Marker)
        {
            return CreateFrame(header);
        }

        return null;
    }

    private VideoFrame CreateFrame(RtpJpegHeader header)
    {
        try
        {
            var jpegData = AssembleCompleteJpeg(header);
            return new VideoFrame
            {
                Data = jpegData,
                IsKeyFrame = true,
                ReceivedAt = _lastPacket.ReceivedAt,
                Codec = Codec.MJPEG,
                Duration = TimeSpan.MinValue,
                ClockRate = _lastPacket.ClockRate,
                Width = header.Width * 8,
                Height = header.Height * 8
            };
        }
        finally
        {
            DropFrame();
        }
    }

    private RtpJpegFragment? ExtractFragmentData(byte[] packetContent, RtpJpegHeader header)
    {
        var dataOffset = RtpJpegHeader.Size;
        byte[]? quantTables = null;

        if (header is { FragmentOffset: 0, Quantization: >= 128 })
        {
            quantTables = ExtractQuantizationTables(packetContent, dataOffset);
            if (quantTables != null)
            {
                dataOffset += 4 + quantTables.Length;
            }
        }

        if (dataOffset >= packetContent.Length)
        {
            _logger.LogError("Invalid packet: data offset beyond packet length");
            return null;
        }

        return new RtpJpegFragment
        {
            FragmentOffset = (uint)header.FragmentOffset,
            Data = packetContent[dataOffset..],
            QuantizationTables = quantTables
        };
    }

    private byte[]? ExtractQuantizationTables(byte[] packetContent, int offset)
    {
        if (packetContent.Length < offset + 4)
            return null;

        var mbz = packetContent[offset];
        var precision = packetContent[offset + 1];
        var length = (packetContent[offset + 2] << 8) | packetContent[offset + 3];

        _logger.LogTrace(
            "Quantization table header: MBZ={mbz}, Precision={precision}, Length={length}",
            mbz, precision, length);

        if (mbz != 0)
        {
            _logger.LogWarning("Invalid quantization table header: MBZ={Mbz}", mbz);
            return null;
        }

        if (packetContent.Length < offset + 4 + length)
        {
            _logger.LogWarning("Quantization tables truncated. Expected: {expected}, Available: {available}.",
                offset + 4 + length, packetContent.Length);
            return null;
        }

        var tables = packetContent[(offset + 4)..(offset + 4 + length)];

        if (tables.Length == 64)
        {
            _logger.LogTrace(
                "Quantization table consists of 64 bytes instead of 128." +
                " This may lead to broken frame.");
        }

        if (tables.Length is not 128 and not 64)
        {
            _logger.LogWarning("Quantization table has unexpected size: {size} bytes", tables.Length);
        }

        return tables;
    }

    private byte[] AssembleCompleteJpeg(RtpJpegHeader header)
    {
        using var ms = new MemoryStream();

        // SOI (Start of Image)
        ms.Write([0xFF, 0xD8]);

        // Получаем таблицы квантизации
        var (luminanceTable, chrominanceTable) = GetQuantizationTables(header);

        var quantizationTables = new[] {luminanceTable, chrominanceTable}.Where(x => x.Length > 0).ToArray();

        // DQT (Define Quantization Tables)
        WriteDqtSegment(ms, quantizationTables);

        // SOF0 (Start of Frame - Baseline DCT)
        WriteSof0Segment(ms, header.Width * 8, header.Height * 8, header.Type, quantizationTables);

        // DHT (Define Huffman Tables)
        WriteDhtSegment(ms, 0x00, HuffmanDcLuminance); // DC Luminance
        WriteDhtSegment(ms, 0x10, HuffmanAcLuminance); // AC Luminance
        WriteDhtSegment(ms, 0x01, HuffmanDcChrominance); // DC Chrominance
        WriteDhtSegment(ms, 0x11, HuffmanAcChrominance); // AC Chrominance

        // SOS (Start of Scan)
        WriteSosSegment(ms);

        // Записываем данные изображения
        foreach (var fragment in _jpegFragments)
        {
            ms.Write(fragment.Data);
        }

        // EOI (End of Image)
        ms.Write([0xFF, 0xD9]);

        var result = ms.ToArray();
        _logger.LogTrace("Assembled JPEG frame: {Size} bytes, {Width}x{Height}",
            result.Length, header.Width * 8, header.Height * 8);

        return result;
    }

    private (byte[] luminance, byte[] chrominance) GetQuantizationTables(RtpJpegHeader header)
    {
        var customTables = _jpegFragments.FirstOrDefault()?.QuantizationTables;
        byte[] luminanceTable;
        byte[] chrominanceTable;

        if (customTables != null)
        {
            switch (customTables.Length)
            {
                case 128:
                {
                    luminanceTable = customTables[..64];
                    chrominanceTable = customTables[64..128];
                    return (luminanceTable, chrominanceTable);
                }
                case 64:
                {
                    var table = customTables[..64];
                    return (table, []);
                }
            }
        }

        // In case tables were not provided, but required. This might break frame
        if (header.Quantization >= 128)
        {
            _logger.LogWarning("Q={q} indicates explicit tables, but none found. Falling back to calculated tables.",
                header.Quantization);
        }

        var qFactor = header.Quantization < 50
            ? 5000.0 / header.Quantization
            : 200.0 - 2.0 * header.Quantization;


        luminanceTable = ScaleQuantTable(DefaultLuminanceQuantTables[0], qFactor);
        chrominanceTable = ScaleQuantTable(DefaultChrominanceQuantTables[0], qFactor);

        return (luminanceTable, chrominanceTable);
    }

    private static byte[] ScaleQuantTable(byte[] baseTable, double factor)
    {
        var scaled = new byte[64];
        for (var i = 0; i < 64; i++)
        {
            var value = (int)(baseTable[i] * factor / 100.0 + 0.5);
            scaled[i] = (byte)Math.Clamp(value, 1, 255);
        }

        return scaled;
    }

    private static void WriteDqtSegment(Stream ms, byte[][] quantizationTables)
    {
        ms.Write([0xFF, 0xDB]); // DQT marker
        ms.Write(BitConverter.GetBytes(
            IPAddress.HostToNetworkOrder((short)(2 + quantizationTables.Length * (1 + 64)))));
        foreach (var (table, index) in quantizationTables.Select((x, i) => (x, i)))
        {
            ms.WriteByte((byte)index);
            ms.Write(table);
        }
    }

    private static void WriteSof0Segment(Stream ms, int width, int height, byte type, byte[][] quantizationTables)
    {
        ms.Write([0xFF, 0xC0]); // SOF0 marker
        ms.Write([0x00, 0x11]); // Length: 17
        ms.WriteByte(0x08); // Sample precision: 8 bits
        ms.WriteByte((byte)(height >> 8));
        ms.WriteByte((byte)height);
        ms.WriteByte((byte)(width >> 8));
        ms.WriteByte((byte)width);
        ms.WriteByte(0x03); // Number of components: 3

        if (type == 0)
        {
            // Type 0: YCbCr 4:2:2 - horizontal subsampling 2:1
            ms.Write([0x01, 0x21, 0x00]); // Y: horizontal=2, vertical=1, quant=0
        }
        else
        {
            // Type 1: YCbCr 4:2:0 - horizontal and vertical subsampling 2:1
            ms.Write([0x01, 0x22, 0x00]); // Y: horizontal=2, vertical=2, quant=0
        }

        ms.Write([0x02, 0x11, (byte)(quantizationTables.Length - 1)]); // Cb: horizontal=1, vertical=1
        ms.Write([0x03, 0x11, (byte)(quantizationTables.Length - 1)]); // Cr: horizontal=1, vertical=1
    }

    private static void WriteDhtSegment(Stream ms, byte tableClass, byte[] huffmanTable)
    {
        ms.Write([0xFF, 0xC4]); // DHT marker
        var length = (ushort)(3 + huffmanTable.Length);
        ms.WriteByte((byte)(length >> 8));
        ms.WriteByte((byte)length);
        ms.WriteByte(tableClass);
        ms.Write(huffmanTable);
    }

    private static void WriteSosSegment(Stream ms)
    {
        ms.Write([0xFF, 0xDA]); // SOS marker
        ms.Write([0x00, 0x0C]); // Length: 12
        ms.WriteByte(0x03); // Number of components: 3

        // Component selectors and Huffman table assignments
        ms.Write([0x01, 0x00]); // Y component: DC=0, AC=0
        ms.Write([0x02, 0x11]); // Cb component: DC=1, AC=1
        ms.Write([0x03, 0x11]); // Cr component: DC=1, AC=1

        // Spectral selection
        ms.WriteByte(0x00); // Start of spectral selection
        ms.WriteByte(0x3F); // End of spectral selection
        ms.WriteByte(0x00); // Successive approximation
    }

    private void DropFrame()
    {
        _jpegFragments.Clear();
    }
}