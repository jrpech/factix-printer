using System;
using System.Collections.Generic;
using System.Text;

namespace Factix
{
    /// <summary>
    /// Reads text from an EMF or from a Windows EMF spool file (MS-EMF).
    /// Coordinates come out in points, origin at the top left.
    /// </summary>
    public static class EmfTextExtractor
    {
        private const uint EmfSignature = 0x464D4520;
        private const uint SpoolVersion = 0x00010000;
        private const int EmrHeader = 1;
        private const int EmrEof = 14;
        private const int EmrSetWindowExtEx = 9;
        private const int EmrSetWindowOrgEx = 10;
        private const int EmrSetViewportExtEx = 11;
        private const int EmrSetViewportOrgEx = 12;
        private const int EmrSetMapMode = 17;
        private const int EmrSetBkMode = 18;
        private const int EmrSetTextAlign = 22;
        private const int EmrSaveDc = 33;
        private const int EmrRestoreDc = 34;
        private const int EmrSetWorldTransform = 35;
        private const int EmrModifyWorldTransform = 36;
        private const int EmrSelectObject = 37;
        private const int EmrDeleteObject = 40;
        private const int EmrSetGraphicsMode = 57;
        private const int EmrExtCreateFontIndirectW = 82;
        private const int EmrExtTextOutA = 83;
        private const int EmrExtTextOutW = 84;
        private const int EmrSmallTextOut = 108;
        private const int EtoNoRect = 0x00000100;
        private const int EtoSmallChars = 0x00000200;
        private const int TaBaseline = 24;
        private const int TaBottom = 8;
        private const int TaRight = 2;
        private const int TaCenter = 6;
        private const int GmAdvanced = 2;

        public static bool IsEmf(byte[] data, int offset, int length)
        {
            if (!Le.Has(data, offset, 88) || length < 88)
                return false;
            if (Le.U32(data, offset) != EmrHeader)
                return false;
            var headerSize = Le.U32(data, offset + 4);
            if (headerSize < 88 || headerSize > length)
                return false;
            return Le.U32(data, offset + 40) == EmfSignature;
        }

        public static bool IsSpool(byte[] data)
        {
            if (!Le.Has(data, 0, 16))
                return false;
            var version = Le.U32(data, 0);
            var headerSize = Le.U32(data, 4);
            return version == SpoolVersion && headerSize >= 16 && headerSize < data.Length;
        }

        public static IReadOnlyList<TextPage> Read(byte[] data)
        {
            if (data == null || data.Length == 0)
                return new TextPage[0];

            if (IsEmf(data, 0, data.Length))
            {
                var page = ReadPage(data, 0, data.Length, 1);
                return page == null ? new TextPage[0] : new[] { page };
            }

            if (IsSpool(data))
                return ReadSpool(data);

            return new TextPage[0];
        }

        private static IReadOnlyList<TextPage> ReadSpool(byte[] data)
        {
            var pages = new List<TextPage>();
            var headerSize = (int)Le.U32(data, 4);
            var offset = headerSize;
            var pageNumber = 1;
            var guard = 0;

            while (offset + 8 <= data.Length && guard++ < 100000)
            {
                var id = Le.U32(data, offset);
                var size = (int)Le.U32(data, offset + 4);
                if (size < 8)
                    break;

                var payload = size;
                var recordEnd = offset + 8 + payload;
                var includesHeader = false;
                if (recordEnd > data.Length && offset + size <= data.Length)
                {
                    includesHeader = true;
                    payload = size - 8;
                    recordEnd = offset + size;
                }
                else if (recordEnd > data.Length)
                {
                    break;
                }

                if (IsMetafileRecord(id) && IsEmf(data, offset + 8, payload))
                {
                    var declared = (int)Le.U32(data, offset + 8 + 48);
                    var emfLength = declared >= 88 && declared <= payload ? declared : payload;
                    var page = ReadPage(data, offset + 8, emfLength, pageNumber);
                    if (page != null)
                    {
                        pages.Add(page);
                        pageNumber++;
                    }

                    if (!includesHeader && size >= emfLength + 8)
                        recordEnd = offset + size;
                }

                var next = recordEnd;
                if (next <= offset)
                    break;
                offset = (next + 3) & ~3;
            }

            return pages;
        }

        private static bool IsMetafileRecord(uint id)
        {
            return id == 0x1 || id == 0x9 || id == 0xA || id == 0xC || id == 0xD || id == 0xE;
        }

        private static TextPage? ReadPage(byte[] data, int offset, int length, int pageNumber)
        {
            if (!IsEmf(data, offset, length))
                return null;

            var headerSize = (int)Le.U32(data, offset + 4);
            var declared = (int)Le.U32(data, offset + 48);
            var end = offset + (declared >= headerSize && declared <= length ? declared : length);
            var deviceCx = Le.I32(data, offset + 72);
            var deviceCy = Le.I32(data, offset + 76);
            var milliCx = Le.I32(data, offset + 80);
            var milliCy = Le.I32(data, offset + 84);
            var pageWidth = MilliToPoints(milliCx);
            var pageHeight = MilliToPoints(milliCy);
            if (pageWidth < 72 || pageHeight < 72)
            {
                pageWidth = 612;
                pageHeight = 792;
            }

            var state = new DcState();
            var saved = new Stack<DcState>();
            var fonts = new Dictionary<int, FontInfo>();
            var runs = new List<TextRun>();
            var cursor = offset + headerSize;
            var guard = 0;

            while (cursor + 8 <= end && guard++ < 200000)
            {
                var type = (int)Le.U32(data, cursor);
                var size = (int)Le.U32(data, cursor + 4);
                if (size < 8 || cursor + size > end)
                    break;

                switch (type)
                {
                    case EmrSetWindowExtEx:
                        if (size >= 16)
                        {
                            state.WinExtX = Le.I32(data, cursor + 8);
                            state.WinExtY = Le.I32(data, cursor + 12);
                        }
                        break;
                    case EmrSetWindowOrgEx:
                        if (size >= 16)
                        {
                            state.WinOrgX = Le.I32(data, cursor + 8);
                            state.WinOrgY = Le.I32(data, cursor + 12);
                        }
                        break;
                    case EmrSetViewportExtEx:
                        if (size >= 16)
                        {
                            state.VpExtX = Le.I32(data, cursor + 8);
                            state.VpExtY = Le.I32(data, cursor + 12);
                        }
                        break;
                    case EmrSetViewportOrgEx:
                        if (size >= 16)
                        {
                            state.VpOrgX = Le.I32(data, cursor + 8);
                            state.VpOrgY = Le.I32(data, cursor + 12);
                        }
                        break;
                    case EmrSetMapMode:
                        if (size >= 12)
                            state.MapMode = (int)Le.U32(data, cursor + 8);
                        break;
                    case EmrSetGraphicsMode:
                        if (size >= 12)
                            state.GraphicsMode = (int)Le.U32(data, cursor + 8);
                        break;
                    case EmrSetTextAlign:
                        if (size >= 12)
                            state.TextAlign = Le.U32(data, cursor + 8);
                        break;
                    case EmrSetBkMode:
                        break;
                    case EmrSetWorldTransform:
                        if (size >= 32)
                            ReadXform(data, cursor + 8, state, replace: true);
                        break;
                    case EmrModifyWorldTransform:
                        if (size >= 36)
                        {
                            var mode = (int)Le.U32(data, cursor + 32);
                            if (mode == 1)
                                state.ResetWorld();
                            else if (mode == 4)
                                ReadXform(data, cursor + 8, state, replace: true);
                            else
                                MultiplyXform(data, cursor + 8, state, left: mode == 2);
                        }
                        break;
                    case EmrSaveDc:
                        saved.Push(state.Clone());
                        break;
                    case EmrRestoreDc:
                        if (saved.Count > 0)
                        {
                            var relative = size >= 12 ? Le.I32(data, cursor + 8) : -1;
                            if (relative >= 0)
                            {
                                while (saved.Count > relative && saved.Count > 0)
                                    state = saved.Pop();
                            }
                            else
                            {
                                for (var i = 0; i < -relative && saved.Count > 0; i++)
                                    state = saved.Pop();
                            }
                        }
                        break;
                    case EmrExtCreateFontIndirectW:
                        ReadFont(data, cursor, size, fonts);
                        break;
                    case EmrSelectObject:
                        if (size >= 12)
                        {
                            var handle = Le.I32(data, cursor + 8);
                            if ((handle & unchecked((int)0x80000000)) != 0)
                                state.Font = FontInfo.Default;
                            else if (fonts.TryGetValue(handle, out var font))
                                state.Font = font;
                        }
                        break;
                    case EmrDeleteObject:
                        if (size >= 12)
                            fonts.Remove(Le.I32(data, cursor + 8));
                        break;
                    case EmrExtTextOutW:
                        AddExtText(data, cursor, size, unicode: true, pageNumber, state, deviceCx, deviceCy, milliCx, milliCy, runs);
                        break;
                    case EmrExtTextOutA:
                        AddExtText(data, cursor, size, unicode: false, pageNumber, state, deviceCx, deviceCy, milliCx, milliCy, runs);
                        break;
                    case EmrSmallTextOut:
                        AddSmallText(data, cursor, size, pageNumber, state, deviceCx, deviceCy, milliCx, milliCy, runs);
                        break;
                    case EmrEof:
                        cursor = end;
                        continue;
                }

                cursor += size;
            }

            return new TextPage(pageNumber, pageWidth, pageHeight, runs);
        }

        private static void ReadFont(byte[] data, int cursor, int size, Dictionary<int, FontInfo> fonts)
        {
            if (size < 12 + 28)
                return;
            var handle = Le.I32(data, cursor + 8);
            var height = Le.I32(data, cursor + 12);
            var face = "";
            if (size >= 12 + 92)
                face = Encoding.Unicode.GetString(data, cursor + 12 + 28, 64).TrimEnd('\0');
            fonts[handle] = new FontInfo(string.IsNullOrWhiteSpace(face) ? "Arial" : face, Math.Abs(height));
        }

        private static void AddExtText(
            byte[] data,
            int cursor,
            int size,
            bool unicode,
            int pageNumber,
            DcState state,
            int deviceCx,
            int deviceCy,
            int milliCx,
            int milliCy,
            List<TextRun> runs)
        {
            if (size < 76)
                return;

            var referenceX = Le.I32(data, cursor + 36);
            var referenceY = Le.I32(data, cursor + 40);
            var nChars = (int)Le.U32(data, cursor + 44);
            var offString = (int)Le.U32(data, cursor + 48);
            var offDx = (int)Le.U32(data, cursor + 72);
            if (nChars <= 0 || nChars > 20000)
                return;

            var charBytes = unicode ? 2 : 1;
            if (offString < 0 || offString + (nChars * charBytes) > size)
                return;

            var text = unicode
                ? Encoding.Unicode.GetString(data, cursor + offString, nChars * 2).TrimEnd('\0')
                : Win1252.Decode(data, cursor + offString, nChars).TrimEnd('\0');
            if (string.IsNullOrEmpty(text))
                return;

            double advance = 0;
            var dxBytes = unicode ? 4 : 4;
            if (offDx > 0 && offDx + (nChars * dxBytes) <= size)
            {
                for (var i = 0; i < nChars; i++)
                    advance += Le.U32(data, cursor + offDx + (i * 4));
            }

            AddRun(text, referenceX, referenceY, advance, pageNumber, state, deviceCx, deviceCy, milliCx, milliCy, runs);
        }

        private static void AddSmallText(
            byte[] data,
            int cursor,
            int size,
            int pageNumber,
            DcState state,
            int deviceCx,
            int deviceCy,
            int milliCx,
            int milliCy,
            List<TextRun> runs)
        {
            if (size < 36)
                return;

            var referenceX = Le.I32(data, cursor + 8);
            var referenceY = Le.I32(data, cursor + 12);
            var nChars = (int)Le.U32(data, cursor + 16);
            var options = Le.U32(data, cursor + 20);
            if (nChars <= 0 || nChars > 20000)
                return;

            var textOffset = 36;
            if ((options & EtoNoRect) == 0)
                textOffset += 16;
            var small = (options & EtoSmallChars) != 0;
            var byteCount = nChars * (small ? 1 : 2);
            if (textOffset + byteCount > size)
                return;

            var text = small
                ? Win1252.Decode(data, cursor + textOffset, nChars)
                : Encoding.Unicode.GetString(data, cursor + textOffset, nChars * 2);
            text = text.TrimEnd('\0');
            if (string.IsNullOrEmpty(text))
                return;

            AddRun(text, referenceX, referenceY, 0, pageNumber, state, deviceCx, deviceCy, milliCx, milliCy, runs);
        }

        private static void AddRun(
            string text,
            int referenceX,
            int referenceY,
            double logicalAdvance,
            int pageNumber,
            DcState state,
            int deviceCx,
            int deviceCy,
            int milliCx,
            int milliCy,
            List<TextRun> runs)
        {
            var x = (double)referenceX;
            var y = (double)referenceY;
            if ((state.TextAlign & 1) != 0)
            {
                x = state.CpX;
                y = state.CpY;
            }

            if (state.GraphicsMode == GmAdvanced)
                ApplyWorld(state, ref x, ref y);

            MapToDevice(state, deviceCx, deviceCy, milliCx, milliCy, ref x, ref y);
            var heightLogical = state.Font.HeightLogical > 0 ? state.Font.HeightLogical : 0;
            var heightPt = heightLogical > 0
                ? Math.Abs(Scale(state, deviceCx, deviceCy, milliCx, milliCy, 0, heightLogical).Y)
                : 12;
            if (heightPt < 1)
                heightPt = 12;

            var widthLogical = logicalAdvance > 0 ? logicalAdvance : text.Length * (heightLogical > 0 ? heightLogical : heightPt) * 0.5;
            var widthPt = logicalAdvance > 0 || heightLogical > 0
                ? Math.Abs(Scale(state, deviceCx, deviceCy, milliCx, milliCy, widthLogical, 0).X)
                : text.Length * heightPt * 0.5;
            if (widthPt <= 0)
                widthPt = text.Length * heightPt * 0.5;

            var align = state.TextAlign;
            var horizontal = (int)(align & 6);
            if (horizontal == TaCenter)
                x -= widthPt / 2;
            else if (horizontal == TaRight)
                x -= widthPt;

            if ((align & TaBaseline) == TaBaseline)
                y -= heightPt * 0.8;
            else if ((align & TaBottom) == TaBottom)
                y -= heightPt;

            if ((align & 1) != 0)
            {
                state.CpX = referenceX + widthLogical;
                state.CpY = referenceY;
            }

            runs.Add(new TextRun(pageNumber, text, x, y, widthPt, heightPt, state.Font.Name));
        }

        private static void MapToDevice(DcState state, int deviceCx, int deviceCy, int milliCx, int milliCy, ref double x, ref double y)
        {
            if (state.MapMode == 1 || state.MapMode == 7 || state.MapMode == 8)
            {
                var sx = SafeDiv(state.VpExtX, state.WinExtX);
                var sy = SafeDiv(state.VpExtY, state.WinExtY);
                x = (x - state.WinOrgX) * sx + state.VpOrgX;
                y = (y - state.WinOrgY) * sy + state.VpOrgY;
            }
            else
            {
                var scaleX = PredefinedScale(state.MapMode, deviceCx, milliCx);
                var scaleY = PredefinedScale(state.MapMode, deviceCy, milliCy);
                x = (x - state.WinOrgX) * scaleX + state.VpOrgX;
                y = state.VpOrgY - ((y - state.WinOrgY) * scaleY);
            }

            x = DeviceToPoints(x, deviceCx, milliCx);
            y = DeviceToPoints(y, deviceCy, milliCy);
        }

        private static (double X, double Y) Scale(DcState state, int deviceCx, int deviceCy, int milliCx, int milliCy, double logicalX, double logicalY)
        {
            var x = logicalX;
            var y = logicalY;
            if (state.MapMode == 1 || state.MapMode == 7 || state.MapMode == 8)
            {
                x *= SafeDiv(state.VpExtX, state.WinExtX);
                y *= SafeDiv(state.VpExtY, state.WinExtY);
            }
            else
            {
                x *= PredefinedScale(state.MapMode, deviceCx, milliCx);
                y *= PredefinedScale(state.MapMode, deviceCy, milliCy);
            }

            return (DeviceToPoints(x, deviceCx, milliCx), DeviceToPoints(y, deviceCy, milliCy));
        }

        private static double PredefinedScale(int mapMode, int device, int millimeters)
        {
            var dpi = device > 0 && millimeters > 0 ? device / (double)millimeters * 25.4 : 96.0;
            switch (mapMode)
            {
                case 2: return 0.1 / 25.4 * dpi;
                case 3: return 0.01 / 25.4 * dpi;
                case 4: return 0.01 * dpi;
                case 5: return 0.001 * dpi;
                case 6: return dpi / 1440.0;
                default: return 1;
            }
        }

        private static double DeviceToPoints(double deviceUnits, int device, int millimeters)
        {
            if (device <= 0 || millimeters <= 0)
                return deviceUnits;
            return deviceUnits / device * millimeters / 25.4 * 72.0;
        }

        private static double MilliToPoints(int millimeters)
        {
            return millimeters / 25.4 * 72.0;
        }

        private static double SafeDiv(double num, double den)
        {
            return Math.Abs(den) < 0.0001 ? num : num / den;
        }

        private static void ApplyWorld(DcState state, ref double x, ref double y)
        {
            var nx = (x * state.M11) + (y * state.M21) + state.Dx;
            var ny = (x * state.M12) + (y * state.M22) + state.Dy;
            x = nx;
            y = ny;
        }

        private static void ReadXform(byte[] data, int offset, DcState state, bool replace)
        {
            state.M11 = Le.F32(data, offset);
            state.M12 = Le.F32(data, offset + 4);
            state.M21 = Le.F32(data, offset + 8);
            state.M22 = Le.F32(data, offset + 12);
            state.Dx = Le.F32(data, offset + 16);
            state.Dy = Le.F32(data, offset + 20);
            if (!replace)
                return;
        }

        private static void MultiplyXform(byte[] data, int offset, DcState state, bool left)
        {
            var a11 = Le.F32(data, offset);
            var a12 = Le.F32(data, offset + 4);
            var a21 = Le.F32(data, offset + 8);
            var a22 = Le.F32(data, offset + 12);
            var adx = Le.F32(data, offset + 16);
            var ady = Le.F32(data, offset + 20);
            var b11 = state.M11;
            var b12 = state.M12;
            var b21 = state.M21;
            var b22 = state.M22;
            var bdx = state.Dx;
            var bdy = state.Dy;
            if (left)
            {
                state.M11 = (a11 * b11) + (a12 * b21);
                state.M12 = (a11 * b12) + (a12 * b22);
                state.M21 = (a21 * b11) + (a22 * b21);
                state.M22 = (a21 * b12) + (a22 * b22);
                state.Dx = (adx * b11) + (ady * b21) + bdx;
                state.Dy = (adx * b12) + (ady * b22) + bdy;
            }
            else
            {
                state.M11 = (b11 * a11) + (b12 * a21);
                state.M12 = (b11 * a12) + (b12 * a22);
                state.M21 = (b21 * a11) + (b22 * a21);
                state.M22 = (b21 * a12) + (b22 * a22);
                state.Dx = (bdx * a11) + (bdy * a21) + adx;
                state.Dy = (bdx * a12) + (bdy * a22) + ady;
            }
        }

        private sealed class DcState
        {
            public int MapMode = 1;
            public int GraphicsMode = 1;
            public double WinOrgX;
            public double WinOrgY;
            public double WinExtX = 1;
            public double WinExtY = 1;
            public double VpOrgX;
            public double VpOrgY;
            public double VpExtX = 1;
            public double VpExtY = 1;
            public double M11 = 1;
            public double M12;
            public double M21;
            public double M22 = 1;
            public double Dx;
            public double Dy;
            public uint TextAlign;
            public double CpX;
            public double CpY;
            public FontInfo Font = FontInfo.Default;

            public DcState Clone()
            {
                return (DcState)MemberwiseClone();
            }

            public void ResetWorld()
            {
                M11 = 1;
                M12 = 0;
                M21 = 0;
                M22 = 1;
                Dx = 0;
                Dy = 0;
            }
        }

        private sealed class FontInfo
        {
            public static readonly FontInfo Default = new FontInfo("Arial", 0);

            public FontInfo(string name, int heightLogical)
            {
                Name = name;
                HeightLogical = heightLogical;
            }

            public string Name { get; }
            public int HeightLogical { get; }
        }
    }
}
