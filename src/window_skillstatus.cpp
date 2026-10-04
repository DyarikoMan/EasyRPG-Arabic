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
#include "window_skillstatus.h"
#include "game_actor.h"
#include "game_actors.h"
#include "bitmap.h"
#include "font.h"
#include "player.h"
#include "translation.h"

Window_SkillStatus::Window_SkillStatus(int ix, int iy, int iwidth, int iheight) :
	Window_Base(ix, iy, iwidth, iheight) {

	SetContents(Bitmap::Create(width - 16, height - 16));
}

void Window_SkillStatus::SetActor(const Game_Actor& actor) {
	this->actor = &actor;
	Refresh();
}

void Window_SkillStatus::Refresh() {
	contents->ClearRect(Rect(0, 0, contents->GetWidth(), 16));
	if (Tr::IsRtlLanguage()) {
		const Font& draw_font = *(font ? font : Font::Default());
		const int contents_width = contents->GetWidth();
		const int y = 2;
		const lcf::rpg::State* state = actor->GetSignificantState();
		const std::string_view actor_name = actor->GetName();
		const std::string level_text = std::to_string(actor->GetLevel());
		const std::string_view state_name = state ? std::string_view(state->name) : std::string_view(lcf::Data::terms.normal_status);
		const std::string hp_text = std::to_string(actor->GetHp()) + "/" + std::to_string(actor->GetMaxHp());
		const std::string sp_text = std::to_string(actor->GetSp()) + "/" + std::to_string(actor->GetMaxSp());
		const int gap = 4;
		const int name_width = Text::GetSize(draw_font, actor_name).width;
		const int level_width = Text::GetSize(draw_font, lcf::Data::terms.lvl_short).width
			+ Text::GetSize(draw_font, level_text).width + gap;
		const int state_width = Text::GetSize(draw_font, state_name).width;
		const int hp_width = Text::GetSize(draw_font, hp_text).width
			+ Text::GetSize(draw_font, lcf::Data::terms.hp_short).width + gap;
		const int sp_width = Text::GetSize(draw_font, sp_text).width
			+ Text::GetSize(draw_font, lcf::Data::terms.sp_short).width + gap;

		// Pack measured fields from the right edge toward the left. Numeric values
		// stay as single LTR strings, with their Arabic labels on the right.
		int right = contents_width;
		contents->TextDraw(right, y, Font::ColorDefault, actor_name, Text::AlignRight);
		right -= name_width + gap;
		DrawActorLevel(*actor, right, y);
		right -= level_width + gap;
		contents->TextDraw(right, y, state ? state->color : Font::ColorDefault,
			state_name, Text::AlignRight);
		right -= state_width + gap;
		DrawActorHp(*actor, right, y, actor->MaxHpValue() >= 1000 ? 4 : 3);
		right -= hp_width + gap;
		DrawActorSp(*actor, std::max(sp_width, right), y, actor->MaxSpValue() >= 1000 ? 4 : 3);
		return;
	}

	// Actors are guaranteed to be valid
	int x = 0;
	int y = 2;
	DrawActorName(*actor, x, y);
	x += 80;
	DrawActorLevel(*actor, x, y);
	x += 44;
	DrawActorState(*actor, x, y);
	int hpdigits = (actor->MaxHpValue() >= 1000) ? 4 : 3;
	int spdigits = (actor->MaxSpValue() >= 1000) ? 4 : 3;
	x += (96 - hpdigits * 6 - spdigits * 6);
	DrawActorHp(*actor, x, y, hpdigits);
	x += (66 + hpdigits * 6 - spdigits * 6);
	DrawActorSp(*actor, x, y, spdigits);
}
