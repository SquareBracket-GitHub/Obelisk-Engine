This distribution contains an altered SDL 3.4.16 runtime, 3.4.16-obelisk.1,
built for Windows x64 by Obelisk Engine. It is not an unmodified upstream binary.
Original SDL copyright and zlib license: ../SDL-LICENSE.txt.

Change: WIN_QuitDeviceNotification closes and resets its hotplug event after
joining the device-notification thread and unregistering notification callbacks.
The accompanying patch and build.json identify the exact source, toolchain and
SHA-256 checksums. The original source archive is available at the URL in build.json.
Rebuild procedure: scripts/Build-SdlNative.ps1 in the Obelisk Engine repository.
This alteration does not modify SDL's public API or the SDL3-CS managed binding.
