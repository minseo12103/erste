# Steamworks.NET dependency

- Source: https://github.com/rlabrecque/Steamworks.NET/releases/tag/2025.164.1
- Pinned version: 2025.164.1, commit prefix d693082
- Installation: embedded package `Packages/com.rlabrecque.steamworks.net`
- Download: https://api.github.com/repos/rlabrecque/Steamworks.NET/zipball/2025.164.1
- Downloaded archive SHA-256: `282e8ed365fdebcde3e9344b45d77e7cdf1b5a590540d09d7ef1a57e70779356`
- Package includes its original MIT license and matching native Steam API libraries.

The prototype uses development App ID 480 (Spacewar). The shipping game must use its own App ID. The prototype does not call RestartAppIfNecessary, since launching app 480 would start Valve's Spacewar instead of this local prototype.

Steam lobbies carry game and protocol metadata. A matching Steam lobby is required before a networking session is accepted. LAN mode is a separate direct TCP development transport, intended for local testing, with the same host/client gameplay protocol; it is not a substitute for a Steam interoperability test.
