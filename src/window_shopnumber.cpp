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
#include <sstream>
#include "game_system.h"
#include "input.h"
#include "util_macro.h"
#include "window_shopnumber.h"
#include "bitmap.h"
#include "font.h"
#include <lcf/reader_util.h>
#include "translation.h"

Window_ShopNumber::Window_ShopNumber(int ix, int iy, int iwidth, int iheight) :
	Window_Base(ix, iy, iwidth, iheight),
	item_max(1), price(0), number(1), item_id(0) {

	SetContents(Bitmap::Create(width - 16, height - 16));
	contents->Clear();
}

void Window_ShopNumber::SetData(int item_id, int item_max, int price) {
	this->item_id = item_id;
	this->item_max = item_max;
	this->price = price;
	number = 1;
}

void Window_ShopNumber::Refresh() {
	contents->Clear();

	int y = 34;
	const bool rtl = Tr::IsRtlLanguage();
	// (Shop) items are guaranteed to be valid
	const auto* item = lcf::ReaderUtil::GetElement(lcf::Data::items, item_id);
	if (rtl) {
		contents->TextDraw(contents->GetWidth(), y, Font::ColorDefault, item->name, Text::AlignRight);
	} else {
		DrawItemName(*item, 0, y);
	}

	std::stringstream ss;
	ss << number;

	const int quantity_x = rtl ? contents->GetWidth() - 162 : 132;
	contents->TextDraw(quantity_x, y, Font::ColorDefault, "x");
	contents->TextDraw(quantity_x + 30, y, Font::ColorDefault, ss.str(), Text::AlignRight);
	if (item_max >= 100) {
		const Rect original(132 + 8, y - 2, 26, 16);
		SetCursorRect(rtl ? Rect(contents->GetWidth() - original.x - original.width, original.y, original.width, original.height) : original);
	} else {
		const Rect original(132 + 14, y - 2, 20, 16);
		SetCursorRect(rtl ? Rect(contents->GetWidth() - original.x - original.width, original.y, original.width, original.height) : original);
	}

	DrawCurrencyValue(GetTotal(), contents->GetWidth(), y + 32);
}

int Window_ShopNumber::GetNumber() const {
	return number;
}

void Window_ShopNumber::Update() {
	Window_Base::Update();

	if (active) {
		int last_number = number;
		if (Input::IsRepeated(Input::RIGHT) && number < item_max) {
			number++;
		} else if (Input::IsRepeated(Input::LEFT) && number > 1) {
			number--;
		} else if ((Input::IsRepeated(Input::UP) || Input::IsTriggered(Input::SCROLL_UP))
			&& number < item_max) {
			number = min(number + 10, item_max);
		} else if ((Input::IsRepeated(Input::DOWN) || Input::IsTriggered(Input::SCROLL_DOWN))
			&& number > 1) {
			number = max(number - 10, 1);
		}

		if (last_number != number) {
			Main_Data::game_system->SePlay(Main_Data::game_system->GetSystemSE(Main_Data::game_system->SFX_Cursor));
			Refresh();
		}
	}
}

int Window_ShopNumber::GetTotal() const {
	return price * number;
}
