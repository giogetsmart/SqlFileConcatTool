using System.Text;

namespace SqlFileConcatTool
{
    public class MainForm : Form
    {
        private readonly ListBox _list;
        private readonly Button _btnAdd;
        private readonly Button _btnRemove;
        private readonly Button _btnClear;
        private readonly Button _btnUp;
        private readonly Button _btnDown;
        private readonly Button _btnSave;
        private readonly CheckBox _cbDeployScriptStartCommands;
        private readonly CheckBox _cbGoBetween;
        private readonly CheckBox _cbFinalGo;
        private readonly CheckBox _cbComments;

        // We keep our own list to preserve the *append order* as the user adds files across multiple opens.
        private readonly List<string> _orderedPaths = new List<string>(capacity: 64);
        private readonly HashSet<string> _dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public MainForm()
        {
            Text = "SQL File Concatenator";
            Width = 920;
            Height = 520;
            MinimizeBox = true;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;

            _list = new ListBox
            {
                Dock = DockStyle.Fill,
                HorizontalScrollbar = true
            };

            var right = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.TopDown,
                Width = 320,
                Padding = new Padding(10),
                AutoScroll = true
            };

            _btnAdd = new Button { Text = "➕ Choose files", AutoSize = true };
            _btnRemove = new Button { Text = "✖ Remove selected", AutoSize = true };
            _btnClear = new Button { Text = "Clear selection", AutoSize = true };
            _btnUp = new Button { Text = "↑ Move up", AutoSize = true };
            _btnDown = new Button { Text = "↓ Move down", AutoSize = true };
            _btnSave = new Button { Text = "💾 Save concatenated file", AutoSize = true };

            _cbDeployScriptStartCommands = new CheckBox { Text = "Turn on ANSI_NULLS and QUOTED_IDENTIFIER", Checked = true, AutoSize = true };
            _cbGoBetween = new CheckBox { Text = "Add GO between selected files", Checked = true, AutoSize = true }; 
            _cbFinalGo = new CheckBox { Text = "Add GO at end of concatenated file", Checked = true, AutoSize = true };
            _cbComments = new CheckBox { Text = "Add informational comments", Checked = false, AutoSize = true }; // off by default

            right.Controls.Add(_btnAdd);
            right.Controls.Add(_btnRemove);
            right.Controls.Add(_btnClear);
            right.Controls.Add(_btnUp);
            right.Controls.Add(_btnDown);
            right.Controls.Add(new Label { Text = "Options", AutoSize = true, Padding = new Padding(0, 12, 0, 0) });
            right.Controls.Add(_cbDeployScriptStartCommands);
            right.Controls.Add(_cbGoBetween);
            right.Controls.Add(_cbFinalGo);
            right.Controls.Add(_cbComments);
            right.Controls.Add(new Label { Text = "", AutoSize = true, Height = 8 });
            right.Controls.Add(_btnSave);

            Controls.Add(_list);
            Controls.Add(right);

            _btnAdd.Click += (s, e) => AddFiles();
            _btnRemove.Click += (s, e) => RemoveSelected();
            _btnClear.Click += (s, e) => ClearAll();
            _btnUp.Click += (s, e) => MoveSelected(-1);
            _btnDown.Click += (s, e) => MoveSelected(1);
            _btnSave.Click += (s, e) => SaveConcatenated();
        }

        private void AddFiles()
        {
            using var ofd = new OpenFileDialog
            {
                Filter = "SQL files (*.sql)|*.sql|All files (*.*)|*.*",
                Multiselect = true,
                Title = "Choose one or more .sql files"
            };

            if (ofd.ShowDialog(this) != DialogResult.OK)
                return;

            foreach (var path in ofd.FileNames)
            {
                if (!File.Exists(path)) continue;
                if (!_dedupe.Add(path)) continue; // skip duplicates
                _orderedPaths.Add(path);
            }

            RefreshList();
        }

        private void RemoveSelected()
        {
            var selected = _list.SelectedItems.Cast<string>().ToList();
            if (selected.Count == 0) return;

            foreach (var s in selected)
            {
                _dedupe.Remove(s);
                _orderedPaths.Remove(s);
            }

            RefreshList();
        }

        private void ClearAll()
        {
            _dedupe.Clear();
            _orderedPaths.Clear();
            RefreshList();
        }

        private void MoveSelected(int delta)
        {
            var idx = _list.SelectedIndex;
            if (idx < 0) return;

            var newIdx = idx + delta;
            if (newIdx < 0 || newIdx >= _orderedPaths.Count) return;

            var item = _orderedPaths[idx];
            _orderedPaths.RemoveAt(idx);
            _orderedPaths.Insert(newIdx, item);

            RefreshList();
            _list.SelectedIndex = newIdx;
        }

        private void RefreshList()
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var p in _orderedPaths)
                _list.Items.Add(p);
            _list.EndUpdate();
        }

        private void SaveConcatenated()
        {
            if (_orderedPaths.Count == 0)
            {
                MessageBox.Show("No files selected.", "Nothing to concatenate.",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var sfd = new SaveFileDialog
            {
                Filter = "SQL files (*.sql)|*.sql|All files (*.*)|*.*",
                FileName = $"{DateOnly.FromDateTime(DateTime.Now):yyyyMMdd}_concatenated.sql",
                OverwritePrompt = true,
                Title = "Save concatenated script"
            };

            if (sfd.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                // UTF-8 w/ BOM works well with SSMS
                using var sw = new StreamWriter(sfd.FileName, append: false,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

                if (_cbComments.Checked)
                {
                    var now = DateTime.Now;
                    sw.WriteLine($"-- Concatenated on {now:yyyy-MM-dd HH:mm:ss}");
                    sw.WriteLine($"-- Files: {_orderedPaths.Count}");
                    sw.WriteLine();
                }

                if (_cbDeployScriptStartCommands.Checked)
                {
                    sw.WriteLine("SET ANSI_NULLS ON");
                    sw.WriteLine("GO");
                    sw.WriteLine();
                    sw.WriteLine("SET QUOTED_IDENTIFIER ON");
                    sw.WriteLine("GO");
                    sw.WriteLine();
                }

                for (int i = 0; i < _orderedPaths.Count; i++)
                {
                    var path = _orderedPaths[i];
                    var name = Path.GetFileName(path);

                    if (_cbComments.Checked)
                    {
                        sw.WriteLine($"-- BEGIN FILE: {name}");
                        sw.WriteLine($"-- PATH: {path}");
                        sw.WriteLine($"-- INDEX: {i + 1}/{_orderedPaths.Count}");
                    }

                    using var sr = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
                    var content = sr.ReadToEnd();

                    // Normalise to CRLF for SSMS friendliness
                    content = content.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
                    if (!content.EndsWith("\r\n"))
                        content += "\r\n";

                    sw.Write(content);

                    if (_cbComments.Checked)
                        sw.WriteLine($"-- END FILE: {name}");

                    if (_cbGoBetween.Checked && i < _orderedPaths.Count - 1)
                    {
                        sw.WriteLine("GO");
                    }

                    sw.WriteLine();
                }

                if (_cbFinalGo.Checked)
                {
                    sw.WriteLine("GO");
                }

                sw.WriteLine();

                sw.Flush();
                MessageBox.Show($"Concatenated script saved:\r\n{sfd.FileName}", "Done",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error while saving: " + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}