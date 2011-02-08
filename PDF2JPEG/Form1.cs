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
using PdfLib;

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
            backgroundWorker1.RunWorkerAsync(files);
        }

        private void CheckDir(List<string> list, string dir)
        {
            foreach (var d in Directory.GetDirectories(dir))
                CheckDir(list, d);
            foreach (var pdf in Directory.GetFiles(dir, "*.pdf"))
                list.Add(pdf);
        }

        private void Parse(string file)
        {
            var dir1 = Path.GetDirectoryName(file);
            var fn = Path.GetFileNameWithoutExtension(file);
            //int p1 = fn.LastIndexOf(' ');
            //if (p1 > 0) fn = fn.Substring(0, p1);
            var dir2 = Path.Combine(dir1, fn);
            if (!Directory.Exists(dir2)) Directory.CreateDirectory(dir2);

            using (var doc = new PdfDocument(file))
            {
                int n = doc.PageCount;
                for (int i = 1; i <= n; i++)
                {
                    if (backgroundWorker1.CancellationPending) return;
                    backgroundWorker1.ReportProgress(0, new string[] { null, i + "/" + n });

                    var page = doc.GetPage(i);
                    var rsrc = page.GetObject("/Resources");
                    if (rsrc == null) continue;
                    var xobj = rsrc.GetObject("/XObject");
                    if (xobj == null) continue;

                    foreach (var key in xobj.Keys)
                    {
                        var obj = xobj.GetObject(key);
                        if (obj != null
                            && obj.GetText("/Subtype") == "/Image"
                            && obj.GetText("/Filter") == "/DCTDecode")
                        {
                            File.WriteAllBytes(
                                Path.Combine(dir2, string.Format("{0:0000}.jpg", i)),
                                obj.GetStreamBytes());
                        }
                    }
                }
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (backgroundWorker1.IsBusy)
            {
                var result = MessageBox.Show(
                    this, "処理を中止しますか？", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.No) e.Cancel = true;
            }
        }

        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            deleteToolStripMenuItem.Checked = !deleteToolStripMenuItem.Checked;
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            var list = new List<string>();
            foreach (var f in e.Argument as string[])
            {
                if (Directory.Exists(f))
                    CheckDir(list, f);
                else if (File.Exists(f) && Path.GetExtension(f).ToLower() == ".pdf")
                    list.Add(f);
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (backgroundWorker1.CancellationPending) return;
                var pdf = list[i];
                backgroundWorker1.ReportProgress(0, new[]
                {
                    string.Format("{0}/{1}: {2}", i + 1, list.Count, Path.GetFileName(pdf)),
                    "Parsing..."
                });
                Parse(pdf);
                backgroundWorker1.ReportProgress(0, pdf);
            }
        }

        private void backgroundWorker1_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (e.UserState is string[])
            {
                var st = e.UserState as string[];
                if (st[0] != null) label1.Text = st[0];
                if (st[1] != null) label2.Text = st[1];
            }
            else if (e.UserState is string)
            {
                if (deleteToolStripMenuItem.Checked)
                    File.Delete(e.UserState as string);
            }
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                MessageBox.Show(e.Error.ToString());
                label2.Text += " " + e.Error.Message;
            }
            else if (!e.Cancelled)
            {
                AllowDrop = true;
                label2.Text += " 完了";
            }
        }
    }
}
