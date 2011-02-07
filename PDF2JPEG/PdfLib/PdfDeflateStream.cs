using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PdfLib
{
    public class PdfDeflateStream : Stream
    {
        private DeflateStream ds;
        int columns, predicator, position, rowpos;
        byte[] prev, rows;

        public PdfDeflateStream(Stream s, PdfObject obj)
        {
            if (obj.Dictionary != null && obj.Dictionary.ContainsKey("/DecodeParms"))
            {
                var dp = obj["/DecodeParms"] as PdfDictionary;
                if (dp != null)
                {
                    if (dp.ContainsKey("/Columns"))
                    {
                        columns = (int)(double)dp["/Columns"];
                        prev = new byte[columns];
                        rows = new byte[columns];
                        rowpos = rows.Length;
                    }
                    if (dp.ContainsKey("/Predictor"))
                        predicator = (int)(double)dp["/Predictor"];
                }
            }
            s.ReadByte();
            s.ReadByte();
            ds = new DeflateStream(s, CompressionMode.Decompress);
        }

        public PdfDeflateStream(Stream s, int columns)
        {
            this.columns = columns;
            if (columns > 0)
            {
                prev = new byte[columns];
                rows = new byte[columns];
                rowpos = rows.Length;
            }
            s.ReadByte();
            s.ReadByte();
            ds = new DeflateStream(s, CompressionMode.Decompress);
        }

        public override long Length
        {
            get { throw new NotImplementedException(); }
        }
        public override bool CanRead { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override bool CanSeek { get { return false; } }
        public override void Flush() { }

        public override long Position
        {
            get { return position; }
            set { throw new NotImplementedException(); }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int ret = 0;
            if (columns == 0)
                ret = ds.Read(buffer, offset, count);
            else
            {
                while (ret < count)
                {
                    if (rowpos >= rows.Length)
                    {
                        if (ds.ReadByte() != 2)
                            throw new Exception("unknown predictor type");
                        Array.Copy(rows, prev, rows.Length);
                        int len = ds.Read(rows, offset, rows.Length);
                        if (len < rows.Length) break;
                        for (int i = 0; i < rows.Length; i++)
                            rows[i] += prev[i];
                        rowpos = 0;
                    }
                    int rlen = Math.Min(count, rows.Length - rowpos);
                    Array.Copy(rows, rowpos, buffer, ret, rlen);
                    ret += rlen;
                    rowpos += rlen;
                }
            }
            position += ret;
            return ret;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }
    }
}
