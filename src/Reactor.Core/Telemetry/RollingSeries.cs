namespace Reactor.Core.Telemetry;

/// <summary>
/// Fixed-capacity ring buffer of samples. Gaps (a sensor that went missing for
/// a poll) are stored as null so the graph can break the line instead of
/// pretending the value dropped to zero.
/// </summary>
public sealed class RollingSeries
{
    private readonly double?[] _buffer;
    private int _start;   // index of the oldest sample

    public RollingSeries(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new double?[capacity];
    }

    public int Capacity => _buffer.Length;

    public int Count { get; private set; }

    public double? Latest => Count == 0 ? null : _buffer[(_start + Count - 1) % Capacity];

    public void Add(double? value)
    {
        if (Count < Capacity)
        {
            _buffer[(_start + Count) % Capacity] = value;
            Count++;
        }
        else
        {
            _buffer[_start] = value;
            _start = (_start + 1) % Capacity;
        }
    }

    public void Clear()
    {
        Array.Clear(_buffer);
        _start = 0;
        Count = 0;
    }

    /// <summary>Oldest-first copy of the current contents.</summary>
    public double?[] ToArray()
    {
        var result = new double?[Count];
        for (var i = 0; i < Count; i++)
            result[i] = _buffer[(_start + i) % Capacity];
        return result;
    }

    public double? Max()
    {
        double? max = null;
        for (var i = 0; i < Count; i++)
        {
            var v = _buffer[(_start + i) % Capacity];
            if (v.HasValue && (!max.HasValue || v.Value > max.Value)) max = v;
        }
        return max;
    }

    public double? Min()
    {
        double? min = null;
        for (var i = 0; i < Count; i++)
        {
            var v = _buffer[(_start + i) % Capacity];
            if (v.HasValue && (!min.HasValue || v.Value < min.Value)) min = v;
        }
        return min;
    }
}
