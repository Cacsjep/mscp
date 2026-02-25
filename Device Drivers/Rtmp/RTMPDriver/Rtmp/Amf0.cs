using System;
using System.Collections.Generic;
using System.Text;

namespace RTMPDriver.Rtmp
{
    /// <summary>
    /// Minimal AMF0 reader for RTMP command parsing.
    /// </summary>
    internal static class Amf0Reader
    {
        private const byte TypeNumber = 0x00;
        private const byte TypeBoolean = 0x01;
        private const byte TypeString = 0x02;
        private const byte TypeObject = 0x03;
        private const byte TypeNull = 0x05;
        private const byte TypeUndefined = 0x06;
        private const byte TypeEcmaArray = 0x08;
        private const byte TypeObjectEnd = 0x09;
        private const int MaxNestingDepth = 32;

        private static object ReadValue(byte[] data, ref int offset, int depth)
        {
            if (depth > MaxNestingDepth)
                throw new FormatException("AMF0 nesting depth exceeded");

            if (offset >= data.Length)
                return null;

            byte type = data[offset++];
            switch (type)
            {
                case TypeNumber:
                    return ReadNumberValue(data, ref offset);
                case TypeBoolean:
                    return ReadBooleanValue(data, ref offset);
                case TypeString:
                    return ReadStringValue(data, ref offset);
                case TypeObject:
                    return ReadObjectValue(data, ref offset, depth + 1);
                case TypeNull:
                case TypeUndefined:
                    return null;
                case TypeEcmaArray:
                    if (offset + 4 > data.Length)
                        return null;
                    offset += 4;
                    return ReadObjectValue(data, ref offset, depth + 1);
                default:
                    throw new FormatException($"Unsupported AMF0 type: 0x{type:X2}");
            }
        }

        private static double ReadNumberValue(byte[] data, ref int offset)
        {
            if (offset + 8 > data.Length)
                throw new FormatException("AMF0 truncated number");
            var bytes = new byte[8];
            Array.Copy(data, offset, bytes, 0, 8);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            offset += 8;
            return BitConverter.ToDouble(bytes, 0);
        }

        private static bool ReadBooleanValue(byte[] data, ref int offset)
        {
            if (offset >= data.Length)
                throw new FormatException("AMF0 truncated boolean");
            return data[offset++] != 0;
        }

        private static string ReadStringValue(byte[] data, ref int offset)
        {
            if (offset + 2 > data.Length)
                throw new FormatException("AMF0 truncated string length");
            int length = (data[offset] << 8) | data[offset + 1];
            offset += 2;
            if (offset + length > data.Length)
                throw new FormatException("AMF0 truncated string data");
            string value = Encoding.UTF8.GetString(data, offset, length);
            offset += length;
            return value;
        }

        private static Dictionary<string, object> ReadObjectValue(byte[] data, ref int offset, int depth)
        {
            var obj = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            while (offset + 2 < data.Length)
            {
                int keyLen = (data[offset] << 8) | data[offset + 1];
                offset += 2;

                if (keyLen == 0 && offset < data.Length && data[offset] == TypeObjectEnd)
                {
                    offset++; // skip 0x09
                    break;
                }

                if (offset + keyLen > data.Length)
                    break;

                string key = Encoding.UTF8.GetString(data, offset, keyLen);
                offset += keyLen;
                object value = ReadValue(data, ref offset, depth);
                obj[key] = value;
            }
            return obj;
        }

        /// <summary>
        /// Parse all AMF0 values from a command message payload.
        /// Returns: [commandName, transactionId, commandObject, ...args]
        /// </summary>
        public static List<object> ParseCommand(byte[] data)
        {
            var result = new List<object>();
            int offset = 0;
            while (offset < data.Length)
            {
                result.Add(ReadValue(data, ref offset, 0));
            }
            return result;
        }
    }

    /// <summary>
    /// Minimal AMF0 writer for RTMP command responses.
    /// </summary>
    internal static class Amf0Writer
    {
        public static void WriteNumber(List<byte> buf, double value)
        {
            buf.Add(0x00);
            var bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            buf.AddRange(bytes);
        }

        public static void WriteBoolean(List<byte> buf, bool value)
        {
            buf.Add(0x01);
            buf.Add(value ? (byte)1 : (byte)0);
        }

        public static void WriteString(List<byte> buf, string value)
        {
            buf.Add(0x02);
            WriteStringRaw(buf, value);
        }

        public static void WriteNull(List<byte> buf)
        {
            buf.Add(0x05);
        }

        public static void WriteObject(List<byte> buf, Dictionary<string, object> obj)
        {
            buf.Add(0x03);
            if (obj != null)
            {
                foreach (var kv in obj)
                {
                    WriteStringRaw(buf, kv.Key);
                    WriteValueUntyped(buf, kv.Value);
                }
            }
            // Object end marker: empty key + 0x09
            buf.Add(0x00);
            buf.Add(0x00);
            buf.Add(0x09);
        }

        private static void WriteStringRaw(List<byte> buf, string value)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(value ?? "");
            buf.Add((byte)(utf8.Length >> 8));
            buf.Add((byte)(utf8.Length & 0xFF));
            buf.AddRange(utf8);
        }

        private static void WriteValueUntyped(List<byte> buf, object value)
        {
            if (value == null)
                WriteNull(buf);
            else if (value is double d)
                WriteNumber(buf, d);
            else if (value is int i)
                WriteNumber(buf, i);
            else if (value is string s)
                WriteString(buf, s);
            else if (value is bool b)
                WriteBoolean(buf, b);
            else if (value is Dictionary<string, object> obj)
                WriteObject(buf, obj);
            else
                WriteNull(buf);
        }
    }
}
