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
#include <string>
#include <lcf/data.h>
#include <lcf/rpg/terrain.h>
#include "cache.h"
#include "background.h"
#include "bitmap.h"
#include "main_data.h"
#include <lcf/reader_util.h>
#include "output.h"
#include "drawable_mgr.h"
#include "game_screen.h"
#include "player.h"
#include "filefinder.h"
#include "widescreen_asset.h"

Background::Background(const std::string& name) : Drawable(Priority_Background)
{
	DrawableMgr::Register(this);

	if (!name.empty()) {
		RequestGraphic("Backdrop", name, false);
	}
}

Background::Background(int terrain_id) : Drawable(Priority_Background)
{
	DrawableMgr::Register(this);

	const lcf::rpg::Terrain* terrain = lcf::ReaderUtil::GetElement(lcf::Data::terrains, terrain_id);

	if (!terrain) {
		Output::Warning("Background: Invalid terrain ID {}", terrain_id);
		return;
	}

	// Either background or frame
	if (terrain->background_type == lcf::rpg::Terrain::BGAssociation_background && !terrain->background_name.empty()) {
		RequestGraphic("Backdrop", ToString(terrain->background_name), false);
		return;
	}

	// Frame
	if (!terrain->background_a_name.empty()) {
		RequestGraphic("Frame", ToString(terrain->background_a_name), false);

		bg_hscroll = terrain->background_a_scrollh ? terrain->background_a_scrollh_speed : 0;
		bg_vscroll = terrain->background_a_scrollv ? terrain->background_a_scrollv_speed : 0;
	}

	if (terrain->background_b && !terrain->background_b_name.empty()) {
		RequestGraphic("Frame", ToString(terrain->background_b_name), true);

		fg_hscroll = terrain->background_b_scrollh ? terrain->background_b_scrollh_speed : 0;
		fg_vscroll = terrain->background_b_scrollv ? terrain->background_b_scrollv_speed : 0;
	}
}

void Background::RequestGraphic(const std::string& directory, const std::string& original_name, bool foreground, bool allow_wide) {
	const bool wide_candidate = allow_wide && WidescreenAsset::ShouldTryWide(Player::screen_width);
	const std::string name = wide_candidate ? WidescreenAsset::MakeWideSibling(original_name) : original_name;
	FileRequestAsync* request = AsyncHandler::RequestFile(directory, name);
	request->SetGraphicFile(true);
	if (foreground) {
		fg_request_id = request->Bind(&Background::OnForegroundFrameGraphicReady, this, std::string(original_name), wide_candidate);
	} else {
		bg_request_id = request->Bind(&Background::OnBackgroundGraphicReady, this, std::string(original_name), wide_candidate);
	}
	request->Start();
}

void Background::OnBackgroundGraphicReady(FileRequestResult* result, std::string original_name, bool wide_candidate) {
	bool wide_asset = false;
	if (wide_candidate) {
		const bool wide_available = result->success && static_cast<bool>(FileFinder::OpenImage(result->directory, result->file));
		if (WidescreenAsset::SelectName(original_name, Player::screen_width, wide_available) == original_name) {
			RequestGraphic(result->directory, original_name, false, false);
			return;
		}
		wide_asset = true;
	}

	if (result->directory == "Backdrop") {
		bg_bitmap = Cache::Backdrop(result->file);
		bg_wide = wide_asset;
	}
	else if (result->directory == "Frame") {
		bg_bitmap = Cache::Frame(result->file, false);
		bg_wide = wide_asset;
	}
}

void Background::OnForegroundFrameGraphicReady(FileRequestResult* result, std::string original_name, bool wide_candidate) {
	bool wide_asset = false;
	if (wide_candidate) {
		const bool wide_available = result->success && static_cast<bool>(FileFinder::OpenImage(result->directory, result->file));
		if (WidescreenAsset::SelectName(original_name, Player::screen_width, wide_available) == original_name) {
			RequestGraphic(result->directory, original_name, true, false);
			return;
		}
		wide_asset = true;
	}

	fg_bitmap = Cache::Frame(result->file);
	fg_wide = wide_asset;
}

void Background::Update(int& rate, int& value) {
	int step =
		(rate > 0) ? 2 << rate :
		(rate < 0) ? 2 << -rate :
		0;
	value += step;
}

void Background::Update() {
	Update(bg_hscroll, bg_x);
	Update(bg_vscroll, bg_y);
	Update(fg_hscroll, fg_x);
	Update(fg_vscroll, fg_y);
}

int Background::Scale(int x) {
	return x > 0 ? x / 64 : -(-x / 64);
}

void Background::Draw(Bitmap& dst) {
	Rect bg_dst_rect = dst.GetRect();
	Rect fg_dst_rect = bg_dst_rect;
	if (!bg_wide && !fg_wide) {
		// Preserve the existing classic background layout.
		BitmapRef center_bitmap = bg_bitmap ? bg_bitmap : fg_bitmap;
		if (center_bitmap) {
			if (center_bitmap->GetWidth() < Player::screen_width) {
				bg_dst_rect.x += Player::menu_offset_x;
				bg_dst_rect.width = MENU_WIDTH;
			}
			if (center_bitmap->GetHeight() < Player::screen_height) {
				bg_dst_rect.y += Player::menu_offset_y;
				bg_dst_rect.height = MENU_HEIGHT;
			}
		}
		fg_dst_rect = bg_dst_rect;
	} else {
		const auto get_destination_rect = [this, &dst](const BitmapRef& bitmap, bool wide_asset) {
			Rect rect = dst.GetRect();
			if (bitmap && bitmap->GetWidth() < Player::screen_width) {
				if (wide_asset) {
					rect.x += (Player::screen_width - bitmap->GetWidth()) / 2;
					rect.width = bitmap->GetWidth();
				} else {
					rect.x += Player::menu_offset_x;
					rect.width = MENU_WIDTH;
				}
			}
			if (bitmap && bitmap->GetHeight() < Player::screen_height) {
				if (wide_asset) {
					rect.y += (Player::screen_height - bitmap->GetHeight()) / 2;
					rect.height = bitmap->GetHeight();
				} else {
					rect.y += Player::menu_offset_y;
					rect.height = MENU_HEIGHT;
				}
			}
			rect.x += Main_Data::game_screen->GetShakeOffsetX();
			rect.y += Main_Data::game_screen->GetShakeOffsetY();
			return rect;
		};

		bg_dst_rect = get_destination_rect(bg_bitmap, bg_wide);
		fg_dst_rect = get_destination_rect(fg_bitmap, fg_wide);
	}

	if (!bg_wide && !fg_wide) {
		bg_dst_rect.x += Main_Data::game_screen->GetShakeOffsetX();
		bg_dst_rect.y += Main_Data::game_screen->GetShakeOffsetY();
		fg_dst_rect = bg_dst_rect;
	}

	if (bg_bitmap)
		dst.TiledBlit(-Scale(bg_x), -Scale(bg_y), bg_bitmap->GetRect(), *bg_bitmap, bg_dst_rect, 255);

	if (fg_bitmap)
		dst.TiledBlit(-Scale(fg_x), -Scale(fg_y), fg_bitmap->GetRect(), *fg_bitmap, fg_dst_rect, 255);

	if (tone_effect != Tone()) {
		dst.ToneBlit(0, 0, dst, dst.GetRect(), tone_effect, Opacity::Opaque());
	}
}
