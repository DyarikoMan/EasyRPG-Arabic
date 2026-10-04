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
#include <iomanip>
#include <sstream>
#include "game_actors.h"
#include "window_equipstatus.h"
#include "bitmap.h"
#include "font.h"
#include "player.h"
#include "translation.h"

Window_EquipStatus::Window_EquipStatus(int ix, int iy, int iwidth, int iheight, const Game_Actor& actor) :
	Window_Base(ix, iy, iwidth, iheight),
	actor(actor),
	draw_params(false),
	dirty(true) {

	SetContents(Bitmap::Create(width - 16, height - 16));

	Refresh();
}

void Window_EquipStatus::Refresh() {
	if (dirty) {
		contents->Clear();

		int y_offset;

		y_offset = 18;
		// Actor data is guaranteed to be valid
		if (Tr::IsRtlLanguage()) {
			contents->TextDraw(contents->GetWidth(), 2, Font::ColorDefault, actor.GetName(), Text::AlignRight);
		} else {
			DrawActorName(actor, 0, 2);
		}

		for (int i = 0; i < 4; ++i) {
			DrawParameter(0, y_offset + ((12 + 4) * i), i);
		}

		dirty = false;
	}
}

void Window_EquipStatus::SetNewParameters(
	int new_atk, int new_def, int new_spi, int new_agi) {
	draw_params = true;

	dirty = true;

	atk = new_atk;
	def = new_def;
	spi = new_spi;
	agi = new_agi;
}

void Window_EquipStatus::ClearParameters() {
	if (draw_params != false) {
		draw_params = false;
		dirty = true;
		Refresh();
	}
}

int Window_EquipStatus::GetNewParameterColor(int old_value, int new_value) {
	if (old_value == new_value) {
		return 0;
	} else if (old_value < new_value) {
		return 2;
	} else {
		return 3;
	}
}

void Window_EquipStatus::DrawParameter(int cx, int cy, int type) {
	std::string_view name;
	int value;
	int new_value;

	switch (type) {
	case 0:
		name = lcf::Data::terms.attack;
		value = actor.GetAtk();
		new_value = atk;
		break;
	case 1:
		name = lcf::Data::terms.defense;
		value = actor.GetDef();
		new_value = def;
		break;
	case 2:
		name = lcf::Data::terms.spirit;
		value = actor.GetSpi();
		new_value = spi;
		break;
	case 3:
		name = lcf::Data::terms.agility;
		value = actor.GetAgi();
		new_value = agi;
		break;
	default:
		return;
	}

	if (Tr::IsRtlLanguage()) {
		const Font& draw_font = *(font ? font : Font::Default());
		const int label_right = contents->GetWidth() - 2;
		const int label_width = Text::GetSize(draw_font, name).width;
		const int current_right = label_right - label_width - 8;
		contents->TextDraw(label_right, cy, 1, name, Text::AlignRight);
		contents->TextDraw(current_right, cy, Font::ColorDefault, std::to_string(value), Text::AlignRight);

		if (draw_params) {
			std::string_view arrow;
			if (lcf::Data::terms.easyrpg_equipment_arrow == lcf::Data::terms.kDefaultTerm) {
				arrow = Player::IsCP932() ? "→" : ">";
			} else {
				arrow = lcf::Data::terms.easyrpg_equipment_arrow;
			}
			const int arrow_width = Text::GetSize(draw_font, arrow).width;
			const int arrow_right = current_right - 6;
			if (lcf::Data::terms.easyrpg_equipment_arrow == lcf::Data::terms.kDefaultTerm && !Player::IsCP932()) {
				contents->TextDraw(arrow_right - arrow_width / 2, cy, 1, arrow);
			} else {
				contents->TextDraw(arrow_right, cy, 1, arrow, Text::AlignRight);
			}

			const int new_value_right = arrow_right - arrow_width - 6;
			contents->TextDraw(new_value_right, cy, GetNewParameterColor(value, new_value), std::to_string(new_value), Text::AlignRight);
		}
		return;
	}

	// Check if 4 digits are needed instead of 3
	int limit = actor.MaxStatBaseValue();
	bool more_space_needed = (Player::IsRPG2k3() && limit >= 500) || limit >= 1000;

	// Draw Term
	contents->TextDraw(cx, cy, 1, name);

	// Draw Value
	cx += (more_space_needed ? (8 * 6 + 6 * 4) : (10 * 6 + 6 * 3));
	contents->TextDraw(cx, cy, Font::ColorDefault, std::to_string(value), Text::AlignRight);

	if (draw_params) {
		if (lcf::Data::terms.easyrpg_equipment_arrow == lcf::Data::terms.kDefaultTerm) {
			if (Player::IsCP932()) {
				// Draw fullwidth arrow
				contents->TextDraw(cx, cy, 1, "→");
			} else {
				// Draw arrow (+3 for center between the two numbers)
				contents->TextDraw(cx + 3, cy, 1, ">");
			}
		} else {
			// Draw arrow
			int offset = (12 - Text::GetSize(*Font::Default(), lcf::Data::terms.easyrpg_equipment_arrow).width) / 2;
			contents->TextDraw(cx + offset, cy, 1, lcf::Data::terms.easyrpg_equipment_arrow);
		}

		// Draw New Value
		cx += 6 * 2 + (more_space_needed ? (6 * 4) : (6 * 3));
		int color = GetNewParameterColor(value, new_value);
		contents->TextDraw(cx, cy, color, std::to_string(new_value), Text::AlignRight);
	}
}
