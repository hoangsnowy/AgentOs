// ILlmClient backed by Azure OpenAI SDK through Microsoft.Extensions.AI IChatClient (the substrate
// Microsoft Agent Framework builds on). Selected via provider key "MAF" or Llm:ForceProvider=MAF.

using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using AgentOs.Domain.Llm;
using AgentOs.SharedKernel.Identity;
using AgentOs.SharedKernel.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentOs.Modules.Llm;

/// <summary>Azure OpenAI client via the official SDK + Microsoft.Extensions.AI <see cref="IChatClient"/>.</summary>
public sealed class MafChatClient : ILlmClient
{
    private readonly AzureOpenAiOptions _options;
    private readonly ILogger<MafChatClient> _logger;

    /// <inheritdoc />
    public string Provider => "MAF";

    public MafChatClient(IOptions<LlmOptions> options, ILogger<MafChatClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value?.AzureOpenAi ?? new AzureOpenAiOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<LlmResponse> SendAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            throw new LlmException(
                "Azure OpenAI not configured for the MAF client. Set 'Llm:AzureOpenAi:ApiKey' and "
                + "'Llm:AzureOpenAi:Endpoint' (user-secrets or env).",
                Provider);
        }

        var deployment = string.IsNullOrWhiteSpace(_options.Model) ? request.Model : _options.Model;
        var azure = new AzureOpenAIClient(new Uri(_options.Endpoint), new ApiKeyCredential(_options.ApiKey));
        IChatClient chat = azure.GetChatClient(deployment).AsIChatClient();

        var messages = new List<ChatMessage>(2);
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new ChatMessage(ChatRole.System, request.SystemPrompt));
        }
        messages.Add(new ChatMessage(ChatRole.User, request.UserPrompt));

        var options = new ChatOptions
        {
            ModelId = deployment,
            Temperature = (float)request.Temperature,
            MaxOutputTokens = request.MaxTokens,
        };

        var tenantId = AmbientIdentity.Current?.TenantId;
        var genAiSystem = LlmTelemetry.SystemFor(Provider);
        using var activity = LlmTelemetry.StartChat(genAiSystem, deployment, tenantId);
        var stopwatch = Stopwatch.StartNew();
        ChatResponse response;
        try
        {
            response = await chat.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LlmTelemetry.RecordError(activity, ex.Message);
            throw;
        }
        stopwatch.Stop();

        var inputTokens = (int)(response.Usage?.InputTokenCount ?? 0);
        var outputTokens = (int)(response.Usage?.OutputTokenCount ?? 0);
        var cost = CostCalculator.Calculate(request.Model, inputTokens, outputTokens);
        LlmTelemetry.RecordSuccess(activity, genAiSystem, deployment, response.ModelId ?? deployment,
            inputTokens, outputTokens, cost, stopwatch.Elapsed.TotalSeconds);
        _logger.LogInformation(
            "LlmCallCompleted {Provider} {Model} {InTok} {OutTok} {CostUsd} {Ms} {Tenant}",
            Provider, deployment, inputTokens, outputTokens, cost, stopwatch.Elapsed.TotalMilliseconds, tenantId ?? "");

        return new LlmResponse(
            Content: response.Text ?? string.Empty,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            CostUsd: cost,
            Latency: stopwatch.Elapsed,
            Model: deployment,
            Provider: Provider);
    }
}
