﻿using System;
using System.Text;
using System.Collections.Generic;
using static Secs4Net.Item;

namespace Secs4Net
{
    static class SecsExtension
    {
        #region Bytes To Item Value
        internal static Item BytesDecode(this SecsFormat format)
        {
            switch (format)
            {
                case SecsFormat.ASCII: return A();
                case SecsFormat.JIS8: return J();
                case SecsFormat.Boolean: return Boolean();
                case SecsFormat.Binary: return B();
                case SecsFormat.U1: return U1();
                case SecsFormat.U2: return U2();
                case SecsFormat.U4: return U4();
                case SecsFormat.U8: return U8();
                case SecsFormat.I1: return I1();
                case SecsFormat.I2: return I2();
                case SecsFormat.I4: return I4();
                case SecsFormat.I8: return I8();
                case SecsFormat.F4: return F4();
                case SecsFormat.F8: return F8();
            }
            throw new ArgumentException(@"Invalid format:" + format, nameof(format));
        }

        internal static Item BytesDecode(this SecsFormat format, ArraySegment<byte> bytes)
        {
            switch (format)
            {
                case SecsFormat.ASCII:
                    return A(Encoding.ASCII.GetString(bytes.Array, bytes.Offset, bytes.Count));
#pragma warning disable CS0618 // Type or member is obsolete
                case SecsFormat.JIS8:
                    return J(JIS8Encoding.GetString(bytes.Array, bytes.Offset, bytes.Count));
#pragma warning restore CS0618 // Type or member is obsolete
                case SecsFormat.Boolean:
                    return new Item(format, Decode<bool>(sizeof(bool), bytes), bytes);
                case SecsFormat.Binary:
                    return new Item(format, Decode<byte>(sizeof(byte), bytes), bytes);
                case SecsFormat.U1:
                    return new Item(format, Decode<byte>(sizeof(byte), bytes), bytes);
                case SecsFormat.U2:
                    return new Item(format, Decode<ushort>(sizeof(ushort), bytes), bytes);
                case SecsFormat.U4:
                    return new Item(format, Decode<uint>(sizeof(uint), bytes), bytes);
                case SecsFormat.U8:
                    return new Item(format, Decode<ulong>(sizeof(ulong), bytes), bytes);
                case SecsFormat.I1:
                    return new Item(format, Decode<sbyte>(sizeof(sbyte), bytes), bytes);
                case SecsFormat.I2:
                    return new Item(format, Decode<short>(sizeof(short), bytes), bytes);
                case SecsFormat.I4:
                    return new Item(format, Decode<int>(sizeof(int), bytes), bytes);
                case SecsFormat.I8:
                    return new Item(format, Decode<long>(sizeof(long), bytes), bytes);
                case SecsFormat.F4:
                    return new Item(format, Decode<float>(sizeof(float), bytes), bytes);
                case SecsFormat.F8:
                    return new Item(format, Decode<double>(sizeof(double), bytes), bytes);
            }
            throw new ArgumentException(@"Invalid format", nameof(format));
        }

        static T[] Decode<T>(int elmSize, ArraySegment<byte> bytes) where T : struct
        {
            bytes.Array.Reverse(bytes.Offset, bytes.Offset+ bytes.Count, elmSize);
            var values = new T[bytes.Count / elmSize];
            Buffer.BlockCopy(bytes.Array, bytes.Offset, values, 0, bytes.Count);
            return values;
        }
        #endregion

        internal static void Reverse(this byte[] bytes, int begin, int end, int offSet)
        {
            if (offSet > 1)
                for (int i = begin; i < end; i += offSet)
                    Array.Reverse(bytes, i, offSet);
        }

        /// <summary>
        /// Encode Item header + value (initial array only)
        /// </summary>
        /// <param name="valueCount">Item value bytes length</param>
        /// <param name="headerlength">return header bytes length</param>
        /// <param name="format"></param>
        /// <returns>header bytes + initial bytes of value </returns>
        internal static byte[] EncodeItem(this SecsFormat format, int valueCount, out int headerlength)
        {
            byte[] lengthBytes = BitConverter.GetBytes(valueCount);
            int dataLength = format == SecsFormat.List ? 0 : valueCount;

            if (valueCount <= 0xff)
            {//	1 byte
                headerlength = 2;
                var result = new byte[dataLength + 2];
                result[0] = (byte)((byte)format | 1);
                result[1] = lengthBytes[0];
                return result;
            }
            if (valueCount <= 0xffff)
            {//	2 byte
                headerlength = 3;
                var result = new byte[dataLength + 3];
                result[0] = (byte)((byte)format | 2);
                result[1] = lengthBytes[1];
                result[2] = lengthBytes[0];
                return result;
            }
            if (valueCount <= 0xffffff)
            {//	3 byte
                headerlength = 4;
                var result = new byte[dataLength + 4];
                result[0] = (byte)((byte)format | 3);
                result[1] = lengthBytes[2];
                result[2] = lengthBytes[1];
                result[3] = lengthBytes[0];
                return result;
            }
            throw new ArgumentOutOfRangeException(nameof(valueCount), valueCount, $"Item data length({valueCount}) is overflow");
        }

        /// <summary>
        /// Encode item to raw data buffer
        /// </summary>
        /// <param name="item"></param>
        /// <param name="buffer"></param>
        /// <returns></returns>
        internal static uint Encode(this Item item, List<ArraySegment<byte>> buffer)
        {
            uint length = (uint)item.RawData.Bytes.Count;
            item.RawData.Encode(item.Format);
            buffer.Add(item.RawData.Bytes);
            if (item.Format == SecsFormat.List)
                foreach (var subItem in item.Items)
                    length += subItem.Encode(buffer);
            return length;
        }
    }
}