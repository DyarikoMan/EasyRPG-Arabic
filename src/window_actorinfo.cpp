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
#include "window_actorinfo.h"
#include "game_actors.h"
#include "game_party.h"
#include "bitmap.h"
#include "font.h"
#include "feature.h"
#include "translation.h"

Window_ActorInfo::Window_ActorInfo(int ix, int iy, int iwidth, int iheight, const Game_Actor& actor) :
	Window_Base(ix, iy, iwidth, iheight),
	actor(actor) {

	SetContents(Bitmap::Create(width - 16, height - 16));

	Refresh();
}

void Window_ActorInfo::Refresh() {
	contents->Clear();

	DrawInfo();
}

void Window_ActorInfo::DrawInfo() {
	const bool rtl = Tr::IsRtlLanguage();
	const int right_edge = contents->GetWidth();
	const int face_x = rtl ? right_edge - 48 : 0;
	const int label_x = rtl ? right_edge : 0;
	const int value_x = rtl ? right_edge : 36;
	const auto label_align = rtl ? Text::AlignRight : Text::AlignLeft;
	const auto value_align = rtl ? Text::AlignRight : Text::AlignLeft;

	if (Feature::HasRow()) {
		// Draw Row formation.
		std::string battle_row = actor.GetBattleRow() == Game_Actor::RowType::RowType_back ? lcf::rpg::Terms::TermOrDefault(lcf::Data::terms.easyrpg_status_scene_back, "Back") : lcf::rpg::Terms::TermOrDefault(lcf::Data::terms.easyrpg_status_scene_front, "Front");
		contents->TextDraw(right_edge, 2, Font::ColorDefault, battle_row, Text::AlignRight);
	}

	// Draw Face
	DrawActorFace(actor, face_x, 0);

	// Draw Name
	contents->TextDraw(label_x, 50, 1, lcf::rpg::Terms::TermOrDefault(lcf::Data::terms.easyrpg_status_scene_name, "Name"), label_align);
	contents->TextDraw(value_x, 66, Font::ColorDefault, actor.GetName(), value_align);

	// Draw Profession
	contents->TextDraw(label_x, 82, 1, lcf::rpg::Terms::TermOrDefault(lcf::Data::terms.easyrpg_status_scene_class, "Class"), label_align);
	contents->TextDraw(value_x, 98, Font::ColorDefault, actor.GetClassName(), value_align);

	// Draw Rank
	contents->TextDraw(label_x, 114, 1, lcf::rpg::Terms::TermOrDefault(lcf::Data::terms.easyrpg_status_scene_title, "Title"), label_align);
	contents->TextDraw(value_x, 130, Font::ColorDefault, actor.GetTitle(), value_align);

	// Draw Status
	contents->TextDraw(label_x, 146, 1, lcf::rpg::Terms::TermOrDefault(lcf::Data::terms.easyrpg_status_scene_condition, "State"), label_align);
	const lcf::rpg::State* state = actor.GetSignificantState();
	contents->TextDraw(value_x, 162, state ? state->color : Font::ColorDefault,
		state ? state->name : lcf::Data::terms.normal_status, value_align);

	//Draw Level
	contents->TextDraw(label_x, 178, 1, lcf::Data::terms.level, label_align);
	contents->TextDraw(rtl ? right_edge - 78 : 78, 178, Font::ColorDefault, std::to_string(actor.GetLevel()), Text::AlignRight);
}
