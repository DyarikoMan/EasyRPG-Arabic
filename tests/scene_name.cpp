#include "scene_name.h"
#include "doctest.h"

#include <algorithm>

TEST_SUITE_BEGIN("Scene_Name");

TEST_CASE("Arabic page sequence starts with Arabic and cycles to Letter and Symbol") {
	const auto pages = BuildSceneNameKeyboardPages(true, 2, {});
	REQUIRE_EQ(pages.initial_index, 0);
	REQUIRE_EQ(pages.modes.size(), 3);
	CHECK(pages.modes[pages.initial_index] == Window_Keyboard::Arabic);
	CHECK(pages.modes[(pages.initial_index + 1) % pages.modes.size()] == Window_Keyboard::Letter);
	CHECK(pages.modes[(pages.initial_index + 2) % pages.modes.size()] == Window_Keyboard::Symbol);
	CHECK(pages.modes[(pages.initial_index + 3) % pages.modes.size()] == Window_Keyboard::Arabic);
}

TEST_CASE("Non-Arabic page sequence preserves charset selection") {
	const auto pages = BuildSceneNameKeyboardPages(false, 1, {
		Window_Keyboard::Hiragana, Window_Keyboard::Katakana
	});
	CHECK((pages.modes == std::vector<Window_Keyboard::Mode>{
		Window_Keyboard::Hiragana, Window_Keyboard::Katakana, Window_Keyboard::Letter, Window_Keyboard::Symbol
	}));
	CHECK(pages.initial_index == 1);
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
