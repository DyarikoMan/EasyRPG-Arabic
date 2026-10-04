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

#include <cctype>
#include <cstdint>
#include <iterator>
#include <limits>
#include <memory>
#include <string>
#include <vector>

#include <unicode/ubidi.h>

namespace {

bool ContainsArabicScript(std::u32string_view text) {
	for (char32_t c : text) {
		if ((c >= 0x0600 && c <= 0x06FF) ||
			(c >= 0x0750 && c <= 0x077F) ||
			(c >= 0x08A0 && c <= 0x08FF) ||
			(c >= 0xFB50 && c <= 0xFDFF) ||
			(c >= 0xFE70 && c <= 0xFEFF)) {
			return true;
		}
	}

	return false;
}

std::vector<Font::ShapeRet> ShapeWithBidi(const Font& font, std::u32string_view text) {
	if (!ContainsArabicScript(text)) {
		return font.Shape(text);
	}

	const auto shape_rtl = [&font, text]() {
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
		shaped_text.insert(shaped_text.end(), shaped_run.begin(), shaped_run.end());
	}

	return bidi_succeeded ? shaped_text : shape_rtl();
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

	Rect dst_rect = Text::GetSize(font, text);

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
