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
                        label2.Text = "0";
                    }));
                    Parse(fs[i]);
                }
            }
            catch (Exception ex)
            {
                Invoke(new Action(() =>
                {
                    MessageBox.Show(ex.ToString());
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
        string token2, token1, token0;

        private string ReadToken(FileStream fs)
        {
            token2 = token1;
            token1 = token0;
            token0 = ReadTokenInternal(fs);
            if (token0 == "obj") int.TryParse(token2, out objno);
            return token0;
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
            token2 = token1 = token0 = null;
            int page = 1;
            var dir1 = Path.GetDirectoryName(file);
            var name = Path.GetFileNameWithoutExtension(file);
            int p1 = name.LastIndexOf(' ');
            if (p1 > 0) name = name.Substring(0, p1);
            var dir2 = Path.Combine(dir1, name);
            if (!Directory.Exists(dir2)) Directory.CreateDirectory(dir2);
            var dict = new Dictionary<int, int>();
            using (var fs = new FileStream(file, FileMode.Open))
            {
                string token;
                while ((token = ReadToken(fs)) != null)
                {
                    if (token == "<<")
                    {
                        var image = false;
                        int len = 0;
                        for (; token != ">>"; token = ReadToken(fs))
                        {
                            if (token == "/Image")
                                image = true;
                            else if (token == "/Length")
                                len = int.Parse(ReadToken(fs));
                            else if (token == "/Name")
                                ReadToken(fs);
                            else if (token.StartsWith("/Obj"))
                                dict[page] = int.Parse(ReadToken(fs));
                        }
                        token = ReadToken(fs);
                        if (token == "stream")
                        {
                            if (cur == 0x0d) cur = fs.ReadByte();
                            if (image)
                            {
                                Invoke(new Action(() =>
                                {
                                    label2.Text = page.ToString();
                                }));
                                var fn = Path.Combine(dir2, string.Format("{0:0000}.jpg", page));
                                var data = new byte[len];
                                fs.Read(data, 0, len);
                                File.WriteAllBytes(fn, data);
                                page++;
                            }
                            cur = fs.ReadByte();
                        }
                    }
                }
            }
        }
    }
}
