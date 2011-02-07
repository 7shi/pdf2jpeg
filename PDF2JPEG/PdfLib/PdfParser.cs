using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PdfLib
{
    public class PdfParser
    {
        private Stream stream;
        public PdfLexer Lexer { get; private set; }

        public PdfParser(Stream stream)
        {
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

        public void ReadXref(PdfDocument doc)
        {
            var xr = readStartXref();
            if (xr == 0) throw new Exception("not find: startxref");

            stream.Position = xr;
            readXref(doc);
        }

        private void readXref(PdfDocument doc)
        {
            Lexer.ReadToken();
            if (Lexer.Current != "xref")
            {
                readXrefObject(doc);
                return;
            }
            while (Lexer.Current != null)
            {
                Lexer.ReadToken();
                if (Lexer.Current == "trailer")
                {
                    readTrailer(doc);
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
                    if (no == 0 && t != 65535)
                        throw Lexer.Abort("xref: 0 must be 65535");
                    Lexer.ReadToken();
                    if (no == 0)
                    {
                        if (Lexer.Current != "f")
                            throw Lexer.Abort("xref: 0 must be 'f'");
                    }
                    else if (Lexer.Current != "n")
                        throw Lexer.Abort("xref: must be 'n'");
                    if (!doc.ContainsKey(no))
                        doc.Add(new PdfObject(no, 0, 0, offset));
                }
            }
        }

        private void readXrefObject(PdfDocument doc)
        {
            var obj = new PdfObject(doc, this);
            if (doc.ContainsKey(obj.Number)) return;
            doc.Add(obj);
            if (obj.Type != "/XRef" || obj.StreamLength == 0)
                throw Lexer.Abort("required: xref");
            if (obj.Dictionary == null)
                throw Lexer.Abort("required: << ... >>");
            if (!obj.Dictionary.ContainsKey("/W"))
                throw Lexer.Abort("required: /W");
            var w = obj["/W"] as object[];
            if (w == null || w.Length != 3)
                throw Lexer.Abort("required: /W [ n n n ]");
            var ww = new int[3];
            for (int i = 0; i < 3; i++) ww[i] = (int)(double)w[i];
            int size = 0;
            if (obj.Dictionary.ContainsKey("/Size"))
                size = (int)(double)obj["/Size"];
            int[] index = null;
            if (obj.Dictionary.ContainsKey("/Index"))
            {
                var idx = obj["/Index"] as object[];
                if (idx == null || idx.Length != 2)
                    throw Lexer.Abort("required: /Index [ n n ]");
                index = new int[idx.Length];
                for (int i = 0; i < idx.Length; i++)
                    index[i] = (int)(double)idx[i];
            }
            else
                index = new[] { 0, size };

            foreach (var key in new[] { "/Root", "/Size", "/Info", "/ID" })
            {
                if (obj.Dictionary.ContainsKey(key) && !doc.ContainsTrailer(key))
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
                        var f2 = ReadToInt64(s, ww[1]);
                        var f3 = ReadToInt64(s, ww[2]);
                        if (!doc.ContainsKey(j))
                        {
                            if (type == 1)
                                doc.Add(new PdfObject(j, 0, 0, f2));
                            else if (type == 2)
                                doc.Add(new PdfObject(j, (int)f2, (int)f3));
                        }
                    }
                }
            }

            Lexer.Clear();
            if (obj.Dictionary.ContainsKey("/Prev"))
            {
                stream.Position = (long)(double)obj["/Prev"];
                readXref(doc);
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

        private void readTrailer(PdfDocument doc)
        {
            Lexer.ReadToken();
            if (Lexer.Current != "<<")
                throw Lexer.Abort("required: <<");
            var dic = new PdfDictionary(this);
            long? prev = null, xrefstm = null;
            foreach (var key in dic.Keys)
            {
                if (key == "/Prev")
                    prev = (long)(double)dic[key];
                else if (key == "/XRefStm")
                    xrefstm = (long)(double)dic[key];
                else if (!doc.ContainsTrailer(key))
                    doc.AddTrailer(key, dic[key]);
            }
            if (xrefstm != null)
            {
                stream.Position = (long)prev;
                Lexer.Clear();
                Lexer.ReadToken();
                readXrefObject(doc);
            }
            else if (prev != null)
            {
                stream.Position = (long)prev;
                Lexer.Clear();
                readXref(doc);
            }
        }

        private object cache;

        public object Read()
        {
            if (cache != null)
            {
                var ret = cache;
                cache = null;
                return ret;
            }
            if (Lexer.IsNumber)
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
                        return new PdfReference((int)num, (int)num2);
                    }
                    else
                    {
                        cache = num2;
                        return num;
                    }
                }
                else
                    return num;
            }
            else if (Lexer.Current == "<<")
                return new PdfDictionary(this);
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
            else if (Lexer.Current == "<")
                return ReadTo('>');
            else if (Lexer.Current == "(")
                return ReadTo(')');
            else
            {
                var ret = Lexer.Current;
                Lexer.ReadToken();
                return ret;
            }
        }

        private string ReadTo(char end)
        {
            var sb = new StringBuilder(Lexer.Current);
            for (; ; )
            {
                int b = stream.ReadByte();
                if (b == -1) break;
                var ch = (char)b;
                sb.Append(ch);
                if (ch == end)
                {
                    Lexer.Clear();
                    Lexer.ReadToken();
                    break;
                }
            }
            return sb.ToString();
        }
    }
}
