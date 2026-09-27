namespace ExactFrame.Core.Help;

public enum HelpBlockKind
{
    Paragraph,
    Steps,
    Bullets,
    Tip
}

/// <summary>
/// One block of help text. Text may mark UI labels as <c>**bold**</c>.
/// Paragraphs and tips use <see cref="Text"/>; steps and bullets use <see cref="Items"/>.
/// </summary>
public sealed record HelpBlock(HelpBlockKind Kind, string Text, IReadOnlyList<string> Items)
{
    public static HelpBlock Paragraph(string text) => new(HelpBlockKind.Paragraph, text, []);

    public static HelpBlock Tip(string text) => new(HelpBlockKind.Tip, text, []);

    public static HelpBlock Steps(params string[] items) => new(HelpBlockKind.Steps, string.Empty, items);

    public static HelpBlock Bullets(params string[] items) => new(HelpBlockKind.Bullets, string.Empty, items);

    public IEnumerable<string> AllText() => Items.Prepend(Text);
}

public sealed record HelpSection(string Heading, IReadOnlyList<HelpBlock> Blocks);

/// <summary>What extra, live content a topic shows above its sections.</summary>
public enum HelpTopicExtra
{
    None,
    Shortcuts,
    About
}

public sealed record HelpTopic(
    string Id,
    string Title,
    string Glyph,
    string Summary,
    IReadOnlyList<HelpSection> Sections,
    HelpTopicExtra Extra = HelpTopicExtra.None)
{
    /// <summary>Case-insensitive match against the title, summary, headings and text. Bold markers are ignored.</summary>
    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string text = string.Join('\n', SearchableText()).Replace("**", string.Empty);
        return words.All(w => text.Contains(w, StringComparison.CurrentCultureIgnoreCase));
    }

    private IEnumerable<string> SearchableText()
    {
        yield return Title;
        yield return Summary;
        foreach (var section in Sections)
        {
            yield return section.Heading;
            foreach (var block in section.Blocks)
                foreach (string text in block.AllText())
                    yield return text;
        }
    }
}

/// <summary>A shortcut that can't be changed, listed with the global hotkeys in help.</summary>
public sealed record ShortcutRow(string Label, IReadOnlyList<string> Keys);
