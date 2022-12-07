using System;
using System.Buffers;
using System.Diagnostics.Tracing;

namespace KSPCommunityFixes.Library.Buffers
{
    [EventSource(Name = "System.Buffers.ArrayPoolEventSource")]
    internal sealed class ArrayPoolEventSource : EventSource
    {
        internal enum BufferAllocatedReason
        {
            Pooled,
            OverMaximumSize,
            PoolExhausted
        }

        internal static readonly ArrayPoolEventSource Log = new ArrayPoolEventSource();

        [Event(1, Level = EventLevel.Verbose)]
        internal unsafe void BufferRented(int bufferId, int bufferSize, int poolId, int bucketId)
        {
            EventData* ptr = stackalloc EventData[4];
            *ptr = new EventData
            {
                Size = 4,
                DataPointer = (IntPtr)(&bufferId)
            };
            ptr[1] = new EventData
            {
                Size = 4,
                DataPointer = (IntPtr)(&bufferSize)
            };
            ptr[2] = new EventData
            {
                Size = 4,
                DataPointer = (IntPtr)(&poolId)
            };
            ptr[3] = new EventData
            {
                Size = 4,
                DataPointer = (IntPtr)(&bucketId)
            };
            WriteEventCore(1, 4, ptr);
        }

        [Event(2, Level = EventLevel.Informational)]
        internal unsafe void BufferAllocated(int bufferId, int bufferSize, int poolId, int bucketId, BufferAllocatedReason reason)
        {
            EventData* ptr = stackalloc EventData[5];
            *ptr = new EventData
            {
                Size = 4,
                DataPointer = (IntPtr)(&bufferId)
            };
            ptr[1] = new EventData
            {
                Size = 4,
                DataPointer = (IntPtr)(&bufferSize)
            };
            ptr[2] = new EventData
            {
                Size = 4,
                DataPointer = (IntPtr)(&poolId)
            };
            ptr[3] = new EventData
            {
                Size = 4,
                DataPointer = (IntPtr)(&bucketId)
            };
            ptr[4] = new EventData
            {
                Size = 4,
                DataPointer = (IntPtr)(&reason)
            };
            WriteEventCore(2, 5, ptr);
        }

        [Event(3, Level = EventLevel.Verbose)]
        internal void BufferReturned(int bufferId, int bufferSize, int poolId)
        {
            WriteEvent(3, bufferId, bufferSize, poolId);
        }
    }
}


