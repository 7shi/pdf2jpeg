using System;
using System.IO;

namespace PdfLib
{
    public class SubStream : Stream
    {
        private Stream s;
        private long start, length, pos;

        public SubStream(Stream s, long length)
        {
            this.s = s;
            this.start = s.Position;
            this.length = length;
        }

        public override long Length { get { return length; } }
        public override bool CanRead { get { return pos < length; } }
        public override bool CanWrite { get { return false; } }
        public override bool CanSeek { get { return true; } }
        public override void Flush() { }

        public override long Position
        {
            get { return pos; }
            set { s.Position = start + (pos = value); }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (!CanRead) return 0;
            count = (int)Math.Min(length - pos, count);
            int ret = s.Read(buffer, offset, count);
            pos += ret;
            return ret;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin: Position = offset; break;
                case SeekOrigin.Current: Position += offset; break;
                case SeekOrigin.End: Position = length + offset; break;
            }
            return pos;
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
