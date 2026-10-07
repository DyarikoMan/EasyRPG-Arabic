#include "window_keyboard.h"
#include "doctest.h"

#include <algorithm>
#include <string>
#include <vector>

TEST_SUITE_BEGIN("Window_Keyboard");

TEST_CASE("Arabic layout contains the requested character set") {
	const auto& layout = Window_Keyboard::layouts[Window_Keyboard::Arabic];
	std::vector<std::string> keys;
	for (const auto& row : layout.items) {
		for (const auto& key : row) {
			keys.push_back(key);
		}
	}

	const std::vector<std::string> required = {
		"ا", "ب", "ت", "ث", "ج", "ح", "خ", "د", "ذ", "ر", "ز", "س", "ش", "ص",
		"ض", "ط", "ظ", "ع", "غ", "ف", "ق", "ك", "ل", "م", "ن", "ه", "و", "ي",
		"ء", "أ", "إ", "آ", "ؤ", "ئ", "ة", "ى", "ـ", "َ", "ً", "ُ", "ٌ", "ِ", "ٍ", "ّ", "ْ"
	};
	for (const auto& key : required) {
		CHECK(std::find(keys.begin(), keys.end(), key) != keys.end());
	}

	const std::vector<std::string> excluded = {"گ", "ڤ", "پ", "چ", "ژ", "٠", "١", "٢", "٣", "٤", "٥", "٦", "٧", "٨", "٩"};
	for (const auto& key : excluded) {
		CHECK(std::find(keys.begin(), keys.end(), key) == keys.end());
	}

	const std::vector<std::string> digits = {"1", "2", "3", "4", "5", "6", "7", "8", "9", "0"};
	for (size_t i = 0; i < digits.size(); ++i) {
		CHECK(layout.items[5][i] == digits[i]);
	}
	CHECK(layout.items[7][0] == Window_Keyboard::SPACE);
	CHECK(layout.items[8][6] == Window_Keyboard::NEXT_PAGE);
	CHECK(layout.items[8][8] == Window_Keyboard::DONE);
}

TEST_SUITE_END();
