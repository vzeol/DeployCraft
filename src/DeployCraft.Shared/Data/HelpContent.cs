namespace DeployCraft.Server.Data;

/// <summary>
/// Built-in console help, written in plain text (Help\aide.txt):
/// "## Title {#id}" starts a topic, "###" a subheading, "-" a bullet, any other line a paragraph.
/// </summary>
public static class HelpContent
{
    public enum BlockKind { Paragraph, Heading, Bullet }

    public record Block(BlockKind Kind, string Text);

    public record Topic(string Id, string Title, List<Block> Blocks)
    {
        /// <summary>The topic contains all the searched words (title or text, case-insensitive).</summary>
        public bool Matches(string search) =>
            search.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(word =>
                Title.Contains(word, StringComparison.CurrentCultureIgnoreCase)
                || Blocks.Any(b => b.Text.Contains(word, StringComparison.CurrentCultureIgnoreCase)));
    }

    public static List<Topic> Parse(string text)
    {
        var topics = new List<Topic>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("## "))
            {
                var title = line[3..].Trim();
                var id = "";
                var anchor = title.LastIndexOf("{#", StringComparison.Ordinal);
                if (anchor >= 0 && title.EndsWith('}'))
                {
                    id = title[(anchor + 2)..^1];
                    title = title[..anchor].Trim();
                }
                topics.Add(new Topic(id, title, []));
            }
            else if (topics.Count == 0) continue; // text before the first topic: ignored
            else if (line.StartsWith("### ")) topics[^1].Blocks.Add(new Block(BlockKind.Heading, line[4..].Trim()));
            else if (line.StartsWith("- ")) topics[^1].Blocks.Add(new Block(BlockKind.Bullet, line[2..].Trim()));
            else topics[^1].Blocks.Add(new Block(BlockKind.Paragraph, line));
        }
        return topics;
    }
}
