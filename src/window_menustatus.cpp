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
#include "window_menustatus.h"
#include "cache.h"
#include "game_party.h"
#include "player.h"
#include "bitmap.h"
#include "feature.h"
#include "translation.h"

Window_MenuStatus::Window_MenuStatus(int ix, int iy, int iwidth, int iheight) :
	Window_Selectable(ix, iy, iwidth, iheight) {

	if (Player::IsRPG2k3()) {
		SetContents(Bitmap::Create(width - 12, height - 16));
		SetBorderX(4);
		text_offset = 4;
	} else {
		SetContents(Bitmap::Create(width - 16, height - 16));
	}

	Refresh();
}

void Window_MenuStatus::Refresh() {
	contents->Clear();
	const bool rtl = Tr::IsRtlLanguage();

	item_max = Main_Data::game_party->GetActors().size();

	int y = 0;
	for (int i = 0; i < item_max; ++i) {
		// The party always contains valid battlers
		const Game_Actor& actor = *(Main_Data::game_party->GetActors()[i]);

		int face_x = 0;
		if (Player::IsRPG2k3()) {
			if (!Feature::HasRow()) {
				face_x = 4;
			} else {
				face_x = actor.GetBattleRow() == Game_Actor::RowType::RowType_back ? 8 : 0;
			}
		}
		if (rtl) {
			face_x = contents->GetWidth() - 48 - face_x;
		}
		DrawActorFace(actor, face_x, i*48 + y);

		if (rtl) {
			const int name_y = i * 48 + 2 + y;
			const int stats_y = i * 48 + 2 + 16 + y;
			const int exp_y = i * 48 + 2 + 16 + 16 + y;
			const Font& draw_font = *(font ? font : Font::Default());
			const int info_right = std::max(0, contents->GetWidth() - 48 - 8);
			const std::string_view actor_name = actor.GetName();
			const std::string_view actor_title = actor.GetTitle();
			const int name_width = Text::GetSize(draw_font, actor_name).width;
			contents->TextDraw(info_right, name_y, Font::ColorDefault, actor_name, Text::AlignRight);
			contents->TextDraw(std::max(0, info_right - name_width - 6), name_y,
				Font::ColorDefault, actor_title, Text::AlignRight);

			const lcf::rpg::State* state = actor.GetSignificantState();
			const std::string_view state_name = state ? std::string_view(state->name) : std::string_view(lcf::Data::terms.normal_status);
			const std::string level_text = std::to_string(actor.GetLevel());
			const int level_width = Text::GetSize(draw_font, lcf::Data::terms.lvl_short).width
				+ Text::GetSize(draw_font, level_text).width + 4;
			const int state_width = Text::GetSize(draw_font, state_name).width;
			const std::string hp_text = std::to_string(actor.GetHp()) + "/" + std::to_string(actor.GetMaxHp());
			const int hp_width = Text::GetSize(draw_font, hp_text).width
				+ Text::GetSize(draw_font, lcf::Data::terms.hp_short).width + 4;
			const int level_right = info_right;
			const int state_right = level_right - level_width - 4;
			const int hp_right = std::max(hp_width, state_right - state_width - 4);
			DrawActorLevel(actor, level_right, stats_y);
			DrawActorState(actor, state_right, stats_y);
			DrawActorHp(actor, hp_right, stats_y, actor.MaxHpValue() >= 1000 ? 4 : 3);

			constexpr int status_label_gap = 2;
			const std::string exp_text = actor.GetExpString() + "/" + actor.GetNextExpString();
			const int exp_value_width = Text::GetSize(draw_font, exp_text).width;
			const int exp_label_width = Text::GetSize(draw_font, lcf::Data::terms.exp_short).width;
			const std::string sp_text = std::to_string(actor.GetSp()) + "/" + std::to_string(actor.GetMaxSp());
			const int sp_width = Text::GetSize(draw_font, sp_text).width
				+ Text::GetSize(draw_font, lcf::Data::terms.sp_short).width + status_label_gap;
			const int exp_width = exp_value_width + exp_label_width + status_label_gap;
			const int exp_right = info_right;
			const int sp_right = std::max(sp_width, exp_right - exp_width - status_label_gap);
			DrawActorExp(actor, exp_right, exp_y, true, status_label_gap);
			DrawActorSp(actor, sp_right, exp_y, actor.MaxSpValue() >= 1000 ? 4 : 3, true, status_label_gap);
		} else {
			DrawActorName(actor, 48 + 8 + text_offset, i*48 + 2 + y);
			DrawActorTitle(actor, 48 + 8 + 88 + text_offset, i*48 + 2 + y);
			DrawActorLevel(actor, 48 + 8 + text_offset, i*48 + 2 + 16 + y);
			DrawActorState(actor, 48 + 8 + 42 + text_offset, i*48 + 2 + 16 + y);
			DrawActorExp(actor, 48 + 8 + text_offset, i*48 + 2 + 16 + 16 + y);
		}
		int digits = (actor.MaxHpValue() >= 1000 || actor.MaxSpValue() >= 1000) ? 4 : 3;
		const int hp_sp_x = 48 + 8 + 106 + text_offset - (digits == 3 ? 0 : 12);
		if (!rtl) {
			DrawActorHp(actor, hp_sp_x, i * 48 + 2 + 16 + y, digits);
			DrawActorSp(actor, hp_sp_x, i * 48 + 2 + 16 + 16 + y, digits);
		}

		y += 10;
	}
}

void Window_MenuStatus::UpdateCursorRect()
{
	if (index < 0) {
		cursor_rect = { 0, 0, 0, 0 };
	} else if (Tr::IsRtlLanguage()) {
		const int width = 168;
		const int original_x = 48 + 4 + text_offset;
		cursor_rect = { contents->GetWidth() - original_x - width, index * (48 + 10), width, 48 };
	} else {
		cursor_rect = { 48 + 4 + text_offset, index * (48 + 10), 168, 48 };
	}
}

Game_Actor* Window_MenuStatus::GetActor() const {
	return &(*Main_Data::game_party)[GetIndex()];
}
