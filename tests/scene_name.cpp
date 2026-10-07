#include "scene_name.h"
#include "doctest.h"

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

TEST_SUITE_END();
