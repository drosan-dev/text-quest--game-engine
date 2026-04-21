using TextQuest.Domain.Models;

namespace TextQuest.Application.Abstractions;

/// <summary>
/// Defines the contract for rendering templated text based on game state.
/// </summary>
public interface ITextRenderer
{
    /// <summary>
    /// Renders a templated text string using the provided game state.
    /// </summary>
    /// <param name="template">The templated text string.</param>
    /// <param name="gameState">The current game state containing variables and flags.</param>
    /// <returns>The rendered text with templates resolved.</returns>
    string Render(string template, GameState gameState);
}