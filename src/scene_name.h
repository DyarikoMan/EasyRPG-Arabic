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

#ifndef EP_SCENE_NAME_H
#define EP_SCENE_NAME_H

#include <string>
#include <string_view>
#include <vector>

#include "scene.h"
#include "window_name.h"
#include "window_face.h"
#include "window_keyboard.h"

struct Scene_Name_KeyboardPages {
	std::vector<Window_Keyboard::Mode> modes;
	int initial_index;
};

struct Scene_Name_WindowGeometry {
	int face_x;
	int face_y;
	int face_width;
	int face_height;
	int name_x;
	int name_y;
	int name_width;
	int name_height;
	int keyboard_x;
	int keyboard_y;
	int keyboard_width;
	int keyboard_height;
};

struct Scene_NameKeyboardLabels {
	std::string space;
	std::string done;
};

/**
 * Calculates name scene window geometry while keeping its existing dimensions
 * and vertical positions. RTL sessions mirror only the face/name horizontal
 * placement within their existing combined group.
 */
Scene_Name_WindowGeometry GetSceneNameWindowGeometry(bool rtl_session,
		int menu_offset_x, int menu_offset_y);

/**
 * Builds the page sequence for the name keyboard.
 * Arabic sessions always start on Arabic; other sessions retain the charset
 * pages and requested initial index passed by the caller.
 */
Scene_Name_KeyboardPages BuildSceneNameKeyboardPages(bool arabic_session, int charset,
		std::vector<Window_Keyboard::Mode> charset_pages);

namespace lcf { class INIReader; }

/**
 * Resolves Name Input button labels from [RPG_RT] in the game INI. Values are
 * decoded using the game's configured code page; empty or missing values do
 * not override the session defaults.
 */
Scene_NameKeyboardLabels ResolveSceneNameKeyboardLabels(bool rtl_session,
		std::string normal_done_text, std::string_view encoding,
		const lcf::INIReader* ini);

/**
 * Scene_Item class.
 */
class Scene_Name : public Scene {

public:
	/**
	 * Constructor.
	 */
	Scene_Name(Game_Actor& actor, int charset, bool use_default_name);

	void Start() override;
	void vUpdate() override;

protected:
	std::vector<Window_Keyboard::Mode> layouts;
	int layout_index = 0;
	bool use_default_name = false;

private:
	Game_Actor& actor;

	std::unique_ptr<Window_Keyboard> kbd_window;
	std::unique_ptr<Window_Name> name_window;
	std::unique_ptr<Window_Face> face_window;
};

#endif
