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
#include <algorithm>
#include "window_actortarget.h"
#include "game_actor.h"
#include "game_party.h"
#include "bitmap.h"
#include "player.h"
#include "font.h"
#include "translation.h"

Window_ActorTarget::Window_ActorTarget(int ix, int iy, int iwidth, int iheight) :
	Window_Selectable(ix, iy, iwidth, iheight) {

	SetContents(Bitmap::Create(width - 16, height - 16));

	Refresh();
}

void Window_ActorTarget::Refresh() {
	contents->Clear();

	item_max = Main_Data::game_party->GetActors().size();

	int y = 0;
	for (int i = 0; i < item_max; ++i) {
		const Game_Actor& actor = *(Main_Data::game_party->GetActors()[i]);

		if (Tr::IsRtlLanguage()) {
			const Font& draw_font = *(font ? font : Font::Default());
			const int info_right = contents->GetWidth() - 56;
			const int name_y = i * 48 + 2 + y;
			const int level_y = i * 48 + 2 + 16 + y;
			const int state_y = i * 48 + 2 + 16 + 16 + y;
			const std::string level_text = std::to_string(actor.GetLevel());
			const int level_width = Text::GetSize(draw_font, lcf::Data::terms.lvl_short).width
				+ Text::GetSize(draw_font, level_text).width + 4;
			const std::string hp_text = std::to_string(actor.GetHp()) + "/" + std::to_string(actor.GetMaxHp());
			const int hp_width = Text::GetSize(draw_font, hp_text).width
				+ Text::GetSize(draw_font, lcf::Data::terms.hp_short).width + 4;
			const lcf::rpg::State* state = actor.GetSignificantState();
			const std::string_view state_name = state ? std::string_view(state->name) : std::string_view(lcf::Data::terms.normal_status);
			const int state_width = Text::GetSize(draw_font, state_name).width;
			const std::string sp_text = std::to_string(actor.GetSp()) + "/" + std::to_string(actor.GetMaxSp());
			const int sp_width = Text::GetSize(draw_font, sp_text).width
				+ Text::GetSize(draw_font, lcf::Data::terms.sp_short).width + 4;
			const int hp_right = std::max(hp_width, info_right - level_width - 4);
			const int sp_right = std::max(sp_width, info_right - state_width - 4);

			DrawActorFace(actor, contents->GetWidth() - 48, i * 48 + y);
			contents->TextDraw(info_right, name_y, Font::ColorDefault, actor.GetName(), Text::AlignRight);
			DrawActorLevel(actor, info_right, level_y);
			DrawActorHp(actor, hp_right, level_y, actor.MaxHpValue() >= 1000 ? 4 : 3);
			DrawActorState(actor, info_right, state_y);
			DrawActorSp(actor, sp_right, state_y, actor.MaxSpValue() >= 1000 ? 4 : 3);
		} else {
			DrawActorFace(actor, 0, i * 48 + y);
			DrawActorName(actor, 48 + 8, i * 48 + 2 + y);
			DrawActorLevel(actor, 48 + 8, i * 48 + 2 + 16 + y);
			DrawActorState(actor, 48 + 8, i * 48 + 2 + 16 + 16 + y);
			int digits = (actor.MaxHpValue() >= 1000 || actor.MaxSpValue() >= 1000) ? 4 : 3;
			int x_offset = 48 + 8 + 46 + (digits == 3 ? 12 : 0);
			DrawActorHp(actor, x_offset, i * 48 + 2 + 16 + y, digits);
			DrawActorSp(actor, x_offset, i * 48 + 2 + 16 + 16 + y, digits);
		}

		y += 10;
	}
}

void Window_ActorTarget::UpdateCursorRect() {
	const int x = Tr::IsRtlLanguage() ? contents->GetWidth() - (48 + 4 + 120) : (48 + 4);
	if (index < -10) { // Entire Party
		cursor_rect = { x, 0, 120, item_max * (48 + 10) - 10 };
	} else if (index < 0) { // Fixed to one
		cursor_rect = { x, (-index - 1) * (48 + 10), 120, 48 };
	} else {
		cursor_rect = { x, index * (48 + 10), 120, 48 };
	}
}

Game_Actor* Window_ActorTarget::GetActor() {
	int ind = GetIndex();
	if (ind >= -10 && ind < 0) {
		ind = -ind - 1;
	}
	else if (ind == -100) {
		return nullptr;
	}

	return &(*Main_Data::game_party)[ind];
}
