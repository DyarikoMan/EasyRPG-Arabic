#include "text.h"
#include "pixel_format.h"
#include "cache.h"
#include "bitmap.h"
#include "font.h"
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
	auto surface = Bitmap::Create(16, 12, true);
	auto system = Cache::SysBlack();

	const Point drawn_size = Text::Draw(*surface, 0, 0, font, *system, 0, "هراوة خشبية بسيطة");
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

TEST_SUITE_END();
