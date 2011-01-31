using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PDF2JPEG
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        protected override void OnDragEnter(DragEventArgs drgevent)
        {
            base.OnDragEnter(drgevent);
            if (drgevent.Data.GetDataPresent(DataFormats.FileDrop))
                drgevent.Effect = DragDropEffects.Link;
        }

        protected override void OnDragDrop(DragEventArgs drgevent)
        {
            base.OnDragDrop(drgevent);
            var files = drgevent.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;

            AllowDrop = false;
            var th = new Thread(new ParameterizedThreadStart(ReadPDFs));
            th.Start(files);
        }

        private void ReadPDFs(object files)
        {
            try
            {
                var fs = files as string[];
                for (int i = 0; i < fs.Length; i++)
                {
                    Invoke(new Action(() =>
                    {
                        label1.Text = string.Format("{0}/{1}: {2}", i + 1, fs.Length, fs[i]);
                        label2.Text = "Parsing...";
                    }));
                    Parse(fs[i]);
                }
            }
            catch (Exception ex)
            {
                Invoke(new Action(() =>
                {
                    MessageBox.Show(ex.ToString());
                    label2.Text += ex.Message;
                }));
            }
            finally
            {
                Invoke(new Action(() =>
                {
                    AllowDrop = true;
                }));
            }
        }

        int cur, objno;
        string token2, token1, current;

        private void ReadToken(Stream fs, bool isStream = false)
        {
            if (!isStream)
            {
                token2 = token1;
                token1 = current;
                current = ReadTokenInternal(fs);
                if (current == "obj") objno = int.Parse(token2);
            }
            else
                current = ReadTokenInternal(fs);
        }

        private string ReadTokenInternal(Stream fs)
        {
            if (cur == 0) cur = fs.ReadByte();
            if (cur == -1) return null;

            var sb = new StringBuilder();
            for (; cur != -1; cur = fs.ReadByte())
            {
                var ch = (char)cur;
                if (char.IsDigit(ch))
                {
                    for (; cur != -1; cur = fs.ReadByte())
                    {
                        var ch2 = (char)cur;
                        if (ch2 == '.' || char.IsDigit(ch2))
                            sb.Append(ch2);
                        else
                            break;
                    }
                    break;
                }
                else if (ch == '/' || char.IsLetter(ch))
                {
                    do
                    {
                        sb.Append((char)cur);
                        cur = fs.ReadByte();
                    }
                    while (cur != -1 && (cur == '_' || char.IsLetterOrDigit((char)cur)));
                    break;
                }
                else if (ch > ' ')
                {
                    sb.Append(ch);
                    cur = fs.ReadByte();
                    if ((ch == '<' || ch == '>') && ch == cur)
                    {
                        sb.Append(ch);
                        cur = fs.ReadByte();
                    }
                    break;
                }
            }
            return sb.ToString();
        }

        private int page;
        private Dictionary<int, int> dict;
        private Dictionary<int, Tuple<long, int>> imgd;

        private void Parse(string file)
        {
            cur = objno = 0;
            token2 = token1 = current = null;
            page = 0;
            var dir1 = Path.GetDirectoryName(file);
            var name = Path.GetFileNameWithoutExtension(file);
            int p1 = name.LastIndexOf(' ');
            if (p1 > 0) name = name.Substring(0, p1);
            var dir2 = Path.Combine(dir1, name);
            if (!Directory.Exists(dir2)) Directory.CreateDirectory(dir2);
            dict = new Dictionary<int, int>();
            imgd = new Dictionary<int, Tuple<long, int>>();
            using (var fs = new FileStream(file, FileMode.Open))
            {
                ReadStream(fs);
                if (dict.Count != imgd.Count)
                    throw new Exception(string.Format("page count {0} != {1}", dict.Count, imgd.Count));
                var pages = dict.Keys.ToList();
                pages.Sort();
                int max = pages[pages.Count - 1];
                foreach (var p in pages)
                {
                    var id = dict[p];
                    var tup = imgd[id];
                    Invoke(new Action(() =>
                    {
                        label2.Text = string.Format("{0}/{1}", p, max);
                    }));
                    var fn = Path.Combine(dir2, string.Format("{0:0000}.jpg", p));
                    var data = new byte[tup.Item2];
                    fs.Position = tup.Item1;
                    fs.Read(data, 0, data.Length);
                    File.WriteAllBytes(fn, data);
                }
            }
        }

        private void ReadStream(Stream st)
        {
            ReadToken(st);
            while (current != null)
            {
                if (current == "<<")
                    ReadDictionary(st);
                else
                    ReadToken(st);
            }
        }

        private void ReadDictionary(Stream st)
        {
            ReadToken(st);
            bool jpeg = false, flate = false, objstm = false;
            int len = 0, imgno = 0;
            while (current != null && current != ">>")
            {
                if (current == "<<")
                    ReadDictionary(st);
                else if (current == "/DCTDecode")
                {
                    ReadToken(st);
                    jpeg = true;
                }
                else if (current == "/FlateDecode")
                {
                    ReadToken(st);
                    flate = true;
                }
                else if (current == "/ObjStm")
                {
                    ReadToken(st);
                    objstm = true;
                }
                else if (current == "/Length")
                {
                    ReadToken(st);
                    len = int.Parse(current);
                    ReadToken(st);
                    if (current == "0")
                    {
                        ReadToken(st);
                        if (current != "R") throw new Exception("R required");
                        ReadToken(st);
                        len = 0;
                    }
                }
                else if (current == "/Page")
                {
                    ReadToken(st);
                    page++;
                }
                else if (current == "/Name")
                {
                    ReadToken(st);
                    ReadToken(st);
                }
                else if (current.StartsWith("/Obj"))
                {
                    ReadToken(st);
                    imgno = int.Parse(current);
                    ReadToken(st);
                }
                else
                    ReadToken(st);
            }
            if (imgno > 0)
            {
                if (page == 0) throw new Exception("page 0");
                if (dict.ContainsKey(page))
                    throw new Exception("duplicate page " + page);
                dict[page] = imgno;
            }
            ReadToken(st);
            if (current == "stream")
            {
                if (cur == 0x0d) cur = st.ReadByte();
                if (objstm)
                {
                    if (flate)
                    {
                        st.Position += 2;
                        var ss = new SubStream(st, len - 2);
                        ReadStream(new DeflateStream(ss, CompressionMode.Decompress));
                    }
                    else
                        ReadStream(new SubStream(st, len));
                }
                else
                {
                    if (jpeg)
                    {
                        var tup = new Tuple<long, int>(st.Position, len);
                        imgd[objno] = tup;
                    }
                    if (len > 0)
                        st.Position += len;
                    else
                        while (current != "endstream")
                            ReadToken(st, true);
                }
                cur = 0;
                ReadToken(st);
            }
        }
    }
}
