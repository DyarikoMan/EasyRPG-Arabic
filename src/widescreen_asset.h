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

#ifndef EP_WIDESCREEN_ASSET_H
#define EP_WIDESCREEN_ASSET_H

#include <string>
#include <string_view>
#include "options.h"

namespace WidescreenAsset {
	inline bool ShouldTryWide(int logical_width) {
		return logical_width > SCREEN_TARGET_WIDTH;
	}

	inline std::string MakeWideSibling(std::string_view name) {
		return std::string(name) + "_wide";
	}

	inline std::string SelectName(std::string_view original_name, int logical_width, bool wide_available) {
		if (ShouldTryWide(logical_width) && wide_available) {
			return MakeWideSibling(original_name);
		}
		return std::string(original_name);
	}

	inline bool IsWideSibling(std::string_view name) {
		constexpr std::string_view suffix = "_wide";
		return name.size() >= suffix.size() && name.substr(name.size() - suffix.size()) == suffix;
	}
}

#endif
