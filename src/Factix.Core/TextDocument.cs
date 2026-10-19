using System.Collections.Generic;
using System.Linq;

namespace Factix
{
    public sealed class TextRun
    {
        public TextRun(int page, string text, double x, double y, double width, double height, string font)
        {
            Page = page;
            Text = text ?? "";
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Font = font ?? "";
        }

        public int Page { get; }
        public string Text { get; }
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }
        public string Font { get; }

        public double Right => X + Width;
        public double Bottom => Y + Height;
    }

    public sealed class TextPage
    {
        public TextPage(int number, double width, double height, IReadOnlyList<TextRun> runs)
        {
            Number = number;
            Width = width;
            Height = height;
            Runs = runs;
        }

        public int Number { get; }
        public double Width { get; }
        public double Height { get; }
        public IReadOnlyList<TextRun> Runs { get; }
    }

    public sealed class TextDocument
    {
        public static readonly TextDocument Empty = new TextDocument(new TextPage[0]);

        public TextDocument(IReadOnlyList<TextPage> pages)
        {
            Pages = pages ?? new TextPage[0];
        }

        public IReadOnlyList<TextPage> Pages { get; }

        public IEnumerable<TextRun> Runs => Pages.SelectMany(page => page.Runs);
    }
}
