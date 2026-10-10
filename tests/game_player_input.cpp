#include "game_player.h"
#include "doctest.h"
#include "options.h"
#include "game_map.h"
#include "main_data.h"
#include "game_system.h"
#include "game_constants.h"
#include "player.h"
#include "rand.h"
#include <climits>
#include <initializer_list>
#include <sstream>
#include <utility>

#include <lcf/inireader.h>

#include "mock_game.h"

TEST_SUITE_BEGIN("Game_Player Input");

static constexpr auto map_id = MockMap::ePassBlock20x15;

class ProjectSettingsGuard {
public:
	ProjectSettingsGuard() : original(Player::project_settings) {}
	~ProjectSettingsGuard() { Player::project_settings = original; }
	ProjectSettingsGuard(const ProjectSettingsGuard&) = delete;
	ProjectSettingsGuard& operator=(const ProjectSettingsGuard&) = delete;
private:
	Player::ProjectSettings original;
};

// FIXME: Test conditions which disable input

static void testPos(Game_Player& ch, int x, int y,
		int dir, int face,
		int remaining_step, bool jumping,
		int stop_count, int max_stop_count,
		int total_encounter_rate, bool encounter_calling,
		bool menu_calling,
		int vehicle, bool boarding, bool aboard)
{

	REQUIRE_EQ(ch.GetX(), x);
	REQUIRE_EQ(ch.GetY(), y);
	REQUIRE_EQ(ch.GetDirection(), dir);
	REQUIRE_EQ(ch.GetFacing(), face);
	REQUIRE_EQ(ch.GetRemainingStep(), remaining_step);
	REQUIRE_EQ(ch.IsJumping(), jumping);
	REQUIRE_EQ(ch.GetStopCount(), stop_count);
	REQUIRE_EQ(ch.GetMaxStopCount(), max_stop_count);
	REQUIRE_EQ(ch.GetTotalEncounterRate(), total_encounter_rate);
	REQUIRE_EQ(ch.IsEncounterCalling(), encounter_calling);
	REQUIRE_EQ(ch.IsMenuCalling(), menu_calling);
	REQUIRE_EQ(ch.GetVehicleType(),vehicle);
	REQUIRE_EQ(ch.IsBoardingOrUnboarding(), boarding);
	REQUIRE_EQ(ch.IsAboard(), aboard);
}

static void testMove(bool success, int input_dir, int dir, int x, int y, int dx, int dy, bool cheat, bool debug) {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = {};
	DebugGuard dg(debug);
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();

	CAPTURE(cheat);
	CAPTURE(debug);
	CAPTURE(input_dir);
	CAPTURE(dir);
	CAPTURE(x);
	CAPTURE(y);

	ch.SetX(x);
	ch.SetY(y);

	Input::ResetKeys();

	Input::dir4 = input_dir;
	Input::dir8 = input_dir;

	if (cheat) {
		Input::press_time[Input::DEBUG_THROUGH] = 1;
	}

	const auto tx = x + dx;
	const auto ty = y + dy;

	// Force RNG to always fail to generate a random encounter
	Rand::LockGuard lk(INT32_MAX);
	ForceUpdate(ch);
	if (success) {
		int acc_rate = (cheat && debug) ? 0 : 100;
		testPos(ch, tx, ty, dir, dir, 224, false, 0, 0, acc_rate, false, false, 0, false, false);
	} else {
		testPos(ch, tx, ty, dir, dir, 0, false, 1, 0, 0, false, false, 0, false, false);
	}
}

TEST_CASE("Move") {
	testMove(true, 8, Up, 4, 8, 0, -1, false, false);
	testMove(true, 6, Right, 4, 8, 1, 0, false, false);
	testMove(true, 2, Down, 4, 8, 0, 1, false, false);
	testMove(true, 4, Left, 4, 8, -1, 0, false, false);

	testMove(false, 8, Up, 16, 8, 0, 0, false, false);
	testMove(false, 6, Right, 16, 8, 0, 0, false, false);
	testMove(false, 2, Down, 16, 8, 0, 0, false, false);
	testMove(false, 4, Left, 16, 8, 0, 0, false, false);

	testMove(false, 8, Up, 16, 8, 0, 0, true, false);
	testMove(false, 6, Right, 16, 8, 0, 0, true, false);
	testMove(false, 2, Down, 16, 8, 0, 0, true, false);
	testMove(false, 4, Left, 16, 8, 0, 0, true, false);

	testMove(true, 8, Up, 16, 8, 0, -1, true, true);
	testMove(true, 6, Right, 16, 8, 1, 0, true, true);
	testMove(true, 2, Down, 16, 8, 0, 1, true, true);
	testMove(true, 4, Left, 16, 8, -1, 0, true, true);
}

TEST_CASE("Disabled eight-direction movement continues to use dir4") {
	ProjectSettingsGuard settings_guard;
	std::istringstream settings_stream(
		"[EasyRPG]\n"
		"EightDirectionMovement=false\n"
		"PixelMovement=false\n");
	lcf::INIReader settings_ini(settings_stream);
	Player::project_settings = Player::ParseProjectSettings(settings_ini);
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(8);
	Input::ResetKeys();
	Input::dir4 = Input::Direction::LEFT;
	Input::dir8 = Input::Direction::UPRIGHT;

	ForceUpdate(ch);

	CHECK(ch.GetX() == 7);
	CHECK(ch.GetY() == 8);
	CHECK(ch.GetDirection() == Game_Character::Left);
	CHECK(Main_Data::game_party->GetSteps() == 1);
}

TEST_CASE("Pixel movement disabled keeps the original tile movement interpolation") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { false, false };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(8);
	Input::ResetKeys();
	Input::dir4 = Input::Direction::RIGHT;
	Input::dir8 = Input::Direction::UPRIGHT;

	ForceUpdate(ch);

	CHECK(ch.GetX() == 9);
	CHECK(ch.GetY() == 8);
	CHECK(ch.GetRemainingStep() == 224);
	CHECK(ch.GetSpriteX() == 8 * SCREEN_TILE_SIZE + 32);
	CHECK(ch.GetSpriteY() == 8 * SCREEN_TILE_SIZE);
}

TEST_CASE("Pixel position initializes at logical tile centers") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { false, true };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(6);

	CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE);
	CHECK(ch.GetPixelY() == 6 * SCREEN_TILE_SIZE);
	CHECK(ch.GetSpriteX() == ch.GetPixelX());
	CHECK(ch.GetSpriteY() == ch.GetPixelY());
}

TEST_CASE("Pixel movement uses dir4 unless eight-direction movement is enabled") {
	ProjectSettingsGuard settings_guard;
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(8);
	Input::ResetKeys();
	Input::dir4 = Input::Direction::RIGHT;
	Input::dir8 = Input::Direction::UPRIGHT;

	Player::project_settings = { false, true };
	ForceUpdate(ch);
	CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + 16);
	CHECK(ch.GetPixelY() == 8 * SCREEN_TILE_SIZE);

	Player::project_settings = { true, true };
	ch.MoveTo(1, 8, 8);
	Input::dir4 = Input::Direction::DOWN;
	Input::dir8 = Input::Direction::UPRIGHT;
	ForceUpdate(ch);
	CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + 16);
	CHECK(ch.GetPixelY() == 8 * SCREEN_TILE_SIZE - 16);
	CHECK(ch.GetDirection() == Game_Character::UpRight);
}

TEST_CASE("Pixel movement scales speed levels at half the tile movement amount") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { false, true };
	for (const int speed : { 3, 4, 5 }) {
		const MockGame mg(map_id);
		auto& ch = *mg.GetPlayer();
		ch.SetX(8);
		ch.SetY(8);
		ch.SetMoveSpeed(speed);
		Input::ResetKeys();
		Input::dir4 = Input::Direction::RIGHT;

		ForceUpdate(ch);

		CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + (1 << speed));
	}
}

TEST_CASE("RunWithShift is ignored when pixel movement is disabled") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { false, false, true };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(8);
	Input::ResetKeys();
	Input::dir4 = Input::Direction::RIGHT;
	Input::press_time[Input::SHIFT] = 1;
	Rand::LockGuard lk(INT32_MAX);

	ForceUpdate(ch);

	CHECK(ch.GetX() == 9);
	CHECK(ch.GetY() == 8);
	CHECK(ch.GetRemainingStep() == 224);
	CHECK(ch.GetSpriteX() == 8 * SCREEN_TILE_SIZE + 32);
}

TEST_CASE("RunWithShift=false keeps pixel movement at walk speed even while Shift is held") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { false, true, false };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(8);
	Input::ResetKeys();
	Input::dir4 = Input::Direction::RIGHT;
	Input::press_time[Input::SHIFT] = 1;

	ForceUpdate(ch);

	CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + 16);
	CHECK(ch.GetPixelY() == 8 * SCREEN_TILE_SIZE);
}

TEST_CASE("RunWithShift doubles pixel walking and releasing Shift restores walk speed") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { false, true, true };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(8);
	Input::ResetKeys();
	Input::dir4 = Input::Direction::RIGHT;

	ForceUpdate(ch);
	CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + 16);

	Input::press_time[Input::SHIFT] = 1;
	ForceUpdate(ch);
	CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + 48);

	Input::press_time[Input::SHIFT] = 0;
	ForceUpdate(ch);
	CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + 64);
}

TEST_CASE("RunWithShift still collides with walls and solid events at run speed") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { false, true, true };

	SUBCASE("wall") {
		const MockGame mg(map_id);
		auto& ch = *mg.GetPlayer();
		ch.SetX(8);
		ch.SetY(8);
		Game_Map::GetMapDataDown()[8 * Game_Map::GetTilesX() + 9] = BLOCK_E + 1;
		Input::ResetKeys();
		Input::dir4 = Input::Direction::RIGHT;
		Input::press_time[Input::SHIFT] = 1;
		Rand::LockGuard lk(INT32_MAX);

		for (int i = 0; i < 4; ++i) {
			ForceUpdate(ch);
		}

		CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + SCREEN_TILE_SIZE / 4);
		CHECK(ch.GetX() == 8);
		CHECK(Main_Data::game_party->GetSteps() == 0);
	}

	SUBCASE("solid event") {
		const MockGame mg(map_id);
		auto& ch = *mg.GetPlayer();
		ch.SetX(8);
		ch.SetY(8);
		auto* event = Game_Map::GetEvent(1);
		REQUIRE(event);
		event->MoveTo(1, 9, 8);
		event->SetLayer(lcf::rpg::EventPage::Layers_same);
		event->SetThrough(false);
		Input::ResetKeys();
		Input::dir4 = Input::Direction::RIGHT;
		Input::press_time[Input::SHIFT] = 1;
		Rand::LockGuard lk(INT32_MAX);

		for (int i = 0; i < 4; ++i) {
			ForceUpdate(ch);
		}

		CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + SCREEN_TILE_SIZE / 4);
		CHECK(ch.GetX() == 8);
		CHECK(Main_Data::game_party->GetSteps() == 0);
	}
}

TEST_CASE("Diagonal RunWithShift doubles both axes with eight-direction movement") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { true, true, true };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(8);
	Input::ResetKeys();
	Input::dir4 = Input::Direction::DOWN;
	Input::dir8 = Input::Direction::UPRIGHT;
	Input::press_time[Input::SHIFT] = 1;

	ForceUpdate(ch);

	CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + 32);
	CHECK(ch.GetPixelY() == 8 * SCREEN_TILE_SIZE - 32);
	CHECK(ch.GetDirection() == Game_Character::UpRight);
}

TEST_CASE("Pixel walking animation is slower than the speed-4 tile cadence") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { false, true, true };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(8);
	ch.SetMoveSpeed(4);
	ch.SetAnimationType(lcf::rpg::EventPage::AnimType_non_continuous);
	ch.SetAnimCount(0);
	Input::ResetKeys();
	Input::dir4 = Input::Direction::RIGHT;
	Rand::LockGuard lk(INT32_MAX);
	const auto starting_frame = ch.GetAnimFrame();

	for (int i = 0; i < 8; ++i) {
		ForceUpdate(ch);
	}
	CHECK(ch.GetAnimFrame() == starting_frame);

	ForceUpdate(ch);
	CHECK(ch.GetAnimFrame() == starting_frame);
	ForceUpdate(ch);
	CHECK(ch.GetAnimFrame() != starting_frame);
}

TEST_CASE("Pixel running animation is faster than walking animation") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { false, true, true };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(8);
	ch.SetMoveSpeed(4);
	ch.SetAnimationType(lcf::rpg::EventPage::AnimType_non_continuous);
	ch.SetAnimCount(0);
	Input::ResetKeys();
	Input::dir4 = Input::Direction::RIGHT;
	Input::press_time[Input::SHIFT] = 1;
	Rand::LockGuard lk(INT32_MAX);
	const auto starting_frame = ch.GetAnimFrame();

	for (int i = 0; i < 4; ++i) {
		ForceUpdate(ch);
	}
	CHECK(ch.GetAnimFrame() == starting_frame);

	ForceUpdate(ch);
	CHECK(ch.GetAnimFrame() != starting_frame);
}

TEST_CASE("Releasing Shift restores pixel walking animation cadence immediately") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { false, true, true };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(8);
	ch.SetMoveSpeed(4);
	ch.SetAnimationType(lcf::rpg::EventPage::AnimType_non_continuous);
	ch.SetAnimCount(0);
	Input::ResetKeys();
	Input::dir4 = Input::Direction::RIGHT;
	Input::press_time[Input::SHIFT] = 1;
	Rand::LockGuard lk(INT32_MAX);
	const auto starting_frame = ch.GetAnimFrame();

	for (int i = 0; i < 4; ++i) {
		ForceUpdate(ch);
	}
	CHECK(ch.GetAnimFrame() == starting_frame);

	Input::press_time[Input::SHIFT] = 0;
	ForceUpdate(ch);
	CHECK(ch.GetAnimFrame() == starting_frame);
	ForceUpdate(ch);
	CHECK(ch.GetAnimFrame() != starting_frame);
}

TEST_CASE("Pixel movement animation changes do not affect tile movement or NPC animation") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { false, false, true };

	SUBCASE("player tile movement keeps the original speed-4 cadence") {
		const MockGame mg(map_id);
		auto& ch = *mg.GetPlayer();
		ch.SetX(8);
		ch.SetY(8);
		ch.SetMoveSpeed(4);
		ch.SetAnimationType(lcf::rpg::EventPage::AnimType_non_continuous);
		ch.SetAnimCount(0);
		Input::ResetKeys();
		Input::dir4 = Input::Direction::RIGHT;
		Input::press_time[Input::SHIFT] = 1;
		Rand::LockGuard lk(INT32_MAX);
		const auto starting_frame = ch.GetAnimFrame();

		for (int i = 0; i < 5; ++i) {
			ForceUpdate(ch);
		}
		CHECK(ch.GetAnimFrame() == starting_frame);

		ForceUpdate(ch);
		CHECK(ch.GetAnimFrame() != starting_frame);
	}

	SUBCASE("NPC animation keeps the original speed-4 cadence") {
		Player::project_settings = { false, true, true };
		const MockGame mg(map_id);
		auto* event = Game_Map::GetEvent(1);
		REQUIRE(event);
		event->SetX(4);
		event->SetY(4);
		event->SetMoveSpeed(4);
		event->SetAnimationType(lcf::rpg::EventPage::AnimType_non_continuous);
		event->SetAnimCount(0);
		REQUIRE(event->Move(Game_Character::Right));
		Input::ResetKeys();
		Input::press_time[Input::SHIFT] = 1;
		const auto starting_frame = event->GetAnimFrame();

		for (int i = 0; i < 5; ++i) {
			ForceUpdate(*event);
		}
		CHECK(event->GetAnimFrame() == starting_frame);

		ForceUpdate(*event);
		CHECK(event->GetAnimFrame() != starting_frame);
	}
}

TEST_CASE("Pixel movement holds, resumes, and reverses from fractional positions") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { false, true };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(8);
	Input::ResetKeys();
	Input::dir4 = Input::Direction::RIGHT;

	ForceUpdate(ch);
	ForceUpdate(ch);
	CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + 32);
	CHECK(ch.GetX() == 8);

	Input::dir4 = Input::Direction::NONE;
	ForceUpdate(ch);
	CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + 32);
	CHECK(ch.GetX() == 8);

	Input::dir4 = Input::Direction::RIGHT;
	ForceUpdate(ch);
	CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + 48);
	Input::dir4 = Input::Direction::LEFT;
	ForceUpdate(ch);
	CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + 32);
	CHECK(ch.GetX() == 8);
}

TEST_CASE("Pixel logical occupancy changes at the half-tile boundary") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { false, true };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(8);
	Input::ResetKeys();
	Input::dir4 = Input::Direction::RIGHT;
	const auto steps_before = Main_Data::game_party->GetSteps();
	Rand::LockGuard lk(INT32_MAX);

	for (int i = 0; i < 7; ++i) {
		ForceUpdate(ch);
	}
	CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + 112);
	CHECK(ch.GetX() == 8);
	CHECK(Main_Data::game_party->GetSteps() == steps_before);

	ForceUpdate(ch);
	CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + 128);
	CHECK(ch.GetX() == 9);
	CHECK(Main_Data::game_party->GetSteps() == steps_before + 1);
	CHECK(ch.GetTotalEncounterRate() == 100);
}

TEST_CASE("Pixel movement cannot cross blocked map or event cells") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { false, true };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(8);
	Game_Map::GetMapDataDown()[8 * Game_Map::GetTilesX() + 9] = BLOCK_E + 1;
	Input::ResetKeys();
	Input::dir4 = Input::Direction::RIGHT;
	Rand::LockGuard lk(INT32_MAX);

	for (int i = 0; i < 8; ++i) {
		ForceUpdate(ch);
	}
	CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + 64);
	CHECK(ch.GetX() == 8);
	CHECK(Main_Data::game_party->GetSteps() == 0);

	// A solid same-layer event must stop at the same occupancy boundary.
	Game_Map::GetMapDataDown()[8 * Game_Map::GetTilesX() + 9] = BLOCK_E;
	auto* event = Game_Map::GetEvent(1);
	REQUIRE(event);
	event->MoveTo(1, 9, 8);
	event->SetLayer(lcf::rpg::EventPage::Layers_same);
	event->SetThrough(false);
	ch.SetX(8);
	ch.SetY(8);
	for (int i = 0; i < 4; ++i) {
		ForceUpdate(ch);
	}
	CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + 64);
	CHECK(ch.GetX() == 8);
	CHECK(Main_Data::game_party->GetSteps() == 0);
}

TEST_CASE("Pixel collision footprint stops at walls on all cardinal sides") {
	struct WallCase {
		Input::Direction::InputDirection input;
		int dx;
		int dy;
	};
	const WallCase walls[] = {
		{ Input::Direction::RIGHT, 1, 0 },
		{ Input::Direction::LEFT, -1, 0 },
		{ Input::Direction::UP, 0, -1 },
		{ Input::Direction::DOWN, 0, 1 },
	};

	ProjectSettingsGuard settings_guard;
	Player::project_settings = { false, true };
	for (const auto& wall : walls) {
		const MockGame mg(map_id);
		auto& ch = *mg.GetPlayer();
		ch.SetX(8);
		ch.SetY(8);
		Game_Map::GetMapDataDown()[(8 + wall.dy) * Game_Map::GetTilesX() + 8 + wall.dx] = BLOCK_E + 1;
		Input::ResetKeys();
		Input::dir4 = wall.input;
		Input::dir8 = wall.input;

		for (int i = 0; i < 8; ++i) {
			ForceUpdate(ch);
		}

		const int expected_x = 8 * SCREEN_TILE_SIZE + wall.dx * SCREEN_TILE_SIZE / 4;
		const int expected_y = 8 * SCREEN_TILE_SIZE + wall.dy * SCREEN_TILE_SIZE / 4;
		CHECK(ch.GetPixelX() == expected_x);
		CHECK(ch.GetPixelY() == expected_y);
		CHECK(ch.GetX() == 8);
		CHECK(ch.GetY() == 8);
		CHECK(ch.GetSpriteX() == expected_x);
		CHECK(ch.GetSpriteY() == expected_y);
		if (wall.dx != 0) {
			CHECK(ch.GetPixelX() + wall.dx * SCREEN_TILE_SIZE / 4
				== 8 * SCREEN_TILE_SIZE + wall.dx * SCREEN_TILE_SIZE / 2);
		} else {
			CHECK(ch.GetPixelY() + wall.dy * SCREEN_TILE_SIZE / 4
				== 8 * SCREEN_TILE_SIZE + wall.dy * SCREEN_TILE_SIZE / 2);
		}
	}
}

TEST_CASE("Pixel movement checks every crossed tile and does not tunnel") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { false, true };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(8);
	ch.SetMoveSpeed(8);
	Game_Map::GetMapDataDown()[8 * Game_Map::GetTilesX() + 9] = BLOCK_E + 1;
	Input::ResetKeys();
	Input::dir4 = Input::Direction::RIGHT;
	Rand::LockGuard lk(INT32_MAX);

	ForceUpdate(ch);

	CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + 64);
	CHECK(ch.GetX() == 8);
	CHECK(Main_Data::game_party->GetSteps() == 0);
}

TEST_CASE("Pixel diagonal movement uses existing MakeWay corner routes") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { true, true };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(8);
	Game_Map::GetMapDataDown()[7 * Game_Map::GetTilesX() + 8] = BLOCK_E + 1;
	Input::ResetKeys();
	Input::dir4 = Input::Direction::DOWN;
	Input::dir8 = Input::Direction::UPRIGHT;
	Rand::LockGuard lk(INT32_MAX);

	for (int i = 0; i < 8; ++i) {
		ForceUpdate(ch);
	}
	CHECK(ch.GetX() == 9);
	CHECK(ch.GetY() == 7);
	CHECK(ch.GetPixelX() == 9 * SCREEN_TILE_SIZE - SCREEN_TILE_SIZE / 2);
	CHECK(ch.GetPixelY() == 7 * SCREEN_TILE_SIZE + SCREEN_TILE_SIZE / 2);
	CHECK(Main_Data::game_party->GetSteps() == 1);

	// The other route order remains valid when the tile to the right is blocked.
	ch.MoveTo(1, 8, 8);
	Game_Map::GetMapDataDown()[7 * Game_Map::GetTilesX() + 8] = BLOCK_E;
	Game_Map::GetMapDataDown()[8 * Game_Map::GetTilesX() + 9] = BLOCK_E + 1;
	for (int i = 0; i < 8; ++i) {
		ForceUpdate(ch);
	}
	CHECK(ch.GetX() == 9);
	CHECK(ch.GetY() == 7);
	CHECK(ch.GetPixelX() == 9 * SCREEN_TILE_SIZE - SCREEN_TILE_SIZE / 2);
	CHECK(ch.GetPixelY() == 7 * SCREEN_TILE_SIZE + SCREEN_TILE_SIZE / 2);
	CHECK(Main_Data::game_party->GetSteps() == 2);

	// Both initial exits blocked: the existing two-route MakeWay check fails.
	ch.MoveTo(1, 8, 8);
	Game_Map::GetMapDataDown()[7 * Game_Map::GetTilesX() + 8] = BLOCK_E + 1;
	Game_Map::GetMapDataDown()[8 * Game_Map::GetTilesX() + 9] = BLOCK_E + 1;
	for (int i = 0; i < 4; ++i) {
		ForceUpdate(ch);
	}
	CHECK(ch.GetX() == 8);
	CHECK(ch.GetY() == 8);
	CHECK(ch.GetPixelX() == 8 * SCREEN_TILE_SIZE + 64);
	CHECK(ch.GetPixelY() == 8 * SCREEN_TILE_SIZE - 64);
	CHECK(Main_Data::game_party->GetSteps() == 2);
}

TEST_CASE("Pixel player rendering and camera follow the continuous position") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { false, true };
	const MockGame mg(MockMap::ePass40x30);
	auto& ch = *mg.GetPlayer();
	ch.SetX(12);
	ch.SetY(8);
	const int camera_x = Game_Map::GetPositionX();
	Input::ResetKeys();
	Input::dir4 = Input::Direction::RIGHT;

	ForceUpdate(ch);

	CHECK(ch.GetSpriteX() == 12 * SCREEN_TILE_SIZE + 16);
	CHECK(Game_Map::GetPositionX() == camera_x + 16);
}

TEST_CASE("Pixel movement leaves NPC interpolation on the tile movement path") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { true, true };
	const MockGame mg(map_id);
	auto* event = Game_Map::GetEvent(1);
	REQUIRE(event);
	event->SetX(4);
	event->SetY(4);
	event->SetDirection(Game_Character::Left);
	event->SetRemainingStep(128);

	CHECK(event->GetSpriteX() == 4 * SCREEN_TILE_SIZE + 128);
	CHECK(event->GetSpriteY() == 4 * SCREEN_TILE_SIZE);
}

TEST_CASE("Pixel movement still triggers touch events on logical tile entry") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { true, true };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(4);
	ch.SetY(8);
	auto& map = const_cast<lcf::rpg::Map&>(Game_Map::GetMap());
	map.events.front().pages.front().trigger = lcf::rpg::EventPage::Trigger_touched;
	map.events.front().pages.front().event_commands.emplace_back();
	auto* event = Game_Map::GetEvent(1);
	REQUIRE(event);
	event->MoveTo(1, 5, 7);
	Input::ResetKeys();
	Input::dir4 = Input::Direction::UP;
	Input::dir8 = Input::Direction::UPRIGHT;

	for (int i = 0; i < 8; ++i) {
		ForceUpdate(ch);
	}

	CHECK(ch.GetX() == 5);
	CHECK(ch.GetY() == 7);
	CHECK(event->IsWaitingForegroundExecution());
}

TEST_CASE("Pixel movement keeps action events in front of the occupied tile") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { true, true };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(4);
	ch.SetY(8);
	auto& map = const_cast<lcf::rpg::Map&>(Game_Map::GetMap());
	map.events.front().pages.front().trigger = lcf::rpg::EventPage::Trigger_action;
	map.events.front().pages.front().event_commands.emplace_back();
	auto* event = Game_Map::GetEvent(1);
	REQUIRE(event);
	event->MoveTo(1, 5, 6);
	event->SetLayer(lcf::rpg::EventPage::Layers_same);
	Input::ResetKeys();
	Input::dir4 = Input::Direction::UP;
	Input::dir8 = Input::Direction::UPRIGHT;

	for (int i = 0; i < 8; ++i) {
		ForceUpdate(ch);
	}
	Input::dir4 = Input::Direction::NONE;
	Input::dir8 = Input::Direction::NONE;
	Input::triggered[Input::DECISION] = true;
	ForceUpdate(ch);

	CHECK(ch.GetX() == 5);
	CHECK(ch.GetY() == 7);
	CHECK(ch.GetDirection() == Game_Character::Up);
	CHECK(event->IsWaitingForegroundExecution());
}

TEST_CASE("Enabled eight-direction movement maps input directions and preserves cardinal movement") {
	struct DirectionCase {
		int input_direction;
		int expected_direction;
		int dx;
		int dy;
		int expected_facing;
	};
	const DirectionCase directions[] = {
		{ Input::Direction::DOWNLEFT, Game_Character::DownLeft, -1, 1, Game_Character::Down },
		{ Input::Direction::DOWN, Game_Character::Down, 0, 1, Game_Character::Down },
		{ Input::Direction::DOWNRIGHT, Game_Character::DownRight, 1, 1, Game_Character::Down },
		{ Input::Direction::LEFT, Game_Character::Left, -1, 0, Game_Character::Left },
		{ Input::Direction::RIGHT, Game_Character::Right, 1, 0, Game_Character::Right },
		{ Input::Direction::UPLEFT, Game_Character::UpLeft, -1, -1, Game_Character::Up },
		{ Input::Direction::UP, Game_Character::Up, 0, -1, Game_Character::Up },
		{ Input::Direction::UPRIGHT, Game_Character::UpRight, 1, -1, Game_Character::Up },
	};

	for (const auto& direction : directions) {
		ProjectSettingsGuard settings_guard;
		Player::project_settings = { true, false };
		const MockGame mg(map_id);
		auto& ch = *mg.GetPlayer();
		ch.SetX(4);
		ch.SetY(8);
		Input::ResetKeys();
		// Keep dir4 pointed elsewhere to ensure enabled movement consumes dir8.
		Input::dir4 = Input::Direction::DOWN;
		Input::dir8 = direction.input_direction;
		const auto steps_before = Main_Data::game_party->GetSteps();

		ForceUpdate(ch);

		CHECK(ch.GetX() == 4 + direction.dx);
		CHECK(ch.GetY() == 8 + direction.dy);
		CHECK(ch.GetDirection() == direction.expected_direction);
		CHECK(ch.GetFacing() == direction.expected_facing);
		CHECK(ch.IsMoving());
		CHECK(Main_Data::game_party->GetSteps() == steps_before + 1);
		CHECK(ch.GetTotalEncounterRate() == 100);
	}
}

static void testDiagonalCollision(std::initializer_list<std::pair<int, int>> blocked_tiles,
		bool should_move, int expected_x, int expected_y) {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { true, false };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(8);
	for (const auto& tile : blocked_tiles) {
		Game_Map::GetMapDataDown()[tile.second * Game_Map::GetTilesX() + tile.first] = BLOCK_E + 1;
	}
	Input::ResetKeys();
	Input::dir4 = Input::Direction::UP;
	Input::dir8 = Input::Direction::UPRIGHT;

	ForceUpdate(ch);

	CHECK(ch.GetX() == expected_x);
	CHECK(ch.GetY() == expected_y);
	CHECK(ch.GetDirection() == Game_Character::UpRight);
	CHECK(Main_Data::game_party->GetSteps() == (should_move ? 1 : 0));
	CHECK(ch.GetTotalEncounterRate() == (should_move ? 100 : 0));
}

TEST_CASE("Diagonal movement keeps MakeWay's two-route corner collision semantics") {
	SUBCASE("blocked tile above still allows the valid right-then-up route") {
		testDiagonalCollision({ { 8, 7 } }, true, 9, 7);
	}
	SUBCASE("blocked tile right still allows the valid up-then-right route") {
		testDiagonalCollision({ { 9, 8 } }, true, 9, 7);
	}
	SUBCASE("both blocked cardinal exits prevent cutting through the corner") {
		testDiagonalCollision({ { 8, 7 }, { 9, 8 } }, false, 8, 8);
	}
}

TEST_CASE("Diagonal movement still collides with map events") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { true, false };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	auto* event = Game_Map::GetEvent(1);
	REQUIRE(event);
	ch.SetX(8);
	ch.SetY(8);
	event->MoveTo(1, 9, 7);
	event->SetLayer(lcf::rpg::EventPage::Layers_same);
	event->SetThrough(false);
	Input::ResetKeys();
	Input::dir4 = Input::Direction::UP;
	Input::dir8 = Input::Direction::UPRIGHT;

	ForceUpdate(ch);

	CHECK(ch.GetX() == 8);
	CHECK(ch.GetY() == 8);
	CHECK(Main_Data::game_party->GetSteps() == 0);
}

TEST_CASE("Touch events still trigger after diagonal movement") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { true, false };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(4);
	ch.SetY(8);
	auto& map = const_cast<lcf::rpg::Map&>(Game_Map::GetMap());
	map.events.front().pages.front().trigger = lcf::rpg::EventPage::Trigger_touched;
	map.events.front().pages.front().event_commands.emplace_back();
	auto* event = Game_Map::GetEvent(1);
	REQUIRE(event);
	event->MoveTo(1, 5, 7);
	Input::ResetKeys();
	Input::dir4 = Input::Direction::UP;
	Input::dir8 = Input::Direction::UPRIGHT;

	ForceUpdate(ch);
	Input::dir4 = Input::Direction::NONE;
	Input::dir8 = Input::Direction::NONE;
	while (!ch.IsStopping()) {
		ForceUpdate(ch);
	}

	CHECK(ch.GetX() == 5);
	CHECK(ch.GetY() == 7);
	CHECK(event->IsWaitingForegroundExecution());
}

TEST_CASE("Eight-direction movement works aboard vehicles") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { true, false };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(8);
	auto save = ch.GetSaveData();
	save.vehicle = Game_Vehicle::Boat;
	save.aboard = true;
	ch.SetSaveData(std::move(save));
	auto* boat = Game_Map::GetVehicle(Game_Vehicle::Boat);
	REQUIRE(boat);
	boat->MoveTo(1, 8, 8);
	boat->SetThrough(true);
	Input::ResetKeys();
	Input::dir4 = Input::Direction::UP;
	Input::dir8 = Input::Direction::UPRIGHT;

	ForceUpdate(ch);

	CHECK(ch.GetX() == 9);
	CHECK(ch.GetY() == 7);
	CHECK(ch.GetDirection() == Game_Character::UpRight);
	CHECK(ch.IsAboard());
	CHECK(Main_Data::game_party->GetSteps() == 1);
}

TEST_CASE("Action button remains cardinal after diagonal movement") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { true, false };
	const MockGame mg(map_id);
	auto& ch = *mg.GetPlayer();
	ch.SetX(8);
	ch.SetY(8);
	Input::ResetKeys();
	Input::dir4 = Input::Direction::UP;
	Input::dir8 = Input::Direction::UPRIGHT;

	ForceUpdate(ch);
	CHECK(ch.GetDirection() == Game_Character::UpRight);
	Input::dir4 = Input::Direction::NONE;
	Input::dir8 = Input::Direction::NONE;
	while (!ch.IsStopping()) {
		ForceUpdate(ch);
	}

	Input::triggered[Input::DECISION] = true;
	ForceUpdate(ch);

	CHECK(ch.GetDirection() == ch.GetFacing());
	CHECK_FALSE(Game_Character::IsDirectionDiagonal(ch.GetDirection()));
	CHECK_FALSE(ch.IsAboard());
}

static void testDecision(bool moved, int vehicle, bool boarding, bool aboard) {
	auto& ch = *MockGame::GetPlayer();

	ch.SetX(8);
	ch.SetY(8);

	Input::ResetKeys();

	Input::triggered[Input::DECISION] = true;

	ForceUpdate(ch);

	int y = moved ? 9 : 8;
	int rs = moved ? 224 : 0;
	int stp = moved ? 0 : 1;
	testPos(ch, 8, y, Down, Down, rs, false, stp, 0, 0, false, false, vehicle, boarding, aboard);
}

TEST_CASE("DecisionNone") {
	const MockGame mg(map_id);

	testDecision(false, 0, false, false);
}

TEST_CASE("DecisionEvent") {
	const MockGame mg(map_id);

	auto* event = Game_Map::GetEvent(1);
	REQUIRE(event);
	event->MoveTo(1, 8, 9);

	testDecision(false, 0, false, false);

	// FIXME: Add map page which triggers
}

TEST_CASE("DecisionBoard") {
	const MockGame mg(map_id);
	auto& ch = *Main_Data::game_player;

	auto* vh = Game_Map::GetVehicle(Game_Vehicle::Boat);
	REQUIRE(vh);
	vh->MoveTo(1, 8, 9);

	testDecision(true, Game_Vehicle::Boat, true, false);

	while (!ch.IsStopping()) {
		ForceUpdate(ch);
	}

	testPos(ch, 8, 9, Down, Left, 0, false, 0, 0, 0, false, false, Game_Vehicle::Boat, 0, true);
}

TEST_CASE("DecisionUnboard") {
	const MockGame mg(map_id);

	// FIXME: Implement
}

static void testMenu(bool success) {
	auto& ch = *Main_Data::game_player;
	ch.SetX(8);
	ch.SetY(8);

	Input::ResetKeys();

	Input::triggered[Input::CANCEL] = true;

	ForceUpdate(ch);
	testPos(ch, 8, 8, Down, Down, 0, false, 1, 0, 0, false, success, 0, false, false);

	// FIXME: Test conditions which disable menu calling
}

TEST_CASE("CallMenu") {
	const MockGame mg(map_id);

	testMenu(true);
}

TEST_CASE("CallMenuDisabled") {
	const MockGame mg(map_id);

	Main_Data::game_system->SetAllowMenu(false);

	testMenu(false);
}

TEST_SUITE_END();
