using System.Diagnostics;

namespace Content.Server.NodeContainer.EntitySystems;

/// <summary>
/// Opt-in, server-thread-only diagnostic recorder. Scopes are synchronous and inclusive:
/// nested samples must not be summed with their parents. Allocations exclude worker threads.
/// Recording is bounded; a benchmark must fail if DroppedSamples is nonzero.
/// </summary>
public sealed class NetworkConnectionProfiler
{
    public readonly record struct Sample(string Phase, long Start, long Elapsed, long Bytes, int Items);

    public readonly List<Sample> Samples = new(4096);
    public int DroppedSamples { get; private set; }

    public Scope Measure(string phase, int items = 0) => new(this, phase, items);

    public readonly struct Scope : IDisposable
    {
        private readonly NetworkConnectionProfiler _owner;
        private readonly string _phase;
        private readonly int _items;
        private readonly long _bytes;
        private readonly long _start;

        internal Scope(NetworkConnectionProfiler owner, string phase, int items)
        {
            _owner = owner;
            _phase = phase;
            _items = items;
            _bytes = GC.GetAllocatedBytesForCurrentThread();
            _start = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            var end = Stopwatch.GetTimestamp();
            var bytes = GC.GetAllocatedBytesForCurrentThread() - _bytes;
            if (_owner.Samples.Count == _owner.Samples.Capacity)
            {
                _owner.DroppedSamples++;
                return;
            }
            _owner.Samples.Add(new Sample(_phase, _start, end - _start, bytes, _items));
        }
    }
}
