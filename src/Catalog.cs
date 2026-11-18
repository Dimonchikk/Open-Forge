// Open Forge — каталог моделей. Кожен Id — справжня назва для «ollama pull» (перевірено на ollama.com, листопад 2026).
// SizeGb — розмір завантаження; під час роботи модель займає приблизно SizeGb + 1 ГБ (контекст).
// Моделі без цензури — Dolphin (офіційна бібліотека) і «abliterated» від huihui_ai (з ваг прибрано відмови).
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenForge
{
    enum Fit { Fast, Slow, No }

    class CatalogModel
    {
        public string Id, Name, Category, Description;
        public float SizeGb;
        public bool Uncensored, Moe;
        public string[] Tags;
        public bool Installed;            // заповнюється зі списку Ollama
        public bool Known = true;         // false — модель встановлена, але її немає в каталозі

        public bool Vision { get { return Tags.Contains("зір"); } }
        public bool Thinks { get { return Tags.Contains("думає"); } }

        // Як модель піде на цьому ПК:
        // Fast — повністю влазить у відеопам'ять (повна швидкість);
        // Slow — потрібні відеопам'ять і RAM разом (працює, але повільніше);
        // No — пам'яті замало (встановити можна, але працюватиме дуже повільно або не запуститься).
        public Fit FitFor(double vramGb, double ramGb)
        {
            double need = SizeGb + 1.0;
            if (vramGb >= 2 && need <= vramGb) return Fit.Fast;
            if (vramGb < 2 && SizeGb <= 2.6) return Fit.Fast;          // без відеокарти маленькі моделі все одно швидкі
            if (ramGb <= 0 || need <= vramGb + ramGb * 0.6) return Fit.Slow;   // ~40 % RAM лишаємо Windows і програмам
            return Fit.No;
        }
    }

    static class Catalog
    {
        static CatalogModel M(string id, string name, string cat, float size, string desc, bool uncensored, bool moe, string tags)
        {
            return new CatalogModel
            {
                Id = id, Name = name, Category = cat, SizeGb = size, Description = desc, Uncensored = uncensored, Moe = moe,
                Tags = tags.Length == 0 ? new string[0] : tags.Split(','),
            };
        }

        public static readonly List<CatalogModel> All = new List<CatalogModel>
        {
            M("qwen3:8b", "Qwen 3 8B", "chat", 5.2f, "Сильна мультимовна модель, добре знає українську. Вміє «подумати» перед відповіддю.", false, false, "думає,інструменти"),
            M("qwen3.5:9b", "Qwen 3.5 9B", "chat", 7.6f, "Нове покоління Qwen: розуміє картинки, думає, працює з інструментами.", false, false, "думає,зір,інструменти"),
            M("llama3.1:8b", "Llama 3.1 8B", "chat", 4.9f, "Класика від Meta. Надійна для щоденних питань і текстів.", false, false, "інструменти"),
            M("gemma4:12b", "Gemma 4 12B", "chat", 8.0f, "Свіжа модель Google: міркування, код, картинки. Дуже якісні відповіді.", false, false, "думає,зір,інструменти"),
            M("gemma4:e4b", "Gemma 4 E4B", "chat", 9.5f, "Компактна Gemma 4 з підтримкою зображень і аудіо.", false, false, "зір,аудіо"),
            M("gemma3:12b", "Gemma 3 12B", "chat", 8.2f, "Попереднє покоління Gemma — розумна й стабільна, розуміє картинки.", false, false, "зір"),
            M("qwen3:14b", "Qwen 3 14B", "chat", 9.3f, "Старша Qwen 3: помітно розумніша за 8B, але потребує більше пам'яті.", false, false, "думає,інструменти"),
            M("gpt-oss:20b", "GPT-OSS 20B", "chat", 14.0f, "Відкрита модель від OpenAI. MoE — працює швидше, ніж очікуєш від 20B.", false, true, "думає,інструменти"),
            M("mistral-small3.2:24b", "Mistral Small 3.2 24B", "chat", 15.0f, "Європейська модель Mistral: чітко виконує інструкції, розуміє картинки.", false, false, "зір,інструменти"),
            M("qwen3.6:35b", "Qwen 3.6 35B", "chat", 23.0f, "Флагман Qwen для агентів і коду. MoE — активна лише мала частина параметрів.", false, true, "думає,зір,інструменти"),
            M("dolphin3:8b", "Dolphin 3.0 8B", "chat", 4.9f, "Llama 3.1 без цензури від Cognitive Computations. Слухняна і пряма.", true, false, ""),
            M("huihui_ai/qwen3-abliterated:8b", "Qwen 3 8B Abliterated", "chat", 5.0f, "Qwen 3 з вимкненими відмовами. Найкращий баланс розуму й розміру.", true, false, "думає"),
            M("huihui_ai/qwen3.5-abliterated:9b", "Qwen 3.5 9B Abliterated", "chat", 6.6f, "Нова Qwen 3.5 без обмежень, з розумінням зображень.", true, false, "думає,зір"),
            M("huihui_ai/gemma-4-abliterated:12b", "Gemma 4 12B Abliterated", "chat", 7.6f, "Gemma 4 без цензури — найпопулярніша abliterated-модель.", true, false, "думає,зір"),
            M("huihui_ai/qwen3-abliterated:14b", "Qwen 3 14B Abliterated", "chat", 9.0f, "Старша Qwen 3 без обмежень.", true, false, "думає"),
            M("huihui_ai/gpt-oss-abliterated:20b", "GPT-OSS 20B Abliterated", "chat", 14.0f, "Модель OpenAI без вбудованих відмов. MoE.", true, true, "думає"),
            M("huihui_ai/Qwen3.6-abliterated:35b", "Qwen 3.6 35B Abliterated", "chat", 24.0f, "Флагман Qwen 3.6 без цензури. MoE.", true, true, "думає"),
            M("dolphin-llama3:8b", "Dolphin Llama 3 8B", "chat", 4.7f, "Перевірена часом Dolphin на Llama 3.", true, false, ""),
            M("dolphin-mistral:7b", "Dolphin Mistral 7B", "chat", 4.1f, "Легка Dolphin на базі Mistral 7B.", true, false, ""),
            M("llama2-uncensored:7b", "Llama 2 Uncensored 7B", "chat", 3.8f, "Стара, але відома модель без цензури.", true, false, ""),
            M("qwen2.5-coder:7b", "Qwen 2.5 Coder 7B", "code", 4.7f, "Найкращий кодер свого розміру. Python, JS, C#, SQL і ще 90 мов.", false, false, "інструменти"),
            M("qwen2.5-coder:14b", "Qwen 2.5 Coder 14B", "code", 9.0f, "Старший кодер: краще тримає великий контекст проєкту.", false, false, "інструменти"),
            M("qwen3-coder:30b", "Qwen 3 Coder 30B", "code", 19.0f, "Агентний кодер для великих задач. MoE — на 8 ГБ VRAM + RAM іде нормально.", false, true, "інструменти"),
            M("deepseek-coder-v2:16b", "DeepSeek Coder V2 16B", "code", 8.9f, "MoE-кодер від DeepSeek: швидкий для свого розміру.", false, true, ""),
            M("devstral-small-2:24b", "Devstral Small 2 24B", "code", 15.0f, "Агент для програмування від Mistral: сам читає й править файли.", false, false, "зір,інструменти"),
            M("huihui_ai/qwen2.5-coder-abliterate:7b", "Qwen 2.5 Coder 7B Abliterated", "code", 4.7f, "Кодер без відмов — напише будь-який скрипт.", true, false, ""),
            M("huihui_ai/qwen2.5-coder-abliterate:14b", "Qwen 2.5 Coder 14B Abliterated", "code", 9.0f, "Старший кодер без цензури.", true, false, ""),
            M("huihui_ai/qwen3-coder-abliterated:30b", "Qwen 3 Coder 30B Abliterated", "code", 19.0f, "Найсильніший локальний кодер без обмежень. MoE.", true, true, ""),
            M("dolphincoder:7b", "DolphinCoder 7B", "code", 4.2f, "Кодер без цензури на основі StarCoder2.", true, false, ""),
            M("deepseek-r1:8b", "DeepSeek R1 8B", "reasoning", 5.2f, "Думає крок за кроком — для математики, логіки, задач.", false, false, "думає,інструменти"),
            M("deepseek-r1:14b", "DeepSeek R1 14B", "reasoning", 9.0f, "Старша R1: глибші міркування.", false, false, "думає,інструменти"),
            M("qwen3:30b", "Qwen 3 30B", "reasoning", 19.0f, "Потужна модель з режимом мислення. MoE — швидка для свого розміру.", false, true, "думає,інструменти"),
            M("phi4-reasoning:14b", "Phi-4 Reasoning 14B", "reasoning", 11.0f, "Microsoft: сильна в математиці й науці.", false, false, "думає"),
            M("magistral:24b", "Magistral 24B", "reasoning", 14.0f, "Мислячa модель Mistral, добре пояснює хід думок.", false, false, "думає,інструменти"),
            M("qwq:32b", "QwQ 32B", "reasoning", 20.0f, "Важкий «мислитель» від Qwen. Повільно, але дуже ретельно.", false, false, "думає"),
            M("huihui_ai/deepseek-r1-abliterated:8b", "DeepSeek R1 8B Abliterated", "reasoning", 5.0f, "R1 без цензури: міркує на будь-яку тему.", true, false, "думає"),
            M("huihui_ai/deepseek-r1-abliterated:14b", "DeepSeek R1 14B Abliterated", "reasoning", 9.0f, "Старша R1 без цензури.", true, false, "думає"),
            M("huihui_ai/qwen3-abliterated:30b", "Qwen 3 30B Abliterated", "reasoning", 19.0f, "Qwen 3 30B з мисленням і без відмов. MoE.", true, true, "думає"),
            M("huihui_ai/qwq-abliterated:32b", "QwQ 32B Abliterated", "reasoning", 20.0f, "QwQ без цензури.", true, false, "думає"),
            M("translategemma:4b", "TranslateGemma 4B", "translation", 3.3f, "Google, спеціально для перекладу: 55 мов, включно з українською.", false, false, ""),
            M("translategemma:12b", "TranslateGemma 12B", "translation", 8.1f, "Старша TranslateGemma — точніший переклад складних текстів.", false, false, ""),
            M("aya-expanse:8b", "Aya Expanse 8B", "translation", 5.1f, "Cohere: 23 мови, природний стиль.", false, false, "інструменти"),
            M("qwen2.5:7b", "Qwen 2.5 7B", "translation", 4.7f, "Універсальна мультимовна модель, добре перекладає.", false, false, "інструменти"),
            M("huihui_ai/hy-mt1.5-abliterated:7b", "Hunyuan MT 1.5 7B Abliterated", "translation", 4.6f, "Модель перекладу Tencent без цензури — перекладе будь-який текст.", true, false, ""),
            M("huihui_ai/aya-expanse-abliterated:8b", "Aya Expanse 8B Abliterated", "translation", 5.1f, "Aya без відмов, 23 мови.", true, false, ""),
            M("huihui_ai/qwen2.5-abliterate:7b", "Qwen 2.5 7B Abliterated", "translation", 4.7f, "Мультимовна Qwen 2.5 без цензури.", true, false, ""),
            M("qwen3:4b", "Qwen 3 4B", "small", 2.5f, "Маленька, але дуже розумна. Летить навіть на слабкому ПК.", false, false, "думає,інструменти"),
            M("qwen3:1.7b", "Qwen 3 1.7B", "small", 1.4f, "Миттєві відповіді на прості питання.", false, false, "думає"),
            M("llama3.2:3b", "Llama 3.2 3B", "small", 2.0f, "Мала Llama — швидка і проста.", false, false, "інструменти"),
            M("phi4-mini:3.8b", "Phi-4 Mini 3.8B", "small", 2.5f, "Microsoft: мала модель з логікою.", false, false, "інструменти"),
            M("gemma3:1b", "Gemma 3 1B", "small", 0.8f, "Крихітна Gemma — працює навіть без відеокарти.", false, false, ""),
            M("qwen3.5:2b", "Qwen 3.5 2B", "small", 3.1f, "Мала Qwen 3.5 з розумінням картинок.", false, false, "зір"),
            M("huihui_ai/qwen3-abliterated:4b", "Qwen 3 4B Abliterated", "small", 2.5f, "Швидка мала модель без цензури.", true, false, "думає"),
            M("huihui_ai/qwen3.5-abliterated:4B", "Qwen 3.5 4B Abliterated", "small", 3.3f, "Мала Qwen 3.5 без цензури.", true, false, ""),
            M("huihui_ai/llama3.2-abliterate:3b", "Llama 3.2 3B Abliterated", "small", 2.0f, "Мала Llama без відмов.", true, false, ""),
            M("huihui_ai/qwen3-abliterated:1.7b", "Qwen 3 1.7B Abliterated", "small", 1.1f, "Найменша без цензури.", true, false, ""),
            M("dolphin-phi:2.7b", "Dolphin Phi 2.7B", "small", 1.6f, "Крихітна Dolphin.", true, false, ""),
        };

        public static CatalogModel Find(string id)
        {
            return All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }
}
