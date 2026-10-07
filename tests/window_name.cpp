#include "window_name.h"
#include "window_keyboard.h"
#include "doctest.h"
#include "mock_game.h"

#include "font.h"
#include "text.h"

#include <string>
#include <utility>

TEST_SUITE_BEGIN("Window_Name");

TEST_CASE("LTR name alignment and caret behavior are unchanged") {
	MockGame game(MockMap::eNone);
	Window_Name name(0, 0, 192, 32, false);
	const auto draw_layout = Window_Name::GetTextDrawLayout(false, name.GetContents()->GetWidth());

	CHECK_FALSE(name.IsRtlLayout());
	CHECK(draw_layout.anchor_x == 2);
	CHECK_FALSE(draw_layout.align_right);
	name.Update();
	CHECK(name.GetCursorRect().x == 2);

	name.Append("A");
	name.Update();
	const int shaped_width = Text::GetSize(*Font::Default(), name.Get()).width;
	CHECK(name.GetCursorRect().x == shaped_width + 2);
	CHECK(name.GetCursorRect().x > 2);
}

TEST_CASE("RTL name is right-aligned and the caret moves left with Arabic text") {
	MockGame game(MockMap::eNone);
	Window_Name name(0, 0, 192, 32, true);
	const int contents_width = name.GetContents()->GetWidth();
	const auto draw_layout = Window_Name::GetTextDrawLayout(true, contents_width);

	CHECK(name.IsRtlLayout());
	CHECK(draw_layout.anchor_x == contents_width - 2);
	CHECK(draw_layout.align_right);
	name.Update();
	const int empty_caret_x = name.GetCursorRect().x;
	CHECK(empty_caret_x == contents_width - 2 - 16);

	name.Append("م");
	CHECK(name.Get() == "م");
	name.Update();
	const int meem_caret_x = name.GetCursorRect().x;
	CHECK(meem_caret_x < empty_caret_x);

	name.Append("ح");
	CHECK(name.Get() == "مح");
	name.Update();
	const int meeh_caret_x = name.GetCursorRect().x;
	CHECK(meeh_caret_x < meem_caret_x);

	name.Append("م");
	name.Append("د");
	CHECK(name.Get() == "محمد");
	name.Update();
	const int full_name_caret_x = name.GetCursorRect().x;
	CHECK(full_name_caret_x < meeh_caret_x);
	CHECK(full_name_caret_x == Window_Name::GetCaretX(true, contents_width,
			Text::GetSize(*Font::Default(), name.Get()).width));

	name.Erase();
	CHECK(name.Get() == "محم");
	name.Update();
	CHECK(name.GetCursorRect().x > full_name_caret_x);
}

TEST_CASE("Window_Name keeps the existing shaped width limit") {
	MockGame game(MockMap::eNone);
	Window_Name name(0, 0, 192, 32, false);
	const std::string max_width_name(12, 'A');
	CHECK(Text::GetSize(*Font::Default(), max_width_name).width == 12 * 6);

	name.Append(max_width_name);
	CHECK(name.Get() == max_width_name);
	name.Append("A");
	CHECK(name.Get() == max_width_name);

	Window_Name rtl_name(0, 0, 192, 32, true);
	const std::string arabic_name = "محمد";
	CHECK(Text::GetSize(*Font::Default(), arabic_name).width <= 12 * 6);
	rtl_name.Append(arabic_name);
	CHECK(rtl_name.Get() == arabic_name);
	rtl_name.Update();
	CHECK(rtl_name.GetCursorRect().x == Window_Name::GetCaretX(true,
			rtl_name.GetContents()->GetWidth(), Text::GetSize(*Font::Default(), arabic_name).width));
}

TEST_CASE("Window_Name preserves tashkeel and mixed text in logical Unicode order") {
	MockGame game(MockMap::eNone);
	Window_Name name(0, 0, 192, 32, true);
	name.Append("م");
	name.Append("َ");
	CHECK(name.Get() == "مَ");

	const std::string mixed = "محمد1A";
	CHECK(Text::GetSize(*Font::Default(), mixed).width <= 12 * 6);
	name.Set("");
	name.Append("محمد");
	name.Append("1A");
	CHECK(name.Get() == mixed);

	Window_Name ltr_name(0, 0, 192, 32, false);
	ltr_name.Set(mixed);
	CHECK_FALSE(ltr_name.IsRtlLayout());
	CHECK_FALSE(Window_Name::GetTextDrawLayout(false,
			ltr_name.GetContents()->GetWidth()).align_right);
}

TEST_CASE("Window_Name erases Arabic code points and tashkeel one at a time") {
	MockGame game(MockMap::eNone);
	Window_Name name(0, 0, 192, 32, true);
	const int contents_width = name.GetContents()->GetWidth();
	const auto check_caret = [&name, contents_width]() {
		name.Update();
		const int width = Text::GetSize(*Font::Default(), name.Get()).width;
		const int logical_end_offset = Text::GetLogicalEndCaretOffset(*Font::Default(), name.Get());
		CHECK(name.GetCursorRect().x == Window_Name::GetCaretX(true,
				contents_width, width, logical_end_offset));
	};

	name.Append("م");
	name.Append("َ");
	name.Append("ّ");
	const std::string meem_with_two_marks = std::string("م") + "َ" + "ّ";
	CHECK(name.Get() == meem_with_two_marks);
	check_caret();

	name.Erase();
	CHECK(name.Get() == std::string("م") + "َ");
	check_caret();
	name.Erase();
	CHECK(name.Get() == "م");
	check_caret();
	name.Erase();
	CHECK(name.Get().empty());
	check_caret();

	name.Set("محمد");
	name.Erase();
	CHECK(name.Get() == "محم");
	check_caret();

	for (const auto& letter : {"أ", "إ", "آ", "ئ", "ؤ", "ـ"}) {
		name.Set(letter);
		name.Erase();
		CHECK(name.Get().empty());
		check_caret();
	}
}

TEST_CASE("Name direction stays with the session across its keyboard pages") {
	MockGame game(MockMap::eNone);
	Window_Name name(0, 0, 192, 32, true);
	Window_Keyboard keyboard(0, 0);

	keyboard.SetMode(Window_Keyboard::Arabic, Window_Keyboard::Symbol);
	CHECK(name.IsRtlLayout());
	keyboard.SetMode(Window_Keyboard::Symbol, Window_Keyboard::Arabic);
	CHECK(name.IsRtlLayout());

	Window_Name ltr_name(0, 0, 192, 32, false);
	keyboard.SetMode(Window_Keyboard::Letter, Window_Keyboard::Symbol);
	CHECK_FALSE(ltr_name.IsRtlLayout());
	keyboard.SetMode(Window_Keyboard::Symbol, Window_Keyboard::Letter);
	CHECK_FALSE(ltr_name.IsRtlLayout());
}

TEST_SUITE_END();
