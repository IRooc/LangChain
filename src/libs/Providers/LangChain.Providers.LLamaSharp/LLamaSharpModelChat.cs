﻿using System.Diagnostics;
using LLama;
using LLama.Common;

namespace LangChain.Providers.LLamaSharp;

/// <summary>
/// 
/// </summary>
[CLSCompliant(false)]
public class LLamaSharpModelChat : LLamaSharpModelBase
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="configuration"></param>
    public LLamaSharpModelChat(LLamaSharpConfiguration configuration) : base(configuration)
    {
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="res"></param>
    /// <returns></returns>
    private static string SanitizeOutput(string res)
    {
        return res
            .Replace("Human:", string.Empty)
            .Replace("Assistant:", string.Empty)
            .Trim();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="request"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override async Task<ChatResponse> GenerateAsync(ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var prompt = ToPrompt(request.Messages);

        var watch = Stopwatch.StartNew();


        var context = Model.CreateContext(Parameters);
        var ex = new InteractiveExecutor(context);
        ChatSession session = new ChatSession(ex);
        var inferenceParams = new InferenceParams()
        {
            Temperature = Configuration.Temperature,
            AntiPrompts = new List<string> { "Human:" },
            MaxTokens = Configuration.MaxTokens,

        };


        var buf = "";
        await foreach (var text in session.ChatAsync(prompt,
                           inferenceParams, cancellationToken))
        {
            buf += text;
        }

        buf = LLamaSharpModelChat.SanitizeOutput(buf);
        var result = request.Messages.ToList();
        result.Add(buf.AsAiMessage());

        watch.Stop();

        // Unsupported
        var usage = Usage.Empty with
        {
            Time = watch.Elapsed,
        };
        TotalUsage += usage;

        return new ChatResponse(
            Messages: result,
            Usage: usage);
    }
}