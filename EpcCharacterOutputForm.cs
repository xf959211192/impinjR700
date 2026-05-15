using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ImpinjR700
{
    public sealed class EpcCharacterOutputForm : Form
    {
        private readonly Func<IReadOnlyList<string>> _knownEpcProvider;
        private readonly Action<EpcCharacterOutputSettings> _settingsChanged;
        private readonly Action _clearOutput;
        private readonly BindingList<CharacterBindingRow> _bindingRows = new();
        private readonly DataGridView _gridBindings = new();
        private readonly ListBox _listKnownEpcs = new();
        private readonly ComboBox _comboMode = new();
        private readonly NumericUpDown _numericDebounce = new();
        private readonly TextBox _textOutput = new();
        private readonly Button _buttonBindSelected = new();
        private readonly Button _buttonClearBinding = new();
        private readonly Button _buttonAddCharacter = new();
        private readonly Button _buttonDeleteCharacter = new();
        private readonly Button _buttonSave = new();
        private readonly Button _buttonRefreshEpcs = new();
        private readonly Button _buttonClearOutput = new();
        private readonly Button _buttonShowDisplay = new();
        private readonly TableLayoutPanel _keyboardPanel = new();
        private readonly Dictionary<string, Button> _keyboardButtons = new(StringComparer.Ordinal);
        private EpcCharacterOutputDisplayForm? _displayForm;
        private string? _selectedCharacterValue;
        private bool _isLoading;

        public EpcCharacterOutputForm(
            EpcCharacterOutputSettings settings,
            Func<IReadOnlyList<string>> knownEpcProvider,
            Action<EpcCharacterOutputSettings> settingsChanged,
            Action clearOutput)
        {
            _knownEpcProvider = knownEpcProvider;
            _settingsChanged = settingsChanged;
            _clearOutput = clearOutput;

            Text = "EPC 字符输出";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(820, 560);
            Size = new Size(940, 660);
            Font = new Font("Microsoft YaHei", 9F, FontStyle.Regular, GraphicsUnit.Point);

            BuildLayout();
            LoadSettings(settings);
            RefreshKnownEpcs();
        }

        public void SetOutputText(string output)
        {
            if (_textOutput.IsDisposed)
            {
                return;
            }

            _textOutput.Text = output;
            _textOutput.SelectionStart = _textOutput.TextLength;
            _textOutput.ScrollToCaret();
            _displayForm?.SetOutputText(output);
        }

        public void RefreshKnownEpcs()
        {
            if (_listKnownEpcs.IsDisposed)
            {
                return;
            }

            var selected = (_listKnownEpcs.SelectedItem as KnownEpcListItem)?.Epc;
            var characterByEpc = _bindingRows
                .Where(row => !string.IsNullOrWhiteSpace(row.Epc))
                .GroupBy(row => EpcCharacterOutputEngine.NormalizeEpc(row.Epc), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().DisplayName, StringComparer.Ordinal);

            _listKnownEpcs.BeginUpdate();
            try
            {
                _listKnownEpcs.Items.Clear();
                foreach (var epc in _knownEpcProvider().OrderBy(item => item, StringComparer.Ordinal))
                {
                    characterByEpc.TryGetValue(epc, out var displayName);
                    _listKnownEpcs.Items.Add(new KnownEpcListItem(epc, displayName));
                }

                if (!string.IsNullOrEmpty(selected))
                {
                    foreach (var item in _listKnownEpcs.Items.OfType<KnownEpcListItem>())
                    {
                        if (string.Equals(item.Epc, selected, StringComparison.Ordinal))
                        {
                            _listKnownEpcs.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
            finally
            {
                _listKnownEpcs.EndUpdate();
            }
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(12)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
            Controls.Add(root);

            var options = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = true,
                Padding = new Padding(0, 0, 0, 8)
            };

            options.Controls.Add(new Label { Text = "输出模式：", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            _comboMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _comboMode.Width = 140;
            _comboMode.Items.Add("单个输出");
            _comboMode.Items.Add("连续输出");
            _comboMode.SelectedIndexChanged += (_, _) => SaveSettingsFromUi();
            options.Controls.Add(_comboMode);

            options.Controls.Add(new Label { Text = "冷却时间（s）：", AutoSize = true, Margin = new Padding(16, 6, 4, 0) });
            _numericDebounce.DecimalPlaces = 2;
            _numericDebounce.Minimum = 0;
            _numericDebounce.Maximum = 60;
            _numericDebounce.Increment = 0.1M;
            _numericDebounce.Width = 80;
            _numericDebounce.ValueChanged += (_, _) => SaveSettingsFromUi();
            options.Controls.Add(_numericDebounce);

            _buttonSave.Text = "保存设置";
            _buttonSave.AutoSize = true;
            _buttonSave.Click += (_, _) => SaveSettingsFromUi();
            options.Controls.Add(_buttonSave);

            _buttonAddCharacter.Text = "新增字符";
            _buttonAddCharacter.AutoSize = true;
            _buttonAddCharacter.Click += (_, _) => AddCustomCharacter();
            options.Controls.Add(_buttonAddCharacter);

            _buttonDeleteCharacter.Text = "删除字符";
            _buttonDeleteCharacter.AutoSize = true;
            _buttonDeleteCharacter.Click += (_, _) => DeleteSelectedCustomCharacter();
            options.Controls.Add(_buttonDeleteCharacter);

            root.Controls.Add(options, 0, 0);
            root.SetColumnSpan(options, 2);

            ConfigureKeyboardPanel();
            root.Controls.Add(_keyboardPanel, 0, 1);
            root.SetColumnSpan(_keyboardPanel, 2);

            ConfigureBindingGrid();
            root.Controls.Add(_gridBindings, 0, 2);

            var epcPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                Padding = new Padding(8, 0, 0, 0)
            };
            epcPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            epcPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            epcPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            epcPanel.Controls.Add(new Label { Text = "已读取 EPC", AutoSize = true }, 0, 0);
            _listKnownEpcs.Dock = DockStyle.Fill;
            _listKnownEpcs.DoubleClick += (_, _) => BindSelectedEpcToSelectedCharacter();
            epcPanel.Controls.Add(_listKnownEpcs, 0, 1);

            var epcButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            _buttonBindSelected.Text = "绑定/替换到选中字符";
            _buttonBindSelected.AutoSize = true;
            _buttonBindSelected.Click += (_, _) => BindSelectedEpcToSelectedCharacter();
            epcButtons.Controls.Add(_buttonBindSelected);

            _buttonClearBinding.Text = "清除绑定";
            _buttonClearBinding.AutoSize = true;
            _buttonClearBinding.Click += (_, _) => ClearSelectedBinding();
            epcButtons.Controls.Add(_buttonClearBinding);

            _buttonRefreshEpcs.Text = "刷新 EPC";
            _buttonRefreshEpcs.AutoSize = true;
            _buttonRefreshEpcs.Click += (_, _) => RefreshKnownEpcs();
            epcButtons.Controls.Add(_buttonRefreshEpcs);
            epcPanel.Controls.Add(epcButtons, 0, 2);

            root.Controls.Add(epcPanel, 1, 2);

            var outputPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                Padding = new Padding(0, 8, 0, 0)
            };
            outputPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outputPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var outputHeader = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            outputHeader.Controls.Add(new Label { Text = "当前输出", AutoSize = true, Margin = new Padding(0, 6, 12, 0) });
            _buttonClearOutput.Text = "清空输出";
            _buttonClearOutput.AutoSize = true;
            _buttonClearOutput.Click += (_, _) =>
            {
                _clearOutput();
                SetOutputText(string.Empty);
            };
            outputHeader.Controls.Add(_buttonClearOutput);

            _buttonShowDisplay.Text = "演示窗口";
            _buttonShowDisplay.AutoSize = true;
            _buttonShowDisplay.Click += (_, _) => ShowDisplayForm();
            outputHeader.Controls.Add(_buttonShowDisplay);
            outputPanel.Controls.Add(outputHeader, 0, 0);

            _textOutput.Dock = DockStyle.Fill;
            _textOutput.Multiline = true;
            _textOutput.ReadOnly = true;
            _textOutput.ScrollBars = ScrollBars.Vertical;
            outputPanel.Controls.Add(_textOutput, 0, 1);

            root.Controls.Add(outputPanel, 0, 3);
            root.SetColumnSpan(outputPanel, 2);
        }

        private void ConfigureKeyboardPanel()
        {
            _keyboardPanel.Dock = DockStyle.Fill;
            _keyboardPanel.AutoSize = true;
            _keyboardPanel.ColumnCount = 1;
            _keyboardPanel.RowCount = 5;
            _keyboardPanel.Padding = new Padding(0, 0, 0, 8);

            AddKeyboardRow(new[]
            {
                Key(EpcCharacterOutputSettings.EscapeActionValue, 1.1F),
                Key("1"), Key("2"), Key("3"), Key("4"), Key("5"),
                Key("6"), Key("7"), Key("8"), Key("9"), Key("0"),
                Key("-"), Key("="), Key(EpcCharacterOutputSettings.DeleteActionValue, 1.6F)
            });
            AddKeyboardRow(new[]
            {
                Key(EpcCharacterOutputSettings.TabActionValue, 1.45F),
                Key("Q"), Key("W"), Key("E"), Key("R"), Key("T"),
                Key("Y"), Key("U"), Key("I"), Key("O"), Key("P"),
                Key("["), Key("]")
            });
            AddKeyboardRow(new[]
            {
                Key(EpcCharacterOutputSettings.CapsActionValue, 1.75F),
                Key("A"), Key("S"), Key("D"), Key("F"), Key("G"),
                Key("H"), Key("J"), Key("K"), Key("L"),
                Key(";"), Key("'"), Key(EpcCharacterOutputSettings.EnterActionValue, 1.75F)
            });
            AddKeyboardRow(new[]
            {
                Key(EpcCharacterOutputSettings.ShiftActionValue, 2.1F),
                Key("Z"), Key("X"), Key("C"), Key("V"), Key("B"),
                Key("N"), Key("M"), Key(","), Key("."), Key("/"),
                Key(EpcCharacterOutputSettings.ShiftActionValue, 2.1F)
            });
            AddKeyboardRow(new[]
            {
                Key(" ", 7F)
            });
        }

        private static KeySpec Key(string value, float widthUnits = 1F)
        {
            return new KeySpec(value, widthUnits);
        }

        private void AddKeyboardRow(IReadOnlyList<KeySpec> keys)
        {
            var rowPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 2, 0, 2)
            };

            foreach (var key in keys)
            {
                var character = EpcCharacterOutputSettings.AllowedCharacters.First(item => string.Equals(item.Value, key.Value, StringComparison.Ordinal));
                var button = new Button
                {
                    Text = character.DisplayName,
                    Tag = character.Value,
                    Width = Math.Max(48, (int)(48 * key.WidthUnits)),
                    Height = 34,
                    Margin = new Padding(3),
                    UseVisualStyleBackColor = true
                };
                button.Click += KeyboardButton_Click;
                rowPanel.Controls.Add(button);

                if (!_keyboardButtons.ContainsKey(character.Value))
                {
                    _keyboardButtons[character.Value] = button;
                }
            }

            _keyboardPanel.Controls.Add(rowPanel);
        }

        private void ConfigureBindingGrid()
        {
            _gridBindings.Dock = DockStyle.Fill;
            _gridBindings.AutoGenerateColumns = false;
            _gridBindings.AllowUserToAddRows = false;
            _gridBindings.AllowUserToDeleteRows = false;
            _gridBindings.RowHeadersVisible = false;
            _gridBindings.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _gridBindings.MultiSelect = false;
            _gridBindings.EditMode = DataGridViewEditMode.EditOnEnter;
            _gridBindings.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _gridBindings.DataSource = _bindingRows;
            _gridBindings.CellBeginEdit += GridBindings_CellBeginEdit;
            _gridBindings.CellEndEdit += (_, e) => SaveSettingsFromUi(GetBindingRow(e.RowIndex));

            _gridBindings.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "字符",
                DataPropertyName = nameof(CharacterBindingRow.DisplayName),
                FillWeight = 24
            });
            _gridBindings.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "绑定 EPC",
                DataPropertyName = nameof(CharacterBindingRow.Epc),
                FillWeight = 76
            });
            _gridBindings.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "类型",
                DataPropertyName = nameof(CharacterBindingRow.KindDisplay),
                ReadOnly = true,
                FillWeight = 20
            });
        }

        private void LoadSettings(EpcCharacterOutputSettings settings)
        {
            _isLoading = true;
            try
            {
                _comboMode.SelectedIndex = settings.Mode == EpcCharacterOutputMode.Continuous ? 1 : 0;
                _numericDebounce.Value = Math.Min(_numericDebounce.Maximum, Math.Max(_numericDebounce.Minimum, (decimal)settings.DebounceSeconds));
                _bindingRows.Clear();

                foreach (var character in settings.GetAvailableCharacters())
                {
                    settings.BindingsByCharacter.TryGetValue(character.Value, out var epc);
                    _bindingRows.Add(new CharacterBindingRow(character.Value, character.DisplayName, character.IsBuiltIn, epc ?? string.Empty));
                }

                RefreshKeyboardButtons();
            }
            finally
            {
                _isLoading = false;
            }
        }

        private void BindSelectedEpcToSelectedCharacter()
        {
            if (_gridBindings.CurrentRow?.DataBoundItem is not CharacterBindingRow row ||
                _listKnownEpcs.SelectedItem is not KnownEpcListItem epcItem)
            {
                MessageBox.Show(this, "请选择一个字符行和一个已读取 EPC。", "绑定提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            row.Epc = epcItem.Epc;
            _gridBindings.Refresh();
            SaveSettingsFromUi(row);
        }

        private void KeyboardButton_Click(object? sender, EventArgs e)
        {
            if (sender is not Button { Tag: string characterValue })
            {
                return;
            }

            SelectCharacter(characterValue);
            if (_listKnownEpcs.SelectedItem is KnownEpcListItem)
            {
                BindSelectedEpcToSelectedCharacter();
            }
        }

        private void SelectCharacter(string characterValue)
        {
            _selectedCharacterValue = characterValue;
            foreach (DataGridViewRow row in _gridBindings.Rows)
            {
                if (row.DataBoundItem is CharacterBindingRow bindingRow &&
                    string.Equals(bindingRow.Value, characterValue, StringComparison.Ordinal))
                {
                    _gridBindings.ClearSelection();
                    row.Selected = true;
                    _gridBindings.CurrentCell = row.Cells[1];
                    break;
                }
            }

            RefreshKeyboardButtons();
        }

        private void ClearSelectedBinding()
        {
            if (_gridBindings.CurrentRow?.DataBoundItem is not CharacterBindingRow row)
            {
                return;
            }

            row.Epc = string.Empty;
            _gridBindings.Refresh();
            SaveSettingsFromUi(row);
        }

        private void AddCustomCharacter()
        {
            var value = CreateDefaultCustomCharacterValue();
            _bindingRows.Add(new CharacterBindingRow(value, value, isBuiltIn: false, epc: string.Empty));
            _gridBindings.ClearSelection();
            var rowIndex = _bindingRows.Count - 1;
            _gridBindings.Rows[rowIndex].Selected = true;
            _gridBindings.CurrentCell = _gridBindings.Rows[rowIndex].Cells[0];
            SaveSettingsFromUi();
            _gridBindings.BeginEdit(true);
        }

        private void DeleteSelectedCustomCharacter()
        {
            if (_gridBindings.CurrentRow?.DataBoundItem is not CharacterBindingRow row)
            {
                return;
            }

            if (row.IsBuiltIn)
            {
                MessageBox.Show(this, "内置字符不能删除，只能清除 EPC 绑定。", "删除提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _bindingRows.Remove(row);
            SaveSettingsFromUi();
        }

        private void GridBindings_CellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0)
            {
                return;
            }

            if (_gridBindings.Rows[e.RowIndex].DataBoundItem is CharacterBindingRow { IsBuiltIn: true })
            {
                e.Cancel = true;
            }
        }

        private CharacterBindingRow? GetBindingRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _gridBindings.Rows.Count)
            {
                return null;
            }

            return _gridBindings.Rows[rowIndex].DataBoundItem as CharacterBindingRow;
        }

        private void SaveSettingsFromUi(CharacterBindingRow? preferredRow = null)
        {
            if (_isLoading)
            {
                return;
            }

            var settings = new EpcCharacterOutputSettings
            {
                Mode = _comboMode.SelectedIndex == 1 ? EpcCharacterOutputMode.Continuous : EpcCharacterOutputMode.Single,
                DebounceSeconds = (double)_numericDebounce.Value,
                BindingsByCharacter = new Dictionary<string, string>(StringComparer.Ordinal)
            };

            NormalizeCustomRows(settings);
            if (preferredRow != null)
            {
                ClearDuplicateEpcBindings(preferredRow);
            }

            var epcToLastCharacter = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in _bindingRows)
            {
                var epc = EpcCharacterOutputEngine.NormalizeEpc(row.Epc);
                if (!string.IsNullOrEmpty(epc))
                {
                    epcToLastCharacter[epc] = row.Value;
                }
            }

            foreach (var row in _bindingRows)
            {
                var epc = EpcCharacterOutputEngine.NormalizeEpc(row.Epc);
                if (string.IsNullOrEmpty(epc) || !string.Equals(epcToLastCharacter[epc], row.Value, StringComparison.Ordinal))
                {
                    row.Epc = string.Empty;
                    continue;
                }

                row.Epc = epc;
                settings.BindingsByCharacter[row.Value] = epc;
            }

            _gridBindings.Refresh();
            RefreshKnownEpcs();
            RefreshKeyboardButtons();
            _settingsChanged(settings);
        }

        private void RefreshKeyboardButtons()
        {
            var epcByCharacter = _bindingRows.ToDictionary(row => row.Value, row => EpcCharacterOutputEngine.NormalizeEpc(row.Epc), StringComparer.Ordinal);
            foreach (var pair in _keyboardButtons)
            {
                var button = pair.Value;
                var displayName = EpcCharacterOutputSettings.GetCharacterDisplayName(pair.Key);
                epcByCharacter.TryGetValue(pair.Key, out var epc);
                var isBound = !string.IsNullOrEmpty(epc);
                var isSelected = string.Equals(_selectedCharacterValue, pair.Key, StringComparison.Ordinal);
                button.Text = isBound ? $"{displayName} ✓" : displayName;
                button.BackColor = isSelected
                    ? Color.LightSkyBlue
                    : isBound
                        ? Color.Honeydew
                        : SystemColors.Control;
                button.UseVisualStyleBackColor = false;
                button.FlatStyle = isSelected || isBound ? FlatStyle.Flat : FlatStyle.Standard;
                button.AccessibleDescription = isBound ? $"已绑定 EPC：{epc}" : "未绑定 EPC";
            }
        }

        private void ClearDuplicateEpcBindings(CharacterBindingRow preferredRow)
        {
            var preferredEpc = EpcCharacterOutputEngine.NormalizeEpc(preferredRow.Epc);
            if (string.IsNullOrEmpty(preferredEpc))
            {
                return;
            }

            foreach (var row in _bindingRows)
            {
                if (ReferenceEquals(row, preferredRow))
                {
                    continue;
                }

                var epc = EpcCharacterOutputEngine.NormalizeEpc(row.Epc);
                if (string.Equals(epc, preferredEpc, StringComparison.Ordinal))
                {
                    row.Epc = string.Empty;
                }
            }
        }

        private void NormalizeCustomRows(EpcCharacterOutputSettings settings)
        {
            var usedValues = new HashSet<string>(
                EpcCharacterOutputSettings.AllowedCharacters.Select(character => character.Value),
                StringComparer.Ordinal);
            var rowsToRemove = new List<CharacterBindingRow>();
            var customIndex = 1;

            foreach (var row in _bindingRows)
            {
                if (row.IsBuiltIn)
                {
                    continue;
                }

                var value = row.DisplayName.Trim();
                if (string.IsNullOrEmpty(value) ||
                    EpcCharacterOutputSettings.AllowedCharacters.Any(character =>
                        string.Equals(character.Value, value, StringComparison.Ordinal) ||
                        string.Equals(character.DisplayName, value, StringComparison.Ordinal)))
                {
                    value = CreateDefaultCustomCharacterValue(customIndex++);
                }

                while (!usedValues.Add(value))
                {
                    value = CreateDefaultCustomCharacterValue(customIndex++);
                }

                row.Value = value;
                row.DisplayName = value;
                settings.CustomCharacters.Add(value);

                if (string.IsNullOrWhiteSpace(row.DisplayName))
                {
                    rowsToRemove.Add(row);
                }
            }

            foreach (var row in rowsToRemove)
            {
                _bindingRows.Remove(row);
            }
        }

        private string CreateDefaultCustomCharacterValue(int startIndex = 1)
        {
            var usedValues = _bindingRows.Select(row => row.Value).ToHashSet(StringComparer.Ordinal);
            var index = Math.Max(1, startIndex);
            while (true)
            {
                var candidate = $"自定义{index}";
                if (!usedValues.Contains(candidate))
                {
                    return candidate;
                }

                index++;
            }
        }

        private void ShowDisplayForm()
        {
            if (_displayForm is { IsDisposed: false })
            {
                _displayForm.SetOutputText(_textOutput.Text);
                _displayForm.Activate();
                return;
            }

            _displayForm = new EpcCharacterOutputDisplayForm();
            _displayForm.FormClosed += (_, _) => _displayForm = null;
            _displayForm.SetOutputText(_textOutput.Text);
            _displayForm.Show(this);
        }

        private readonly record struct KeySpec(string Value, float WidthUnits);

        private sealed class KnownEpcListItem
        {
            public KnownEpcListItem(string epc, string? boundCharacterDisplayName)
            {
                Epc = epc;
                BoundCharacterDisplayName = boundCharacterDisplayName;
            }

            public string Epc { get; }
            public string? BoundCharacterDisplayName { get; }

            public override string ToString()
            {
                return string.IsNullOrEmpty(BoundCharacterDisplayName)
                    ? $"{Epc}（未绑定）"
                    : $"{Epc}（已绑定：{BoundCharacterDisplayName}）";
            }
        }

        private sealed class CharacterBindingRow : INotifyPropertyChanged
        {
            private string _value;
            private string _displayName;
            private string _epc;

            public CharacterBindingRow(string value, string displayName, bool isBuiltIn, string epc)
            {
                _value = value;
                _displayName = displayName;
                IsBuiltIn = isBuiltIn;
                _epc = epc;
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            public bool IsBuiltIn { get; }
            public string KindDisplay => IsBuiltIn ? "内置" : "自定义";

            public string Value
            {
                get => _value;
                set
                {
                    if (string.Equals(_value, value, StringComparison.Ordinal))
                    {
                        return;
                    }

                    _value = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
                }
            }

            public string DisplayName
            {
                get => _displayName;
                set
                {
                    if (string.Equals(_displayName, value, StringComparison.Ordinal))
                    {
                        return;
                    }

                    _displayName = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
                }
            }

            public string Epc
            {
                get => _epc;
                set
                {
                    if (string.Equals(_epc, value, StringComparison.Ordinal))
                    {
                        return;
                    }

                    _epc = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Epc)));
                }
            }
        }
    }
}
