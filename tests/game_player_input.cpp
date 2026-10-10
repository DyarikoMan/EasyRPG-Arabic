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

TEST_CASE("Pixel movement setting does not change tile movement yet") {
	ProjectSettingsGuard settings_guard;
	Player::project_settings = { false, true };
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
	CHECK(ch.GetDirection() == Game_Character::Right);
	CHECK(Main_Data::game_party->GetSteps() == 1);
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
