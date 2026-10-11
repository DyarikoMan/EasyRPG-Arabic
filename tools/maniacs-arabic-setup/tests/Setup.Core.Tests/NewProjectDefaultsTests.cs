using EasyRpgArabic.Setup.Core.Modules;

namespace EasyRpgArabic.Setup.Core.Tests;

public sealed class NewProjectDefaultsTests
{
	[Fact]
	public void DefaultsAreAllFalseAndSerializeExplicitly()
	{
		Assert.Equal(new NewProjectDefaults(false, false, false, false), NewProjectDefaults.AllFalse);
		Assert.Equal(
			"[EasyRPG]\r\nEightDirectionMovement=false\r\nPixelMovement=false\r\nRunWithShift=false\r\nSmoothCamera=false\r\n",
			System.Text.Encoding.UTF8.GetString(NewProjectDefaults.AllFalse.ToIniBytes()));
	}

	[Fact]
	public void EightDirectionMovementCanBeEnabledWithoutPixelMovement()
	{
		var defaults = NewProjectDefaults.FromIniOptions(true, false, false, false);

		Assert.True(defaults.EightDirectionMovement);
		Assert.False(defaults.PixelMovement);
		Assert.False(defaults.RunWithShift);
		Assert.False(defaults.SmoothCamera);
	}

	[Fact]
	public void PixelMovementCanBeEnabledWithoutEightDirectionMovement()
	{
		var defaults = NewProjectDefaults.FromIniOptions(false, true, true, true);

		Assert.False(defaults.EightDirectionMovement);
		Assert.True(defaults.PixelMovement);
		Assert.True(defaults.RunWithShift);
		Assert.True(defaults.SmoothCamera);
	}

	[Fact]
	public void SelectedMovementCombinationSerializesTheChosenValues()
	{
		var defaults = NewProjectDefaults.FromIniOptions(false, true, true, false);
		var ini = System.Text.Encoding.UTF8.GetString(defaults.ToIniBytes());

		Assert.Equal(
			"[EasyRPG]\r\nEightDirectionMovement=false\r\nPixelMovement=true\r\nRunWithShift=true\r\nSmoothCamera=false\r\n",
			ini);
	}

	[Fact]
	public void RunAndSmoothCameraAreClearedWhenPixelMovementTurnsOff()
	{
		var pixelSettings = NewProjectDefaults.FromIniOptions(true, true, true, true);
		var defaults = NewProjectDefaults.FromIniOptions(
			pixelSettings.EightDirectionMovement,
			false,
			pixelSettings.RunWithShift,
			pixelSettings.SmoothCamera);

		Assert.True(defaults.EightDirectionMovement);
		Assert.False(defaults.PixelMovement);
		Assert.False(defaults.RunWithShift);
		Assert.False(defaults.SmoothCamera);
	}
}
