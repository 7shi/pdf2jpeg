using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PdfLib
{
    public class PdfParser
    {
        private PdfDocument doc;
        private Stream stream;
        public PdfLexer Lexer { get; private set; }

        public PdfParser(PdfDocument doc, Stream stream)
        {
            this.doc = doc;
            this.stream = stream;
            Lexer = new PdfLexer(stream);
        }

        private long readStartXref()
        {
            stream.Position = Math.Max(0, stream.Length - 64);
            var last = Lexer.ReadAscii(64);
            var p = last.LastIndexOf("startxref");
            if (p < 0) return 0;
            p += 9;
            while (p < last.Length && last[p] <= ' ') p++;
            return PdfLexer.ReadInt64(last, p);
        }

        public void ReadXref()
        {
            var xr = readStartXref();
            if (xr == 0) throw new Exception("not find: startxref");

            stream.Position = xr;
            readXref();
        }

        private void readXref()
        {
            Lexer.ReadToken();
            if (Lexer.Current != "xref")
            {
                readXrefObject();
                return;
            }
            while (Lexer.Current != null)
            {
                Lexer.ReadToken();
                if (Lexer.Current == "trailer")
                {
                    readTrailer();
                    return;
                }
                var start = int.Parse(Lexer.Current);
                Lexer.ReadToken();
                var size = int.Parse(Lexer.Current);
                for (int i = 0; i < size; i++)
                {
                    int no = start + i;
                    Lexer.ReadToken();
                    long offset = long.Parse(Lexer.Current);
                    Lexer.ReadToken();
                    int t = int.Parse(Lexer.Current);
                    Lexer.ReadToken();
                    if (offset > 0 && Lexer.Current == "n")
                    {
                        var p = doc.GetObject(no);
                        if (p.Position == 0 && p.ObjStm == 0)
                            p.Position = offset;
                    }
                }
            }
        }

        private void readXrefObject()
        {
            var obj = new PdfObject(doc, int.Parse(Lexer.Current));
            obj.Position = Lexer.Position;
            obj.Read(this);
            if (doc.ContainsKey(obj.Number)) return;
            doc.Add(obj);
            if (obj.Type != "/XRef" || obj.StreamLength == 0)
                throw Lexer.Abort("required: xref");
            obj.Details = obj.Type;
            var w = obj.GetObjects("/W");
            if (w == null || w.Length != 3)
                throw Lexer.Abort("required: /W [ n n n ]");
            var ww = new int[3];
            for (int i = 0; i < 3; i++) ww[i] = (int)(double)w[i];
            int size = 0;
            if (obj.ContainsKey("/Size"))
                size = (int)obj.GetValue("/Size");
            int[] index = null;
            if (obj.ContainsKey("/Index"))
            {
                var idx = obj.GetObjects("/Index");
                if (idx == null || idx.Length == 0 || (idx.Length & 1) != 0)
                    throw Lexer.Abort("required: /Index [ n n ... ]");
                index = new int[idx.Length];
                for (int i = 0; i < idx.Length; i++)
                    index[i] = (int)(double)idx[i];
            }
            else
                index = new[] { 0, size };

            foreach (var key in new[] { "/Root", "/Size", "/Info", "/ID" })
            {
                if (obj.ContainsKey(key) && !doc.ContainsTrailer(key))
                    doc.AddTrailer(key, obj[key]);
            }

            using (var s = obj.GetStream(stream))
            {
                for (int i = 0; i < index.Length; i += 2)
                {
                    var end = index[i] + index[i + 1];
                    for (int j = index[i]; j < end; j++)
                    {
                        var type = ww[0] == 0 ? 1 : ReadToInt64(s, ww[0]);
                        if (type < 0 || type > 2)
                            throw Lexer.Abort("invalid type: {0}", type);
                        var f2 = ReadToInt64(s, ww[1]);
                        var f3 = ReadToInt64(s, ww[2]);
                        if (type == 0) continue;
                        var p = doc.GetObject(j);
                        if (f2 > 0 && p.Position == 0 && p.ObjStm == 0)
                        {
                            if (type == 1)
                                p.Position = f2;
                            else if (type == 2)
                            {
                                p.ObjStm = (int)f2;
                                p.Index = (int)f3;
                                var objstm = doc.GetObject((int)f2);
                                objstm.Details = "/ObjStm";
                            }
                        }
                    }
                }
            }

            Lexer.Clear();
            if (obj.ContainsKey("/Prev"))
            {
                stream.Position = (long)obj.GetValue("/Prev");
                readXref();
            }
        }

        public static long ReadToInt64(Stream s, int len)
        {
            long ret = 0;
            for (int i = 0; i < len; i++)
            {
                var b = s.ReadByte();
                if (b == -1) break;
                ret = (ret << 8) + b;
            }
            return ret;
        }

        private void readTrailer()
        {
            Lexer.ReadToken();
            if (Lexer.Current != "<<")
                throw Lexer.Abort("required: <<");
            Lexer.ReadToken();
            var dict = new PdfObject(doc, this);
            long? prev = null, xrefstm = null;
            foreach (var key in dict.Keys)
            {
                if (key == "/Prev")
                    prev = (long)dict.GetValue(key);
                else if (key == "/XRefStm")
                    xrefstm = (long)dict.GetValue(key);
                else if (!doc.ContainsTrailer(key))
                    doc.AddTrailer(key, dict[key]);
            }
            if (xrefstm != null)
            {
                stream.Position = (long)prev;
                Lexer.Clear();
                Lexer.ReadToken();
                readXrefObject();
            }
            else if (prev != null)
            {
                stream.Position = (long)prev;
                Lexer.Clear();
                readXref();
            }
        }

        private double? cache;

        public object Read()
        {
            if (cache != null)
            {
                var ret = cache.Value;
                cache = null;
                return ret;
            }
            else if (Lexer.IsNumber)
            {
                var num = double.Parse(Lexer.Current);
                Lexer.ReadToken();
                if (Lexer.IsNumber)
                {
                    var num2 = double.Parse(Lexer.Current);
                    Lexer.ReadToken();
                    if (Lexer.Current == "R")
                    {
                        Lexer.ReadToken();
                        return doc.GetObject((int)num);
                    }
                    else
                        cache = num2;
                }
                return num;
            }
            else if (Lexer.Current == "<<")
            {
                Lexer.ReadToken();
                return new PdfObject(doc, this);
            }
            else if (Lexer.Current == "[")
            {
                var list = new List<object>();
                Lexer.ReadToken();
                while (Lexer.Current != null)
                {
                    if (cache == null && Lexer.Current == "]")
                    {
                        Lexer.ReadToken();
                        break;
                    }
                    list.Add(Read());
                }
                return list.ToArray();
            }
            else
            {
                var ret = Lexer.Current;
                Lexer.ReadToken();
                return ret;
            }
        }

        public void ReadLinear()
        {
            PdfObject root = null;
            stream.Position = 0;
            long len = stream.Length;
            int p = 0;
            for (; ; )
            {
                Lexer.ReadToken();
                if (Lexer.Current == null)
                    break;
                else if (Lexer.Current == "obj")
                {
                    int pp = (int)(Lexer.ObjPos * 100 / len);
                    if (p != pp) doc.Progress(p = pp);
                    var obj = doc.GetObject(Lexer.ObjNo);
                    System.Diagnostics.Debug.Print("{0} {1}", Lexer.ObjPos, Lexer.ObjNo);
                    obj.Init(Lexer.ObjPos);
                    obj.Read(this);
                    if (obj.Type == "/Catalog") root = obj;
                }
            }
            doc.Progress(100);
            if (root != null)
            {
                root.Details = "/Root";
                doc.AddTrailer("/Root", root);
            }
        }
    }
}
