#include <windows.h>

#include <CommCtrl.h>
#include <TlHelp32.h>

#include <algorithm>
#include <iterator>
#include <limits>
#include <mutex>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace {

constexpr UINT_PTR kTOverEditSubclassId = 0x41524142;
constexpr size_t kMaxClipboardCharacters = 1024 * 1024;

std::vector<HHOOK> thread_hooks;
std::mutex creation_mutex;
std::unordered_set<HWND> pending_creations;
std::mutex font_mutex;
std::unordered_map<HWND, std::vector<HFONT>> owned_fonts;

bool IsTOverEdit(HWND window) {
	if (window == nullptr || !IsWindow(window)) {
		return false;
	}
	wchar_t class_name[64]{};
	const int length = GetClassNameW(window, class_name,
		static_cast<int>(std::size(class_name)));
	return length == 9 && lstrcmpW(class_name, L"TOverEdit") == 0;
}

bool IsOwnedFont(HWND window, HFONT font) {
	std::lock_guard<std::mutex> guard(font_mutex);
	const auto found = owned_fonts.find(window);
	return found != owned_fonts.end()
		&& std::find(found->second.begin(), found->second.end(), font) != found->second.end();
}

HFONT CreateArabicFont(HWND window, HFONT current_font) {
	if (current_font == nullptr || IsOwnedFont(window, current_font)) {
		return nullptr;
	}
	LOGFONTW description{};
	if (GetObjectW(current_font, sizeof(description), &description)
		!= static_cast<int>(sizeof(description))) {
		return nullptr;
	}
	description.lfCharSet = ARABIC_CHARSET;
	HDC device_context = GetDC(window);
	const int dpi_y = device_context == nullptr ? 96
		: GetDeviceCaps(device_context, LOGPIXELSY);
	if (device_context != nullptr) {
		ReleaseDC(window, device_context);
	}
	const int increment = std::max(1, MulDiv(dpi_y > 0 ? dpi_y : 96, 3, 96));
	if (description.lfHeight < 0
		&& description.lfHeight > (std::numeric_limits<LONG>::min)() + increment) {
		description.lfHeight -= increment;
	} else if (description.lfHeight > 0
		&& description.lfHeight < (std::numeric_limits<LONG>::max)() - increment) {
		description.lfHeight += increment;
	} else if (description.lfHeight == 0) {
		HDC measurement_context = CreateCompatibleDC(nullptr);
		if (measurement_context != nullptr) {
			const HGDIOBJ previous_font = SelectObject(measurement_context, current_font);
			TEXTMETRICW metrics{};
			if (previous_font != nullptr && previous_font != HGDI_ERROR
				&& GetTextMetricsW(measurement_context, &metrics)) {
				const int character_height = std::max(1,
					static_cast<int>(metrics.tmHeight) - static_cast<int>(metrics.tmInternalLeading));
				description.lfHeight = -(character_height + increment);
			}
			if (previous_font != nullptr && previous_font != HGDI_ERROR) {
				SelectObject(measurement_context, previous_font);
			}
			DeleteDC(measurement_context);
		}
	}
	const HFONT replacement = CreateFontIndirectW(&description);
	if (replacement != nullptr) {
		std::lock_guard<std::mutex> guard(font_mutex);
		owned_fonts[window].push_back(replacement);
	}
	return replacement;
}

void EnsureArabicFont(HWND window) {
	const HFONT current_font = reinterpret_cast<HFONT>(SendMessageW(window, WM_GETFONT, 0, 0));
	if (current_font == nullptr || IsOwnedFont(window, current_font)) {
		return;
	}
	const HFONT replacement = CreateArabicFont(window, current_font);
	if (replacement != nullptr) {
		SendMessageW(window, WM_SETFONT, reinterpret_cast<WPARAM>(replacement), TRUE);
	}
}

void ReleaseOwnedFonts(HWND window) {
	std::vector<HFONT> fonts;
	{
		std::lock_guard<std::mutex> guard(font_mutex);
		const auto found = owned_fonts.find(window);
		if (found != owned_fonts.end()) {
			fonts.swap(found->second);
			owned_fonts.erase(found);
		}
	}
	for (HFONT font : fonts) {
		DeleteObject(font);
	}
}

bool ReadClipboardUnicode(HWND window, std::wstring& text) {
	if (!OpenClipboard(window)) {
		return false;
	}
	HANDLE clipboard_data = GetClipboardData(CF_UNICODETEXT);
	if (clipboard_data == nullptr) {
		CloseClipboard();
		return false;
	}
	const SIZE_T byte_count = GlobalSize(clipboard_data);
	const auto* data = static_cast<const wchar_t*>(GlobalLock(clipboard_data));
	if (data == nullptr || byte_count < sizeof(wchar_t)) {
		if (data != nullptr) {
			GlobalUnlock(clipboard_data);
		}
		CloseClipboard();
		return false;
	}
	const size_t capacity = std::min<size_t>(byte_count / sizeof(wchar_t),
		kMaxClipboardCharacters);
	for (size_t index = 0; index < capacity && data[index] != L'\0'; ++index) {
		text.push_back(data[index]);
	}
	GlobalUnlock(clipboard_data);
	CloseClipboard();
	return true;
}

LRESULT CALLBACK TOverEditSubclassProc(HWND window, UINT message, WPARAM wparam,
	LPARAM lparam, UINT_PTR, DWORD_PTR) {
	if ((message == WM_PASTE || (message == WM_CHAR && wparam == 0x16))
		&& IsWindowUnicode(window)) {
		std::wstring clipboard_text;
		if (ReadClipboardUnicode(window, clipboard_text)) {
			DefSubclassProc(window, EM_REPLACESEL, TRUE,
				reinterpret_cast<LPARAM>(clipboard_text.c_str()));
			return 0;
		}
	}
	if (message == WM_PAINT || message == WM_PRINT || message == WM_PRINTCLIENT) {
		EnsureArabicFont(window);
	}
	if (message == WM_NCDESTROY) {
		const LRESULT result = DefSubclassProc(window, message, wparam, lparam);
		ReleaseOwnedFonts(window);
		RemoveWindowSubclass(window, TOverEditSubclassProc, kTOverEditSubclassId);
		return result;
	}
	return DefSubclassProc(window, message, wparam, lparam);
}

bool PatchTOverEdit(HWND window) {
	DWORD_PTR reference_data = 0;
	const bool already_attached = GetWindowSubclass(window, TOverEditSubclassProc,
		kTOverEditSubclassId, &reference_data) != FALSE;
	if (!already_attached && !SetWindowSubclass(window, TOverEditSubclassProc,
		kTOverEditSubclassId, 0)) {
		return false;
	}
	EnsureArabicFont(window);
	return true;
}

bool AttachTOverEdit(HWND window) {
	return IsTOverEdit(window) && PatchTOverEdit(window);
}

void ForgetCreation(HWND window) {
	std::lock_guard<std::mutex> guard(creation_mutex);
	pending_creations.erase(window);
}

bool IsPendingCreation(HWND window) {
	std::lock_guard<std::mutex> guard(creation_mutex);
	return pending_creations.find(window) != pending_creations.end();
}

BOOL CALLBACK AttachExistingChild(HWND window, LPARAM) {
	AttachTOverEdit(window);
	return TRUE;
}

BOOL CALLBACK ScanExistingTopLevel(HWND window, LPARAM) {
	AttachTOverEdit(window);
	EnumChildWindows(window, AttachExistingChild, 0);
	return TRUE;
}

void ScanExistingTOverEditsOnCurrentThread() {
	EnumThreadWindows(GetCurrentThreadId(), ScanExistingTopLevel, 0);
}

LRESULT CALLBACK CbtProc(int code, WPARAM wparam, LPARAM lparam) {
	if (code == HCBT_CREATEWND) {
		const HWND window = reinterpret_cast<HWND>(wparam);
		if (window != nullptr) {
			std::lock_guard<std::mutex> guard(creation_mutex);
			pending_creations.insert(window);
		}
	}
	return CallNextHookEx(nullptr, code, wparam, lparam);
}

bool IsShowWindowPosition(LPARAM lparam) {
	return lparam != 0
		&& (reinterpret_cast<const WINDOWPOS*>(lparam)->flags & SWP_SHOWWINDOW) != 0;
}

LRESULT CALLBACK CallWndProc(int code, WPARAM wparam, LPARAM lparam) {
	if (code < 0 || lparam == 0) {
		return CallNextHookEx(nullptr, code, wparam, lparam);
	}
	const auto* message = reinterpret_cast<const CWPSTRUCT*>(lparam);
	const HWND window = message->hwnd;
	if (message->message == WM_NCDESTROY) {
		if (IsPendingCreation(window)) {
			ForgetCreation(window);
		}
	} else {
		const bool visibility_transition =
			(message->message == WM_SHOWWINDOW && message->wParam != FALSE)
			|| (message->message == WM_WINDOWPOSCHANGING
				&& IsShowWindowPosition(message->lParam));
		if (!visibility_transition && message->message != WM_PAINT) {
			return CallNextHookEx(nullptr, code, wparam, lparam);
		}
		if (IsPendingCreation(window)) {
			if (!IsTOverEdit(window)) {
				ForgetCreation(window);
			} else if (PatchTOverEdit(window)) {
				ForgetCreation(window);
			}
		}
	}
	return CallNextHookEx(nullptr, code, wparam, lparam);
}

void InstallHooksForProcessThreads() {
	HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
	if (snapshot == INVALID_HANDLE_VALUE) {
		return;
	}
	THREADENTRY32 entry{};
	entry.dwSize = sizeof(entry);
	const DWORD process_id = GetCurrentProcessId();
	constexpr int hook_types[] = {WH_CBT, WH_CALLWNDPROC};
	if (Thread32First(snapshot, &entry)) {
		do {
			if (entry.th32OwnerProcessID != process_id) {
				continue;
			}
			const DWORD thread_id = entry.th32ThreadID;
			for (const int hook_type : hook_types) {
				HOOKPROC procedure = hook_type == WH_CBT ? CbtProc : CallWndProc;
				const HHOOK hook = SetWindowsHookExW(hook_type, procedure, nullptr, thread_id);
				if (hook != nullptr) {
					thread_hooks.push_back(hook);
				}
			}
		} while (Thread32Next(snapshot, &entry));
	}
	CloseHandle(snapshot);
}

} // namespace

bool InitializeTOverEditSupport() {
	InstallHooksForProcessThreads();
	ScanExistingTOverEditsOnCurrentThread();
	return !thread_hooks.empty();
}
