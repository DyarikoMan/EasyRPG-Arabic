#include "player.h"
#include "doctest.h"
#include "filefinder.h"
#include "font.h"
#include "utils.h"
#include <utility>
#include <sstream>

#include <lcf/inireader.h>

#ifdef _WIN32
#include <windows.h>
#endif

TEST_SUITE_BEGIN("Player");

TEST_CASE("Project movement settings default to false when keys are absent") {
	std::istringstream stream("[EasyRPG]\n");
	lcf::INIReader ini(stream);

	const auto settings = Player::ParseProjectSettings(ini);
	CHECK_FALSE(settings.eight_direction_movement);
	CHECK_FALSE(settings.pixel_movement);
	CHECK_FALSE(settings.run_with_shift);
}

TEST_CASE("Project movement settings accept explicit false values") {
	std::istringstream stream(
		"[EasyRPG]\n"
		"EightDirectionMovement=false\n"
		"PixelMovement=false\n"
		"RunWithShift=false\n");
	lcf::INIReader ini(stream);

	const auto settings = Player::ParseProjectSettings(ini);
	CHECK_FALSE(settings.eight_direction_movement);
	CHECK_FALSE(settings.pixel_movement);
	CHECK_FALSE(settings.run_with_shift);
}

TEST_CASE("Project movement settings read opt-in values from the EasyRPG section") {
	std::istringstream stream(
		"[RPG_RT]\nEightDirectionMovement=true\nPixelMovement=true\nRunWithShift=true\n"
		"[EasyRPG]\nEightDirectionMovement=true\nPixelMovement=true\nRunWithShift=true\n");
	lcf::INIReader ini(stream);

	const auto settings = Player::ParseProjectSettings(ini);
	CHECK(settings.eight_direction_movement);
	CHECK(settings.pixel_movement);
	CHECK(settings.run_with_shift);
}

TEST_CASE("CP1256 encoding aliases are recognized for Arabic font fallback") {
	const auto original_encoding = Player::encoding;

	for (const auto encoding : {"1256", "CP1256", "windows-1256", "IBM-1256_P100-1995", "x-cp1256"}) {
		Player::encoding = encoding;
		CHECK(Player::IsCP1256());
	}

	for (const auto encoding : {"1252", "windows-1251", "UTF-8"}) {
		Player::encoding = encoding;
		CHECK_FALSE(Player::IsCP1256());
	}

	Player::encoding = original_encoding;
}

#if defined(_WIN32) && defined(HAVE_FREETYPE)
TEST_CASE("Windows system font fallback contains the Arabic Show Text glyphs") {
	wchar_t windows_dir[MAX_PATH + 1] = {};
	const UINT windows_dir_len = GetWindowsDirectoryW(windows_dir, MAX_PATH);
	REQUIRE(windows_dir_len > 0);
	REQUIRE(windows_dir_len < MAX_PATH);

	const auto windows_path = Utils::FromWideString(std::wstring(windows_dir, windows_dir_len));
	const auto fonts_path = FileFinder::MakePath(windows_path, "Fonts");
	FontRef font;
	for (const auto font_name : {"arial.ttf", "tahoma.ttf", "segoeui.ttf"}) {
		const auto font_path = FileFinder::MakePath(fonts_path, font_name);
		auto stream = FileFinder::Root().OpenInputStream(font_path);
		if (stream) {
			font = Font::CreateFtFont(std::move(stream), 12, false, false);
			if (font) {
				break;
			}
		}
	}

	REQUIRE(font != nullptr);
	font->SetFallbackFont(nullptr);
	for (const auto codepoint : {U'ا', U'ل', U'س', U'م'}) {
		CHECK(font->GetSize(codepoint).width > 0);
	}
#ifdef HAVE_HARFBUZZ
	CHECK(font->CanShape());
	CHECK_FALSE(font->Shape(U"سلام", Font::ShapeDirection::RTL).empty());
#endif
}
#endif

TEST_SUITE_END();
