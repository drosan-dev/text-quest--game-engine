namespace TextQuest.Application.Abstractions;

/// <summary>
/// Предоставляет детерминированные случайные значения на основе seed и ключа.
/// </summary>
public interface IRandomProvider
{
    /// <summary>
    /// Создаёт seed для новой игровой сессии.
    /// </summary>
    int CreateSeed();

    /// <summary>
    /// Возвращает детерминированное целое число в диапазоне [minInclusive, maxExclusive) для заданного seed и ключа.
    /// </summary>
    int NextInt(int seed, string key, int minInclusive, int maxExclusive);
}
