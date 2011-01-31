using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
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

        private void ReadToken(FileStream fs, bool isStream = false)
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

        private string ReadTokenInternal(FileStream fs)
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

        private void Parse(string file)
        {
            cur = objno = 0;
            token2 = token1 = current = null;
            int page = 0;
            var dir1 = Path.GetDirectoryName(file);
            var name = Path.GetFileNameWithoutExtension(file);
            int p1 = name.LastIndexOf(' ');
            if (p1 > 0) name = name.Substring(0, p1);
            var dir2 = Path.Combine(dir1, name);
            if (!Directory.Exists(dir2)) Directory.CreateDirectory(dir2);
            var dict = new Dictionary<int, int>();
            var imgd = new Dictionary<int, Tuple<long, int>>();
            var imgl = new List<Tuple<long, int>>();
            using (var fs = new FileStream(file, FileMode.Open))
            {
                ReadToken(fs);
                while (current != null)
                {
                    if (current == "<<")
                    {
                        ReadToken(fs);
                        bool jpeg = false, lengthOnly = false;
                        int len = 0;
                        while (current != null && current != ">>")
                        {
                            if (current == "/DCTDecode")
                            {
                                ReadToken(fs);
                                jpeg = true;
                            }
                            else if (current == "/Length")
                            {
                                var prev = token1;
                                ReadToken(fs);
                                len = int.Parse(current);
                                ReadToken(fs);
                                if (current == "0")
                                {
                                    ReadToken(fs);
                                    if (current != "R") throw new Exception("R required");
                                    ReadToken(fs);
                                    len = 0;
                                }
                                if (prev == "<<" && current == ">>")
                                    lengthOnly = true;
                            }
                            else if (current == "/Page")
                            {
                                ReadToken(fs);
                                page++;
                            }
                            else if (current == "/Name")
                            {
                                ReadToken(fs);
                                ReadToken(fs);
                            }
                            else if (current.StartsWith("/Obj") && current != "/ObjStm")
                            {
                                ReadToken(fs);
                                dict[page] = int.Parse(current);
                                ReadToken(fs);
                            }
                            else
                                ReadToken(fs);
                        }
                        ReadToken(fs);
                        if (current == "stream")
                        {
                            if (cur == 0x0d) cur = fs.ReadByte();
                            if (jpeg)
                            {
                                var tup = new Tuple<long, int>(fs.Position, len);
                                imgd[objno] = tup;
                                imgl.Add(tup);
                            }
                            if (len > 0)
                                fs.Position += len;
                            else
                                while (current != "endstream")
                                    ReadToken(fs, true);
                            cur = 0;
                            ReadToken(fs);
                        }
                    }
                    else
                        ReadToken(fs);
                }
                if (dict.Count < imgl.Count)
                {
                    int max = imgl.Count;
                    for (int i = 0; i < max; i++)
                    {
                        var tup = imgl[i];
                        Invoke(new Action(() =>
                        {
                            label2.Text = string.Format("{0}/{1}", i + 1, max);
                        }));
                        var fn = Path.Combine(dir2, string.Format("{0:0000}.jpg", i + 1));
                        var data = new byte[tup.Item2];
                        fs.Position = tup.Item1;
                        fs.Read(data, 0, data.Length);
                        File.WriteAllBytes(fn, data);
                    }
                }
                else
                {
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
        }
    }
}
