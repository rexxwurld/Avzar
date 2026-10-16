using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tailoring.Api.Security;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Common;
using Tailoring.Application.Billing;
using Tailoring.Domain.Access;
using Tailoring.Domain.Billing;
using Tailoring.Application.Media;

namespace Tailoring.Api.Controllers;

public sealed record FashionDesignRequest(string GarmentType, string Occasion, string Style, string Fabric, string Colours, string Details);
public sealed record FashionDesignResponse(string Prompt, string Model, string ImageDataUrl);
public sealed record SaveFashionDesignRequest(string ImageDataUrl, string FileName);
public sealed record AssistantBuilderRequest(string BusinessType, string Audience, string BrandStyle, string Goals, string ExistingContent);
public sealed record AssistantBuilderResponse(string SuggestedAssistantName, string SystemInstructions, string Greeting, string SuggestedPrompts);

[ApiController, Route("api/ai")]
public sealed class AiController(IConfiguration config, IHttpClientFactory clients, ITenantContext tenant, IEntitlementService entitlements, IMediaService media) : ControllerBase
{
    [HttpPost("fashion-design"), HasPermission(Permissions.DesignsManage), EnableRateLimiting("auth")]
    public async Task<FashionDesignResponse> FashionDesign(FashionDesignRequest request, CancellationToken ct)
    {
        var businessId = tenant.BusinessId ?? throw AppException.Unauthorized("business_required", "Select a business first.");
        await entitlements.RequireFlagAsync(businessId, EntitlementKeys.AiFashionDesign, ct);
        Validate(request.GarmentType, request.Occasion, request.Style, request.Fabric, request.Colours, request.Details);
        var key = config["AI:OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(key)) throw new AppException(503, "ai_not_configured", "AI generation is not configured on this deployment.");
        var prompt = $"Create a high-quality fashion concept image for a garment designer. Garment: {request.GarmentType}. Occasion: {request.Occasion}. Style: {request.Style}. Fabric: {request.Fabric}. Colours: {request.Colours}. Additional details: {request.Details}. Show the full garment clearly on a professional fashion illustration/model, neutral studio background, no text, no logos, realistic fabric texture.";
        using var msg = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/images/generations");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        msg.Content = JsonContent.Create(new { model = config["AI:OpenAI:ImageModel"] ?? "gpt-image-1", prompt, size = "1024x1024", n = 1, output_format = "png" });
        using var response = await clients.CreateClient("ai").SendAsync(msg, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new AppException(502, "ai_provider_error", "The image provider could not complete this request. Try again later.");
        using var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data")[0];
        if (!data.TryGetProperty("b64_json", out var image)) throw new AppException(502, "ai_provider_response_invalid", "The image provider returned an unsupported response.");
        return new(prompt, config["AI:OpenAI:ImageModel"] ?? "gpt-image-1", "data:image/png;base64," + image.GetString());
    }

    [HttpPost("fashion-design/save"), HasPermission(Permissions.MediaManage), EnableRateLimiting("upload")]
    [RequestSizeLimit(18 * 1024 * 1024)]
    public async Task<MediaAssetDto> SaveFashionDesign(SaveFashionDesignRequest request, CancellationToken ct)
    {
        var businessId = tenant.BusinessId ?? throw AppException.Unauthorized("business_required", "Select a business first.");
        await entitlements.RequireFlagAsync(businessId, EntitlementKeys.AiFashionDesign, ct);
        if (string.IsNullOrWhiteSpace(request.FileName) || request.FileName.Length > 120 || !request.ImageDataUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal))
            throw AppException.BadRequest("invalid_generated_image", "Provide a generated PNG image and a valid file name.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(request.ImageDataUrl["data:image/png;base64,".Length..]); }
        catch (FormatException) { throw AppException.BadRequest("invalid_generated_image", "Generated image data is invalid."); }
        if (bytes.Length == 0 || bytes.Length > 12 * 1024 * 1024) throw AppException.BadRequest("invalid_generated_image", "Generated image must be under 12 MB.");
        await using var stream = new MemoryStream(bytes, writable: false);
        return await media.UploadAsync(stream, request.FileName, "DesignImage", ct);
    }

    [HttpPost("assistant-builder"), HasPermission(Permissions.StorefrontEdit), EnableRateLimiting("auth")]
    public async Task<AssistantBuilderResponse> AssistantBuilder(AssistantBuilderRequest request, CancellationToken ct)
    {
        var businessId = tenant.BusinessId ?? throw AppException.Unauthorized("business_required", "Select a business first.");
        await entitlements.RequireFlagAsync(businessId, EntitlementKeys.AiAssistantBuilder, ct);
        Validate(request.BusinessType, request.Audience, request.BrandStyle, request.Goals, request.ExistingContent);
        var key = config["AI:OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(key)) throw new AppException(503, "ai_not_configured", "AI generation is not configured on this deployment.");
        var prompt = "You are helping a fashion business configure a customer-facing storefront assistant. Return ONLY a JSON object with keys suggestedAssistantName, systemInstructions, greeting, suggestedPrompts. Never invent prices, delivery guarantees, stock, or policies; instruct the assistant to ask the business when facts are missing. Keep systemInstructions under 3000 characters and suggestedPrompts under 1000 characters.\n" + JsonSerializer.Serialize(request);
        using var msg = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        msg.Content = JsonContent.Create(new { model = config["AI:OpenAI:TextModel"] ?? "gpt-4.1-mini", temperature = 0.4, messages = new[] { new { role = "system", content = "Follow the requested schema exactly. Do not include markdown fences." }, new { role = "user", content = prompt } } });
        using var response = await clients.CreateClient("ai").SendAsync(msg, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw new AppException(502, "ai_provider_error", "The AI provider could not complete this request. Try again later.");
        using var json = JsonDocument.Parse(body);
        var content = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        try { return JsonSerializer.Deserialize<AssistantBuilderResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new JsonException(); }
        catch (JsonException) { throw new AppException(502, "ai_provider_response_invalid", "The AI provider returned an unsupported assistant configuration."); }
    }

    private static void Validate(params string[] values)
    {
        if (values.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 2000)) throw AppException.BadRequest("invalid_ai_request", "All fields are required and must be no longer than 2,000 characters.");
    }
}
