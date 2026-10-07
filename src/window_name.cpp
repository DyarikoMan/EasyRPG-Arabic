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
#include <string>
#include "window_name.h"
#include "bitmap.h"
#include "font.h"
#include <lcf/data.h>
#include "game_system.h"

namespace {

constexpr int name_text_margin = 2;
constexpr int name_cursor_width = 16;

} // namespace

Window_Name::Window_Name(int ix, int iy, int iwidth, int iheight, bool rtl_layout) :
	Window_Base(ix, iy, iwidth, iheight),
	rtl_layout(rtl_layout) {

	SetContents(Bitmap::Create(width - 16, height - 16));

	name.clear();

	Refresh();
}

bool Window_Name::IsRtlLayout() const {
	return rtl_layout;
}

Window_Name::TextDrawLayout Window_Name::GetTextDrawLayout(bool rtl_layout, int contents_width) {
	return {
		rtl_layout ? contents_width - name_text_margin : name_text_margin,
		rtl_layout
	};
}

int Window_Name::GetCaretX(bool rtl_layout, int contents_width, int shaped_text_width) {
	// A total width cannot map the logical end through mixed BiDi runs.
	if (rtl_layout) {
		return contents_width - name_text_margin - shaped_text_width - name_cursor_width;
	}
	return shaped_text_width + name_text_margin;
}

void Window_Name::Refresh() {
	contents->Clear();
	const auto layout = GetTextDrawLayout(rtl_layout, contents->GetWidth());
	contents->TextDraw(layout.anchor_x, 2, Font::ColorDefault, name,
			layout.align_right ? Text::AlignRight : Text::AlignLeft);
}

void Window_Name::Set(std::string text) {
	name = std::move(text);
	Refresh();
}

void Window_Name::Append(std::string_view text) {
	// Avoid string copies by reusing the buffer in name
	name.append(text.begin(), text.end());
	if (Text::GetSize(*Font::Default(), name).width <= (12 * 6)) {
		Refresh();
	} else {
		Main_Data::game_system->SePlay(Main_Data::game_system->GetSystemSE(Main_Data::game_system->SFX_Buzzer));
		name.resize(name.size() - text.size());
	}
}

void Window_Name::Update() {
	Rect const name_size = Text::GetSize(*Font::Default(), name);
	SetCursorRect(Rect(GetCaretX(rtl_layout, contents->GetWidth(), name_size.width), 0, 16, 16));
}

void Window_Name::Erase() {
	if (name.empty())
		return;

	auto u32name = Utils::DecodeUTF32(name);
	u32name.pop_back();
	name = Utils::EncodeUTF(u32name);

	Refresh();
}

