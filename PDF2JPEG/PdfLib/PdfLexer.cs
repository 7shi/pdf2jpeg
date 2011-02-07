using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PdfLib
{
    public class PdfLexer
    {
        public Stream Stream { get; private set; }

        public PdfLexer(Stream stream)
        {
            Stream = stream;
        }

        public void Clear()
        {
            cur = 0;
            token2 = token1 = current = null;
            IsNumber = false;
        }

        private int cur, objno;
        private string token2, token1, current;

        public long Position { get; private set; }
        public bool IsNumber { get; private set; }

        public int ObjNo { get { return objno; } }
        public string Current { get { return current; } }

        public void ReadToken(bool isStream = false)
        {
            if (!isStream)
            {
                token2 = token1;
                token1 = current;
                current = ReadTokenInternal();
                if (current == "obj") objno = int.Parse(token2);
            }
            else
                current = ReadTokenInternal();
        }

        private string ReadTokenInternal()
        {
            IsNumber = false;
            Position = Stream.Position;
            if (cur == 0) cur = Stream.ReadByte();
            if (cur == -1) return null;

            var sb = new StringBuilder();
            for (; cur != -1; cur = Stream.ReadByte())
            {
                var ch = (char)cur;
                if (ch == '-' || char.IsDigit(ch))
                {
                    do
                    {
                        sb.Append((char)cur);
                        cur = Stream.ReadByte();
                    }
                    while (cur != -1 && (cur == '.' || char.IsDigit((char)cur)));
                    IsNumber = true;
                    break;
                }
                else if (ch == '/' || char.IsLetter(ch))
                {
                    do
                    {
                        sb.Append((char)cur);
                        cur = Stream.ReadByte();
                    }
                    while (cur != -1 && (cur == '-' || cur == '_' || char.IsLetterOrDigit((char)cur)));
                    break;
                }
                else if (ch > ' ')
                {
                    sb.Append(ch);
                    cur = Stream.ReadByte();
                    if ((ch == '<' || ch == '>') && ch == cur)
                    {
                        sb.Append(ch);
                        cur = Stream.ReadByte();
                    }
                    break;
                }
            }
            return sb.ToString();
        }

        public string ReadAscii(int len)
        {
            var buf = new byte[len];
            var rlen = Stream.Read(buf, 0, len);
            return GetAscii(buf, 0, rlen);
        }

        public static string GetAscii(byte[] buf, int start, int count)
        {
            var sb = new StringBuilder();
            char prev = '\0';
            for (int i = 0; i < count; i++)
            {
                var ch = (char)buf[start + i];
                if (prev == '\r' && ch != '\n')
                    sb.Append('\n');
                else if (prev != '\r' && ch == '\n')
                    sb.Append('\r');
                sb.Append(ch);
                prev = ch;
            }
            return sb.ToString();
        }

        public string ReadString(int len, Encoding enc)
        {
            var buf = new byte[len];
            var rlen = Stream.Read(buf, 0, len);
            return GetString(buf, 0, rlen, enc);
        }

        public static string GetString(byte[] buf, int start, int count, Encoding enc)
        {
            var list = new List<byte>();
            byte prev = 0;
            for (int i = 0; i < count; i++)
            {
                var ch = buf[start + i];
                if (prev == '\r' && ch != '\n')
                    list.Add((byte)'\n');
                else if (prev != '\r' && ch == '\n')
                    list.Add((byte)'\r');
                list.Add(ch);
                prev = ch;
            }
            return enc.GetString(list.ToArray());
        }

        public static long ReadInt64(string s, int pos)
        {
            long ret = 0;
            for (int p = pos; '0' <= s[p] && s[p] <= '9'; p++)
            {
                ret *= 10;
                ret += s[p] - '0';
            }
            return ret;
        }

        public Exception Abort(string format, params object[] args)
        {
            var msg = string.Format(format, args);
            return new Exception(string.Format("{0:x} [{1}] {2}", Position, current, msg));
        }

        public long SkipStream(long len)
        {
            if (cur == 0x0d) Stream.ReadByte();
            Clear();
            var ret = Stream.Position;
            Stream.Position += len;
            return ret;
        }

        public double GetValue(PdfDocument doc, object val)
        {
            if (val is double)
                return (long)(double)val;
            else if (val is PdfReference)
            {
                var pos = Stream.Position;
                var lenref = val as PdfReference;
                var lenobj = doc[lenref.Number];
                var ret = (double)lenobj.Object;
                Stream.Position = pos;
                return ret;
            }
            else
                throw Abort("unexpected /Length: {0}", val);
        }
    }
}
