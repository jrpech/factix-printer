using System.Collections.Generic;

namespace Factix
{
    internal static class Win1252
    {
        private static readonly char[] Map = BuildMap();

        public static string Decode(byte[] data, int offset, int count)
        {
            var chars = new char[count];
            for (var i = 0; i < count; i++)
                chars[i] = Map[data[offset + i]];
            return new string(chars);
        }

        public static byte[] Encode(string text)
        {
            var bytes = new byte[text.Length];
            for (var i = 0; i < text.Length; i++)
                bytes[i] = EncodeChar(text[i]);
            return bytes;
        }

        private static byte EncodeChar(char value)
        {
            if (value < 0x80)
                return (byte)value;

            for (var i = 0x80; i < 256; i++)
            {
                if (Map[i] == value)
                    return (byte)i;
            }

            return (byte)'?';
        }

        private static char[] BuildMap()
        {
            var map = new char[256];
            for (var i = 0; i < 256; i++)
                map[i] = (char)i;

            var specials = new Dictionary<int, char>
            {
                { 0x80, '\u20AC' }, { 0x82, '\u201A' }, { 0x83, '\u0192' }, { 0x84, '\u201E' },
                { 0x85, '\u2026' }, { 0x86, '\u2020' }, { 0x87, '\u2021' }, { 0x88, '\u02C6' },
                { 0x89, '\u2030' }, { 0x8A, '\u0160' }, { 0x8B, '\u2039' }, { 0x8C, '\u0152' },
                { 0x8E, '\u017D' }, { 0x91, '\u2018' }, { 0x92, '\u2019' }, { 0x93, '\u201C' },
                { 0x94, '\u201D' }, { 0x95, '\u2022' }, { 0x96, '\u2013' }, { 0x97, '\u2014' },
                { 0x98, '\u02DC' }, { 0x99, '\u2122' }, { 0x9A, '\u0161' }, { 0x9B, '\u203A' },
                { 0x9C, '\u0153' }, { 0x9E, '\u017E' }, { 0x9F, '\u0178' }
            };

            foreach (var pair in specials)
                map[pair.Key] = pair.Value;

            return map;
        }
    }
}
