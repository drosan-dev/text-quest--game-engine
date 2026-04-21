using System.Text;
using System.Text.RegularExpressions;
using TextQuest.Frontends.Contracts;

internal sealed class TerminalUi
{
    private readonly bool _ansiEnabled;
    private readonly bool _interactive;
    private readonly bool _unicode;
    private int _width;
    private int _height;
    private string _hr;
    private int _lastWidth;
    private int _lastHeight;

    private static readonly Regex AnsiRegex = new(@"\u001B\[[0-9;]*m", RegexOptions.Compiled);

    private TerminalUi(bool ansiEnabled, int width)
    {
        _ansiEnabled = ansiEnabled;
        _interactive = IsInteractiveTerminal();
        _unicode = IsUnicodePreferred();
        _width = Math.Clamp(width, 40, 240);
        _height = Math.Clamp(TryGetConsoleHeight() ?? 30, 15, 120);
        _hr = new string(_unicode ? '─' : '-', _width);
        _lastWidth = _width;
        _lastHeight = _height;
    }

    public static TerminalUi Create()
    {
        TryEnableUtf8Output();

        var ansiEnabled = IsAnsiEnabled();
        var width = TryGetConsoleWidth() ?? 100;
        return new TerminalUi(ansiEnabled, width);
    }

    public bool IsInteractive => _interactive;

    public IDisposable BeginSession()
    {
        if (!_interactive)
        {
            return NoopDisposable.Instance;
        }

        if (_ansiEnabled)
        {
            TryWrite(AnsiAltBufferOn());
            TryWrite(AnsiHideCursor());
        }
        else
        {
            TrySetCursorVisible(visible: false);
        }

        TryClear();
        return new SessionScope(_ansiEnabled);
    }

    public void RenderScreen(PresentableState state, int selectedIndex, TerminalStatus status)
    {
        if (!_interactive)
        {
            RenderStateLineMode(state);
            return;
        }

        RefreshSize();
        if (_width != _lastWidth || _height != _lastHeight)
        {
            _lastWidth = _width;
            _lastHeight = _height;
            _hr = new string(_unicode ? '─' : '-', _width);
            TryClear();
        }

        var lines = BuildScreenLines(state, selectedIndex, status);
        for (var row = 0; row < _height; row++)
        {
            TrySetCursor(0, row);
            var text = row < lines.Count ? lines[row] : string.Empty;
            WriteRowClipped(text, _width);
        }

        TrySetCursor(0, Math.Max(0, _height - 1));
    }

    public void RenderStateLineMode(PresentableState state)
    {
        WriteLine();
        WriteHeading(state.Title);

        foreach (var textBlock in state.TextBlocks)
        {
            WriteWrappedBlock(textBlock, indent: 2);
            WriteLine();
        }

        if (state.IsCompleted)
        {
            return;
        }

        if (state.Choices.Count > 0)
        {
            WriteChoices(state.Choices, selectedIndex: null);
        }

        WriteMutedLine("Команды: save [id] · load [id] · exit");
    }

    public void WritePrompt()
    {
        WriteLine();
        Write(Accent("> "));
    }

    public void WriteInfo(string message) => WriteLine(Muted(message));

    public void WriteWarning(string message) => WriteLine(Warn(message));

    public void WriteError(string message) => WriteLine(Error(message));

    public void WriteSuccess(string message) => WriteLine(Success(message));

    public TerminalAction ReadAction(PresentableState state, ref int selectedIndex)
    {
        if (!_interactive)
        {
            return TerminalAction.Noop;
        }

        if (state.Choices.Count == 0)
        {
            selectedIndex = 0;
        }
        else
        {
            selectedIndex = Math.Clamp(selectedIndex, 0, state.Choices.Count - 1);
        }

        while (true)
        {
            RefreshSize();
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.UpArrow)
            {
                if (state.Choices.Count > 0)
                {
                    selectedIndex = (selectedIndex - 1 + state.Choices.Count) % state.Choices.Count;
                    return TerminalAction.NavigationChanged;
                }

                continue;
            }

            if (key.Key == ConsoleKey.DownArrow)
            {
                if (state.Choices.Count > 0)
                {
                    selectedIndex = (selectedIndex + 1) % state.Choices.Count;
                    return TerminalAction.NavigationChanged;
                }

                continue;
            }

            if (key.Key == ConsoleKey.K)
            {
                if (state.Choices.Count > 0)
                {
                    selectedIndex = (selectedIndex - 1 + state.Choices.Count) % state.Choices.Count;
                    return TerminalAction.NavigationChanged;
                }

                continue;
            }

            if (key.Key == ConsoleKey.J)
            {
                if (state.Choices.Count > 0)
                {
                    selectedIndex = (selectedIndex + 1) % state.Choices.Count;
                    return TerminalAction.NavigationChanged;
                }

                continue;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                if (state.Choices.Count == 0)
                {
                    continue;
                }

                return TerminalAction.Choose(state.Choices[selectedIndex].Id);
            }

            if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Q)
            {
                return TerminalAction.Exit;
            }

            if (key.Key == ConsoleKey.S)
            {
                var id = PromptForText("Save id", defaultValue: "quick");
                return id is null ? TerminalAction.NavigationChanged : TerminalAction.Save(id);
            }

            if (key.Key == ConsoleKey.L)
            {
                var id = PromptForText("Load id", defaultValue: "quick");
                return id is null ? TerminalAction.NavigationChanged : TerminalAction.Load(id);
            }

            if (TryMapDigitKey(key, out var number) && number > 0 && number <= state.Choices.Count)
            {
                selectedIndex = number - 1;
                return TerminalAction.NavigationChanged;
            }
        }
    }

    private void WriteHeading(string title)
    {
        WriteLine(Title(title));
        WriteLine(Muted(_hr[..Math.Min(_hr.Length, Math.Max(10, Math.Min(title.Length, _width)))]));
        WriteLine();
    }

    private void WriteChoices(IReadOnlyList<ChoiceViewModel> choices, int? selectedIndex)
    {
        var numberWidth = choices.Count.ToString().Length;

        WriteLine(Muted("Варианты:"));
        for (var index = 0; index < choices.Count; index++)
        {
            var choice = choices[index];
            var number = (index + 1).ToString().PadLeft(numberWidth);
            var isSelected = selectedIndex.HasValue && selectedIndex.Value == index;
            var markerPlain = isSelected ? "> " : "  ";
            var markerStyled = isSelected ? Accent("› ") : Muted("  ");
            var prefixPlain = $"{markerPlain}{number}. ";
            var prefixStyled = $"{markerStyled}{Accent(number)}{Muted(".")} ";

            WriteWrappedHanging(prefixPlain, prefixStyled, choice.Text, indent: 2);
        }

        WriteLine();
    }

    private void WriteWrappedBlock(string text, int indent)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var indentText = new string(' ', indent);
        var availableWidth = Math.Max(20, _width - indent);

        foreach (var line in SplitLines(text))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                WriteLine();
                continue;
            }

            foreach (var wrapped in WrapLine(line, availableWidth))
            {
                WriteLine(indentText + wrapped);
            }
        }
    }

    private void WriteWrappedHanging(string prefixPlain, string prefixStyled, string text, int indent)
    {
        var indentText = new string(' ', indent);
        var availableWidth = Math.Max(20, _width - indent);

        var firstLineWidth = Math.Max(10, availableWidth - prefixPlain.Length);
        var wrapped = WrapLine(text, firstLineWidth).ToList();

        if (wrapped.Count == 0)
        {
            WriteLine(indentText + prefixStyled);
            return;
        }

        WriteLine(indentText + prefixStyled + wrapped[0]);
        var hangingIndent = indentText + new string(' ', prefixPlain.Length);

        for (var i = 1; i < wrapped.Count; i++)
        {
            WriteLine(hangingIndent + wrapped[i]);
        }
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            yield return line;
        }
    }

    private static IEnumerable<string> WrapLine(string text, int width)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            yield break;
        }

        var buffer = new StringBuilder(capacity: Math.Min(trimmed.Length, width));
        var index = 0;

        while (index < trimmed.Length)
        {
            while (index < trimmed.Length && char.IsWhiteSpace(trimmed[index]))
            {
                index++;
            }

            if (index >= trimmed.Length)
            {
                break;
            }

            var start = index;
            while (index < trimmed.Length && !char.IsWhiteSpace(trimmed[index]))
            {
                index++;
            }

            var word = trimmed.Substring(start, index - start);

            if (buffer.Length == 0)
            {
                if (word.Length <= width)
                {
                    buffer.Append(word);
                }
                else
                {
                    foreach (var chunk in BreakLongWord(word, width))
                    {
                        yield return chunk;
                    }
                }

                continue;
            }

            if (buffer.Length + 1 + word.Length <= width)
            {
                buffer.Append(' ').Append(word);
                continue;
            }

            yield return buffer.ToString();
            buffer.Clear();

            if (word.Length <= width)
            {
                buffer.Append(word);
            }
            else
            {
                foreach (var chunk in BreakLongWord(word, width))
                {
                    yield return chunk;
                }
            }
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString();
        }
    }

    private static IEnumerable<string> BreakLongWord(string word, int width)
    {
        for (var i = 0; i < word.Length; i += width)
        {
            yield return word.Substring(i, Math.Min(width, word.Length - i));
        }
    }

    private static int? TryGetConsoleWidth()
    {
        try
        {
            if (Console.IsOutputRedirected)
            {
                return null;
            }

            return Console.WindowWidth;
        }
        catch
        {
            return null;
        }
    }

    private static int? TryGetConsoleHeight()
    {
        try
        {
            if (Console.IsOutputRedirected)
            {
                return null;
            }

            return Console.WindowHeight;
        }
        catch
        {
            return null;
        }
    }

    private void RefreshSize()
    {
        _width = Math.Clamp(TryGetConsoleWidth() ?? _width, 40, 240);
        _height = Math.Clamp(TryGetConsoleHeight() ?? _height, 15, 120);
    }

    private static bool IsInteractiveTerminal()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return false;
        }

        try
        {
            var left = Console.CursorLeft;
            var top = Console.CursorTop;
            Console.SetCursorPosition(left, top);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAnsiEnabled()
    {
        if (Console.IsOutputRedirected)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
        {
            return false;
        }

        var term = Environment.GetEnvironmentVariable("TERM");
        if (string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsUnicodePreferred()
    {
        try
        {
            return Console.OutputEncoding.WebName.Contains("utf", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void TryEnableUtf8Output()
    {
        try
        {
            if (!Console.IsOutputRedirected)
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
        }
        catch
        {
        }
    }

    private void WriteLine() => Console.WriteLine();

    private void WriteLine(string text) => Console.WriteLine(text);

    private void Write(string text) => Console.Write(text);

    private string Title(string text) => _ansiEnabled ? $"\u001b[1m\u001b[36m{text}\u001b[0m" : text;

    private string Accent(string text) => _ansiEnabled ? $"\u001b[36m{text}\u001b[0m" : text;

    private string Muted(string text) => _ansiEnabled ? $"\u001b[90m{text}\u001b[0m" : text;

    private string Warn(string text) => _ansiEnabled ? $"\u001b[33m{text}\u001b[0m" : text;

    private string Error(string text) => _ansiEnabled ? $"\u001b[31m{text}\u001b[0m" : text;

    private string Success(string text) => _ansiEnabled ? $"\u001b[32m{text}\u001b[0m" : text;

    private void WriteMutedLine(string text) => WriteLine(Muted(text));

    private static bool TryMapDigitKey(ConsoleKeyInfo key, out int number)
    {
        number = 0;
        if (key.Key is >= ConsoleKey.D0 and <= ConsoleKey.D9)
        {
            number = key.Key - ConsoleKey.D0;
            return true;
        }

        if (key.Key is >= ConsoleKey.NumPad0 and <= ConsoleKey.NumPad9)
        {
            number = key.Key - ConsoleKey.NumPad0;
            return true;
        }

        return false;
    }

    private List<string> BuildScreenLines(PresentableState state, int selectedIndex, TerminalStatus status)
    {
        var innerWidth = Math.Max(10, _width - 2);

        // If the terminal is extremely small, keep the layout simple.
        if (_height < 12 || _width < 40)
        {
            var fallback = new List<string>(capacity: _height)
            {
                Title(state.Title),
                Muted(_hr[..Math.Min(_hr.Length, Math.Max(10, Math.Min(VisibleLength(state.Title), _width)))]),
                string.Empty,
            };

            var contentLines = BuildContentLines(state, width: _width, maxLines: Math.Max(1, _height - 6));
            fallback.AddRange(contentLines);

            var statusLine = FormatStatus(status);
            fallback.Add(string.Empty);
            fallback.Add(statusLine);
            fallback.Add(Muted("↑/↓ выбрать · Enter подтвердить · Q выйти"));
            return fallback.Take(_height).ToList();
        }

        var fixedLines = 8; // frame + title + separators + status/help
        var innerHeight = _height - fixedLines;
        if (innerHeight < 4)
        {
            return new List<string>(_height) { Title(state.Title) };
        }

        var choicesCount = state.IsCompleted ? 0 : state.Choices.Count;
        var minChoicesHeight = choicesCount > 0 ? 3 : 1;
        var desiredChoicesHeight = choicesCount > 0
            ? Math.Min(innerHeight - 1, Math.Max(minChoicesHeight, innerHeight / 3))
            : minChoicesHeight;

        var choicesHeight = desiredChoicesHeight;
        var contentHeight = Math.Max(1, innerHeight - choicesHeight);

        var lines = new List<string>(capacity: _height);

        lines.Add(MakeTopBorder(innerWidth));
        lines.Add(MakeTitleRow(state.Title, innerWidth));
        lines.Add(MakeDivider(innerWidth, "Текст"));

        var content = BuildContentLines(state, width: innerWidth, maxLines: contentHeight);
        lines.AddRange(content.Select(line => MakeFrameRow(innerWidth, line)));

        lines.Add(MakeDivider(innerWidth, "Выбор"));

        var choiceRows = BuildChoiceAreaLines(state, selectedIndex, innerWidth, choicesHeight);
        lines.AddRange(choiceRows.Select(line => MakeFrameRow(innerWidth, line)));

        lines.Add(MakeDivider(innerWidth, "Статус"));

        lines.Add(MakeFrameRow(innerWidth, FormatStatus(status)));
        lines.Add(MakeFrameRow(innerWidth, Muted("↑/↓ (J/K) · 1..9 выбрать · Enter подтвердить · S save · L load · Q выйти")));
        lines.Add(MakeBottomBorder(innerWidth));

        return lines.Take(_height).ToList();
    }

    private IEnumerable<string> BuildContentLines(PresentableState state, int width, int maxLines)
    {
        var availableWidth = Math.Max(10, width - 2);
        var output = new List<string>();

        foreach (var textBlock in state.TextBlocks)
        {
            foreach (var line in SplitLines(textBlock))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    output.Add(string.Empty);
                    continue;
                }

                foreach (var wrapped in WrapLine(line, availableWidth))
                {
                    output.Add(" " + wrapped);
                }
            }

            output.Add(string.Empty);

            if (output.Count >= maxLines)
            {
                break;
            }
        }

        if (state.IsCompleted)
        {
            output.Add(string.Empty);
            output.Add(" " + Muted("Квест завершён."));
            if (!string.IsNullOrWhiteSpace(state.Result))
            {
                output.Add(" " + Success($"Результат: {state.Result}"));
            }
        }

        if (output.Count > maxLines)
        {
            output = output.Take(maxLines).ToList();
            output[^1] = Muted(" …");
        }

        while (output.Count < maxLines)
        {
            output.Add(string.Empty);
        }

        return output;
    }

    private IEnumerable<string> BuildChoiceAreaLines(PresentableState state, int selectedIndex, int width, int height)
    {
        if (state.IsCompleted || state.Choices.Count == 0)
        {
            return Enumerable.Repeat(Muted(" (нет доступных вариантов)"), height);
        }

        var choices = state.Choices;
        selectedIndex = Math.Clamp(selectedIndex, 0, choices.Count - 1);

        var topEllipsisNeeded = false;
        var bottomEllipsisNeeded = false;

        var show = height;
        var startGuess = selectedIndex - show / 2;
        if (startGuess < 0) startGuess = 0;
        if (startGuess > choices.Count - show) startGuess = Math.Max(0, choices.Count - show);

        topEllipsisNeeded = startGuess > 0;
        bottomEllipsisNeeded = startGuess + show < choices.Count;

        if (topEllipsisNeeded) show--;
        if (bottomEllipsisNeeded) show--;

        show = Math.Max(1, show);

        var start = selectedIndex - show / 2;
        if (start < 0) start = 0;
        if (start > choices.Count - show) start = Math.Max(0, choices.Count - show);

        var result = new List<string>(capacity: height);

        if (topEllipsisNeeded)
        {
            result.Add(Muted(" …"));
        }

        var numberWidth = choices.Count.ToString().Length;
        for (var i = 0; i < show && start + i < choices.Count; i++)
        {
            var index = start + i;
            var choice = choices[index];
            var number = (index + 1).ToString().PadLeft(numberWidth);

            var prefix = $" {number}. ";
            var plainText = $"{prefix}{choice.Text}";
            plainText = TrimToWidth(plainText, width);

            if (index == selectedIndex && _ansiEnabled)
            {
                result.Add(Invert(PadRightVisible(plainText, width)));
            }
            else if (index == selectedIndex)
            {
                result.Add(PadRightVisible($"> {plainText}".TrimStart(), width));
            }
            else
            {
                result.Add(PadRightVisible(plainText, width));
            }
        }

        if (bottomEllipsisNeeded)
        {
            result.Add(Muted(" …"));
        }

        while (result.Count < height)
        {
            result.Add(string.Empty);
        }

        return result.Take(height).ToList();
    }

    private string FormatStatus(TerminalStatus status)
    {
        return status.Kind switch
        {
            TerminalStatusKind.None => string.Empty,
            TerminalStatusKind.Info => Muted(status.Message),
            TerminalStatusKind.Warning => Warn(status.Message),
            TerminalStatusKind.Error => Error(status.Message),
            TerminalStatusKind.Success => Success(status.Message),
            _ => status.Message,
        };
    }

    private string MakeFrameRow(int innerWidth, string innerContent)
    {
        var content = PadRightVisible(TrimToWidth(innerContent, innerWidth), innerWidth);
        var v = _unicode ? '│' : '|';
        return $"{v}{content}{v}";
    }

    private string MakeTitleRow(string title, int innerWidth)
    {
        var styled = Title(title);
        var centered = CenterVisible(styled, innerWidth);
        var v = _unicode ? '│' : '|';
        return $"{v}{centered}{v}";
    }

    private string MakeTopBorder(int innerWidth)
    {
        if (!_unicode)
        {
            return $"+{new string('-', innerWidth)}+";
        }

        return $"┌{new string('─', innerWidth)}┐";
    }

    private string MakeBottomBorder(int innerWidth)
    {
        if (!_unicode)
        {
            return $"+{new string('-', innerWidth)}+";
        }

        return $"└{new string('─', innerWidth)}┘";
    }

    private string MakeDivider(int innerWidth, string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            if (!_unicode)
            {
                return $"+{new string('-', innerWidth)}+";
            }

            return $"├{new string('─', innerWidth)}┤";
        }

        var cleanLabel = $" {label.Trim()} ";
        if (VisibleLength(cleanLabel) > innerWidth - 2)
        {
            cleanLabel = cleanLabel[..Math.Max(0, innerWidth - 2)];
        }

        var remaining = innerWidth - VisibleLength(cleanLabel);
        var left = remaining / 2;
        var right = remaining - left;
        if (!_unicode)
        {
            return $"+{new string('-', innerWidth)}+";
        }

        return $"├{new string('─', left)}{Muted(cleanLabel)}{new string('─', right)}┤";
    }

    private string Invert(string text) => _ansiEnabled ? $"\u001b[7m{text}\u001b[0m" : text;

    private static string TrimToWidth(string text, int width)
    {
        var plain = StripAnsi(text);
        if (plain.Length <= width)
        {
            return text;
        }

        var trimmed = plain[..Math.Max(0, width)];
        return trimmed;
    }

    private static string PadRightVisible(string text, int width)
    {
        var plain = StripAnsi(text);
        if (plain.Length >= width)
        {
            return text;
        }

        return text + new string(' ', width - plain.Length);
    }

    private static string CenterVisible(string text, int width)
    {
        var plain = StripAnsi(text);
        if (plain.Length >= width)
        {
            return plain[..width];
        }

        var left = (width - plain.Length) / 2;
        var right = width - plain.Length - left;
        return new string(' ', left) + text + new string(' ', right);
    }

    private void WriteRowClipped(string text, int width)
    {
        var plainLength = VisibleLength(text);
        if (plainLength > width)
        {
            var clipped = StripAnsi(text);
            if (clipped.Length > width)
            {
                clipped = clipped[..width];
            }

            Console.Write(clipped);
            return;
        }

        Console.Write(text);
        if (plainLength < width)
        {
            Console.Write(new string(' ', width - plainLength));
        }
    }

    private static string StripAnsi(string text) => AnsiRegex.Replace(text, string.Empty);

    private static int VisibleLength(string text) => StripAnsi(text).Length;

    private static void TrySetCursor(int left, int top)
    {
        try
        {
            Console.SetCursorPosition(left, top);
        }
        catch
        {
        }
    }

    private static void TryClear()
    {
        try
        {
            Console.Clear();
        }
        catch
        {
        }
    }

    private static void TryWrite(string text)
    {
        try
        {
            Console.Write(text);
        }
        catch
        {
        }
    }

    private static string AnsiAltBufferOn() => "\u001b[?1049h";

    private static string AnsiAltBufferOff() => "\u001b[?1049l";

    private static string AnsiHideCursor() => "\u001b[?25l";

    private static string AnsiShowCursor() => "\u001b[?25h";

    private string? PromptForText(string title, string defaultValue)
    {
        RefreshSize();

        var promptPrefix = $"{title} (Esc отмена, Enter = '{defaultValue}'): ";
        var buffer = new StringBuilder();

        while (true)
        {
            TrySetCursor(0, Math.Max(0, _height - 1));
            WriteRowClipped(Muted(promptPrefix) + buffer.ToString(), _width);
            TrySetCursor(Math.Min(_width - 1, VisibleLength(promptPrefix) + buffer.Length), Math.Max(0, _height - 1));

            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Escape)
            {
                return null;
            }

            if (key.Key == ConsoleKey.Enter)
            {
                return buffer.Length == 0 ? defaultValue : buffer.ToString().Trim();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar) && buffer.Length < 64)
            {
                buffer.Append(key.KeyChar);
            }
        }
    }

    private sealed class SessionScope : IDisposable
    {
        private readonly bool _ansiEnabled;
        private bool _disposed;

        public SessionScope(bool ansiEnabled) => _ansiEnabled = ansiEnabled;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_ansiEnabled)
            {
                TryWrite(AnsiShowCursor());
                TryWrite(AnsiAltBufferOff());
            }
            else
            {
                TrySetCursorVisible(visible: true);
            }
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();
        public void Dispose()
        {
        }
    }

    private static void TrySetCursorVisible(bool visible)
    {
        try
        {
            Console.CursorVisible = visible;
        }
        catch
        {
        }
    }
}

internal enum TerminalStatusKind
{
    None,
    Info,
    Warning,
    Error,
    Success,
}

internal readonly record struct TerminalStatus(TerminalStatusKind Kind, string Message)
{
    public static TerminalStatus None => new(TerminalStatusKind.None, string.Empty);
}

internal enum TerminalActionKind
{
    Noop,
    NavigationChanged,
    Exit,
    Choose,
    Save,
    Load,
}

internal readonly record struct TerminalAction(TerminalActionKind Kind, string? Argument)
{
    public static TerminalAction Noop => new(TerminalActionKind.Noop, null);
    public static TerminalAction NavigationChanged => new(TerminalActionKind.NavigationChanged, null);
    public static TerminalAction Exit => new(TerminalActionKind.Exit, null);
    public static TerminalAction Choose(string choiceId) => new(TerminalActionKind.Choose, choiceId);
    public static TerminalAction Save(string id) => new(TerminalActionKind.Save, id);
    public static TerminalAction Load(string id) => new(TerminalActionKind.Load, id);
}
