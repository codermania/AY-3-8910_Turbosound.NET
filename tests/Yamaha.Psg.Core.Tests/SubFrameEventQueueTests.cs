using Yamaha.Psg.Core.Timing;

namespace Yamaha.Psg.Core.Tests;

public class SubFrameEventQueueTests
{
    [Fact]
    public void DrainDue_ReturnsNothing_BeforeAnyWriteIsDue()
    {
        var queue = new SubFrameEventQueue();
        queue.Load([new TimedRegisterWrite(10, 8, 0x0F)]);

        Assert.Empty(queue.DrainDue(9));
        Assert.True(queue.HasPending);
    }

    [Fact]
    public void DrainDue_ReturnsWrite_ExactlyOnItsScheduledCycle()
    {
        var queue = new SubFrameEventQueue();
        queue.Load([new TimedRegisterWrite(10, 8, 0x0F)]);

        var due = queue.DrainDue(10).ToList();

        Assert.Single(due);
        Assert.Equal(10, due[0].CycleOffset);
        Assert.Equal((byte)8, due[0].Register);
        Assert.Equal((byte)0x0F, due[0].Value);
        Assert.False(queue.HasPending);
    }

    [Fact]
    public void DrainDue_DoesNotReturnTheSameWriteTwice()
    {
        var queue = new SubFrameEventQueue();
        queue.Load([new TimedRegisterWrite(5, 0, 1)]);

        Assert.Single(queue.DrainDue(100));
        Assert.Empty(queue.DrainDue(200));
    }

    [Fact]
    public void DrainDue_ReturnsMultipleWrites_InCycleOrder_RegardlessOfLoadOrder()
    {
        var queue = new SubFrameEventQueue();
        queue.Load(
        [
            new TimedRegisterWrite(20, 1, 0xAA),
            new TimedRegisterWrite(5, 0, 0x11),
            new TimedRegisterWrite(20, 2, 0xBB), // same cycle as the first write above
        ]);

        Assert.Empty(queue.DrainDue(4));

        var atFive = queue.DrainDue(5).ToList();
        Assert.Single(atFive);
        Assert.Equal((byte)0, atFive[0].Register);

        Assert.Empty(queue.DrainDue(19));

        var atTwenty = queue.DrainDue(20).ToList();
        Assert.Equal(2, atTwenty.Count);
        Assert.Equal((byte)1, atTwenty[0].Register);
        Assert.Equal((byte)2, atTwenty[1].Register);
    }

    [Fact]
    public void Load_ReplacesPreviousBufferContents()
    {
        var queue = new SubFrameEventQueue();
        queue.Load([new TimedRegisterWrite(0, 0, 1)]);
        queue.DrainDue(0);

        queue.Load([new TimedRegisterWrite(0, 0, 2)]);

        var due = queue.DrainDue(0).ToList();
        Assert.Single(due);
        Assert.Equal((byte)2, due[0].Value);
    }

    [Fact]
    public void Load_Null_ClearsQueue()
    {
        var queue = new SubFrameEventQueue();
        queue.Load([new TimedRegisterWrite(0, 0, 1)]);

        queue.Load(null);

        Assert.False(queue.HasPending);
        Assert.Empty(queue.DrainDue(1000));
    }
}
