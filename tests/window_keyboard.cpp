#include "window_keyboard.h"
#include "doctest.h"
#include "mock_game.h"

#include <algorithm>
#include <string>
#include <vector>

TEST_SUITE_BEGIN("Window_Keyboard");

TEST_CASE("Arabic layout contains the requested character set") {
	const auto& layout = Window_Keyboard::layouts[Window_Keyboard::Arabic];
	std::vector<std::string> keys;
	for (const auto& row : layout.items) {
		for (const auto& key : row) {
			keys.push_back(key);
		}
	}

	const std::vector<std::string> required = {
		"ا", "ب", "ت", "ث", "ج", "ح", "خ", "د", "ذ", "ر", "ز", "س", "ش", "ص",
		"ض", "ط", "ظ", "ع", "غ", "ف", "ق", "ك", "ل", "م", "ن", "ه", "و", "ي",
		"ء", "أ", "إ", "آ", "ؤ", "ئ", "ة", "ى", "ـ", "َ", "ً", "ُ", "ٌ", "ِ", "ٍ", "ّ", "ْ"
	};
	for (const auto& key : required) {
		CHECK(std::find(keys.begin(), keys.end(), key) != keys.end());
	}

	const std::vector<std::string> excluded = {"گ", "ڤ", "پ", "چ", "ژ", "ٰ", "٠", "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩"};
	for (const auto& key : excluded) {
		CHECK(std::find(keys.begin(), keys.end(), key) == keys.end());
	}

	const std::vector<std::string> digits = {"1", "2", "3", "4", "5", "6", "7", "8", "9", "0"};
	for (size_t i = 0; i < digits.size(); ++i) {
		CHECK(layout.items[5][i] == digits[i]);
	}
	CHECK(layout.items[7][0] == Window_Keyboard::SPACE);
	CHECK(layout.items[8][6] == Window_Keyboard::NEXT_PAGE);
	CHECK(layout.items[8][8] == Window_Keyboard::DONE);
}

TEST_CASE("Arabic keyboard mirrors logical columns and wide control anchors") {
	using Keyboard = Window_Keyboard;

	CHECK(Keyboard::IsRtlLayout(Keyboard::Arabic));
	CHECK(Keyboard::LogicalToVisualColumn(Keyboard::Arabic, 0) == 9);
	CHECK(Keyboard::LogicalToVisualColumn(Keyboard::Arabic, 9) == 0);
	CHECK(Keyboard::GetItemStartVisualColumn(Keyboard::Arabic, 0, 0) == 9);
	CHECK(Keyboard::layouts[Keyboard::Arabic].items[0][0] == "ا");

	// Logical controls occupy two adjacent cells. The visual anchor is the
	// left edge of the mirrored span so their labels and cursors stay on-screen.
	CHECK(Keyboard::GetItemStartVisualColumn(Keyboard::Arabic, 7, 0) == 8);
	CHECK(Keyboard::GetItemStartVisualColumn(Keyboard::Arabic, 8, 6) == 2);
	CHECK(Keyboard::GetItemStartVisualColumn(Keyboard::Arabic, 8, 8) == 0);
	CHECK(Keyboard::GetItemStartVisualColumn(Keyboard::Arabic, 8, 6) + 1 ==
		Keyboard::LogicalToVisualColumn(Keyboard::Arabic, 6));
	CHECK(Keyboard::GetItemStartVisualColumn(Keyboard::Arabic, 8, 8) + 1 ==
		Keyboard::LogicalToVisualColumn(Keyboard::Arabic, 8));
}

TEST_CASE("Window_Keyboard starts Arabic at alef on the visual top-right") {
	MockGame game(MockMap::eNone);
	Window_Keyboard keyboard(0, 0);
	keyboard.SetMode(Window_Keyboard::Arabic, Window_Keyboard::Symbol);

	CHECK(keyboard.GetSelected() == "ا");
	CHECK(keyboard.GetKey(8, 6) == Window_Keyboard::layouts[Window_Keyboard::Symbol].key_text);
	CHECK(keyboard.GetCursorRect().x == keyboard.GetItemRect(0, 0).x);
	CHECK(keyboard.GetCursorRect().y == keyboard.GetItemRect(0, 0).y - 2);
	CHECK(keyboard.GetItemRect(0, 0).x > keyboard.GetItemRect(0, 9).x);
	const Rect space_rect = keyboard.GetItemRect(7, 0);
	const Rect page_rect = keyboard.GetItemRect(8, 6);
	const Rect done_rect = keyboard.GetItemRect(8, 8);
	CHECK(done_rect.x < page_rect.x);
	CHECK(page_rect.x < space_rect.x);
	CHECK(space_rect.x + space_rect.width <= 304);
	CHECK(page_rect.x + page_rect.width <= 304);
	CHECK(done_rect.x + done_rect.width <= 304);

	// Selection indices remain intact as the mode changes; only the Arabic
	// page mirrors the item/cursor geometry.
	keyboard.SetMode(Window_Keyboard::Letter, Window_Keyboard::Symbol);
	CHECK(keyboard.GetSelected() == "A");
	CHECK(keyboard.GetItemRect(0, 0).x < keyboard.GetItemRect(0, 9).x);
	keyboard.SetMode(Window_Keyboard::Symbol, Window_Keyboard::Arabic);
	CHECK(keyboard.GetSelected() == "$A");
	CHECK(keyboard.GetKey(8, 6) == Window_Keyboard::layouts[Window_Keyboard::Arabic].key_text);
	CHECK(keyboard.GetItemRect(0, 0).x < keyboard.GetItemRect(0, 9).x);
	keyboard.SetMode(Window_Keyboard::Arabic, Window_Keyboard::Symbol);
	CHECK(keyboard.GetSelected() == "ا");
	CHECK(keyboard.GetKey(8, 6) == Window_Keyboard::layouts[Window_Keyboard::Symbol].key_text);
	CHECK(keyboard.GetCursorRect().x == keyboard.GetItemRect(0, 0).x);
}

TEST_CASE("Arabic name input displays Arabic labels and preserves control sentinels") {
	MockGame game(MockMap::eNone);
	Window_Keyboard keyboard(0, 0);

	keyboard.SetMode(Window_Keyboard::Arabic, Window_Keyboard::Symbol);
	CHECK(keyboard.GetSelected() == "ا");
	CHECK(keyboard.GetKey(7, 0) == "مسافة");
	CHECK(keyboard.GetKey(8, 8) == "تم");
	CHECK(keyboard.GetKey(8, 6) == Window_Keyboard::layouts[Window_Keyboard::Symbol].key_text);
	CHECK(Window_Keyboard::layouts[Window_Keyboard::Arabic].items[7][0] == Window_Keyboard::SPACE);
	CHECK(Window_Keyboard::layouts[Window_Keyboard::Arabic].items[8][8] == Window_Keyboard::DONE);

	keyboard.SetMode(Window_Keyboard::Symbol, Window_Keyboard::Arabic);
	CHECK(keyboard.GetKey(8, 8) == "تم");
	CHECK(keyboard.GetKey(8, 6) == Window_Keyboard::layouts[Window_Keyboard::Arabic].key_text);
	CHECK(Window_Keyboard::layouts[Window_Keyboard::Symbol].items[8][8] == Window_Keyboard::DONE);

	keyboard.SetMode(Window_Keyboard::Letter, Window_Keyboard::Symbol);
	CHECK(keyboard.GetKey(7, 0) == Window_Keyboard::SPACE);
	CHECK(keyboard.GetKey(8, 8) == Window_Keyboard::DONE);
	keyboard.SetMode(Window_Keyboard::Symbol, Window_Keyboard::Letter);
	CHECK(keyboard.GetKey(8, 8) == Window_Keyboard::DONE);
}

TEST_CASE("Arabic keyboard navigation follows visual directions and skips empty cells") {
	using Keyboard = Window_Keyboard;

	// Initial logical selection (0, 0) is Arabic alef, at the visual right edge.
	auto selection = Keyboard::GetNextSelection(Keyboard::Arabic, 0, 0, 0, 0);
	CHECK(selection.first == 0);
	CHECK(selection.second == 0);
	CHECK(Keyboard::layouts[Keyboard::Arabic].items[selection.first][selection.second] == "ا");
	CHECK(Keyboard::LogicalToVisualColumn(Keyboard::Arabic, selection.second) == 9);

	// LEFT moves to the next logical key; RIGHT returns visually to the right.
	selection = Keyboard::GetNextSelection(Keyboard::Arabic, 0, 0, 0, -1);
	CHECK(selection.first == 0);
	CHECK(selection.second == 1);
	CHECK(Keyboard::layouts[Keyboard::Arabic].items[selection.first][selection.second] == "ب");
	selection = Keyboard::GetNextSelection(Keyboard::Arabic, selection.first, selection.second, 0, 1);
	CHECK(selection.first == 0);
	CHECK(selection.second == 0);

	// Horizontal wrapping follows the visual edge of the RTL keyboard.
	selection = Keyboard::GetNextSelection(Keyboard::Arabic, 0, 0, 0, 1);
	CHECK(selection.first == 0);
	CHECK(selection.second == 9);
	CHECK(Keyboard::layouts[Keyboard::Arabic].items[selection.first][selection.second] == "ر");
	selection = Keyboard::GetNextSelection(Keyboard::Arabic, 0, 9, 0, -1);
	CHECK(selection.first == 0);
	CHECK(selection.second == 0);

	// Vertical movement is unchanged and bypasses holes in sparse rows.
	selection = Keyboard::GetNextSelection(Keyboard::Arabic, 2, 2, 1, 0);
	CHECK(selection.first == 3);
	CHECK(selection.second == 2);
	CHECK(Keyboard::layouts[Keyboard::Arabic].items[selection.first][selection.second] == "ؤ");
	selection = Keyboard::GetNextSelection(Keyboard::Arabic, 3, 2, -1, 0);
	CHECK(selection.first == 2);
	CHECK(selection.second == 2);
	selection = Keyboard::GetNextSelection(Keyboard::Arabic, 4, 4, 0, -1);
	CHECK(selection.first == 4);
	CHECK(selection.second == 0);
	selection = Keyboard::GetNextSelection(Keyboard::Arabic, 4, 0, 0, 1);
	CHECK(selection.first == 4);
	CHECK(selection.second == 4);
	selection = Keyboard::GetNextSelection(Keyboard::Arabic, 5, 1, 1, 0);
	CHECK(selection == std::make_pair(7, 0)); // vertical entry into SPACE's second cell

	// Moving across either mirrored two-cell page control reaches the other
	// sentinel without selecting its empty continuation cell.
	selection = Keyboard::GetNextSelection(Keyboard::Arabic, 8, 6, 0, -1);
	CHECK(selection == std::make_pair(8, 8));
	selection = Keyboard::GetNextSelection(Keyboard::Arabic, 8, 8, 0, 1);
	CHECK(selection == std::make_pair(8, 6));
	CHECK(Keyboard::GetNextSelection(Keyboard::Arabic, 7, 0, 0, -1) == std::make_pair(7, 0));
	CHECK(Keyboard::GetNextSelection(Keyboard::Arabic, 7, 0, 0, 1) == std::make_pair(7, 0));
}

TEST_CASE("Symbol keyboard follows RTL session geometry and navigation without mirroring icons") {
	using Keyboard = Window_Keyboard;
	MockGame game(MockMap::eNone);

	CHECK(Keyboard::IsRtlLayout(Keyboard::Symbol, true));
	CHECK(Keyboard::LogicalToVisualColumn(Keyboard::Symbol, 0, true) == 9);
	CHECK(Keyboard::GetItemStartVisualColumn(Keyboard::Symbol, 0, 0, true) == 9);
	CHECK(Keyboard::GetItemStartVisualColumn(Keyboard::Symbol, 8, 6, true) == 2);
	CHECK(Keyboard::GetItemStartVisualColumn(Keyboard::Symbol, 8, 8, true) == 0);

	// Symbol tokens and page content remain in their original logical order.
	CHECK(Keyboard::layouts[Keyboard::Symbol].items[0][0] == "$A");
	CHECK(Keyboard::layouts[Keyboard::Symbol].items[0][1] == "$B");
	CHECK(Keyboard::layouts[Keyboard::Symbol].items[8][6] == Keyboard::NEXT_PAGE);
	CHECK(Keyboard::layouts[Keyboard::Symbol].items[8][8] == Keyboard::DONE);

	Keyboard rtl_keyboard(0, 0, 320, 80, Keyboard::DONE, true);
	rtl_keyboard.SetMode(Keyboard::Arabic, Keyboard::Symbol);
	CHECK(rtl_keyboard.GetSelected() == "ا");
	rtl_keyboard.SetMode(Keyboard::Symbol, Keyboard::Arabic);
	CHECK(rtl_keyboard.GetSelected() == "$A");
	CHECK(rtl_keyboard.GetKey(8, 6) == Keyboard::layouts[Keyboard::Arabic].key_text);
	CHECK(rtl_keyboard.GetItemRect(0, 0).x > rtl_keyboard.GetItemRect(0, 9).x);
	const Rect page_rect = rtl_keyboard.GetItemRect(8, 6);
	const Rect done_rect = rtl_keyboard.GetItemRect(8, 8);
	CHECK(done_rect.x < page_rect.x);
	CHECK(page_rect.x < rtl_keyboard.GetItemRect(0, 0).x);
	CHECK(done_rect.x < rtl_keyboard.GetContents()->GetWidth() / 2);
	CHECK(page_rect.x < rtl_keyboard.GetContents()->GetWidth() / 2);

	// LEFT advances visually left, RIGHT moves back right, and wrapping skips holes.
	auto selection = Keyboard::GetNextSelection(Keyboard::Symbol, 0, 0, 0, -1, true);
	CHECK(selection == std::make_pair(0, 1)); // $A -> $B
	selection = Keyboard::GetNextSelection(Keyboard::Symbol, selection.first, selection.second,
			0, 1, true);
	CHECK(selection == std::make_pair(0, 0));
	selection = Keyboard::GetNextSelection(Keyboard::Symbol, 0, 0, 0, 1, true);
	CHECK(selection == std::make_pair(0, 9)); // Wrap to the visual left edge.
	selection = Keyboard::GetNextSelection(Keyboard::Symbol, 0, 9, 0, -1, true);
	CHECK(selection == std::make_pair(0, 0));
	selection = Keyboard::GetNextSelection(Keyboard::Symbol, 5, 0, 0, -1, true);
	CHECK(selection == std::make_pair(5, 5)); // Empty cells 1-4 are skipped.
	selection = Keyboard::GetNextSelection(Keyboard::Symbol, 8, 6, 0, -1, true);
	CHECK(selection == std::make_pair(8, 8)); // Page -> Done.
	selection = Keyboard::GetNextSelection(Keyboard::Symbol, 8, 8, 0, 1, true);
	CHECK(selection == std::make_pair(8, 6)); // Done -> Page.

	// The same Symbol mode remains LTR when the owning session is non-RTL.
	Keyboard ltr_keyboard(0, 0);
	ltr_keyboard.SetMode(Keyboard::Letter, Keyboard::Symbol);
	CHECK_FALSE(Keyboard::IsRtlLayout(Keyboard::Symbol));
	ltr_keyboard.SetMode(Keyboard::Symbol, Keyboard::Letter);
	CHECK(ltr_keyboard.GetSelected() == "$A");
	CHECK(ltr_keyboard.GetItemRect(0, 0).x < ltr_keyboard.GetItemRect(0, 9).x);
	CHECK(ltr_keyboard.GetItemRect(8, 6).x < ltr_keyboard.GetItemRect(8, 8).x);
	CHECK(Keyboard::GetNextSelection(Keyboard::Symbol, 0, 0, 0, 1) == std::make_pair(0, 1));
}

TEST_CASE("Arabic digits retain logical Western order and mirror visually") {
	using Keyboard = Window_Keyboard;
	const auto& layout = Keyboard::layouts[Keyboard::Arabic];
	for (int logical_col = 0; logical_col < Keyboard::col_max; ++logical_col) {
		CHECK(layout.items[5][logical_col] == std::to_string(logical_col == 9 ? 0 : logical_col + 1));
		CHECK(Keyboard::LogicalToVisualColumn(Keyboard::Arabic, logical_col) == Keyboard::col_max - 1 - logical_col);
	}
}

TEST_CASE("Non-Arabic keyboard geometry and navigation stay left-to-right") {
	using Keyboard = Window_Keyboard;
	const std::vector<Keyboard::Mode> ltr_modes = {
		Keyboard::Hiragana, Keyboard::Katakana, Keyboard::Hangul1, Keyboard::Hangul2,
		Keyboard::ZhCn1, Keyboard::ZhCn2, Keyboard::ZhTw1, Keyboard::ZhTw2,
		Keyboard::RuCyrl, Keyboard::Letter, Keyboard::Symbol
	};
	for (const auto mode : ltr_modes) {
		CHECK_FALSE(Keyboard::IsRtlLayout(mode));
		CHECK(Keyboard::LogicalToVisualColumn(mode, 0) == 0);
		CHECK(Keyboard::LogicalToVisualColumn(mode, 9) == 9);
		CHECK(Keyboard::GetNextSelection(mode, 0, 0, 0, 1) == std::make_pair(0, 1));
		CHECK(Keyboard::GetNextSelection(mode, 0, 1, 0, -1) == std::make_pair(0, 0));
		CHECK(Keyboard::GetNextSelection(mode, 8, 6, 0, 1) == std::make_pair(8, 8));
		CHECK(Keyboard::GetNextSelection(mode, 8, 8, 0, -1) == std::make_pair(8, 6));
	}
	CHECK(Keyboard::GetNextSelection(Keyboard::Letter, 6, 1, 1, 0) == std::make_pair(7, 0));
	CHECK(Keyboard::GetNextSelection(Keyboard::Hangul1, 7, 7, 1, 0) == std::make_pair(8, 6));
	CHECK(Keyboard::GetNextSelection(Keyboard::Hangul1, 7, 9, 1, 0) == std::make_pair(8, 8));

	// Switching between keyboard modes changes their geometry/direction while
	// preserving the selected logical cell.
	CHECK(Keyboard::GetItemStartVisualColumn(Keyboard::Letter, 8, 6) == 6);
	CHECK(Keyboard::GetItemStartVisualColumn(Keyboard::Symbol, 8, 6) == 6);
	CHECK(Keyboard::GetItemStartVisualColumn(Keyboard::Arabic, 8, 6) == 2);
	CHECK(Keyboard::GetNextSelection(Keyboard::Letter, 0, 0, 0, 1) == std::make_pair(0, 1));
	CHECK(Keyboard::GetNextSelection(Keyboard::Symbol, 0, 0, 0, 1) == std::make_pair(0, 1));
	CHECK(Keyboard::GetNextSelection(Keyboard::Arabic, 0, 0, 0, -1) == std::make_pair(0, 1));
}

TEST_SUITE_END();
