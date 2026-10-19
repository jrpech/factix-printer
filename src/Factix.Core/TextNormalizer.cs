using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Factix
{
    public static class TextNormalizer
    {
        public static TextDocument ReadFile(string path)
        {
            return ReadBytes(File.ReadAllBytes(path));
        }

        public static TextDocument ReadBytes(byte[] data)
        {
            if (data == null || data.Length == 0)
                return TextDocument.Empty;

            IReadOnlyList<TextPage> pages;
            if (XpsTextExtractor.IsPackage(data))
                pages = XpsTextExtractor.Read(data);
            else if (EmfTextExtractor.IsEmf(data, 0, data.Length) || EmfTextExtractor.IsSpool(data))
                pages = EmfTextExtractor.Read(data);
            else if (StreamTextExtractor.TryRead(data, out var streamPages))
                pages = streamPages;
            else
                pages = ReadPlainText(data);

            return new TextDocument(MergePages(pages));
        }

        private static IReadOnlyList<TextPage> MergePages(IReadOnlyList<TextPage> pages)
        {
            var merged = new List<TextPage>(pages.Count);
            foreach (var page in pages)
                merged.Add(new TextPage(page.Number, page.Width, page.Height, MergeRuns(page.Runs)));
            return merged;
        }

        public static IReadOnlyList<TextRun> MergeRuns(IReadOnlyList<TextRun> runs)
        {
            var ordered = new List<TextRun>(runs);
            ordered.Sort(CompareRuns);
            var merged = new List<TextRun>();
            foreach (var run in ordered)
            {
                if (string.IsNullOrEmpty(run.Text))
                    continue;
                if (merged.Count == 0 || !CanMerge(merged[merged.Count - 1], run))
                {
                    merged.Add(run);
                    continue;
                }

                merged[merged.Count - 1] = Combine(merged[merged.Count - 1], run);
            }

            return merged;
        }

        private static int CompareRuns(TextRun left, TextRun right)
        {
            var page = left.Page.CompareTo(right.Page);
            if (page != 0)
                return page;
            var y = left.Y.CompareTo(right.Y);
            return y != 0 ? y : left.X.CompareTo(right.X);
        }

        private static bool CanMerge(TextRun left, TextRun right)
        {
            if (left.Page != right.Page)
                return false;
            var height = Math.Max(Math.Max(left.Height, right.Height), 1);
            if (Math.Abs(left.Height - right.Height) > (height * 0.25) + 0.5)
                return false;
            var leftMid = left.Y + (left.Height / 2);
            var rightMid = right.Y + (right.Height / 2);
            if (Math.Abs(leftMid - rightMid) > height * 0.45)
                return false;
            var gap = right.X - left.Right;
            return gap >= -1 && gap <= height * 0.55;
        }

        private static TextRun Combine(TextRun left, TextRun right)
        {
            var gap = right.X - left.Right;
            var space = gap > Math.Max(left.Height, 1) * 0.12 ? " " : "";
            return new TextRun(
                left.Page,
                left.Text + space + right.Text,
                left.X,
                Math.Min(left.Y, right.Y),
                right.Right - left.X,
                Math.Max(left.Height, right.Height),
                string.IsNullOrEmpty(left.Font) ? right.Font : left.Font);
        }

        private static IReadOnlyList<TextPage> ReadPlainText(byte[] data)
        {
            var text = DecodeText(data);
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var runs = new List<TextRun>();
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length == 0)
                    continue;
                runs.Add(new TextRun(1, lines[i], 36, 36 + (i * 14), Math.Max(20, lines[i].Length * 7), 12, "Consolas"));
            }

            return new[] { new TextPage(1, 612, 792, runs) };
        }

        private static string DecodeText(byte[] data)
        {
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
                return Encoding.Unicode.GetString(data, 2, data.Length - 2);
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                return Encoding.UTF8.GetString(data, 3, data.Length - 3);
            return Encoding.UTF8.GetString(data);
        }
    }
}
