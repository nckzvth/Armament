namespace Armament.SharedSim.Determinism;

public struct XorShift32
{
    private uint _state;

    public XorShift32(uint seed)
    {
        _state = seed == 0 ? 0x9E3779B9u : seed;
    }

    public uint State => _state;

    public uint NextUInt()
    {
        var x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }

    public float NextFloat01()
    {
        return (NextUInt() & 0x00FFFFFF) / 16777216f;
    }
}
