# PlaySpace 2.0 dependencies

PlaySpace source is covered by the project LICENSE. The native application uses React, TypeScript, SCSS, Lucide, Vite, Tauri and Rust crates. Their individual licenses continue to apply. The portable ZIP includes `licenses/DEPENDENCIES.json` with dependency versions, license identifiers and upstream repositories, and copies of license/notice files distributed in the downloaded Rust crate sources.

React, TypeScript, Lucide, Vite and Sass retain their respective MIT licenses; Tauri and most direct Rust dependencies use MIT and/or Apache-2.0. Exact transitive versions and license identifiers are recorded in the dependency inventory, rather than assumed from the top-level dependency.

Game artwork and store metadata remain the property of their respective owners. Steam, Epic, Thunderstore, Cloudflare WARP and NVIDIA are separate services and products. The launcher does not grant rights to game or mod content.

The portable launcher does not bundle WARP, NVIDIA DLSS, OptiScaler or UniversalDLSS5 executable components. Optional downloads come from their upstream sources and retain their upstream licenses. The launcher checks signatures and/or pinned hashes before use.

OptiScaler 0.9.4: GPL-3.0, source https://github.com/optiscaler/OptiScaler/tree/v0.9.4. Installed payload includes its upstream license notices and a copy of the NVIDIA license when NVIDIA components are imported. NVIDIA DLSS source/release: https://github.com/NVIDIA/DLSS. UniversalDLSS5 0.5.1: third-party experimental controller by MotionflowOffical, Apache-2.0, https://github.com/MotionflowOffical/UniversalDLSS5. These components are not relicensed under the launcher's MIT license.

JavaScript build/runtime dependency notices are also included under `licenses/javascript/`. Build-only packages do not become launcher-owned code.
