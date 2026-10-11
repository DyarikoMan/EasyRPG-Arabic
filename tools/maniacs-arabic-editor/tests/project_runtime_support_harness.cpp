#include "../src/project_runtime_support.cpp"

#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <vector>

namespace {

std::wstring captured_application;
std::wstring captured_command_line;
std::wstring captured_current_directory;

BOOL WINAPI CaptureCreateProcessW(
	LPCWSTR application_name,
	LPWSTR command_line,
	LPSECURITY_ATTRIBUTES,
	LPSECURITY_ATTRIBUTES,
	BOOL,
	DWORD,
	LPVOID,
	LPCWSTR current_directory,
	LPSTARTUPINFOW,
	LPPROCESS_INFORMATION) {
	captured_application = application_name == nullptr ? L"" : application_name;
	captured_command_line = command_line == nullptr ? L"" : command_line;
	captured_current_directory = current_directory == nullptr ? L"" : current_directory;
	return TRUE;
}

void Require(bool condition, const char* message) {
	if (!condition) { throw std::runtime_error(message); }
}

void WriteBytes(const std::filesystem::path& path, const std::vector<char>& bytes) {
	std::ofstream output(path, std::ios::binary);
	output.write(bytes.data(), static_cast<std::streamsize>(bytes.size()));
	if (!output) { throw std::runtime_error("could not write harness fixture"); }
}

std::vector<char> ReadBytes(const std::filesystem::path& path) {
	std::ifstream input(path, std::ios::binary);
	return std::vector<char>(std::istreambuf_iterator<char>(input), std::istreambuf_iterator<char>());
}

} // namespace

int wmain(int argc, wchar_t** argv) {
	if (argc != 2) {
		std::wcerr << L"Usage: project_runtime_support_harness <managed Player.exe>\n";
		return 2;
	}

	wchar_t temporary_directory[MAX_PATH]{};
	const DWORD temporary_length = GetTempPathW(MAX_PATH, temporary_directory);
	if (temporary_length == 0 || temporary_length >= MAX_PATH) {
		std::cerr << "could not locate the Windows temporary directory\n";
		return 2;
	}

	const std::filesystem::path root = std::filesystem::path(temporary_directory)
		/ (L"EasyRpgArabicHookHarness-" + std::to_wstring(GetCurrentProcessId()));
	try {
		std::filesystem::remove_all(root);
		const auto managed = root / L"managed";
		const auto new_project = root / L"new-project";
		const auto existing_project = root / L"existing-project";
		std::filesystem::create_directories(managed);
		std::filesystem::create_directories(new_project);
		std::filesystem::create_directories(existing_project);

		managed_player_path = FullPath(argv[1]);
		managed_runtime_path = FullPath((managed / L"RPG_RT.exe").wstring());
		managed_database_template_path = FullPath((managed / L"RPG_RT.ldb.dat").wstring());
		Require(FileExists(managed_player_path), "the rebuilt managed Player.exe is missing");
		WriteBytes(managed / L"RPG_RT.exe", {'r', 'u', 'n', 't', 'i', 'm', 'e'});
		WriteBytes(managed / L"RPG_RT.ldb.dat", {'d', 'a', 't', 'a', 'b', 'a', 's', 'e'});
		WriteBytes(new_project / L"RPG_RT.exe", {'p', 'r', 'o', 'j', 'e', 'c', 't'});
		WriteBytes(new_project / L"RPG_RT.ldb", {'d', 'a', 't', 'a', 'b', 'a', 's', 'e'});
		WriteBytes(new_project / L"Map0001.lmu", {'m', 'a', 'p'});
		WriteBytes(existing_project / L"RPG_RT.ini", {'[', 'E', 'a', 's', 'y', 'R', 'P', 'G', ']'});

		original_copy_file_w = ::CopyFileW;
		const std::wstring new_runtime = FullPath((new_project / L"RPG_RT.exe").wstring());
		Require(CopyPlayerTemplate(new_runtime), "a newly created project did not receive Player.exe");
		Require(ReadBytes(new_project / L"Player.exe") == ReadBytes(managed_player_path),
			"the project Player.exe differs from the rebuilt managed Player payload");

		candidate_project_directory.clear();
		ready_project_directory.clear();
		tracked_map_directory.clear();
		tracked_map_read_handle.store(nullptr, std::memory_order_release);
		candidate_updater_started = false;
		const std::wstring existing_map = FullPath((existing_project / L"Map0001.lmu").wstring());
		Require(!IsCandidateMapRead(existing_map, GENERIC_READ, OPEN_EXISTING),
			"opening an existing project was classified as new-project initialization");
		Require(ReadBytes(existing_project / L"RPG_RT.ini") == std::vector<char>({'[', 'E', 'a', 's', 'y', 'R', 'P', 'G', ']'}),
			"opening an existing project changed its INI fixture");

		RecordNewProjectRuntimeCopy(new_runtime);
		RecordNewProjectDatabaseCopy(
			managed_database_template_path,
			FullPath((new_project / L"RPG_RT.ldb").wstring()));
		const std::wstring new_map = FullPath((new_project / L"Map0001.lmu").wstring());
		Require(IsCandidateMapRead(new_map, GENERIC_READ, OPEN_EXISTING),
			"a real new project was not recognized for initialization");

		original_create_process_w = CaptureCreateProcessW;
		std::wstring play_command = L"\"RPG_RT.exe\" --test-play";
		Require(HookCreateProcessW(nullptr, play_command.data(), nullptr, nullptr, FALSE, 0, nullptr,
			new_project.c_str(), nullptr, nullptr) != FALSE, "the Play launch hook failed");
		const std::wstring project_player = FullPath((new_project / L"Player.exe").wstring());
		Require(SamePath(captured_application, project_player),
			"yellow Play did not launch the project-local Player.exe");
		Require(captured_command_line.find(project_player) != std::wstring::npos &&
			captured_command_line.find(L"--test-play") != std::wstring::npos,
			"yellow Play did not preserve arguments while redirecting to Player.exe");
		Require(SamePath(captured_current_directory, FullPath(new_project.wstring())),
			"yellow Play did not keep the project directory as its working directory");

		std::filesystem::remove(new_project / L"Player.exe");
		const std::wstring project_runtime = FullPath((new_project / L"RPG_RT.exe").wstring());
		std::wstring fallback_command = L"\"RPG_RT.exe\" --fallback";
		Require(HookCreateProcessW(project_runtime.c_str(), fallback_command.data(), nullptr, nullptr, FALSE, 0,
			nullptr, new_project.c_str(), nullptr, nullptr) != FALSE,
			"the original RPG_RT.exe fallback launch failed when project Player.exe was absent");
		Require(SamePath(captured_application, project_runtime) && captured_command_line == fallback_command,
			"the missing-Player fallback did not pass the original RPG_RT.exe launch through unchanged");

		std::filesystem::remove_all(root);
		std::cout << "PASS: project Player copy, existing-project guard, new-project detection, yellow Play redirection, and RPG_RT.exe fallback.\n";
		return 0;
	} catch (const std::exception& exception) {
		std::filesystem::remove_all(root);
		std::cerr << "FAIL: " << exception.what() << '\n';
		return 1;
	}
}
