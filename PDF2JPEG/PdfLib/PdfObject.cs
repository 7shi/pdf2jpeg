using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PdfLib
{
    public class PdfObject
    {
        public long Position { get; private set; }
        public long Length { get; private set; }
        public string Type { get; private set; }
        public int Number { get; private set; }
        public int Index { get; private set; }
        public PdfDictionary Dictionary { get; private set; }
        public long StreamStart { get; private set; }
        public long StreamLength { get; private set; }
        public object Object { get; private set; }
        public int ObjStm { get; private set; }
        public string Details = "";

        public PdfObject(int no, int objstm, int index, long position = 0)
        {
            Position = position;
            Number = no;
            ObjStm = objstm;
            Index = index;
            if (Number == 0) HasRead = true;
        }

        public PdfObject(PdfDocument doc, PdfParser parser)
        {
            Read(doc, parser);
        }

        public bool HasRead { get; private set; }

        public void Read(PdfDocument doc, PdfParser parser)
        {
            if (HasRead) return;
            HasRead = true;
            if (ObjStm != 0)
            {
                doc.GetObject(ObjStm).Read(doc, parser);
                return;
            }
            var lexer = parser.Lexer;
            if (Position == 0)
                Position = lexer.Position;
            else
            {
                lexer.Stream.Position = Position;
                lexer.Clear();
                lexer.ReadToken();
            }
            if (!lexer.IsNumber)
                throw lexer.Abort("required: number");
            Number = int.Parse(lexer.Current);
            lexer.ReadToken();
            Index = int.Parse(lexer.Current);
            lexer.ReadToken();
            if (lexer.Current != "obj")
                throw lexer.Abort("required: obj");
            lexer.ReadToken();
            if (lexer.Current == "<<")
                readDictionary(parser);
            if (lexer.Current == "stream")
            {
                if (Dictionary == null | !Dictionary.ContainsKey("/Length"))
                    throw lexer.Abort("not found: /Length");
                StreamLength = (long)lexer.GetValue(doc, Dictionary["/Length"]);
                StreamStart = lexer.SkipStream(StreamLength);
                lexer.ReadToken();
                if (lexer.Current != "endstream")
                    throw lexer.Abort("required: endstream");
                lexer.ReadToken();
            }
            if (lexer.Current != "endobj")
                Object = parser.Read();
            if (lexer.Current != "endobj")
                throw lexer.Abort("required: endobj");
            Length = lexer.Position + 6 - Position;
            lexer.ReadToken();
            if (Type == "/ObjStm")
                readObjStm(doc, parser.Lexer);
        }

        private void readDictionary(PdfParser parser)
        {
            Dictionary = new PdfDictionary(parser);
            if (Dictionary.ContainsKey("/Type"))
                Type = Dictionary["/Type"] as string;
        }

        public Stream GetStream(Stream stream)
        {
            stream.Position = StreamStart;
            var s = new SubStream(stream, StreamLength);
            if (Dictionary.ContainsKey("/Filter"))
            {
                var filter = Dictionary["/Filter"] as string;
                if (filter == "/FlateDecode")
                    return new PdfDeflateStream(s, this);
            }
            return s;
        }

        private void readObjStm(PdfDocument doc, PdfLexer lexer)
        {
            using (var s = GetStream(lexer.Stream))
            {
                if (!Dictionary.ContainsKey("/N"))
                    throw new Exception(string.Format(
                        "{0:x} [ObjStm] required: /N", Position));
                var n = (int)(double)Dictionary["/N"];
                if (!Dictionary.ContainsKey("/First"))
                    throw new Exception(string.Format(
                        "{0:x} [ObjStm] required: /First", Position));
                var first = (int)(double)Dictionary["/First"];
                var objno = new int[n];
                var objpos = new long[n];
                var parser = new PdfParser(s);
                var lexer2 = parser.Lexer;
                for (int i = 0; i < n; i++)
                {
                    lexer2.ReadToken();
                    objno[i] = int.Parse(lexer2.Current);
                    lexer2.ReadToken();
                    objpos[i] = int.Parse(lexer2.Current);
                }
                lexer2.ReadToken();
                for (int i = 0; i < n; i++)
                {
                    var obj = doc[objno[i]];
                    obj.HasRead = true;
                    obj.readDictionary(parser);
                    obj.Position = first + objpos[i];
                    if (i < n - 1)
                        obj.Length = objpos[i + 1] - objpos[i];
                    else
                        obj.Length = s.Position - obj.Position;
                }
            }
        }

        public object this[string key]
        {
            get { return Dictionary == null ? null : Dictionary[key]; }
        }

        public byte[] GetStreamBytes(Stream stream)
        {
            var ms = new MemoryStream();
            using (var s = GetStream(stream))
            {
                var buf = new byte[4096];
                int len;
                while ((len = s.Read(buf, 0, buf.Length)) > 0)
                    ms.Write(buf, 0, len);
            }
            return ms.ToArray();
        }
    }
}
