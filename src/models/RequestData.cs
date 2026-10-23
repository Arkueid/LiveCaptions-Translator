namespace LiveCaptionsTranslator.models
{
    // Reasoning/thinking for all LLMs is disabled or minimal to reduce response time.

    public class BaseLLMRequestData(string model, List<BaseLLMConfig.Message> messages, double temperature)
    {
        // A translation of one caption line is short; the cap only exists to stop a model from
        // rambling after the sentence is done.
        public const int DEFAULT_MAX_TOKENS = 128;

        public string model { get; set; } = model;
        public List<BaseLLMConfig.Message> messages { get; set; } = messages;
        public double temperature { get; set; } = temperature;

        public int max_tokens { get; set; } = DEFAULT_MAX_TOKENS;
        public bool stream { get; set; } = false;
        public int keep_alive { get; set; } = 600;
    }

    public class IntegratedLLMRequestData(string model, List<BaseLLMConfig.Message> messages, double temperature)
        : BaseLLMRequestData(model, messages, temperature)
    {
        // Some platforms do not return 400/422 errors; instead, they automatically ignore incorrect parameters.
        // This request data is used for these platforms to ensure that model thinking is disabled.

        public class Reasoning
        {
            public bool exclude { get; set; } = true;
            public bool enabled { get; set; } = false;
            public string effort { get; set; } = "low";
        }
        public class Thinking
        {
            public string type { get; set; } = "disabled";
        }

        public bool think { get; set; } = false;
        public bool enable_thinking { get; set; } = false;
        public string reasoning_effort { get; set; } = "none";
        public Reasoning reasoning { get; set; } = new();
        public Thinking thinking { get; set; } = new();
    }

    public class OllamaRequestData(string model, List<BaseLLMConfig.Message> messages, double temperature)
        : BaseLLMRequestData(model, messages, temperature)
    {
        // Ollama ignores `temperature` and `max_tokens` at the top level: it only reads them from
        // `options` (as `num_predict`), so both settings used to have no effect at all — the model
        // ran at its default temperature and generated until it stopped by itself.
        public class OllamaOptions
        {
            public double temperature { get; set; }
            public int num_predict { get; set; } = BaseLLMRequestData.DEFAULT_MAX_TOKENS;
        }

        public bool think { get; set; } = false;
        public OllamaOptions options { get; set; } = new() { temperature = temperature };
    }

    public class OpenRouterRequestData(string model, List<BaseLLMConfig.Message> messages, double temperature)
        : BaseLLMRequestData(model, messages, temperature)
    {
        public class Reasoning
        {
            public bool exclude { get; set; } = true;
            public bool enabled { get; set; } = false;
        }
        public Reasoning reasoning { get; set; } = new();
    }

    public class AnthropicRequestData(string model, List<BaseLLMConfig.Message> messages, double temperature)
        : BaseLLMRequestData(model, messages, temperature)
    {
        // Supported Platform: Anthropic, Zhipu (BigModel)
        public class Thinking
        {
            public string type { get; set; } = "disabled";
        }
        public Thinking thinking { get; set; } = new();
    }

    public class AliyunRequestData(string model, List<BaseLLMConfig.Message> messages, double temperature)
        : BaseLLMRequestData(model, messages, temperature)
    {
        // Supported Platform: Aliyun (Bailian), Silicon Flow
        public bool enable_thinking { get; set; } = false;
    }

    public class OpenAIRequestData(string model, List<BaseLLMConfig.Message> messages, double temperature)
        : BaseLLMRequestData(model, messages, temperature)
    {
        // Supported Platform: OpenAI, Silicon Flow (For reasoning models)
        public class Reasoning
        {
            public string effort { get; set; } = "low";
        }
        public Reasoning reasoning { get; set; } = new();
    }

    public class XAIRequestData(string model, List<BaseLLMConfig.Message> messages, double temperature)
        : BaseLLMRequestData(model, messages, temperature)
    {
        // Supported Platform: xAI (Grok)
        public string reasoning_effort { get; set; } = "low";
    }
}