using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models;
using Anthropic.Models.Messages;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Ai;

public enum AiOutcome { Success, WrongKey, Billing, RateLimited, Unavailable, NoConnection, Refusal, Incomplete }
public sealed record AiResult(AiOutcome Outcome, string Text = "");

public sealed class AiTransport
{
    public const string Model = "claude-opus-5-5";
    private static readonly HttpClient SharedHttp = new();
    private readonly HttpClient http;
    public AiTransport(HttpClient? client = null) => http = client ?? SharedHttp;

    public async Task<AiResult> SendAsync(string key, string prompt, int maxTokens, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(prompt);
        var client = new AnthropicClient { ApiKey = key, HttpClient = http, MaxRetries = 0 };
        var request = new MessageCreateParams
        {
            Model = Model, MaxTokens = maxTokens,
            Messages = [new UserMessageParam(prompt)],
        };
        try
        {
            var response = await client.Messages.Create(request, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var reason = response.StopReason?.Value();
            if (reason == StopReason.Refusal) return new(AiOutcome.Refusal);
            if (reason != StopReason.EndTurn) return new(AiOutcome.Incomplete);
            string text = string.Join("\n", response.Content.Where(block => block.Value is TextBlock)
                .Select(block => ((TextBlock)block.Value!).Text));
            return string.IsNullOrWhiteSpace(text) ? new(AiOutcome.Incomplete) : new(AiOutcome.Success, text);
        }
        catch (AnthropicUnauthorizedException) { return new(AiOutcome.WrongKey); }
        catch (AnthropicRateLimitException) { return new(AiOutcome.RateLimited); }
        catch (Anthropic5xxException) { return new(AiOutcome.Unavailable); }
        catch (AnthropicApiException exception) when (exception.StatusCode == HttpStatusCode.PaymentRequired
            && exception.ErrorType == ErrorType.BillingError) { return new(AiOutcome.Billing); }
        catch (AnthropicApiException) { return new(AiOutcome.Unavailable); }
        catch (AnthropicIOException) { return new(AiOutcome.NoConnection); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(AiOutcome.NoConnection); }
    }
}
