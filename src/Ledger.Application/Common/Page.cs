namespace Ledger.Application.Common;

/// <summary>
/// Страница для курсорной пагинации. Курсор — непрозрачная строка (у нас это Sequence
/// последнего элемента). В отличие от offset-пагинации не «едет» при вставках и
/// не деградирует на больших смещениях (OFFSET 1000000 читает миллион строк).
/// </summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor);
