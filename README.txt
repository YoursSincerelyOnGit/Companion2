Companion - Milestone 1 (avatar version)
========================================
Needs: .NET 8 SDK (https://dotnet.microsoft.com/download) on Windows.

Run:
  1. Open a terminal in this folder.
  2. dotnet run
  (Or open Companion.csproj in Visual Studio 2022 and press F5.)

Controls:
  - He follows your cursor, stops ~240px away, and sits after ~4s of stillness.
  - Left-drag him to move him by hand.
  - Right-click him: toggle "Follow cursor", "Celebrate (test)" (alternates thumbs-up / flex), or Quit.

Sprites live in Assets/ (body.png, thumbs.png, flex.png), cut from your avatar.
To add a pose: drop a transparent PNG in Assets/, register it in the MainWindow
constructor (_sprites["name"] = new SpriteDef(LoadSprite("name.png"), height)), then use it in ApplyPose.
