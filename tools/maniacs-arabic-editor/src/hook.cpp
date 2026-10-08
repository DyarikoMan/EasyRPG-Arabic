#include <windows.h>

#include <MinHook.h>

#include <climits>
#include <mutex>
#include <string>

static_assert(sizeof(void*) == 4, "The Maniacs editor hook is x86 only.");

bool InitializeTOverEditSupport();
bool InitializeProjectRuntimeSupport();

namespace {

using ExtTextOutAFunction = BOOL(WINAPI*)(
	HDC,
	int,
	int,
	UINT,
	const RECT*,
	LPCSTR,
	UINT,
	const INT*
);

ExtTextOutAFunction original_ext_text_out_a = nullptr;
std::once_flag hook_initialization_once;
bool hook_initialization_succeeded = false;

bool IsArabicLetter(wchar_t character) {
	return (character >= 0x0621 && character <= 0x063A)
		|| (character >= 0x0641 && character <= 0x064A)
		|| (character >= 0x066E && character <= 0x066F)
		|| (character >= 0x0671 && character <= 0x06D3)
		|| character == 0x06D5
		|| character == 0x06E5
		|| character == 0x06E6
		|| (character >= 0x06EE && character <= 0x06EF)
		|| (character >= 0x06FA && character <= 0x06FC)
		|| character == 0x06FF;
}

bool IsArabicCombiningMark(wchar_t character) {
	return (character >= 0x064B && character <= 0x065F)
		|| character == 0x0670
		|| (character >= 0x06D6 && character <= 0x06ED);
}

bool DecodeLikelyArabicCp1256(const char* text, UINT length, std::wstring& decoded) {
	if (text == nullptr || length < 2 || length > 32768 || length > INT_MAX) {
		return false;
	}

	UINT high_bytes = 0;
	for (UINT index = 0; index < length; ++index) {
		if (static_cast<unsigned char>(text[index]) >= 0x80) {
			++high_bytes;
		}
	}
	if (high_bytes < 2) {
		return false;
	}

	const int input_length = static_cast<int>(length);
	const int wide_length = MultiByteToWideChar(1256, 0, text, input_length, nullptr, 0);
	if (wide_length <= 0) {
		return false;
	}

	std::wstring candidate(static_cast<size_t>(wide_length), L'\0');
	if (MultiByteToWideChar(1256, 0, text, input_length, candidate.data(), wide_length) != wide_length) {
		return false;
	}

	int arabic_letters = 0;
	int current_run = 0;
	int longest_run = 0;
	for (wchar_t character : candidate) {
		if (IsArabicLetter(character)) {
			++arabic_letters;
			++current_run;
			if (current_run > longest_run) {
				longest_run = current_run;
			}
		} else if (!IsArabicCombiningMark(character)) {
			current_run = 0;
		}
	}

	if (arabic_letters < 2 || longest_run < 2) {
		return false;
	}

	decoded.swap(candidate);
	return true;
}

BOOL WINAPI HookExtTextOutA(
	HDC device_context,
	int x,
	int y,
	UINT options,
	const RECT* rectangle,
	LPCSTR text,
	UINT length,
	const INT* spacing
) {
	if (original_ext_text_out_a == nullptr) {
		return FALSE;
	}

	if ((options & ETO_GLYPH_INDEX) == 0) {
		try {
			std::wstring decoded;
			if (DecodeLikelyArabicCp1256(text, length, decoded)) {
				// GDI should recalculate spacing after CP1256 bytes become UTF-16.
				return ExtTextOutW(
					device_context,
					x,
					y,
					options,
					rectangle,
					decoded.data(),
					static_cast<UINT>(decoded.size()),
					nullptr
				);
			}
		} catch (...) {
			// Fall through to the original ANSI call if conversion allocation fails.
		}
	}

	return original_ext_text_out_a(
		device_context,
		x,
		y,
		options,
		rectangle,
		text,
		length,
		spacing
	);
}

bool InstallExtTextOutHook() {
	const MH_STATUS initialize_status = MH_Initialize();
	if (initialize_status != MH_OK && initialize_status != MH_ERROR_ALREADY_INITIALIZED) {
		return false;
	}

	HMODULE gdi_module = GetModuleHandleW(L"gdi32.dll");
	if (gdi_module == nullptr) {
		return false;
	}

	FARPROC target = GetProcAddress(gdi_module, "ExtTextOutA");
	if (target == nullptr) {
		return false;
	}

	const MH_STATUS create_status = MH_CreateHook(
		reinterpret_cast<LPVOID>(target),
		reinterpret_cast<LPVOID>(&HookExtTextOutA),
		reinterpret_cast<LPVOID*>(&original_ext_text_out_a)
	);
	if (create_status != MH_OK) {
		return false;
	}

	const MH_STATUS enable_status = MH_EnableHook(reinterpret_cast<LPVOID>(target));
	if (enable_status != MH_OK) {
		MH_RemoveHook(reinterpret_cast<LPVOID>(target));
		return false;
	}
	return true;
}

} // namespace

extern "C" BOOL WINAPI InitializeArabicTextHook() {
	try {
		std::call_once(hook_initialization_once, []() {
			hook_initialization_succeeded = InstallExtTextOutHook();
			if (hook_initialization_succeeded) {
				(void)InitializeTOverEditSupport();
				(void)InitializeProjectRuntimeSupport();
			}
		});
	} catch (...) {
		return FALSE;
	}
	return hook_initialization_succeeded ? TRUE : FALSE;
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID) {
	if (reason == DLL_PROCESS_ATTACH) {
		DisableThreadLibraryCalls(instance);
	}
	return TRUE;
}
