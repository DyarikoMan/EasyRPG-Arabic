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

#include <cassert>
#include <utility>

#include "scene_name.h"
#include "game_actors.h"
#include "game_system.h"
#include "input.h"
#include "player.h"
#include "output.h"
#include "translation.h"

Scene_Name_WindowGeometry GetSceneNameWindowGeometry(bool rtl_session,
		int menu_offset_x, int menu_offset_y) {
	constexpr int margin_x = 32;
	constexpr int margin_y = 8;
	constexpr int window_face_width = 64;
	constexpr int window_face_height = 64;
	constexpr int window_name_width = 192;
	constexpr int window_name_height = 32;
	constexpr int window_keyboard_width = 256;
	constexpr int window_keyboard_height = 160;

	const int group_x = menu_offset_x + margin_x;
	return {
		rtl_session ? group_x + window_name_width : group_x,
		menu_offset_y + margin_y,
		window_face_width,
		window_face_height,
		rtl_session ? group_x : group_x + window_face_width,
		menu_offset_y + margin_y + 32,
		window_name_width,
		window_name_height,
		group_x,
		menu_offset_y + window_face_height + margin_y,
		window_keyboard_width,
		window_keyboard_height
	};
}

Scene_Name_KeyboardPages BuildSceneNameKeyboardPages(bool arabic_session, int charset,
		std::vector<Window_Keyboard::Mode> charset_pages) {
	if (arabic_session) {
		return {{Window_Keyboard::Arabic, Window_Keyboard::Letter, Window_Keyboard::Symbol}, 0};
	}

	charset_pages.push_back(Window_Keyboard::Letter);
	charset_pages.push_back(Window_Keyboard::Symbol);
	return {std::move(charset_pages), charset};
}

Scene_Name::Scene_Name(Game_Actor& actor, int charset, bool use_default_name)
	: layout_index(charset), use_default_name(use_default_name), actor(actor)
{
	Scene::type = Scene::Name;
}

void Scene_Name::Start() {
	const bool arabic_session = Tr::IsRtlLanguage();
	const auto geometry = GetSceneNameWindowGeometry(arabic_session,
			Player::menu_offset_x, Player::menu_offset_y);

	// Create the windows. RTL sessions mirror only the face/name horizontal group.
	face_window.reset(new Window_Face(geometry.face_x, geometry.face_y,
			geometry.face_width, geometry.face_height));
	face_window->Set(actor);
	face_window->Refresh();

	name_window.reset(new Window_Name(geometry.name_x, geometry.name_y, geometry.name_width, geometry.name_height));
	name_window->Set(use_default_name ? ToString(actor.GetName()) : "");
	name_window->Refresh();

	const char* done = Window_Keyboard::DONE;
	std::vector<Window_Keyboard::Mode> charset_pages;
	if (!arabic_session) {
		// Japanese pages
		if (Player::IsCP932()) {
			charset_pages.push_back(Window_Keyboard::Hiragana);
			charset_pages.push_back(Window_Keyboard::Katakana);
			done = Window_Keyboard::DONE_JP;
		// Korean pages
		} else if (Player::IsCP949()) {
			charset_pages.push_back(Window_Keyboard::Hangul1);
			charset_pages.push_back(Window_Keyboard::Hangul2);
			done = Window_Keyboard::DONE_KO;
		// Simp. Chinese pages
		} else if (Player::IsCP936()) {
			charset_pages.push_back(Window_Keyboard::ZhCn1);
			charset_pages.push_back(Window_Keyboard::ZhCn2);
			done = Window_Keyboard::DONE_ZH_CN;
		// Trad. Chinese pages
		} else if (Player::IsBig5()) {
			charset_pages.push_back(Window_Keyboard::ZhTw1);
			charset_pages.push_back(Window_Keyboard::ZhTw2);
			done = Window_Keyboard::DONE_ZH_TW;
		// Cyrillic page (we assume it's Russian since we have no way to detect Serbian etc.)
		} else if (Player::IsCP1251()) {
			charset_pages.push_back(Window_Keyboard::RuCyrl);
			done = Window_Keyboard::DONE_RU;
		}
	}

	auto page_selection = BuildSceneNameKeyboardPages(arabic_session, layout_index, std::move(charset_pages));
	layouts = std::move(page_selection.modes);
	layout_index = page_selection.initial_index;
	kbd_window.reset(new Window_Keyboard(geometry.keyboard_x, geometry.keyboard_y,
			geometry.keyboard_width, geometry.keyboard_height, done));

	auto next_index = layout_index + 1;
	if (next_index >= static_cast<int>(layouts.size())) {
		next_index = 0;
	}
	kbd_window->SetMode(layouts[layout_index], layouts[next_index]);

	kbd_window->Refresh();
	kbd_window->UpdateCursorRect();
}

void Scene_Name::vUpdate() {
	kbd_window->Update();
	name_window->Update();

	if (Input::IsTriggered(Input::CANCEL)) {
		if (name_window->Get().size() > 0) {
			Main_Data::game_system->SePlay(Main_Data::game_system->GetSystemSE(Main_Data::game_system->SFX_Cancel));
			name_window->Erase();
		} else {
			Main_Data::game_system->SePlay(Main_Data::game_system->GetSystemSE(Main_Data::game_system->SFX_Buzzer));
		}
	} else if (Input::IsTriggered(Input::DECISION)) {
		Main_Data::game_system->SePlay(Main_Data::game_system->GetSystemSE(Main_Data::game_system->SFX_Decision));
		std::string const& s = kbd_window->GetSelected();

		assert(!s.empty());

		if (s == Window_Keyboard::DONE) {
			if (name_window->Get().empty()) {
				name_window->Set(ToString(actor.GetName()));
				name_window->Refresh();
			} else {
				actor.SetName(name_window->Get());
				Scene::Pop();
			}
		} else if (s == Window_Keyboard::NEXT_PAGE) {
			++layout_index;
			if (layout_index >= static_cast<int>(layouts.size())) {
				layout_index = 0;
			}

			auto next_index = layout_index + 1;
			if (next_index >= static_cast<int>(layouts.size())) {
				next_index = 0;
			}
			kbd_window->SetMode(layouts[layout_index], layouts[next_index]);
		} else if (s == Window_Keyboard::SPACE) {
			name_window->Append(" ");
		} else {
			name_window->Append(s);
		}
	}
}
