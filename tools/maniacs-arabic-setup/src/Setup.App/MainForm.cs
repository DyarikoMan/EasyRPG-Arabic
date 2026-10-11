using System.Diagnostics;
using EasyRpgArabic.Setup.Core.Detection;
using EasyRpgArabic.Setup.Core.Maniacs;
using EasyRpgArabic.Setup.Core.Modules;
using EasyRpgArabic.Setup.Core.Transactions;

namespace EasyRpgArabic.Setup.App;

internal sealed class MainForm : Form
{
	private readonly SteamRpgMaker2003Detector _detector;
	private readonly IReadOnlyList<ISetupModule> _modules;
	private readonly ManiacsSetupModule? _maniacsModule;
	private readonly Dictionary<string, CheckBox> _moduleChecks = new(StringComparer.OrdinalIgnoreCase);
	private readonly TextBox _sourcePath = new();
	private readonly Button _browseSourceButton = new();
	private readonly TextBox _maniacsPackagePath = new();
	private readonly Label _maniacsPackagePathLabel = new();
	private readonly Button _browseManiacsPackageButton = new();
	private readonly ComboBox _maniacsPackageSource = new();
	private readonly Label _maniacsPackageSourceLabel = new();
	private readonly TextBox _destinationPath = new();
	private readonly Button _browseDestinationButton = new();
	private readonly Label _maniacsPackageStatus = new();
	private readonly Label _detectionStatus = new();
	private readonly Label _operationStatus = new();
	private readonly ProgressBar _installationProgress = new();
	private readonly Button _installButton = new();
	private readonly Button _advancedButton = new();
	private readonly Panel _advancedPanel = new();
	private readonly CheckBox _eightDirectionMovement = new();
	private readonly CheckBox _pixelMovement = new();
	private readonly CheckBox _runWithShift = new();
	private readonly CheckBox _smoothCamera = new();
	private readonly Panel _successPanel = new();
	private readonly Label _successStatus = new();
	private readonly TemporaryManiacsPackage? _embeddedManiacsPackage;
	private readonly string? _embeddedManiacsPackageError;
	private bool _destinationManuallyChanged;
	private bool _updatingDestination;
	private bool _advancedExpanded;
	private bool _privateEmbeddedBuild;
	private string? _successfulInstallDirectory;

	public MainForm(
		SteamRpgMaker2003Detector detector,
		IReadOnlyList<ISetupModule> modules,
		ManiacsPackageSourceMode defaultManiacsPackageSource,
		Func<Stream>? openEmbeddedManiacsPackage)
	{
		_detector = detector;
		_modules = modules;
		_maniacsModule = modules.OfType<ManiacsSetupModule>().SingleOrDefault();
		_privateEmbeddedBuild = IsEmbeddedManiacsPackageBuild(defaultManiacsPackageSource);
		if (defaultManiacsPackageSource == ManiacsPackageSourceMode.EmbeddedValidated241029)
		{
			try
			{
				if (openEmbeddedManiacsPackage is null)
				{
					throw new InvalidDataException("This build selected an embedded Maniacs package but contains no package resource.");
				}
				_embeddedManiacsPackage = TemporaryManiacsPackage.Extract(openEmbeddedManiacsPackage);
				if (_maniacsModule is null)
				{
					throw new InvalidDataException("The Maniacs package inspector is unavailable.");
				}
				var inspection = _maniacsModule.InspectPackage(_embeddedManiacsPackage.DirectoryPath);
				if (!inspection.IsKnownSupportedBuild || !inspection.ArabicEditorValidated)
				{
					throw new InvalidDataException("The embedded Maniacs package failed the existing exact 241029 fingerprint or Arabic Editor compatibility checks. " + inspection.Summary);
				}
			}
			catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
			{
				_embeddedManiacsPackageError = exception.Message;
				_embeddedManiacsPackage?.Dispose();
				_embeddedManiacsPackage = null;
			}
		}

		Text = "EasyRPG-Arabic Setup";
		StartPosition = FormStartPosition.CenterScreen;
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MaximizeBox = false;
		MinimizeBox = true;
		ClientSize = new Size(710, 326);
		Font = new Font("Segoe UI", 9F);

		BuildLayout(defaultManiacsPackageSource);
		DetectInstallation();
		UpdatePackageSourceControls();
	}

	private void BuildLayout(ManiacsPackageSourceMode defaultManiacsPackageSource)
	{
		var heading = new Label
		{
			Text = "EasyRPG-Arabic Setup",
			Font = new Font(Font, FontStyle.Bold),
			Location = new Point(24, 20),
			Size = new Size(660, 34)
		};
		Controls.Add(heading);

		var sourceLabel = new Label { Text = "RPG Maker 2003", Location = new Point(24, 67), Size = new Size(240, 22) };
		Controls.Add(sourceLabel);
		_detectionStatus.Location = new Point(460, 67);
		_detectionStatus.Size = new Size(224, 22);
		_detectionStatus.TextAlign = ContentAlignment.MiddleRight;
		Controls.Add(_detectionStatus);

		_sourcePath.Location = new Point(24, 93);
		_sourcePath.Size = new Size(660, 28);
		_sourcePath.ReadOnly = true;
		_sourcePath.TextChanged += (_, _) => UpdateSourceValidation();
		Controls.Add(_sourcePath);
		_browseSourceButton.Text = "Browse...";
		_browseSourceButton.Location = new Point(592, 92);
		_browseSourceButton.Size = new Size(92, 30);
		_browseSourceButton.Visible = false;
		_browseSourceButton.Click += (_, _) => BrowseForSource();
		Controls.Add(_browseSourceButton);

		var destinationLabel = new Label { Text = "Install Arabic version to", Location = new Point(24, 145), Size = new Size(320, 22) };
		Controls.Add(destinationLabel);
		_destinationPath.Location = new Point(24, 171);
		_destinationPath.Size = new Size(660, 28);
		_destinationPath.ReadOnly = true;
		_destinationPath.TextChanged += (_, _) =>
		{
			if (!_updatingDestination) { _destinationManuallyChanged = true; }
		};
		Controls.Add(_destinationPath);
		_browseDestinationButton.Text = "Browse...";
		_browseDestinationButton.Location = new Point(592, 170);
		_browseDestinationButton.Size = new Size(92, 30);
		_browseDestinationButton.Visible = false;
		_browseDestinationButton.Click += (_, _) => BrowseForDestination();
		Controls.Add(_browseDestinationButton);

		_advancedButton.Text = "Advanced options  ▸";
		_advancedButton.Location = new Point(24, 218);
		_advancedButton.Size = new Size(190, 30);
		_advancedButton.FlatStyle = FlatStyle.Flat;
		_advancedButton.Click += (_, _) => ToggleAdvancedOptions();
		Controls.Add(_advancedButton);

		_advancedPanel.Location = new Point(24, 254);
		_advancedPanel.Size = new Size(660, 350);
		_advancedPanel.Visible = false;
		_advancedPanel.BorderStyle = BorderStyle.FixedSingle;

		_maniacsPackageSourceLabel.Text = "Maniacs package source";
		_maniacsPackageSourceLabel.Location = new Point(8, 8);
		_maniacsPackageSourceLabel.Size = new Size(250, 22);
		_advancedPanel.Controls.Add(_maniacsPackageSourceLabel);
		_maniacsPackageSource.DropDownStyle = ComboBoxStyle.DropDownList;
		_maniacsPackageSource.Location = new Point(8, 31);
		_maniacsPackageSource.Size = new Size(640, 28);
		if (IsEmbeddedManiacsPackageBuild(defaultManiacsPackageSource))
		{
			_maniacsPackageSource.Items.Add(new PackageSourceChoice(ManiacsPackageSourceMode.EmbeddedValidated241029, "Use validated Maniacs 241029 included with this private setup"));
		}
		_maniacsPackageSource.Items.Add(new PackageSourceChoice(ManiacsPackageSourceMode.UserSuppliedValidatedPackage, "Use an extracted supported Maniacs package"));
		_maniacsPackageSource.SelectedItem = _maniacsPackageSource.Items.Cast<PackageSourceChoice>()
			.Single(choice => choice.Mode == defaultManiacsPackageSource);
		_maniacsPackageSource.SelectedIndexChanged += (_, _) => UpdatePackageSourceControls();
		_advancedPanel.Controls.Add(_maniacsPackageSource);

		_maniacsPackagePathLabel.Text = "Extracted Maniacs package folder";
		_maniacsPackagePathLabel.Location = new Point(8, 66);
		_maniacsPackagePathLabel.Size = new Size(380, 20);
		_advancedPanel.Controls.Add(_maniacsPackagePathLabel);
		_maniacsPackagePath.Location = new Point(8, 88);
		_maniacsPackagePath.Size = new Size(536, 28);
		_maniacsPackagePath.TextChanged += (_, _) => UpdateManiacsPackageInspection();
		_advancedPanel.Controls.Add(_maniacsPackagePath);
		_browseManiacsPackageButton.Text = "Browse...";
		_browseManiacsPackageButton.Location = new Point(554, 87);
		_browseManiacsPackageButton.Size = new Size(94, 30);
		_browseManiacsPackageButton.Click += (_, _) => BrowseForManiacsPackage();
		_advancedPanel.Controls.Add(_browseManiacsPackageButton);
		_maniacsPackageStatus.Location = new Point(8, 120);
		_maniacsPackageStatus.Size = new Size(640, 36);
		_maniacsPackageStatus.ForeColor = Color.DimGray;
		_advancedPanel.Controls.Add(_maniacsPackageStatus);
		if (_privateEmbeddedBuild)
		{
			_maniacsPackageSourceLabel.Visible = false;
			_maniacsPackageSource.Visible = false;
			_maniacsPackagePathLabel.Visible = false;
			_maniacsPackagePath.Visible = false;
			_browseManiacsPackageButton.Visible = false;
			_maniacsPackageStatus.Location = new Point(8, 8);
			_maniacsPackageStatus.Size = new Size(640, 36);
		}

		var defaultsGroup = new GroupBox
		{
			Text = "New project defaults",
			Location = new Point(8, 160),
			Size = new Size(282, 178)
		};
		ConfigureMovementDefaultCheckBox(_eightDirectionMovement, "8-direction movement", 24);
		ConfigureMovementDefaultCheckBox(_pixelMovement, "Pixel movement", 54);
		ConfigureMovementDefaultCheckBox(_runWithShift, "Run with Shift", 84);
		ConfigureMovementDefaultCheckBox(_smoothCamera, "Smooth camera", 114);
		_pixelMovement.CheckedChanged += (_, _) => UpdatePixelMovementDependencies();
		defaultsGroup.Controls.AddRange([_eightDirectionMovement, _pixelMovement, _runWithShift, _smoothCamera]);
		_advancedPanel.Controls.Add(defaultsGroup);
		UpdatePixelMovementDependencies();

		_advancedPanel.Controls.Add(new Label
		{
			Text = "Selected modules",
			Font = new Font(Font, FontStyle.Bold),
			Location = new Point(300, 160),
			Size = new Size(348, 22)
		});
		var modulePanel = new TableLayoutPanel
		{
			Location = new Point(300, 184),
			Size = new Size(348, 154),
			ColumnCount = 3,
			RowCount = _modules.Count,
			CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
		};
		modulePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));
		modulePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
		modulePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
		for (var index = 0; index < _modules.Count; index++)
		{
			var descriptor = _modules[index].Descriptor;
			modulePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / Math.Max(1, _modules.Count)));
			var check = new CheckBox
			{
				Checked = descriptor.Readiness == ModuleReadiness.Available,
				AutoSize = false,
				Size = new Size(20, 22),
				Anchor = AnchorStyles.None,
				Enabled = descriptor.Readiness == ModuleReadiness.Available
			};
			_moduleChecks.Add(descriptor.Id, check);
			modulePanel.Controls.Add(check, 0, index);
			modulePanel.Controls.Add(new Label { Text = descriptor.Name, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 1, index);
			var statusText = descriptor.Readiness switch
			{
				ModuleReadiness.NotImplemented => "Not implemented",
				ModuleReadiness.Unavailable => "Unavailable",
				_ when descriptor.Id is "maniacs" or "maniacs-english" => "Ready · validated Maniacs package required",
				_ when descriptor.Id == "arabic-editor" => "Ready · requires Maniacs English",
				_ when descriptor.Id == "easyrpg-arabic" => "Ready · installs EasyRPG Player support",
				_ => descriptor.StatusMessage
			};
			var status = new Label
			{
				Text = statusText,
				TextAlign = ContentAlignment.MiddleLeft,
				Dock = DockStyle.Fill,
				ForeColor = descriptor.Readiness == ModuleReadiness.Available ? Color.DarkGreen : Color.DarkOrange
			};
			modulePanel.Controls.Add(status, 2, index);
		}
		_advancedPanel.Controls.Add(modulePanel);
		Controls.Add(_advancedPanel);

		_operationStatus.Location = new Point(24, 254);
		_operationStatus.Size = new Size(490, 22);
		_operationStatus.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
		_operationStatus.ForeColor = Color.DarkSlateGray;
		Controls.Add(_operationStatus);
		_installationProgress.Location = new Point(24, 278);
		_installationProgress.Size = new Size(490, 14);
		_installationProgress.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
		_installationProgress.Minimum = 0;
		_installationProgress.Maximum = 100;
		_installationProgress.Visible = false;
		Controls.Add(_installationProgress);

		_installButton.Text = "Install";
		_installButton.Location = new Point(560, 250);
		_installButton.Size = new Size(124, 40);
		_installButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
		_installButton.BackColor = Color.FromArgb(0, 105, 190);
		_installButton.ForeColor = Color.White;
		_installButton.FlatStyle = FlatStyle.Flat;
		_installButton.Font = new Font(Font, FontStyle.Bold);
		_installButton.Click += async (_, _) => await InstallRecommendedAsync();
		Controls.Add(_installButton);

		_successPanel.Dock = DockStyle.Fill;
		_successPanel.Visible = false;
		_successPanel.BackColor = SystemColors.Window;
		var successHeading = new Label
		{
			Text = "✓ EasyRPG-Arabic is ready.",
			Font = new Font(Font, FontStyle.Bold),
			Location = new Point(32, 36),
			Size = new Size(520, 34)
		};
		_successPanel.Controls.Add(successHeading);
		_successStatus.Location = new Point(32, 78);
		_successStatus.Size = new Size(520, 28);
		_successStatus.ForeColor = Color.DarkSlateGray;
		_successPanel.Controls.Add(_successStatus);
		var openButton = new Button { Text = "Open the installation folder", Location = new Point(32, 132), Size = new Size(250, 38) };
		openButton.Click += async (_, _) => await OpenSuccessfulInstallationFolderAsync(closeAfterOpen: true);
		_successPanel.Controls.Add(openButton);
		var closeButton = new Button { Text = "Close", Location = new Point(440, 132), Size = new Size(96, 38) };
		closeButton.Click += (_, _) => Close();
		_successPanel.Controls.Add(closeButton);
		Controls.Add(_successPanel);
		_successPanel.BringToFront();
	}

	private static bool IsEmbeddedManiacsPackageBuild(ManiacsPackageSourceMode defaultSource) =>
		defaultSource == ManiacsPackageSourceMode.EmbeddedValidated241029;

	private void DetectInstallation()
	{
		var detected = _detector.Detect();
		if (detected is null)
		{
			_detectionStatus.Text = "Not found";
			_detectionStatus.ForeColor = Color.DarkOrange;
			UpdateSourceValidation();
			return;
		}

		_sourcePath.Text = detected.Directory;
		var validation = RpgMaker2003InstallationValidator.Validate(detected.Directory);
		ShowSourceValidation(validation);
		if (validation.IsValid) { SetDefaultDestination(detected.Directory); }
	}

	private void UpdateSourceValidation()
	{
		if (string.IsNullOrWhiteSpace(_sourcePath.Text))
		{
			_detectionStatus.Text = "Not found";
			_detectionStatus.ForeColor = Color.DarkOrange;
			RefreshSourceBrowseVisibility(false);
			return;
		}

		var validation = RpgMaker2003InstallationValidator.Validate(_sourcePath.Text.Trim());
		ShowSourceValidation(validation);
		if (validation.IsValid && !_destinationManuallyChanged) { SetDefaultDestination(_sourcePath.Text.Trim()); }
	}

	private void ShowSourceValidation(InstallationValidationResult validation)
	{
		_detectionStatus.Text = validation.IsValid ? "Found ✓" : "Check location";
		_detectionStatus.ForeColor = validation.IsValid ? Color.DarkGreen : Color.DarkOrange;
		RefreshSourceBrowseVisibility(validation.IsValid);
	}

	private void RefreshSourceBrowseVisibility(bool sourceIsValid)
	{
		_browseSourceButton.Visible = _advancedExpanded || !sourceIsValid;
		_sourcePath.Size = new Size(_browseSourceButton.Visible ? 554 : 660, 28);
	}

	private void SetDefaultDestination(string source)
	{
		_updatingDestination = true;
		try { _destinationPath.Text = Path.Combine(Path.GetFullPath(source), "Arabic"); }
		finally { _updatingDestination = false; }
		_destinationManuallyChanged = false;
	}

	private void BrowseForSource()
	{
		using var dialog = new FolderBrowserDialog
		{
			Description = "Select the vanilla Steam RPG Maker 2003 folder.",
			ShowNewFolderButton = false,
			SelectedPath = Directory.Exists(_sourcePath.Text) ? _sourcePath.Text : string.Empty
		};
		if (dialog.ShowDialog(this) == DialogResult.OK) { _sourcePath.Text = dialog.SelectedPath; }
	}

	private void BrowseForDestination()
	{
		using var dialog = new FolderBrowserDialog
		{
			Description = "Select the managed Arabic installation folder.",
			ShowNewFolderButton = true,
			SelectedPath = Directory.Exists(_destinationPath.Text) ? _destinationPath.Text : Path.GetDirectoryName(_destinationPath.Text) ?? string.Empty
		};
		if (dialog.ShowDialog(this) == DialogResult.OK)
		{
			_destinationPath.Text = dialog.SelectedPath;
			_destinationManuallyChanged = true;
		}
	}

	private void BrowseForManiacsPackage()
	{
		using var dialog = new FolderBrowserDialog
		{
			Description = "Select the root of an extracted supported Maniacs package.",
			ShowNewFolderButton = false,
			SelectedPath = Directory.Exists(_maniacsPackagePath.Text) ? _maniacsPackagePath.Text : string.Empty
		};
		if (dialog.ShowDialog(this) == DialogResult.OK) { _maniacsPackagePath.Text = dialog.SelectedPath; }
	}

	private ManiacsPackageSourceMode SelectedManiacsPackageSource =>
		_maniacsPackageSource.SelectedItem is PackageSourceChoice choice
			? choice.Mode
			: ManiacsPackageSourceMode.UserSuppliedValidatedPackage;

	private void UpdatePackageSourceControls()
	{
		var embedded = SelectedManiacsPackageSource == ManiacsPackageSourceMode.EmbeddedValidated241029;
		_maniacsPackagePathLabel.Visible = !embedded;
		_maniacsPackagePath.Visible = !embedded;
		_browseManiacsPackageButton.Visible = !embedded;
		if (embedded)
		{
			if (_embeddedManiacsPackageError is not null)
			{
				_maniacsPackageStatus.Text = "The bundled package failed validation: " + _embeddedManiacsPackageError;
				_maniacsPackageStatus.ForeColor = Color.DarkOrange;
				return;
			}
			if (_embeddedManiacsPackage is null || _maniacsModule is null)
			{
				_maniacsPackageStatus.Text = "The validated Maniacs package is unavailable in this setup build.";
				_maniacsPackageStatus.ForeColor = Color.DarkOrange;
				return;
			}
			UpdateManiacsPackageInspection(_embeddedManiacsPackage.DirectoryPath);
			return;
		}
		UpdateManiacsPackageInspection();
	}

	private void UpdateManiacsPackageInspection() =>
		UpdateManiacsPackageInspection(_maniacsPackagePath.Text.Trim());

	private void UpdateManiacsPackageInspection(string packageDirectory)
	{
		if (_maniacsModule is null)
		{
			_maniacsPackageStatus.Text = "Maniacs package validation is unavailable.";
			_maniacsPackageStatus.ForeColor = Color.DarkOrange;
			return;
		}
		if (string.IsNullOrWhiteSpace(packageDirectory))
		{
			_maniacsPackageStatus.Text = "Select an extracted package to check its exact version, files, architecture, and compatibility.";
			_maniacsPackageStatus.ForeColor = Color.DimGray;
			return;
		}

		try
		{
			var inspection = _maniacsModule.InspectPackage(packageDirectory);
			var lines = new List<string> { inspection.Summary };
			if (inspection.Status == ManiacsPackageStatus.KnownSupportedBuild)
			{
				lines.Add(inspection.ArabicEditorValidated
					? "The exact supported package is ready; license acceptance is required before installation."
					: "Arabic Editor support is not validated for this build.");
			}
			if (inspection.Issues.Count != 0) { lines.Add(string.Join(" · ", inspection.Issues.Take(2))); }
			_maniacsPackageStatus.Text = string.Join(Environment.NewLine, lines);
			_maniacsPackageStatus.ForeColor = inspection.IsKnownSupportedBuild && inspection.ArabicEditorValidated ? Color.DarkGreen : Color.DarkOrange;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
		{
			_maniacsPackageStatus.Text = $"Could not inspect package: {exception.Message}";
			_maniacsPackageStatus.ForeColor = Color.DarkOrange;
		}
	}

	private void ToggleAdvancedOptions()
	{
		_advancedExpanded = !_advancedExpanded;
		_advancedPanel.Visible = _advancedExpanded;
		_advancedButton.Text = _advancedExpanded ? "Advanced options  ▾" : "Advanced options  ▸";
		_destinationPath.ReadOnly = !_advancedExpanded;
		_browseDestinationButton.Visible = _advancedExpanded;
		RefreshSourceBrowseVisibility(RpgMaker2003InstallationValidator.Validate(_sourcePath.Text.Trim()).IsValid);
		_destinationPath.Size = new Size(_advancedExpanded ? 554 : 660, 28);
		ClientSize = new Size(710, _advancedExpanded ? 690 : 326);
		_installButton.Location = new Point(560, _advancedExpanded ? 624 : 250);
		_operationStatus.Location = new Point(24, _advancedExpanded ? 625 : 254);
		_installationProgress.Location = new Point(24, _advancedExpanded ? 650 : 278);
	}

	private static void ConfigureMovementDefaultCheckBox(CheckBox checkBox, string text, int top)
	{
		checkBox.Text = text;
		checkBox.Location = new Point(12, top);
		checkBox.Size = new Size(252, 24);
		checkBox.Checked = false;
	}

	private void UpdatePixelMovementDependencies()
	{
		var pixelMovement = _pixelMovement.Checked;
		_runWithShift.Enabled = pixelMovement;
		_smoothCamera.Enabled = pixelMovement;
		if (!pixelMovement)
		{
			_runWithShift.Checked = false;
			_smoothCamera.Checked = false;
		}
	}

	private NewProjectDefaults CaptureNewProjectDefaults() => NewProjectDefaults.FromIniOptions(
		_eightDirectionMovement.Checked,
		_pixelMovement.Checked,
		_runWithShift.Checked,
		_smoothCamera.Checked);

	private async Task InstallRecommendedAsync()
	{
		var projectDefaults = CaptureNewProjectDefaults();
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
			_operationStatus.Text = "No files changed. Some selected modules are not available.";
			MessageBox.Show(this, details + Environment.NewLine + Environment.NewLine + "No files have been changed.", "Setup not ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}

		var usesManiacs = plan.OrderedModules.Any(module => module.Descriptor.Id is "maniacs" or "maniacs-english");
		string? maniacsPackageDirectory = null;
		var maniacsLicenseAccepted = false;
		if (usesManiacs)
		{
			if (_maniacsModule is null)
			{
				MessageBox.Show(this, "Maniacs package validation is unavailable in this setup.", "Setup not ready", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}
			maniacsPackageDirectory = SelectedManiacsPackageSource switch
			{
				ManiacsPackageSourceMode.EmbeddedValidated241029 => _embeddedManiacsPackage?.DirectoryPath,
				_ => _maniacsPackagePath.Text.Trim()
			};
			if (string.IsNullOrWhiteSpace(maniacsPackageDirectory))
			{
				MessageBox.Show(this,
					SelectedManiacsPackageSource == ManiacsPackageSourceMode.EmbeddedValidated241029
						? "The embedded Maniacs package is unavailable. Choose an extracted supported package in Advanced options or rebuild Setup with a validated 241029 payload."
						: "Choose an extracted supported Maniacs package in Advanced options before installing.",
					"Select Maniacs package", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			var inspection = _maniacsModule.InspectPackage(maniacsPackageDirectory);
			var requiresArabicCompatibility = plan.OrderedModules.Any(module => module.Descriptor.Id == "arabic-editor");
			if (!inspection.IsKnownSupportedBuild || (requiresArabicCompatibility && !inspection.ArabicEditorValidated) || inspection.PackageRoot is null)
			{
				MessageBox.Show(this, inspection.Summary + (inspection.Issues.Count == 0 ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, inspection.Issues)),
					"Maniacs package needs validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}
			var licensePath = Path.Combine(inspection.PackageRoot, "LICENSE.txt");
			string licenseText;
			try { licenseText = File.ReadAllText(licensePath); }
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				MessageBox.Show(this, "Could not read the license included with the Maniacs package: " + exception.Message, "Maniacs license", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}
			using var licenseDialog = new ManiacsLicenseDialog(licenseText);
			if (licenseDialog.ShowDialog(this) != DialogResult.OK) { return; }
			maniacsPackageDirectory = inspection.PackageRoot;
			maniacsLicenseAccepted = true;
		}

		var destination = _destinationPath.Text.Trim();
		if (string.IsNullOrWhiteSpace(destination))
		{
			MessageBox.Show(this, "Choose the Arabic installation folder shown above.", "Select destination", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}
		var replaceExisting = false;
		if (Directory.Exists(destination))
		{
			var choice = MessageBox.Show(this,
				$"The Arabic installation folder already exists. Setup will stage and verify the new installation first, then replace only this folder's contents. The parent RPG Maker 2003 installation will remain untouched.\n\n{destination}\n\nContinue?",
				"Confirm replacement", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
			if (choice != DialogResult.Yes) { return; }
			replaceExisting = true;
		}

		_installButton.Enabled = false;
		SetInstallationControlsEnabled(false);
		UseWaitCursor = true;
		ControlBox = false;
		_installationProgress.Value = 0;
		_installationProgress.Visible = true;
		_operationStatus.ForeColor = Color.DarkSlateGray;
		_operationStatus.Text = "Preparing installation...";
		try
		{
			var progress = new Progress<InstallationProgress>(update =>
			{
				_operationStatus.Text = update.Stage;
				_operationStatus.ForeColor = update.Failed ? Color.DarkRed : Color.DarkSlateGray;
				_installationProgress.Value = Math.Clamp(update.Percent, 0, 100);
			});
			var result = await new InstallationCoordinator().InstallAsync(source, destination, plan, replaceExisting,
				maniacsPackageDirectory, maniacsLicenseAccepted, progress: progress, projectDefaults: projectDefaults);
			_successfulInstallDirectory = result.InstallationDirectory;
			ShowSuccess();
			_successStatus.Text = "Opening installation folder...";
			await OpenSuccessfulInstallationFolderAsync(closeAfterOpen: true);
		}
		catch (Exception exception)
		{
			var rollbackFailed = exception is SetupInstallException installException && installException.RollbackFailures.Count != 0;
			_operationStatus.Text = rollbackFailed
				? "Setup failed. Rollback reported an error; the vanilla installation was not modified."
				: "Setup failed. Rollback completed; the vanilla installation was not modified.";
			_operationStatus.ForeColor = Color.DarkRed;
			var details = exception.Message;
			if (exception is SetupInstallException { RollbackFailures.Count: > 0 } failedInstall)
			{
				details += Environment.NewLine + Environment.NewLine + "Rollback details:" + Environment.NewLine + string.Join(Environment.NewLine, failedInstall.RollbackFailures);
			}
			MessageBox.Show(this, details, "Setup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
		finally
		{
			UseWaitCursor = false;
			ControlBox = true;
			if (!_successPanel.Visible) { SetInstallationControlsEnabled(true); }
		}
	}

	private void ShowSuccess()
	{
		foreach (Control control in Controls)
		{
			if (!ReferenceEquals(control, _successPanel)) { control.Visible = false; }
		}
		ClientSize = new Size(600, 210);
		_successPanel.Visible = true;
		_successPanel.BringToFront();
	}

	private async Task OpenSuccessfulInstallationFolderAsync(bool closeAfterOpen)
	{
		if (string.IsNullOrWhiteSpace(_successfulInstallDirectory)) { return; }
		try
		{
			if (!Directory.Exists(_successfulInstallDirectory)) { throw new DirectoryNotFoundException("The managed installation folder is missing."); }
			_ = Process.Start(new ProcessStartInfo("explorer.exe")
			{
				UseShellExecute = false,
				ArgumentList = { _successfulInstallDirectory }
			}) ?? throw new InvalidOperationException("Windows Explorer could not open the installation folder.");
			_successStatus.Text = "Opening installation folder...";
			if (closeAfterOpen)
			{
				await Task.Delay(TimeSpan.FromMilliseconds(1500));
				if (!IsDisposed) { Close(); }
			}
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
		{
			_successStatus.Text = "EasyRPG-Arabic is ready, but the installation folder could not be opened: " + exception.Message;
		}
	}

	private void SetInstallationControlsEnabled(bool enabled)
	{
		_sourcePath.Enabled = enabled;
		_browseSourceButton.Enabled = enabled;
		_destinationPath.Enabled = enabled;
		_browseDestinationButton.Enabled = enabled;
		_advancedButton.Enabled = enabled;
		_advancedPanel.Enabled = enabled;
		_installButton.Enabled = enabled;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			try { _embeddedManiacsPackage?.Dispose(); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { /* Temporary package cleanup can be retried by the OS. */ }
		}
		base.Dispose(disposing);
	}

	private sealed record PackageSourceChoice(ManiacsPackageSourceMode Mode, string Label)
	{
		public override string ToString() => Label;
	}
}
