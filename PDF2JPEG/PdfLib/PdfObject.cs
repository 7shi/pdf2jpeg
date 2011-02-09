using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PdfLib
{
    public class PdfObject
    {
        private Dictionary<string, object> dict;
        private PdfDocument doc;

        public long Position;
        public int Index;
        public int ObjStm;
        public object Object;
        public string Details = "";

        public int Number { get; private set; }
        public string Type { get; private set; }
        public long Length { get; private set; }
        public long StreamStart { get; private set; }
        public long StreamLength { get; private set; }

        public PdfObject(PdfDocument doc, int no)
        {
            this.doc = doc;
            this.Number = no;
        }

        public PdfObject(PdfDocument doc, PdfParser parser)
        {
            this.doc = doc;
            HasRead = true;
            readDictionary(parser);
        }

        public void Init(long position)
        {
            HasRead = false;
            Position = position;
            dict = null;
            Type = null;
            Details = "";
            Index = ObjStm = 0;
            Length = StreamStart = StreamLength = 0;
        }

        public bool HasRead { get; private set; }

        public void Read(PdfParser parser)
        {
            if (HasRead) return;
            HasRead = true;
            if (ObjStm != 0)
            {
                doc.GetObject(ObjStm).Read(parser);
                return;
            }

            var lexer = parser.Lexer;
            if (Position == 0)
                throw lexer.Abort("can not seek position");
            lexer.Stream.Position = Position;
            lexer.Clear();
            lexer.ReadToken();

            if (!lexer.IsNumber)
                throw lexer.Abort("required: number");
            if (Number != int.Parse(lexer.Current))
                throw lexer.Abort("invalid number: {0}", Number);
            lexer.ReadToken();
            Index = int.Parse(lexer.Current);
            lexer.ReadToken();
            if (lexer.Current != "obj")
                throw lexer.Abort("required: obj");
            lexer.ReadToken();
            if (lexer.Current == "<<")
            {
                lexer.ReadToken();
                readDictionary(parser);
            }
            if (lexer.Current == "stream") readStream(parser);
            if (lexer.Current != "endobj") Object = parser.Read();
            if (lexer.Current != "endobj") throw lexer.Abort("required: endobj");
            Length = lexer.Position + 6 - Position;
            lexer.ReadToken();
            if (Type == "/ObjStm") readObjStm(parser.Lexer);
        }

        private void readStream(PdfParser parser)
        {
            var lexer = parser.Lexer;
            if (dict == null | !dict.ContainsKey("/Length"))
                throw lexer.Abort("not found: /Length");
            StreamStart = lexer.GetStreamStart();
            var len = this["/Length"];
            if (len is PdfObject)
                len = (len as PdfObject).Object;
            if (len is double)
            {
                long slen = (long)(double)len;
                lexer.Stream.Position += slen;
                lexer.Clear();
                lexer.ReadToken();
                if (lexer.Current == "endstream")
                    StreamLength = slen;
                else
                    lexer.Stream.Position = StreamStart;
            }
            if (StreamLength == 0)
            {
                lexer.SearchAscii("endstream");
                StreamLength = lexer.Position - StreamStart;
            }
            lexer.Clear();
            lexer.ReadToken();
            //if (Details == "") Details = "stream";
        }

        private void readObjStm(PdfLexer lexer)
        {
            using (var s = GetStream(lexer.Stream))
            {
                if (!dict.ContainsKey("/N"))
                    throw new Exception(string.Format(
                        "{0:x} [ObjStm] required: /N", Position));
                var n = (int)GetValue("/N");
                if (!dict.ContainsKey("/First"))
                    throw new Exception(string.Format(
                        "{0:x} [ObjStm] required: /First", Position));
                var first = (int)GetValue("/First");
                var objno = new int[n];
                var objpos = new long[n];
                var parser = new PdfParser(doc, s);
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
                    if (lexer2.Current != "<<")
                        throw lexer2.Abort("required: <<");
                    lexer2.ReadToken();
                    var obj = doc.GetObject(objno[i]);
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

        public Stream GetStream(Stream stream)
        {
            var filter = GetText("/Filter");
            stream.Position = StreamStart;
            var s = new SubStream(stream, StreamLength);
            if (filter != null && filter == "/FlateDecode")
                return new PdfDeflateStream(s, this);
            return s;
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

        public byte[] GetStreamBytes()
        {
            return GetStreamBytes(doc.Parser.Lexer.Stream);
        }

        private void readDictionary(PdfParser parser)
        {
            var lexer = parser.Lexer;
            if (dict != null)
                throw lexer.Abort("duplicate read");
            dict = new Dictionary<string, object>();
            while (lexer.Current != null)
            {
                var key = lexer.Current;
                lexer.ReadToken();
                if (key == ">>") break;
                dict.Add(key, parser.Read());
            }
            var type = GetText("/Type");
            if (type != null) Details = Type = type;
        }

        public double GetValue(string key)
        {
            var obj = this[key];
            if (obj is PdfObject)
                obj = (obj as PdfObject).Object;
            else if (obj is object[])
                obj = (obj as object[])[0];
            return (double)obj;
        }

        public string GetText(string key)
        {
            var obj = this[key];
            if (obj is PdfObject)
                obj = (obj as PdfObject).Object;
            else if (obj is object[])
                obj = (obj as object[])[0];
            return obj as string;
        }

        public object[] GetObjects(string key)
        {
            var obj = this[key];
            if (obj is PdfObject)
                obj = (obj as PdfObject).Object;
            return obj as object[];
        }

        public PdfObject GetObject(string key)
        {
            return this[key] as PdfObject;
        }

        public object this[string key]
        {
            get
            {
                if (dict == null || !dict.ContainsKey(key))
                    return null;
                var ret = dict[key];
                if (ret is PdfObject && doc.Parser != null)
                {
                    var obj = ret as PdfObject;
                    if (!obj.HasRead)
                    {
                        var parser = doc.Parser;
                        var state = parser.Lexer.Save();
                        obj.Read(parser);
                        parser.Lexer.Load(state);
                    }
                }
                return ret;
            }
        }

        public Dictionary<string, object>.KeyCollection Keys
        {
            get { return dict.Keys; }
        }

        public bool ContainsKey(string key)
        {
            return dict != null ? dict.ContainsKey(key) : false;
        }

        public bool HasValue { get { return Object is double; } }
        public bool HasText { get { return Object is string; } }
        public bool HasObjects { get { return Object is object[]; } }

        public double Value { get { return (double)Object; } }
        public string Text { get { return Object as string; } }
        public object[] Objects { get { return Object as object[]; } }
    }
}
