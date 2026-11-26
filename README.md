<div align="center">

<img src="https://raw.githubusercontent.com/Dimonchikk/Open-Forge/main/assets/icon.png" width="120" alt="Open Forge">

# Open Forge

**Нейромережі прямо на твоєму ПК.**<br>
Чат, агент, що сам пише й тестує проєкти, порівняння моделей і каталог з 134 моделей —<br>
у звичайному Windows-застосунку поверх [Ollama](https://ollama.com). Без хмари, без підписок, без інтернету після завантаження моделі.

[![Build](https://img.shields.io/github/actions/workflow/status/Dimonchikk/Open-Forge/build.yml?label=Build&logo=github)](https://github.com/Dimonchikk/Open-Forge/actions)
[![Release](https://img.shields.io/github/v/release/Dimonchikk/Open-Forge?label=Release&color=7c3aed)](https://github.com/Dimonchikk/Open-Forge/releases/latest)
[![License](https://img.shields.io/badge/License-MIT-3fb950)](LICENSE)
[![Windows](https://img.shields.io/badge/Windows-10%2F11-0078d4?logo=windows&logoColor=white)](https://github.com/Dimonchikk/Open-Forge/releases/latest)
[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4-512bd4?logo=dotnet&logoColor=white)](#як-зібрати)
[![Ollama](https://img.shields.io/badge/Ollama-0.4%2B-111111?logo=ollama&logoColor=white)](https://ollama.com)
[![Models](https://img.shields.io/badge/Моделей-134-f97316)](#можливості)

### [⬇️ Завантажити останню версію](https://github.com/Dimonchikk/Open-Forge/releases/latest) · [Можливості](#можливості) · [Як зібрати](#як-зібрати) · [Сайт](https://dimonchikk.github.io/Open-Forge/) · [Що нового](CHANGES.md)

<hr>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/Dimonchikk/Open-Forge/main/docs/img/agent_dark.webp">
  <img src="https://raw.githubusercontent.com/Dimonchikk/Open-Forge/main/docs/img/agent_light.webp" width="860" alt="Open Forge — агент з панеллю «Прогрес»">
</picture>

<sub>Агент сам створює сайт, фотографує результат і показує кроки, файли й модель у бічній панелі</sub>

</div>

## Як це виглядає

<table>
<tr>
<td width="50%" valign="top">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/Dimonchikk/Open-Forge/main/docs/img/chat_dark.webp">
  <img src="https://raw.githubusercontent.com/Dimonchikk/Open-Forge/main/docs/img/chat_light.webp" width="420" alt="Чат">
</picture>

**Чат**<br>
<sub>Думки моделі, код з кнопкою «Копіювати», швидкість у токенах і озвучка відповіді</sub>

</td>
<td width="50%" valign="top">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/Dimonchikk/Open-Forge/main/docs/img/models_dark.webp">
  <img src="https://raw.githubusercontent.com/Dimonchikk/Open-Forge/main/docs/img/models_light.webp" width="420" alt="Каталог моделей">
</picture>

**Каталог моделей**<br>
<sub>134 моделі: сила 1–10, позначка, чи потягне твій ПК, фільтри й розділ Hugging Face</sub>

</td>
</tr>
<tr>
<td width="50%" valign="top">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/Dimonchikk/Open-Forge/main/docs/img/compare_dark.webp">
  <img src="https://raw.githubusercontent.com/Dimonchikk/Open-Forge/main/docs/img/compare_light.webp" width="420" alt="Порівняння">
</picture>

**Порівняння**<br>
<sub>Одне питання — до трьох моделей поруч, зі швидкістю кожної</sub>

</td>
<td width="50%" valign="top">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/Dimonchikk/Open-Forge/main/docs/img/settings_dark.webp">
  <img src="https://raw.githubusercontent.com/Dimonchikk/Open-Forge/main/docs/img/settings_light.webp" width="420" alt="Налаштування">
</picture>

**Налаштування**<br>
<sub>Інструкція для моделі, пам'ять розмови, твій ПК і оновлення</sub>

</td>
</tr>
</table>

<sub>Знімки самі перемикаються між світлою й темною темою разом з темою GitHub.</sub>

## Можливості

### 💬 Розмова
- Чат зі стрімінгом, таблицями, кодом і «Думками моделі» — перемикач «Думати» для моделей, що вміють міркувати
- Повідомлення можна редагувати, видаляти й перегенерувати, відповіді — слухати вголос
- Фото, скріншоти й файли: PDF, Word, текст і код (кнопка, Ctrl+V або перетягування)
- Історія чатів з пошуком; чернетка й папка агента — окремі для кожного чату

### 🤖 Агент
- Працює в обраній папці: створює файли, запускає команди й програми
- Сам натискає кнопки у вікнах програм і фотографує кожен крок — тестує, як людина
- Не здає «зразок»: помічає заглушки замість коду й перевіряє, що проєкт запускається
- Бічна панель «Прогрес» як у Claude: кроки, створені файли, скріншоти, модель і пам'ять розмови
- Три рівні дозволів: «Питати завжди», «Авто» (файли сам, команди й видалення — з твого дозволу) і «Повний доступ»

### 📦 Моделі
- Каталог із 134 моделей: чат, код, зображення, міркування, переклад, швидкі — звичайні й без цензури
- Сила 1–10, потрібна пам'ять і позначка, чи потягне твій ПК
- Завантаження й видалення в один клік; розділ «Hugging Face» для моделей, яких немає в Ollama
- Порівняння до трьох моделей поруч

### 🖥 ПК і пристрої
- Кнопка Ollama: запустити, перезапустити або зупинити прямо з програми
- Навантаження процесора, пам'яті й відеокарти в боковому меню
- Пристрої: Android, iPhone, плати ESP32, Arduino, Raspberry Pi Pico, STM32 — з режимом і COM-портом
- Світла й темна тема, як у Windows

## Швидкий старт

1. Встанови [Ollama](https://ollama.com/download).
2. Завантаж `OpenForge-setup.exe` з [останнього релізу](https://github.com/Dimonchikk/Open-Forge/releases/latest) і запусти — права адміністратора не потрібні.
3. У розділі «Моделі» обери модель з зеленою позначкою (вона швидко працюватиме на твоєму ПК) і натисни «Завантажити».
4. Пиши в чат. Щоб модель сама робила проєкт, натисни кнопку з папкою біля поля вводу й обери папку.

**Вимоги:** Windows 10 або 11, Ollama. Для моделей 7–9B вистачить 16 ГБ оперативної пам'яті; відеокарта з 8 ГБ і більше робить їх у рази швидшими.

## Як зібрати

Код — C# 5 / .NET Framework 4 / WinForms, без сторонніх бібліотек.

- Локально на Windows: подвійний клік по `src/build.bat` — з'являться `src/OpenForge.exe` і `src/OpenForge-setup.exe`.
- На GitHub: Actions → «Збирання» → «Run workflow». З номером версії створюється чернетка релізу з `CHANGES.md`.

Інсталятор — це та сама програма під назвою з `setup`: ставить себе в `%LOCALAPPDATA%\Programs\OpenForge` без прав адміністратора.

Остання версія на Python — у гілці `python-legacy`.

## Ліцензія

[MIT](LICENSE)
