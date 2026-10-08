#include "scene_name.h"
#include "doctest.h"

#include <algorithm>
#include <sstream>

#include <lcf/inireader.h>

TEST_SUITE_BEGIN("Scene_Name");

TEST_CASE("Arabic page sequence starts with Arabic and cycles directly to Symbol") {
	const auto pages = BuildSceneNameKeyboardPages(true, 2, {});
	REQUIRE_EQ(pages.initial_index, 0);
	REQUIRE_EQ(pages.modes.size(), 2);
	CHECK((pages.modes == std::vector<Window_Keyboard::Mode>{
		Window_Keyboard::Arabic, Window_Keyboard::Symbol
	}));
	CHECK(pages.modes[pages.initial_index] == Window_Keyboard::Arabic);
	CHECK(pages.modes[(pages.initial_index + 1) % pages.modes.size()] == Window_Keyboard::Symbol);
	CHECK(pages.modes[(pages.initial_index + 2) % pages.modes.size()] == Window_Keyboard::Arabic);
	CHECK(std::find(pages.modes.begin(), pages.modes.end(), Window_Keyboard::Letter) == pages.modes.end());
}

TEST_CASE("Non-Arabic page sequence preserves charset selection and Letter/Symbol pages") {
	const auto pages = BuildSceneNameKeyboardPages(false, 1, {
		Window_Keyboard::Hiragana, Window_Keyboard::Katakana
	});
	CHECK((pages.modes == std::vector<Window_Keyboard::Mode>{
		Window_Keyboard::Hiragana, Window_Keyboard::Katakana, Window_Keyboard::Letter, Window_Keyboard::Symbol
	}));
	CHECK(pages.initial_index == 1);
}

TEST_CASE("Name Input label defaults and RPG_RT.ini overrides are resolved by session") {
	const std::string arabic_ini =
		"[RPG_RT]\nNameInputSpace=فراغ\nNameInputDone=مزيان\n";
	std::istringstream arabic_stream(arabic_ini);
	lcf::INIReader arabic_reader(arabic_stream);
	const auto arabic = ResolveSceneNameKeyboardLabels(true, "<Done>", "65001", &arabic_reader);
	CHECK(arabic.space == "فراغ");
	CHECK(arabic.done == "مزيان");

	const std::string space_only_ini = "[RPG_RT]\nNameInputSpace=فراغ\n";
	std::istringstream space_stream(space_only_ini);
	lcf::INIReader space_reader(space_stream);
	const auto space_only = ResolveSceneNameKeyboardLabels(true, "<Done>", "65001", &space_reader);
	CHECK(space_only.space == "فراغ");
	CHECK(space_only.done == "صافي");

	const std::string done_only_ini = "[RPG_RT]\nNameInputDone=مزيان\n";
	std::istringstream done_stream(done_only_ini);
	lcf::INIReader done_reader(done_stream);
	const auto done_only = ResolveSceneNameKeyboardLabels(true, "<Done>", "65001", &done_reader);
	CHECK(done_only.space == "مسافة");
	CHECK(done_only.done == "مزيان");

	const auto arabic_without_ini = ResolveSceneNameKeyboardLabels(true, "<Done>", "65001", nullptr);
	CHECK(arabic_without_ini.space == "مسافة");
	CHECK(arabic_without_ini.done == "صافي");

	const auto non_arabic_without_ini = ResolveSceneNameKeyboardLabels(false, "<OK>", "65001", nullptr);
	CHECK(non_arabic_without_ini.space == Window_Keyboard::SPACE);
	CHECK(non_arabic_without_ini.done == "<OK>");

	const std::string non_arabic_ini = "[RPG_RT]\nNameInputSpace=Spacebar\nNameInputDone=Finish\n";
	std::istringstream non_arabic_stream(non_arabic_ini);
	lcf::INIReader non_arabic_reader(non_arabic_stream);
	const auto non_arabic = ResolveSceneNameKeyboardLabels(false, "<OK>", "65001", &non_arabic_reader);
	CHECK(non_arabic.space == "Spacebar");
	CHECK(non_arabic.done == "Finish");
}

TEST_CASE("Name Input label overrides use the RPG_RT section and ignore empty values") {
	const std::string other_section_ini = "[EasyRPG]\nNameInputSpace=Wrong\nNameInputDone=Wrong\n";
	std::istringstream other_section_stream(other_section_ini);
	lcf::INIReader other_section_reader(other_section_stream);
	const auto other_section = ResolveSceneNameKeyboardLabels(true, "<Done>", "65001", &other_section_reader);
	CHECK(other_section.space == "مسافة");
	CHECK(other_section.done == "صافي");

	const std::string empty_ini = "[RPG_RT]\nNameInputSpace=\nNameInputDone=\n";
	std::istringstream empty_stream(empty_ini);
	lcf::INIReader empty_reader(empty_stream);
	const auto empty = ResolveSceneNameKeyboardLabels(true, "<Done>", "65001", &empty_reader);
	CHECK(empty.space == "مسافة");
	CHECK(empty.done == "صافي");

	// The TEST project's RPG_RT.ini has no Encoding entry, so the Arabic fork's
	// normal fallback code page (1256) is used for its Arabic label bytes.
	const std::string cp1256_ini = "[RPG_RT]\nNameInputSpace=\xE3\xD3\xC7\xDD\xC9\nNameInputDone=\xD5\xC7\xDD\xED\n";
	std::istringstream cp1256_stream(cp1256_ini);
	lcf::INIReader cp1256_reader(cp1256_stream);
	const auto cp1256 = ResolveSceneNameKeyboardLabels(true, "<Done>", "1256", &cp1256_reader);
	CHECK(cp1256.space == "مسافة");
	CHECK(cp1256.done == "صافي");
}

TEST_CASE("Name scene keeps the existing LTR upper-window geometry") {
	constexpr int menu_x = 100;
	constexpr int menu_y = 50;
	const auto geometry = GetSceneNameWindowGeometry(false, menu_x, menu_y);

	CHECK(geometry.face_x == menu_x + 32);
	CHECK(geometry.name_x == geometry.face_x + 64);
	CHECK(geometry.face_y == menu_y + 8);
	CHECK(geometry.name_y == menu_y + 40);
	CHECK(geometry.face_width == 64);
	CHECK(geometry.face_height == 64);
	CHECK(geometry.name_width == 192);
	CHECK(geometry.name_height == 32);
	CHECK(geometry.keyboard_x == menu_x + 32);
	CHECK(geometry.keyboard_y == menu_y + 72);
	CHECK(geometry.keyboard_width == 256);
	CHECK(geometry.keyboard_height == 160);
}

TEST_CASE("Name scene mirrors the upper-window group for RTL sessions") {
	constexpr int menu_x = 100;
	constexpr int menu_y = 50;
	const auto ltr = GetSceneNameWindowGeometry(false, menu_x, menu_y);
	const auto rtl = GetSceneNameWindowGeometry(true, menu_x, menu_y);

	CHECK(rtl.name_x == menu_x + 32);
	CHECK(rtl.face_x == rtl.name_x + rtl.name_width);
	CHECK(rtl.name_x < rtl.face_x);

	const int ltr_group_left = std::min(ltr.face_x, ltr.name_x);
	const int rtl_group_left = std::min(rtl.face_x, rtl.name_x);
	const int ltr_group_right = std::max(ltr.face_x + ltr.face_width,
			ltr.name_x + ltr.name_width);
	const int rtl_group_right = std::max(rtl.face_x + rtl.face_width,
			rtl.name_x + rtl.name_width);
	CHECK(ltr_group_left == rtl_group_left);
	CHECK(ltr_group_left == menu_x + 32);
	CHECK(ltr_group_right == rtl_group_right);
	CHECK(ltr_group_right - ltr_group_left == 256);
	CHECK(rtl_group_right - rtl_group_left == 256);
}

TEST_CASE("RTL name scene preserves vertical positions, sizes, and keyboard placement") {
	const auto ltr = GetSceneNameWindowGeometry(false, 100, 50);
	const auto rtl = GetSceneNameWindowGeometry(true, 100, 50);

	CHECK(rtl.face_y == ltr.face_y);
	CHECK(rtl.name_y == ltr.name_y);
	CHECK(rtl.keyboard_y == ltr.keyboard_y);
	CHECK(rtl.face_width == ltr.face_width);
	CHECK(rtl.face_height == ltr.face_height);
	CHECK(rtl.name_width == ltr.name_width);
	CHECK(rtl.name_height == ltr.name_height);
	CHECK(rtl.keyboard_x == ltr.keyboard_x);
	CHECK(rtl.keyboard_width == ltr.keyboard_width);
	CHECK(rtl.keyboard_height == ltr.keyboard_height);
}

TEST_SUITE_END();
