#include <windows.h>

#include <MinHook.h>

#include <array>
#include <mutex>
#include <string>
#include <string_view>
#include <vector>

static_assert(sizeof(void*) == 4, "The Maniacs editor hook is x86 only.");

namespace {

using CopyFileAFunction = BOOL(WINAPI*)(LPCSTR, LPCSTR, BOOL);
using CopyFileWFunction = BOOL(WINAPI*)(LPCWSTR, LPCWSTR, BOOL);
using CreateProcessAFunction = BOOL(WINAPI*)(
	LPCSTR, LPSTR, LPSECURITY_ATTRIBUTES, LPSECURITY_ATTRIBUTES, BOOL, DWORD, LPVOID,
	LPCSTR, LPSTARTUPINFOA, LPPROCESS_INFORMATION);
using CreateProcessWFunction = BOOL(WINAPI*)(
	LPCWSTR, LPWSTR, LPSECURITY_ATTRIBUTES, LPSECURITY_ATTRIBUTES, BOOL, DWORD, LPVOID,
	LPCWSTR, LPSTARTUPINFOW, LPPROCESS_INFORMATION);

CopyFileAFunction original_copy_file_a = nullptr;
CopyFileWFunction original_copy_file_w = nullptr;
CreateProcessAFunction original_create_process_a = nullptr;
CreateProcessWFunction original_create_process_w = nullptr;

std::once_flag project_runtime_initialization_once;
bool project_runtime_initialization_succeeded = false;
thread_local unsigned int intercepted_api_depth = 0;

std::wstring managed_runtime_path;
std::wstring managed_player_path;

class InterceptionScope {
public:
	InterceptionScope() { ++intercepted_api_depth; }
	~InterceptionScope() { --intercepted_api_depth; }
};

bool IsSeparator(wchar_t value) {
	return value == L'\\' || value == L'/';
}

bool IsSeparator(char value) {
	return value == '\\' || value == '/';
}

template <typename Character>
std::basic_string_view<Character> LeafName(std::basic_string_view<Character> path) {
	const size_t separator = path.find_last_of(static_cast<Character>('/'));
	const size_t backslash = path.find_last_of(static_cast<Character>('\\'));
	const size_t last = separator == std::basic_string_view<Character>::npos
		? backslash
		: (backslash == std::basic_string_view<Character>::npos ? separator : (separator > backslash ? separator : backslash));
	return path.substr(last == std::basic_string_view<Character>::npos ? 0 : last + 1);
}

template <typename Character>
bool EqualsAsciiInsensitive(std::basic_string_view<Character> value, const char* ascii) {
	size_t index = 0;
	for (; index < value.size() && ascii[index] != '\0'; ++index) {
		Character left = value[index];
		char right = ascii[index];
		if (left >= static_cast<Character>('A') && left <= static_cast<Character>('Z')) {
			left = static_cast<Character>(left - static_cast<Character>('A') + static_cast<Character>('a'));
		}
		if (right >= 'A' && right <= 'Z') {
			right = static_cast<char>(right - 'A' + 'a');
		}
		if (left != static_cast<Character>(right)) {
			return false;
		}
	}
	return index == value.size() && ascii[index] == '\0';
}

bool IsRpgRtPath(const wchar_t* path) {
	return path != nullptr && EqualsAsciiInsensitive(LeafName(std::wstring_view(path)), "RPG_RT.exe");
}

bool IsRpgRtPath(const char* path) {
	return path != nullptr && EqualsAsciiInsensitive(LeafName(std::string_view(path)), "RPG_RT.exe");
}

std::wstring FullPath(const std::wstring& path) {
	if (path.empty()) {
		return {};
	}
	DWORD capacity = GetFullPathNameW(path.c_str(), 0, nullptr, nullptr);
	if (capacity == 0) {
		return {};
	}
	std::vector<wchar_t> buffer(static_cast<size_t>(capacity) + 1);
	DWORD length = GetFullPathNameW(path.c_str(), static_cast<DWORD>(buffer.size()), buffer.data(), nullptr);
	if (length == 0 || length >= buffer.size()) {
		return {};
	}
	return std::wstring(buffer.data(), length);
}

std::wstring FullPath(const wchar_t* path) {
	return path == nullptr ? std::wstring() : FullPath(std::wstring(path));
}

bool SamePath(const std::wstring& left, const std::wstring& right) {
	return !left.empty() && !right.empty() && CompareStringOrdinal(
		left.c_str(), -1, right.c_str(), -1, TRUE) == CSTR_EQUAL;
}

bool AnsiToWide(const char* text, std::wstring& result) {
	if (text == nullptr) {
		return false;
	}
	const int length = lstrlenA(text);
	if (length <= 0) {
		return false;
	}
	const int wide_length = MultiByteToWideChar(CP_ACP, MB_ERR_INVALID_CHARS, text, length, nullptr, 0);
	if (wide_length <= 0) {
		return false;
	}
	result.resize(static_cast<size_t>(wide_length));
	return MultiByteToWideChar(CP_ACP, MB_ERR_INVALID_CHARS, text, length, result.data(), wide_length) == wide_length;
}

bool WideToAnsi(const std::wstring& text, std::string& result) {
	if (text.empty()) {
		return false;
	}
	BOOL used_default_character = FALSE;
	const int length = WideCharToMultiByte(CP_ACP, WC_NO_BEST_FIT_CHARS, text.data(), static_cast<int>(text.size()),
		nullptr, 0, nullptr, &used_default_character);
	if (length <= 0 || used_default_character) {
		return false;
	}
	result.resize(static_cast<size_t>(length));
	used_default_character = FALSE;
	return WideCharToMultiByte(CP_ACP, WC_NO_BEST_FIT_CHARS, text.data(), static_cast<int>(text.size()),
		result.data(), length, nullptr, &used_default_character) == length && !used_default_character;
}

std::wstring JoinPath(const std::wstring& directory, const wchar_t* leaf) {
	if (directory.empty()) {
		return {};
	}
	std::wstring result = directory;
	if (!IsSeparator(result.back())) {
		result.push_back(L'\\');
	}
	result += leaf;
	return result;
}

std::wstring ParentDirectory(const std::wstring& path) {
	const size_t separator = path.find_last_of(L"\\/");
	if (separator == std::wstring::npos) {
		return {};
	}
	if (separator == 2 && path.size() >= 3 && path[1] == L':') {
		return path.substr(0, 3);
	}
	return path.substr(0, separator);
}

bool FileExists(const std::wstring& path) {
	const DWORD attributes = GetFileAttributesW(path.c_str());
	return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0;
}

std::wstring ResolveAgainstDirectory(const std::wstring& path, const wchar_t* requested_directory) {
	if (path.empty()) {
		return {};
	}
	const bool absolute = (path.size() >= 3 && path[1] == L':' && IsSeparator(path[2]))
		|| (!path.empty() && IsSeparator(path[0]));
	if (absolute || requested_directory == nullptr || requested_directory[0] == L'\0') {
		return FullPath(path);
	}
	return FullPath(JoinPath(requested_directory, path.c_str()));
}

std::wstring ResolveAgainstDirectory(const std::wstring& path, const std::wstring& requested_directory) {
	return ResolveAgainstDirectory(path, requested_directory.empty() ? nullptr : requested_directory.c_str());
}

struct WideTokenSpan {
	size_t start = 0;
	size_t end = 0;
	std::wstring value;
};

struct AnsiTokenSpan {
	size_t start = 0;
	size_t end = 0;
	std::string value;
};

bool FirstCommandToken(const wchar_t* command_line, WideTokenSpan& token) {
	if (command_line == nullptr) {
		return false;
	}
	std::wstring_view view(command_line);
	size_t start = view.find_first_not_of(L" \t");
	if (start == std::wstring_view::npos) {
		return false;
	}
	size_t end = start;
	size_t value_start = start;
	size_t value_end = start;
	if (view[start] == L'"') {
		value_start = start + 1;
		value_end = view.find(L'"', value_start);
		if (value_end == std::wstring_view::npos) {
			return false;
		}
		end = value_end + 1;
	} else {
		value_end = view.find_first_of(L" \t", start);
		if (value_end == std::wstring_view::npos) {
			value_end = view.size();
		}
		end = value_end;
	}
	token.start = start;
	token.end = end;
	token.value.assign(view.substr(value_start, value_end - value_start));
	return !token.value.empty();
}

bool FirstCommandToken(const char* command_line, AnsiTokenSpan& token) {
	if (command_line == nullptr) {
		return false;
	}
	std::string_view view(command_line);
	size_t start = view.find_first_not_of(" \t");
	if (start == std::string_view::npos) {
		return false;
	}
	size_t end = start;
	size_t value_start = start;
	size_t value_end = start;
	if (view[start] == '"') {
		value_start = start + 1;
		value_end = view.find('"', value_start);
		if (value_end == std::string_view::npos) {
			return false;
		}
		end = value_end + 1;
	} else {
		value_end = view.find_first_of(" \t", start);
		if (value_end == std::string_view::npos) {
			value_end = view.size();
		}
		end = value_end;
	}
	token.start = start;
	token.end = end;
	token.value.assign(view.substr(value_start, value_end - value_start));
	return !token.value.empty();
}

std::wstring ReplaceCommandExecutable(const wchar_t* command_line, const std::wstring& player_path) {
	WideTokenSpan token;
	if (!FirstCommandToken(command_line, token) || !IsRpgRtPath(token.value.c_str())) {
		return command_line == nullptr ? std::wstring() : std::wstring(command_line);
	}
	std::wstring result(command_line, token.start);
	result += L"\"" + player_path + L"\"";
	result += command_line + token.end;
	return result;
}

std::string ReplaceCommandExecutable(const char* command_line, const std::string& player_path) {
	AnsiTokenSpan token;
	if (!FirstCommandToken(command_line, token) || !IsRpgRtPath(token.value.c_str())) {
		return command_line == nullptr ? std::string() : std::string(command_line);
	}
	std::string result(command_line, token.start);
	result += "\"" + player_path + "\"";
	result += command_line + token.end;
	return result;
}

bool ResolveManagedPaths() {
	std::vector<wchar_t> buffer(512);
	for (;;) {
		DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
		if (length == 0) {
			return false;
		}
		if (length < buffer.size() - 1) {
			buffer.resize(length);
			break;
		}
		if (buffer.size() >= 32768) {
			return false;
		}
		buffer.resize(buffer.size() * 2);
	}
	const std::wstring executable(buffer.begin(), buffer.end());
	const std::wstring directory = ParentDirectory(executable);
	if (directory.empty()) {
		return false;
	}
	managed_runtime_path = FullPath(JoinPath(directory, L"RPG_RT.exe"));
	managed_player_path = FullPath(JoinPath(directory, L"Player.exe"));
	return !managed_runtime_path.empty() && !managed_player_path.empty();
}

void CopyPlayerTemplate(const std::wstring& destination_runtime_path) {
	if (original_copy_file_w == nullptr || managed_player_path.empty() || !FileExists(managed_player_path)) {
		return;
	}
	const std::wstring project_directory = ParentDirectory(destination_runtime_path);
	if (project_directory.empty()) {
		return;
	}
	const std::wstring project_player_path = JoinPath(project_directory, L"Player.exe");
	// CopyFileW's fail-if-exists flag protects a user's project-local Player.exe,
	// including a file created between this check and the copy operation.
	(void)original_copy_file_w(managed_player_path.c_str(), project_player_path.c_str(), TRUE);
}

BOOL WINAPI HookCopyFileW(LPCWSTR source, LPCWSTR destination, BOOL fail_if_exists) {
	if (original_copy_file_w == nullptr) {
		SetLastError(ERROR_PROC_NOT_FOUND);
		return FALSE;
	}
	if (intercepted_api_depth != 0 || !IsRpgRtPath(source) || !IsRpgRtPath(destination)) {
		return original_copy_file_w(source, destination, fail_if_exists);
	}

	InterceptionScope scope;
	const BOOL copied = original_copy_file_w(source, destination, fail_if_exists);
	const DWORD original_error = GetLastError();
	if (copied) {
		try {
			const std::wstring source_path = FullPath(source);
			const std::wstring destination_path = FullPath(destination);
			if (SamePath(source_path, managed_runtime_path) && !SamePath(destination_path, managed_runtime_path)) {
				CopyPlayerTemplate(destination_path);
			}
		} catch (...) {
			// A Player template copy is supplemental; keep project creation's result.
		}
	}
	SetLastError(original_error);
	return copied;
}

BOOL WINAPI HookCopyFileA(LPCSTR source, LPCSTR destination, BOOL fail_if_exists) {
	if (original_copy_file_a == nullptr) {
		SetLastError(ERROR_PROC_NOT_FOUND);
		return FALSE;
	}
	if (intercepted_api_depth != 0 || !IsRpgRtPath(source) || !IsRpgRtPath(destination)) {
		return original_copy_file_a(source, destination, fail_if_exists);
	}

	InterceptionScope scope;
	const BOOL copied = original_copy_file_a(source, destination, fail_if_exists);
	const DWORD original_error = GetLastError();
	if (copied) {
		try {
			std::wstring source_wide;
			std::wstring destination_wide;
			if (AnsiToWide(source, source_wide) && AnsiToWide(destination, destination_wide)) {
				const std::wstring source_path = FullPath(source_wide);
				const std::wstring destination_path = FullPath(destination_wide);
				if (SamePath(source_path, managed_runtime_path) && !SamePath(destination_path, managed_runtime_path)) {
					CopyPlayerTemplate(destination_path);
				}
			}
		} catch (...) {
			// A Player template copy is supplemental; keep project creation's result.
		}
	}
	SetLastError(original_error);
	return copied;
}

bool PreparePlayerLaunch(
	LPCWSTR application_name,
	LPCWSTR command_line,
	LPCWSTR requested_current_directory,
	std::wstring& player_path,
	std::wstring& project_directory,
	std::wstring& rewritten_command_line,
	bool& rewrite_command_line) {
	std::wstring executable;
	WideTokenSpan command_token;
	if (application_name != nullptr && application_name[0] != L'\0') {
		if (!IsRpgRtPath(application_name)) {
			return false;
		}
		executable = application_name;
	} else {
		if (!FirstCommandToken(command_line, command_token) || !IsRpgRtPath(command_token.value.c_str())) {
			return false;
		}
		executable = command_token.value;
	}

	const std::wstring resolved_runtime = ResolveAgainstDirectory(executable, requested_current_directory);
	if (resolved_runtime.empty() || SamePath(resolved_runtime, managed_runtime_path) || !FileExists(resolved_runtime)) {
		return false;
	}
	project_directory = ParentDirectory(resolved_runtime);
	if (project_directory.empty()) {
		return false;
	}
	player_path = FullPath(JoinPath(project_directory, L"Player.exe"));
	if (player_path.empty() || !FileExists(player_path)) {
		return false;
	}

	rewrite_command_line = false;
	if (command_line != nullptr) {
		if (FirstCommandToken(command_line, command_token) && IsRpgRtPath(command_token.value.c_str())) {
			rewritten_command_line = ReplaceCommandExecutable(command_line, player_path);
			rewrite_command_line = true;
		} else {
			rewritten_command_line = command_line;
		}
	}
	return true;
}

bool PreparePlayerLaunch(
	LPCSTR application_name,
	LPCSTR command_line,
	LPCSTR requested_current_directory,
	std::string& player_path,
	std::string& project_directory,
	std::string& rewritten_command_line,
	bool& rewrite_command_line) {
	std::wstring application_wide;
	std::wstring command_wide;
	std::wstring directory_wide;
	if (application_name != nullptr && application_name[0] != '\0' && !AnsiToWide(application_name, application_wide)) {
		return false;
	}
	if (command_line != nullptr && !AnsiToWide(command_line, command_wide)) {
		return false;
	}
	if (requested_current_directory != nullptr && requested_current_directory[0] != '\0' &&
		!AnsiToWide(requested_current_directory, directory_wide)) {
		return false;
	}
	std::wstring player_wide;
	std::wstring project_wide;
	std::wstring rewritten_wide;
	bool rewrite_wide = false;
	if (!PreparePlayerLaunch(
		application_name == nullptr ? nullptr : application_wide.c_str(),
		command_line == nullptr ? nullptr : command_wide.c_str(),
		requested_current_directory == nullptr ? nullptr : directory_wide.c_str(),
		player_wide, project_wide, rewritten_wide, rewrite_wide)) {
		return false;
	}
	if (!WideToAnsi(player_wide, player_path) || !WideToAnsi(project_wide, project_directory)) {
		return false;
	}
	if (command_line != nullptr && rewrite_wide && !WideToAnsi(rewritten_wide, rewritten_command_line)) {
		return false;
	}
	rewrite_command_line = command_line != nullptr && rewrite_wide;
	return true;
}

BOOL WINAPI HookCreateProcessW(
	LPCWSTR application_name,
	LPWSTR command_line,
	LPSECURITY_ATTRIBUTES process_attributes,
	LPSECURITY_ATTRIBUTES thread_attributes,
	BOOL inherit_handles,
	DWORD creation_flags,
	LPVOID environment,
	LPCWSTR current_directory,
	LPSTARTUPINFOW startup_info,
	LPPROCESS_INFORMATION process_information) {
	if (original_create_process_w == nullptr) {
		SetLastError(ERROR_PROC_NOT_FOUND);
		return FALSE;
	}
	if (intercepted_api_depth != 0) {
		return original_create_process_w(application_name, command_line, process_attributes, thread_attributes,
			inherit_handles, creation_flags, environment, current_directory, startup_info, process_information);
	}

	std::wstring player_path;
	std::wstring project_directory;
	std::wstring rewritten_command_line;
	bool rewrite_command_line = false;
	LPCWSTR selected_application = application_name;
	LPWSTR selected_command_line = command_line;
	LPCWSTR selected_current_directory = current_directory;
	try {
		if (PreparePlayerLaunch(application_name, command_line, current_directory, player_path,
			project_directory, rewritten_command_line, rewrite_command_line)) {
			selected_application = player_path.c_str();
			selected_current_directory = project_directory.c_str();
			if (command_line != nullptr && rewrite_command_line) {
				selected_command_line = rewritten_command_line.data();
			}
		}
	} catch (...) {
		selected_application = application_name;
		selected_command_line = command_line;
		selected_current_directory = current_directory;
	}

	InterceptionScope scope;
	return original_create_process_w(selected_application, selected_command_line, process_attributes,
		thread_attributes, inherit_handles, creation_flags, environment, selected_current_directory,
		startup_info, process_information);
}

BOOL WINAPI HookCreateProcessA(
	LPCSTR application_name,
	LPSTR command_line,
	LPSECURITY_ATTRIBUTES process_attributes,
	LPSECURITY_ATTRIBUTES thread_attributes,
	BOOL inherit_handles,
	DWORD creation_flags,
	LPVOID environment,
	LPCSTR current_directory,
	LPSTARTUPINFOA startup_info,
	LPPROCESS_INFORMATION process_information) {
	if (original_create_process_a == nullptr) {
		SetLastError(ERROR_PROC_NOT_FOUND);
		return FALSE;
	}
	if (intercepted_api_depth != 0) {
		return original_create_process_a(application_name, command_line, process_attributes, thread_attributes,
			inherit_handles, creation_flags, environment, current_directory, startup_info, process_information);
	}

	std::string player_path;
	std::string project_directory;
	std::string rewritten_command_line;
	bool rewrite_command_line = false;
	LPCSTR selected_application = application_name;
	LPSTR selected_command_line = command_line;
	LPCSTR selected_current_directory = current_directory;
	try {
		if (PreparePlayerLaunch(application_name, command_line, current_directory, player_path,
			project_directory, rewritten_command_line, rewrite_command_line)) {
			selected_application = player_path.c_str();
			selected_current_directory = project_directory.c_str();
			if (command_line != nullptr && rewrite_command_line) {
				selected_command_line = rewritten_command_line.data();
			}
		}
	} catch (...) {
		selected_application = application_name;
		selected_command_line = command_line;
		selected_current_directory = current_directory;
	}

	InterceptionScope scope;
	return original_create_process_a(selected_application, selected_command_line, process_attributes,
		thread_attributes, inherit_handles, creation_flags, environment, selected_current_directory,
		startup_info, process_information);
}

bool InstallProjectRuntimeHooks() {
	if (!ResolveManagedPaths()) {
		return false;
	}
	const MH_STATUS initialize_status = MH_Initialize();
	if (initialize_status != MH_OK && initialize_status != MH_ERROR_ALREADY_INITIALIZED) {
		return false;
	}
	HMODULE kernel_module = GetModuleHandleW(L"kernel32.dll");
	if (kernel_module == nullptr) {
		return false;
	}
	const std::array<void*, 4> targets = {
		reinterpret_cast<void*>(GetProcAddress(kernel_module, "CopyFileA")),
		reinterpret_cast<void*>(GetProcAddress(kernel_module, "CopyFileW")),
		reinterpret_cast<void*>(GetProcAddress(kernel_module, "CreateProcessA")),
		reinterpret_cast<void*>(GetProcAddress(kernel_module, "CreateProcessW"))
	};
	const std::array<void*, 4> detours = {
		reinterpret_cast<void*>(&HookCopyFileA),
		reinterpret_cast<void*>(&HookCopyFileW),
		reinterpret_cast<void*>(&HookCreateProcessA),
		reinterpret_cast<void*>(&HookCreateProcessW)
	};
	const std::array<void**, 4> originals = {
		reinterpret_cast<void**>(&original_copy_file_a),
		reinterpret_cast<void**>(&original_copy_file_w),
		reinterpret_cast<void**>(&original_create_process_a),
		reinterpret_cast<void**>(&original_create_process_w)
	};
	std::vector<void*> created;
	for (size_t index = 0; index < targets.size(); ++index) {
		if (targets[index] == nullptr || MH_CreateHook(targets[index], detours[index], originals[index]) != MH_OK) {
			for (void* target : created) { (void)MH_RemoveHook(target); }
			return false;
		}
		created.push_back(targets[index]);
	}
	for (void* target : created) {
		if (MH_EnableHook(target) != MH_OK) {
			for (void* hook : created) {
				(void)MH_DisableHook(hook);
				(void)MH_RemoveHook(hook);
			}
			return false;
		}
	}
	return true;
}

} // namespace

bool InitializeProjectRuntimeSupport() {
	try {
		std::call_once(project_runtime_initialization_once, []() {
			project_runtime_initialization_succeeded = InstallProjectRuntimeHooks();
		});
	} catch (...) {
		return false;
	}
	return project_runtime_initialization_succeeded;
}
