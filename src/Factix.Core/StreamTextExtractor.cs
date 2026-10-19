using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Factix
{
    public static class StreamTextExtractor
    {
        public static bool TryRead(byte[] data, out IReadOnlyList<TextPage> pages)
        {
            pages = Array.Empty<TextPage>();
            if (!LooksLikePostScript(data))
                return false;

            var text = Win1252.Decode(data, 0, data.Length);
            var runs = new List<TextRun>();
            var stack = new List<double>();
            double pageWidth = 612;
            double pageHeight = 792;
            double fontSize = 12;
            double currentX = 36;
            double currentY = pageHeight - 36;
            var i = 0;

            while (i < text.Length)
            {
                i = SkipSpace(text, i);
                if (i >= text.Length)
                    break;

                if (text[i] == '%')
                {
                    var line = ReadLine(text, i);
                    ReadBoundingBox(line, ref pageWidth, ref pageHeight);
                    i += line.Length;
                    continue;
                }

                if (text[i] == '(')
                {
                    var literal = ReadLiteral(text, ref i);
                    i = SkipSpace(text, i);
                    if (StartsWithWord(text, i, "show") || StartsWithWord(text, i, "ashow") || StartsWithWord(text, i, "widthshow") || StartsWithWord(text, i, "Tj"))
                    {
                        Emit(runs, literal, ref currentX, currentY, fontSize, pageHeight);
                        i = SkipWord(text, i);
                    }

                    continue;
                }

                if (text[i] == '<' && i + 1 < text.Length && text[i + 1] != '<')
                {
                    var literal = ReadHex(text, ref i);
                    i = SkipSpace(text, i);
                    if (literal.Length > 0 && (StartsWithWord(text, i, "show") || StartsWithWord(text, i, "Tj")))
                    {
                        Emit(runs, literal, ref currentX, currentY, fontSize, pageHeight);
                        i = SkipWord(text, i);
                    }

                    continue;
                }

                if (IsNumberStart(text, i))
                {
                    stack.Add(ReadNumber(text, ref i));
                    if (stack.Count > 32)
                        stack.RemoveAt(0);
                    continue;
                }

                if (text[i] == '/')
                {
                    i = SkipWord(text, i);
                    continue;
                }

                var word = ReadWord(text, ref i);
                if (word.Length == 0)
                {
                    i++;
                    continue;
                }

                if (word == "moveto" && stack.Count >= 2)
                {
                    currentX = stack[stack.Count - 2];
                    currentY = stack[stack.Count - 1];
                }
                else if ((word == "Tf" || word == "selectfont") && stack.Count >= 1)
                {
                    var size = stack[stack.Count - 1];
                    if (size > 0 && size < 200)
                        fontSize = size;
                }

                stack.Clear();
            }

            if (runs.Count == 0)
            {
                runs.Add(new TextRun(
                    1,
                    "El trabajo se guardó, pero el controlador no envió texto legible.",
                    36,
                    36,
                    480,
                    12,
                    "PS"));
            }

            pages = new[] { new TextPage(1, pageWidth, pageHeight, runs) };
            return true;
        }

        private static bool LooksLikePostScript(byte[] data)
        {
            var i = 0;
            while (i < data.Length && (data[i] == ' ' || data[i] == '\t' || data[i] == '\r' || data[i] == '\n'))
                i++;
            return i + 1 < data.Length && data[i] == (byte)'%' && data[i + 1] == (byte)'!';
        }

        private static void Emit(List<TextRun> runs, string literal, ref double x, double baseline, double fontSize, double pageHeight)
        {
            if (string.IsNullOrEmpty(literal))
                return;
            var top = pageHeight - baseline - fontSize;
            if (top < 0)
                top = 0;
            var width = Math.Max(literal.Length * fontSize * 0.5, 4);
            runs.Add(new TextRun(1, literal, x, top, width, fontSize, "PS"));
            x += width;
        }

        private static void ReadBoundingBox(string line, ref double pageWidth, ref double pageHeight)
        {
            var marker = "%%BoundingBox:";
            var at = line.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0)
                return;
            var numbers = new List<double>();
            var i = at + marker.Length;
            while (i < line.Length)
            {
                i = SkipSpace(line, i);
                if (i >= line.Length || !IsNumberStart(line, i))
                    break;
                numbers.Add(ReadNumber(line, ref i));
            }

            if (numbers.Count < 4)
                return;
            var width = numbers[2] - numbers[0];
            var height = numbers[3] - numbers[1];
            if (width > 72 && height > 72)
            {
                pageWidth = width;
                pageHeight = height;
            }
        }

        private static string ReadLiteral(string text, ref int i)
        {
            var value = new StringBuilder();
            i++;
            while (i < text.Length)
            {
                var ch = text[i++];
                if (ch == '\\' && i < text.Length)
                {
                    var escaped = text[i++];
                    if (escaped == 'n')
                        value.Append('\n');
                    else if (escaped == 'r')
                        value.Append('\r');
                    else if (escaped == 't')
                        value.Append('\t');
                    else if (escaped >= '0' && escaped <= '7')
                    {
                        var octal = escaped - '0';
                        var digits = 1;
                        while (digits < 3 && i < text.Length && text[i] >= '0' && text[i] <= '7')
                        {
                            octal = (octal * 8) + (text[i++] - '0');
                            digits++;
                        }

                        value.Append((char)octal);
                    }
                    else
                        value.Append(escaped);
                    continue;
                }

                if (ch == ')')
                    break;
                value.Append(ch);
            }

            return value.ToString();
        }

        private static string ReadHex(string text, ref int i)
        {
            var hex = new StringBuilder();
            i++;
            while (i < text.Length && text[i] != '>')
            {
                if (!char.IsWhiteSpace(text[i]))
                    hex.Append(text[i]);
                i++;
            }

            if (i < text.Length && text[i] == '>')
                i++;
            if (hex.Length < 2 || hex.Length % 2 != 0)
                return "";

            var bytes = new byte[hex.Length / 2];
            for (var n = 0; n < bytes.Length; n++)
            {
                if (!byte.TryParse(hex.ToString(n * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[n]))
                    return "";
                if (bytes[n] < 32 || bytes[n] > 126)
                    return "";
            }

            return Encoding.ASCII.GetString(bytes);
        }

        private static string ReadLine(string text, int i)
        {
            var end = i;
            while (end < text.Length && text[end] != '\n' && text[end] != '\r')
                end++;
            if (end < text.Length && text[end] == '\r')
                end++;
            if (end < text.Length && text[end] == '\n')
                end++;
            return text.Substring(i, end - i);
        }

        private static bool IsNumberStart(string text, int i)
        {
            if (text[i] == '-' || text[i] == '+')
                return i + 1 < text.Length && (char.IsDigit(text[i + 1]) || text[i + 1] == '.');
            return char.IsDigit(text[i]) || (text[i] == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1]));
        }

        private static double ReadNumber(string text, ref int i)
        {
            var start = i;
            if (text[i] == '-' || text[i] == '+')
                i++;
            while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'))
                i++;
            double.TryParse(text.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number);
            return number;
        }

        private static string ReadWord(string text, ref int i)
        {
            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '(' && text[i] != ')' && text[i] != '<' && text[i] != '>' && text[i] != '%' && text[i] != '[' && text[i] != ']')
                i++;
            return text.Substring(start, i - start);
        }

        private static int SkipWord(string text, int i)
        {
            ReadWord(text, ref i);
            return i;
        }

        private static int SkipSpace(string text, int i)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i]))
                i++;
            return i;
        }

        private static bool StartsWithWord(string text, int i, string word)
        {
            if (i + word.Length > text.Length)
                return false;
            if (!string.Equals(text.Substring(i, word.Length), word, StringComparison.Ordinal))
                return false;
            var end = i + word.Length;
            return end >= text.Length || char.IsWhiteSpace(text[end]) || text[end] == '%' || text[end] == '(' || text[end] == '/';
        }
    }
}
