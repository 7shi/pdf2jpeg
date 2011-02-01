using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using iTextSharp.text;
using iTextSharp.text.pdf;

namespace PDF2JPEG
{
    public delegate void Action();

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
                        label1.Text = string.Format("{0}/{1}: {2}",
                            i + 1, fs.Length, Path.GetFileName(fs[i]));
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
                    label2.Text += " " + ex.Message;
                }));
            }
            finally
            {
                Invoke(new Action(() =>
                {
                    AllowDrop = true;
                    label2.Text += " 完了";
                }));
            }
        }

        private void Parse(string file)
        {
            var dir1 = Path.GetDirectoryName(file);
            var fn = Path.GetFileNameWithoutExtension(file);
            //int p1 = fn.LastIndexOf(' ');
            //if (p1 > 0) fn = fn.Substring(0, p1);
            var dir2 = Path.Combine(dir1, fn);
            if (!Directory.Exists(dir2)) Directory.CreateDirectory(dir2);

            var pdf = new PdfReader(file);
            int n = pdf.NumberOfPages;
            for (int i = 1; i <= n; i++)
            {
                Invoke(new Action(() =>
                {
                    label2.Text = string.Format("{0}/{1}", i, n);
                }));

                var pg = pdf.GetPageN(i);
                var res = PdfReader.GetPdfObject(pg.Get(PdfName.RESOURCES)) as PdfDictionary;
                var xobj = PdfReader.GetPdfObject(res.Get(PdfName.XOBJECT)) as PdfDictionary;
                if (xobj == null) continue;

                foreach (var name in xobj.Keys)
                {
                    var obj = xobj.Get(name);
                    if (!obj.IsIndirect()) continue;

                    var tg = PdfReader.GetPdfObject(obj) as PdfDictionary;
                    var type = PdfReader.GetPdfObject(tg.Get(PdfName.SUBTYPE)) as PdfName;
                    if (!PdfName.IMAGE.Equals(type)) continue;

                    int XrefIndex = (obj as PRIndirectReference).Number;
                    var pdfStream = pdf.GetPdfObject(XrefIndex) as PRStream;
                    var data = PdfReader.GetStreamBytesRaw(pdfStream);
                    var jpeg = Path.Combine(dir2, string.Format("{0:0000}.jpg", i));
                    File.WriteAllBytes(jpeg, data);
                    break;
                }
            }
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var about = new Form2())
                about.ShowDialog(this);
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
