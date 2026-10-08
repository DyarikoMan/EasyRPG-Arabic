#include "text.h"
#include "pixel_format.h"
#include "cache.h"
#include "bitmap.h"
#include "font.h"
#include "window_name.h"
#include "mock_game.h"
#include <iostream>
#include <string>
#include <utility>
#include <vector>
#include "doctest.h"

namespace {

constexpr char32_t glyph_simple = 0xE001;
constexpr char32_t glyph_wooden = 0xE002;
constexpr char32_t glyph_club = 0xE003;

class BidiVisualOrderTestFont : public Font {
public:
	BidiVisualOrderTestFont() : Font("bidi-test", 12, false, false) {}

	mutable std::vector<std::pair<std::u32string, ShapeDirection>> shape_calls;
	mutable std::vector<char32_t> rendered_glyphs;

protected:
	Rect vGetSize(char32_t) const override {
		return {0, 0, 1, 12};
	}

	GlyphRet vRender(char32_t glyph) const override {
		return RenderGlyph(glyph);
	}

	GlyphRet vRenderShaped(char32_t glyph) const override {
		rendered_glyphs.push_back(glyph);
		return RenderGlyph(glyph);
	}

	bool vCanShape() const override {
		return true;
	}

	std::vector<ShapeRet> vShape(std::u32string_view text, ShapeDirection direction) const override {
		shape_calls.emplace_back(std::u32string(text), direction);

		if (direction == ShapeDirection::RTL && text == U"هراوة خشبية بسيطة") {
			// HarfBuzz RTL output is in visual left-to-right glyph order.
			return {
				{glyph_simple, Point(1, 0), Point(0, 0), false},
				{U' ', Point(1, 0), Point(0, 0), false},
				{glyph_wooden, Point(1, 0), Point(0, 0), false},
				{U' ', Point(1, 0), Point(0, 0), false},
				{glyph_club, Point(1, 0), Point(0, 0), false}
			};
		}

		std::vector<ShapeRet> shaped;
		shaped.reserve(text.size());
		for (char32_t glyph : text) {
			shaped.push_back({glyph, Point(1, 0), Point(0, 0), false});
		}
		return shaped;
	}

private:
	GlyphRet RenderGlyph(char32_t glyph) const {
		Color color;
		switch (glyph) {
		case glyph_simple: color = Color(255, 0, 0, 255); break;
		case glyph_wooden: color = Color(0, 255, 0, 255); break;
		case glyph_club: color = Color(0, 0, 255, 255); break;
		default: return {};
		}
		return {Bitmap::Create(1, 1, color), Point(1, 0), Point(0, 0), true};
	}
};

Color InlineGlyphColor(char32_t glyph) {
	return Color(
		static_cast<uint8_t>((static_cast<uint32_t>(glyph) * 37) % 251),
		static_cast<uint8_t>((static_cast<uint32_t>(glyph) * 53) % 251),
		static_cast<uint8_t>((static_cast<uint32_t>(glyph) * 71) % 251), 255);
}

class InlineBidiTestFont : public Font {
public:
	InlineBidiTestFont() : Font("inline-bidi-test", 12, false, false) {}

protected:
	Rect vGetSize(char32_t) const override {
		return {0, 0, 1, 12};
	}

	GlyphRet vRender(char32_t glyph) const override {
		return RenderGlyph(glyph);
	}

	GlyphRet vRenderShaped(char32_t glyph) const override {
		return RenderGlyph(glyph);
	}

	bool vCanShape() const override {
		return true;
	}

	std::vector<ShapeRet> vShape(std::u32string_view text, ShapeDirection direction) const override {
		std::vector<ShapeRet> shaped;
		shaped.reserve(text.size());
		if (direction == ShapeDirection::RTL) {
			for (auto iter = text.rbegin(); iter != text.rend(); ++iter) {
				shaped.push_back({*iter, Point(1, 0), Point(0, 0), false});
			}
		} else {
			for (const char32_t glyph : text) {
				shaped.push_back({glyph, Point(1, 0), Point(0, 0), false});
			}
		}
		return shaped;
	}

private:
	GlyphRet RenderGlyph(char32_t glyph) const {
		return {Bitmap::Create(1, 1, InlineGlyphColor(glyph)), Point(1, 0), Point(0, 0), true};
	}
};

class InlineBidiTestExFont : public Font {
public:
	InlineBidiTestExFont() : Font("inline-bidi-test-exfont", 12, false, false) {}

protected:
	Rect vGetSize(char32_t) const override {
		return {0, 0, icon_width, 12};
	}

	GlyphRet vRender(char32_t) const override {
		return {Bitmap::Create(icon_width, 1, icon_color), Point(icon_width, 0), Point(0, 0), true};
	}

private:
	static constexpr int icon_width = 2;
	static constexpr Color icon_color = Color(0, 255, 0, 255);
};

bool HasShapeCall(const BidiVisualOrderTestFont& font, std::u32string_view text, Font::ShapeDirection direction) {
	for (const auto& call : font.shape_calls) {
		if (call.first.find(text) != std::u32string::npos && call.second == direction) {
			return true;
		}
	}
	return false;
}

} // namespace

TEST_SUITE_BEGIN("Text");

constexpr int width = 240;
constexpr int height = 80;
constexpr int ch = 12;
constexpr int cwh = 6;
constexpr int cwf = 12;

TEST_CASE("TextDrawSystemStrReturn") {
	Bitmap::SetFormat(format_R8G8B8A8_a().format());
	auto font = Font::Default();
	auto surface = Bitmap::Create(width, height);
	auto system = Cache::SysBlack();

	auto draw = [&](int x, int y, const auto& text) {
		return Text::Draw(*surface, x, y, *font, *system, 0, text);
	};

	REQUIRE_EQ(draw(0, 0, ""), Point(0, 0));
	REQUIRE_EQ(draw(0, 0, "abc"), Point(cwh * 3, ch));
	REQUIRE_EQ(draw(3, 17, "$A"), Point(cwf, ch));
	REQUIRE_EQ(draw(3, 17, "$A $B"), Point(cwf * 2 + cwh, ch));
}

TEST_CASE("TextDrawColorStrReturn") {
	Bitmap::SetFormat(format_R8G8B8A8_a().format());
	auto font = Font::Default();
	auto surface = Bitmap::Create(width, height);
	auto color = Color(255,255,255,255);

	auto draw = [&](int x, int y, const auto& text) {
		return Text::Draw(*surface, x, y, *font, color, text);
	};

	REQUIRE_EQ(draw(0, 0, ""), Point(0, 0));
	REQUIRE_EQ(draw(0, 0, "abc"), Point(cwh * 3, 0));
	REQUIRE_EQ(draw(3, 17, "\n"), Point(0, 12));
	REQUIRE_EQ(draw(3, 17, "x\nyz"), Point(cwh * 2, 12));
	REQUIRE_EQ(draw(10, 0, "xy\nz"), Point(cwh * 2, 12));
}

TEST_CASE("TextBidiVisualOrderAndMixedRuns") {
	Bitmap::SetFormat(format_R8G8B8A8_a().format());
	BidiVisualOrderTestFont font;
	auto surface = Bitmap::Create(16, 16, true);
	auto system = Cache::SysBlack();

	const Point drawn_size = Text::Draw(*surface, 0, 4, font, *system, 0, "هراوة خشبية بسيطة");
	const Rect measured_size = Text::GetSize(font, "هراوة خشبية بسيطة");
	CHECK(surface->GetColorAt(0, 0).red > 250); // بسيطة, at left
	CHECK(surface->GetColorAt(2, 0).green > 250); // خشبية
	CHECK(surface->GetColorAt(4, 0).blue > 250); // هراوة, at right
	CHECK_EQ(drawn_size.x, measured_size.width);
	CHECK_EQ(drawn_size.x, 5);
	REQUIRE_EQ(font.rendered_glyphs.size(), 5);
	CHECK_EQ(font.rendered_glyphs[0], glyph_simple);
	CHECK_EQ(font.rendered_glyphs[1], U' ');
	CHECK_EQ(font.rendered_glyphs[2], glyph_wooden);
	CHECK_EQ(font.rendered_glyphs[3], U' ');
	CHECK_EQ(font.rendered_glyphs[4], glyph_club);

	font.shape_calls.clear();
	Text::GetSize(font, "HP ديالك 50 / 100");
	CHECK(HasShapeCall(font, U"HP", Font::ShapeDirection::LTR));
	CHECK(HasShapeCall(font, U"50", Font::ShapeDirection::LTR));
	CHECK(HasShapeCall(font, U"100", Font::ShapeDirection::LTR));
	CHECK(HasShapeCall(font, U"ديالك", Font::ShapeDirection::RTL));

	font.shape_calls.clear();
	Text::GetSize(font, "RPG Maker 2003 خدام مزيان");
	CHECK(HasShapeCall(font, U"RPG Maker 2003", Font::ShapeDirection::LTR));
	CHECK(HasShapeCall(font, U"خدام مزيان", Font::ShapeDirection::RTL));
}

TEST_CASE("Logical end caret offset follows ICU visual runs in RTL name strings") {
	BidiVisualOrderTestFont font;
	const std::pair<const char*, bool> cases[] = {
		{"محمد", false},
		{"أحمد", false},
		{"عبد الرحمن", false},
		{"مُحَمَّد", false},
		{"محمد123", true},
		{"محمدABC", true},
		{"ABCمحمد", false},
		{"عبد Player", true},
		{"Player عبد", false},
	};

	for (const auto& [value, logical_end_after_visual_left] : cases) {
		const int text_width = Text::GetSize(font, value).width;
		const int logical_end_offset = Text::GetLogicalEndCaretOffset(font, value);
		CHECK(logical_end_offset >= 0);
		CHECK(logical_end_offset <= text_width);
		if (logical_end_after_visual_left) {
			CHECK(logical_end_offset > 0);
		} else {
			CHECK(logical_end_offset == 0);
		}

		constexpr int contents_width = 200;
		CHECK(Window_Name::GetCaretX(true, contents_width, text_width, logical_end_offset) ==
			contents_width - 2 - text_width + logical_end_offset - 16);
	}

	// Without Arabic script, the logical end is the right edge of the LTR run.
	CHECK(Text::GetLogicalEndCaretOffset(font, "Player") == Text::GetSize(font, "Player").width);
}

TEST_CASE("RTL caret offset stops before a trailing ExFont icon") {
	BidiVisualOrderTestFont font;

	CHECK(Text::GetLogicalEndCaretOffset(font, "$A") == 0);
	CHECK(Text::GetLogicalEndCaretOffset(font, "محمد$A") == 0);
	CHECK(Text::GetLogicalEndCaretOffset(font, "محمد$A$B") == 0);
	CHECK(Text::GetLogicalEndCaretOffset(font, "$Aمحمد") == 0);
}

TEST_CASE("Arabic text and ExFont icons share one visual BiDi paragraph") {
	Bitmap::SetFormat(format_R8G8B8A8_a().format());
	const FontRef original_exfont = Font::exfont;
	struct ExFontRestore {
		FontRef original;
		~ExFontRestore() { Font::exfont = std::move(original); }
	} restore{original_exfont};
	Font::exfont = std::make_shared<InlineBidiTestExFont>();

	InlineBidiTestFont font;
	auto surface = Bitmap::Create(64, 16, true);
	auto system = Cache::SysBlack();
	const Color icon_color(0, 255, 0, 255);
	const auto icon = [&icon_color]() { return std::vector<Color>{icon_color, icon_color}; };
	const auto glyph = [](std::u32string_view text) {
		std::vector<Color> colors;
		for (const char32_t codepoint : text) {
			colors.push_back(InlineGlyphColor(codepoint));
		}
		return colors;
	};
	const auto colors_match = [](Color actual, Color expected) {
		const auto within_one = [](uint8_t left, uint8_t right) {
			return left <= right ? right - left <= 1 : left - right <= 1;
		};
		return within_one(actual.red, expected.red) && within_one(actual.green, expected.green) &&
			within_one(actual.blue, expected.blue) && actual.alpha == expected.alpha;
	};
	const auto check_visual = [&](std::string_view text, const std::vector<Color>& expected) {
		surface->Clear();
		const Point drawn = Text::Draw(*surface, 0, 4, font, *system, 0, text);
		const Rect measured = Text::GetSize(font, text);
		CHECK(drawn.x == measured.width);
		CHECK(drawn.x == static_cast<int>(expected.size()));
		for (size_t i = 0; i < expected.size(); ++i) {
			CHECK(colors_match(surface->GetColorAt(static_cast<int>(i), 0), expected[i]));
		}

		auto solid_surface = Bitmap::Create(64, 16, true);
		const Point solid_drawn = Text::Draw(*solid_surface, 0, 4, font,
			Color(255, 255, 255, 255), text);
		CHECK(solid_drawn.x == measured.width);
		for (int x = 0; x < measured.width; ++x) {
			CHECK(solid_surface->GetColorAt(x, 0).alpha == 255);
		}
	};

	// Logical order is ا ب ت [icon] ج ح. ICU's single RTL paragraph places
	// the suffix at the visual left, then the object, then the prefix.
	// Keep the expected sequence explicit and without a visual-space glyph.
	const std::vector<Color> expected_one_icon = {
		InlineGlyphColor(U'ح'), InlineGlyphColor(U'ج'),
		icon_color, icon_color,
		InlineGlyphColor(U'ت'), InlineGlyphColor(U'ب'), InlineGlyphColor(U'ا')
	};
	check_visual("ابت$Aجح", expected_one_icon);
	CHECK(Text::GetLogicalEndCaretOffset(font, "ابت$Aجح") == 0);

	const std::vector<Color> expected_trailing_icon = {
		icon_color, icon_color,
		InlineGlyphColor(U'ت'), InlineGlyphColor(U'ب'), InlineGlyphColor(U'ا')
	};
	check_visual("ابت$A", expected_trailing_icon);
	CHECK(Text::GetLogicalEndCaretOffset(font, "ابت$A") == 0);
	const std::vector<Color> expected_trailing_icons = {
		icon_color, icon_color, icon_color, icon_color,
		InlineGlyphColor(U'ت'), InlineGlyphColor(U'ب'), InlineGlyphColor(U'ا')
	};
	check_visual("ابت$A$B", expected_trailing_icons);
	CHECK(Text::GetLogicalEndCaretOffset(font, "ابت$A$B") == 0);

	const std::vector<Color> expected_two_icons = {
		InlineGlyphColor(U'ه'), InlineGlyphColor(U'خ'), icon_color, icon_color,
		InlineGlyphColor(U'ح'), InlineGlyphColor(U'ج'), icon_color, icon_color,
		InlineGlyphColor(U'ت'), InlineGlyphColor(U'ب'), InlineGlyphColor(U'ا')
	};
	check_visual("ابت$Aجح$Bخه", expected_two_icons);
	CHECK(Text::GetLogicalEndCaretOffset(font, "ابت$Aجح$Bخه") == 0);

	const auto icon_then_arabic = [&]() {
		auto expected = glyph(U"تبا");
		auto object = icon();
		expected.insert(expected.end(), object.begin(), object.end());
		return expected;
	}();
	check_visual("$Aابت", icon_then_arabic);
	CHECK(Text::GetLogicalEndCaretOffset(font, "$Aابت") == 0);

	{
		MockGame game(MockMap::eNone);
		const int previous_patch_setting = Player::game_config.patch_maniac.Get();
		struct PatchManiacRestore {
			int setting;
			~PatchManiacRestore() { Player::game_config.patch_maniac.Set(setting); }
		} restore_patch{previous_patch_setting};
		Player::game_config.patch_maniac.Set(true);
		check_visual("ابت$[43]جح", expected_one_icon);
		CHECK(Text::GetLogicalEndCaretOffset(font, "ابت$[43]جح") == 0);
	}

	// The same layout keeps width and drawing advances aligned for mixed runs.
	for (const std::string_view mixed : {
		"ابت$A123", "ابت$AABC", "ABC$Aابت", "ابت123$Aجح", "ابت$A"
	}) {
		surface->Clear();
		const Point drawn = Text::Draw(*surface, 0, 4, font, *system, 0, mixed);
		const Rect measured = Text::GetSize(font, mixed);
		CHECK(drawn.x == measured.width);
		for (int x = 0; x < measured.width; ++x) {
			CHECK(surface->GetColorAt(x, 0).alpha == 255);
		}
	}
	// The existing LTR sequence remains in logical left-to-right order.
	const std::vector<Color> expected_ltr = {
		InlineGlyphColor(U'A'), InlineGlyphColor(U'B'), InlineGlyphColor(U'C'),
		icon_color, icon_color
	};
	check_visual("ABC$A", expected_ltr);
}

TEST_CASE("GlyphRenderUsesGlobalVerticalOffset") {
	Bitmap::SetFormat(format_R8G8B8A8_a().format());
	BidiVisualOrderTestFont font;
	auto surface = Bitmap::Create(16, 16, true);
	auto system = Cache::SysBlack();

	const Point plain_advance = font.Render(*surface, 2, 6, *system, 0, glyph_simple);
	const Font::ShapeRet shaped = {glyph_simple, Point(1, 0), Point(0, 0), false};
	const Point shaped_advance = font.Render(*surface, 4, 6, *system, 0, shaped);
	font.Render(*surface, 6, 2, *system, 0, glyph_simple);

	CHECK_EQ(plain_advance, Point(1, 0));
	CHECK_EQ(shaped_advance, Point(1, 0));
	CHECK(surface->GetColorAt(2, 2).red > 250);
	CHECK(surface->GetColorAt(4, 2).red > 250);
	CHECK(surface->GetColorAt(2, 6).alpha == 0);
	CHECK(surface->GetColorAt(4, 6).alpha == 0);
	CHECK(surface->GetColorAt(6, 0).red > 250);
	CHECK(surface->GetColorAt(6, 2).alpha == 0);
}

TEST_SUITE_END();
