#include <windows.h>

#include <MinHook.h>

#include <array>
#include <atomic>
#include <mutex>
#include <string>
#include <string_view>
#include <vector>

static_assert(sizeof(void*) == 4, "The Maniacs editor hook is x86 only.");

namespace {

using CopyFileAFunction = BOOL(WINAPI*)(LPCSTR, LPCSTR, BOOL);
using CopyFileWFunction = BOOL(WINAPI*)(LPCWSTR, LPCWSTR, BOOL);
using CreateFileAFunction = HANDLE(WINAPI*)(LPCSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
using CreateFileWFunction = HANDLE(WINAPI*)(LPCWSTR, DWORD, DWORD, LPSECURITY_ATTRIBUTES, DWORD, DWORD, HANDLE);
using CloseHandleFunction = BOOL(WINAPI*)(HANDLE);
using CreateProcessAFunction = BOOL(WINAPI*)(
	LPCSTR, LPSTR, LPSECURITY_ATTRIBUTES, LPSECURITY_ATTRIBUTES, BOOL, DWORD, LPVOID,
	LPCSTR, LPSTARTUPINFOA, LPPROCESS_INFORMATION);
using CreateProcessWFunction = BOOL(WINAPI*)(
	LPCWSTR, LPWSTR, LPSECURITY_ATTRIBUTES, LPSECURITY_ATTRIBUTES, BOOL, DWORD, LPVOID,
	LPCWSTR, LPSTARTUPINFOW, LPPROCESS_INFORMATION);

CopyFileAFunction original_copy_file_a = nullptr;
CopyFileWFunction original_copy_file_w = nullptr;
CreateFileAFunction original_create_file_a = nullptr;
CreateFileWFunction original_create_file_w = nullptr;
CloseHandleFunction original_close_handle = nullptr;
CreateProcessAFunction original_create_process_a = nullptr;
CreateProcessWFunction original_create_process_w = nullptr;

std::once_flag project_runtime_initialization_once;
bool project_runtime_initialization_succeeded = false;
thread_local unsigned int intercepted_api_depth = 0;

std::wstring managed_runtime_path;
std::wstring managed_player_path;
std::wstring managed_database_template_path;
std::mutex new_project_mutex;
std::wstring candidate_project_directory;
std::wstring ready_project_directory;
std::wstring tracked_map_directory;
std::atomic<HANDLE> tracked_map_read_handle{nullptr};
bool candidate_updater_started = false;

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

template <typename Character>
bool IsDatabaseTemplateSourcePath(const Character* path) {
	return path != nullptr && EqualsAsciiInsensitive(LeafName(std::basic_string_view<Character>(path)), "RPG_RT.ldb.dat");
}

template <typename Character>
bool IsProjectDatabasePath(const Character* path) {
	return path != nullptr && EqualsAsciiInsensitive(LeafName(std::basic_string_view<Character>(path)), "RPG_RT.ldb");
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

bool IsDatabaseTemplatePath(const std::wstring& path) {
	return !managed_database_template_path.empty() && SamePath(path, managed_database_template_path);
}

void RecordNewProjectRuntimeCopy(const std::wstring& runtime_path) {
	const std::wstring directory = ParentDirectory(runtime_path);
	if (directory.empty()) { return; }
	std::lock_guard<std::mutex> lock(new_project_mutex);
	candidate_project_directory = directory;
	ready_project_directory.clear();
	tracked_map_directory.clear();
	tracked_map_read_handle.store(nullptr, std::memory_order_release);
	candidate_updater_started = false;
}

void RecordNewProjectDatabaseCopy(const std::wstring& source, const std::wstring& destination) {
	if (!IsDatabaseTemplatePath(source) || !EqualsAsciiInsensitive(LeafName(std::wstring_view(destination)), "RPG_RT.ldb")) {
		return;
	}
	const std::wstring directory = ParentDirectory(destination);
	if (directory.empty()) { return; }
	std::lock_guard<std::mutex> lock(new_project_mutex);
	if (!candidate_project_directory.empty() && SamePath(directory, candidate_project_directory)) {
		ready_project_directory = directory;
	}
}

bool IsCandidateMapRead(const std::wstring& path, DWORD desired_access, DWORD creation_disposition) {
	if ((desired_access & GENERIC_READ) == 0 || creation_disposition != OPEN_EXISTING ||
		!EqualsAsciiInsensitive(LeafName(std::wstring_view(path)), "Map0001.lmu") ||
		tracked_map_read_handle.load(std::memory_order_acquire) != nullptr) {
		return false;
	}
	const std::wstring directory = ParentDirectory(path);
	if (directory.empty()) { return false; }
	std::lock_guard<std::mutex> lock(new_project_mutex);
	return !candidate_updater_started && !ready_project_directory.empty() && SamePath(directory, ready_project_directory);
}

std::wstring QuoteWindowsArgument(std::wstring_view argument) {
	std::wstring quoted(1, L'"');
	size_t backslashes = 0;
	for (wchar_t character : argument) {
		if (character == L'\\') {
			++backslashes;
			continue;
		}
		if (character == L'"') {
			quoted.append(backslashes * 2 + 1, L'\\');
			quoted.push_back(L'"');
			backslashes = 0;
			continue;
		}
		quoted.append(backslashes, L'\\');
		backslashes = 0;
		quoted.push_back(character);
	}
	quoted.append(backslashes * 2, L'\\');
	quoted.push_back(L'"');
	return quoted;
}

bool WriteRuntimeLogLine(const std::wstring& path, const std::wstring& line) {
	const HANDLE file = CreateFileW(path.c_str(), FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE,
		nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
	if (file == INVALID_HANDLE_VALUE) { return false; }
	const int byte_count = WideCharToMultiByte(CP_UTF8, 0, line.data(), static_cast<int>(line.size()), nullptr, 0, nullptr, nullptr);
	if (byte_count <= 0) { (void)original_close_handle(file); return false; }
	std::string utf8(static_cast<size_t>(byte_count), '\0');
	if (WideCharToMultiByte(CP_UTF8, 0, line.data(), static_cast<int>(line.size()), utf8.data(), byte_count, nullptr, nullptr) != byte_count) {
		(void)original_close_handle(file);
		return false;
	}
	DWORD written = 0;
	const BOOL succeeded = WriteFile(file, utf8.data(), static_cast<DWORD>(utf8.size()), &written, nullptr);
	(void)original_close_handle(file);
	return succeeded && written == utf8.size();
}

std::wstring GetPowerShellExecutable() {
	std::vector<wchar_t> windows_directory(512);
	for (;;) {
		const UINT length = GetWindowsDirectoryW(windows_directory.data(), static_cast<UINT>(windows_directory.size()));
		if (length == 0) { break; }
		if (length < windows_directory.size()) {
			windows_directory.resize(length);
			const std::wstring candidate = JoinPath(JoinPath(std::wstring(windows_directory.data(), length), L"System32\\WindowsPowerShell\\v1.0"), L"powershell.exe");
			if (FileExists(candidate)) { return candidate; }
			break;
		}
		windows_directory.resize(static_cast<size_t>(length) + 1);
	}
	std::vector<wchar_t> search_result(32768);
	const DWORD length = SearchPathW(nullptr, L"powershell.exe", nullptr, static_cast<DWORD>(search_result.size()), search_result.data(), nullptr);
	if (length == 0 || length >= search_result.size()) { return {}; }
	search_result.resize(length);
	return std::wstring(search_result.begin(), search_result.end());
}

void LaunchArabicProjectUpdater(const std::wstring& project_directory) {
	if (original_create_process_w == nullptr || original_close_handle == nullptr || project_directory.empty()) { return; }
	InterceptionScope scope;
	const std::wstring support_directory = ParentDirectory(managed_player_path);
	const std::wstring script_path = JoinPath(JoinPath(support_directory, L"ArabicSupport\\tools"), L"update-arabic.ps1");
	const std::wstring lcftrans_path = JoinPath(JoinPath(support_directory, L"ArabicSupport\\tools"), L"lcftrans.exe");
	const std::wstring language_directory = JoinPath(JoinPath(project_directory, L"Language"), L"ar");
	const std::wstring runtime_log = JoinPath(language_directory, L"Arabic-setup.log");
	if (!FileExists(script_path) || !FileExists(lcftrans_path)) {
		(void)WriteRuntimeLogLine(runtime_log, L"Arabic PO initialization could not start: bundled updater or LcfTrans is missing.\r\n");
		return;
	}
	if (!CreateDirectoryW(JoinPath(project_directory, L"Language").c_str(), nullptr) && GetLastError() != ERROR_ALREADY_EXISTS) {
		(void)WriteRuntimeLogLine(runtime_log, L"Arabic PO initialization could not start: cannot create Language directory.\r\n");
		return;
	}
	if (!CreateDirectoryW(language_directory.c_str(), nullptr) && GetLastError() != ERROR_ALREADY_EXISTS) {
		(void)WriteRuntimeLogLine(runtime_log, L"Arabic PO initialization could not start: cannot create Language/ar directory.\r\n");
		return;
	}
	(void)WriteRuntimeLogLine(runtime_log, L"Arabic PO initialization started.\r\n");

	const std::wstring powershell = GetPowerShellExecutable();
	if (powershell.empty()) {
		(void)WriteRuntimeLogLine(runtime_log, L"Arabic PO initialization failed: Windows PowerShell was not found.\r\n");
		return;
	}
	std::wstring command_line = QuoteWindowsArgument(powershell)
		+ L" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File "
		+ QuoteWindowsArgument(script_path)
		+ L" -GamePath " + QuoteWindowsArgument(project_directory)
		+ L" -LcfTransPath " + QuoteWindowsArgument(lcftrans_path)
		+ L" -InitializeNewProject"
		+ L" -RuntimeLogPath " + QuoteWindowsArgument(runtime_log);
	std::vector<wchar_t> mutable_command_line(command_line.begin(), command_line.end());
	mutable_command_line.push_back(L'\0');
	STARTUPINFOW startup_info{};
	startup_info.cb = sizeof(startup_info);
	startup_info.dwFlags = STARTF_USESHOWWINDOW;
	startup_info.wShowWindow = SW_HIDE;
	PROCESS_INFORMATION process_info{};
	if (!original_create_process_w(powershell.c_str(), mutable_command_line.data(), nullptr, nullptr, FALSE,
		CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT, nullptr, project_directory.c_str(), &startup_info, &process_info)) {
		const DWORD error = GetLastError();
		(void)WriteRuntimeLogLine(runtime_log, L"Arabic PO initialization failed to launch Windows PowerShell (Win32 error " + std::to_wstring(error) + L").\r\n");
		return;
	}
	(void)original_close_handle(process_info.hThread);
	(void)original_close_handle(process_info.hProcess);
}

void OnNewProjectMapReadClosed(HANDLE handle) {
	if (tracked_map_read_handle.load(std::memory_order_acquire) != handle) { return; }
	HANDLE expected = handle;
	if (!tracked_map_read_handle.compare_exchange_strong(expected, nullptr, std::memory_order_acq_rel)) { return; }
	std::wstring project_directory;
	{
		std::lock_guard<std::mutex> lock(new_project_mutex);
		project_directory = tracked_map_directory;
		candidate_updater_started = true;
		tracked_map_directory.clear();
		candidate_project_directory.clear();
		ready_project_directory.clear();
	}
	if (!project_directory.empty()) {
		try { LaunchArabicProjectUpdater(project_directory); }
		catch (...) { (void)WriteRuntimeLogLine(JoinPath(JoinPath(project_directory, L"Language\\ar"), L"Arabic-setup.log"), L"Arabic PO initialization failed unexpectedly.\r\n"); }
	}
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
	managed_database_template_path = FullPath(JoinPath(directory, L"RPG_RT.ldb.dat"));
	return !managed_runtime_path.empty() && !managed_player_path.empty();
}

bool CopyPlayerTemplate(const std::wstring& destination_runtime_path) {
	if (original_copy_file_w == nullptr || managed_player_path.empty() || !FileExists(managed_player_path)) {
		return false;
	}
	const std::wstring project_directory = ParentDirectory(destination_runtime_path);
	if (project_directory.empty()) {
		return false;
	}
	const std::wstring project_player_path = JoinPath(project_directory, L"Player.exe");
	// CopyFileW's fail-if-exists flag protects a user's project-local Player.exe,
	// including a file created between this check and the copy operation.
	return original_copy_file_w(managed_player_path.c_str(), project_player_path.c_str(), TRUE) != FALSE;
}

BOOL WINAPI HookCopyFileW(LPCWSTR source, LPCWSTR destination, BOOL fail_if_exists) {
	if (original_copy_file_w == nullptr) {
		SetLastError(ERROR_PROC_NOT_FOUND);
		return FALSE;
	}
	const bool runtime_copy = IsRpgRtPath(source) && IsRpgRtPath(destination);
	const bool database_copy = IsDatabaseTemplateSourcePath(source) && IsProjectDatabasePath(destination);
	if (intercepted_api_depth != 0 || (!runtime_copy && !database_copy)) {
		return original_copy_file_w(source, destination, fail_if_exists);
	}

	const bool destination_existed = FileExists(destination);
	InterceptionScope scope;
	const BOOL copied = original_copy_file_w(source, destination, fail_if_exists);
	const DWORD original_error = GetLastError();
	if (copied) {
		try {
			const std::wstring source_path = FullPath(source);
			const std::wstring destination_path = FullPath(destination);
			if (runtime_copy && SamePath(source_path, managed_runtime_path) && !SamePath(destination_path, managed_runtime_path)) {
				const bool player_copied = CopyPlayerTemplate(destination_path);
				if (!destination_existed && player_copied) { RecordNewProjectRuntimeCopy(destination_path); }
			} else if (database_copy && !destination_existed && IsDatabaseTemplatePath(source_path)) {
				RecordNewProjectDatabaseCopy(source_path, destination_path);
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
	const bool runtime_copy = IsRpgRtPath(source) && IsRpgRtPath(destination);
	const bool database_copy = IsDatabaseTemplateSourcePath(source) && IsProjectDatabasePath(destination);
	if (intercepted_api_depth != 0 || (!runtime_copy && !database_copy)) {
		return original_copy_file_a(source, destination, fail_if_exists);
	}

	std::wstring destination_wide_before;
	const bool converted_destination = AnsiToWide(destination, destination_wide_before);
	const bool destination_existed = converted_destination && FileExists(FullPath(destination_wide_before));
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
				if (runtime_copy && SamePath(source_path, managed_runtime_path) && !SamePath(destination_path, managed_runtime_path)) {
					const bool player_copied = CopyPlayerTemplate(destination_path);
					if (!destination_existed && player_copied) { RecordNewProjectRuntimeCopy(destination_path); }
				} else if (database_copy && !destination_existed && IsDatabaseTemplatePath(source_path)) {
					RecordNewProjectDatabaseCopy(source_path, destination_path);
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

void TrackNewProjectMapRead(HANDLE handle, const std::wstring& path, DWORD desired_access, DWORD creation_disposition) {
	if (handle == INVALID_HANDLE_VALUE || !IsCandidateMapRead(path, desired_access, creation_disposition)) { return; }
	const std::wstring directory = ParentDirectory(path);
	if (directory.empty()) { return; }
	std::lock_guard<std::mutex> lock(new_project_mutex);
	if (!ready_project_directory.empty() && SamePath(directory, ready_project_directory) &&
		tracked_map_read_handle.load(std::memory_order_relaxed) == nullptr) {
		tracked_map_directory = directory;
		tracked_map_read_handle.store(handle, std::memory_order_release);
	}
}

HANDLE WINAPI HookCreateFileW(
	LPCWSTR file_name,
	DWORD desired_access,
	DWORD share_mode,
	LPSECURITY_ATTRIBUTES security_attributes,
	DWORD creation_disposition,
	DWORD flags_and_attributes,
	HANDLE template_file) {
	if (original_create_file_w == nullptr) {
		SetLastError(ERROR_PROC_NOT_FOUND);
		return INVALID_HANDLE_VALUE;
	}
	const bool possible_new_project_map_read = file_name != nullptr &&
		(desired_access & GENERIC_READ) != 0 && creation_disposition == OPEN_EXISTING &&
		EqualsAsciiInsensitive(LeafName(std::wstring_view(file_name)), "Map0001.lmu") &&
		tracked_map_read_handle.load(std::memory_order_acquire) == nullptr;
	if (intercepted_api_depth != 0 || !possible_new_project_map_read) {
		return original_create_file_w(file_name, desired_access, share_mode, security_attributes,
			creation_disposition, flags_and_attributes, template_file);
	}
	std::wstring path;
	try { path = FullPath(file_name); }
	catch (...) { return original_create_file_w(file_name, desired_access, share_mode, security_attributes,
		creation_disposition, flags_and_attributes, template_file); }
	if (!IsCandidateMapRead(path, desired_access, creation_disposition)) {
		return original_create_file_w(file_name, desired_access, share_mode, security_attributes,
			creation_disposition, flags_and_attributes, template_file);
	}
	InterceptionScope scope;
	HANDLE handle = original_create_file_w(file_name, desired_access, share_mode, security_attributes,
		creation_disposition, flags_and_attributes, template_file);
	const DWORD original_error = GetLastError();
	if (handle != INVALID_HANDLE_VALUE) {
		try { TrackNewProjectMapRead(handle, path, desired_access, creation_disposition); }
		catch (...) { /* Project creation must remain independent from optional PO setup. */ }
	}
	SetLastError(original_error);
	return handle;
}

HANDLE WINAPI HookCreateFileA(
	LPCSTR file_name,
	DWORD desired_access,
	DWORD share_mode,
	LPSECURITY_ATTRIBUTES security_attributes,
	DWORD creation_disposition,
	DWORD flags_and_attributes,
	HANDLE template_file) {
	if (original_create_file_a == nullptr) {
		SetLastError(ERROR_PROC_NOT_FOUND);
		return INVALID_HANDLE_VALUE;
	}
	const bool possible_new_project_map_read = file_name != nullptr &&
		(desired_access & GENERIC_READ) != 0 && creation_disposition == OPEN_EXISTING &&
		EqualsAsciiInsensitive(LeafName(std::string_view(file_name)), "Map0001.lmu") &&
		tracked_map_read_handle.load(std::memory_order_acquire) == nullptr;
	if (intercepted_api_depth != 0 || !possible_new_project_map_read) {
		return original_create_file_a(file_name, desired_access, share_mode, security_attributes,
			creation_disposition, flags_and_attributes, template_file);
	}
	std::wstring path;
	if (!AnsiToWide(file_name, path)) {
		return original_create_file_a(file_name, desired_access, share_mode, security_attributes,
			creation_disposition, flags_and_attributes, template_file);
	}
	try { path = FullPath(path); }
	catch (...) { return original_create_file_a(file_name, desired_access, share_mode, security_attributes,
		creation_disposition, flags_and_attributes, template_file); }
	if (!IsCandidateMapRead(path, desired_access, creation_disposition)) {
		return original_create_file_a(file_name, desired_access, share_mode, security_attributes,
			creation_disposition, flags_and_attributes, template_file);
	}
	InterceptionScope scope;
	HANDLE handle = original_create_file_a(file_name, desired_access, share_mode, security_attributes,
		creation_disposition, flags_and_attributes, template_file);
	const DWORD original_error = GetLastError();
	if (handle != INVALID_HANDLE_VALUE) {
		try { TrackNewProjectMapRead(handle, path, desired_access, creation_disposition); }
		catch (...) { /* Project creation must remain independent from optional PO setup. */ }
	}
	SetLastError(original_error);
	return handle;
}

BOOL WINAPI HookCloseHandle(HANDLE handle) {
	if (original_close_handle == nullptr) {
		SetLastError(ERROR_PROC_NOT_FOUND);
		return FALSE;
	}
	if (intercepted_api_depth != 0 || tracked_map_read_handle.load(std::memory_order_acquire) != handle) {
		return original_close_handle(handle);
	}
	InterceptionScope scope;
	const BOOL closed = original_close_handle(handle);
	const DWORD original_error = GetLastError();
	if (closed) {
		try { OnNewProjectMapReadClosed(handle); }
		catch (...) { /* Arabic PO initialization is supplemental to new-project creation. */ }
	}
	SetLastError(original_error);
	return closed;
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
	const std::array<void*, 7> targets = {
		reinterpret_cast<void*>(GetProcAddress(kernel_module, "CopyFileA")),
		reinterpret_cast<void*>(GetProcAddress(kernel_module, "CopyFileW")),
		reinterpret_cast<void*>(GetProcAddress(kernel_module, "CreateFileA")),
		reinterpret_cast<void*>(GetProcAddress(kernel_module, "CreateFileW")),
		reinterpret_cast<void*>(GetProcAddress(kernel_module, "CloseHandle")),
		reinterpret_cast<void*>(GetProcAddress(kernel_module, "CreateProcessA")),
		reinterpret_cast<void*>(GetProcAddress(kernel_module, "CreateProcessW"))
	};
	const std::array<void*, 7> detours = {
		reinterpret_cast<void*>(&HookCopyFileA),
		reinterpret_cast<void*>(&HookCopyFileW),
		reinterpret_cast<void*>(&HookCreateFileA),
		reinterpret_cast<void*>(&HookCreateFileW),
		reinterpret_cast<void*>(&HookCloseHandle),
		reinterpret_cast<void*>(&HookCreateProcessA),
		reinterpret_cast<void*>(&HookCreateProcessW)
	};
	const std::array<void**, 7> originals = {
		reinterpret_cast<void**>(&original_copy_file_a),
		reinterpret_cast<void**>(&original_copy_file_w),
		reinterpret_cast<void**>(&original_create_file_a),
		reinterpret_cast<void**>(&original_create_file_w),
		reinterpret_cast<void**>(&original_close_handle),
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
