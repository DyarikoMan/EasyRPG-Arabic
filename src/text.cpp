/*
 * This file is part of EasyRPG Player.
 *
 * EasyRPG Player is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * EasyRPG Player is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with EasyRPG Player. If not, see <http://www.gnu.org/licenses/>.
 */

// Headers
#include <lcf/data.h>
#include "cache.h"
#include "output.h"
#include "utils.h"
#include "bitmap.h"
#include "font.h"
#include "text.h"
#include "compiler.h"

#include <algorithm>
#include <cctype>
#include <cstdint>
#include <iterator>
#include <limits>
#include <memory>
#include <optional>
#include <string>
#include <vector>

#include <unicode/ubidi.h>

namespace {

bool IsArabicScript(char32_t c) {
	return (c >= 0x0600 && c <= 0x06FF) ||
		(c >= 0x0750 && c <= 0x077F) ||
		(c >= 0x08A0 && c <= 0x08FF) ||
		(c >= 0xFB50 && c <= 0xFDFF) ||
		(c >= 0xFE70 && c <= 0xFEFF);
}

bool ContainsArabicScript(std::u32string_view text) {
	return std::any_of(text.begin(), text.end(), IsArabicScript);
}

std::vector<Font::ShapeRet> ShapeWithBidi(const Font& font, std::u32string_view text,
		int* logical_end_offset = nullptr) {
	if (!ContainsArabicScript(text)) {
		return font.Shape(text);
	}

	const auto shape_rtl = [&font, text, logical_end_offset]() {
		if (logical_end_offset) {
			// The fallback shapes the whole paragraph as an RTL run, whose
			// logical end is at the visual left edge.
			*logical_end_offset = 0;
		}
		return font.Shape(text, Font::ShapeDirection::RTL);
	};

	std::vector<UChar> text16;
	text16.reserve(text.size());
	for (char32_t c : text) {
		const uint32_t codepoint = static_cast<uint32_t>(c);
		if (codepoint <= 0xFFFF) {
			text16.push_back(static_cast<UChar>(codepoint));
		} else {
			const uint32_t supplementary = codepoint - 0x10000;
			text16.push_back(static_cast<UChar>(0xD800 + (supplementary >> 10)));
			text16.push_back(static_cast<UChar>(0xDC00 + (supplementary & 0x3FF)));
		}
	}

	if (text16.size() > static_cast<size_t>(std::numeric_limits<int32_t>::max())) {
		return shape_rtl();
	}

	UErrorCode bidi_status = U_ZERO_ERROR;
	std::unique_ptr<UBiDi, decltype(&ubidi_close)> bidi(
		ubidi_openSized(static_cast<int32_t>(text16.size()), 0, &bidi_status),
		&ubidi_close
	);
	if (U_FAILURE(bidi_status) || !bidi) {
		return shape_rtl();
	}

	const int32_t text16_length = static_cast<int32_t>(text16.size());
	ubidi_setPara(bidi.get(), text16.data(), text16_length, UBIDI_RTL, nullptr, &bidi_status);
	if (U_FAILURE(bidi_status)) {
		return shape_rtl();
	}

	const int32_t run_count = ubidi_countRuns(bidi.get(), &bidi_status);
	if (U_FAILURE(bidi_status)) {
		return shape_rtl();
	}

	std::vector<Font::ShapeRet> shaped_text;
	bool bidi_succeeded = true;
	bool logical_end_found = false;
	int visual_run_x = 0;
	int mapped_logical_end_offset = 0;
	for (int32_t visual_run = 0; visual_run < run_count; ++visual_run) {
		int32_t logical_start = 0;
		int32_t run_length = 0;
		const UBiDiDirection direction = ubidi_getVisualRun(bidi.get(), visual_run, &logical_start, &run_length);
		if (logical_start < 0 || run_length < 0 || logical_start > text16_length - run_length) {
			bidi_succeeded = false;
			break;
		}

		std::u32string run32;
		run32.reserve(static_cast<size_t>(run_length));
		const int32_t run_end = logical_start + run_length;
		for (int32_t i = logical_start; i < run_end;) {
			uint32_t codepoint = text16[static_cast<size_t>(i++)];
			if (codepoint >= 0xD800 && codepoint <= 0xDBFF && i < run_end) {
				const uint32_t low = text16[static_cast<size_t>(i)];
				if (low >= 0xDC00 && low <= 0xDFFF) {
					++i;
					codepoint = 0x10000 + ((codepoint - 0xD800) << 10) + (low - 0xDC00);
				}
			}
			run32.push_back(static_cast<char32_t>(codepoint));
		}

		const Font::ShapeDirection shape_direction = direction == UBIDI_RTL
			? Font::ShapeDirection::RTL
			: Font::ShapeDirection::LTR;
		auto shaped_run = font.Shape(run32, shape_direction);
		int run_width = 0;
		for (const auto& glyph : shaped_run) {
			run_width += font.GetSize(glyph).width;
		}
		if (logical_start + run_length == text16_length) {
			mapped_logical_end_offset = visual_run_x +
				(direction == UBIDI_RTL ? 0 : run_width);
			logical_end_found = true;
		}
		visual_run_x += run_width;
		shaped_text.insert(shaped_text.end(), shaped_run.begin(), shaped_run.end());
	}

	if (!bidi_succeeded || !logical_end_found) {
		return shape_rtl();
	}
	if (logical_end_offset) {
		*logical_end_offset = mapped_logical_end_offset;
	}
	return shaped_text;
}

struct InlineBidiUnit {
	char32_t glyph = 0;
	bool is_exfont = false;
	int32_t utf16_start = 0;
	int32_t utf16_end = 0;
};

struct InlineBidiItem {
	bool is_exfont = false;
	char32_t glyph = 0;
	std::vector<Font::ShapeRet> shaped_text;
	int x = 0;
	int width = 0;
	int height = 0;
	bool rtl = false;
};

struct InlineBidiLayout {
	std::vector<InlineBidiItem> items;
	Rect size;
	int logical_end_offset = 0;
};

/**
 * Builds one ICU paragraph for Arabic text that contains inline ExFont objects.
 * Each parsed ExFont token is represented by one U+FFFC during BiDi analysis;
 * the original token is retained in its unit and rendered through the usual
 * ExFont path below.
 */
std::optional<InlineBidiLayout> BuildInlineBidiLayout(const Font& font, std::string_view text) {
	if (!font.CanShape()) {
		return std::nullopt;
	}

	std::vector<InlineBidiUnit> units;
	bool has_arabic = false;
	bool has_exfont = false;
	auto iter = text.data();
	const auto end = iter + text.size();
	while (iter != end) {
		const auto ret = Utils::TextNext(iter, end, 0);
		if (ret.next <= iter) {
			break;
		}
		iter = ret.next;
		if (!ret) {
			continue;
		}

		// Preserve the existing control-character path, which has its own line
		// and control handling and is not used by the Name Input field.
		if (!ret.is_exfont && Utils::IsControlCharacter(ret.ch)) {
			return std::nullopt;
		}

		InlineBidiUnit unit;
		unit.glyph = ret.ch;
		unit.is_exfont = ret.is_exfont;
		units.push_back(unit);
		has_exfont = has_exfont || ret.is_exfont;
		has_arabic = has_arabic || (!ret.is_exfont && IsArabicScript(ret.ch));
	}

	// Leave every existing LTR/non-Arabic path exactly as it is. The inline
	// layout is needed when ExFont would otherwise split an Arabic paragraph.
	if (!has_arabic || !has_exfont || units.empty()) {
		return std::nullopt;
	}

	std::vector<UChar> text16;
	text16.reserve(units.size());
	for (auto& unit : units) {
		const uint32_t codepoint = static_cast<uint32_t>(unit.is_exfont ? 0xFFFC : unit.glyph);
		const size_t code_units = codepoint <= 0xFFFF ? 1 : 2;
		if (text16.size() > static_cast<size_t>(std::numeric_limits<int32_t>::max()) - code_units) {
			return std::nullopt;
		}
		unit.utf16_start = static_cast<int32_t>(text16.size());
		if (codepoint <= 0xFFFF) {
			text16.push_back(static_cast<UChar>(codepoint));
		} else {
			const uint32_t supplementary = codepoint - 0x10000;
			text16.push_back(static_cast<UChar>(0xD800 + (supplementary >> 10)));
			text16.push_back(static_cast<UChar>(0xDC00 + (supplementary & 0x3FF)));
		}
		unit.utf16_end = static_cast<int32_t>(text16.size());
	}

	if (text16.size() > static_cast<size_t>(std::numeric_limits<int32_t>::max())) {
		return std::nullopt;
	}

	InlineBidiLayout layout;
	std::vector<size_t> unit_to_item(units.size(), std::numeric_limits<size_t>::max());
	const auto append_text_item = [&font, &layout, &unit_to_item, &units](
			const std::vector<size_t>& visual_units, Font::ShapeDirection direction) {
		if (visual_units.empty()) {
			return;
		}

		std::vector<size_t> logical_units = visual_units;
		if (direction == Font::ShapeDirection::RTL) {
			std::reverse(logical_units.begin(), logical_units.end());
		}

		std::u32string logical_text;
		logical_text.reserve(logical_units.size());
		for (const size_t unit_index : logical_units) {
			logical_text.push_back(units[unit_index].glyph);
		}

		InlineBidiItem item;
		item.shaped_text = font.Shape(logical_text, direction);
		item.rtl = direction == Font::ShapeDirection::RTL;
		for (const auto& glyph : item.shaped_text) {
			const Rect glyph_size = font.GetSize(glyph);
			item.width += glyph_size.width;
			item.height = std::max(item.height, glyph_size.height);
		}
		item.x = layout.size.width;

		const size_t item_index = layout.items.size();
		for (const size_t unit_index : visual_units) {
			unit_to_item[unit_index] = item_index;
		}
		layout.size.width += item.width;
		layout.size.height = std::max(layout.size.height, item.height);
		layout.items.push_back(std::move(item));
	};

	const auto append_exfont_item = [&font, &layout, &unit_to_item, &units](
			size_t unit_index, Font::ShapeDirection direction) {
		InlineBidiItem item;
		item.is_exfont = true;
		item.glyph = units[unit_index].glyph;
		item.rtl = direction == Font::ShapeDirection::RTL;
		const Rect glyph_size = Text::GetSize(font, item.glyph, true);
		item.width = glyph_size.width;
		item.height = glyph_size.height;
		item.x = layout.size.width;

		unit_to_item[unit_index] = layout.items.size();
		layout.size.width += item.width;
		layout.size.height = std::max(layout.size.height, item.height);
		layout.items.push_back(std::move(item));
	};

	const auto append_visual_run = [&units, &append_text_item, &append_exfont_item](
			std::vector<size_t> visual_units, Font::ShapeDirection direction) {
		if (direction == Font::ShapeDirection::RTL) {
			std::reverse(visual_units.begin(), visual_units.end());
		}

		std::vector<size_t> text_units;
		const auto flush_text = [&]() {
			append_text_item(text_units, direction);
			text_units.clear();
		};
		for (const size_t unit_index : visual_units) {
			if (units[unit_index].is_exfont) {
				flush_text();
				append_exfont_item(unit_index, direction);
			} else {
				text_units.push_back(unit_index);
			}
		}
		flush_text();
	};

	bool bidi_succeeded = false;
	UErrorCode bidi_status = U_ZERO_ERROR;
	std::unique_ptr<UBiDi, decltype(&ubidi_close)> bidi(
		ubidi_openSized(static_cast<int32_t>(text16.size()), 0, &bidi_status),
		&ubidi_close
	);
	if (U_SUCCESS(bidi_status) && bidi) {
		const int32_t text16_length = static_cast<int32_t>(text16.size());
		ubidi_setPara(bidi.get(), text16.data(), text16_length, UBIDI_RTL, nullptr, &bidi_status);
		if (U_SUCCESS(bidi_status)) {
			const int32_t run_count = ubidi_countRuns(bidi.get(), &bidi_status);
			if (U_SUCCESS(bidi_status)) {
				bidi_succeeded = true;
				for (int32_t visual_run = 0; visual_run < run_count; ++visual_run) {
					int32_t logical_start = 0;
					int32_t run_length = 0;
					const UBiDiDirection direction = ubidi_getVisualRun(
						bidi.get(), visual_run, &logical_start, &run_length);
					if (logical_start < 0 || run_length < 0 ||
							logical_start > text16_length - run_length) {
						bidi_succeeded = false;
						break;
					}

					const int32_t run_end = logical_start + run_length;
					std::vector<size_t> visual_units;
					for (size_t i = 0; i < units.size(); ++i) {
						if (units[i].utf16_start >= logical_start && units[i].utf16_end <= run_end) {
							visual_units.push_back(i);
					}
					}
					const Font::ShapeDirection shape_direction = direction == UBIDI_RTL
						? Font::ShapeDirection::RTL
						: Font::ShapeDirection::LTR;
					append_visual_run(std::move(visual_units), shape_direction);
				}
			}
		}
	}

	if (!bidi_succeeded || std::find(unit_to_item.begin(), unit_to_item.end(),
			std::numeric_limits<size_t>::max()) != unit_to_item.end()) {
		// Allocation/ICU failures are rare, but still keep all Arabic segments
		// in one RTL fallback sequence instead of restarting after each object.
		layout = {};
		std::fill(unit_to_item.begin(), unit_to_item.end(), std::numeric_limits<size_t>::max());
		std::vector<size_t> visual_units(units.size());
		for (size_t i = 0; i < units.size(); ++i) {
			visual_units[i] = i;
		}
		append_visual_run(std::move(visual_units), Font::ShapeDirection::RTL);
	}

	const size_t logical_end_item = unit_to_item.back();
	const InlineBidiItem& item = layout.items[logical_end_item];
	bool logical_end_rtl = item.rtl;
	if (units.back().is_exfont) {
		// ICU may resolve a terminal neutral object as an LTR run at paragraph
		// end. The insertion boundary after that object follows the preceding
		// logical text run, while the object's visual placement remains ICU's.
		bool preceding_text_found = false;
		for (size_t i = units.size() - 1; i > 0; --i) {
			const size_t preceding_unit = i - 1;
			if (!units[preceding_unit].is_exfont) {
				logical_end_rtl = layout.items[unit_to_item[preceding_unit]].rtl;
				preceding_text_found = true;
				break;
			}
		}
		if (!preceding_text_found) {
			logical_end_rtl = true;
		}
	}
	layout.logical_end_offset = item.x + (logical_end_rtl ? 0 : item.width);
	return layout;
}

int GetShapedTextWidth(const Font& font, const std::vector<Font::ShapeRet>& shaped_text) {
	int width = 0;
	for (const auto& glyph : shaped_text) {
		width += font.GetSize(glyph).width;
	}
	return width;
}

int DrawShapedText(Bitmap& dest, int x, int y, const Font& font, const Bitmap& system, int color,
		const std::vector<Font::ShapeRet>& shaped_text, bool bidi) {
	if (bidi) {
		// RTL HarfBuzz output is stored in visual left-to-right order.
		int next_glyph_pos = 0;
		for (const auto& glyph : shaped_text) {
			font.Render(dest, x + next_glyph_pos, y, system, color, glyph);
			next_glyph_pos += font.GetSize(glyph).width;
		}
		return next_glyph_pos;
	}

	int next_glyph_pos = 0;
	for (const auto& glyph : shaped_text) {
		next_glyph_pos += font.Render(dest, x + next_glyph_pos, y, system, color, glyph).x;
	}
	return next_glyph_pos;
}

int DrawShapedText(Bitmap& dest, int x, int y, const Font& font, Color color,
		const std::vector<Font::ShapeRet>& shaped_text, bool bidi) {
	int next_glyph_pos = 0;
	for (const auto& glyph : shaped_text) {
		if (bidi) {
			font.Render(dest, x + next_glyph_pos, y, color, glyph);
			next_glyph_pos += font.GetSize(glyph).width;
		} else {
			next_glyph_pos += font.Render(dest, x + next_glyph_pos, y, color, glyph).x;
		}
	}
	return next_glyph_pos;
}

void AddShapedTextSize(Rect& rect, const Font& font, const std::vector<Font::ShapeRet>& shaped_text, bool bidi) {
	if (bidi) {
		// Match DrawShapedText's left-to-right pen movement over the visual glyph order.
		rect.width += GetShapedTextWidth(font, shaped_text);
		for (const auto& glyph : shaped_text) {
			const Rect size = font.GetSize(glyph);
			rect.height = std::max(rect.height, size.height);
		}
		return;
	}

	for (const auto& glyph : shaped_text) {
		const Rect size = font.GetSize(glyph);
		rect.width += glyph.offset.x + size.width;
		rect.height = std::max(rect.height, size.height);
	}
}

} // namespace

Point Text::Draw(Bitmap& dest, int x, int y, const Font& font, const Bitmap& system, int color, char32_t glyph, bool is_exfont) {
	if (is_exfont) {
		if (!font.IsStyleApplied()) {
			return Font::exfont->Render(dest, x, y, system, color, glyph);
		} else {
			auto style = font.GetCurrentStyle();
			auto style_guard = Font::exfont->ApplyStyle(style);
			return Font::exfont->Render(dest, x, y, system, color, glyph);
		}
	} else {
		return font.Render(dest, x, y, system, color, glyph);
	}
}

Point Text::Draw(Bitmap& dest, int x, int y, const Font& font, Color color, char32_t glyph, bool is_exfont) {
	if (is_exfont) {
		if (!font.IsStyleApplied()) {
			return Font::exfont->Render(dest, x, y, color, glyph);
		} else {
			auto style = font.GetCurrentStyle();
			auto style_guard = Font::exfont->ApplyStyle(style);
			return Font::exfont->Render(dest, x, y, color, glyph);
		}
	} else {
		return font.Render(dest, x, y, color, glyph);
	}
}

Point Text::Draw(Bitmap& dest, const int x, const int y, const Font& font, const Bitmap& system, const int color, std::string_view text, const Text::Alignment align) {
	if (text.length() == 0) return { 0, 0 };

	auto inline_layout = BuildInlineBidiLayout(font, text);
	Rect dst_rect = inline_layout ? inline_layout->size : Text::GetSize(font, text);

	const int ih = dst_rect.height;

	switch (align) {
	case Text::AlignCenter:
		dst_rect.x = x - dst_rect.width / 2; break;
	case Text::AlignRight:
		dst_rect.x = x - dst_rect.width; break;
	case Text::AlignLeft:
		dst_rect.x = x; break;
	default: assert(false);
	}

	dst_rect.y = y;
	dst_rect.width += 1; dst_rect.height += 1; // Need place for shadow

	const int iy = dst_rect.y;
	const int ix = dst_rect.x;
	if (inline_layout) {
		int drawn_width = 0;
		for (const auto& item : inline_layout->items) {
			if (item.is_exfont) {
				drawn_width += Draw(dest, ix + item.x, iy, font, system, color, item.glyph, true).x;
			} else {
				drawn_width += DrawShapedText(dest, ix + item.x, iy, font, system, color,
					item.shaped_text, true);
			}
		}
		return { drawn_width, ih };
	}

	// Where to draw the next glyph (x pos)
	int next_glyph_pos = 0;

	// This loops always renders a single char, color blends it and then puts
	// it onto the text_surface (including the drop shadow)
	auto iter = text.data();
	const auto end = iter + text.size();

	if (font.CanShape()) {
		// Collect all glyphs until ExFont or end of string and then shape and render
		std::u32string text32;
		while (iter != end) {
			auto ret = Utils::TextNext(iter, end, 0);

			iter = ret.next;
			if (EP_UNLIKELY(!ret)) {
				continue;
			}

			if (EP_UNLIKELY(Utils::IsControlCharacter(ret.ch))) {
				next_glyph_pos += Draw(dest, ix + next_glyph_pos, iy, font, system, color, ret.ch, ret.is_exfont).x;
				continue;
			}

			if (ret.is_exfont) {
				if (!text32.empty()) {
					const bool bidi = ContainsArabicScript(text32);
					auto shape_ret = ShapeWithBidi(font, text32);
					text32.clear();

					next_glyph_pos += DrawShapedText(dest, ix + next_glyph_pos, iy, font, system, color, shape_ret, bidi);
				}

				next_glyph_pos += Draw(dest, ix + next_glyph_pos, iy, font, system, color, ret.ch, true).x;
				continue;
			}

			text32 += ret.ch;
		}

		if (!text32.empty()) {
			const bool bidi = ContainsArabicScript(text32);
			auto shape_ret = ShapeWithBidi(font, text32);
			next_glyph_pos += DrawShapedText(dest, ix + next_glyph_pos, iy, font, system, color, shape_ret, bidi);
		}
	} else {
		while (iter != end) {
			auto ret = Utils::TextNext(iter, end, 0);

			iter = ret.next;
			if (EP_UNLIKELY(!ret)) {
				continue;
			}
			next_glyph_pos += Text::Draw(dest, ix + next_glyph_pos, iy, font, system, color, ret.ch, ret.is_exfont).x;
		}
	}
	return { next_glyph_pos, ih };
}

Point Text::Draw(Bitmap& dest, const int x, const int y, const Font& font, const Color color, std::string_view text) {
	if (text.length() == 0) return { 0, 0 };

	if (auto inline_layout = BuildInlineBidiLayout(font, text)) {
		int drawn_width = 0;
		for (const auto& item : inline_layout->items) {
			if (item.is_exfont) {
				drawn_width += Draw(dest, x + item.x, y, font, color, item.glyph, true).x;
			} else {
				drawn_width += DrawShapedText(dest, x + item.x, y, font, color,
					item.shaped_text, true);
			}
		}
		return { drawn_width, inline_layout->size.height };
	}

	int dx = x;
	int mx = x;

	int dy = y;
	int ny = 0;

	auto iter = text.data();
	const auto end = iter + text.size();
	while (iter != end) {
		auto ret = Utils::UTF8Next(iter, end);

		iter = ret.next;
		if (EP_UNLIKELY(!ret)) {
			continue;
		}

		if (ret.ch == U'\n') {
			if (ny == 0) {
				ny = font.GetSize(ret.ch).height;
			}
			dy += ny;
			mx = std::max(mx, dx);
			dx = x;
			ny = 0;
			continue;
		}

		auto rect = font.Render(dest, dx, dy, color, ret.ch);
		dx += rect.x;
		assert(ny == 0 || ny == rect.y);
		ny = rect.y;
	}
	dy += ny;
	mx = std::max(mx, dx);

	return { mx - x , dy - y };
}

Rect Text::GetSize(const Font& font, std::string_view text) {
	if (auto inline_layout = BuildInlineBidiLayout(font, text)) {
		return inline_layout->size;
	}

	Rect rect;
	Rect rect_tmp;

	auto iter = text.data();
	const auto end = iter + text.size();

	if (font.CanShape()) {
		std::u32string text32;
		while (iter != end) {
			auto ret = Utils::TextNext(iter, end, 0);

			iter = ret.next;
			if (EP_UNLIKELY(!ret)) {
				continue;
			}

			if (EP_UNLIKELY(Utils::IsControlCharacter(ret.ch))) {
				rect_tmp = GetSize(font, ret.ch, ret.is_exfont);
				rect.width += rect_tmp.width;
				rect.height = std::max(rect.height, rect_tmp.height);
				continue;
			}

			if (ret.is_exfont) {
				if (!text32.empty()) {
					const bool bidi = ContainsArabicScript(text32);
					auto shape_ret = ShapeWithBidi(font, text32);
					text32.clear();
					AddShapedTextSize(rect, font, shape_ret, bidi);
				}

				rect_tmp = GetSize(font, ret.ch, ret.is_exfont);
				rect.width += rect_tmp.width;
				rect.height = std::max(rect.height, rect_tmp.height);
				continue;
			}

			text32 += ret.ch;
		}

		if (!text32.empty()) {
			const bool bidi = ContainsArabicScript(text32);
			auto shape_ret = ShapeWithBidi(font, text32);
			AddShapedTextSize(rect, font, shape_ret, bidi);
		}
	} else {
		while (iter != end) {
			auto ret = Utils::TextNext(iter, end, 0);

			iter = ret.next;
			if (EP_UNLIKELY(!ret)) {
				continue;
			}

			rect_tmp = GetSize(font, ret.ch, ret.is_exfont);
			rect.width += rect_tmp.width;
			rect.height = std::max(rect.height, rect_tmp.height);
		}
	}

	return rect;
}

int Text::GetLogicalEndCaretOffset(const Font& font, std::string_view text) {
	if (auto inline_layout = BuildInlineBidiLayout(font, text)) {
		return inline_layout->logical_end_offset;
	}

	if (!font.CanShape()) {
		// Preserve the existing unshaped RTL offset within each text segment,
		// while accounting for ExFont tokens as separately rendered glyphs.
		int visual_prefix_width = 0;
		int trailing_exfont_width = 0;
		std::u32string text32;
		auto iter = text.data();
		const auto end = iter + text.size();
		while (iter != end) {
			const auto ret = Utils::TextNext(iter, end, 0);
			if (ret.next <= iter) {
				break;
			}
			iter = ret.next;

			if (ret.is_exfont) {
				if (!text32.empty()) {
					visual_prefix_width += GetSize(font, Utils::EncodeUTF(text32)).width;
					text32.clear();
				}
				trailing_exfont_width = GetSize(font, ret.ch, true).width;
				visual_prefix_width += trailing_exfont_width;
			} else {
				text32 += ret.ch;
				trailing_exfont_width = 0;
			}
		}

		if (text32.empty()) {
			return visual_prefix_width - trailing_exfont_width;
		}
		const int segment_width = GetSize(font, Utils::EncodeUTF(text32)).width;
		return visual_prefix_width + (ContainsArabicScript(text32) ? 0 : segment_width);
	}

	int visual_prefix_width = 0;
	int trailing_exfont_width = 0;
	std::u32string text32;
	const auto measure_segment = [&font](std::u32string_view segment, int& width) {
		int logical_end_offset = -1;
		const auto shaped = ShapeWithBidi(font, segment, &logical_end_offset);
		if (ContainsArabicScript(segment)) {
			width = GetShapedTextWidth(font, shaped);
		} else {
			Rect size;
			AddShapedTextSize(size, font, shaped, false);
			width = size.width;
			logical_end_offset = width;
		}
		return logical_end_offset;
	};

	auto iter = text.data();
	const auto end = iter + text.size();
	while (iter != end) {
		auto ret = Utils::TextNext(iter, end, 0);
		iter = ret.next;
		if (EP_UNLIKELY(!ret)) {
			continue;
		}

		if (ret.is_exfont) {
			if (!text32.empty()) {
				int segment_width = 0;
				measure_segment(text32, segment_width);
				visual_prefix_width += segment_width;
				text32.clear();
			}
			trailing_exfont_width = GetSize(font, ret.ch, true).width;
			visual_prefix_width += trailing_exfont_width;
		} else {
			text32 += ret.ch;
			trailing_exfont_width = 0;
		}
	}

	if (text32.empty()) {
		// Text::Draw emits an ExFont glyph after the preceding shaped segment.
		// Its visual leading edge is therefore the insertion point for an RTL
		// name ending with that token.
		return visual_prefix_width - trailing_exfont_width;
	}

	int segment_width = 0;
	const int logical_end_offset = measure_segment(text32, segment_width);
	return visual_prefix_width + logical_end_offset;
}

Rect Text::GetSize(const Font& font, char32_t glyph, bool is_exfont) {
	if (is_exfont) {
		if (!font.IsStyleApplied()) {
			return Font::exfont->GetSize(glyph);
		} else {
			auto style = font.GetCurrentStyle();
			auto style_guard = Font::exfont->ApplyStyle(style);
			return Font::exfont->GetSize(glyph);
		}
	} else {
		return font.GetSize(glyph);
	}
}
