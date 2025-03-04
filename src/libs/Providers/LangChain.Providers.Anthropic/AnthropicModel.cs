using System.Diagnostics;

namespace LangChain.Providers.Anthropic;

/// <summary>
/// 
/// </summary>
public class AnthropicModel : IChatModelWithTokenCounting, IPaidLargeLanguageModel
{
    #region Properties

    /// <summary>
    /// 
    /// </summary>
    public string Id { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public string ApiKey { get; init; }

    /// <inheritdoc/>
    public Usage TotalUsage { get; private set; }

    /// <inheritdoc/>
    public int ContextLength => ApiHelpers.CalculateContextLength(Id);

    private HttpClient HttpClient { get; set; }
    private Tiktoken.Encoding Encoding { get; } = Tiktoken.Encoding.Get("cl100k_base");

    #endregion

    #region Constructors

    /// <summary>
    /// Wrapper around Anthropic large language models.
    /// </summary>
    /// <param name="configuration"></param>
    /// <param name="httpClient"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public AnthropicModel(AnthropicConfiguration configuration, HttpClient httpClient)
    {
        configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        ApiKey = configuration.ApiKey ?? throw new ArgumentException("ApiKey is not defined", nameof(configuration));
        Id = configuration.ModelId ?? throw new ArgumentException("ModelId is not defined", nameof(configuration));
        HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// Wrapper around Anthropic large language models.
    /// </summary>
    /// <param name="apiKey"></param>
    /// <param name="id"></param>
    /// <param name="httpClient"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public AnthropicModel(string apiKey, HttpClient httpClient, string id)
    {
        ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        Id = id ?? throw new ArgumentNullException(nameof(id));
    }

    #endregion

    #region Methods

    private static string ToRequestMessage(Message message)
    {
        return message.Role switch
        {
            MessageRole.System => message.Content.AsAssistantMessage(),
            MessageRole.Ai => message.Content.AsAssistantMessage(),
            MessageRole.Human => StringExtensions.AsHumanMessage(message.Content),
            MessageRole.FunctionCall => throw new NotImplementedException(),
            MessageRole.FunctionResult => throw new NotImplementedException(),
            _ => throw new NotImplementedException(),
        };
    }

    private static Message ToMessage(CreateCompletionResponse message)
    {
        return new Message(
            Content: message.Completion,
            Role: MessageRole.Ai);
    }

    private async Task<CreateCompletionResponse> CreateChatCompletionAsync(
        IReadOnlyCollection<Message> messages,
        CancellationToken cancellationToken = default)
    {
        var api = new AnthropicApi(apiKey: ApiKey, HttpClient);

        return await api.CompleteAsync(new CreateCompletionRequest
        {
            Prompt = messages
                .Select(ToRequestMessage)
                .ToArray().AsPrompt(),
            Max_tokens_to_sample = 100_000,
            Model = Id,
        }, cancellationToken).ConfigureAwait(false);
    }

    private Usage GetUsage(IReadOnlyCollection<Message> messages)
    {
        var completionTokens = CountTokens(messages.Last().Content);
        var promptTokens = CountTokens(messages
            .Take(messages.Count - 1)
            .Select(ToRequestMessage)
            .ToArray()
            .AsPrompt());
        var priceInUsd = CalculatePriceInUsd(
            outputTokens: completionTokens,
            inputTokens: promptTokens);

        return Usage.Empty with
        {
            InputTokens = promptTokens,
            OutputTokens = completionTokens,
            Messages = 1,
            PriceInUsd = priceInUsd,
        };
    }

    /// <inheritdoc/>
    public async Task<ChatResponse> GenerateAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var messages = request.Messages.ToList();
        var watch = Stopwatch.StartNew();
        var response = await CreateChatCompletionAsync(messages, cancellationToken).ConfigureAwait(false);

        messages.Add(ToMessage(response));

        var usage = GetUsage(messages) with
        {
            Time = watch.Elapsed,
        };
        TotalUsage += usage;

        return new ChatResponse(
            Messages: messages,
            Usage: usage);
    }

    /// <inheritdoc/>
    public int CountTokens(string text)
    {
        return Encoding.CountTokens(text);
    }

    /// <inheritdoc/>
    public int CountTokens(IReadOnlyCollection<Message> messages)
    {
        return CountTokens(string.Join(
            Environment.NewLine,
            messages.Select(static x => x.Content)));
    }

    /// <inheritdoc/>
    public int CountTokens(ChatRequest request)
    {
        return CountTokens(request.Messages);
    }

    /// <inheritdoc/>
    public double CalculatePriceInUsd(int inputTokens, int outputTokens)
    {
        return ApiHelpers.CalculatePriceInUsd(
            modelId: Id,
            completionTokens: outputTokens,
            promptTokens: inputTokens);
    }

    #endregion
}