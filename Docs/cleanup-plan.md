# Очищення робочої папки v1.7.7

Перенесено 86 застарілі результати роботи (3 486 файлів, 3 020 002 324 байти) до D:\DeadAirUnityProject\trash. Нічого не видалено; помилок перенесення немає. Відносні шляхи збережені, тому файли можна повернути на початкове місце. Місце на диску звільниться лише після ручного видалення trash користувачем.

Повний машинний звіт із шляхами, кількістю файлів, розмірами, перевірками та відхиленими попередніми спробами видалення: Builds/Optimization-v1.7.7/cleanup-result.json. Інвентар до очищення: Builds/Optimization-v1.7.7/cleanup-inventory.json. /trash/ виключено з Git.

Збережено .git, Assets, Art, Library, Packages, ProjectSettings, UserSettings, .claude, LocalCatalog, Tools, scripts та Docs. Також збережено всі Builds/Releases, усі ZIP/контрольні суми інших релізів, метадані завантажень/порівняння ReleaseAudit-20261007, оригінальний ZIP v1.7.4 та актуальні матеріали:

- D:\DeadAirUnityProject\Builds\Windows-v1.7.6-final
- D:\DeadAirUnityProject\Builds\Verification-v1.7.6\Player-final
- D:\DeadAirUnityProject\Builds\Verification-v1.7.6\Results-final
- D:\DeadAirUnityProject\Builds\Verification-v1.7.6\Update-final
- D:\DeadAirUnityProject\Builds\Verification-v1.7.6\Graphics-fifth
- D:\DeadAirUnityProject\Builds\Optimization-v1.7.7

Усі файли в корені Builds/Verification-v1.7.6 (вихідні тексти, скрипти, DLL, patch, compile.rsp) залишилися. Список 133 згенерованих статичних мешів без GUID-посилань є лише матеріалом для перевірки; Assets не змінювалися під час очищення. Library також залишилася для стабільного повторного білду.

Перед кожним перенесенням перевірено абсолютні початковий і цільовий шляхи: обидва всередині робочої папки; ціль — строго всередині trash; початковий шлях не містить і не охоплює захищені дані. У початковому дереві та всіх предках обох шляхів не було reparse points. Колізії відхилялися; після перенесення кількість і сумарний розмір файлів повторно звірялися.

| Початковий абсолютний шлях | Новий абсолютний шлях | Байти | Файли |
| --- | --- | ---: | ---: |
| D:\DeadAirUnityProject\Builds\Verification-v1.7.5 | D:\DeadAirUnityProject\trash\Builds\Verification-v1.7.5 | 539176890 | 641 |
| D:\DeadAirUnityProject\Builds\ReleaseAudit-20261007\RuleAudit | D:\DeadAirUnityProject\trash\Builds\ReleaseAudit-20261007\RuleAudit | 11748182 | 64 |
| D:\DeadAirUnityProject\Builds\ReleaseAudit-20261007\Runtime-v1.7.2 | D:\DeadAirUnityProject\trash\Builds\ReleaseAudit-20261007\Runtime-v1.7.2 | 15291620 | 17 |
| D:\DeadAirUnityProject\Builds\ReleaseAudit-20261007\RuntimeAudit | D:\DeadAirUnityProject\trash\Builds\ReleaseAudit-20261007\RuntimeAudit | 171473184 | 184 |
| D:\DeadAirUnityProject\Builds\ReleaseAudit-20261007\v1.7.2 | D:\DeadAirUnityProject\trash\Builds\ReleaseAudit-20261007\v1.7.2 | 160130118 | 164 |
| D:\DeadAirUnityProject\Builds\Verification-v1.7.3 | D:\DeadAirUnityProject\trash\Builds\Verification-v1.7.3 | 433749026 | 464 |
| D:\DeadAirUnityProject\Builds\Windows-v1.7.5-final | D:\DeadAirUnityProject\trash\Builds\Windows-v1.7.5-final | 160622989 | 162 |
| D:\DeadAirUnityProject\Builds\Windows-v1.7.5 | D:\DeadAirUnityProject\trash\Builds\Windows-v1.7.5 | 160590669 | 162 |
| D:\DeadAirUnityProject\Builds\Windows-v1.7.3 | D:\DeadAirUnityProject\trash\Builds\Windows-v1.7.3 | 160302242 | 161 |
| D:\DeadAirUnityProject\Builds\Windows | D:\DeadAirUnityProject\trash\Builds\Windows | 159612268 | 167 |
| D:\DeadAirUnityProject\Builds\.release-staging | D:\DeadAirUnityProject\trash\Builds\.release-staging | 145855047 | 168 |
| D:\DeadAirUnityProject\Builds\Diagnostics | D:\DeadAirUnityProject\trash\Builds\Diagnostics | 4172229 | 17 |
| D:\DeadAirUnityProject\Builds\Windows-v1.7.6 | D:\DeadAirUnityProject\trash\Builds\Windows-v1.7.6 | 2684 | 2 |
| D:\DeadAirUnityProject\Builds\Verification-v1.7.6\Player-before-reflection-uv | D:\DeadAirUnityProject\trash\Builds\Verification-v1.7.6\Player-before-reflection-uv | 165987723 | 162 |
| D:\DeadAirUnityProject\Builds\Verification-v1.7.6\Graphics-fourth | D:\DeadAirUnityProject\trash\Builds\Verification-v1.7.6\Graphics-fourth | 7131664 | 17 |
| D:\DeadAirUnityProject\Builds\Verification-v1.7.6\Graphics-third | D:\DeadAirUnityProject\trash\Builds\Verification-v1.7.6\Graphics-third | 5035040 | 15 |
| D:\DeadAirUnityProject\Builds\Verification-v1.7.6\Graphics-second | D:\DeadAirUnityProject\trash\Builds\Verification-v1.7.6\Graphics-second | 733103 | 7 |
| D:\DeadAirUnityProject\Builds\Verification-v1.7.6\Graphics-first | D:\DeadAirUnityProject\trash\Builds\Verification-v1.7.6\Graphics-first | 650618 | 6 |
| D:\DeadAirUnityProject\Verification-ImportedCharacters | D:\DeadAirUnityProject\trash\Verification-ImportedCharacters | 94223665 | 154 |
| D:\DeadAirUnityProject\Verification-Release-v1.5.0 | D:\DeadAirUnityProject\trash\Verification-Release-v1.5.0 | 42056383 | 48 |
| D:\DeadAirUnityProject\Verification-20261001-grips | D:\DeadAirUnityProject\trash\Verification-20261001-grips | 37642006 | 34 |
| D:\DeadAirUnityProject\Verification-20261001-final | D:\DeadAirUnityProject\trash\Verification-20261001-final | 35096792 | 32 |
| D:\DeadAirUnityProject\Verification-20261001 | D:\DeadAirUnityProject\trash\Verification-20261001 | 34545449 | 32 |
| D:\DeadAirUnityProject\Verification-Presentation | D:\DeadAirUnityProject\trash\Verification-Presentation | 27998153 | 77 |
| D:\DeadAirUnityProject\Verification-Characters | D:\DeadAirUnityProject\trash\Verification-Characters | 26587409 | 54 |
| D:\DeadAirUnityProject\Verification-20260929 | D:\DeadAirUnityProject\trash\Verification-20260929 | 13837003 | 16 |
| D:\DeadAirUnityProject\Verification-IndieUpgrade | D:\DeadAirUnityProject\trash\Verification-IndieUpgrade | 10045514 | 16 |
| D:\DeadAirUnityProject\Verification-20261001-window | D:\DeadAirUnityProject\trash\Verification-20261001-window | 438024 | 32 |
| D:\DeadAirUnityProject\Logs\AssetImportWorkerHW0-prev.log | D:\DeadAirUnityProject\trash\Logs\AssetImportWorkerHW0-prev.log | 10870 | 1 |
| D:\DeadAirUnityProject\Logs\office-windows-build.log | D:\DeadAirUnityProject\trash\Logs\office-windows-build.log | 483327 | 1 |
| D:\DeadAirUnityProject\Logs\office-finalize.log | D:\DeadAirUnityProject\trash\Logs\office-finalize.log | 531165 | 1 |
| D:\DeadAirUnityProject\Logs\office-editor.log | D:\DeadAirUnityProject\trash\Logs\office-editor.log | 4030244 | 1 |
| D:\DeadAirUnityProject\Logs\menu-editor.log | D:\DeadAirUnityProject\trash\Logs\menu-editor.log | 517510 | 1 |
| D:\DeadAirUnityProject\Logs\menu-editor-polish.log | D:\DeadAirUnityProject\trash\Logs\menu-editor-polish.log | 82718 | 1 |
| D:\DeadAirUnityProject\Logs\menu-editor-final.log | D:\DeadAirUnityProject\trash\Logs\menu-editor-final.log | 84896 | 1 |
| D:\DeadAirUnityProject\Logs\lighting-final-editor.log | D:\DeadAirUnityProject\trash\Logs\lighting-final-editor.log | 7492620 | 1 |
| D:\DeadAirUnityProject\Logs\lighting-editor.log | D:\DeadAirUnityProject\trash\Logs\lighting-editor.log | 5181760 | 1 |
| D:\DeadAirUnityProject\Logs\horror-runtime.log | D:\DeadAirUnityProject\trash\Logs\horror-runtime.log | 3473 | 1 |
| D:\DeadAirUnityProject\Logs\horror-runtime-final.log | D:\DeadAirUnityProject\trash\Logs\horror-runtime-final.log | 3910 | 1 |
| D:\DeadAirUnityProject\Logs\horror-polish-final-build.log | D:\DeadAirUnityProject\trash\Logs\horror-polish-final-build.log | 2880408 | 1 |
| D:\DeadAirUnityProject\Logs\horror-headlamp-final-build.log | D:\DeadAirUnityProject\trash\Logs\horror-headlamp-final-build.log | 3518852 | 1 |
| D:\DeadAirUnityProject\Logs\update-runtime.log | D:\DeadAirUnityProject\trash\Logs\update-runtime.log | 4455 | 1 |
| D:\DeadAirUnityProject\Logs\UDS-Service-prev.log | D:\DeadAirUnityProject\trash\Logs\UDS-Service-prev.log | 775 | 1 |
| D:\DeadAirUnityProject\Logs\repair-editor.log | D:\DeadAirUnityProject\trash\Logs\repair-editor.log | 3148533 | 1 |
| D:\DeadAirUnityProject\Logs\repair-editor-second.log | D:\DeadAirUnityProject\trash\Logs\repair-editor-second.log | 65959 | 1 |
| D:\DeadAirUnityProject\Logs\repair-editor-final.log | D:\DeadAirUnityProject\trash\Logs\repair-editor-final.log | 2751054 | 1 |
| D:\DeadAirUnityProject\Logs\release-v1.5.0-horror.log | D:\DeadAirUnityProject\trash\Logs\release-v1.5.0-horror.log | 7630 | 1 |
| D:\DeadAirUnityProject\Logs\release-v1.5.0-characters.log | D:\DeadAirUnityProject\trash\Logs\release-v1.5.0-characters.log | 9267 | 1 |
| D:\DeadAirUnityProject\Logs\release-runtime-v1.7.1.log | D:\DeadAirUnityProject\trash\Logs\release-runtime-v1.7.1.log | 4606 | 1 |
| D:\DeadAirUnityProject\Logs\presentation-runtime.log | D:\DeadAirUnityProject\trash\Logs\presentation-runtime.log | 7738 | 1 |
| D:\DeadAirUnityProject\Logs\horror-final-repairs.log | D:\DeadAirUnityProject\trash\Logs\horror-final-repairs.log | 5241 | 1 |
| D:\DeadAirUnityProject\Logs\horror-final-lighting.log | D:\DeadAirUnityProject\trash\Logs\horror-final-lighting.log | 4398 | 1 |
| D:\DeadAirUnityProject\Logs\character-runtime-dx11.log | D:\DeadAirUnityProject\trash\Logs\character-runtime-dx11.log | 9322 | 1 |
| D:\DeadAirUnityProject\Logs\character-runtime-default.log | D:\DeadAirUnityProject\trash\Logs\character-runtime-default.log | 9515 | 1 |
| D:\DeadAirUnityProject\Logs\catalog-editor.log | D:\DeadAirUnityProject\trash\Logs\catalog-editor.log | 1899716 | 1 |
| D:\DeadAirUnityProject\Logs\build-v1.7.5.log | D:\DeadAirUnityProject\trash\Logs\build-v1.7.5.log | 120687 | 1 |
| D:\DeadAirUnityProject\Logs\build-v1.7.5-verified.log | D:\DeadAirUnityProject\trash\Logs\build-v1.7.5-verified.log | 69876 | 1 |
| D:\DeadAirUnityProject\Logs\build-v1.7.5-retry.log | D:\DeadAirUnityProject\trash\Logs\build-v1.7.5-retry.log | 1298771 | 1 |
| D:\DeadAirUnityProject\Logs\build-v1.7.5-player.log | D:\DeadAirUnityProject\trash\Logs\build-v1.7.5-player.log | 49713 | 1 |
| D:\DeadAirUnityProject\Logs\build-v1.7.5-package.log | D:\DeadAirUnityProject\trash\Logs\build-v1.7.5-package.log | 142383 | 1 |
| D:\DeadAirUnityProject\Logs\build-v1.7.5-final.log | D:\DeadAirUnityProject\trash\Logs\build-v1.7.5-final.log | 152502 | 1 |
| D:\DeadAirUnityProject\Logs\build-v1.7.5-final-fixture.log | D:\DeadAirUnityProject\trash\Logs\build-v1.7.5-final-fixture.log | 66994 | 1 |
| D:\DeadAirUnityProject\Logs\build-StandaloneWindows64-1790697024815.log | D:\DeadAirUnityProject\trash\Logs\build-StandaloneWindows64-1790697024815.log | 463853 | 1 |
| D:\DeadAirUnityProject\Logs\build-StandaloneWindows64-1790677439343.log | D:\DeadAirUnityProject\trash\Logs\build-StandaloneWindows64-1790677439343.log | 1137545 | 1 |
| D:\DeadAirUnityProject\Logs\AssetImportWorkerHW0-UDS-prev.log | D:\DeadAirUnityProject\trash\Logs\AssetImportWorkerHW0-UDS-prev.log | 324 | 1 |
| D:\DeadAirUnityProject\Logs\character-runtime.log | D:\DeadAirUnityProject\trash\Logs\character-runtime.log | 33925 | 1 |
| D:\DeadAirUnityProject\Logs\horror-final-menu.log | D:\DeadAirUnityProject\trash\Logs\horror-final-menu.log | 5830 | 1 |
| D:\DeadAirUnityProject\Logs\Editor-prev.log | D:\DeadAirUnityProject\trash\Logs\Editor-prev.log | 2210860 | 1 |
| D:\DeadAirUnityProject\Logs\horror-final-horror.log | D:\DeadAirUnityProject\trash\Logs\horror-final-horror.log | 3910 | 1 |
| D:\DeadAirUnityProject\Logs\horror-editor.log | D:\DeadAirUnityProject\trash\Logs\horror-editor.log | 16421 | 1 |
| D:\DeadAirUnityProject\Logs\horror-editor-retry.log | D:\DeadAirUnityProject\trash\Logs\horror-editor-retry.log | 2987581 | 1 |
| D:\DeadAirUnityProject\Logs\horror-editor-final.log | D:\DeadAirUnityProject\trash\Logs\horror-editor-final.log | 4318172 | 1 |
| D:\DeadAirUnityProject\Logs\horror-editor-complete.log | D:\DeadAirUnityProject\trash\Logs\horror-editor-complete.log | 3638330 | 1 |
| D:\DeadAirUnityProject\Logs\horror-dust-final-build.log | D:\DeadAirUnityProject\trash\Logs\horror-dust-final-build.log | 3505045 | 1 |
| D:\DeadAirUnityProject\Logs\headlamp-diagnostic-editor.log | D:\DeadAirUnityProject\trash\Logs\headlamp-diagnostic-editor.log | 46400 | 1 |
| D:\DeadAirUnityProject\Logs\git-whitespace.log | D:\DeadAirUnityProject\trash\Logs\git-whitespace.log | 207 | 1 |
| D:\DeadAirUnityProject\Logs\git-add-repair.log | D:\DeadAirUnityProject\trash\Logs\git-add-repair.log | 314100 | 1 |
| D:\DeadAirUnityProject\Logs\fixed-v1.7.3-pose-build.log | D:\DeadAirUnityProject\trash\Logs\fixed-v1.7.3-pose-build.log | 72089 | 1 |
| D:\DeadAirUnityProject\Logs\fixed-v1.7.3-final-build.log | D:\DeadAirUnityProject\trash\Logs\fixed-v1.7.3-final-build.log | 58652 | 1 |
| D:\DeadAirUnityProject\Logs\fixed-v1.7.3-build.log | D:\DeadAirUnityProject\trash\Logs\fixed-v1.7.3-build.log | 1458599 | 1 |
| D:\DeadAirUnityProject\Logs\Editor-UDS-prev.log | D:\DeadAirUnityProject\trash\Logs\Editor-UDS-prev.log | 313 | 1 |
| D:\DeadAirUnityProject\Logs\Editor-grips.log | D:\DeadAirUnityProject\trash\Logs\Editor-grips.log | 577851 | 1 |
| D:\DeadAirUnityProject\Logs\visual-audio-editor.log | D:\DeadAirUnityProject\trash\Logs\visual-audio-editor.log | 930406 | 1 |
| D:\DeadAirUnityProject\Builds\Original-v1.7.4\Extracted | D:\DeadAirUnityProject\trash\Builds\Original-v1.7.4\Extracted | 160073647 | 165 |
| D:\DeadAirUnityProject\Builds\Verification-v1.7.7\Graphics | D:\DeadAirUnityProject\trash\Builds\Verification-v1.7.7\Graphics | 14377949 | 26 |
| D:\DeadAirUnityProject\Builds\Verification-v1.7.7\Native | D:\DeadAirUnityProject\trash\Builds\Verification-v1.7.7\Native | 164383733 | 165 |

Попередні спроби видалення були автоматично відхилені інструментом до виконання з причиною «blocked by policy»: пакет кандидатів, точний каталог Builds/.release-staging та окремий Logs/AssetImportWorkerHW0-UDS-prev.log. Подальші спроби видалення припинено; за новим прямим дорученням користувача виконано оборотне перенесення до trash.

Повторно використовувані нативні перевірки збережено в Tools/Verification. prepare-runtime-audit.ps1 і run-hidden-runtime-audit.ps1 приймають BuildPath/OutputPath, беруть версію з фактичного build-info.json та використовують незмінну копію Player усередині OutputPath. Звіти записуються до OutputPath/Results. Історичні скрипти v1.7.6 не змінювалися.

Додатково перенесено лише початкові прогони Builds/Verification-v1.7.7/Graphics та Native. Фінальні Graphics-final, Update-final, Native-final і Windows-v1.7.7-final, усі релізи та Optimization-v1.7.7 залишилися на місці.
