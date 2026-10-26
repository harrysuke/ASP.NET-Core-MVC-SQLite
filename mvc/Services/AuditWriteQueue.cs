using System.Threading.Channels;

namespace mvc.Services
{
    // Bounded singleton queues used to move audit/page-view DB writes off the
    // request thread. TryWrite is used so a full channel never blocks or
    // fails the HTTP request; oldest entries are dropped first.
    public sealed class AuditWriteQueue
    {
        private const int Capacity = 4096;

        public Channel<object> AuditLogs { get; } = Channel.CreateBounded<object>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        public Channel<object> PageViews { get; } = Channel.CreateBounded<object>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        public bool TryWriteAudit(object entry) => AuditLogs.Writer.TryWrite(entry);
        public bool TryWritePageView(object entry) => PageViews.Writer.TryWrite(entry);
    }
}