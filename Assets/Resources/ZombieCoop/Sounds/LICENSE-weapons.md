# Звуки оружия

Выстрел дробовика (`shot_shotgun.wav`) — **«Shotgun - Fire.wav»** от AbuNijmeh,
лицензия **CC0 1.0**: https://freesound.org/people/AbuNijmeh/sounds/318973/
(переведён в моно, срезан гул ниже 30 Гц, нормализован).

Выстрелы пистолета и автомата, сухие щелчки пистолета и автомата и перезарядка
автомата — тоже с freesound.org, **CC0 1.0** (их делает `tools/make_freesound_sounds.py`):

| Файл | Запись |
|---|---|
| shot_pistol_1…4 | «9mm Pistol Gunshots», DledeSOUND — https://freesound.org/s/731942/ (четыре выстрела одного ствола) |
| shot_smg_1…4 | «An AK-47 being shot // has distinctive metallic kalashnikov-esque report // punchy sounding», serøutōnin--deprivəd — https://freesound.org/s/855842/ (одиночный выстрел, хвост обрезан до 0.7 с, 4 варианта с чуть разной скоростью; файл взят из github.com/birkmann/zugriff, assets/sounds/shot_ak_1.ogg) |
| click_pistol | «9mm Handgun Being Dry Fired», serøutōnin--deprivəd — https://freesound.org/s/674568/ |
| click_smg | «A rifle being dry fired once», serøutōnin--deprivəd — https://freesound.org/s/725402/ |
| reload_smg | «Uzi submachine gun being reloaded while empty. Full sequence», serøutōnin--deprivəd — https://freesound.org/s/677160/ |

Кроме пистолета и автомата, это куски из готовых нарезок открытого проекта
https://github.com/webdevcody/survive-the-night-fps (client/audio/samples, все CC0,
источники перечислены в его CREDITS.md): сам freesound из облака отвечал 403.

Перезарядка пистолета и всё для дробовика, кроме выстрела, нарезаны из **Free Firearm Sound Library**
(Бен Ящак / Still North Media, Kickstarter 2014), лицензия
**CC0 1.0 Universal** (общественное достояние): можно использовать,
менять и распространять в любых, в том числе коммерческих, проектах
без указания автора и без разрешения.

Источник: https://github.com/buddingmonkey/FreeFirearmsSFXLibrary
(зеркало полной библиотеки, файл LICENSE в репозитории — текст CC0 1.0).
Текст лицензии: https://creativecommons.org/publicdomain/zero/1.0/

Как получены файлы: `tools/make_weapon_sounds.py` (нарезка, моно 44.1 кГц,
приглушённый верх, эхо короткой комнаты, выравнивание громкости).

| Файл | Оружие в записи | Что это |
|---|---|---|
| shot_shotgun | дробовик | «Shotgun - Fire.wav», AbuNijmeh, freesound.org (CC0) |
| reload_pistol | Colt 1911 | вынуть магазин, вставить полный, отпустить затвор (A_5, A_4, A_9) |
| reload_shotgun | Benelli Nova | три патрона в магазин и помпа (O_3, O_6) |
| click_shotgun | Benelli Nova | сухой щелчок спуска (O_8) |
