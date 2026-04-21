using System.Security.Cryptography;
using System.Text;
using TextQuest.Application.Abstractions;

namespace TextQuest.Application;

/// <summary>
/// Детерминированный генератор псевдослучайных значений на основе seed и ключа.
/// </summary>
public sealed class DeterministicRandomProvider : IRandomProvider
{
    public int CreateSeed() => RandomNumberGenerator.GetInt32(int.MaxValue);

    public int NextInt(int seed, string key, int minInclusive, int maxExclusive)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Random key must not be empty.", nameof(key));
        }

        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Random range must be non-empty.");
        }

        var range = maxExclusive - minInclusive;
        var input = Encoding.UTF8.GetBytes($"{seed}:{key}");
        var hash = SHA256.HashData(input);
        var value = BitConverter.ToInt32(hash, 0) & int.MaxValue;

        return minInclusive + (value % range);
    }
}
