using TextQuest.Application;
using TextQuest.Application.Abstractions;

namespace TextQuest.Tests;

internal sealed class FixedSeedRandomProvider : IRandomProvider
{
    private readonly int _seed;
    private readonly DeterministicRandomProvider _inner = new();

    public FixedSeedRandomProvider(int seed)
    {
        _seed = seed;
    }

    public int CreateSeed() => _seed;

    public int NextInt(int seed, string key, int minInclusive, int maxExclusive)
        => _inner.NextInt(seed, key, minInclusive, maxExclusive);
}
