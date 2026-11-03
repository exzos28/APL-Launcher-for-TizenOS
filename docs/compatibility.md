# Compatibility

What works today, what works with limits, what has not been tested on a real screen yet, and what is not possible.
Every "Works" below was checked on a real screen, not only in the PC preview.

| | Meaning |
|---|---|
| ✅ | Works, checked on a real screen |
| ⚠️ | Works, with the limits given in the notes |
| 🧪 | Built, but not yet checked on a real screen |
| ❌ | Not possible on these screens |

## Screens

| Screen | Status | Notes |
|---|---|---|
| Samsung Smart Monitor M8, Tizen 9 | ✅ | Developed and used every day on this screen, rooted with Vetro. |
| Samsung The Frame 2025 (43", Tizen 9), launcher alone through Apps2Samsung | ⚠️ | Reported by a user: Home and a full start open the launcher. Coming back to it after sleep needs 1.0.2, not yet confirmed on that screen. No APL Overlay in this package. |
| Other Samsung Smart Monitors and TVs | 🧪 | Not tested yet. They need Vetro, so only screens that Vetro supports can work. Reports are welcome in the issues. |
| Samsung screens without root / Vetro | ❌ | The launcher needs Vetro for the Home button, the remote buttons and the system controls. |

## Remotes

| Remote | Status | Notes |
|---|---|---|
| Samsung Smart Remote (*Smart Control 2016*), paired over Bluetooth | ✅ | Every button, the microphone included. |
| The same remote, infrared only | ⚠️ | Every button except the microphone: by infrared it only asks the screen to show the pairing guide. |
| Other remotes and keyboards | 🧪 | Not tested. |

Vetro intercepts one device at a time: pick the remote in Vetro › Remapper (see [Installation](installation.md)).

## PC

| | Status | Notes |
|---|---|---|
| Windows 11 | ✅ | APL Launcher Setup. |
| Windows 10 | 🧪 | Should work the same; not tested. |
| macOS, Linux | ❌ | APL Launcher Setup is a Windows app. The PC preview (`launcher/index.html`) opens in any browser. |

## Features

### Home screen

| Feature | Status | Notes |
|---|---|---|
| Top Shelf, glass dock, grid, focus motion, sounds | ✅ | 57–60 frames per second once the screen has settled. |
| Back to the launcher when an app closes | ✅ | In less than 0.65 s. |
| Hold OK on an app: Edit Home Screen, Hide, Delete | ✅ | System apps cannot be deleted. |
| Live Top Shelf (trending titles, Game Pass, Steam, Ligue 1, top albums…) | ✅ | Needs a free TMDB key; YouTube trends need a YouTube key. |
| Official logos and automatic artwork for other apps | ✅ | |
| Household profiles and Memoji | ✅ | |
| Interface in English and French | ✅ | French when the screen is in French; *Settings › General & Privacy › Language* to choose. |
| Launcher interface in 4K | ❌ | The screen draws apps and menus in a single 1920×1080 mode, Samsung's own menus included. Only video is 4K. |

### Continue Watching

| App | Status | Notes |
|---|---|---|
| AquaPlay: last channel | ✅ | Channel name and a sharp logo for 203 French channels. |
| AquaPlay: film or series | ✅ | Picture with the title from TMDB. |
| AquaPlay: progress of a series episode | 🧪 | Checked with simulated data on the PC; not seen yet on the screen. |
| AquaPlay: open straight on a channel or series | ❌ | AquaPlay reads no launch request; its own settings can resume the last channel. |
| Stremio | 🧪 | Read from real Stremio data on the PC; not seen yet on the screen. |
| Hot Player | ⚠️ | Hot Player keeps only the episode number, never the series or channel name. |
| Netflix, Prime Video, Disney+, YouTube, Crunchyroll… | ❌ | They keep nothing readable on the screen and have no public API for your history. The Top Shelf shows what is popular instead. |

### Remote buttons

| Feature | Status | Notes |
|---|---|---|
| Home: back to the launcher | ✅ | After a full restart, Vetro takes up to 2 minutes to take over the remote. |
| Home twice: App Switcher | ✅ | |
| Hold Home, or ⏯: Control Center, even over a video | ✅ | ⏯ no longer pauses videos. |
| Samsung TV Plus → AquaPlay, Prime Video → TizenTube | ✅ | Only when those apps are installed; otherwise the buttons keep their Samsung role. |
| A stuck key released by itself | ⚠️ | Seen working with the infrared remote; not yet seen over Bluetooth. |

### Control Center and Settings

| Feature | Status | Notes |
|---|---|---|
| Control Center on the launcher and over any app | ✅ | Over apps it is drawn by APL Overlay. |
| Sleep, Wi-Fi, Do Not Disturb, Sleep Timer, Brightness | ✅ | |
| Audio output and Bluetooth headphones (AirPods…) | ✅ | |
| Settings covering every Samsung settings page | ✅ | Real picture controls (backlight, contrast, sharpness, colour, local dimming); a few pages open Samsung's own settings. |
| Sources: inputs and the device on each | ✅ | |

### Voice

| Feature | Status | Notes |
|---|---|---|
| Alexa on the microphone button | ✅ | With the remote paired over Bluetooth, after signing in to Amazon once. |
| Siri-like glow while you speak | ✅ | |
| Alexa+ | ❌ | Samsung's Alexa app on this screen is the classic Alexa. |
| Text of what you say and of Alexa's answer | ❌ | Alexa on this screen sends neither; she only speaks. |
| An Echo opening apps on the screen | 🧪 | Built; not yet tried with a real Echo. |
| Bixby | ✅ | Turned off by Optimize, and back with Restore. |

### Screen saver

| Feature | Status | Notes |
|---|---|---|
| Apple's aerial videos in 4K | ✅ | 3840×2160 HEVC on the screen's video plane; 1080p if a 4K video is refused. |
| Cross-fade between two 4K videos | 🧪 | The screen decodes one 4K video at a time: the old one holds its last picture during the fade. Not seen yet. |
| Apple's newest aerials | ❌ | Published only in a format the screen refuses. They are left out of the list. |

### System

| Feature | Status | Notes |
|---|---|---|
| Apps left 10 minutes in the background close by themselves | ✅ | Never the app in front, the launcher, the TV inputs or AirPlay. Samsung's apps that open by themselves and were never used close after 1 minute. |
| Optimize and Restore | ✅ | Checked on the screen with the scripts; the new app runs the same scripts. |
| Full restart (`reboot`) | ✅ | The launcher is back after about a minute. |
| Power cut, or a device switched on on HDMI at start-up | 🧪 | Not tested. |
| Automatic updates from GitHub | 🧪 | Built, with automatic rollback; not yet tried end to end. They need this repository to be public. |
| TizenBrew and TizenTube | ✅ | Not needed; the launcher opens them and leaves them alone. |
| Casting YouTube from a phone | 🧪 | Not tested since Samsung's YouTube app was removed on the test screen. |
| Steam Link and Moonlight | ✅ | Shown with their own artwork when they are installed. The installer does not install them. |
| Android apps (APK) | ❌ | Tizen cannot run them. |
