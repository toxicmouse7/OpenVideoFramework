namespace OpenVideoFramework.RtpDemuxUnit;

public class SequenceProcessor
{
    public void ProcessSequence(MediaStreamContext context, ushort sequenceNumber)
    {
        if (context.PacketCount > 0)
        {
            var expected = (ushort)(context.LastSequenceNumber + 1);
            if (sequenceNumber != expected)
            {
                context.LostPacketCount += (ushort)(sequenceNumber - expected);
            }
        }
        
        context.LastSequenceNumber = sequenceNumber;
        context.PacketCount++;
        context.LastPacketTime = DateTimeOffset.Now;
    }
}