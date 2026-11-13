"""
Static catalog of recommended models per category.
vram_gb = minimum VRAM to run comfortably on GPU.
ram_gb  = minimum RAM for CPU-only fallback.
"""

CATALOG: list[dict] = [
    # ── CHAT ──────────────────────────────────────────────────────────────────
    {
        "id": "llama3.2:3b",
        "name": "Llama 3.2 3B",
        "category": "chat",
        "censored": True,
        "params_b": 3,
        "vram_gb": 3,
        "ram_gb": 6,
        "size_gb": 2.0,
        "description": "Швидка розмовна модель від Meta. Добре для щоденних питань.",
    },
    {
        "id": "llama3.1:8b",
        "name": "Llama 3.1 8B",
        "category": "chat",
        "censored": True,
        "params_b": 8,
        "vram_gb": 6,
        "ram_gb": 10,
        "size_gb": 4.7,
        "description": "Збалансована модель Meta. Гарний вибір для більшості задач.",
    },
    {
        "id": "qwen2.5:14b",
        "name": "Qwen 2.5 14B",
        "category": "chat",
        "censored": True,
        "params_b": 14,
        "vram_gb": 10,
        "ram_gb": 18,
        "size_gb": 8.5,
        "description": "Сильна мультимовна модель Alibaba. Добре розуміє українську.",
    },
    {
        "id": "mistral:7b",
        "name": "Mistral 7B",
        "category": "chat",
        "censored": True,
        "params_b": 7,
        "vram_gb": 5,
        "ram_gb": 8,
        "size_gb": 4.1,
        "description": "Швидка і розумна французька модель.",
    },
    {
        "id": "dolphin-llama3:8b",
        "name": "Dolphin Llama 3 8B",
        "category": "chat",
        "censored": False,
        "params_b": 8,
        "vram_gb": 6,
        "ram_gb": 10,
        "size_gb": 4.7,
        "description": "Llama 3 без обмежень. Відповідає на будь-які питання.",
    },
    {
        "id": "dolphin-mistral:7b",
        "name": "Dolphin Mistral 7B",
        "category": "chat",
        "censored": False,
        "params_b": 7,
        "vram_gb": 5,
        "ram_gb": 8,
        "size_gb": 4.1,
        "description": "Mistral без цензури. Популярна безцензурна модель.",
    },
    {
        "id": "dolphin-qwen2.5:14b",
        "name": "Dolphin Qwen 2.5 14B",
        "category": "chat",
        "censored": False,
        "params_b": 14,
        "vram_gb": 10,
        "ram_gb": 18,
        "size_gb": 8.5,
        "description": "Qwen 2.5 без обмежень. Мультимовна і потужна.",
    },

    # ── CODE ──────────────────────────────────────────────────────────────────
    {
        "id": "qwen2.5-coder:7b",
        "name": "Qwen 2.5 Coder 7B",
        "category": "code",
        "censored": True,
        "params_b": 7,
        "vram_gb": 5,
        "ram_gb": 8,
        "size_gb": 4.3,
        "description": "Спеціалізована кодова модель Alibaba. Відмінно знає Python, JS, C++.",
    },
    {
        "id": "qwen2.5-coder:14b",
        "name": "Qwen 2.5 Coder 14B",
        "category": "code",
        "censored": True,
        "params_b": 14,
        "vram_gb": 10,
        "ram_gb": 18,
        "size_gb": 8.5,
        "description": "Велика версія Qwen Coder. Краще розуміє складні проєкти.",
    },
    {
        "id": "deepseek-coder-v2:16b",
        "name": "DeepSeek Coder V2 16B",
        "category": "code",
        "censored": True,
        "params_b": 16,
        "vram_gb": 12,
        "ram_gb": 20,
        "size_gb": 9.1,
        "description": "Топова відкрита модель для коду від DeepSeek.",
    },
    {
        "id": "dolphin-qwen2.5-coder:7b",
        "name": "Dolphin Qwen 2.5 Coder 7B",
        "category": "code",
        "censored": False,
        "params_b": 7,
        "vram_gb": 5,
        "ram_gb": 8,
        "size_gb": 4.3,
        "description": "Qwen Coder без обмежень. Пише будь-який код.",
    },
    {
        "id": "dolphin-deepseek-coder:6.7b",
        "name": "Dolphin DeepSeek Coder 6.7B",
        "category": "code",
        "censored": False,
        "params_b": 6.7,
        "vram_gb": 5,
        "ram_gb": 8,
        "size_gb": 3.8,
        "description": "DeepSeek Coder без цензури.",
    },

    # ── REASONING ─────────────────────────────────────────────────────────────
    {
        "id": "qwq:32b",
        "name": "QwQ 32B",
        "category": "reasoning",
        "censored": True,
        "params_b": 32,
        "vram_gb": 20,
        "ram_gb": 40,
        "size_gb": 19.0,
        "description": "Модель Alibaba, що «думає вголос». Розв'язує складні задачі.",
    },
    {
        "id": "deepseek-r1:14b",
        "name": "DeepSeek R1 14B",
        "category": "reasoning",
        "censored": True,
        "params_b": 14,
        "vram_gb": 10,
        "ram_gb": 18,
        "size_gb": 8.5,
        "description": "Модель з ланцюжком думок від DeepSeek.",
    },
    {
        "id": "deepseek-r1:8b",
        "name": "DeepSeek R1 8B",
        "category": "reasoning",
        "censored": True,
        "params_b": 8,
        "vram_gb": 6,
        "ram_gb": 10,
        "size_gb": 4.7,
        "description": "Компактна версія R1 для слабших машин.",
    },
    {
        "id": "dolphin-deepseek-r1:14b",
        "name": "Dolphin DeepSeek R1 14B",
        "category": "reasoning",
        "censored": False,
        "params_b": 14,
        "vram_gb": 10,
        "ram_gb": 18,
        "size_gb": 8.5,
        "description": "DeepSeek R1 без обмежень. Аналізує без цензури.",
    },

    # ── TRANSLATION / LANGUAGE ────────────────────────────────────────────────
    {
        "id": "qwen2.5:7b",
        "name": "Qwen 2.5 7B",
        "category": "translation",
        "censored": True,
        "params_b": 7,
        "vram_gb": 5,
        "ram_gb": 8,
        "size_gb": 4.3,
        "description": "Мультимовна модель. Добре перекладає між 20+ мовами.",
    },
    {
        "id": "aya:8b",
        "name": "Aya 8B",
        "category": "translation",
        "censored": True,
        "params_b": 8,
        "vram_gb": 6,
        "ram_gb": 10,
        "size_gb": 4.8,
        "description": "Спеціалізована мультимовна модель Cohere. 101 мова.",
    },
    {
        "id": "dolphin-qwen2.5:7b",
        "name": "Dolphin Qwen 2.5 7B",
        "category": "translation",
        "censored": False,
        "params_b": 7,
        "vram_gb": 5,
        "ram_gb": 8,
        "size_gb": 4.3,
        "description": "Мультимовна без цензури.",
    },

    # ── SMALL / FAST ──────────────────────────────────────────────────────────
    {
        "id": "tinyllama:1.1b",
        "name": "TinyLlama 1.1B",
        "category": "small",
        "censored": True,
        "params_b": 1.1,
        "vram_gb": 1,
        "ram_gb": 2,
        "size_gb": 0.6,
        "description": "Мінімальна модель. Запускається на будь-якому ПК.",
    },
    {
        "id": "qwen2.5:1.5b",
        "name": "Qwen 2.5 1.5B",
        "category": "small",
        "censored": True,
        "params_b": 1.5,
        "vram_gb": 1,
        "ram_gb": 3,
        "size_gb": 1.0,
        "description": "Маленька, але розумна. Добре для простих питань.",
    },
    {
        "id": "phi3:3.8b",
        "name": "Phi-3 Mini 3.8B",
        "category": "small",
        "censored": True,
        "params_b": 3.8,
        "vram_gb": 3,
        "ram_gb": 5,
        "size_gb": 2.2,
        "description": "Компактна модель Microsoft. Відмінна якість для свого розміру.",
    },
    {
        "id": "dolphin-phi:2.7b",
        "name": "Dolphin Phi 2.7B",
        "category": "small",
        "censored": False,
        "params_b": 2.7,
        "vram_gb": 2,
        "ram_gb": 4,
        "size_gb": 1.6,
        "description": "Маленька модель без обмежень.",
    },
]

CATEGORIES = [
    ("chat",        "💬 Чат"),
    ("code",        "💻 Код"),
    ("reasoning",   "🧠 Міркування"),
    ("translation", "🌐 Переклад"),
    ("small",       "⚡ Швидкі"),
]


def get_recommended(vram_gb: float, ram_gb: float) -> set[str]:
    """Return set of model IDs that fit the hardware."""
    result = set()
    for m in CATALOG:
        fits_gpu = vram_gb >= m["vram_gb"]
        fits_cpu = ram_gb >= m["ram_gb"]
        if fits_gpu or fits_cpu:
            result.add(m["id"])
    return result


def get_by_category(category: str, censored: bool | None = None) -> list[dict]:
    """Filter catalog by category and optionally by censored flag."""
    out = [m for m in CATALOG if m["category"] == category]
    if censored is not None:
        out = [m for m in out if m["censored"] == censored]
    return out
