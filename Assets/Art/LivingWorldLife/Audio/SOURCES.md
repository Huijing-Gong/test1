# Natural ambience sources

Retrieved 2026-10-24. All four source pages explicitly mark **Creative Commons 0**.
License: https://creativecommons.org/publicdomain/zero/1.0/
These are recordings, not generated bird imitations. No voice, music or paid asset
is introduced. Attribution is retained even though CC0 does not require it.

| Game asset | Author and original source page | Download used | Modification |
|---|---|---|---|
| LakeShore.ogg | Anya_Media, “Lake waves.wav”, https://freesound.org/people/Anya_Media/sounds/523392/ | https://cdn.freesound.org/previews/523/523392_2010973-hq.mp3 | Original recording excerpt 18–39 s; mono; 80 Hz high-pass, 8.5 kHz low-pass; -23 LUFS normalization; circular 1 s equal-power join; 20 s Vorbis loop. |
| CanopyBreeze.ogg | nickydunne, “Wind in trees.WAV”, https://freesound.org/people/nickydunne/sounds/463551/ | https://cdn.freesound.org/previews/463/463551_9359506-hq.mp3 | Excerpt 12–33 s; mono; 160 Hz high-pass, 8 kHz low-pass; -25 LUFS normalization; circular 1 s equal-power join; 20 s Vorbis loop. Original includes faint distant birds and nearby river. |
| BirdChirpA.ogg | Allan Legemaate / alegemaate, “Single Bird Chirp”, https://freesound.org/people/alegemaate/sounds/233255/ | https://cdn.freesound.org/previews/233/233255_2531187-hq.mp3 | Mono 44.1 kHz; 250 Hz high-pass, 10 kHz low-pass; short end fade; Vorbis. |
| BirdChirpB.ogg | Allan Legemaate / alegemaate, “Single Bird Chirp 2”, https://freesound.org/people/alegemaate/sounds/233254/ | https://cdn.freesound.org/previews/233/233254_2531187-hq.mp3 | Mono 44.1 kHz; same filters; short end fade; Vorbis. |

Preview downloads retain the asset's CC0 license. Compressed previews were chosen
to avoid downloading full uncompressed source recordings unnecessarily. Unity
imports the two loops as compressed streaming audio; short calls are preloaded,
mono compressed-in-memory audio. There is no runtime web access.

Runtime has exactly five reusable AudioSources. Shore sound is spatial and follows
the nearest authored shoreline anchor with a smooth handover. The canopy bed is
a quiet regional nonspatial wash, modulated by actual tree proximity and the same
travelling gust expression as the vegetation shader. Bird calls are spatial,
limited to nearby trees and real wildlife takeoffs. Host/menu/travel suspension
pauses these voices only; global audio and existing game music are untouched.
