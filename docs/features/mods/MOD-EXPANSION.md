# Расширение модов: локальная версия 1.0.5-preview

Авторские права (c) 2026 d3a0g
Copyright (c) 2026 d3a0g

Добавлено 37 адаптеров: 23 каталога Thunderstore и 14 ZIP-адаптеров. Всего теперь 69 игр: 36 Thunderstore, 28 ZIP, 5 Paradox. Все новые Steam ID есть в рекомендациях приложения.

## Проверенные каталоги

Проверка выполнена 7 ноября 2026 года: реальные API, ZIP загрузчика, разрешение зависимостей и маршрутизация хотя бы одного DLL-плагина на игру. Игры не запускались; это не проверка игровой сессии или совместимости всех версий. Число пакетов включает библиотеки и инструменты и не означает, что каждый пакет устанавливается автоматически.

| Игра | Steam ID | Каталог | Пакетов на момент проверки | Загрузчик | Проверенный пакет |
|---|---|---|---:|---|---|
| 9 Kings | 2784470 | [Thunderstore](https://thunderstore.io/c/9-kings/) | 7 | BepInEx-BepInExPack_IL2CPP | Caioreix-9KCheatMode |
| A Little to the Left | 1629520 | [Thunderstore](https://thunderstore.io/c/a-little-to-the-left/) | 3 | BepInEx-BepInExPack_IL2CPP | drohack-A_Little_to_the_Left_Archipelago |
| Among Us | 945360 | [Thunderstore](https://thunderstore.io/c/among-us/) | 17 | BepInEx-BepInExPack_AmongUs | HuierHuang-UnityExplorer |
| Cities: Skylines II | 949230 | [Thunderstore](https://thunderstore.io/c/cities-skylines-ii/) | 284 | BepInEx-BepInExPack | algernon-Unified_Icon_Library |
| Cuphead | 268910 | [Thunderstore](https://thunderstore.io/c/cuphead/) | 4 | BepInEx-BepInExPack | JKLeckr-CupheadArchipelago |
| Deep Rock Galactic: Survivor | 2321470 | [Thunderstore](https://thunderstore.io/c/deep-rock-galactic-survivor/) | 18 | BepInEx-BepInExPack_IL2CPP | HyperGolicMage-AutoPickUp |
| DREDGE | 1562430 | [Thunderstore](https://thunderstore.io/c/dredge/) | 4 | BepInEx-BepInExPack_x86 | Legoless-Rod_Locker |
| Enter the Gungeon | 311690 | [Thunderstore](https://thunderstore.io/c/enter-the-gungeon/) | 468 | BepInEx-BepInExPack_EtG | www_Day_Dream-CessilCellsCeaChells |
| Graveyard Keeper 2 | 4358690 | [Thunderstore](https://thunderstore.io/c/graveyard-keeper-2/) | 37 | BepInEx-BepInExPack | Service_Accounts-SharedStorage |
| Hollow Knight: Silksong | 1030300 | [Thunderstore](https://thunderstore.io/c/hollow-knight-silksong/) | 491 | silksong_modding-BepInExPack_Silksong | MonoDetour-MonoDetour |
| How to Fish | 4001890 | [Thunderstore](https://thunderstore.io/c/how-to-fish/) | 283 | BepInEx-BepInExPack | WhitetailStudios-KeepInventory |
| Human Fall Flat | 477160 | [Thunderstore](https://thunderstore.io/c/human-fall-flat/) | 15 | BepInEx-BepInExPack_HumanFallFlat | Lighty-StrongHuman |
| Monster Train 2 | 2742830 | [Thunderstore](https://thunderstore.io/c/monster-train-2/) | 73 | BepInEx-BepInExPack | GravitonGamer-Upgrade_Champions_2 |
| Nine Sols | 1809540 | [Thunderstore](https://thunderstore.io/c/nine-sols/) | 99 | BepInEx-BepInExPack | ninesolsmodding-BepinExConfigurationManager |
| Raft | 648800 | [Thunderstore](https://thunderstore.io/c/raft/) | 3 | BepInEx-BepInExPack | SolomonsTriad-RaftAdminMenu |
| Rhythm Doctor | 774181 | [Thunderstore](https://thunderstore.io/c/rhythm-doctor/) | 5 | BepInEx-BepInExPack | nfrm-Pulse |
| Sons Of The Forest | 1326470 | [Thunderstore](https://thunderstore.io/c/sons-of-the-forest/) | 101 | BepInEx-BepInExPack_IL2CPP | CallMeSlinky-DebugConsole |
| Subnautica | 264710 | [Thunderstore](https://thunderstore.io/c/subnautica/) | 118 | Subnautica_Modding-BepInExPack | Subnautica_Modding-MirrorInternalLogs |
| SULFUR | 2124120 | [Thunderstore](https://thunderstore.io/c/sulfur/) | 43 | BepInEx-BepInExPack | piyoryyta-IsItWorth |
| The Planet Crafter | 1284190 | [Thunderstore](https://thunderstore.io/c/the-planet-crafter/) | 16 | BepInEx-BepInExPack | ViViKo-MachineTweaks |
| Trombone Champ | 1059990 | [Thunderstore](https://thunderstore.io/c/trombone-champ/) | 86 | BepInEx-BepInExPack_TromboneChamp | TromboneChamps-Newtonsoft_Json |
| V Rising | 1604030 | [Thunderstore](https://thunderstore.io/c/v-rising/) | 324 | BepInEx-BepInExPack_V_Rising | Mythic-ServerLaunchFix |
| White Knuckle | 3195790 | [Thunderstore](https://thunderstore.io/c/white-knuckle/) | 426 | BepInEx-BepInExPack | prideunique-ImuiBepInEx |

## ZIP и ограничения

Новые форматы описаны в MODS.md. ZIP должен содержать один мод, подходящую метаинформацию или VPK/PACK. XML читается без DTD и внешних сущностей. Нельзя подменять оригинальные файлы; удаление использует журнал владения.

Моды включаются в меню игры/официальном лаунчере, если этого требует экосистема. Не включаются античит-обходы, изменения EAC или разрешения Windows. Размещение ZIP не гарантирует поддержку текущей версии игры или сетевых серверов. No Man's Sky не добавлен: нельзя смешивать разные поколения его путей и форматов.

Дополнительно поддержана упакованная среда dotnet загрузчиков BepInEx 6/IL2CPP: разрешены библиотечные файлы и dotnet.exe, но командные сценарии отклоняются. Эти файлы скачиваются только при установке соответствующего загрузчика и не включены в сборку PlaySpace.

## Источники

- Конфигурация игр: https://github.com/ebkr/r2modmanPlus/blob/develop/src/assets/data/ecosystem.json
- Left 4 Dead, размещение VPK: https://www.l4d.com/blog/post.php?id=4771
- Bannerlord, модули: https://moddocs.bannerlord.com/asset-management/quickguide_create_a_mod/
- Civilization VI SDK: https://github.com/Wild-W/Civ-6-Documentation
- Формат Total War PACK: https://github.com/TotalWar-Modding/docs/blob/master/pack%20file%20format.md

## Лицензия

LICENSE содержит русский перевод перед неизменёнными условиями английского MIT. Автор оригинального кода: d3a0g. Сторонние авторы сохраняют свои права; имя разработчика не заменяет их уведомления.

## Проверки

dotnet run --project GameLauncherGUI.Tests -c Release -- --expanded-mods

118 проверок: привязка к рекомендациям, идентификация папок, установка и удаление новых ZIP-форматов, отказ от выхода за папку, XXE и командных файлов. Только временные тестовые папки.
