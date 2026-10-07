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

#include <vector>

#include "scene.h"
#include "window_name.h"
#include "window_face.h"
#include "window_keyboard.h"

struct Scene_Name_KeyboardPages {
	std::vector<Window_Keyboard::Mode> modes;
	int initial_index;
};

/**
 * Builds the page sequence for the name keyboard.
 * Arabic sessions always start on Arabic; other sessions retain the charset
 * pages and requested initial index passed by the caller.
 */
Scene_Name_KeyboardPages BuildSceneNameKeyboardPages(bool arabic_session, int charset,
		std::vector<Window_Keyboard::Mode> charset_pages);

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
