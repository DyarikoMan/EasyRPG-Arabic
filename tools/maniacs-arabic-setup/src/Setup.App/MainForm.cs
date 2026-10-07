using EasyRpgArabic.Setup.Core.Detection;
using EasyRpgArabic.Setup.Core.Modules;
using EasyRpgArabic.Setup.Core.Maniacs;
using EasyRpgArabic.Setup.Core.Transactions;

namespace EasyRpgArabic.Setup.App;

internal sealed class MainForm : Form
{
	private readonly SteamRpgMaker2003Detector _detector;
	private readonly IReadOnlyList<ISetupModule> _modules;
	private readonly ManiacsSetupModule? _maniacsModule;
	private readonly Dictionary<string, CheckBox> _moduleChecks = new(StringComparer.OrdinalIgnoreCase);
	private readonly TextBox _sourcePath = new();
	private readonly TextBox _maniacsPackagePath = new();
	private readonly TextBox _destinationPath = new();
	private readonly Label _maniacsPackageStatus = new();
	private readonly Label _detectionStatus = new();
	private readonly Label _operationStatus = new();
	private readonly Button _installButton = new();
	private readonly Button _advancedButton = new();
	private readonly Panel _advancedPanel = new();
	private bool _destinationManuallyChanged;

	public MainForm(SteamRpgMaker2003Detector detector, IReadOnlyList<ISetupModule> modules)
	{
		_detector = detector;
		_modules = modules;
		_maniacsModule = modules.OfType<ManiacsSetupModule>().SingleOrDefault();
		Text = "EasyRPG-Arabic Setup";
		StartPosition = FormStartPosition.CenterScreen;
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MaximizeBox = false;
		MinimizeBox = true;
		ClientSize = new Size(760, 550);
		Font = new Font("Segoe UI", 9F);

		BuildLayout();
		DetectInstallation();
	}

	private void BuildLayout()
	{
		var heading = new Label
		{
			Text = "EasyRPG-Arabic Setup",
			Font = new Font(Font, FontStyle.Bold),
			Location = new Point(24, 20),
			Size = new Size(690, 34)
		};
		Controls.Add(heading);

		_detectionStatus.Location = new Point(24, 60);
		_detectionStatus.Size = new Size(700, 24);
		Controls.Add(_detectionStatus);

		var sourceLabel = new Label { Text = "Vanilla Steam RPG Maker 2003 folder", Location = new Point(24, 96), Size = new Size(360, 22) };
		Controls.Add(sourceLabel);
		_sourcePath.Location = new Point(24, 122);
		_sourcePath.Size = new Size(590, 28);
		_sourcePath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
		_sourcePath.TextChanged += (_, _) => UpdateSourceValidation();
		Controls.Add(_sourcePath);

		var browseSource = new Button { Text = "Browse...", Location = new Point(626, 120), Size = new Size(108, 30), Anchor = AnchorStyles.Top | AnchorStyles.Right };
		browseSource.Click += (_, _) => BrowseForSource();
		Controls.Add(browseSource);

		var recommendedLabel = new Label
		{
			Text = "Recommended setup",
			Font = new Font(Font, FontStyle.Bold),
			Location = new Point(24, 174),
			Size = new Size(400, 24)
		};
		Controls.Add(recommendedLabel);

		var modulePanel = new TableLayoutPanel
		{
			Location = new Point(24, 202),
			Size = new Size(710, 156),
			ColumnCount = 3,
			RowCount = _modules.Count,
			Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
			CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
		};
		modulePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
		modulePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
		modulePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		for (var index = 0; index < _modules.Count; index++)
		{
			var descriptor = _modules[index].Descriptor;
			modulePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
			var check = new CheckBox { Checked = true, AutoSize = false, Size = new Size(20, 22), Anchor = AnchorStyles.None };
			check.Enabled = descriptor.Readiness == ModuleReadiness.Available;
			_moduleChecks.Add(descriptor.Id, check);
			modulePanel.Controls.Add(check, 0, index);
			modulePanel.Controls.Add(new Label { Text = descriptor.Name, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 1, index);
			var statusText = descriptor.Readiness switch
			{
				_ when descriptor.Id == "maniacs" => "Versioned package inspection · install gated",
				ModuleReadiness.NotImplemented => "Not implemented",
				ModuleReadiness.Unavailable => "Unavailable",
				_ when descriptor.Id == "arabic-editor" => "Package ready · requires Maniacs English",
				_ => descriptor.StatusMessage
			};
			var status = new Label { Text = statusText, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill, ForeColor = descriptor.Readiness == ModuleReadiness.Available ? Color.DarkGreen : Color.DarkOrange };
			modulePanel.Controls.Add(status, 2, index);
		}
		Controls.Add(modulePanel);

		_advancedButton.Text = "Advanced options  ▸";
		_advancedButton.Location = new Point(24, 378);
		_advancedButton.Size = new Size(180, 30);
		_advancedButton.FlatStyle = FlatStyle.Flat;
		_advancedButton.Click += (_, _) => ToggleAdvancedOptions();
		Controls.Add(_advancedButton);

		_advancedPanel.Location = new Point(24, 414);
		_advancedPanel.Size = new Size(710, 190);
		_advancedPanel.Visible = false;
		var destinationLabel = new Label { Text = "Separate managed installation folder", Location = new Point(0, 0), Size = new Size(400, 22) };
		_advancedPanel.Controls.Add(destinationLabel);
		_destinationPath.Location = new Point(0, 26);
		_destinationPath.Size = new Size(590, 28);
		_destinationPath.TextChanged += (_, _) => _destinationManuallyChanged = true;
		_advancedPanel.Controls.Add(_destinationPath);
		var browseDestination = new Button { Text = "Browse...", Location = new Point(602, 24), Size = new Size(108, 30) };
		browseDestination.Click += (_, _) => BrowseForDestination();
		_advancedPanel.Controls.Add(browseDestination);
		_advancedPanel.Controls.Add(new Label
		{
			Text = "Extracted official Maniacs package (used for build/layout inspection)",
			Location = new Point(0, 61),
			Size = new Size(500, 22)
		});
		_maniacsPackagePath.Location = new Point(0, 87);
		_maniacsPackagePath.Size = new Size(590, 28);
		_maniacsPackagePath.TextChanged += (_, _) => UpdateManiacsPackageInspection();
		_advancedPanel.Controls.Add(_maniacsPackagePath);
		var browseManiacsPackage = new Button { Text = "Browse...", Location = new Point(602, 85), Size = new Size(108, 30) };
		browseManiacsPackage.Click += (_, _) => BrowseForManiacsPackage();
		_advancedPanel.Controls.Add(browseManiacsPackage);
		_maniacsPackageStatus.Location = new Point(0, 119);
		_maniacsPackageStatus.Size = new Size(700, 66);
		_maniacsPackageStatus.ForeColor = Color.DimGray;
		_advancedPanel.Controls.Add(_maniacsPackageStatus);
		Controls.Add(_advancedPanel);
		UpdateManiacsPackageInspection();

		_operationStatus.Location = new Point(24, 466);
		_operationStatus.Size = new Size(520, 48);
		_operationStatus.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
		_operationStatus.ForeColor = Color.DarkSlateGray;
		Controls.Add(_operationStatus);

		_installButton.Text = "Install";
		_installButton.Location = new Point(612, 482);
		_installButton.Size = new Size(122, 42);
		_installButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
		_installButton.BackColor = Color.FromArgb(0, 105, 190);
		_installButton.ForeColor = Color.White;
		_installButton.FlatStyle = FlatStyle.Flat;
		_installButton.Font = new Font(Font, FontStyle.Bold);
		_installButton.Click += async (_, _) => await InstallRecommendedAsync();
		Controls.Add(_installButton);
	}

	private void DetectInstallation()
	{
		var detected = _detector.Detect();
		if (detected is null)
		{
			_detectionStatus.Text = "RPG Maker 2003 was not detected. Browse to your vanilla Steam installation.";
			_detectionStatus.ForeColor = Color.DarkOrange;
			return;
		}

		_sourcePath.Text = detected.Directory;
		_detectionStatus.Text = "RPG Maker 2003 detected and validated.";
		_detectionStatus.ForeColor = Color.DarkGreen;
		SetDefaultDestination(detected.Directory);
	}

	private void UpdateSourceValidation()
	{
		if (string.IsNullOrWhiteSpace(_sourcePath.Text)) { return; }
		var validation = RpgMaker2003InstallationValidator.Validate(_sourcePath.Text.Trim());
		_detectionStatus.Text = validation.IsValid ? "RPG Maker 2003 detected and validated." : validation.Message;
		_detectionStatus.ForeColor = validation.IsValid ? Color.DarkGreen : Color.DarkOrange;
		if (validation.IsValid && !_destinationManuallyChanged) { SetDefaultDestination(_sourcePath.Text.Trim()); }
	}

	private void SetDefaultDestination(string source)
	{
		var parent = Directory.GetParent(Path.GetFullPath(source))?.FullName;
		if (parent is null) { return; }
		_destinationPath.Text = Path.Combine(parent, "RPG Maker 2003 - Arabic");
		_destinationManuallyChanged = false;
	}

	private void BrowseForSource()
	{
		using var dialog = new FolderBrowserDialog
		{
			Description = "Select the vanilla Steam RPG Maker 2003 installation folder.",
			ShowNewFolderButton = false,
			SelectedPath = Directory.Exists(_sourcePath.Text) ? _sourcePath.Text : string.Empty
		};
		if (dialog.ShowDialog(this) == DialogResult.OK)
		{
			_sourcePath.Text = dialog.SelectedPath;
			var result = RpgMaker2003InstallationValidator.Validate(dialog.SelectedPath);
			if (result.IsValid && !_destinationManuallyChanged) { SetDefaultDestination(dialog.SelectedPath); }
		}
	}

	private void BrowseForDestination()
	{
		using var dialog = new FolderBrowserDialog
		{
			Description = "Select the parent folder for the separate RPG Maker 2003 - Arabic installation.",
			ShowNewFolderButton = true,
			SelectedPath = Directory.Exists(_destinationPath.Text) ? _destinationPath.Text : Path.GetDirectoryName(_destinationPath.Text) ?? string.Empty
		};
		if (dialog.ShowDialog(this) == DialogResult.OK)
		{
			_destinationPath.Text = Path.Combine(dialog.SelectedPath, "RPG Maker 2003 - Arabic");
		}
	}

	private void BrowseForManiacsPackage()
	{
		using var dialog = new FolderBrowserDialog
		{
			Description = "Select the extracted official Maniacs package folder.",
			ShowNewFolderButton = false,
			SelectedPath = Directory.Exists(_maniacsPackagePath.Text) ? _maniacsPackagePath.Text : string.Empty
		};
		if (dialog.ShowDialog(this) == DialogResult.OK) { _maniacsPackagePath.Text = dialog.SelectedPath; }
	}

	private void UpdateManiacsPackageInspection()
	{
		if (_maniacsModule is null)
		{
			_maniacsPackageStatus.Text = "Maniacs package inspector is unavailable.";
			_maniacsPackageStatus.ForeColor = Color.DarkOrange;
			return;
		}
		if (string.IsNullOrWhiteSpace(_maniacsPackagePath.Text))
		{
			_maniacsPackageStatus.Text = "Select the extracted package to check its version, required files, layout, architecture, and Arabic Editor compatibility.";
			_maniacsPackageStatus.ForeColor = Color.DimGray;
			return;
		}

		try
		{
			var inspection = _maniacsModule.InspectPackage(_maniacsPackagePath.Text.Trim());
			var lines = new List<string> { inspection.Summary };
			if (!string.IsNullOrEmpty(inspection.TechnicalSummary)) { lines.Add(inspection.TechnicalSummary); }
			if (inspection.Issues.Count != 0) { lines.Add(string.Join(" · ", inspection.Issues.Take(2))); }
			if (inspection.Status == ManiacsPackageStatus.KnownSupportedBuild)
			{
				lines.Add("Inspection only; official utility and English patch application is not verified.");
			}
			_maniacsPackageStatus.Text = string.Join(Environment.NewLine, lines);
			_maniacsPackageStatus.ForeColor = inspection.Status switch
			{
				ManiacsPackageStatus.KnownSupportedBuild when inspection.ArabicEditorValidated => Color.DarkGreen,
				ManiacsPackageStatus.KnownSupportedBuild => Color.DarkOrange,
				_ => Color.DarkOrange
			};
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
		{
			_maniacsPackageStatus.Text = $"Could not inspect package: {exception.Message}";
			_maniacsPackageStatus.ForeColor = Color.DarkOrange;
		}
	}

	private void ToggleAdvancedOptions()
	{
		_advancedPanel.Visible = !_advancedPanel.Visible;
		_advancedButton.Text = _advancedPanel.Visible ? "Advanced options  ▾" : "Advanced options  ▸";
		ClientSize = new Size(760, _advancedPanel.Visible ? 720 : 550);
	}

	private async Task InstallRecommendedAsync()
	{
		var source = _sourcePath.Text.Trim();
		var validation = RpgMaker2003InstallationValidator.Validate(source);
		if (!validation.IsValid)
		{
			MessageBox.Show(this, validation.Message, "Check RPG Maker 2003", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}

		var selected = _moduleChecks.Where(pair => pair.Value.Checked).Select(pair => pair.Key).ToArray();
		SetupPlan plan;
		try
		{
			plan = SetupModulePlanner.CreatePlan(selected, _modules);
		}
		catch (SetupModulePlanException exception)
		{
			var details = exception.Blockers.Count == 0 ? exception.Message : exception.Message + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, exception.Blockers.Select(blocker => "• " + blocker));
			_operationStatus.Text = "No files changed. Some recommended modules are not available yet.";
			MessageBox.Show(this, details + Environment.NewLine + Environment.NewLine + "No files have been changed.", "Setup not ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}

		var destination = _destinationPath.Text.Trim();
		if (string.IsNullOrWhiteSpace(destination))
		{
			MessageBox.Show(this, "Choose a separate installation folder in Advanced options.", "Select destination", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}
		var replaceExisting = false;
		if (Directory.Exists(destination))
		{
			var choice = MessageBox.Show(this,
				$"The destination already exists. If installation succeeds, it will be kept as a rollback backup before the new setup is finalized.\n\n{destination}\n\nContinue?",
				"Confirm replacement", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
			if (choice != DialogResult.Yes) { return; }
			replaceExisting = true;
		}

		_installButton.Enabled = false;
		UseWaitCursor = true;
		_operationStatus.Text = "Preparing the separate installation...";
		try
		{
			var result = await new InstallationCoordinator().InstallAsync(source, destination, plan, replaceExisting);
			_operationStatus.Text = $"Done. Installed at {result.InstallationDirectory}";
			MessageBox.Show(this, $"Setup completed.\n\n{result.InstallationDirectory}", "EasyRPG-Arabic Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
		}
		catch (Exception exception)
		{
			_operationStatus.Text = "Setup failed. The source installation was not modified.";
			MessageBox.Show(this, exception.Message, "Setup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
		finally
		{
			UseWaitCursor = false;
			_installButton.Enabled = true;
		}
	}
}
