"""
Model catalog. Every `id` is a real Ollama pull name (checked on ollama.com,
November 2026). `size_gb` is the download size; memory needed while running
is roughly size + ~1 GB for context.

Uncensored models are Dolphin fine-tunes (official library) and
"abliterated" builds by huihui_ai — the refusal behaviour removed from the
original weights.
"""

CATEGORIES = [
    ("all",         "Усі"),
    ("chat",        "Чат"),
    ("code",        "Код"),
    ("reasoning",   "Міркування"),
    ("translation", "Переклад"),
    ("small",       "Швидкі"),
    ("installed",   "Встановлені"),
]


def _m(id, name, category, size_gb, desc, uncensored=False, moe=False, tags=()):
    return {
        "id": id, "name": name, "category": category, "size_gb": size_gb,
        "description": desc, "uncensored": uncensored, "moe": moe, "tags": list(tags),
    }


CATALOG: list[dict] = [
    # ── ЧАТ ───────────────────────────────────────────────────────────────────
    _m("qwen3:8b", "Qwen 3 8B", "chat", 5.2,
       "Сильна мультимовна модель, добре знає українську. Вміє «подумати» перед відповіддю.",
       tags=("думає", "інструменти")),
    _m("qwen3.5:9b", "Qwen 3.5 9B", "chat", 7.6,
       "Нове покоління Qwen: розуміє картинки, думає, працює з інструментами.",
       tags=("думає", "зір", "інструменти")),
    _m("llama3.1:8b", "Llama 3.1 8B", "chat", 4.9,
       "Класика від Meta. Надійна для щоденних питань і текстів.", tags=("інструменти",)),
    _m("gemma4:12b", "Gemma 4 12B", "chat", 8.0,
       "Свіжа модель Google: міркування, код, картинки. Дуже якісні відповіді.",
       tags=("думає", "зір", "інструменти")),
    _m("gemma4:e4b", "Gemma 4 E4B", "chat", 9.5,
       "Компактна Gemma 4 з підтримкою зображень і аудіо.", tags=("зір", "аудіо")),
    _m("gemma3:12b", "Gemma 3 12B", "chat", 8.2,
       "Попереднє покоління Gemma — розумна й стабільна, розуміє картинки.", tags=("зір",)),
    _m("qwen3:14b", "Qwen 3 14B", "chat", 9.3,
       "Старша Qwen 3: помітно розумніша за 8B, але потребує більше пам'яті.",
       tags=("думає", "інструменти")),
    _m("gpt-oss:20b", "GPT-OSS 20B", "chat", 14.0,
       "Відкрита модель від OpenAI. MoE — працює швидше, ніж очікуєш від 20B.",
       moe=True, tags=("думає", "інструменти")),
    _m("mistral-small3.2:24b", "Mistral Small 3.2 24B", "chat", 15.0,
       "Європейська модель Mistral: чітко виконує інструкції, розуміє картинки.",
       tags=("зір", "інструменти")),
    _m("qwen3.6:35b", "Qwen 3.6 35B", "chat", 23.0,
       "Флагман Qwen для агентів і коду. MoE — активна лише мала частина параметрів.",
       moe=True, tags=("думає", "зір", "інструменти")),

    _m("dolphin3:8b", "Dolphin 3.0 8B", "chat", 4.9,
       "Llama 3.1 без цензури від Cognitive Computations. Слухняна і пряма.", uncensored=True),
    _m("huihui_ai/qwen3-abliterated:8b", "Qwen 3 8B Abliterated", "chat", 5.0,
       "Qwen 3 з вимкненими відмовами. Найкращий баланс розуму й розміру.",
       uncensored=True, tags=("думає",)),
    _m("huihui_ai/qwen3.5-abliterated:9b", "Qwen 3.5 9B Abliterated", "chat", 6.6,
       "Нова Qwen 3.5 без обмежень, з розумінням зображень.",
       uncensored=True, tags=("думає", "зір")),
    _m("huihui_ai/gemma-4-abliterated:12b", "Gemma 4 12B Abliterated", "chat", 7.6,
       "Gemma 4 без цензури — найпопулярніша abliterated-модель.",
       uncensored=True, tags=("думає", "зір")),
    _m("huihui_ai/qwen3-abliterated:14b", "Qwen 3 14B Abliterated", "chat", 9.0,
       "Старша Qwen 3 без обмежень.", uncensored=True, tags=("думає",)),
    _m("huihui_ai/gpt-oss-abliterated:20b", "GPT-OSS 20B Abliterated", "chat", 14.0,
       "Модель OpenAI без вбудованих відмов. MoE.", uncensored=True, moe=True, tags=("думає",)),
    _m("huihui_ai/Qwen3.6-abliterated:35b", "Qwen 3.6 35B Abliterated", "chat", 24.0,
       "Флагман Qwen 3.6 без цензури. MoE.", uncensored=True, moe=True, tags=("думає",)),
    _m("dolphin-llama3:8b", "Dolphin Llama 3 8B", "chat", 4.7,
       "Перевірена часом Dolphin на Llama 3.", uncensored=True),
    _m("dolphin-mistral:7b", "Dolphin Mistral 7B", "chat", 4.1,
       "Легка Dolphin на базі Mistral 7B.", uncensored=True),
    _m("llama2-uncensored:7b", "Llama 2 Uncensored 7B", "chat", 3.8,
       "Стара, але відома модель без цензури.", uncensored=True),

    # ── КОД ───────────────────────────────────────────────────────────────────
    _m("qwen2.5-coder:7b", "Qwen 2.5 Coder 7B", "code", 4.7,
       "Найкращий кодер свого розміру. Python, JS, C#, SQL і ще 90 мов.", tags=("інструменти",)),
    _m("qwen2.5-coder:14b", "Qwen 2.5 Coder 14B", "code", 9.0,
       "Старший кодер: краще тримає великий контекст проєкту.", tags=("інструменти",)),
    _m("qwen3-coder:30b", "Qwen 3 Coder 30B", "code", 19.0,
       "Агентний кодер для великих задач. MoE — на 8 ГБ VRAM + RAM іде нормально.",
       moe=True, tags=("інструменти",)),
    _m("deepseek-coder-v2:16b", "DeepSeek Coder V2 16B", "code", 8.9,
       "MoE-кодер від DeepSeek: швидкий для свого розміру.", moe=True),
    _m("devstral-small-2:24b", "Devstral Small 2 24B", "code", 15.0,
       "Агент для програмування від Mistral: сам читає й править файли.",
       tags=("зір", "інструменти")),

    _m("huihui_ai/qwen2.5-coder-abliterate:7b", "Qwen 2.5 Coder 7B Abliterated", "code", 4.7,
       "Кодер без відмов — напише будь-який скрипт.", uncensored=True),
    _m("huihui_ai/qwen2.5-coder-abliterate:14b", "Qwen 2.5 Coder 14B Abliterated", "code", 9.0,
       "Старший кодер без цензури.", uncensored=True),
    _m("huihui_ai/qwen3-coder-abliterated:30b", "Qwen 3 Coder 30B Abliterated", "code", 19.0,
       "Найсильніший локальний кодер без обмежень. MoE.", uncensored=True, moe=True),
    _m("dolphincoder:7b", "DolphinCoder 7B", "code", 4.2,
       "Кодер без цензури на основі StarCoder2.", uncensored=True),

    # ── МІРКУВАННЯ ────────────────────────────────────────────────────────────
    _m("deepseek-r1:8b", "DeepSeek R1 8B", "reasoning", 5.2,
       "Думає крок за кроком — для математики, логіки, задач.", tags=("думає", "інструменти")),
    _m("deepseek-r1:14b", "DeepSeek R1 14B", "reasoning", 9.0,
       "Старша R1: глибші міркування.", tags=("думає", "інструменти")),
    _m("qwen3:30b", "Qwen 3 30B", "reasoning", 19.0,
       "Потужна модель з режимом мислення. MoE — швидка для свого розміру.",
       moe=True, tags=("думає", "інструменти")),
    _m("phi4-reasoning:14b", "Phi-4 Reasoning 14B", "reasoning", 11.0,
       "Microsoft: сильна в математиці й науці.", tags=("думає",)),
    _m("magistral:24b", "Magistral 24B", "reasoning", 14.0,
       "Мислячa модель Mistral, добре пояснює хід думок.", tags=("думає", "інструменти")),
    _m("qwq:32b", "QwQ 32B", "reasoning", 20.0,
       "Важкий «мислитель» від Qwen. Повільно, але дуже ретельно.", tags=("думає",)),

    _m("huihui_ai/deepseek-r1-abliterated:8b", "DeepSeek R1 8B Abliterated", "reasoning", 5.0,
       "R1 без цензури: міркує на будь-яку тему.", uncensored=True, tags=("думає",)),
    _m("huihui_ai/deepseek-r1-abliterated:14b", "DeepSeek R1 14B Abliterated", "reasoning", 9.0,
       "Старша R1 без цензури.", uncensored=True, tags=("думає",)),
    _m("huihui_ai/qwen3-abliterated:30b", "Qwen 3 30B Abliterated", "reasoning", 19.0,
       "Qwen 3 30B з мисленням і без відмов. MoE.", uncensored=True, moe=True, tags=("думає",)),
    _m("huihui_ai/qwq-abliterated:32b", "QwQ 32B Abliterated", "reasoning", 20.0,
       "QwQ без цензури.", uncensored=True, tags=("думає",)),

    # ── ПЕРЕКЛАД ──────────────────────────────────────────────────────────────
    _m("translategemma:4b", "TranslateGemma 4B", "translation", 3.3,
       "Google, спеціально для перекладу: 55 мов, включно з українською."),
    _m("translategemma:12b", "TranslateGemma 12B", "translation", 8.1,
       "Старша TranslateGemma — точніший переклад складних текстів."),
    _m("aya-expanse:8b", "Aya Expanse 8B", "translation", 5.1,
       "Cohere: 23 мови, природний стиль.", tags=("інструменти",)),
    _m("qwen2.5:7b", "Qwen 2.5 7B", "translation", 4.7,
       "Універсальна мультимовна модель, добре перекладає.", tags=("інструменти",)),

    _m("huihui_ai/hy-mt1.5-abliterated:7b", "Hunyuan MT 1.5 7B Abliterated", "translation", 4.6,
       "Модель перекладу Tencent без цензури — перекладе будь-який текст.", uncensored=True),
    _m("huihui_ai/aya-expanse-abliterated:8b", "Aya Expanse 8B Abliterated", "translation", 5.1,
       "Aya без відмов, 23 мови.", uncensored=True),
    _m("huihui_ai/qwen2.5-abliterate:7b", "Qwen 2.5 7B Abliterated", "translation", 4.7,
       "Мультимовна Qwen 2.5 без цензури.", uncensored=True),

    # ── ШВИДКІ ────────────────────────────────────────────────────────────────
    _m("qwen3:4b", "Qwen 3 4B", "small", 2.5,
       "Маленька, але дуже розумна. Летить навіть на слабкому ПК.", tags=("думає", "інструменти")),
    _m("qwen3:1.7b", "Qwen 3 1.7B", "small", 1.4, "Миттєві відповіді на прості питання.",
       tags=("думає",)),
    _m("llama3.2:3b", "Llama 3.2 3B", "small", 2.0, "Мала Llama — швидка і проста.",
       tags=("інструменти",)),
    _m("phi4-mini:3.8b", "Phi-4 Mini 3.8B", "small", 2.5, "Microsoft: мала модель з логікою.",
       tags=("інструменти",)),
    _m("gemma3:1b", "Gemma 3 1B", "small", 0.8, "Крихітна Gemma — працює навіть без відеокарти."),
    _m("qwen3.5:2b", "Qwen 3.5 2B", "small", 3.1, "Мала Qwen 3.5 з розумінням картинок.",
       tags=("зір",)),

    _m("huihui_ai/qwen3-abliterated:4b", "Qwen 3 4B Abliterated", "small", 2.5,
       "Швидка мала модель без цензури.", uncensored=True, tags=("думає",)),
    _m("huihui_ai/qwen3.5-abliterated:4B", "Qwen 3.5 4B Abliterated", "small", 3.3,
       "Мала Qwen 3.5 без цензури.", uncensored=True),
    _m("huihui_ai/llama3.2-abliterate:3b", "Llama 3.2 3B Abliterated", "small", 2.0,
       "Мала Llama без відмов.", uncensored=True),
    _m("huihui_ai/qwen3-abliterated:1.7b", "Qwen 3 1.7B Abliterated", "small", 1.1,
       "Найменша без цензури.", uncensored=True),
    _m("dolphin-phi:2.7b", "Dolphin Phi 2.7B", "small", 1.6,
       "Крихітна Dolphin.", uncensored=True),
]


# ── fit to hardware ───────────────────────────────────────────────────────────

FIT_FAST, FIT_SLOW, FIT_NO = "fast", "slow", "no"


def fit(model: dict, vram_gb: float, ram_gb: float) -> str:
    """How well a model runs on this PC.

    fast — fits entirely in video memory: full speed (recommended)
    slow — needs video memory + RAM together: works, but slower
    no   — not enough memory at all (can still be installed)
    """
    need = model["size_gb"] + 1.0
    if vram_gb >= 2 and need <= vram_gb:
        return FIT_FAST
    if vram_gb < 2 and model["size_gb"] <= 2.6:
        return FIT_FAST          # no usable GPU: tiny models are still quick on CPU
    if need <= vram_gb + ram_gb * 0.6:  # leave ~40% of RAM for Windows and apps
        return FIT_SLOW
    return FIT_NO


def get_recommended(vram_gb: float, ram_gb: float) -> set[str]:
    return {m["id"] for m in CATALOG if fit(m, vram_gb, ram_gb) == FIT_FAST}


def get_by_category(category: str) -> list[dict]:
    if category == "all":
        return list(CATALOG)
    return [m for m in CATALOG if m["category"] == category]


def find(model_id: str) -> dict | None:
    for m in CATALOG:
        if m["id"].lower() == model_id.lower():
            return m
    return None
