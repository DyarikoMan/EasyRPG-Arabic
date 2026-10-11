#include "doctest.h"
#include "widescreen_asset.h"
#include "filefinder.h"
#include <tuple>

TEST_SUITE_BEGIN("WidescreenAsset");

TEST_CASE("Classic resolution and missing wide images keep the original name") {
	CHECK(WidescreenAsset::SelectName("MyTitle", 320, true) == "MyTitle");
	CHECK(WidescreenAsset::SelectName("MyTitle", 426, false) == "MyTitle");
	CHECK(WidescreenAsset::SelectName("Nested/MyTitle", 560, false) == "Nested/MyTitle");
}

TEST_CASE("Title and battle background use the wide sibling when present") {
	CHECK(WidescreenAsset::SelectName("Title/MyTitle", 426, true) == "Title/MyTitle_wide");
	CHECK(WidescreenAsset::SelectName("Backdrop/Forest", 426, true) == "Backdrop/Forest_wide");
	CHECK(WidescreenAsset::SelectName("Frame/Clouds", 560, true) == "Frame/Clouds_wide");
}

TEST_CASE("Wide sibling naming preserves nested paths and normal image extension lookup") {
	CHECK(WidescreenAsset::MakeWideSibling("Title/Folder/MyTitle") == "Title/Folder/MyTitle_wide");
	CHECK(WidescreenAsset::MakeWideSibling("Backdrop/Forest") == "Backdrop/Forest_wide");
	CHECK(WidescreenAsset::IsWideSibling("MyTitle_wide"));
	CHECK(!WidescreenAsset::IsWideSibling("MyTitle"));
	CHECK(FileFinder::IMG_TYPES[0] == ".bmp");
	CHECK(FileFinder::IMG_TYPES[1] == ".png");
	CHECK(FileFinder::IMG_TYPES[2] == ".xyz");

	// The wide lookup still goes through FileFinder's existing case-insensitive
	// path handling and supported image-extension list.
	auto assets = FileFinder::Root().Subtree(EP_TEST_PATH);
	auto image = assets.FindFile("rtp/FaceSet/actor2", FileFinder::IMG_TYPES);
	CHECK(!image.empty());
	std::string directory;
	std::string filename;
	std::tie(directory, filename) = FileFinder::GetPathAndFilename(image);
	CHECK(filename == "Actor2.png");
}

TEST_SUITE_END();
