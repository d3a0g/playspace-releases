# Third-party notices

The MIT license in this repository applies to original LauncherPlaySpace code.
It does not relicense games, store artwork, screenshots, downloaded mods or NVIDIA runtime files.

- Newtonsoft.Json 13.0.4 — MIT, https://github.com/JamesNK/Newtonsoft.Json/blob/master/LICENSE.md
- SharpCompress 0.50.8 — MIT. Full notice: GameLauncherGUI/Assets/ThirdParty/SharpCompress-LICENSE.txt; https://github.com/adamhathcock/sharpcompress . Used for in-process 7z extraction; users do not need a separate 7-Zip installation.
- OptiScaler 0.9.4 is an optional, separately downloaded GPL-3.0 component: https://github.com/optiscaler/OptiScaler/tree/v0.9.4 . Its binaries are not bundled with PLAYSPACE. Installation retains upstream runtime license files and adds the GPL text and source links alongside the managed module. NVIDIA DLSS files remain subject to NVIDIA's SDK license; AMD/Intel/DirectX libraries retain their bundled terms. PLAYSPACE does not relicense these components under MIT.
- Optional Windows setup packages are built with Inno Setup by Jordan Russell and contributors. Inno Setup and its setup engine retain their own license: https://jrsoftware.org/files/is/license.txt . The compiler is a development tool and is not included in the launcher source archive.
- .NET / Windows Forms — MIT and applicable third-party notices, https://github.com/dotnet/runtime/blob/main/LICENSE.TXT and https://github.com/dotnet/winforms/blob/main/LICENSE.TXT
- Steam game artwork and screenshots shown in the gallery belong to their respective publishers. They demonstrate the launcher UI and are not covered by the project's MIT license. Steam is a trademark of Valve Corporation. This is an independent project.
- UniversalDLSS5 by MotionflowOffical is an optional, separately downloaded component under Apache-2.0 and its bundled notices. Its installer and proprietary NVIDIA DLLs are not included in this repository or the launcher ZIP. See docs/features/dlss/UNIVERSAL-DLSS5.md.
- Downloaded Thunderstore packages, Paradox mods and other community content retain their authors' licenses. Installing them does not transfer ownership to this project.
- Additional game/executable/data-folder mappings were cross-checked against the public Thunderstore ecosystem configuration maintained in https://github.com/ebkr/r2modmanPlus/blob/develop/src/assets/data/ecosystem.json and live Thunderstore catalogs. No upstream installer or mod binary is embedded in the launcher release. The project's copyright notice does not cover third-party mods, loaders or runtime files.
- Cloudflare WARP is an optional official component downloaded directly from Cloudflare after user consent. It is not bundled, modified or relicensed under MIT. Cloudflare's application terms and privacy policy apply: https://www.cloudflare.com/application/terms/ and https://www.cloudflare.com/application/privacypolicy/. See docs/features/networking/NETWORK.md.

Self-contained releases include Microsoft .NET runtime components under their applicable distribution terms. Runtime notices are available in the .NET sources above and at https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT.
