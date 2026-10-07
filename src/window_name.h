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

#ifndef EP_WINDOW_NAME_H
#define EP_WINDOW_NAME_H

// Headers
#include <string>
#include "window_base.h"
#include "string_view.h"

/**
 * Window Name Class.
 */
class Window_Name :	public Window_Base {
public:
	struct TextDrawLayout {
		int anchor_x;
		bool align_right;
	};

	/**
	 * Constructor.
	 */
	Window_Name(int ix, int iy, int iwidth, int iheight, bool rtl_layout);

	bool IsRtlLayout() const;
	static TextDrawLayout GetTextDrawLayout(bool rtl_layout, int contents_width);
	static int GetCaretX(bool rtl_layout, int contents_width, int shaped_text_width);

	/**
	 * Renders the current name on the window.
	 */
	void Refresh();

	void Set(std::string text);
	void Append(std::string_view text);
	void Erase();
	const std::string& Get() const;

	void Update() override;

protected:
	std::string name;

private:
	const bool rtl_layout;
};

inline const std::string& Window_Name::Get() const {
	return name;
}

#endif
