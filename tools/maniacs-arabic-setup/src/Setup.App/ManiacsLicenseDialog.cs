namespace EasyRpgArabic.Setup.App;

internal sealed class ManiacsLicenseDialog : Form
{
	private readonly CheckBox _accept = new();
	private readonly Button _continue = new();

	public ManiacsLicenseDialog(string licenseText)
	{
		Text = "Maniacs license";
		StartPosition = FormStartPosition.CenterParent;
		FormBorderStyle = FormBorderStyle.FixedDialog;
		MinimizeBox = false;
		MaximizeBox = false;
		ShowInTaskbar = false;
		ClientSize = new Size(620, 490);
		Font = new Font("Segoe UI", 9F);

		Controls.Add(new Label
		{
			Text = "Review the license included with the official Maniacs package.",
			Location = new Point(18, 16),
			Size = new Size(580, 24)
		});
		var license = new RichTextBox
		{
			Location = new Point(18, 48),
			Size = new Size(580, 340),
			ReadOnly = true,
			DetectUrls = true,
			Text = licenseText,
			ScrollBars = RichTextBoxScrollBars.Vertical
		};
		Controls.Add(license);
		var officialLink = new LinkLabel
		{
			Text = "Open the official RPG Maker 2003 / Maniacs information page",
			Location = new Point(18, 397),
			Size = new Size(580, 22),
			AutoSize = false
		};
		officialLink.LinkClicked += (_, _) =>
		{
			try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://steamcommunity.com/app/362870/discussions/0/541906348059148217/") { UseShellExecute = true }); }
			catch (System.ComponentModel.Win32Exception) { }
		};
		Controls.Add(officialLink);
		_accept.Text = "I have read and accept the license included with this Maniacs package.";
		_accept.Location = new Point(18, 429);
		_accept.Size = new Size(450, 24);
		_accept.CheckedChanged += (_, _) => _continue.Enabled = _accept.Checked;
		Controls.Add(_accept);
		var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(414, 457), Size = new Size(86, 28) };
		_continue.Text = "Continue";
		_continue.Enabled = false;
		_continue.Location = new Point(512, 457);
		_continue.Size = new Size(86, 28);
		_continue.DialogResult = DialogResult.OK;
		Controls.Add(cancel);
		Controls.Add(_continue);
		AcceptButton = _continue;
		CancelButton = cancel;
	}
}
