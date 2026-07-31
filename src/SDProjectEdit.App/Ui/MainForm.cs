using System.ComponentModel;
using SDProjectEdit.Core.Analysis;
using SDProjectEdit.Core.Io;
using SDProjectEdit.Core.Model;
using SDProjectEdit.Core.Planning;
using SDProjectEdit.Core.Reporting;

namespace SDProjectEdit.App.Ui;

internal sealed class MainForm : Form
{
    private const string DecisionLeave = "Dejar por-proyecto";
    private const string DecisionUnify = "Unificar en Common.props";
    private const string DecisionKeep = "Unificar, conservar overrides";

    private readonly TextBox _pathBox = new() { Dock = DockStyle.Fill };
    private readonly Button _browseButton = new() { Text = "Examinar…", Width = 100, Dock = DockStyle.Fill };
    private readonly Button _analyzeButton = new() { Text = "Analizar", Width = 100, Dock = DockStyle.Fill };

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _grid = new();
    private readonly DataGridView _detailGrid = new();
    private readonly DataGridView _commonGrid = new();
    private readonly TextBox _surveyText = CreateMonospaceBox();
    private readonly TextBox _planText = CreateMonospaceBox();
    private readonly TextBox _verifyText = CreateMonospaceBox();

    private readonly CheckBox _showAll = new() { Text = "Mostrar también las propiedades por-proyecto", AutoSize = true };
    private readonly ComboBox _importPlacement = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly CheckBox _removeEmptyGroups = new() { Text = "Eliminar PropertyGroup vacíos", Checked = true, AutoSize = true };
    private readonly CheckBox _backup = new() { Text = "Hacer backup", Checked = true, AutoSize = true };

    private readonly Button _planButton = new() { Text = "Ver plan", Width = 110, Enabled = false };
    private readonly Button _applyButton = new() { Text = "Aplicar…", Width = 110, Enabled = false };
    private readonly Button _verifyButton = new() { Text = "Verificar", Width = 110, Enabled = false };
    private readonly Button _saveCommonButton = new() { Text = "Guardar Common.props", Width = 180, Enabled = false };
    private readonly Button _addCommonButton = new() { Text = "Agregar propiedad", Width = 150, Enabled = false };

    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

    private PropertyMatrix? _matrix;
    private CommonPropsFile? _commonProps;

    public MainForm(string? initialPath)
    {
        Text = "SDProjectEdit — Editor de proyectos multi-DLL de Clarion";
        Width = 1180;
        Height = 780;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 600);

        _importPlacement.Items.AddRange([
            "Import después del PropertyGroup general (recomendado)",
            "Import justo después de <Project>",
        ]);
        _importPlacement.SelectedIndex = 0;

        BuildLayout();
        WireEvents();

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            _pathBox.Text = initialPath;
            // Analizar recién cuando la ventana ya existe: en el constructor todavía no hay handle.
            Shown += (_, _) => Analyze();
        }
        else
        {
            SetStatus("Elegí la carpeta del solution y tocá Analizar.");
        }
    }

    // ---- layout --------------------------------------------------------------------------

    private static TextBox CreateMonospaceBox() => new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font("Consolas", 9f),
        BackColor = SystemColors.Window,
    };

    private void BuildLayout()
    {
        var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 36, ColumnCount = 4, Padding = new Padding(6, 6, 6, 0) };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        top.Controls.Add(new Label { Text = "Solution / carpeta:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        top.Controls.Add(_pathBox, 1, 0);
        top.Controls.Add(_browseButton, 2, 0);
        top.Controls.Add(_analyzeButton, 3, 0);

        _tabs.TabPages.Add(BuildPropertiesTab());
        _tabs.TabPages.Add(BuildTextTab("Relevamiento", _surveyText));
        _tabs.TabPages.Add(BuildTextTab("Plan", _planText));
        _tabs.TabPages.Add(BuildCommonPropsTab());
        _tabs.TabPages.Add(BuildTextTab("Verificación", _verifyText));

        var optionsBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(6, 8, 6, 0) };
        optionsBar.Controls.AddRange([_importPlacement, _removeEmptyGroups, _backup]);

        var buttonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(6),
        };
        buttonBar.Controls.AddRange([_applyButton, _planButton, _verifyButton]);

        _status.Items.Add(_statusLabel);
        _status.Dock = DockStyle.Bottom;

        Controls.Add(_tabs);
        Controls.Add(buttonBar);
        Controls.Add(optionsBar);
        Controls.Add(top);
        Controls.Add(_status);
    }

    private TabPage BuildPropertiesTab()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;

        _grid.Columns.AddRange([
            new DataGridViewTextBoxColumn { Name = "scope", HeaderText = "Ámbito", ReadOnly = true, FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "prop", HeaderText = "Propiedad", ReadOnly = true, FillWeight = 110 },
            new DataGridViewTextBoxColumn { Name = "status", HeaderText = "Estado", ReadOnly = true, FillWeight = 90 },
            new DataGridViewTextBoxColumn { Name = "present", HeaderText = "Presente", ReadOnly = true, FillWeight = 55 },
            new DataGridViewComboBoxColumn { Name = "decision", HeaderText = "Decisión", FillWeight = 150,
                Items = { DecisionLeave, DecisionUnify, DecisionKeep }, FlatStyle = FlatStyle.Flat },
            new DataGridViewTextBoxColumn { Name = "value", HeaderText = "Valor en Common.props", FillWeight = 130 },
        ]);

        _detailGrid.Dock = DockStyle.Fill;
        _detailGrid.ReadOnly = true;
        _detailGrid.AllowUserToAddRows = false;
        _detailGrid.RowHeadersVisible = false;
        _detailGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _detailGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _detailGrid.Columns.AddRange([
            new DataGridViewTextBoxColumn { Name = "project", HeaderText = "Proyecto", FillWeight = 100 },
            new DataGridViewTextBoxColumn { Name = "current", HeaderText = "Valor actual", FillWeight = 120 },
            new DataGridViewTextBoxColumn { Name = "after", HeaderText = "Quedaría", FillWeight = 120 },
        ]);

        // SplitterDistance sólo se puede fijar cuando el control ya tiene tamaño real; hacerlo en
        // el inicializador tira InvalidOperationException contra el alto por defecto del control.
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.HandleCreated += (_, _) =>
        {
            var target = Math.Min(380, Math.Max(split.Panel1MinSize, split.Height - split.Panel2MinSize - split.SplitterWidth));
            if (target > 0) split.SplitterDistance = target;
        };
        split.Panel1.Controls.Add(_grid);

        var detailHeader = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "Detalle por proyecto",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold),
        };
        split.Panel2.Controls.Add(_detailGrid);
        split.Panel2.Controls.Add(detailHeader);

        var page = new TabPage("Propiedades");
        page.Controls.Add(split);
        page.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 26, Controls = { _showAll } });
        return page;
    }

    private static TabPage BuildTextTab(string title, Control content)
    {
        var page = new TabPage(title);
        page.Controls.Add(content);
        return page;
    }

    private TabPage BuildCommonPropsTab()
    {
        _commonGrid.Dock = DockStyle.Fill;
        _commonGrid.AllowUserToAddRows = false;
        _commonGrid.RowHeadersVisible = false;
        _commonGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _commonGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _commonGrid.Columns.AddRange([
            new DataGridViewTextBoxColumn { Name = "scope", HeaderText = "Ámbito", ReadOnly = true, FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "prop", HeaderText = "Propiedad", ReadOnly = true, FillWeight = 90 },
            new DataGridViewTextBoxColumn { Name = "value", HeaderText = "Valor", FillWeight = 90 },
            new DataGridViewTextBoxColumn { Name = "desc", HeaderText = "Qué hace", ReadOnly = true, FillWeight = 200 },
            new DataGridViewTextBoxColumn { Name = "shadow", HeaderText = "Pisada por", ReadOnly = true, FillWeight = 110 },
        ]);
        _commonGrid.AllowUserToDeleteRows = true;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(6) };
        bar.Controls.AddRange([_saveCommonButton, _addCommonButton,
            new Label { Text = "Editá el valor y guardá. Del(supr) elimina la fila.", AutoSize = true, Padding = new Padding(10, 8, 0, 0) }]);

        var page = new TabPage("Common.props");
        page.Controls.Add(_commonGrid);
        page.Controls.Add(bar);
        return page;
    }

    private void WireEvents()
    {
        _browseButton.Click += (_, _) => Browse();
        _analyzeButton.Click += (_, _) => Analyze();
        _planButton.Click += (_, _) => ShowPlan();
        _applyButton.Click += (_, _) => ApplyPlan();
        _verifyButton.Click += (_, _) => RunVerification();
        _showAll.CheckedChanged += (_, _) => PopulateGrid();
        _grid.SelectionChanged += (_, _) => UpdateDetail();
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) => { if (e.RowIndex >= 0) UpdateDetail(); };
        _grid.DataError += (_, e) => e.ThrowException = false;
        _saveCommonButton.Click += (_, _) => SaveCommonProps();
        _addCommonButton.Click += (_, _) => AddCommonProperty();
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedTab?.Text == "Common.props") LoadCommonProps();
        };
    }

    private void SetStatus(string message) => _statusLabel.Text = message;

    // ---- análisis ------------------------------------------------------------------------

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog { Description = "Carpeta raíz del solution Clarion", UseDescriptionForTitle = true };
        if (Directory.Exists(_pathBox.Text)) dialog.SelectedPath = _pathBox.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK) _pathBox.Text = dialog.SelectedPath;
    }

    private MigrationOptions CurrentOptions() => new()
    {
        ImportPlacement = _importPlacement.SelectedIndex == 0
            ? ImportPlacement.AfterFirstPropertyGroup
            : ImportPlacement.AfterProjectElement,
        RemoveEmptyPropertyGroups = _removeEmptyGroups.Checked,
        CreateBackup = _backup.Checked,
    };

    private void Analyze()
    {
        var path = _pathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) || !(Directory.Exists(path) || File.Exists(path)))
        {
            MessageBox.Show(this, "Indicá una carpeta, un .sln o un .cwproj existente.", "Path inválido",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            UseWaitCursor = true;
            var solution = SolutionSet.Load(path);
            if (solution.Projects.Count == 0)
            {
                MessageBox.Show(this, $"No se encontró ningún .cwproj en {solution.RootDirectory}.", "Sin proyectos",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _matrix = PropertyMatrix.Build(solution);
            _surveyText.Text = TextReport.Survey(_matrix).Replace("\n", "\r\n")
                + "\r\n" + TextReport.Divergences(_matrix).Replace("\n", "\r\n");
            _planText.Clear();
            _verifyText.Clear();

            PopulateGrid();
            LoadCommonProps();

            _planButton.Enabled = _applyButton.Enabled = _verifyButton.Enabled = true;

            var divergences = _matrix.Divergences.Count();
            SetStatus($"{solution.Projects.Count} proyectos · {_matrix.Candidates.Count()} propiedades candidatas · " +
                      $"{divergences} divergencia(s) esperando decisión" +
                      (solution.Warnings.Count > 0 ? $" · {solution.Warnings.Count} aviso(s), ver Relevamiento" : ""));
            _tabs.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "No se pudo analizar", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private void PopulateGrid()
    {
        if (_matrix is null) return;

        var previous = CollectDecisions().ToDictionary(d => d.Key, d => d);
        _grid.Rows.Clear();

        foreach (var row in _matrix.Rows)
        {
            if (!_showAll.Checked && row.Status == UnificationStatus.Blocked) continue;

            var index = _grid.Rows.Add(
                row.Key.Scope.Display,
                row.Key.Name,
                row.Status == UnificationStatus.Blocked ? "Por-proyecto (fija)"
                    : !row.SafeToEdit ? "No editable" : row.StatusText,
                $"{row.PresentIn.Count}/{_matrix.Solution.Projects.Count}",
                DecisionLeave,
                row.MajorityValue);

            var gridRow = _grid.Rows[index];
            gridRow.Tag = row;

            var editable = row.IsCandidate;
            gridRow.Cells["decision"].ReadOnly = !editable;
            gridRow.Cells["value"].ReadOnly = !editable;

            if (!editable)
            {
                gridRow.DefaultCellStyle.BackColor = SystemColors.Control;
                gridRow.DefaultCellStyle.ForeColor = SystemColors.GrayText;
                gridRow.Cells["value"].Value = row.DistinctValues.Count == 1 ? row.MajorityValue : $"({row.DistinctValues.Count} valores)";
                gridRow.Cells[2].ToolTipText = row.UnsafeReason ?? "Propiedad inherentemente por-proyecto: nunca se centraliza.";
                continue;
            }

            var decision = previous.TryGetValue(row.Key, out var kept)
                ? kept
                : new PropertyDecision(row.Key, row.Status == UnificationStatus.Uniform ? DecisionKind.Unify : DecisionKind.Leave, row.MajorityValue);

            gridRow.Cells["decision"].Value = decision.Kind switch
            {
                DecisionKind.Unify => DecisionUnify,
                DecisionKind.UnifyKeepOverrides => DecisionKeep,
                _ => DecisionLeave,
            };
            gridRow.Cells["value"].Value = decision.Value;
            gridRow.Cells[1].ToolTipText = row.Info.Description;

            if (row.NeedsDecision) gridRow.DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 225);
        }

        UpdateDetail();
    }

    private void UpdateDetail()
    {
        _detailGrid.Rows.Clear();
        if (_grid.CurrentRow?.Tag is not PropertyRow row || _matrix is null) return;

        var decision = DecisionFor(_grid.CurrentRow);

        foreach (var project in _matrix.Solution.Projects)
        {
            var current = row.ValuesByProject.GetValueOrDefault(project.Name);
            string after;
            if (decision.Kind == DecisionKind.Leave)
                after = current ?? "(no definida)";
            else if (decision.Kind == DecisionKind.UnifyKeepOverrides && current is not null && current != decision.Value)
                after = $"{current}  (override en el .cwproj)";
            else
                after = $"{decision.Value}  (desde Common.props)";

            var index = _detailGrid.Rows.Add(project.Name, current ?? "(no definida)", after);
            var changed = (current ?? "") != after.Split("  (")[0];
            if (changed) _detailGrid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(176, 0, 32);
        }
    }

    private PropertyDecision DecisionFor(DataGridViewRow gridRow)
    {
        var row = (PropertyRow)gridRow.Tag!;
        var kind = (gridRow.Cells["decision"].Value as string) switch
        {
            DecisionUnify => DecisionKind.Unify,
            DecisionKeep => DecisionKind.UnifyKeepOverrides,
            _ => DecisionKind.Leave,
        };
        var value = gridRow.Cells["value"].Value as string ?? row.MajorityValue;
        return new PropertyDecision(row.Key, row.IsCandidate ? kind : DecisionKind.Leave, value);
    }

    private List<PropertyDecision> CollectDecisions() => _grid.Rows
        .Cast<DataGridViewRow>()
        .Where(r => r.Tag is PropertyRow)
        .Select(DecisionFor)
        .ToList();

    // ---- plan / aplicar ------------------------------------------------------------------

    private MigrationPlan? BuildPlan()
    {
        if (_matrix is null) return null;
        var decisions = CollectDecisions();

        // Las filas ocultas (por-proyecto) no están en la grilla: se agregan como Leave.
        var known = decisions.Select(d => d.Key).ToHashSet();
        decisions.AddRange(_matrix.Rows
            .Where(r => !known.Contains(r.Key))
            .Select(r => new PropertyDecision(r.Key, DecisionKind.Leave, r.MajorityValue)));

        return MigrationPlan.Create(_matrix, decisions, CurrentOptions());
    }

    private void ShowPlan()
    {
        var plan = BuildPlan();
        if (plan is null) return;
        _planText.Text = TextReport.Plan(plan).Replace("\n", "\r\n");
        _tabs.SelectedIndex = 2;
        SetStatus(plan.CanApply
            ? $"{plan.ChangedEdits.Count()} .cwproj a modificar · {plan.BehaviorChanges.Count} cambio(s) de comportamiento."
            : $"El plan tiene {plan.Blockers.Count} bloqueo(s).");
    }

    private void ApplyPlan()
    {
        var plan = BuildPlan();
        if (plan is null) return;

        _planText.Text = TextReport.Plan(plan).Replace("\n", "\r\n");

        if (!plan.CanApply)
        {
            _tabs.SelectedIndex = 2;
            MessageBox.Show(this, "El plan tiene bloqueos, ver la pestaña Plan:\n\n" + string.Join("\n", plan.Blockers),
                "No se puede aplicar", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var dryRun = MigrationExecutor.Apply(plan, dryRun: true);
        if (dryRun.Errors.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n", dryRun.Errors), "Falló la simulación, no se escribió nada",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var summary = $"Se van a escribir {dryRun.Changes.Count} archivo(s):\n" +
                      string.Join("\n", dryRun.Changes.Select(c => $"  {(c.IsNew ? "nuevo " : "editar")} {c.FileName}"));

        if (plan.BehaviorChanges.Count > 0)
        {
            summary += $"\n\nATENCIÓN: {plan.BehaviorChanges.Count} cambio(s) de comportamiento real:\n" +
                       string.Join("\n", plan.BehaviorChanges.Take(15)
                           .Select(c => $"  {c.Project} · {c.Key}: {c.BeforeText} -> {c.AfterText}")) +
                       (plan.BehaviorChanges.Count > 15 ? $"\n  … y {plan.BehaviorChanges.Count - 15} más (ver pestaña Plan)." : "");
        }

        summary += plan.Options.CreateBackup
            ? "\n\nSe hace backup de los originales en .sdprojectedit\\backup\\."
            : "\n\nSIN BACKUP.";
        summary += "\n\n¿Continuar?";

        if (MessageBox.Show(this, summary, "Confirmar aplicación", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        var result = MigrationExecutor.Apply(plan, dryRun: false);
        if (!result.Applied)
        {
            MessageBox.Show(this, string.Join("\n", result.Errors), "No se aplicó nada", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var reloaded = SolutionSet.Load(_pathBox.Text.Trim());
        var report = Verifier.Run(reloaded, CurrentOptions(), plan.ExpectedOverrides);

        _verifyText.Text = (TextReport.ApplyOutcome(result) + "\n" + TextReport.Verification(report)).Replace("\n", "\r\n");
        _tabs.SelectedIndex = 4;

        MessageBox.Show(this,
            $"{result.Changes.Count} archivo(s) escritos." +
            (result.BackupDirectory is not null ? $"\nBackup: {result.BackupDirectory}" : "") +
            (report.AllPassed ? "\n\nVerificación OK." : "\n\nHay chequeos en falla, ver la pestaña Verificación.") +
            "\n\nCorré un Rebuild Solution completo (no Compile) para confirmar que MSBuild resuelve el Import.",
            "Listo", MessageBoxButtons.OK, report.AllPassed ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

        Analyze();
    }

    private void RunVerification()
    {
        try
        {
            var solution = SolutionSet.Load(_pathBox.Text.Trim());
            var report = Verifier.Run(solution, CurrentOptions());
            _verifyText.Text = TextReport.Verification(report).Replace("\n", "\r\n");
            _tabs.SelectedIndex = 4;
            SetStatus(report.AllPassed ? "Verificación OK." : "Hay chequeos en falla.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "No se pudo verificar", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---- edición de Common.props ---------------------------------------------------------

    private void LoadCommonProps()
    {
        _commonGrid.Rows.Clear();
        _saveCommonButton.Enabled = _addCommonButton.Enabled = false;
        if (_matrix is null) return;

        var path = Path.Combine(_matrix.Solution.RootDirectory, CommonPropsFile.DefaultFileName);
        _commonProps = CommonPropsFile.Load(path);
        _addCommonButton.Enabled = true;
        _saveCommonButton.Enabled = _commonProps.ExistedOnDisk;

        if (!_commonProps.ExistedOnDisk)
        {
            SetStatus($"Todavía no existe {path}. Aplicá el plan para crearlo.");
            return;
        }

        foreach (var key in _commonProps.Keys)
        {
            var shadowedBy = _matrix.Solution.Projects.Where(p => p.Find(key) is not null).Select(p => p.Name).ToList();
            var index = _commonGrid.Rows.Add(
                key.Scope.Display,
                key.Name,
                _commonProps.Values[key],
                PropertyCatalog.Describe(key.Name).Description,
                shadowedBy.Count == 0 ? "" : string.Join(", ", shadowedBy));
            _commonGrid.Rows[index].Tag = key;
            if (shadowedBy.Count > 0) _commonGrid.Rows[index].Cells["shadow"].Style.ForeColor = Color.FromArgb(176, 0, 32);
        }
        _saveCommonButton.Enabled = true;
    }

    private void AddCommonProperty()
    {
        if (_matrix is null) return;
        using var dialog = new AddPropertyForm(_matrix.Solution.Configurations);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Key is null) return;

        var key = dialog.Key.Value;
        if (PropertyCatalog.IsNeverUnify(key.Name))
        {
            MessageBox.Show(this, $"{key.Name} es una propiedad por-proyecto y no se centraliza.", "No permitido",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var index = _commonGrid.Rows.Add(key.Scope.Display, key.Name, dialog.Value,
            PropertyCatalog.Describe(key.Name).Description, "");
        _commonGrid.Rows[index].Tag = key;
        _saveCommonButton.Enabled = true;
    }

    private void SaveCommonProps()
    {
        if (_commonProps is null || _matrix is null) return;

        _commonProps.Clear();
        foreach (DataGridViewRow row in _commonGrid.Rows)
        {
            if (row.Tag is not PropertyKey key) continue;
            _commonProps.Set(key, row.Cells["value"].Value as string ?? "");
        }

        var format = (_matrix.Solution.Projects.FirstOrDefault()?.Format ?? TextFileFormat.ClarionDefault)
            with { HasXmlDeclaration = false, TrailingNewLine = true };

        if (_backup.Checked && File.Exists(_commonProps.Path))
        {
            var backupDir = Path.Combine(_matrix.Solution.RootDirectory, ".sdprojectedit", "backup",
                DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(backupDir);
            File.Copy(_commonProps.Path, Path.Combine(backupDir, _commonProps.FileName), overwrite: true);
        }

        _commonProps.Save(format);
        SetStatus($"{_commonProps.FileName} guardado ({_commonProps.Values.Count} propiedades).");
        RunVerification();
    }
}

/// <summary>Diálogo chico para agregar una propiedad al Common.props.</summary>
internal sealed class AddPropertyForm : Form
{
    private readonly ComboBox _scope = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 180 };
    private readonly ComboBox _name = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 260 };
    private readonly TextBox _value = new() { Width = 180 };
    private readonly Label _description = new() { AutoSize = false, Width = 460, Height = 40, ForeColor = SystemColors.GrayText };

    public PropertyKey? Key { get; private set; }
    public string Value => _value.Text;

    public AddPropertyForm(IEnumerable<string> configurations)
    {
        Text = "Agregar propiedad a Common.props";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(500, 210);

        _scope.Items.Add("general");
        foreach (var configuration in configurations) _scope.Items.Add(configuration);
        _scope.SelectedIndex = _scope.Items.Count > 1 ? 1 : 0;

        foreach (var info in PropertyCatalog.AllKnown) _name.Items.Add(info.Name);

        var ok = new Button { Text = "Agregar", DialogResult = DialogResult.OK, Width = 90 };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Width = 90 };
        AcceptButton = ok;
        CancelButton = cancel;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10), RowCount = 5 };
        layout.Controls.Add(new Label { Text = "Ámbito:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_scope, 1, 0);
        layout.Controls.Add(new Label { Text = "Propiedad:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(_name, 1, 1);
        layout.Controls.Add(new Label { Text = "Valor:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        layout.Controls.Add(_value, 1, 2);
        layout.Controls.Add(_description, 1, 3);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Height = 34 };
        buttons.Controls.AddRange([cancel, ok]);
        layout.Controls.Add(buttons, 1, 4);
        Controls.Add(layout);

        _name.TextChanged += (_, _) =>
        {
            var info = PropertyCatalog.Describe(_name.Text);
            _description.Text = info.Description +
                (info.Choices.Count > 0 ? $"\nValores: {string.Join(" · ", info.Choices)}" : "");
        };

        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK) return;
            if (string.IsNullOrWhiteSpace(_name.Text) || string.IsNullOrWhiteSpace(_scope.Text))
            {
                MessageBox.Show(this, "Faltan el ámbito o el nombre.", "Datos incompletos",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                return;
            }
            var scope = _scope.Text.Equals("general", StringComparison.OrdinalIgnoreCase)
                ? PropertyScope.General
                : PropertyScope.For(_scope.Text.Trim());
            Key = new PropertyKey(scope, _name.Text.Trim());
        };
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _name.Focus();
    }
}
