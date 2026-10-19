using System;

namespace Factix
{
    internal static class Le
    {
        public static bool Has(byte[] data, int offset, int count)
        {
            return offset >= 0 && count >= 0 && offset <= data.Length - count;
        }

        public static uint U32(byte[] data, int offset)
        {
            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }

        public static int I32(byte[] data, int offset)
        {
            return (int)U32(data, offset);
        }

        public static ushort U16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        public static float F32(byte[] data, int offset)
        {
            if (BitConverter.IsLittleEndian)
                return BitConverter.ToSingle(data, offset);

            var swapped = new[] { data[offset + 3], data[offset + 2], data[offset + 1], data[offset] };
            return BitConverter.ToSingle(swapped, 0);
        }
    }
}
