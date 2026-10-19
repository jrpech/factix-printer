using System.IO;
using System.Text;

namespace Factix.Core.Tests;

internal static class EmfBuilder
{
    public static byte[] FacturaPage()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteHeader(writer);
        WriteFont(writer, "Arial", -50);
        WriteSelect(writer, 1);
        WriteText(writer, "FACTURA", 300, 300);
        WriteEof(writer);
        var bytes = stream.ToArray();
        Patch(bytes, records: 5);
        return bytes;
    }

    public static byte[] WrapSpool(byte[] emf, bool sizeIncludesHeader)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x00010000);
        writer.Write(16);
        writer.Write(0);
        writer.Write(0);
        writer.Write(1);
        writer.Write(sizeIncludesHeader ? emf.Length + 8 : emf.Length);
        writer.Write(emf);
        return stream.ToArray();
    }

    private static void WriteHeader(BinaryWriter writer)
    {
        writer.Write(1);
        writer.Write(108);
        writer.Write(0);
        writer.Write(0);
        writer.Write(2550);
        writer.Write(3300);
        writer.Write(0);
        writer.Write(0);
        writer.Write(21600);
        writer.Write(27900);
        writer.Write(0x464D4520);
        writer.Write(0x00010000);
        writer.Write(0);
        writer.Write(0);
        writer.Write((ushort)2);
        writer.Write((ushort)0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(2550);
        writer.Write(3300);
        writer.Write(216);
        writer.Write(279);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(216000);
        writer.Write(279000);
    }

    private static void WriteFont(BinaryWriter writer, string face, int height)
    {
        writer.Write(82);
        writer.Write(104);
        writer.Write(1);
        writer.Write(height);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(400);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        var name = Encoding.Unicode.GetBytes(face);
        writer.Write(name);
        writer.Write(new byte[64 - name.Length]);
    }

    private static void WriteSelect(BinaryWriter writer, int handle)
    {
        writer.Write(37);
        writer.Write(12);
        writer.Write(handle);
    }

    private static void WriteText(BinaryWriter writer, string text, int x, int y)
    {
        var chars = Encoding.Unicode.GetBytes(text);
        var padded = (chars.Length + 3) & ~3;
        var size = 76 + padded;
        writer.Write(84);
        writer.Write(size);
        writer.Write(x);
        writer.Write(y);
        writer.Write(x + 400);
        writer.Write(y + 60);
        writer.Write(1);
        writer.Write(1f);
        writer.Write(1f);
        writer.Write(x);
        writer.Write(y);
        writer.Write(text.Length);
        writer.Write(76);
        writer.Write(0);
        writer.Write(x);
        writer.Write(y);
        writer.Write(x + 400);
        writer.Write(y + 60);
        writer.Write(0);
        writer.Write(chars);
        if (padded > chars.Length)
            writer.Write(new byte[padded - chars.Length]);
    }

    private static void WriteEof(BinaryWriter writer)
    {
        writer.Write(14);
        writer.Write(8);
    }

    private static void Patch(byte[] bytes, int records)
    {
        WriteInt(bytes, 48, bytes.Length);
        WriteInt(bytes, 52, records);
    }

    private static void WriteInt(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }
}
