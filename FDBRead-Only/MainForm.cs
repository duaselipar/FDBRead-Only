using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Linq;


namespace FDBRead_Only
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            InitializeComponent();
        }

        // Button: Load FDB file (GMTool/EPL style)
        private void btnLoad_Click(object sender, EventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "FDB Files (*.fdb)|*.fdb|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK)
                return;

            string filePath = dlg.FileName;
            string fileName = Path.GetFileName(filePath);

            try
            {
                var (fields, rows) = FdbLoaderEPLStyle.Load(filePath);

                dataGridView1.Columns.Clear();
                foreach (var field in fields)
                {
                    string name = string.IsNullOrWhiteSpace(field.Name) ? $"Field{fields.IndexOf(field) + 1}" : field.Name;
                    dataGridView1.Columns.Add(name, name);
                }

                dataGridView1.Rows.Clear();
                foreach (var row in rows)
                {
                    dataGridView1.Rows.Add(row.ToArray());
                }

                dataGridView1.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.AllCells);

                // Set window title bar to include filename
                this.Text = $"FDB Editor ({fileName})";

                string msg = $"Loaded {rows.Count} records, {fields.Count} fields.";
                MessageBox.Show(msg, "Success");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load FDB:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    // Field info
    public class FdbField
    {
        public byte Type;    // 1-10
        public int Offset;   // offset from textbase for name
        public string Name;  // field name
    }

    /// <summary>
    /// EPL/GMTool style FDB Loader — no extra ID field, full row as per field structure.
    /// </summary>
    public static class FdbLoaderEPLStyle
    {




        public static (List<FdbField>, List<List<object>>) Load(string path)
        {
            var data = File.ReadAllBytes(path);

            // Header & Layout info
            const int HEADER_SIZE = 0x20;
            int fieldCount = BitConverter.ToInt32(data, 0x14);
            int rowCount = BitConverter.ToInt32(data, 0x18);
            int textLen = BitConverter.ToInt32(data, 0x1C);
            int textBase = data.Length - textLen;

            // Use GBK encoding for all strings
            var gbk = Encoding.GetEncoding("GBK");

            // 1. Parse field names
            List<string> labels = new();
            int ptr = textBase;
            for (int i = 0; i < fieldCount; i++)
            {
                int start = ptr;
                while (ptr < data.Length && data[ptr] != 0) ptr++;
                labels.Add(gbk.GetString(data, start, ptr - start));
                ptr++; // skip null
            }

            // 2. Field types (setiap 5 byte per field)
            var fields = new List<FdbField>();
            for (int i = 0; i < fieldCount; i++)
            {
                int fieldOffset = HEADER_SIZE + i * 5;
                byte type = data[fieldOffset];
                fields.Add(new FdbField
                {
                    Type = type,
                    Name = labels[i]
                });
            }

            // 3. Row pointer table (8 byte per row, pointer = 2nd DWORD)
            int ptrTableOffset = HEADER_SIZE + fieldCount * 5;
            var rowPtrs = new List<int>();
            for (int i = 0; i < rowCount; i++)
            {
                int recPos = ptrTableOffset + i * 8;
                int recPtr = BitConverter.ToInt32(data, recPos + 4);
                rowPtrs.Add(recPtr);
            }

            // 4. Parse rows
            var rows = new List<List<object>>();
            foreach (var rowPtr in rowPtrs)
            {
                // Nilai pointer "kosong" biasanya 0, -1, atau D6000000
                if (rowPtr <= 0 || rowPtr == 0xD6000000)
                {
                    rows.Add(Enumerable.Repeat<object>("", fields.Count).ToList());
                    continue;
                }
                int pos = rowPtr;
                var values = new List<object>();
                for (int f = 0; f < fields.Count; f++)
                {
                    var field = fields[f];
                    object val = null;
                    switch (field.Type)
                    {
                        case 1: val = data[pos]; pos += 1; break;
                        case 2: val = BitConverter.ToInt16(data, pos); pos += 2; break;
                        case 3: val = (ushort)BitConverter.ToInt16(data, pos); pos += 2; break;
                        case 4: val = BitConverter.ToInt32(data, pos); pos += 4; break;
                        case 5: val = (uint)BitConverter.ToInt32(data, pos); pos += 4; break;
                        case 6: val = BitConverter.ToSingle(data, pos); pos += 4; break;
                        case 7: val = BitConverter.ToDouble(data, pos); pos += 8; break;
                        case 8: val = BitConverter.ToInt64(data, pos); pos += 8; break;
                        case 9: val = (ulong)BitConverter.ToInt64(data, pos); pos += 8; break;
                        case 10:
                            int strPtr = BitConverter.ToInt32(data, pos);
                            int strAddr = textBase + strPtr;
                            val = "";
                            if (strAddr >= 0 && strAddr < data.Length)
                            {
                                int end = strAddr;
                                while (end < data.Length && data[end] != 0) end++;
                                val = gbk.GetString(data, strAddr, end - strAddr);
                            }
                            pos += 4;
                            break;
                        default:
                            val = ""; break;
                    }
                    values.Add(val);
                }
                rows.Add(values);
            }
            return (fields, rows);
        }






    }
}
