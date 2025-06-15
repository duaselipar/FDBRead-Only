using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Linq;


namespace FDBRead_Only
{
    public partial class Form1 : Form
    {
        private List<FdbField> fdbFields;
        private List<List<object>> fdbRows;
        private string loadedFilePath = "";
        private ContextMenuStrip contextMenu;
        private bool sortAscending = true;
        private int lastSortColumn = -1;
        private byte[] originalHeader = null;
        private byte[] fileOriginalBytes = null; // Patch: keep original file bytes for perfect clone
        private bool dataDirty = false;          // Track if user has changed anything

        public Form1()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            InitializeComponent();

            dataGridView1.VirtualMode = true;
            dataGridView1.ReadOnly = false;
            dataGridView1.MultiSelect = true;
            dataGridView1.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridView1.AllowUserToAddRows = false;
            dataGridView1.AllowUserToDeleteRows = false;
            dataGridView1.AllowUserToOrderColumns = true;
            dataGridView1.AllowUserToResizeRows = true;
            dataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;

            dataGridView1.CellValueNeeded += DataGridView1_CellValueNeeded;
            dataGridView1.CellValuePushed += DataGridView1_CellValuePushed;
            dataGridView1.ColumnHeaderMouseClick += DataGridView1_ColumnHeaderMouseClick;
            dataGridView1.CellValueChanged += DataGridView1_CellValueChanged;
            dataGridView1.RowsRemoved += DataGridView1_RowsRemoved;
            dataGridView1.UserAddedRow += DataGridView1_UserAddedRow;

            btnSave.Click += btnSave_Click;
            InitializeContextMenu();
            dataGridView1.ContextMenuStrip = contextMenu;
        }

        private void btnLoad_Click(object sender, EventArgs e)
        {
            // Clean up
            if (fdbRows != null) fdbRows.Clear();
            if (fdbFields != null) fdbFields.Clear();
            fdbRows = null;
            fdbFields = null;
            originalHeader = null;
            fileOriginalBytes = null;
            dataDirty = false;

            dataGridView1.Columns.Clear();
            dataGridView1.RowCount = 0;
            dataGridView1.DataSource = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();

            var dlg = new OpenFileDialog
            {
                Filter = "FDB Files (*.fdb)|*.fdb|All Files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK)
                return;

            loadedFilePath = dlg.FileName;
            string fileName = Path.GetFileName(loadedFilePath);

            try
            {
                fileOriginalBytes = File.ReadAllBytes(loadedFilePath);
                (fdbFields, fdbRows, originalHeader) = FdbLoaderEPLStyle.Load(loadedFilePath);
                dataDirty = false; // reset dirty on load

                dataGridView1.Columns.Clear();
                foreach (var field in fdbFields)
                {
                    string name = string.IsNullOrWhiteSpace(field.Name) ? $"Field{fdbFields.IndexOf(field) + 1}" : field.Name;
                    dataGridView1.Columns.Add(name, name);
                }
                dataGridView1.RowCount = fdbRows.Count;

                this.Text = $"FDB Editor ({fileName}) — {fdbRows.Count} records, {fdbFields.Count} fields";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load FDB:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---- Virtual Mode Cell Events ----
        private void DataGridView1_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (fdbRows == null || e.RowIndex < 0 || e.RowIndex >= fdbRows.Count || e.ColumnIndex < 0 || e.ColumnIndex >= fdbFields.Count)
                e.Value = "";
            else
                e.Value = fdbRows[e.RowIndex][e.ColumnIndex];
        }
        private void DataGridView1_CellValuePushed(object sender, DataGridViewCellValueEventArgs e)
        {
            if (fdbRows == null || e.RowIndex < 0 || e.RowIndex >= fdbRows.Count || e.ColumnIndex < 0 || e.ColumnIndex >= fdbFields.Count)
                return;
            fdbRows[e.RowIndex][e.ColumnIndex] = e.Value;
            dataDirty = true;
        }
        private void DataGridView1_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            dataDirty = true;
        }
        private void DataGridView1_RowsRemoved(object sender, DataGridViewRowsRemovedEventArgs e)
        {
            dataDirty = true;
        }
        private void DataGridView1_UserAddedRow(object sender, DataGridViewRowEventArgs e)
        {
            dataDirty = true;
        }

        // ---- Manual Sorting ----
        private void DataGridView1_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (fdbRows == null) return;
            int col = e.ColumnIndex;
            if (lastSortColumn == col)
                sortAscending = !sortAscending;
            else
                sortAscending = true;

            fdbRows = (sortAscending
                ? fdbRows.OrderBy(r => r[col] ?? "").ToList()
                : fdbRows.OrderByDescending(r => r[col] ?? "").ToList());

            lastSortColumn = col;
            dataDirty = true; // user change order
            dataGridView1.Invalidate();
        }

        // ---- Context Menu Delete (multi-select) ----
        private void InitializeContextMenu()
        {
            contextMenu = new ContextMenuStrip();
            var deleteItem = new ToolStripMenuItem("Delete Selected Row(s)");
            deleteItem.Click += DeleteSelectedRows_Click;
            contextMenu.Items.Add(deleteItem);
        }

        private void DeleteSelectedRows_Click(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count == 0) return;

            var indexes = dataGridView1.SelectedRows
                .Cast<DataGridViewRow>()
                .Where(r => !r.IsNewRow)
                .Select(r => r.Index)
                .OrderByDescending(i => i)
                .ToList();

            foreach (var idx in indexes)
            {
                if (idx >= 0 && idx < fdbRows.Count)
                    fdbRows.RemoveAt(idx);
            }
            dataGridView1.RowCount = fdbRows.Count;
            dataGridView1.ClearSelection();
            dataGridView1.Invalidate();
            dataDirty = true;
        }

        // ---- Save Function (ikut susunan grid semasa) ----
        private void btnSave_Click(object sender, EventArgs e)
        {
            if (fdbFields == null || fdbRows == null || fdbRows.Count == 0)
            {
                MessageBox.Show("No data to save!", "Error");
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "FDB Files (*.fdb)|*.fdb|All Files (*.*)|*.*",
                FileName = Path.GetFileNameWithoutExtension(loadedFilePath) + "_mod.fdb"
            };
            if (dlg.ShowDialog() != DialogResult.OK)
                return;

            try
            {
                // 1. If data never changed since load, save exact original (100% match)
                if (!dataDirty && fileOriginalBytes != null)
                {
                    File.WriteAllBytes(dlg.FileName, fileOriginalBytes);
                }
                else
                {
                    FdbLoaderEPLStyle.Save(dlg.FileName, fdbFields, fdbRows, originalHeader);
                }
                MessageBox.Show("Save completed.", "Success");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save FDB:\n{ex.Message}", "Error");
            }
        }
    }

    public class FdbField
    {
        public byte Type;    // 1-10
        public int Offset;   // offset from textbase for name (tak wajib)
        public string Name;  // field name
    }

    public static class FdbLoaderEPLStyle
    {
        public static (List<FdbField>, List<List<object>>, byte[]) Load(string path)
        {
            var data = File.ReadAllBytes(path);
            const int HEADER_SIZE = 0x20;

            // PATCH: Save original header
            byte[] header = new byte[HEADER_SIZE];
            Array.Copy(data, 0, header, 0, HEADER_SIZE);

            int fieldCount = BitConverter.ToInt32(data, 0x14);
            int rowCount = BitConverter.ToInt32(data, 0x18);
            int textLen = BitConverter.ToInt32(data, 0x1C);
            int textBase = data.Length - textLen;

            var gbk = Encoding.GetEncoding("GBK");
            List<string> labels = new();
            int ptr = textBase;
            for (int i = 0; i < fieldCount; i++)
            {
                int start = ptr;
                while (ptr < data.Length && data[ptr] != 0) ptr++;
                labels.Add(gbk.GetString(data, start, ptr - start));
                ptr++; // skip null
            }

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

            int ptrTableOffset = HEADER_SIZE + fieldCount * 5;
            var rowPtrs = new List<int>();
            for (int i = 0; i < rowCount; i++)
            {
                int recPos = ptrTableOffset + i * 8;
                int recPtr = BitConverter.ToInt32(data, recPos + 4);
                rowPtrs.Add(recPtr);
            }

            var rows = new List<List<object>>(rowCount);
            foreach (var rowPtr in rowPtrs)
            {
                if (rowPtr <= 0 || rowPtr == 0xD6000000)
                {
                    rows.Add(Enumerable.Repeat<object>("", fields.Count).ToList());
                    continue;
                }
                int pos = rowPtr;
                var values = new List<object>(fields.Count);
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
            return (fields, rows, header);
        }

        public static void Save(string path, List<FdbField> fields, List<List<object>> rows, byte[] header)
        {
            const int HEADER_SIZE = 0x20;
            var gbk = Encoding.GetEncoding("GBK");
            int fieldCount = fields.Count;
            int rowCount = rows.Count;

            // ===== 1. BUILD TEXT POOL =====
            List<byte> textBytes = new();
            Dictionary<string, int> stringPointerDict = new();
            foreach (var f in fields)
            {
                var raw = gbk.GetBytes(f.Name ?? "");
                textBytes.AddRange(raw);
                textBytes.Add(0);
            }
            foreach (var row in rows)
            {
                for (int f = 0; f < fields.Count; f++)
                {
                    if (fields[f].Type == 10)
                    {
                        string s = row[f]?.ToString() ?? "";
                        if (!stringPointerDict.ContainsKey(s))
                        {
                            stringPointerDict[s] = textBytes.Count;
                            var raw = gbk.GetBytes(s);
                            textBytes.AddRange(raw);
                            textBytes.Add(0);
                        }
                    }
                }
            }

            int textLen = textBytes.Count;
            int textBase = HEADER_SIZE + fieldCount * 5 + rowCount * 8;
            int estimatedRowBytes = rows.Count * fields.Count * 8 + 1024;
            byte[] outBuf = new byte[textBase + estimatedRowBytes + textLen];

            // === 3. Salin header asal ===
            Array.Copy(header, outBuf, HEADER_SIZE);

            BitConverter.GetBytes(fieldCount).CopyTo(outBuf, 0x14);
            BitConverter.GetBytes(rowCount).CopyTo(outBuf, 0x18);
            BitConverter.GetBytes(textLen).CopyTo(outBuf, 0x1C);

            // === 4. Field types ===
            for (int i = 0; i < fieldCount; i++)
            {
                int fieldOffset = HEADER_SIZE + i * 5;
                outBuf[fieldOffset] = fields[i].Type;
            }

            // === 5. Bina pointer table dan tulis row data ===
            int ptrTableOffset = HEADER_SIZE + fieldCount * 5;
            int rowDataBase = ptrTableOffset + rowCount * 8;
            int rowPtr = rowDataBase;

            for (int i = 0; i < rowCount; i++)
            {
                int ptrPos = ptrTableOffset + i * 8;

                // **Ambil ID dari kolum pertama row, default 0**
                int rowId = 0;
                if (!(rows[i].All(x => x == null || x.ToString() == "")))
                {
                    object idVal = rows[i][0];
                    if (idVal != null)
                    {
                        // safe conversion for numeric & string types
                        if (idVal is int) rowId = (int)idVal;
                        else int.TryParse(idVal.ToString(), out rowId);
                    }
                }
                BitConverter.GetBytes(rowId).CopyTo(outBuf, ptrPos); // +0: ID

                // --- Check if this row is "empty" (semua kosong/null) ---
                bool isEmpty = rows[i].All(x => x == null || x.ToString() == "");
                if (isEmpty)
                {
                    BitConverter.GetBytes(0).CopyTo(outBuf, ptrPos + 4); // +4: offset = 0 utk kosong
                    continue;
                }

                BitConverter.GetBytes(rowPtr).CopyTo(outBuf, ptrPos + 4); // +4: offset ke row data

                // === Tulis row data, ikut format asal ===
                for (int f = 0; f < fieldCount; f++)
                {
                    var field = fields[f];
                    object val = rows[i][f];
                    switch (field.Type)
                    {
                        case 1:
                            outBuf[rowPtr++] = Convert.ToByte(val ?? 0);
                            break;
                        case 2:
                            BitConverter.GetBytes(Convert.ToInt16(val ?? 0)).CopyTo(outBuf, rowPtr);
                            rowPtr += 2;
                            break;
                        case 3:
                            BitConverter.GetBytes(Convert.ToUInt16(val ?? 0)).CopyTo(outBuf, rowPtr);
                            rowPtr += 2;
                            break;
                        case 4:
                            BitConverter.GetBytes(Convert.ToInt32(val ?? 0)).CopyTo(outBuf, rowPtr);
                            rowPtr += 4;
                            break;
                        case 5:
                            BitConverter.GetBytes(Convert.ToUInt32(val ?? 0)).CopyTo(outBuf, rowPtr);
                            rowPtr += 4;
                            break;
                        case 6:
                            BitConverter.GetBytes(Convert.ToSingle(val ?? 0)).CopyTo(outBuf, rowPtr);
                            rowPtr += 4;
                            break;
                        case 7:
                            BitConverter.GetBytes(Convert.ToDouble(val ?? 0)).CopyTo(outBuf, rowPtr);
                            rowPtr += 8;
                            break;
                        case 8:
                            BitConverter.GetBytes(Convert.ToInt64(val ?? 0)).CopyTo(outBuf, rowPtr);
                            rowPtr += 8;
                            break;
                        case 9:
                            BitConverter.GetBytes(Convert.ToUInt64(val ?? 0)).CopyTo(outBuf, rowPtr);
                            rowPtr += 8;
                            break;
                        case 10:
                            string s = val?.ToString() ?? "";
                            int strPtr = stringPointerDict.ContainsKey(s) ? stringPointerDict[s] : 0;
                            BitConverter.GetBytes(strPtr).CopyTo(outBuf, rowPtr);
                            rowPtr += 4;
                            break;
                        default:
                            // fallback
                            BitConverter.GetBytes(0).CopyTo(outBuf, rowPtr);
                            rowPtr += 4;
                            break;
                    }
                }
            }

            // === 6. Sambung text pool ===
            Array.Copy(textBytes.ToArray(), 0, outBuf, rowPtr, textBytes.Count);
            int realTotalLen = rowPtr + textBytes.Count;

            // === 7. Simpan file, potong betul² size ===
            File.WriteAllBytes(path, outBuf.Take(realTotalLen).ToArray());
        }











    }
}
